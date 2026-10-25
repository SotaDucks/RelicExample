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
    public const string AttackSpeedParameterName = "AttackSpeed";
    public const string AttackTag = "Attack";

    private static readonly int MovingParameter = Animator.StringToHash(MovingParameterName);
    private static readonly int TurnParameter = Animator.StringToHash(TurnParameterName);
    private static readonly int AttackParameter = Animator.StringToHash(AttackParameterName);
    private static readonly int AttackSpeedParameter = Animator.StringToHash(AttackSpeedParameterName);
    private const float DirectionEpsilon = 0.0001f;

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
    [Tooltip("Multiplies only AttackA1. Animator state base speed remains 1.")]
    [Range(0.1f, 3f)] [SerializeField] private float attackSpeed = 1f;
    [SerializeField] private bool faceMouseWhenAttacking = true;

    private Rigidbody body;
    private Animator characterAnimator;
    private Vector3 movementDirection;
    private Vector3 facingDirection;
    private bool attackRequested;
    private bool ready;
    private float lastTurnSign = 1f;
    private float targetTurn;

    // Lock physics from the request frame through the complete outgoing blend.
    public bool IsAttacking => ready && (attackRequested || AnimatorIsAttacking());
    public Vector3 MovementDirection => movementDirection;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        characterAnimator = GetComponent<Animator>();
        if (animationController != null)
            characterAnimator.runtimeAnimatorController = animationController;
        if (!HasParameter(MovingParameter, AnimatorControllerParameterType.Bool) ||
            !HasParameter(TurnParameter, AnimatorControllerParameterType.Float) ||
            !HasParameter(AttackParameter, AnimatorControllerParameterType.Trigger) ||
            !HasParameter(AttackSpeedParameter, AnimatorControllerParameterType.Float))
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
        // Activation resets to the graph's default. No gameplay Play/CrossFade.
        characterAnimator.Rebind();
        characterAnimator.SetBool(MovingParameter, false);
        characterAnimator.SetFloat(TurnParameter, 0f);
        characterAnimator.SetFloat(AttackSpeedParameter, attackSpeed);
        characterAnimator.ResetTrigger(AttackParameter);
    }

    private void Update()
    {
        if (Time.timeScale <= 0f)
            return;
        if (attackRequested && AnimatorIsAttacking())
            attackRequested = false;
        movementDirection = GetWorldDirection(ReadMovement());
        // Intent stays current while attacking, so exit conditions choose the
        // correct locomotion state when the animation has actually finished.
        bool moving = movementDirection.sqrMagnitude > DirectionEpsilon && runSpeed > 0f;
        characterAnimator.SetBool(MovingParameter, moving);
        characterAnimator.SetFloat(AttackSpeedParameter, Mathf.Clamp(attackSpeed, 0.1f, 3f));
        Mouse mouse = Mouse.current;
        bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        if (mouse != null && mouse.leftButton.wasPressedThisFrame && !overUI && !IsAttacking)
            RequestAttack(mouse.position.ReadValue());
        if (!IsAttacking && moving)
            facingDirection = movementDirection.normalized;
        characterAnimator.SetFloat(TurnParameter, IsAttacking || !moving ? 0f : targetTurn,
            turnAnimationDamping, Time.deltaTime);
    }

    private void FixedUpdate()
    {
        if (!ready)
            return;
        bool attacking = IsAttacking;
        Vector3 planarVelocity = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up);
        Vector3 desiredVelocity = movementDirection * runSpeed;
        planarVelocity = attacking ? Vector3.zero : Vector3.MoveTowards(planarVelocity, desiredVelocity,
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
        if (attacking)
            body.MoveRotation(Quaternion.LookRotation(facingDirection, Vector3.up));
        else
        {
            float step = Mathf.Clamp(angle, -turnSpeed * Time.fixedDeltaTime, turnSpeed * Time.fixedDeltaTime);
            body.MoveRotation(body.rotation * Quaternion.AngleAxis(step, Vector3.up));
        }
    }

    private void RequestAttack(Vector2 screenPosition)
    {
        if (!characterAnimator.isActiveAndEnabled)
            return;
        if (faceMouseWhenAttacking)
        {
            ResolveCamera();
            if (gameplayCamera != null && Screen.width > 0 && Screen.height > 0)
            {
                // Normalized screen coordinates also work with PixelCamera's
                // lower-resolution RenderTexture.
                Vector3 viewport = new Vector3(screenPosition.x / Screen.width, screenPosition.y / Screen.height);
                Ray ray = gameplayCamera.ViewportPointToRay(viewport);
                Plane ground = new Plane(Vector3.up, body.position);
                if (ground.Raycast(ray, out float distance))
                {
                    Vector3 direction = Vector3.ProjectOnPlane(ray.GetPoint(distance) - body.position, Vector3.up);
                    if (direction.sqrMagnitude > DirectionEpsilon)
                        facingDirection = direction.normalized;
                }
            }
        }
        attackRequested = true;
        targetTurn = 0f;
        characterAnimator.SetFloat(TurnParameter, 0f);
        characterAnimator.SetTrigger(AttackParameter);
        body.linearVelocity = Vector3.up * body.linearVelocity.y;
    }

    private bool AnimatorIsAttacking()
    {
        if (!characterAnimator.isInitialized)
            return false;
        return characterAnimator.GetCurrentAnimatorStateInfo(0).IsTag(AttackTag) ||
            (characterAnimator.IsInTransition(0) && characterAnimator.GetNextAnimatorStateInfo(0).IsTag(AttackTag));
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
        if (ready && characterAnimator != null && characterAnimator.isInitialized)
        {
            characterAnimator.ResetTrigger(AttackParameter);
            characterAnimator.SetBool(MovingParameter, false);
            characterAnimator.SetFloat(TurnParameter, 0f);
        }
        if (body != null && !body.isKinematic)
            body.linearVelocity = Vector3.up * body.linearVelocity.y;
    }
}
