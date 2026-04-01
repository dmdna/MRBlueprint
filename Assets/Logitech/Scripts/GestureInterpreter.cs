using UnityEngine;

public class GestureInterpreter : MonoBehaviour
{
    [SerializeField] private float _minimumStrokeDistance = 0.03f;
    [SerializeField] private float _flickSpeedThreshold = 1.2f;
    [SerializeField] private float _straightnessThreshold = 1.2f;
    [SerializeField] private float _closedLoopThreshold = 0.04f;
    [SerializeField] private float _circleRoundnessThreshold = 0.35f;

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

        return readout;
    }

    private float CalculatePathLength(StrokeData stroke)
    {
        var length = 0f;
        for (var i = 1; i < stroke.Points.Count; i++)
        {
            length += Vector3.Distance(stroke.Points[i - 1].Position, stroke.Points[i].Position);
        }

        return length;
    }

    private bool IsClosedLoop(StrokeData stroke, float chordLength, float pathLength)
    {
        return chordLength <= _closedLoopThreshold && pathLength >= _minimumStrokeDistance;
    }

    private float CalculateLoopConfidence(StrokeData stroke, float chordLength, float pathLength)
    {
        var closureScore = 1f - Mathf.Clamp01(chordLength / Mathf.Max(_closedLoopThreshold, 0.001f));
        var pathScore = Mathf.Clamp01(pathLength / (_minimumStrokeDistance * 4f));
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
}
