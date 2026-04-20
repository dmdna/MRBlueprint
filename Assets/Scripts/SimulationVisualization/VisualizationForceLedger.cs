using UnityEngine;

public sealed class VisualizationForceLedger
{
    public float Gravity { get; private set; }
    public float UserForce { get; private set; }
    public float Spring { get; private set; }
    public float Hinge { get; private set; }
    public float Impact { get; private set; }
    public float Friction { get; private set; }
    public float Other { get; private set; }
    public PhysicsLensDriver DominantDriver { get; private set; }

    public void Clear()
    {
        Gravity = 0f;
        UserForce = 0f;
        Spring = 0f;
        Hinge = 0f;
        Impact = 0f;
        Friction = 0f;
        Other = 0f;
        DominantDriver = PhysicsLensDriver.None;
    }

    public void Update(
        Rigidbody rb,
        PlaceableAsset asset,
        Vector3 approxNetForce,
        PhysicsLensConstraintSummary constraint,
        PhysicsLensCollisionEvent latestImpact,
        PhysicsLensForceEvent latestUserForce,
        VisualizationConfig config,
        float fixedDeltaTime)
    {
        Clear();

        if (rb == null || config == null)
            return;

        if (rb.useGravity && !rb.isKinematic)
            Gravity = rb.mass * Physics.gravity.magnitude;

        var recentWindow = config.RecentImpactSeconds;
        if (latestUserForce.IsValid && Time.time - latestUserForce.Time <= recentWindow)
        {
            UserForce = latestUserForce.IsImpulse
                ? latestUserForce.Magnitude / Mathf.Max(0.001f, fixedDeltaTime)
                : latestUserForce.Magnitude;
        }

        if (constraint.IsValid)
        {
            if (constraint.Kind == PhysicsLensConstraintKind.Spring)
                Spring = constraint.LoadMagnitude;
            else if (constraint.Kind == PhysicsLensConstraintKind.Hinge)
                Hinge = Mathf.Max(constraint.TorqueMagnitude, constraint.NormalizedLimitProximity);
        }

        if (latestImpact.IsValid && Time.time - latestImpact.Time <= recentWindow)
            Impact = latestImpact.ImpulseMagnitude / Mathf.Max(0.001f, fixedDeltaTime);

        if (asset != null && !rb.isKinematic && rb.linearVelocity.sqrMagnitude > 0.0001f)
        {
            var slip = Mathf.Clamp01(rb.linearVelocity.magnitude / 0.25f);
            Friction = rb.mass * Physics.gravity.magnitude * asset.GetDynamicFrictionCoefficient() * slip;
        }

        var known = Gravity + UserForce + Spring + Hinge + Impact + Friction;
        Other = Mathf.Max(0f, approxNetForce.magnitude - known);
        DominantDriver = ResolveDominant(config);
    }

    public float GetValue(PhysicsLensDriver driver)
    {
        switch (driver)
        {
            case PhysicsLensDriver.Gravity:
                return Gravity;
            case PhysicsLensDriver.UserForce:
                return UserForce;
            case PhysicsLensDriver.Spring:
                return Spring;
            case PhysicsLensDriver.HingeJoint:
                return Hinge;
            case PhysicsLensDriver.Impact:
                return Impact;
            case PhysicsLensDriver.Friction:
                return Friction;
            case PhysicsLensDriver.Other:
                return Other;
            default:
                return 0f;
        }
    }

    private PhysicsLensDriver ResolveDominant(VisualizationConfig config)
    {
        var best = config != null ? config.LowForceThreshold : 0.1f;
        var driver = PhysicsLensDriver.None;

        Consider(Gravity, PhysicsLensDriver.Gravity, ref best, ref driver);
        Consider(UserForce, PhysicsLensDriver.UserForce, ref best, ref driver);
        Consider(Spring, PhysicsLensDriver.Spring, ref best, ref driver);
        Consider(Hinge, PhysicsLensDriver.HingeJoint, ref best, ref driver);
        Consider(Impact, PhysicsLensDriver.Impact, ref best, ref driver);
        Consider(Friction, PhysicsLensDriver.Friction, ref best, ref driver);
        Consider(Other, PhysicsLensDriver.Other, ref best, ref driver);

        return driver;
    }

    private static void Consider(float value, PhysicsLensDriver candidate, ref float best, ref PhysicsLensDriver driver)
    {
        if (value <= best)
            return;

        best = value;
        driver = candidate;
    }
}
