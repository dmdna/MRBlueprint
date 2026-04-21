using UnityEngine;

public enum PhysicsLensDriver
{
    None,
    Gravity,
    UserForce,
    Spring,
    HingeJoint,
    Impact,
    Friction,
    Other
}

public enum PhysicsLensGraphMode
{
    MotionTimeline,
    SpringPhaseRibbon,
    HingePhaseRibbon
}

public enum PhysicsLensConstraintKind
{
    None,
    Spring,
    Hinge
}

public enum PhysicsLensSpringState
{
    Compressing,
    NearRest,
    Stretching
}

public struct PhysicsLensCollisionEvent
{
    public bool IsValid;
    public float Time;
    public float ImpulseMagnitude;
    public float Restitution;
    public Vector3 Point;
    public string PartnerName;
}

public struct PhysicsLensForceEvent
{
    public bool IsValid;
    public float Time;
    public float Magnitude;
    public Vector3 Direction;
    public string Label;
    public bool IsImpulse;
}

public struct PhysicsLensConstraintSummary
{
    public PhysicsLensConstraintKind Kind;
    public bool IsValid;
    public string DisplayName;
    public int ConnectedConstraintCount;
    public string TopConstraintNameA;
    public float TopConstraintLoadA;
    public string TopConstraintNameB;
    public float TopConstraintLoadB;
    public float BreakRatio;

    public Vector3 WorldAnchorA;
    public Vector3 WorldAnchorB;
    public Vector3 AxisWorld;
    public float RestLength;
    public float CurrentLength;
    public float Extension;
    public float RelativeSpeed;
    public float SignedLoad;
    public float LoadMagnitude;
    public PhysicsLensSpringState SpringState;

    public float HingeAngle;
    public float HingeMinLimit;
    public float HingeMaxLimit;
    public bool HasHingeLimits;
    public float SignedAngularVelocityDeg;
    public float TorqueMagnitude;
    public float DistanceToLimit;
    public float NormalizedLimitProximity;

    public static PhysicsLensConstraintSummary None => new PhysicsLensConstraintSummary
    {
        Kind = PhysicsLensConstraintKind.None,
        DisplayName = "Free",
        DistanceToLimit = float.PositiveInfinity,
        BreakRatio = -1f
    };
}

public struct PhysicsLensSample
{
    public float Time;
    public Vector3 CenterOfMass;
    public Vector3 Velocity;
    public Vector3 AngularVelocity;
    public Vector3 Acceleration;
    public float Speed;
    public float AngularSpeedDeg;
    public float ApproxNetForce;
    public float Momentum;
    public float LinearKineticEnergy;
    public float PotentialEnergy;
    public float Mass;
    public bool GravityEnabled;
    public bool IsKinematic;
    public bool IsSleeping;
    public PhysicsLensDriver DominantDriver;
    public bool HasImpactEvent;
    public float ImpactImpulse;

    public float SpringExtension;
    public float SpringRelativeSpeed;
    public float SpringLoad;
    public float HingeAngle;
    public float HingeAngularVelocity;
    public float HingeTorque;
    public float HingeLimitProximity;
}

public static class PhysicsLensFormat
{
    public static string ShortNumber(float value, string suffix)
    {
        var abs = Mathf.Abs(value);
        if (abs >= 1000f)
            return (value / 1000f).ToString("0.0") + "k " + suffix;
        if (abs >= 100f)
            return value.ToString("0") + " " + suffix;
        if (abs >= 10f)
            return value.ToString("0.0") + " " + suffix;
        return value.ToString("0.00") + " " + suffix;
    }

    public static string SignedDistance(float value)
    {
        if (value > 0.005f)
            return "+" + value.ToString("0.00") + " m";
        if (value < -0.005f)
            return value.ToString("0.00") + " m";
        return "near rest";
    }

    public static string DriverLabel(PhysicsLensDriver driver)
    {
        switch (driver)
        {
            case PhysicsLensDriver.Gravity:
                return "Gravity";
            case PhysicsLensDriver.UserForce:
                return "User Force";
            case PhysicsLensDriver.Spring:
                return "Spring";
            case PhysicsLensDriver.HingeJoint:
                return "Hinge";
            case PhysicsLensDriver.Impact:
                return "Impact";
            case PhysicsLensDriver.Friction:
                return "Friction";
            case PhysicsLensDriver.Other:
                return "Other";
            default:
                return "Balanced";
        }
    }
}
