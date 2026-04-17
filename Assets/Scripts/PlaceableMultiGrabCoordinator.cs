using System.Collections.Generic;
using UnityEngine;

public static class PlaceableMultiGrabCoordinator
{
    public const int LeftControllerSourceId = 12001;
    public const int RightControllerSourceId = 12002;
    public const int MXInkSourceId = 12003;
    public const int DirectStylusSourceId = 12004;

    private const float MinGrabDistance = 0.01f;
    private const float MinTwoPointDistance = 0.001f;
    private const float MinScaleFactor = 0.05f;
    private const float MaxScaleFactor = 20f;

    private static readonly Dictionary<int, GrabState> ActiveGrabs = new();
    private static readonly List<int> ScratchSourceIds = new();

    private static MultiGrabState _multiGrab;

    public static bool AnyGrabActive
    {
        get
        {
            PruneInvalidGrabs();
            return ActiveGrabs.Count > 0;
        }
    }

    public static bool IsSourceGrabbing(int sourceId)
    {
        PruneInvalidGrabs();
        return ActiveGrabs.ContainsKey(sourceId);
    }

    public static bool IsSourceGrabbingPhysicsDrawing(int sourceId)
    {
        PruneInvalidGrabs();
        return ActiveGrabs.TryGetValue(sourceId, out var grab)
               && grab.Target != null
               && grab.Target.IsDrawing;
    }

    public static bool TryGetSourceGrabPoint(int sourceId, out Vector3 grabPoint)
    {
        PruneInvalidGrabs();
        if (ActiveGrabs.TryGetValue(sourceId, out var grab))
        {
            grabPoint = grab.GrabPoint;
            return true;
        }

        grabPoint = default;
        return false;
    }

    public static bool TryGetSourceGrabDistance(int sourceId, out float distance)
    {
        PruneInvalidGrabs();
        if (ActiveGrabs.TryGetValue(sourceId, out var grab))
        {
            distance = grab.Distance;
            return true;
        }

        distance = default;
        return false;
    }

    public static bool IsSourceDirectGrab(int sourceId)
    {
        PruneInvalidGrabs();
        return ActiveGrabs.TryGetValue(sourceId, out var grab) && grab.IsDirect;
    }

    public static bool TryBeginGrab(
        int sourceId,
        PlaceableAsset placeable,
        Vector3 origin,
        Vector3 direction,
        float hitDistance,
        float minDistance,
        float maxDistance)
    {
        return TryBeginGrab(
            sourceId,
            GrabTarget.FromPlaceable(placeable),
            origin,
            direction,
            Quaternion.identity,
            false,
            hitDistance,
            minDistance,
            maxDistance);
    }

    public static bool TryBeginGrab(
        int sourceId,
        PhysicsDrawingSelectable drawing,
        Vector3 origin,
        Vector3 direction,
        Quaternion sourceRotation,
        float hitDistance,
        float minDistance,
        float maxDistance)
    {
        return TryBeginGrab(
            sourceId,
            GrabTarget.FromDrawing(drawing),
            origin,
            direction,
            sourceRotation,
            true,
            hitDistance,
            minDistance,
            maxDistance);
    }

    public static bool TryBeginDirectGrab(
        int sourceId,
        PlaceableAsset placeable,
        Vector3 grabPoint)
    {
        return TryBeginDirectGrab(
            sourceId,
            GrabTarget.FromPlaceable(placeable),
            grabPoint,
            Quaternion.identity,
            false);
    }

    public static bool TryBeginDirectGrab(
        int sourceId,
        PhysicsDrawingSelectable drawing,
        Vector3 grabPoint,
        Quaternion sourceRotation)
    {
        return TryBeginDirectGrab(
            sourceId,
            GrabTarget.FromDrawing(drawing),
            grabPoint,
            sourceRotation,
            true);
    }

    private static bool TryBeginDirectGrab(
        int sourceId,
        GrabTarget target,
        Vector3 grabPoint,
        Quaternion sourceRotation,
        bool useSourceRotation)
    {
        if (target == null || !target.IsValid)
        {
            return false;
        }

        EndGrab(sourceId);

        var position = ResolvePosition(target);
        ActiveGrabs[sourceId] = new GrabState
        {
            SourceId = sourceId,
            Target = target,
            IsDirect = true,
            Distance = 0f,
            GrabPoint = grabPoint,
            Offset = position - grabPoint,
            SourceRotation = sourceRotation,
            InitialSourceRotation = sourceRotation,
            InitialGrabPoint = grabPoint,
            InitialWorldPositions = useSourceRotation && target.SupportsSourceRotation
                ? target.GetWorldLinePositions()
                : null
        };

        CaptureMultiGrabIfNeeded(target);

        if (AssetSelectionManager.Instance != null && AssetSelectionManager.Instance.HasSelection)
        {
            AssetSelectionManager.Instance.ClearSelection();
        }

        return true;
    }

    private static bool TryBeginGrab(
        int sourceId,
        GrabTarget target,
        Vector3 origin,
        Vector3 direction,
        Quaternion sourceRotation,
        bool useSourceRotation,
        float hitDistance,
        float minDistance,
        float maxDistance)
    {
        if (target == null || !target.IsValid || direction.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        EndGrab(sourceId);

        direction.Normalize();
        var minimumDistance = target.IsDrawing
            ? MinGrabDistance
            : Mathf.Max(MinGrabDistance, minDistance);
        var grabDistance = Mathf.Clamp(
            Mathf.Max(hitDistance, MinGrabDistance),
            minimumDistance,
            Mathf.Max(MinGrabDistance, maxDistance));
        var grabPoint = origin + direction * grabDistance;
        var position = ResolvePosition(target);

        ActiveGrabs[sourceId] = new GrabState
        {
            SourceId = sourceId,
            Target = target,
            Distance = grabDistance,
            GrabPoint = grabPoint,
            Offset = position - grabPoint,
            SourceRotation = sourceRotation,
            InitialSourceRotation = sourceRotation,
            InitialGrabPoint = grabPoint,
            InitialWorldPositions = useSourceRotation && target.SupportsSourceRotation
                ? target.GetWorldLinePositions()
                : null
        };

        CaptureMultiGrabIfNeeded(target);

        if (AssetSelectionManager.Instance != null && AssetSelectionManager.Instance.HasSelection)
        {
            AssetSelectionManager.Instance.ClearSelection();
        }

        return true;
    }

    public static void UpdateGrab(
        int sourceId,
        Vector3 origin,
        Vector3 direction,
        float distanceDelta,
        float minDistance,
        float maxDistance)
    {
        UpdateGrab(sourceId, origin, direction, Quaternion.identity, false, distanceDelta, minDistance, maxDistance);
    }

    public static void UpdateGrab(
        int sourceId,
        Vector3 origin,
        Vector3 direction,
        Quaternion sourceRotation,
        float distanceDelta,
        float minDistance,
        float maxDistance)
    {
        UpdateGrab(sourceId, origin, direction, sourceRotation, true, distanceDelta, minDistance, maxDistance);
    }

    private static void UpdateGrab(
        int sourceId,
        Vector3 origin,
        Vector3 direction,
        Quaternion sourceRotation,
        bool useSourceRotation,
        float distanceDelta,
        float minDistance,
        float maxDistance)
    {
        PruneInvalidGrabs();
        if (!ActiveGrabs.TryGetValue(sourceId, out var grab))
        {
            return;
        }

        if (grab.Target == null || !grab.Target.IsValid || direction.sqrMagnitude <= 0.0001f)
        {
            EndGrab(sourceId);
            return;
        }

        if (grab.IsDirect)
        {
            grab.GrabPoint = origin;
            if (useSourceRotation)
            {
                grab.SourceRotation = sourceRotation;
            }

            ApplyMotion(grab.Target);
            return;
        }

        direction.Normalize();
        var minimumDistance = grab.Target.IsDrawing
            ? MinGrabDistance
            : Mathf.Max(MinGrabDistance, minDistance);
        grab.Distance = Mathf.Clamp(
            grab.Distance + distanceDelta,
            minimumDistance,
            Mathf.Max(MinGrabDistance, maxDistance));
        grab.GrabPoint = origin + direction * grab.Distance;
        if (useSourceRotation)
        {
            grab.SourceRotation = sourceRotation;
        }

        ApplyMotion(grab.Target);
    }

    public static void UpdateDirectGrab(int sourceId, Vector3 grabPoint)
    {
        UpdateDirectGrab(sourceId, grabPoint, Quaternion.identity, false);
    }

    public static void UpdateDirectGrab(int sourceId, Vector3 grabPoint, Quaternion sourceRotation)
    {
        UpdateDirectGrab(sourceId, grabPoint, sourceRotation, true);
    }

    private static void UpdateDirectGrab(
        int sourceId,
        Vector3 grabPoint,
        Quaternion sourceRotation,
        bool useSourceRotation)
    {
        PruneInvalidGrabs();
        if (!ActiveGrabs.TryGetValue(sourceId, out var grab))
        {
            return;
        }

        if (grab.Target == null || !grab.Target.IsValid)
        {
            EndGrab(sourceId);
            return;
        }

        grab.GrabPoint = grabPoint;
        if (useSourceRotation)
        {
            grab.SourceRotation = sourceRotation;
        }

        ApplyMotion(grab.Target);
    }

    public static void EndGrab(int sourceId)
    {
        PruneInvalidGrabs();
        if (!ActiveGrabs.TryGetValue(sourceId, out var grab))
        {
            return;
        }

        var target = grab.Target;
        ActiveGrabs.Remove(sourceId);

        if (_multiGrab.IsActive && _multiGrab.Target != null && _multiGrab.Target.SameAs(target))
        {
            _multiGrab = default;
            RefreshOffsetsForTarget(target);
            RefreshSourceRotationSnapshotsForTarget(target);
            CaptureMultiGrabIfNeeded(target);
        }
    }

    public static void EndAll()
    {
        ActiveGrabs.Clear();
        _multiGrab = default;
    }

    private static void ApplyMotion(GrabTarget target)
    {
        if (target == null || !target.IsValid)
        {
            return;
        }

        PruneInvalidGrabs();

        var grabCount = GetGrabCountForTarget(target, out var first, out var second);
        if (grabCount <= 0)
        {
            return;
        }

        if (grabCount >= 2 && second != null && target.SupportsScale)
        {
            ApplyTwoPointGrab(target, first, second);
            return;
        }

        if (target.SupportsSourceRotation && first.HasSourceRotationSnapshot)
        {
            ApplySourceRotationGrab(target, first);
            return;
        }

        if (grabCount >= 2 && second != null)
        {
            MoveTarget(target, ((first.GrabPoint + first.Offset) + (second.GrabPoint + second.Offset)) * 0.5f);
            return;
        }

        MoveTarget(target, first.GrabPoint + first.Offset);
    }

    private static void ApplySourceRotationGrab(GrabTarget target, GrabState grab)
    {
        if (grab.InitialWorldPositions == null || grab.InitialWorldPositions.Length == 0)
        {
            MoveTarget(target, grab.GrabPoint + grab.Offset);
            return;
        }

        var rotation = grab.SourceRotation * Quaternion.Inverse(grab.InitialSourceRotation);
        var positions = new Vector3[grab.InitialWorldPositions.Length];
        for (var i = 0; i < positions.Length; i++)
        {
            positions[i] = rotation * (grab.InitialWorldPositions[i] - grab.InitialGrabPoint) + grab.GrabPoint;
        }

        target.SetWorldLinePositions(positions);
    }

    private static void ApplyTwoPointGrab(GrabTarget target, GrabState first, GrabState second)
    {
        if (!_multiGrab.IsActive
            || _multiGrab.Target == null
            || !_multiGrab.Target.SameAs(target)
            || _multiGrab.SourceA != first.SourceId
            || _multiGrab.SourceB != second.SourceId)
        {
            CaptureMultiGrab(target, first, second);
        }

        var currentDistance = Vector3.Distance(first.GrabPoint, second.GrabPoint);
        var scaleFactor = Mathf.Clamp(
            currentDistance / Mathf.Max(MinTwoPointDistance, _multiGrab.InitialDistance),
            MinScaleFactor,
            MaxScaleFactor);
        var currentMidpoint = (first.GrabPoint + second.GrabPoint) * 0.5f;

        if (target.SupportsWorldLineScaling && _multiGrab.InitialWorldPositions != null)
        {
            var rotation = ResolveGrabRotation(_multiGrab.InitialGrabVector, second.GrabPoint - first.GrabPoint);
            var lineTargetCenter = currentMidpoint + rotation * (_multiGrab.InitialCenterOffset * scaleFactor);
            target.SetTransformedWorldLinePositions(
                _multiGrab.InitialWorldPositions,
                lineTargetCenter,
                rotation,
                scaleFactor);
            RefreshOffsetsForTarget(target);
            return;
        }

        var targetCenter = currentMidpoint + _multiGrab.InitialCenterOffset * scaleFactor;
        target.SetScale(_multiGrab.InitialScale * scaleFactor);
        MoveTarget(target, targetCenter);
        RefreshOffsetsForTarget(target);
    }

    private static void CaptureMultiGrabIfNeeded(GrabTarget target)
    {
        if (target == null || !target.IsValid || !target.SupportsScale)
        {
            return;
        }

        var grabCount = GetGrabCountForTarget(target, out var first, out var second);
        if (grabCount >= 2 && second != null)
        {
            CaptureMultiGrab(target, first, second);
        }
    }

    private static void CaptureMultiGrab(GrabTarget target, GrabState first, GrabState second)
    {
        var midpoint = (first.GrabPoint + second.GrabPoint) * 0.5f;
        _multiGrab = new MultiGrabState
        {
            IsActive = true,
            Target = target,
            SourceA = first.SourceId,
            SourceB = second.SourceId,
            InitialDistance = Mathf.Max(MinTwoPointDistance, Vector3.Distance(first.GrabPoint, second.GrabPoint)),
            InitialGrabVector = second.GrabPoint - first.GrabPoint,
            InitialScale = target.GetScale(),
            InitialCenterOffset = ResolvePosition(target) - midpoint,
            InitialWorldPositions = target.SupportsWorldLineScaling
                ? target.GetWorldLinePositions()
                : null
        };
    }

    private static Quaternion ResolveGrabRotation(Vector3 initialVector, Vector3 currentVector)
    {
        if (initialVector.sqrMagnitude <= 0.000001f || currentVector.sqrMagnitude <= 0.000001f)
        {
            return Quaternion.identity;
        }

        return Quaternion.FromToRotation(initialVector.normalized, currentVector.normalized);
    }

    private static int GetGrabCountForTarget(
        GrabTarget target,
        out GrabState first,
        out GrabState second)
    {
        first = null;
        second = null;
        var count = 0;

        foreach (var grab in ActiveGrabs.Values)
        {
            if (grab.Target == null || !grab.Target.SameAs(target))
            {
                continue;
            }

            count++;
            if (first == null)
            {
                first = grab;
            }
            else if (second == null)
            {
                second = grab;
            }
        }

        return count;
    }

    private static void RefreshOffsetsForTarget(GrabTarget target)
    {
        if (target == null || !target.IsValid)
        {
            return;
        }

        var position = ResolvePosition(target);
        foreach (var grab in ActiveGrabs.Values)
        {
            if (grab.Target != null && grab.Target.SameAs(target))
            {
                grab.Offset = position - grab.GrabPoint;
            }
        }
    }

    private static void RefreshSourceRotationSnapshotsForTarget(GrabTarget target)
    {
        if (target == null || !target.IsValid || !target.SupportsSourceRotation)
        {
            return;
        }

        foreach (var grab in ActiveGrabs.Values)
        {
            if (grab.Target == null || !grab.Target.SameAs(target))
            {
                continue;
            }

            grab.InitialSourceRotation = grab.SourceRotation;
            grab.InitialGrabPoint = grab.GrabPoint;
            grab.InitialWorldPositions = target.GetWorldLinePositions();
        }
    }

    private static Vector3 ResolvePosition(GrabTarget target)
    {
        return target.ResolvePosition();
    }

    private static void MoveTarget(GrabTarget target, Vector3 targetPosition)
    {
        target.MoveTo(targetPosition);
    }

    private static void PruneInvalidGrabs()
    {
        ScratchSourceIds.Clear();
        foreach (var pair in ActiveGrabs)
        {
            if (pair.Value.Target == null || !pair.Value.Target.IsValid)
            {
                ScratchSourceIds.Add(pair.Key);
            }
        }

        for (var i = 0; i < ScratchSourceIds.Count; i++)
        {
            ActiveGrabs.Remove(ScratchSourceIds[i]);
        }

        if (_multiGrab.IsActive && (_multiGrab.Target == null || !_multiGrab.Target.IsValid))
        {
            _multiGrab = default;
        }
    }

    private sealed class GrabState
    {
        public int SourceId;
        public GrabTarget Target;
        public bool IsDirect;
        public float Distance;
        public Vector3 GrabPoint;
        public Vector3 Offset;
        public Quaternion SourceRotation;
        public Quaternion InitialSourceRotation;
        public Vector3 InitialGrabPoint;
        public Vector3[] InitialWorldPositions;

        public bool HasSourceRotationSnapshot =>
            InitialWorldPositions != null && InitialWorldPositions.Length > 0;
    }

    private struct MultiGrabState
    {
        public bool IsActive;
        public GrabTarget Target;
        public int SourceA;
        public int SourceB;
        public float InitialDistance;
        public Vector3 InitialGrabVector;
        public Vector3 InitialScale;
        public Vector3 InitialCenterOffset;
        public Vector3[] InitialWorldPositions;
    }

    private sealed class GrabTarget
    {
        private readonly PlaceableAsset _placeable;
        private readonly PhysicsDrawingSelectable _drawing;

        private GrabTarget(PlaceableAsset placeable, PhysicsDrawingSelectable drawing)
        {
            _placeable = placeable;
            _drawing = drawing;
        }

        public bool IsValid => _placeable != null || _drawing != null;
        public bool IsDrawing => _drawing != null;
        public bool SupportsScale => _placeable != null || SupportsWorldLineScaling;
        public bool SupportsWorldLineScaling => _drawing != null && _drawing.SupportsRadiusScaling;
        public bool SupportsSourceRotation => _drawing != null;

        public static GrabTarget FromPlaceable(PlaceableAsset placeable)
        {
            return placeable != null ? new GrabTarget(placeable, null) : null;
        }

        public static GrabTarget FromDrawing(PhysicsDrawingSelectable drawing)
        {
            return drawing != null ? new GrabTarget(null, drawing) : null;
        }

        public bool SameAs(GrabTarget other)
        {
            if (other == null)
            {
                return false;
            }

            if (_placeable != null)
            {
                return other._placeable == _placeable;
            }

            return _drawing != null && other._drawing == _drawing;
        }

        public Vector3 ResolvePosition()
        {
            if (_placeable != null)
            {
                var rb = _placeable.Rigidbody;
                return rb != null ? rb.position : _placeable.GetPosition();
            }

            return _drawing != null ? _drawing.GetGrabPosition() : Vector3.zero;
        }

        public Vector3 GetScale()
        {
            return _placeable != null ? _placeable.GetScale() : Vector3.one;
        }

        public Vector3[] GetWorldLinePositions()
        {
            return _drawing != null ? _drawing.GetWorldLinePositions() : null;
        }

        public void SetWorldLinePositions(IReadOnlyList<Vector3> positions)
        {
            _drawing?.SetWorldLinePositions(positions);
        }

        public void SetScale(Vector3 scale)
        {
            if (_placeable != null)
            {
                _placeable.SetScale(scale);
            }
        }

        public void SetScaledWorldLinePositions(
            IReadOnlyList<Vector3> positions,
            Vector3 targetCenter,
            float scaleFactor)
        {
            _drawing?.SetScaledWorldLinePositions(positions, targetCenter, scaleFactor);
        }

        public void SetTransformedWorldLinePositions(
            IReadOnlyList<Vector3> positions,
            Vector3 targetCenter,
            Quaternion rotation,
            float scaleFactor)
        {
            _drawing?.SetTransformedWorldLinePositions(positions, targetCenter, rotation, scaleFactor);
        }

        public void MoveTo(Vector3 targetPosition)
        {
            if (_placeable != null)
            {
                var rb = _placeable.Rigidbody;
                if (rb != null)
                {
                    rb.position = targetPosition;
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                    return;
                }

                _placeable.SetPosition(targetPosition);
                return;
            }

            _drawing?.SetGrabPosition(targetPosition);
        }
    }
}
