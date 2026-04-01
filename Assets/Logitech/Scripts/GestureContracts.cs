using System.Collections.Generic;
using UnityEngine;

public enum GestureType
{
    Unknown,
    Line,
    Flick,
    Boundary
}

public enum PhysicsIntentType
{
    Unknown,
    Spring,
    Impulse,
    Hinge,
    Boundary
}

public struct StrokePoint
{
    public Vector3 Position;
    public float Pressure;
    public float Timestamp;
}

public class StrokeData
{
    public List<StrokePoint> Points = new List<StrokePoint>();
    public float Duration;
    public float AveragePressure;
}

public class GestureResult
{
    public GestureType Type;
    public StrokeData Stroke;
    public Vector3 Direction;
    public float Confidence;
}

public class TargetResolutionResult
{
    public Rigidbody PrimaryObject;
    public Rigidbody SecondaryObject;
    public Vector3 HitPoint;
}

public class PhysicsGestureReadoutResult
{
    public GestureResult Gesture;
    public PhysicsIntentType PhysicsIntent;
    public string ShapeName;
    public string Summary;
}
