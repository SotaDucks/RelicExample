using UnityEngine;

/// <summary>Moves the PixelCameraSystem rig before PixelCameraManager performs pixel correction.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-100)]
[AddComponentMenu("Kaoru/Pixel Camera Follow")]
public sealed class KaoruPixelCameraFollow : MonoBehaviour
{
    [Tooltip("Character to follow. Attach this component to PixelCameraSystem.")]
    [SerializeField] private Transform target;
    [Tooltip("Follow smoothing in seconds. Zero follows immediately.")]
    [Min(0f)] [SerializeField] private float smoothTime = 0.12f;
    [Tooltip("Follow terrain height as well as horizontal movement.")]
    [SerializeField] private bool followHeight = true;

    private Transform activeTarget;
    private Vector3 followOffset;
    private Vector3 velocity;

    public Transform Target => target;
    public Vector3 FollowOffset => followOffset;

    private void OnEnable()
    {
        CaptureOffset();
    }

    public void SetTarget(Transform followTarget)
    {
        target = followTarget;
        CaptureOffset();
    }

    private void CaptureOffset()
    {
        activeTarget = target;
        velocity = Vector3.zero;
        if (target != null)
            followOffset = transform.position - target.position;
    }

    private void LateUpdate()
    {
        if (target == null || Time.deltaTime <= 0f)
            return;
        if (activeTarget != target)
            CaptureOffset();

        // Read the interpolated character Transform, not its unsmoothed physics
        // position. The rig retains its scene-authored rotation and composition.
        Vector3 destination = target.position + followOffset;
        if (!followHeight)
        {
            destination.y = transform.position.y;
            velocity.y = 0f;
        }
        transform.position = smoothTime <= 0f ? destination : Vector3.SmoothDamp(
            transform.position, destination, ref velocity, smoothTime, Mathf.Infinity, Time.deltaTime);
    }

    private void OnDisable()
    {
        velocity = Vector3.zero;
    }
}
