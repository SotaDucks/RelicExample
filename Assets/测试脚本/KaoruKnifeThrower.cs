using System;
using UnityEngine;

/// <summary>
/// Spawns a knife at the release frame of each upper-body attack state.
/// Flight parameters live on the knife prefab (KaoruThrowingKnife).
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(KaoruTopDownController), typeof(Animator))]
[AddComponentMenu("Kaoru/Knife Thrower")]
public sealed class KaoruKnifeThrower : MonoBehaviour
{
    [Serializable]
    public sealed class ThrowPoint
    {
        [Tooltip("Upper Body state that releases this knife.")]
        public string stateName = "AttackA1";
        [Tooltip("Normalized state time of the release (0 = start, 1 = end of the clip).")]
        [Range(0f, 1f)] public float releaseTime = 0.15f;
        [Tooltip("Humanoid bone the knife leaves from.")]
        public HumanBodyBones hand = HumanBodyBones.RightHand;
        [Tooltip("Optional explicit spawn transform; overrides the hand bone.")]
        public Transform spawnOverride;
        [Tooltip("Added to the hand position, in character space (x right, y up, z forward).")]
        public Vector3 spawnOffset = new Vector3(0f, 0f, 0.15f);
        [Tooltip("Extra horizontal angle for this knife, degrees (+ = clockwise seen from above).")]
        [Range(-45f, 45f)] public float yawOffset;

        [NonSerialized] public float lastTime = -1f;
        [NonSerialized] public bool fired;
    }

    [Tooltip("Prefab with a Rigidbody, a Collider and KaoruThrowingKnife. Every throw spawns a new one.")]
    public KaoruThrowingKnife knifePrefab;
    [Tooltip("Knife model held in the hand. At game start its Rigidbodies, Colliders, Joints and " +
        "KaoruThrowingKnife are removed so it only follows the hand as a visual.")]
    public GameObject heldKnife;
    [Tooltip("One entry per release. Defaults: both AttackA1 and AttackA2 throw from the right hand.")]
    public ThrowPoint[] throws =
    {
        new ThrowPoint { stateName = "AttackA1", releaseTime = 0.15f, hand = HumanBodyBones.RightHand },
        new ThrowPoint { stateName = "AttackA2", releaseTime = 0.17f, hand = HumanBodyBones.RightHand }
    };

    [Header("Aim")]
    [Tooltip("Fly toward the cursor's ground point so both hands converge on it, instead of parallel to the body.")]
    public bool aimAtCursorPoint = true;
    [Tooltip("Below this horizontal distance to the cursor the body's aim direction is used instead.")]
    [Min(0f)] public float minCursorDistance = 1.5f;
    [Tooltip("Up/down launch angle in degrees; 0 = level flight.")]
    [Range(-30f, 30f)] public float launchPitch;
    [Tooltip("Launch every knife at a fixed height above the feet instead of the hand's height.")]
    public bool overrideLaunchHeight;
    [Min(0f)] public float launchHeight = 1f;
    [Tooltip("Add the character's horizontal velocity to the knife.")]
    public bool inheritCharacterVelocity;

    private KaoruTopDownController controller;
    private Animator characterAnimator;
    private Rigidbody body;
    private Collider[] ownColliders;
    private int upperBodyLayer = -1;
    private bool warnedMissingPrefab;

    private void Awake()
    {
        controller = GetComponent<KaoruTopDownController>();
        characterAnimator = GetComponent<Animator>();
        body = GetComponent<Rigidbody>();
        MakeVisualOnly(heldKnife);
        ownColliders = GetComponentsInChildren<Collider>();
    }

    private void MakeVisualOnly(GameObject model)
    {
        if (model == null)
            return;
        if (model == gameObject || transform.IsChildOf(model.transform))
        {
            Debug.LogWarning("Held Knife 不能是角色本身或其父级。", this);
            return;
        }
        // Dependents first: they RequireComponent(Rigidbody). Immediate so no physics step sees them.
        foreach (KaoruThrowingKnife knife in model.GetComponentsInChildren<KaoruThrowingKnife>(true))
            DestroyImmediate(knife);
        foreach (Joint joint in model.GetComponentsInChildren<Joint>(true))
            DestroyImmediate(joint);
        foreach (Collider modelCollider in model.GetComponentsInChildren<Collider>(true))
            DestroyImmediate(modelCollider);
        foreach (Rigidbody modelBody in model.GetComponentsInChildren<Rigidbody>(true))
            DestroyImmediate(modelBody);
    }

    private void OnEnable()
    {
        foreach (ThrowPoint point in throws)
            ResetPoint(point);
    }

    // After the Animator has evaluated, so state time and hand pose are this frame's.
    private void LateUpdate()
    {
        if (characterAnimator.runtimeAnimatorController == null || !characterAnimator.isInitialized)
            return;
        if (upperBodyLayer < 0)
            upperBodyLayer = characterAnimator.GetLayerIndex(KaoruTopDownController.UpperBodyLayerName);
        if (upperBodyLayer < 0)
            return;
        AnimatorStateInfo current = characterAnimator.GetCurrentAnimatorStateInfo(upperBodyLayer);
        bool inTransition = characterAnimator.IsInTransition(upperBodyLayer);
        AnimatorStateInfo next = inTransition ? characterAnimator.GetNextAnimatorStateInfo(upperBodyLayer) : default;
        foreach (ThrowPoint point in throws)
        {
            float time = -1f;
            if (current.IsName(point.stateName))
                time = current.normalizedTime;
            else if (inTransition && next.IsName(point.stateName))
                time = next.normalizedTime;
            if (time < 0f)
            {
                ResetPoint(point);
                continue;
            }
            // A drop in time means the state was entered again.
            if (time < point.lastTime)
                point.fired = false;
            point.lastTime = time;
            if (!point.fired && time >= point.releaseTime)
            {
                point.fired = true;
                Throw(point);
            }
        }
    }

    private void Throw(ThrowPoint point)
    {
        if (knifePrefab == null)
        {
            if (!warnedMissingPrefab)
                Debug.LogWarning("KaoruKnifeThrower 未指定 Knife Prefab，不会投出飞刀。", this);
            warnedMissingPrefab = true;
            return;
        }
        Vector3 origin = GetSpawnPosition(point, characterAnimator);
        Vector3 direction = controller.AimDirection;
        if (aimAtCursorPoint && controller.HasAimPoint)
        {
            Vector3 toCursor = Vector3.ProjectOnPlane(controller.AimPoint - origin, Vector3.up);
            if (toCursor.magnitude >= minCursorDistance)
                direction = toCursor;
        }
        direction = Vector3.ProjectOnPlane(direction, Vector3.up);
        if (direction.sqrMagnitude < 0.0001f)
            direction = transform.forward;
        direction = Quaternion.AngleAxis(point.yawOffset, Vector3.up) * direction.normalized;
        // Rotating about the right axis by a negative angle pitches upward.
        direction = Quaternion.AngleAxis(-launchPitch, Vector3.Cross(Vector3.up, direction)) * direction;

        KaoruThrowingKnife knife = Instantiate(knifePrefab, origin, Quaternion.LookRotation(direction, Vector3.up));
        Vector3 inherited = inheritCharacterVelocity && body != null
            ? Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up) : Vector3.zero;
        knife.Launch(direction, gameObject, ownColliders, inherited);
    }

    private Vector3 GetSpawnPosition(ThrowPoint point, Animator animator)
    {
        Vector3 origin;
        if (point.spawnOverride != null)
            origin = point.spawnOverride.position;
        else
        {
            Transform bone = animator != null && animator.isHuman ? animator.GetBoneTransform(point.hand) : null;
            origin = bone != null ? bone.position : transform.position + Vector3.up * launchHeight;
        }
        origin += transform.rotation * point.spawnOffset;
        if (overrideLaunchHeight)
            origin.y = transform.position.y + launchHeight;
        return origin;
    }

    private static void ResetPoint(ThrowPoint point)
    {
        point.lastTime = -1f;
        point.fired = false;
    }

    private void OnDrawGizmosSelected()
    {
        Animator animator = GetComponent<Animator>();
        if (animator == null || throws == null)
            return;
        Gizmos.color = new Color(1f, 0.6f, 0.1f);
        foreach (ThrowPoint point in throws)
        {
            Vector3 origin = GetSpawnPosition(point, animator);
            Gizmos.DrawWireSphere(origin, 0.05f);
            Gizmos.DrawLine(origin, origin + Quaternion.AngleAxis(point.yawOffset, Vector3.up) * transform.forward * 0.6f);
        }
    }
}
