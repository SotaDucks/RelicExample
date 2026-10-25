using UnityEngine;
using VLB;

/// <summary>A floating light rig that follows an XZ-plane character and lights the path ahead.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(50)]
[AddComponentMenu("Kaoru/Light Companion")]
public sealed class KaoruLightCompanion : MonoBehaviour
{
    private const float DirectionEpsilon = 0.0001f;

    [Header("References / 引用")]
    [Tooltip("角色根节点。应与 KaoruTopDownController、Rigidbody 所在对象一致。")]
    [SerializeField] private Transform target;
    [Tooltip("本对象或子对象上的 VolumetricLightBeamHD；同一个灯光对象需要有 Spot Light。留空时自动查找。")]
    [SerializeField] private VolumetricLightBeamHD beam;

    [Header("Following / 跟随")]
    [Tooltip("相对于照明方向的位置：X 为右侧，Y 为悬浮高度，Z 为前方。负 Z 表示位于身后。单位为世界米，不受角色缩放影响。")]
    [SerializeField] private Vector3 followOffset = new Vector3(0.85f, 2.4f, -1.1f);
    [Tooltip("位置追上的平滑时间。0 表示立即跟随。")]
    [Min(0f)] [SerializeField] private float followSmoothTime = 0.22f;
    [Tooltip("每秒围绕角色转向的最大角度。影响伙伴位置和照明方向。")]
    [Min(1f)] [SerializeField] private float turnSpeed = 180f;
    [Tooltip("开启：跟随移动方向，停止时保留最后的方向；Kaoru 攻击时跟随攻击朝向。关闭：始终跟随角色朝向。")]
    [SerializeField] private bool followMovementDirection = true;
    [Tooltip("没有 Kaoru 控制器时，用实际位移判断移动方向的最低速度，避免静止时被微小抖动改变朝向。")]
    [Min(0.01f)] [SerializeField] private float movementThreshold = 0.1f;
    [Tooltip("角色单帧位移超过此距离时立即跟上，避免传送后缓慢穿过整个场景。0 为关闭。")]
    [Min(0f)] [SerializeField] private float teleportDistance = 8f;

    [Header("Aiming / 照射")]
    [Tooltip("瞄准角色前方多远。增大可以看得更远；设为 0 则照向角色脚下。")]
    [Min(0f)] [SerializeField] private float lookAheadDistance = 2.5f;
    [Tooltip("瞄准点相对于角色根节点的高度。角色根节点在脚下时，可从 0.1 开始。")]
    [SerializeField] private float aimHeight = 0.1f;
    [Tooltip("光束转动的平滑时间。0 表示立即瞄准。")]
    [Min(0f)] [SerializeField] private float aimSmoothTime = 0.08f;

    [Header("Hover / 悬浮")]
    [Tooltip("上下漂浮幅度，单位为世界米。0 为关闭。只移动伙伴，不让瞄准点上下漂移。")]
    [Min(0f)] [SerializeField] private float hoverAmplitude = 0.06f;
    [Tooltip("每秒漂浮次数。0 为关闭。随游戏暂停一起停止。")]
    [Min(0f)] [SerializeField] private float hoverFrequency = 0.7f;

    private Transform activeTarget;
    private VolumetricLightBeamHD activeBeam;
    private KaoruTopDownController targetController;
    private Vector3 lastTargetPosition;
    private Vector3 followPosition;
    private Vector3 followVelocity;
    private Vector3 desiredHeadingDirection;
    private Quaternion headingRotation;
    private Quaternion aimRotation;
    private float hoverPhase;
    private bool initialized;

    public Transform Target => target;
    public VolumetricLightBeamHD Beam => beam;

    private void Reset()
    {
        beam = GetComponentInChildren<VolumetricLightBeamHD>(true);
    }

    private void OnEnable()
    {
        initialized = false;
        followVelocity = Vector3.zero;
    }

    private void Start()
    {
        if (target == null)
            Debug.LogWarning("Kaoru Light Companion：请将角色根节点拖入 Target，设置后会自动开始跟随。", this);
    }

    public void SetTarget(Transform followTarget)
    {
        target = followTarget;
        initialized = false;
    }

    private void LateUpdate()
    {
        float deltaTime = Time.deltaTime;
        if (target == null)
        {
            initialized = false;
            return;
        }
        if (deltaTime <= 0f)
            return;
        if (!initialized || activeTarget != target || activeBeam != beam)
        {
            if (!Initialize())
                return;
        }

        Vector3 displacement = target.position - lastTargetPosition;
        bool teleported = teleportDistance > 0f && displacement.sqrMagnitude > teleportDistance * teleportDistance;
        desiredHeadingDirection = teleported
            ? PlanarDirection(target.forward, headingRotation * Vector3.forward)
            : GetDesiredDirection(displacement, deltaTime);
        Quaternion desiredHeading = Quaternion.LookRotation(desiredHeadingDirection, Vector3.up);
        headingRotation = teleported ? desiredHeading : Quaternion.RotateTowards(
            headingRotation, desiredHeading, turnSpeed * deltaTime);

        hoverPhase = Mathf.Repeat(hoverPhase + deltaTime * hoverFrequency * Mathf.PI * 2f, Mathf.PI * 2f);
        Vector3 destination = GetFollowDestination(target.position, headingRotation);
        if (teleported || followSmoothTime <= 0f)
        {
            followPosition = destination;
            followVelocity = Vector3.zero;
        }
        else
        {
            // Keep an independent world position, even when the rig is parented under the character.
            followPosition = Vector3.SmoothDamp(followPosition, destination, ref followVelocity,
                followSmoothTime, Mathf.Infinity, deltaTime);
        }

        ApplyPose(teleported, deltaTime);
        // HD's public property setters dirty only the changed properties. Preserve its existing
        // source toggles and multipliers; no mesh regeneration or additional tracking component.
        beam.AssignPropertiesFromAttachedSpotLight();
        lastTargetPosition = target.position;
    }

    private bool Initialize()
    {
        if (target == transform || target.IsChildOf(transform))
            return FailSetup("脚本需要挂在独立的灯光对象或伙伴空物体上，不能挂到角色自身或角色的父节点。");

        if (beam == null)
            beam = GetComponentInChildren<VolumetricLightBeamHD>(true);
        if (beam == null)
            return FailSetup("请将 VolumetricLightBeamHD 拖入 Beam，或放到脚本所在对象的子节点。");
        if (beam.transform != transform && !beam.transform.IsChildOf(transform))
            return FailSetup("Beam 必须位于脚本所在对象或它的子节点中。");
        Light spotLight = beam.GetComponent<Light>();
        if (spotLight == null || spotLight.type != LightType.Spot)
            return FailSetup("VolumetricLightBeamHD 所在对象还需要一个 Type 为 Spot 的 Unity Light，用于照亮地面和角色。");

        activeTarget = target;
        activeBeam = beam;
        targetController = target.GetComponent<KaoruTopDownController>();
        lastTargetPosition = target.position;
        followVelocity = Vector3.zero;
        hoverPhase = 0f;
        desiredHeadingDirection = PlanarDirection(target.forward, Vector3.forward);
        headingRotation = Quaternion.LookRotation(desiredHeadingDirection, Vector3.up);
        followPosition = GetFollowDestination(target.position, headingRotation);
        ApplyPose(true, 0f);
        initialized = true;
        return true;
    }

    private Vector3 GetDesiredDirection(Vector3 displacement, float deltaTime)
    {
        if (!followMovementDirection)
            return PlanarDirection(target.forward, desiredHeadingDirection);

        if (targetController != null && targetController.isActiveAndEnabled)
        {
            // MovementDirection is input intent and can remain non-zero during an attack.
            if (targetController.IsAttacking)
                return PlanarDirection(target.forward, desiredHeadingDirection);
            return PlanarDirection(targetController.MovementDirection, desiredHeadingDirection);
        }

        Vector3 planarDisplacement = Vector3.ProjectOnPlane(displacement, Vector3.up);
        float minimumDistance = movementThreshold * deltaTime;
        return planarDisplacement.sqrMagnitude > minimumDistance * minimumDistance
            ? planarDisplacement.normalized : desiredHeadingDirection;
    }

    private Vector3 GetFollowDestination(Vector3 targetPosition, Quaternion heading)
    {
        return targetPosition + heading * followOffset;
    }

    private Vector3 GetAimPoint(Vector3 targetPosition, Quaternion heading)
    {
        return targetPosition + heading * Vector3.forward * lookAheadDistance + Vector3.up * aimHeight;
    }

    private void ApplyPose(bool snap, float deltaTime)
    {
        // The root stays upright for a future companion model. Only the light tilts down.
        Vector3 hover = Vector3.up * (Mathf.Sin(hoverPhase) * hoverAmplitude);
        transform.SetPositionAndRotation(followPosition + hover, headingRotation);
        Vector3 direction = GetAimPoint(target.position, headingRotation) - beam.transform.position;
        if (direction.sqrMagnitude <= DirectionEpsilon)
            direction = Vector3.down;
        Vector3 up = Mathf.Abs(Vector3.Dot(direction.normalized, Vector3.up)) > 0.999f
            ? headingRotation * Vector3.forward : Vector3.up;
        Quaternion desiredAim = Quaternion.LookRotation(direction, up);
        aimRotation = snap || aimSmoothTime <= 0f ? desiredAim : Quaternion.Slerp(
            aimRotation, desiredAim, 1f - Mathf.Exp(-deltaTime / aimSmoothTime));
        beam.transform.rotation = aimRotation;
    }

    private static Vector3 PlanarDirection(Vector3 direction, Vector3 fallback)
    {
        direction = Vector3.ProjectOnPlane(direction, Vector3.up);
        return direction.sqrMagnitude > DirectionEpsilon ? direction.normalized : fallback;
    }

    private bool FailSetup(string message)
    {
        Debug.LogError("Kaoru Light Companion：" + message, this);
        enabled = false;
        return false;
    }

    private void OnDisable()
    {
        initialized = false;
        followVelocity = Vector3.zero;
    }

    private void OnDrawGizmosSelected()
    {
        if (target == null)
            return;
        Quaternion heading = Application.isPlaying && initialized ? headingRotation :
            Quaternion.LookRotation(PlanarDirection(target.forward, Vector3.forward), Vector3.up);
        Vector3 source = GetFollowDestination(target.position, heading);
        Vector3 aim = GetAimPoint(target.position, heading);
        Gizmos.color = new Color(0.45f, 0.85f, 1f, 0.8f);
        Gizmos.DrawWireSphere(source, 0.12f);
        Gizmos.DrawWireSphere(aim, 0.16f);
        Gizmos.DrawLine(source, aim);
    }
}
