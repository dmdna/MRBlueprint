using UnityEngine;

public sealed class ForceLedger
{
    public float Gravity { get; private set; }
    public float UserForce { get; private set; }
    public float Spring { get; private set; }
    public float Hinge { get; private set; }
    public float Impact { get; private set; }
    public float Other { get; private set; }
    public PhysicsLensDriver DominantDriver { get; private set; }

    public void Clear()
    {
        Gravity = 0f;
        UserForce = 0f;
        Spring = 0f;
        Hinge = 0f;
        Impact = 0f;
        Other = 0f;
        DominantDriver = PhysicsLensDriver.None;
    }

    public void Update(
        Rigidbody rb,
        float approxNetForce,
        PhysicsLensConstraintSummary constraint,
        PhysicsLensCollisionEvent latestImpact,
        PhysicsLensForceEvent latestUserForce,
        PhysicsLensConfig config,
        float fixedDeltaTime)
    {
        Clear();

        if (rb == null || config == null)
            return;

        if (rb.useGravity && !rb.isKinematic)
            Gravity = rb.mass * Physics.gravity.magnitude;

        var recentWindow = config != null ? config.RecentImpactSeconds : 2f;
        if (latestUserForce.IsValid && Time.time - latestUserForce.Time <= recentWindow)
            UserForce = latestUserForce.IsImpulse
                ? latestUserForce.Magnitude / Mathf.Max(0.001f, fixedDeltaTime)
                : latestUserForce.Magnitude;

        if (constraint.IsValid)
        {
            if (constraint.Kind == PhysicsLensConstraintKind.Spring)
                Spring = constraint.LoadMagnitude;
            else if (constraint.Kind == PhysicsLensConstraintKind.Hinge)
                Hinge = Mathf.Max(constraint.TorqueMagnitude, constraint.NormalizedLimitProximity);
        }

        if (latestImpact.IsValid && Time.time - latestImpact.Time <= config.RecentImpactSeconds)
        {
            Impact = latestImpact.ImpulseMagnitude / Mathf.Max(0.001f, fixedDeltaTime);
        }

        var known = Gravity + UserForce + Spring + Impact;
        Other = Mathf.Max(0f, approxNetForce - known);
        if (constraint.Kind == PhysicsLensConstraintKind.Hinge)
            Other = Mathf.Max(0f, Other - Hinge);

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
            case PhysicsLensDriver.Other:
                return Other;
            default:
                return 0f;
        }
    }

    public float MaxValue()
    {
        var max = Gravity;
        if (UserForce > max)
            max = UserForce;
        if (Spring > max)
            max = Spring;
        if (Hinge > max)
            max = Hinge;
        if (Impact > max)
            max = Impact;
        if (Other > max)
            max = Other;
        return max;
    }

    private PhysicsLensDriver ResolveDominant(PhysicsLensConfig config)
    {
        var threshold = config != null ? config.LowForceThreshold : 0.1f;
        var best = threshold;
        var driver = PhysicsLensDriver.None;

        Consider(Gravity, PhysicsLensDriver.Gravity, ref best, ref driver);
        Consider(UserForce, PhysicsLensDriver.UserForce, ref best, ref driver);
        Consider(Spring, PhysicsLensDriver.Spring, ref best, ref driver);
        Consider(Hinge, PhysicsLensDriver.HingeJoint, ref best, ref driver);
        Consider(Impact, PhysicsLensDriver.Impact, ref best, ref driver);
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
