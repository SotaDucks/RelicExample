using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Ground support, slope motion and stair traversal for an upright rigidbody capsule.</summary>
internal sealed class KaoruGroundMotor
{
    internal const float DefaultMaxGroundSlope = 50f;
    internal const float DefaultMaxStepHeight = 0.9f;
    internal const float DefaultStepClimbSpeed = 3f;
    internal const float DefaultGroundSnapDistance = 0.85f;

    internal readonly struct Settings
    {
        internal readonly LayerMask WalkableLayers;
        internal readonly float MaxGroundSlope;
        internal readonly float MaxStepHeight;
        internal readonly float StepClimbSpeed;
        internal readonly float GroundSnapDistance;

        internal Settings(LayerMask walkableLayers, float maxGroundSlope, float maxStepHeight,
            float stepClimbSpeed, float groundSnapDistance)
        {
            WalkableLayers = walkableLayers;
            MaxGroundSlope = maxGroundSlope;
            MaxStepHeight = maxStepHeight;
            StepClimbSpeed = stepClimbSpeed;
            GroundSnapDistance = groundSnapDistance;
        }
    }

    private const float DirectionEpsilon = 0.0001f;
    private const float GroundProbeSkin = 0.015f;
    private const float GroundProbeLift = 0.08f;
    private const float GroundContactDistance = 0.04f;

    private readonly Rigidbody body;
    private readonly CapsuleCollider capsule;
    private readonly RaycastHit[] movementHits = new RaycastHit[32];
    private bool followingGround;
    private bool steppingUp;
    private Vector3 stepTarget;
    private Vector3 stepDirection;
    private float stepTimeRemaining;

    internal KaoruGroundMotor(Rigidbody body, CapsuleCollider capsule)
    {
        this.body = body;
        this.capsule = capsule;
    }

    internal void Move(Vector3 planarVelocity, Vector3 moveDirection, float moveSpeed, Settings settings)
    {
        if (!body.isKinematic)
            body.linearVelocity = ResolveVelocity(planarVelocity, body.linearVelocity.y, moveDirection, moveSpeed, settings);
    }

    internal void Stop()
    {
        if (!body.isKinematic)
        {
            float verticalSpeed = body.linearVelocity.y;
            body.linearVelocity = Vector3.up * (steppingUp ? Mathf.Min(0f, verticalSpeed) : verticalSpeed);
        }
        steppingUp = false;
    }

    internal void Reset()
    {
        followingGround = false;
        steppingUp = false;
    }

    private Vector3 ResolveVelocity(Vector3 planarVelocity, float verticalSpeed, Vector3 moveDirection,
        float moveSpeed, Settings settings)
    {
        bool wasFollowingGround = followingGround;
        followingGround = false;
        Vector3 velocity = planarVelocity + Vector3.up * verticalSpeed;
        if (!capsule.enabled || capsule.isTrigger || capsule.direction != 1 ||
            body.rotation * Vector3.up != Vector3.up ||
            (body.constraints & RigidbodyConstraints.FreezePositionY) != 0)
        {
            steppingUp = false;
            return velocity;
        }

        Vector3 scale = capsule.transform.lossyScale;
        float radius = capsule.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        if (radius <= 0f)
            return velocity;
        float inset = Mathf.Min(GroundProbeSkin, radius * 0.1f);
        float halfSegment = Mathf.Max(0f, capsule.height * Mathf.Abs(scale.y) * 0.5f - radius);
        Vector3 center = body.position + body.rotation * Vector3.Scale(capsule.center, scale);
        Vector3 lower = center - Vector3.up * halfSegment;
        Vector3 upper = center + Vector3.up * halfSegment;
        PhysicsScene physicsScene = body.gameObject.scene.GetPhysicsScene();
        if (steppingUp)
        {
            if (TryAdvanceStep(physicsScene, lower, upper, radius - inset, inset, moveDirection, moveSpeed,
                settings.StepClimbSpeed, out Vector3 climbVelocity))
                return climbVelocity;

            steppingUp = false;
            // The ascent motor supplied the upward velocity. Let gravity settle the landing.
            verticalSpeed = Mathf.Min(0f, verticalSpeed);
            velocity = planarVelocity + Vector3.up * verticalSpeed;
        }

        float walkableY = Mathf.Cos(settings.MaxGroundSlope * Mathf.Deg2Rad);
        float probeDrop = wasFollowingGround ? Mathf.Max(GroundContactDistance, settings.GroundSnapDistance) : GroundContactDistance;
        if (!ProbeGround(physicsScene, lower, radius - inset, inset, probeDrop, walkableY, settings.WalkableLayers,
            out RaycastHit groundHit, out float drop))
            return velocity;

        // Sample the surface itself. The sphere can report a diagonal normal on a stair edge.
        float slopeSpeed = 0f;
        if (planarVelocity.sqrMagnitude > DirectionEpsilon)
        {
            int count = physicsScene.Raycast(groundHit.point + Vector3.up * GroundProbeLift, Vector3.down,
                movementHits, GroundProbeLift * 2f, settings.WalkableLayers, QueryTriggerInteraction.Ignore);
            if (TryGetNearestHit(count, -1f, out RaycastHit surface) &&
                surface.collider == groundHit.collider && surface.normal.y >= walkableY)
                slopeSpeed = -Vector3.Dot(planarVelocity, surface.normal) / surface.normal.y;
        }

        // A shallow lip can kick the capsule upward without breaking ground contact.
        bool closeToGround = drop <= GroundContactDistance;
        float upwardTolerance = wasFollowingGround && closeToGround ? 2f : 0.5f;
        if (verticalSpeed > Mathf.Max(0f, slopeSpeed) + upwardTolerance ||
            (!wasFollowingGround && (drop > capsule.contactOffset || verticalSpeed < -1f)))
            return velocity;

        followingGround = true;
        if (closeToGround && settings.MaxStepHeight > 0f && Vector3.Dot(moveDirection, planarVelocity) > 0f &&
            TryStartStep(physicsScene, lower, upper, radius, inset, planarVelocity * Time.fixedDeltaTime,
                walkableY, moveSpeed, settings))
        {
            if (TryAdvanceStep(physicsScene, lower, upper, radius - inset, inset, moveDirection, moveSpeed,
                settings.StepClimbSpeed, out Vector3 stepVelocity))
                return stepVelocity;
            steppingUp = false;
        }

        if (drop > inset && drop <= settings.GroundSnapDistance && wasFollowingGround)
        {
            // Sweep the whole body before lowering it, including across the edge of the upper tread.
            float snap = drop;
            if (SweepCapsule(physicsScene, lower, upper, radius - inset, Vector3.down, drop + inset,
                out RaycastHit obstruction))
                snap = Mathf.Clamp(obstruction.distance - inset, 0f, drop);
            body.position -= Vector3.up * snap;
        }
        return planarVelocity + Vector3.up * slopeSpeed;
    }

    private bool TryStartStep(PhysicsScene physicsScene, Vector3 lower, Vector3 upper, float radius,
        float inset, Vector3 displacement, float walkableY, float moveSpeed, Settings settings)
    {
        float travel = displacement.magnitude;
        if (travel <= Mathf.Epsilon)
            return false;

        Vector3 direction = displacement / travel;
        float castRadius = radius - inset;
        if (!SweepCapsule(physicsScene, lower, upper, castRadius, direction, travel + 2f * inset,
            out RaycastHit obstacle) || obstacle.distance <= 0f ||
            (obstacle.rigidbody != null && !obstacle.rigidbody.isKinematic))
            return false;

        Vector3 wallNormal = Vector3.ProjectOnPlane(obstacle.normal, Vector3.up).normalized;
        float approach = -Vector3.Dot(direction, wallNormal);
        if (approach < 0.25f)
            return false;

        // Sample just past the riser to find the top of the tread.
        float landingDistance = Vector3.Dot(lower - obstacle.point, wallNormal) / approach + inset;
        if (landingDistance <= 0f || landingDistance > travel + radius * 1.5f)
            return false;

        Vector3 feet = lower - Vector3.up * radius;
        Vector3 origin = feet + direction * landingDistance + Vector3.up * (settings.MaxStepHeight + inset);
        int count = physicsScene.Raycast(origin, Vector3.down, movementHits, settings.MaxStepHeight + inset,
            settings.WalkableLayers, QueryTriggerInteraction.Ignore);
        if (!TryGetNearestHit(count, -1f, out RaycastHit landing) || landing.normal.y < walkableY ||
            (obstacle.normal.y >= walkableY && Vector3.Dot(obstacle.normal, landing.normal) > 0.98f) ||
            (landing.rigidbody != null && !landing.rigidbody.isKinematic))
            return false;

        float rise = landing.point.y - feet.y;
        if (rise <= inset || rise > settings.MaxStepHeight)
            return false;

        float lift = rise + inset;
        Vector3 elevation = Vector3.up * lift;
        if (SweepCapsule(physicsScene, lower, upper, castRadius, Vector3.up, lift + inset, out _))
            return false;

        // A narrow tread may be reachable by the bottom of the capsule before its center clears the riser.
        float advance = landingDistance;
        if (SweepCapsule(physicsScene, lower + elevation, upper + elevation, castRadius,
            direction, landingDistance, out RaycastHit nextObstacle))
            advance = nextObstacle.distance - inset;
        if (advance <= 0f || !ProbeGround(physicsScene, lower + elevation + direction * advance,
            castRadius, inset, rise, walkableY, settings.WalkableLayers, out RaycastHit support, out _))
            return false;

        // A wide capsule can rest on the edge of the next tread before its center reaches it.
        float supportRise = support.point.y - feet.y;
        if (supportRise <= inset || supportRise > settings.MaxStepHeight ||
            (support.rigidbody != null && !support.rigidbody.isKinematic))
            return false;

        stepTarget = body.position + elevation + direction * advance;
        stepDirection = direction;
        stepTimeRemaining = lift / Mathf.Max(0.1f, settings.StepClimbSpeed) + advance / Mathf.Max(0.1f, moveSpeed) + 0.15f;
        steppingUp = true;
        return true;
    }

    private bool TryAdvanceStep(PhysicsScene physicsScene, Vector3 lower, Vector3 upper, float radius,
        float inset, Vector3 moveDirection, float moveSpeed, float stepClimbSpeed, out Vector3 velocity)
    {
        velocity = Vector3.zero;
        float deltaTime = Time.fixedDeltaTime;
        stepTimeRemaining -= deltaTime;
        if (stepTimeRemaining <= 0f || moveSpeed <= 0f || Vector3.Dot(moveDirection, stepDirection) < 0f)
            return false;

        Vector3 remaining = stepTarget - body.position;
        Vector3 forward = Vector3.ProjectOnPlane(remaining, Vector3.up);
        float distance = forward.magnitude;
        if (remaining.y <= inset * 0.25f && distance <= inset)
            return false;

        float lift = Mathf.Clamp(remaining.y, 0f, Mathf.Max(0.1f, stepClimbSpeed) * deltaTime);
        if (lift > 0f && SweepCapsule(physicsScene, lower, upper, radius, Vector3.up, lift + inset, out RaycastHit ceiling))
            lift = Mathf.Clamp(ceiling.distance - inset, 0f, lift);

        // Move into the tread as the rounded foot clears the riser. The body stays in the solver.
        if (distance > inset)
        {
            Vector3 direction = forward / distance;
            float travel = Mathf.Min(distance, moveSpeed * deltaTime);
            if (SweepCapsule(physicsScene, lower, upper, radius, direction, travel + inset, out RaycastHit riser))
                travel = Mathf.Clamp(riser.distance - inset, 0f, travel);
            velocity = direction * (travel / deltaTime);
        }

        if (lift <= 0f && velocity.sqrMagnitude <= DirectionEpsilon)
            return false;

        followingGround = true;
        float gravity = body.useGravity ? Physics.gravity.y : 0f;
        velocity.y = lift / deltaTime - gravity * deltaTime;
        return true;
    }

    private bool ProbeGround(PhysicsScene physicsScene, Vector3 lower, float radius, float inset,
        float maxDrop, float walkableY, LayerMask walkableLayers, out RaycastHit hit, out float drop)
    {
        int count = physicsScene.SphereCast(lower + Vector3.up * GroundProbeLift, radius,
            Vector3.down, movementHits, GroundProbeLift + maxDrop + inset / walkableY,
            walkableLayers, QueryTriggerInteraction.Ignore);
        if (!TryGetNearestHit(count, walkableY, out hit))
        {
            drop = 0f;
            return false;
        }
        drop = hit.distance - GroundProbeLift - inset / hit.normal.y;
        return drop <= maxDrop;
    }

    private bool SweepCapsule(PhysicsScene physicsScene, Vector3 lower, Vector3 upper, float radius,
        Vector3 direction, float distance, out RaycastHit hit)
    {
        int count = physicsScene.CapsuleCast(lower, upper, radius, direction, movementHits, distance,
            Physics.AllLayers, QueryTriggerInteraction.Ignore);
        return TryGetNearestHit(count, -1f, out hit);
    }

    private bool TryGetNearestHit(int count, float minNormalY, out RaycastHit nearest)
    {
        nearest = default;
        float nearestDistance = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = movementHits[i];
            Collider other = hit.collider;
            if (hit.distance >= nearestDistance || hit.normal.y < minNormalY ||
                (hit.distance <= 0f && minNormalY >= 0f) || other.attachedRigidbody == body ||
                Physics.GetIgnoreLayerCollision(capsule.gameObject.layer, other.gameObject.layer) ||
                Physics.GetIgnoreCollision(capsule, other))
                continue;
            nearest = hit;
            nearestDistance = hit.distance;
        }
        return nearest.collider != null;
    }
}
