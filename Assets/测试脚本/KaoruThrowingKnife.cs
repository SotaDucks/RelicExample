using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Physics-driven flight of one thrown knife. Lives on the knife prefab; launched by KaoruKnifeThrower.
/// The Rigidbody does all the flying: the values below are applied to it on Launch and
/// override whatever is set on the Rigidbody component itself.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
[AddComponentMenu("Kaoru/Throwing Knife")]
public sealed class KaoruThrowingKnife : MonoBehaviour
{
    public enum HitBehaviour
    {
        [Tooltip("Stick when the tip hits fast and straight enough, otherwise bounce physically.")]
        StickWhenTipHits,
        [Tooltip("Remove the knife on the first collision.")]
        Destroy,
        [Tooltip("Never stick; collisions are left entirely to the physics engine.")]
        PhysicsOnly
    }

    [Header("Rigidbody")]
    [Tooltip("kg. Heavier knives push hit Rigidbodies harder and need more force with Impulse launch.")]
    [Min(0.001f)] public float mass = 0.15f;
    [Tooltip("Rigidbody linear damping: velocity-proportional air resistance.")]
    [Min(0f)] public float linearDamping = 0.05f;
    [Tooltip("Rigidbody angular damping: how quickly spin dies out.")]
    [Min(0f)] public float angularDamping = 0.5f;
    [Tooltip("Multiplier of Physics.gravity. 0 = straight flight, 1 = real gravity.")]
    public float gravityScale = 0.3f;
    [Tooltip("Centre of mass in the knife's local space. Toward the tip (+Z) makes it fly nose-first naturally. Off = Unity computes it from the colliders.")]
    public bool overrideCenterOfMass = true;
    public Vector3 centerOfMass = new Vector3(0f, 0f, 0.15f);
    [Tooltip("rad/s cap on spin. Unity's default of 7 is too low for a thrown blade.")]
    [Min(1f)] public float maxAngularVelocity = 50f;
    public CollisionDetectionMode collisionDetection = CollisionDetectionMode.ContinuousDynamic;
    public RigidbodyInterpolation interpolation = RigidbodyInterpolation.Interpolate;

    [Header("Launch")]
    [Tooltip("Velocity change in m/s (VelocityChange) or impulse in N·s (Impulse, speed = force / mass).")]
    [Min(0f)] public float launchForce = 18f;
    public ForceMode launchForceMode = ForceMode.VelocityChange;
    [Tooltip("Initial spin in the knife's local space, degrees per second. X = end-over-end, Z = drill roll.")]
    public Vector3 launchSpin = Vector3.zero;
    [Tooltip("Roll of the blade around its tip axis at launch. The mesh tip is local +Z; 90 lays the blade flat for the top-down camera.")]
    [Range(-180f, 180f)] public float bladeRoll = 90f;

    [Header("Aerodynamics")]
    [Tooltip("Torque that turns the tip into the airflow, like an arrow's fletching. Scales with speed. 0 = none.")]
    [Min(0f)] public float stabilization = 1.5f;
    [Tooltip("Extra quadratic air drag (force = k * v²), on top of Linear Damping.")]
    [Min(0f)] public float quadraticDrag = 0f;

    [Header("Surface")]
    [Tooltip("Create a physics material from the values below for all knife colliders.")]
    public bool applyPhysicsMaterial = true;
    [Range(0f, 1f)] public float bounciness = 0.2f;
    [Range(0f, 1f)] public float dynamicFriction = 0.4f;
    [Range(0f, 1f)] public float staticFriction = 0.5f;

    [Header("Hit")]
    public HitBehaviour hitBehaviour = HitBehaviour.StickWhenTipHits;
    [Tooltip("Minimum impact speed (m/s) for the tip to stick.")]
    [Min(0f)] public float minStickSpeed = 6f;
    [Tooltip("Maximum angle between the tip and the surface's inward normal for sticking. 90 = any angle that points into it.")]
    [Range(0f, 90f)] public float maxStickAngle = 60f;
    [Tooltip("How far the tip sinks into the surface when sticking.")]
    [Min(0f)] public float stickDepth = 0.08f;
    [Tooltip("Follow the hit object if it has a Rigidbody (e.g. a moving enemy).")]
    public bool stickToMovingTargets = true;

    [Header("Lifetime")]
    [Tooltip("Seconds after launch before an unstuck knife (flying, bounced or lying around) is removed.")]
    [Min(0.1f)] public float lifetime = 4f;
    [Tooltip("Seconds a stuck knife stays before it is removed.")]
    [Min(0f)] public float stuckLifetime = 2f;

    /// <summary>Raised for every collision before the knife sticks; hook damage here.</summary>
    public event Action<KaoruThrowingKnife, Collision> Hit;

    public GameObject Thrower { get; private set; }
    public bool IsStuck => stuck;
    public Rigidbody Body => body;

    private Rigidbody body;
    private Collider[] colliders;
    private PhysicsMaterial runtimeMaterial;
    private Vector3 tipLocal;
    private Vector3 lastVelocity;
    private Quaternion lastRotation;
    private float despawnTime = float.PositiveInfinity;
    private bool launched;
    private bool stuck;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        colliders = GetComponentsInChildren<Collider>();
        // The tip is the far end of the capsule; used to sink it on Stick.
        if (TryGetComponent(out CapsuleCollider capsule))
        {
            Vector3 axis = capsule.direction == 0 ? Vector3.right : capsule.direction == 1 ? Vector3.up : Vector3.forward;
            tipLocal = capsule.center + axis * (capsule.height * 0.5f);
        }
    }

    /// <summary>Applies the physics settings and throws the knife along <paramref name="direction"/>.</summary>
    public void Launch(Vector3 direction, GameObject thrower, IReadOnlyList<Collider> ignoredColliders,
        Vector3 inheritedVelocity = default)
    {
        if (direction.sqrMagnitude < 0.0001f)
            direction = transform.forward;
        direction.Normalize();
        Thrower = thrower;
        if (ignoredColliders != null)
            foreach (Collider own in colliders)
                foreach (Collider other in ignoredColliders)
                    if (other != null)
                        Physics.IgnoreCollision(own, other);
        ApplyPhysicsSettings();
        Quaternion rotation = Quaternion.LookRotation(direction, Vector3.up) * Quaternion.Euler(0f, 0f, bladeRoll);
        transform.rotation = rotation;
        body.rotation = rotation;
        body.linearVelocity = inheritedVelocity;
        body.angularVelocity = Vector3.zero;
        body.AddForce(direction * launchForce, launchForceMode);
        body.AddTorque(rotation * (launchSpin * Mathf.Deg2Rad), ForceMode.VelocityChange);
        lastRotation = rotation;
        lastVelocity = inheritedVelocity + direction * launchForce;
        despawnTime = Time.time + lifetime;
        launched = true;
    }

    private void ApplyPhysicsSettings()
    {
        body.isKinematic = false;
        // Gravity is applied in FixedUpdate so it can be scaled.
        body.useGravity = false;
        body.mass = mass;
        body.linearDamping = linearDamping;
        body.angularDamping = angularDamping;
        body.maxAngularVelocity = maxAngularVelocity;
        body.collisionDetectionMode = collisionDetection;
        body.interpolation = interpolation;
        body.constraints = RigidbodyConstraints.None;
        if (overrideCenterOfMass)
            body.centerOfMass = centerOfMass;
        else
            body.ResetCenterOfMass();
        if (!applyPhysicsMaterial)
            return;
        if (runtimeMaterial == null)
            runtimeMaterial = new PhysicsMaterial("KaoruKnife (runtime)");
        runtimeMaterial.bounciness = bounciness;
        runtimeMaterial.dynamicFriction = dynamicFriction;
        runtimeMaterial.staticFriction = staticFriction;
        runtimeMaterial.bounceCombine = PhysicsMaterialCombine.Maximum;
        foreach (Collider own in colliders)
            own.sharedMaterial = runtimeMaterial;
    }

    private void Update()
    {
        if (Time.time >= despawnTime)
            Destroy(gameObject);
    }

    private void FixedUpdate()
    {
        if (!launched || stuck)
            return;
        if (gravityScale != 0f)
            body.AddForce(Physics.gravity * gravityScale, ForceMode.Acceleration);
        Vector3 velocity = body.linearVelocity;
        float speed = velocity.magnitude;
        if (quadraticDrag > 0f && speed > 0.01f)
            body.AddForce(-velocity * (quadraticDrag * speed), ForceMode.Force);
        if (stabilization > 0f && speed > 0.5f)
        {
            // Cross product points along the axis that turns the tip toward the velocity.
            Vector3 correction = Vector3.Cross(transform.forward, velocity / speed);
            body.AddTorque(correction * (stabilization * speed), ForceMode.Acceleration);
        }
        // Pre-step state; OnCollisionEnter runs after the solver has already changed it.
        lastVelocity = body.linearVelocity;
        lastRotation = body.rotation;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!launched || stuck)
            return;
        Hit?.Invoke(this, collision);
        if (this == null)
            return;
        switch (hitBehaviour)
        {
            case HitBehaviour.Destroy:
                Destroy(gameObject);
                break;
            case HitBehaviour.StickWhenTipHits:
                if (CanStick(collision, out ContactPoint contact))
                    Stick(collision, contact);
                break;
        }
    }

    private bool CanStick(Collision collision, out ContactPoint contact)
    {
        contact = default;
        if (collision.contactCount == 0 || collision.relativeVelocity.magnitude < minStickSpeed)
            return false;
        contact = collision.GetContact(0);
        Vector3 tip = lastRotation * Vector3.forward;
        // The tip must lead the motion and point into the surface.
        return Vector3.Dot(tip, lastVelocity) > 0f && Vector3.Angle(tip, -contact.normal) <= maxStickAngle;
    }

    private void Stick(Collision collision, ContactPoint contact)
    {
        stuck = true;
        Quaternion rotation = lastRotation;
        Vector3 tip = rotation * Vector3.forward;
        transform.rotation = rotation;
        float tipDistance = Vector3.Dot(transform.TransformVector(tipLocal), tip);
        Vector3 position = contact.point - tip * (tipDistance - stickDepth);
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        body.isKinematic = true;
        body.position = position;
        body.rotation = rotation;
        transform.SetPositionAndRotation(position, rotation);
        foreach (Collider own in colliders)
            own.enabled = false;
        if (stickToMovingTargets && collision.rigidbody != null)
            transform.SetParent(collision.transform, true);
        despawnTime = Time.time + stuckLifetime;
    }

    private void OnDestroy()
    {
        if (runtimeMaterial != null)
            Destroy(runtimeMaterial);
    }

    private void OnDrawGizmosSelected()
    {
        if (!overrideCenterOfMass)
            return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.TransformPoint(centerOfMass), 0.02f);
    }
}
