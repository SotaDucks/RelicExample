using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>Camera-relative movement; animation transitions belong to the Animator graph.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider), typeof(Animator))]
[AddComponentMenu("Kaoru/Top Down Controller")]
public sealed class KaoruTopDownController : MonoBehaviour
{
    public const string MovingParameterName = "IsMoving";
    public const string TurnParameterName = "Turn";
    public const string AttackParameterName = "Attack";
    public const string AttackHeldParameterName = "AttackHeld";
    public const string AttackSpeedParameterName = "AttackSpeed";
    public const string AimingParameterName = "Aiming";
    public const string WalkBackParameterName = "WalkBack";
    public const string UpperBodyLayerName = "Upper Body";
    public const string AttackTag = "Attack";

    private static readonly int MovingParameter = Animator.StringToHash(MovingParameterName);
    private static readonly int TurnParameter = Animator.StringToHash(TurnParameterName);
    private static readonly int AttackParameter = Animator.StringToHash(AttackParameterName);
    private static readonly int AttackHeldParameter = Animator.StringToHash(AttackHeldParameterName);
    private static readonly int AttackSpeedParameter = Animator.StringToHash(AttackSpeedParameterName);
    private static readonly int AimingParameter = Animator.StringToHash(AimingParameterName);
    private static readonly int WalkBackParameter = Animator.StringToHash(WalkBackParameterName);
    private const float DirectionEpsilon = 0.0001f;
    // Hysteresis around a sideways strafe so the legs do not flicker between clips.
    private const float WalkBackEnterDot = -0.2f;
    private const float WalkBackExitDot = 0.2f;

    [Header("References")]
    [Tooltip("World-rendering camera. For PixelCamera use PixelCamera rather than ViewCamera.")]
    [SerializeField] private Camera gameplayCamera;
    [SerializeField] private RuntimeAnimatorController animationController;

    [Header("Running")]
    [Min(0f)] [SerializeField] private float runSpeed = 4f;
    [Min(1f)] [SerializeField] private float turnSpeed = 540f;
    [Min(1f)] [SerializeField] private float acceleration = 28f;
    [Min(1f)] [SerializeField] private float deceleration = 40f;
    [Tooltip("Heading error at which RunA_L / RunA_R receives full weight.")]
    [Range(5f, 180f)] [SerializeField] private float fullTurnAnimationAngle = 45f;
    [Min(0f)] [SerializeField] private float turnAnimationDamping = 0.1f;

    [Header("Attack")]
    [Tooltip("Multiplies AttackA1 and AttackA2. Animator state base speed remains 1.")]
    [Range(0.1f, 3f)] [SerializeField] private float attackSpeed = 1f;
    [SerializeField] private bool faceMouseWhenAttacking = true;
    [Tooltip("Horizontal speed while the upper body attacks and the legs play Walk / Walk_back.")]
    [Min(0f)] [SerializeField] private float attackMoveSpeed = 2f;
    [Tooltip("Body turn rate while tracking the mouse during an attack.")]
    [Min(1f)] [SerializeField] private float aimTurnSpeed = 1440f;
    [Min(0f)] [SerializeField] private float walkBackDamping = 0.08f;

    private Rigidbody body;
    private Animator characterAnimator;
    private int upperBodyLayer = -1;
    private Vector3 movementDirection;
    private Vector3 facingDirection;
    private bool attackRequested;
    private bool walkingBack;
    private bool hasAimPoint;
    private Vector3 aimPoint;
    private bool ready;
    private float lastTurnSign = 1f;
    private float targetTurn;

    // Covers the request frame through the upper body's complete outgoing blend.
    public bool IsAttacking => ready && (attackRequested || AnimatorIsAttacking());
    public Vector3 MovementDirection => movementDirection;
    /// <summary>Horizontal mouse aim while attacking; projectiles should launch along it.</summary>
    public Vector3 AimDirection => facingDirection;
    /// <summary>True when AimPoint holds the cursor's ground point for the current attack.</summary>
    public bool HasAimPoint => hasAimPoint && IsAttacking;
    /// <summary>Cursor position on the horizontal plane through the character's feet.</summary>
    public Vector3 AimPoint => aimPoint;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        characterAnimator = GetComponent<Animator>();
        if (animationController != null)
            characterAnimator.runtimeAnimatorController = animationController;
        upperBodyLayer = characterAnimator.runtimeAnimatorController != null
            ? characterAnimator.GetLayerIndex(UpperBodyLayerName) : -1;
        if (upperBodyLayer < 0 ||
            !HasParameter(MovingParameter, AnimatorControllerParameterType.Bool) ||
            !HasParameter(TurnParameter, AnimatorControllerParameterType.Float) ||
            !HasParameter(AttackParameter, AnimatorControllerParameterType.Trigger) ||
            !HasParameter(AttackHeldParameter, AnimatorControllerParameterType.Bool) ||
            !HasParameter(AttackSpeedParameter, AnimatorControllerParameterType.Float) ||
            !HasParameter(AimingParameter, AnimatorControllerParameterType.Bool) ||
            !HasParameter(WalkBackParameter, AnimatorControllerParameterType.Float))
        {
            Debug.LogError("Kaoru Animator 未配置。请选择角色并执行 Tools > Kaoru > Configure Selected Character。", this);
            enabled = false;
            return;
        }
        characterAnimator.applyRootMotion = false;
        characterAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        characterAnimator.updateMode = AnimatorUpdateMode.Normal;
        characterAnimator.speed = 1f;
        ResolveCamera();
        ready = true;
    }

    private void OnEnable()
    {
        if (!ready)
            return;
        movementDirection = Vector3.zero;
        facingDirection = body.rotation * Vector3.forward;
        targetTurn = 0f;
        attackRequested = false;
        walkingBack = false;
        // Activation resets to the graph's default. No gameplay Play/CrossFade.
        characterAnimator.Rebind();
        characterAnimator.SetBool(MovingParameter, false);
        characterAnimator.SetFloat(TurnParameter, 0f);
        characterAnimator.SetFloat(AttackSpeedParameter, attackSpeed);
        characterAnimator.SetBool(AimingParameter, false);
        characterAnimator.SetBool(AttackHeldParameter, false);
        characterAnimator.SetFloat(WalkBackParameter, 0f);
        characterAnimator.ResetTrigger(AttackParameter);
    }

    private void Update()
    {
        if (Time.timeScale <= 0f)
            return;
        if (attackRequested && AnimatorIsAttacking())
            attackRequested = false;
        movementDirection = GetWorldDirection(ReadMovement());
        // Intent stays current while attacking: the legs keep walking and the
        // exit conditions choose the right locomotion state afterwards.
        bool moving = movementDirection.sqrMagnitude > DirectionEpsilon && runSpeed > 0f;
        characterAnimator.SetBool(MovingParameter, moving);
        characterAnimator.SetFloat(AttackSpeedParameter, Mathf.Clamp(attackSpeed, 0.1f, 3f));
        Mouse mouse = Mouse.current;
        bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        // Holding the button chains AttackA2 straight back into AttackA1 in
        // the graph; a press after the combo has ended starts a new one.
        bool attackHeld = mouse != null && !overUI &&
            (mouse.leftButton.isPressed || mouse.leftButton.wasPressedThisFrame);
        characterAnimator.SetBool(AttackHeldParameter, attackHeld);
        if (attackHeld && !IsAttacking)
            RequestAttack();
        bool attacking = IsAttacking;
        // Keep tracking the cursor for the whole combo, like a twin-stick aim.
        if (attacking && faceMouseWhenAttacking && mouse != null)
            AimAt(mouse.position.ReadValue());
        if (!attacking && moving)
            facingDirection = movementDirection.normalized;
        if (attacking && moving)
        {
            float dot = Vector3.Dot(movementDirection.normalized, facingDirection);
            if (dot < WalkBackEnterDot)
                walkingBack = true;
            else if (dot > WalkBackExitDot)
                walkingBack = false;
        }
        characterAnimator.SetBool(AimingParameter, attacking);
        characterAnimator.SetFloat(WalkBackParameter, walkingBack ? 1f : 0f, walkBackDamping, Time.deltaTime);
        characterAnimator.SetFloat(TurnParameter, attacking || !moving ? 0f : targetTurn,
            turnAnimationDamping, Time.deltaTime);
    }

    private void FixedUpdate()
    {
        if (!ready)
            return;
        bool attacking = IsAttacking;
        Vector3 planarVelocity = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up);
        Vector3 desiredVelocity = movementDirection * (attacking ? attackMoveSpeed : runSpeed);
        planarVelocity = Vector3.MoveTowards(planarVelocity, desiredVelocity,
            (desiredVelocity.sqrMagnitude > DirectionEpsilon ? acceleration : deceleration) * Time.fixedDeltaTime);
        body.linearVelocity = planarVelocity + Vector3.up * body.linearVelocity.y;
        if (facingDirection.sqrMagnitude <= DirectionEpsilon)
            return;
        float angle = Vector3.SignedAngle(body.rotation * Vector3.forward, facingDirection, Vector3.up);
        float magnitude = Mathf.Abs(angle);
        if (magnitude > 2f && magnitude < 175f)
            lastTurnSign = Mathf.Sign(angle);
        // Keep the same side on an ambiguous 180-degree reversal.
        if (magnitude >= 175f)
            angle = magnitude * lastTurnSign;
        targetTurn = attacking ? 0f : Mathf.Sign(angle) * Mathf.InverseLerp(2f, fullTurnAnimationAngle, magnitude);
        float maxStep = (attacking ? aimTurnSpeed : turnSpeed) * Time.fixedDeltaTime;
        body.MoveRotation(body.rotation * Quaternion.AngleAxis(Mathf.Clamp(angle, -maxStep, maxStep), Vector3.up));
    }

    private void RequestAttack()
    {
        if (!characterAnimator.isActiveAndEnabled)
            return;
        Mouse mouse = Mouse.current;
        hasAimPoint = false;
        if (faceMouseWhenAttacking && mouse != null)
            AimAt(mouse.position.ReadValue());
        // Pick the leg clip from the click frame instead of a stale damped value.
        walkingBack = movementDirection.sqrMagnitude > DirectionEpsilon &&
            Vector3.Dot(movementDirection.normalized, facingDirection) < 0f;
        characterAnimator.SetFloat(WalkBackParameter, walkingBack ? 1f : 0f);
        attackRequested = true;
        targetTurn = 0f;
        characterAnimator.SetFloat(TurnParameter, 0f);
        characterAnimator.SetBool(AimingParameter, true);
        characterAnimator.SetTrigger(AttackParameter);
    }

    private void AimAt(Vector2 screenPosition)
    {
        ResolveCamera();
        if (gameplayCamera == null || Screen.width <= 0 || Screen.height <= 0)
            return;
        // Normalized screen coordinates also work with PixelCamera's
        // lower-resolution RenderTexture.
        Vector3 viewport = new Vector3(screenPosition.x / Screen.width, screenPosition.y / Screen.height);
        Ray ray = gameplayCamera.ViewportPointToRay(viewport);
        Plane ground = new Plane(Vector3.up, body.position);
        if (!ground.Raycast(ray, out float distance))
            return;
        aimPoint = ray.GetPoint(distance);
        hasAimPoint = true;
        Vector3 direction = Vector3.ProjectOnPlane(aimPoint - body.position, Vector3.up);
        if (direction.sqrMagnitude > DirectionEpsilon)
            facingDirection = direction.normalized;
    }

    private bool AnimatorIsAttacking()
    {
        if (!characterAnimator.isInitialized || upperBodyLayer < 0)
            return false;
        return characterAnimator.GetCurrentAnimatorStateInfo(upperBodyLayer).IsTag(AttackTag) ||
            (characterAnimator.IsInTransition(upperBodyLayer) &&
             characterAnimator.GetNextAnimatorStateInfo(upperBodyLayer).IsTag(AttackTag));
    }

    private bool HasParameter(int hash, AnimatorControllerParameterType type)
    {
        if (characterAnimator.runtimeAnimatorController == null)
            return false;
        foreach (AnimatorControllerParameter parameter in characterAnimator.parameters)
            if (parameter.nameHash == hash && parameter.type == type)
                return true;
        return false;
    }

    private static Vector2 ReadMovement()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
            return Vector2.zero;
        return Vector2.ClampMagnitude(new Vector2(
            (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f),
            (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f)), 1f);
    }

    private Vector3 GetWorldDirection(Vector2 input)
    {
        ResolveCamera();
        if (gameplayCamera == null)
            return new Vector3(input.x, 0f, input.y);
        Vector3 forward = Vector3.ProjectOnPlane(gameplayCamera.transform.forward, Vector3.up);
        if (forward.sqrMagnitude <= DirectionEpsilon)
            forward = Vector3.ProjectOnPlane(gameplayCamera.transform.up, Vector3.up);
        forward.Normalize();
        return Vector3.ClampMagnitude(Vector3.Cross(Vector3.up, forward) * input.x + forward * input.y, 1f);
    }

    private void ResolveCamera()
    {
        if (gameplayCamera == null)
            gameplayCamera = Camera.main;
    }

    private void OnDisable()
    {
        movementDirection = Vector3.zero;
        targetTurn = 0f;
        attackRequested = false;
        walkingBack = false;
        if (ready && characterAnimator != null && characterAnimator.isInitialized)
        {
            characterAnimator.ResetTrigger(AttackParameter);
            characterAnimator.SetBool(MovingParameter, false);
            characterAnimator.SetBool(AimingParameter, false);
            characterAnimator.SetBool(AttackHeldParameter, false);
            characterAnimator.SetFloat(TurnParameter, 0f);
            characterAnimator.SetFloat(WalkBackParameter, 0f);
        }
        if (body != null && !body.isKinematic)
            body.linearVelocity = Vector3.up * body.linearVelocity.y;
    }
}
