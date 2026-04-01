using System.Collections.Generic;
using UnityEngine;

public class GestureInterpreter : MonoBehaviour
{
    private const int CircleSegmentCount = 48;

    [SerializeField] private float _minimumStrokeDistance = 0.03f;
    [SerializeField] private float _flickSpeedThreshold = 1.2f;
    [SerializeField] private float _straightnessThreshold = 1.2f;
    [SerializeField] private float _closedLoopThreshold = 0.04f;
    [SerializeField] private float _loopEligibilityDistanceMultiplier = 2.5f;
    [SerializeField] private float _circleRoundnessThreshold = 0.35f;

    public float ClosedLoopThreshold => _closedLoopThreshold;

    public GestureResult Classify(StrokeData stroke)
    {
        var result = new GestureResult
        {
            Type = GestureType.Unknown,
            Stroke = stroke,
            Direction = Vector3.zero,
            Confidence = 0f
        };

        if (stroke == null || stroke.Points == null || stroke.Points.Count < 2)
        {
            return result;
        }

        var start = stroke.Points[0].Position;
        var end = stroke.Points[stroke.Points.Count - 1].Position;
        var chordLength = Vector3.Distance(start, end);
        var pathLength = CalculatePathLength(stroke);
        var duration = Mathf.Max(stroke.Duration, 0.0001f);
        var speed = pathLength / duration;

        result.Direction = (end - start).sqrMagnitude > 0f ? (end - start).normalized : Vector3.zero;

        if (pathLength < _minimumStrokeDistance)
        {
            return result;
        }

        var straightness = chordLength > 0.0001f ? pathLength / chordLength : float.MaxValue;

        if (speed >= _flickSpeedThreshold && straightness <= _straightnessThreshold)
        {
            result.Type = GestureType.Flick;
            result.Confidence = Mathf.Clamp01((speed - _flickSpeedThreshold) / _flickSpeedThreshold + 0.5f);
            return result;
        }

        if (IsClosedLoop(stroke, chordLength, pathLength))
        {
            result.Type = GestureType.Boundary;
            result.Confidence = CalculateLoopConfidence(stroke, chordLength, pathLength);
            return result;
        }

        if (straightness <= _straightnessThreshold)
        {
            result.Type = GestureType.Line;
            result.Confidence = Mathf.Clamp01(1f - ((straightness - 1f) / Mathf.Max(_straightnessThreshold - 1f, 0.001f)));
            return result;
        }

        return result;
    }

    public PhysicsGestureReadoutResult BuildReadout(StrokeData stroke)
    {
        var gesture = Classify(stroke);
        var readout = new PhysicsGestureReadoutResult
        {
            Gesture = gesture,
            PhysicsIntent = PhysicsIntentType.Unknown,
            ShapeName = "Unknown",
            Summary = "Unknown stroke -> no physics mapping yet"
        };

        switch (gesture.Type)
        {
            case GestureType.Line:
                readout.PhysicsIntent = PhysicsIntentType.Spring;
                readout.ShapeName = "Line";
                readout.Summary = $"Line -> Spring (confidence {gesture.Confidence:0.00})";
                break;
            case GestureType.Flick:
                readout.PhysicsIntent = PhysicsIntentType.Impulse;
                readout.ShapeName = "Flick";
                readout.Summary = $"Flick -> Impulse (confidence {gesture.Confidence:0.00})";
                break;
            case GestureType.Boundary:
                if (LooksLikeCircle(stroke))
                {
                    readout.PhysicsIntent = PhysicsIntentType.Hinge;
                    readout.ShapeName = "Circle";
                    readout.Summary = $"Circle -> Hinge (confidence {gesture.Confidence:0.00})";
                }
                else
                {
                    readout.PhysicsIntent = PhysicsIntentType.Boundary;
                    readout.ShapeName = "Boundary";
                    readout.Summary = $"Boundary -> Boundary physics (confidence {gesture.Confidence:0.00})";
                }
                break;
        }

        readout.DisplayPoints = BuildDisplayPoints(stroke, readout);
        return readout;
    }

    public bool WouldClassifyAsClosedLoop(IReadOnlyList<StrokePoint> points)
    {
        if (points == null || points.Count < 2)
        {
            return false;
        }

        var start = points[0].Position;
        var end = points[points.Count - 1].Position;
        var chordLength = Vector3.Distance(start, end);
        var pathLength = CalculatePathLength(points);
        return IsClosedLoopCandidate(chordLength, pathLength);
    }

    private float CalculatePathLength(StrokeData stroke)
    {
        return CalculatePathLength(stroke.Points);
    }

    private bool IsClosedLoop(StrokeData stroke, float chordLength, float pathLength)
    {
        return IsClosedLoopCandidate(chordLength, pathLength);
    }

    private float CalculateLoopConfidence(StrokeData stroke, float chordLength, float pathLength)
    {
        var closureScore = 1f - Mathf.Clamp01(chordLength / Mathf.Max(_closedLoopThreshold, 0.001f));
        var pathScore = Mathf.Clamp01(pathLength / (GetLoopEligibilityDistance() * 2f));
        return Mathf.Clamp01((closureScore + pathScore) * 0.5f);
    }

    private bool LooksLikeCircle(StrokeData stroke)
    {
        if (stroke == null || stroke.Points == null || stroke.Points.Count < 5)
        {
            return false;
        }

        var center = Vector3.zero;
        for (var i = 0; i < stroke.Points.Count; i++)
        {
            center += stroke.Points[i].Position;
        }

        center /= stroke.Points.Count;

        var radiusSum = 0f;
        for (var i = 0; i < stroke.Points.Count; i++)
        {
            radiusSum += Vector3.Distance(center, stroke.Points[i].Position);
        }

        var averageRadius = radiusSum / stroke.Points.Count;
        if (averageRadius <= 0.0001f)
        {
            return false;
        }

        var variance = 0f;
        for (var i = 0; i < stroke.Points.Count; i++)
        {
            var radius = Vector3.Distance(center, stroke.Points[i].Position);
            variance += Mathf.Pow(radius - averageRadius, 2f);
        }

        var normalizedStdDev = Mathf.Sqrt(variance / stroke.Points.Count) / averageRadius;
        return normalizedStdDev <= _circleRoundnessThreshold;
    }

    private List<Vector3> BuildDisplayPoints(StrokeData stroke, PhysicsGestureReadoutResult readout)
    {
        if (stroke == null || stroke.Points == null || stroke.Points.Count == 0)
        {
            return new List<Vector3>();
        }

        switch (readout.ShapeName)
        {
            case "Line":
            case "Flick":
                return BuildStraightDisplayPoints(stroke);
            case "Circle":
                return BuildCircleDisplayPoints(stroke);
            case "Boundary":
                return BuildClosedBoundaryDisplayPoints(stroke);
            default:
                return BuildRawDisplayPoints(stroke);
        }
    }

    private List<Vector3> BuildRawDisplayPoints(StrokeData stroke)
    {
        var points = new List<Vector3>(stroke.Points.Count);
        for (var i = 0; i < stroke.Points.Count; i++)
        {
            points.Add(stroke.Points[i].Position);
        }

        return points;
    }

    private List<Vector3> BuildStraightDisplayPoints(StrokeData stroke)
    {
        var points = new List<Vector3>(2)
        {
            stroke.Points[0].Position,
            stroke.Points[stroke.Points.Count - 1].Position
        };

        return points;
    }

    private List<Vector3> BuildClosedBoundaryDisplayPoints(StrokeData stroke)
    {
        var points = BuildRawDisplayPoints(stroke);
        if (points.Count == 0)
        {
            return points;
        }

        var start = points[0];
        var end = points[points.Count - 1];
        if (Vector3.Distance(start, end) > 0.0001f)
        {
            points.Add(start);
        }

        return points;
    }

    private List<Vector3> BuildCircleDisplayPoints(StrokeData stroke)
    {
        var center = Vector3.zero;
        for (var i = 0; i < stroke.Points.Count; i++)
        {
            center += stroke.Points[i].Position;
        }

        center /= stroke.Points.Count;

        var normal = CalculateLoopNormal(stroke, center);
        var tangent = stroke.Points[0].Position - center;
        tangent -= Vector3.Dot(tangent, normal) * normal;

        if (tangent.sqrMagnitude <= 0.000001f)
        {
            tangent = Vector3.Cross(normal, Vector3.up);
            if (tangent.sqrMagnitude <= 0.000001f)
            {
                tangent = Vector3.Cross(normal, Vector3.right);
            }
        }

        tangent.Normalize();
        var bitangent = Vector3.Cross(normal, tangent).normalized;

        var radius = 0f;
        for (var i = 0; i < stroke.Points.Count; i++)
        {
            radius += Vector3.Distance(center, stroke.Points[i].Position);
        }

        radius /= stroke.Points.Count;

        var points = new List<Vector3>(CircleSegmentCount + 1);
        for (var i = 0; i <= CircleSegmentCount; i++)
        {
            var angle = (Mathf.PI * 2f * i) / CircleSegmentCount;
            var offset = (Mathf.Cos(angle) * tangent + Mathf.Sin(angle) * bitangent) * radius;
            points.Add(center + offset);
        }

        return points;
    }

    private Vector3 CalculateLoopNormal(StrokeData stroke, Vector3 center)
    {
        var normal = Vector3.zero;
        for (var i = 0; i < stroke.Points.Count; i++)
        {
            var current = stroke.Points[i].Position - center;
            var next = stroke.Points[(i + 1) % stroke.Points.Count].Position - center;
            normal += Vector3.Cross(current, next);
        }

        if (normal.sqrMagnitude <= 0.000001f)
        {
            var first = stroke.Points[0].Position;
            var mid = stroke.Points[stroke.Points.Count / 2].Position;
            var last = stroke.Points[stroke.Points.Count - 1].Position;
            normal = Vector3.Cross(mid - first, last - first);
        }

        if (normal.sqrMagnitude <= 0.000001f)
        {
            return Vector3.forward;
        }

        return normal.normalized;
    }

    private float CalculatePathLength(IReadOnlyList<StrokePoint> points)
    {
        var length = 0f;
        for (var i = 1; i < points.Count; i++)
        {
            length += Vector3.Distance(points[i - 1].Position, points[i].Position);
        }

        return length;
    }

    private bool IsClosedLoopCandidate(float chordLength, float pathLength)
    {
        return chordLength <= _closedLoopThreshold && pathLength >= GetLoopEligibilityDistance();
    }

    private float GetLoopEligibilityDistance()
    {
        return Mathf.Max(_minimumStrokeDistance, _closedLoopThreshold * _loopEligibilityDistanceMultiplier);
    }
}
