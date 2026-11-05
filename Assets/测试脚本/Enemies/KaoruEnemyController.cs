using UnityEngine;

/// <summary>Ground-plane melee enemy. Plain C# states own its behaviour.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider), typeof(KaoruHealth))]
[AddComponentMenu("Kaoru/Enemy Controller")]
public sealed class KaoruEnemyController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("玩家根物体；不指定时自动寻找 KaoruTopDownController。")]
    [SerializeField] private Transform target;
    [Tooltip("小怪自身或子物体上的 Animator。留空时自动查找。")]
    [SerializeField] private Animator characterAnimator;
    [SerializeField] private bool autoFindPlayer = true;

    [Header("Detection and movement")]
    [Min(0.1f)] [SerializeField] private float detectionRadius = 8f;
    [Tooltip("追逐后超过此距离才放弃，避免在警戒边缘反复切换。")]
    [Min(0.1f)] [SerializeField] private float loseTargetRadius = 12f;
    [Tooltip("允许的脚底高度差，防止攻击楼上、楼下的目标。")]
    [Min(0f)] [SerializeField] private float maxHeightDifference = 2f;
    [Min(0f)] [SerializeField] private float runSpeed = 3f;
    [Min(1f)] [SerializeField] private float turnSpeed = 540f;

    [Header("Attack")]
    [Tooltip("双方根物体在 XZ 平面上的距离，不是碰撞体表面间距。")]
    [Min(0.1f)] [SerializeField] private float attackRange = 1.5f;
    [Tooltip("攻击结束后，超过此距离才重新追逐，避免近身来回起跑。")]
    [Min(0.1f)] [SerializeField] private float attackExitRange = 1.8f;
    [Tooltip("挥击命中帧允许的距离，玩家移出范围即可躲开。")]
    [Min(0.1f)] [SerializeField] private float attackHitRange = 1.8f;
    [Tooltip("正前方攻击扇形的总角度。")]
    [Range(1f, 360f)] [SerializeField] private float attackArc = 120f;
    [Tooltip("攻击动画的命中时刻，0 是开头，1 是结尾。")]
    [Range(0f, 1f)] [SerializeField] private float attackHitTime = 0.4f;
    [Tooltip("本次攻击结束或被打断后，到下次攻击的等待秒数。")]
    [Min(0f)] [SerializeField] private float attackCooldown = 0.8f;
    [Min(1)] [SerializeField] private int attackDamage = 1;

    [Header("Dodge")]
    [Tooltip("每次非致命命中后触发闪避的概率；每只小怪一生最多成功触发一次，仍正常扣血。")]
    [Range(0f, 1f)] [SerializeField] private float dodgeChance = 0.3f;
    [Tooltip("闪避时向身体左侧移动的速度，使用 Rigidbody 保留与墙体的碰撞。")]
    [Min(0f)] [SerializeField] private float dodgeSpeed = 4f;
    [Tooltip("闪避动画播放到此进度后停止位移，剩余部分作为动作收尾。")]
    [Range(0f, 1f)] [SerializeField] private float dodgeMoveEndTime = 0.85f;

    [Header("Animator state names (Base Layer)")]
    [Tooltip("填写状态名或完整路径，例如 Idle 或 Base Layer.Idle。无需参数和过渡连线。")]
    [SerializeField] private string idleState = "Idle";
    [SerializeField] private string runState = "Run";
    [SerializeField] private string attackState = "Attack";
    [SerializeField] private string hitState = "Hit";
    [SerializeField] private string deathState = "Death";
    [SerializeField] private string dodgeState = "小怪闪避";
    [Min(0f)] [SerializeField] private float animationBlendTime = 0.08f;

    [Header("Death")]
    [SerializeField] private bool despawnAfterDeath = true;
    [Tooltip("死亡动画完整播放后，尸体额外保留的秒数。")]
    [Min(0f)] [SerializeField] private float corpseLifetime = 4f;

    private Rigidbody body;
    private KaoruHealth health;
    private KaoruHealth targetHealth;
    private Transform cachedTarget;
    private KaoruEnemyStateMachine stateMachine;
    private KaoruEnemyStateBase[] states;
    private readonly int[] animationHashes = new int[6];
    private int requestedAnimationHash;
    private int animationRequestFrame;
    private float nextAttackTime;
    private float nextTargetSearchTime;
    private bool ready;
    private bool warnedMissingTargetHealth;
    private bool hasDodged;

    public KaoruEnemyState CurrentState => stateMachine != null && stateMachine.Current != null
        ? stateMachine.Current.Id : KaoruEnemyState.Idle;
    public Transform Target => target;
    public KaoruHealth Health => health;
    public bool IsDead => health != null && health.IsDead;
    public bool HasDodged => hasDodged;

    internal float DetectionRadius => detectionRadius;
    internal float LoseTargetRadius => loseTargetRadius;
    internal float AttackRange => attackRange;
    internal float AttackExitRange => attackExitRange;
    internal float AttackHitTime => attackHitTime;
    internal float DodgeMoveEndTime => dodgeMoveEndTime;
    internal float CorpseLifetime => corpseLifetime;
    internal bool DespawnAfterDeath => despawnAfterDeath;
    internal bool CanAttack => Time.time >= nextAttackTime;
    internal bool IsReturningFromMelee => stateMachine.Previous != null &&
        (stateMachine.Previous.Id == KaoruEnemyState.Attack || stateMachine.Previous.Id == KaoruEnemyState.Hit ||
         stateMachine.Previous.Id == KaoruEnemyState.Dodge);

    private bool HasTarget => target != null && target != transform &&
        target.gameObject.activeInHierarchy && (targetHealth == null || !targetHealth.IsDead);

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        health = GetComponent<KaoruHealth>();
        stateMachine = new KaoruEnemyStateMachine();
        states = new KaoruEnemyStateBase[]
        {
            new KaoruEnemyIdleState(this), new KaoruEnemyChaseState(this),
            new KaoruEnemyAttackState(this), new KaoruEnemyHitState(this),
            new KaoruEnemyDeathState(this), new KaoruEnemyDodgeState(this)
        };
    }

    private void OnEnable()
    {
        health.Damaged += OnDamaged;
        health.Died += OnDied;
        if (ready)
            ChangeState(IsDead ? KaoruEnemyState.Death : KaoruEnemyState.Idle);
    }

    private void Start()
    {
        if (characterAnimator == null)
            characterAnimator = GetComponentInChildren<Animator>();
        if (!ValidateAnimator())
        {
            enabled = false;
            return;
        }
        if (body.isKinematic)
        {
            Debug.LogError("小怪需要动态 Rigidbody，请关闭 Is Kinematic。", this);
            enabled = false;
            return;
        }
        body.constraints |= RigidbodyConstraints.FreezeRotation;
        characterAnimator.applyRootMotion = false;
        characterAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        characterAnimator.updateMode = AnimatorUpdateMode.Normal;
        characterAnimator.speed = 1f;
        RefreshTarget();
        ready = true;
        ChangeState(IsDead ? KaoruEnemyState.Death : KaoruEnemyState.Idle);
    }

    private void Update()
    {
        if (!ready || Time.timeScale <= 0f)
            return;
        if (!IsDead)
            RefreshTarget();
        stateMachine.Tick();
    }

    private void FixedUpdate()
    {
        if (ready)
            stateMachine.FixedTick();
    }

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
        cachedTarget = newTarget;
        targetHealth = target != null ? target.GetComponentInParent<KaoruHealth>() : null;
        warnedMissingTargetHealth = false;
    }

    private void RefreshTarget()
    {
        if (target != cachedTarget)
            SetTarget(target);
        if (HasTarget || !autoFindPlayer || Time.time < nextTargetSearchTime)
            return;
        nextTargetSearchTime = Time.time + 1f;
        KaoruTopDownController player = FindFirstObjectByType<KaoruTopDownController>();
        if (player != null)
            SetTarget(player.transform);
    }

    internal void ChangeState(KaoruEnemyState next, bool restart = false)
    {
        if (!ready || (IsDead && next != KaoruEnemyState.Death))
            return;
        stateMachine.ChangeState(states[(int)next], restart);
    }

    internal void ResumeCombat()
    {
        if (!TargetIsWithin(loseTargetRadius))
            ChangeState(KaoruEnemyState.Idle);
        else if (TargetIsWithin(attackExitRange))
            ChangeState(KaoruEnemyState.Idle);
        else
            ChangeState(KaoruEnemyState.Chase);
    }

    internal bool TargetIsWithin(float radius)
    {
        if (!HasTarget)
            return false;
        Vector3 offset = target.position - body.position;
        return Mathf.Abs(offset.y) <= maxHeightDifference &&
            offset.x * offset.x + offset.z * offset.z <= radius * radius;
    }

    internal void MoveTowardTarget()
    {
        if (!HasTarget)
        {
            StopMoving();
            return;
        }
        Vector3 offset = Vector3.ProjectOnPlane(target.position - body.position, Vector3.up);
        float distance = offset.magnitude;
        float speed = Mathf.Min(runSpeed, Mathf.Max(0f, distance - attackRange * 0.9f) / Time.fixedDeltaTime);
        Vector3 velocity = distance > 0.0001f ? offset / distance * speed : Vector3.zero;
        body.linearVelocity = velocity + Vector3.up * body.linearVelocity.y;
        FaceTarget();
    }

    internal void StopMoving()
    {
        if (body != null && !body.isKinematic)
            body.linearVelocity = Vector3.up * body.linearVelocity.y;
    }

    internal Vector3 GetDodgeDirection()
    {
        return Vector3.ProjectOnPlane(body.rotation * Vector3.left, Vector3.up).normalized;
    }

    internal void MoveForDodge(Vector3 direction)
    {
        if (!body.isKinematic)
            body.linearVelocity = direction * dodgeSpeed + Vector3.up * body.linearVelocity.y;
    }

    internal void FaceTarget()
    {
        if (!HasTarget || body.isKinematic)
            return;
        Vector3 offset = Vector3.ProjectOnPlane(target.position - body.position, Vector3.up);
        if (offset.sqrMagnitude > 0.0001f)
            body.MoveRotation(Quaternion.RotateTowards(body.rotation,
                Quaternion.LookRotation(offset), turnSpeed * Time.fixedDeltaTime));
    }

    internal void StartAttackCooldown()
    {
        nextAttackTime = Time.time + attackCooldown;
    }

    internal void TryHitTarget()
    {
        if (CurrentState != KaoruEnemyState.Attack || IsDead || !TargetIsWithin(attackHitRange))
            return;
        Vector3 offset = Vector3.ProjectOnPlane(target.position - body.position, Vector3.up);
        if (offset.sqrMagnitude > 0.0001f &&
            Vector3.Angle(body.rotation * Vector3.forward, offset) > attackArc * 0.5f)
            return;
        if (targetHealth != null)
            targetHealth.TakeDamage(attackDamage, gameObject);
        else if (!warnedMissingTargetHealth)
        {
            Debug.LogWarning("小怪的攻击目标没有 KaoruHealth；可以播放攻击，但不会扣血。", target);
            warnedMissingTargetHealth = true;
        }
    }

    private void OnDamaged(KaoruHealth damaged, GameObject source)
    {
        if (!ready || IsDead)
            return;
        if (source != null && source != gameObject)
            SetTarget(source.transform);
        // Damage was already applied by KaoruHealth. Further nonlethal hits
        // neither restart the dodge nor grant another roll of the probability.
        if (CurrentState == KaoruEnemyState.Dodge)
            return;
        if (!hasDodged && dodgeChance > 0f &&
            (dodgeChance >= 1f || Random.value < dodgeChance))
        {
            hasDodged = true;
            ChangeState(KaoruEnemyState.Dodge);
            return;
        }
        ChangeState(KaoruEnemyState.Hit, true);
    }

    private void OnDied(KaoruHealth damaged, GameObject source)
    {
        ChangeState(KaoruEnemyState.Death);
    }

    internal void FreezeForDeath()
    {
        if (!body.isKinematic)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.isKinematic = true;
        }
        // Disable contacts without making a gravity-driven corpse fall through the floor.
        body.detectCollisions = false;
    }

    private bool ValidateAnimator()
    {
        if (characterAnimator == null || !characterAnimator.isActiveAndEnabled ||
            characterAnimator.runtimeAnimatorController == null || characterAnimator.layerCount == 0)
        {
            Debug.LogError("请给小怪指定已启用的 Animator 和包含六个动画状态的 Controller。", this);
            return false;
        }
        string layer = characterAnimator.GetLayerName(0) + ".";
        string[] names = { idleState, runState, attackState, hitState, deathState, dodgeState };
        for (int i = 0; i < names.Length; i++)
        {
            if (i == (int)KaoruEnemyState.Dodge && dodgeChance <= 0f)
                continue;
            string name = names[i] != null ? names[i].Trim() : string.Empty;
            string path = name.StartsWith(layer, System.StringComparison.Ordinal) ? name : layer + name;
            animationHashes[i] = Animator.StringToHash(path);
            if (name.Length == 0 || !characterAnimator.HasState(0, animationHashes[i]))
            {
                Debug.LogError("小怪 Animator 缺少状态：" + path + "。请检查 Enemy Controller 中的状态名。", this);
                return false;
            }
        }
        return true;
    }

    internal void PlayAnimation(KaoruEnemyState state)
    {
        int hash = animationHashes[(int)state];
        bool firstAnimation = requestedAnimationHash == 0;
        bool restarting = requestedAnimationHash == hash;
        requestedAnimationHash = hash;
        animationRequestFrame = Time.frameCount;
        // Repeated hits need a fresh timeline, rather than reading the old Hit
        // state while it blends into another copy of itself.
        if (firstAnimation || restarting || animationBlendTime <= 0f)
            characterAnimator.Play(hash, 0, 0f);
        else
            characterAnimator.CrossFadeInFixedTime(hash, animationBlendTime, 0, 0f);
    }

    internal bool AnimationReached(float normalizedTime)
    {
        // Let the Animator evaluate a newly requested state before inspecting it.
        if (Time.frameCount <= animationRequestFrame || !characterAnimator.isInitialized)
            return false;
        if (characterAnimator.IsInTransition(0))
        {
            AnimatorStateInfo next = characterAnimator.GetNextAnimatorStateInfo(0);
            if (next.fullPathHash == requestedAnimationHash)
                return next.normalizedTime >= normalizedTime;
        }
        AnimatorStateInfo current = characterAnimator.GetCurrentAnimatorStateInfo(0);
        return current.fullPathHash == requestedAnimationHash && current.normalizedTime >= normalizedTime;
    }

    private void OnDisable()
    {
        if (health != null)
        {
            health.Damaged -= OnDamaged;
            health.Died -= OnDied;
        }
        stateMachine?.Clear();
        StopMoving();
    }

    private void OnValidate()
    {
        attackRange = Mathf.Max(0.1f, attackRange);
        attackExitRange = Mathf.Max(attackRange, attackExitRange);
        attackHitRange = Mathf.Max(attackRange, attackHitRange);
        detectionRadius = Mathf.Max(attackExitRange, detectionRadius);
        loseTargetRadius = Mathf.Max(detectionRadius, loseTargetRadius);
        dodgeChance = Mathf.Clamp01(dodgeChance);
        dodgeSpeed = Mathf.Max(0f, dodgeSpeed);
        dodgeMoveEndTime = Mathf.Clamp01(dodgeMoveEndTime);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRadius);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);
        Gizmos.color = Color.gray;
        Gizmos.DrawWireSphere(transform.position, loseTargetRadius);
    }
}
