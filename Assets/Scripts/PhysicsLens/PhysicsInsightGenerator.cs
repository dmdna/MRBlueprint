using UnityEngine;

public static class PhysicsInsightGenerator
{
    public static PhysicsLensGraphMode ResolveGraphMode(PhysicsLensConstraintSummary constraint)
    {
        if (constraint.IsValid && constraint.Kind == PhysicsLensConstraintKind.Spring)
            return PhysicsLensGraphMode.SpringPhaseRibbon;
        if (constraint.IsValid && constraint.Kind == PhysicsLensConstraintKind.Hinge)
            return PhysicsLensGraphMode.HingePhaseRibbon;
        return PhysicsLensGraphMode.MotionTimeline;
    }

    public static string BuildInsight(
        PhysicsLensSample sample,
        PhysicsLensConstraintSummary constraint,
        ForceLedger ledger,
        PhysicsLensCollisionEvent latestImpact,
        PhysicsLensConfig config)
    {
        if (config != null
            && sample.Speed <= config.RestSpeedThreshold
            && sample.ApproxNetForce <= config.LowForceThreshold)
        {
            return "Object is at rest.";
        }

        if (latestImpact.IsValid
            && config != null
            && latestImpact.Restitution > 0.05f
            && Time.time - latestImpact.Time <= 0.45f)
        {
            return "Restitution is reflecting the impact.";
        }

        if (latestImpact.IsValid && config != null && Time.time - latestImpact.Time <= 0.35f)
            return "Recent impact is driving the motion.";

        if (constraint.IsValid && constraint.Kind == PhysicsLensConstraintKind.Hinge)
        {
            if (constraint.HasHingeLimits && constraint.NormalizedLimitProximity > 0.65f)
                return "Hinge is approaching its stop.";
            return Mathf.Abs(constraint.SignedAngularVelocityDeg) > 12f
                ? "Hinge motion is the dominant behavior."
                : "Hinge is guiding the object.";
        }

        if (constraint.IsValid && constraint.Kind == PhysicsLensConstraintKind.Spring)
        {
            if (Mathf.Abs(constraint.Extension) > 0.02f)
                return "Spring is storing energy.";

            if (Mathf.Abs(constraint.RelativeSpeed) > 0.08f)
                return "Damping is bleeding energy off.";

            return "Spring is holding near rest length.";
        }

        if (ledger != null && ledger.DominantDriver == PhysicsLensDriver.Gravity)
            return "Gravity is the dominant driver.";

        if (ledger != null && ledger.DominantDriver == PhysicsLensDriver.Friction)
            return "Friction is slowing the object down.";

        if (config != null
            && sample.Speed > config.RestSpeedThreshold * 3f
            && sample.ApproxNetForce <= config.LowForceThreshold * 2f)
        {
            return "Object is coasting with low net force.";
        }

        if (ledger != null && ledger.DominantDriver == PhysicsLensDriver.Impact)
            return "Impact is still the major contributor.";

        return "Motion is mostly balanced.";
    }

    public static string BuildLastEvent(
        PhysicsLensCollisionEvent latestImpact,
        PhysicsLensConstraintSummary constraint,
        ForceLedger ledger,
        PhysicsLensConfig config)
    {
        if (latestImpact.IsValid && config != null)
        {
            var age = Time.time - latestImpact.Time;
            if (age <= config.RecentImpactSeconds && latestImpact.Restitution > 0.05f)
            {
                return "Bounce: e " + latestImpact.Restitution.ToString("0.00")
                       + ", " + latestImpact.ImpulseMagnitude.ToString("0.0")
                       + " N*s, " + age.ToString("0.0") + "s ago";
            }

            if (age <= config.RecentImpactSeconds)
                return "Impact: " + latestImpact.ImpulseMagnitude.ToString("0.0") + " N*s, " + age.ToString("0.0") + "s ago";
        }

        if (ledger != null && ledger.UserForce > 0.01f)
            return "User force applied";

        if (ledger != null && ledger.Friction > 0.01f && ledger.DominantDriver == PhysicsLensDriver.Friction)
            return "Friction load is high";

        if (constraint.IsValid && constraint.Kind == PhysicsLensConstraintKind.Spring)
        {
            if (constraint.LoadMagnitude > 3f)
                return "Spring load spike";
        }

        if (constraint.IsValid
            && constraint.Kind == PhysicsLensConstraintKind.Hinge
            && constraint.HasHingeLimits
            && constraint.NormalizedLimitProximity > 0.75f)
        {
            return "Hinge near limit";
        }

        return "No recent event";
    }

    public static string BuildStateBadge(Rigidbody rb)
    {
        if (rb == null)
            return "Missing";
        if (rb.isKinematic)
            return "Kinematic";
        if (rb.IsSleeping())
            return "Sleeping";
        return "Dynamic";
    }

    public static string BuildConstraintLimitText(PhysicsLensConstraintSummary constraint)
    {
        if (!constraint.IsValid || constraint.Kind != PhysicsLensConstraintKind.Hinge)
            return "Free";
        if (!constraint.HasHingeLimits)
            return "Free";
        return constraint.DistanceToLimit.ToString("0.0") + " deg";
    }
}
