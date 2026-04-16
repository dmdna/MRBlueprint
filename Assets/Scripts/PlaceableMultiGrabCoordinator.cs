using System.Collections.Generic;
using UnityEngine;

public static class PlaceableMultiGrabCoordinator
{
    public const int LeftControllerSourceId = 12001;
    public const int RightControllerSourceId = 12002;
    public const int MXInkSourceId = 12003;

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

    public static bool TryBeginGrab(
        int sourceId,
        PlaceableAsset placeable,
        Vector3 origin,
        Vector3 direction,
        float hitDistance,
        float minDistance,
        float maxDistance)
    {
        if (placeable == null || direction.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        EndGrab(sourceId);

        direction.Normalize();
        var grabDistance = Mathf.Clamp(
            Mathf.Max(hitDistance, MinGrabDistance),
            Mathf.Max(MinGrabDistance, minDistance),
            Mathf.Max(MinGrabDistance, maxDistance));
        var grabPoint = origin + direction * grabDistance;
        var position = ResolvePosition(placeable);

        ActiveGrabs[sourceId] = new GrabState
        {
            SourceId = sourceId,
            Target = placeable,
            Distance = grabDistance,
            GrabPoint = grabPoint,
            Offset = position - grabPoint
        };

        CaptureMultiGrabIfNeeded(placeable);

        if (AssetSelectionManager.Instance != null
            && AssetSelectionManager.Instance.SelectedAsset != null)
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
        PruneInvalidGrabs();
        if (!ActiveGrabs.TryGetValue(sourceId, out var grab))
        {
            return;
        }

        if (grab.Target == null || direction.sqrMagnitude <= 0.0001f)
        {
            EndGrab(sourceId);
            return;
        }

        direction.Normalize();
        grab.Distance = Mathf.Clamp(
            grab.Distance + distanceDelta,
            Mathf.Max(MinGrabDistance, minDistance),
            Mathf.Max(MinGrabDistance, maxDistance));
        grab.GrabPoint = origin + direction * grab.Distance;

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

        if (_multiGrab.IsActive && _multiGrab.Target == target)
        {
            _multiGrab = default;
            RefreshOffsetsForTarget(target);
            CaptureMultiGrabIfNeeded(target);
        }
    }

    public static void EndAll()
    {
        ActiveGrabs.Clear();
        _multiGrab = default;
    }

    private static void ApplyMotion(PlaceableAsset target)
    {
        if (target == null)
        {
            return;
        }

        PruneInvalidGrabs();

        var grabCount = GetGrabCountForTarget(target, out var first, out var second);
        if (grabCount <= 0)
        {
            return;
        }

        if (grabCount >= 2 && second != null)
        {
            ApplyTwoPointGrab(target, first, second);
            return;
        }

        MovePlaceable(target, first.GrabPoint + first.Offset);
    }

    private static void ApplyTwoPointGrab(PlaceableAsset target, GrabState first, GrabState second)
    {
        if (!_multiGrab.IsActive
            || _multiGrab.Target != target
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

        target.SetScale(_multiGrab.InitialScale * scaleFactor);
        MovePlaceable(target, currentMidpoint + _multiGrab.InitialCenterOffset * scaleFactor);
        RefreshOffsetsForTarget(target);
    }

    private static void CaptureMultiGrabIfNeeded(PlaceableAsset target)
    {
        if (target == null)
        {
            return;
        }

        var grabCount = GetGrabCountForTarget(target, out var first, out var second);
        if (grabCount >= 2 && second != null)
        {
            CaptureMultiGrab(target, first, second);
        }
    }

    private static void CaptureMultiGrab(PlaceableAsset target, GrabState first, GrabState second)
    {
        var midpoint = (first.GrabPoint + second.GrabPoint) * 0.5f;
        _multiGrab = new MultiGrabState
        {
            IsActive = true,
            Target = target,
            SourceA = first.SourceId,
            SourceB = second.SourceId,
            InitialDistance = Mathf.Max(MinTwoPointDistance, Vector3.Distance(first.GrabPoint, second.GrabPoint)),
            InitialScale = target.GetScale(),
            InitialCenterOffset = ResolvePosition(target) - midpoint
        };
    }

    private static int GetGrabCountForTarget(
        PlaceableAsset target,
        out GrabState first,
        out GrabState second)
    {
        first = null;
        second = null;
        var count = 0;

        foreach (var grab in ActiveGrabs.Values)
        {
            if (grab.Target != target)
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

    private static void RefreshOffsetsForTarget(PlaceableAsset target)
    {
        if (target == null)
        {
            return;
        }

        var position = ResolvePosition(target);
        foreach (var grab in ActiveGrabs.Values)
        {
            if (grab.Target == target)
            {
                grab.Offset = position - grab.GrabPoint;
            }
        }
    }

    private static Vector3 ResolvePosition(PlaceableAsset placeable)
    {
        var rb = placeable.Rigidbody;
        return rb != null ? rb.position : placeable.GetPosition();
    }

    private static void MovePlaceable(PlaceableAsset placeable, Vector3 targetPosition)
    {
        var rb = placeable.Rigidbody;
        if (rb != null)
        {
            rb.position = targetPosition;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            return;
        }

        placeable.SetPosition(targetPosition);
    }

    private static void PruneInvalidGrabs()
    {
        ScratchSourceIds.Clear();
        foreach (var pair in ActiveGrabs)
        {
            if (pair.Value.Target == null)
            {
                ScratchSourceIds.Add(pair.Key);
            }
        }

        for (var i = 0; i < ScratchSourceIds.Count; i++)
        {
            ActiveGrabs.Remove(ScratchSourceIds[i]);
        }

        if (_multiGrab.IsActive && _multiGrab.Target == null)
        {
            _multiGrab = default;
        }
    }

    private sealed class GrabState
    {
        public int SourceId;
        public PlaceableAsset Target;
        public float Distance;
        public Vector3 GrabPoint;
        public Vector3 Offset;
    }

    private struct MultiGrabState
    {
        public bool IsActive;
        public PlaceableAsset Target;
        public int SourceA;
        public int SourceB;
        public float InitialDistance;
        public Vector3 InitialScale;
        public Vector3 InitialCenterOffset;
    }
}
