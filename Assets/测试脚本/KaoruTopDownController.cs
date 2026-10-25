using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>Camera-relative running and mouse-directed attacks for Kaoru.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider), typeof(Animator))]
[AddComponentMenu("Kaoru/Top Down Controller")]
public sealed class KaoruTopDownController : MonoBehaviour
{
    private static readonly int IdleState = Animator.StringToHash("Base Layer.Idle");
    private static readonly int RunState = Animator.StringToHash("Base Layer.Run");
    private static readonly int AttackState = Animator.StringToHash("Base Layer.Attack");
    private static readonly int TurnParameter = Animator.StringToHash("Turn");

    [Header("References")]
    [Tooltip("Use the camera rendering the world, including PixelCamera when using the pixel camera system.")]
    [SerializeField] private Camera gameplayCamera;
    [SerializeField] private RuntimeAnimatorController animationController;
    [SerializeField] private AnimationClip attackClip;

    [Header("Running")]
    [Min(0f)] [SerializeField] private float runSpeed = 4f;
    [Min(0f)] [SerializeField] private float turnSpeed = 1080f;
    [Tooltip("Heading error that gives the left/right running animation full weight, in degrees.")]
    [Range(5f, 180f)] [SerializeField] private float fullTurnAnimationAngle = 45f;
    [Tooltip("Smooths the blend between RunA, RunA_L and RunA_R while preserving the running cycle.")]
    [Min(0f)] [SerializeField] private float turnAnimationDamping = 0.06f;

    [Header("Animation")]
    [Min(0f)] [SerializeField] private float blendDuration = 0.1f;
    [Min(0.01f)] [SerializeField] private float attackSpeed = 1f;
    [SerializeField] private bool faceMouseWhenAttacking = true;

    private Rigidbody body;
    private Animator characterAnimator;
    private Vector3 movementDirection;
    private Vector3 facingDirection;
    private float attackTimeRemaining;
    private int currentState;
    private float lastTurnSign = 1f;

    public bool IsAttacking => attackTimeRemaining > 0f;
    public Vector3 MovementDirection => movementDirection;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        characterAnimator = GetComponent<Animator>();

        if (animationController == null || attackClip == null)
        {
            Debug.LogError("Kaoru 控制器尚未配置。请选择 Kaoru，然后执行 Tools > Kaoru > Configure Selected Character。", this);
            enabled = false;
            return;
        }

        // The asset's display controller and root motion must not drive the player.
        characterAnimator.runtimeAnimatorController = animationController;
        characterAnimator.applyRootMotion = false;
        characterAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        characterAnimator.updateMode = AnimatorUpdateMode.Normal;
        characterAnimator.speed = 1f;

        // The supplied prefab freezes horizontal position. Allow XZ movement and
        // let gravity/collisions handle height while keeping the character upright.
        body.isKinematic = false;
        body.useGravity = true;
        body.constraints = RigidbodyConstraints.FreezeRotation;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        if (gameplayCamera == null)
            gameplayCamera = Camera.main;

        facingDirection = transform.forward;
        characterAnimator.SetFloat(TurnParameter, 0f);
        characterAnimator.Play(IdleState, 0, 0f);
        currentState = IdleState;
    }

    private void Update()
    {
        if (Time.timeScale <= 0f)
            return;

        Vector2 input = ReadMovement();
        movementDirection = GetWorldDirection(input);

        if (IsAttacking)
            attackTimeRemaining = Mathf.Max(0f, attackTimeRemaining - Time.deltaTime);

        Mouse mouse = Mouse.current;
        bool pointerOverUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        if (!IsAttacking && mouse != null && mouse.leftButton.wasPressedThisFrame && !pointerOverUI)
            StartAttack(mouse.position.ReadValue());

        if (!IsAttacking)
        {
            if (movementDirection.sqrMagnitude > 0.001f)
                facingDirection = movementDirection;

            ChangeState(movementDirection.sqrMagnitude > 0.001f ? RunState : IdleState);
        }

        UpdateTurnAnimation();
    }

    private void UpdateTurnAnimation()
    {
        if (IsAttacking || movementDirection.sqrMagnitude <= 0.001f)
        {
            characterAnimator.SetFloat(TurnParameter, 0f);
            return;
        }

        // Use the actual physics heading, not A/D keys or the interpolated render
        // transform: either key can mean a left or right turn depending on facing.
        float angle = Vector3.SignedAngle(body.rotation * Vector3.forward, movementDirection, Vector3.up);
        float magnitude = Mathf.Abs(angle);
        if (magnitude > 3f && magnitude < 175f)
            lastTurnSign = Mathf.Sign(angle);

        // A 180-degree reversal has two equally short paths. Retain the previous
        // turning side so the animation and physical rotation agree without jitter.
        if (magnitude >= 175f)
            angle = magnitude * lastTurnSign;

        float turn = Mathf.Sign(angle) * Mathf.InverseLerp(3f, fullTurnAnimationAngle, magnitude);
        characterAnimator.SetFloat(TurnParameter, turn, turnAnimationDamping, Time.deltaTime);
    }

    private void FixedUpdate()
    {
        Vector3 velocity = IsAttacking ? Vector3.zero : movementDirection * runSpeed;
        velocity.y = body.linearVelocity.y;
        body.linearVelocity = velocity;

        if (facingDirection.sqrMagnitude > 0.001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(facingDirection, Vector3.up);
            // Resolve an exactly opposite target with the same side as the RunA blend.
            float remainingAngle = Vector3.Angle(body.rotation * Vector3.forward, facingDirection);
            if (!IsAttacking && remainingAngle >= 175f)
                targetRotation = body.rotation * Quaternion.Euler(0f,
                    lastTurnSign * Mathf.Min(turnSpeed * Time.fixedDeltaTime, remainingAngle), 0f);
            body.MoveRotation(IsAttacking
                ? targetRotation
                : Quaternion.RotateTowards(body.rotation, targetRotation, turnSpeed * Time.fixedDeltaTime));
        }
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
        if (gameplayCamera == null)
            return new Vector3(input.x, 0f, input.y);

        Vector3 forward = Vector3.ProjectOnPlane(gameplayCamera.transform.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.001f)
            forward = Vector3.ProjectOnPlane(gameplayCamera.transform.up, Vector3.up);

        forward.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        return Vector3.ClampMagnitude(right * input.x + forward * input.y, 1f);
    }

    private void StartAttack(Vector2 screenPosition)
    {
        if (faceMouseWhenAttacking && gameplayCamera != null)
        {
            // Viewport coordinates also work when PixelCamera renders to a small
            // RenderTexture instead of directly to the screen.
            Vector3 viewport = new Vector3(screenPosition.x / Screen.width, screenPosition.y / Screen.height, 0f);
            Ray ray = gameplayCamera.ViewportPointToRay(viewport);
            Plane groundPlane = new Plane(Vector3.up, body.position);
            if (groundPlane.Raycast(ray, out float distance))
            {
                Vector3 direction = Vector3.ProjectOnPlane(ray.GetPoint(distance) - body.position, Vector3.up);
                if (direction.sqrMagnitude > 0.001f)
                    facingDirection = direction.normalized;
            }
        }

        attackTimeRemaining = attackClip.length / attackSpeed;
        characterAnimator.SetFloat(TurnParameter, 0f);
        characterAnimator.speed = attackSpeed;
        // A fresh click on the exact frame an attack finishes must restart it too.
        characterAnimator.CrossFadeInFixedTime(AttackState, blendDuration, 0, 0f);
        currentState = AttackState;
    }

    private void ChangeState(int state)
    {
        if (currentState == state)
            return;

        if (state != AttackState)
            characterAnimator.speed = 1f;

        characterAnimator.CrossFadeInFixedTime(state, blendDuration, 0, 0f);
        currentState = state;
    }

    private void OnDisable()
    {
        movementDirection = Vector3.zero;
        attackTimeRemaining = 0f;
        if (body != null && !body.isKinematic)
            body.linearVelocity = new Vector3(0f, body.linearVelocity.y, 0f);
    }

    private void OnEnable()
    {
        // Also recover correctly when the component is disabled during an attack.
        if (characterAnimator != null && animationController != null)
        {
            characterAnimator.speed = 1f;
            characterAnimator.SetFloat(TurnParameter, 0f);
            characterAnimator.Play(IdleState, 0, 0f);
            currentState = IdleState;
        }
    }
}
