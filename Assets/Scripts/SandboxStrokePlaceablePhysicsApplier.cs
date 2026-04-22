using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Arms physics drawings when sandbox simulation starts. Edit-mode drawings only describe intent; this class
/// turns attached springs, impulses, and hinges into runtime force drivers and removes them on exit/restart.
/// </summary>
public static class SandboxStrokePlaceablePhysicsApplier
{
    private const float EndpointResolveRadius = 0.14f;
    private const float SegmentResolveRadius = 0.08f;
    private const float ImpulseInstantMin = 1.2f;
    private const float ImpulseInstantMax = 9f;
    private const float ImpulseContinuousMin = 2f;
    private const float ImpulseContinuousMax = 28f;
    private const float SpringForceMin = 10f;
    private const float SpringForceMax = 180f;
    private const float SpringDamperMin = 0.8f;
    private const float SpringDamperMax = 16f;
    private const float HingeStiffnessMin = 80f;
    private const float HingeStiffnessMax = 680f;
    private const float HingeDamperMin = 8f;
    private const float HingeDamperMax = 42f;
    private const float HingeTorqueEstimateMin = 0.15f;
    private const float HingeTorqueEstimateMax = 4.5f;

    private static readonly List<SandboxDrawingPhysicsRuntime> ActiveRuntimes = new();

    public static void ActivateAllDrawingPhysics()
    {
        DeactivateAllDrawingPhysics();

        var drawings = Object.FindObjectsByType<PhysicsDrawingSelectable>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        for (var i = 0; i < drawings.Length; i++)
        {
            TryActivateDrawing(drawings[i]);
        }
    }

    public static void DeactivateAllDrawingPhysics()
    {
        for (var i = ActiveRuntimes.Count - 1; i >= 0; i--)
        {
            DestroyRuntime(ActiveRuntimes[i]);
        }

        ActiveRuntimes.Clear();

        var orphaned = Object.FindObjectsByType<SandboxDrawingPhysicsRuntime>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (var i = 0; i < orphaned.Length; i++)
        {
            DestroyRuntime(orphaned[i]);
        }
    }

    public static float ResolveImpulseStrength(float normalized, bool instant)
    {
        return instant
            ? Mathf.Lerp(ImpulseInstantMin, ImpulseInstantMax, Mathf.Clamp01(normalized))
            : Mathf.Lerp(ImpulseContinuousMin, ImpulseContinuousMax, Mathf.Clamp01(normalized));
    }

    public static float ResolveSpringStrength(float normalized)
    {
        return Mathf.Lerp(SpringForceMin, SpringForceMax, Mathf.Clamp01(normalized));
    }

    public static float ResolveSpringDamper(float normalized)
    {
        return Mathf.Lerp(SpringDamperMin, SpringDamperMax, Mathf.Clamp01(normalized));
    }

    public static float ResolveHingeTetherStiffness(float normalized)
    {
        return Mathf.Lerp(HingeStiffnessMin, HingeStiffnessMax, Mathf.Clamp01(normalized));
    }

    public static float ResolveHingeDamper(float normalized)
    {
        return Mathf.Lerp(HingeDamperMin, HingeDamperMax, Mathf.Clamp01(normalized));
    }

    public static float ResolveHingeTorqueEstimate(float normalized)
    {
        return Mathf.Lerp(HingeTorqueEstimateMin, HingeTorqueEstimateMax, Mathf.Clamp01(normalized));
    }

    private static void TryActivateDrawing(PhysicsDrawingSelectable drawing)
    {
        if (drawing == null || !drawing.isActiveAndEnabled)
        {
            return;
        }

        switch (drawing.PhysicsIntent)
        {
            case PhysicsIntentType.Spring:
                TryActivateSpring(drawing);
                break;
            case PhysicsIntentType.Impulse:
                TryActivateImpulse(drawing);
                break;
            case PhysicsIntentType.Hinge:
                TryActivateHinge(drawing);
                break;
        }
    }

    private static void TryActivateSpring(PhysicsDrawingSelectable drawing)
    {
        var positions = drawing.GetWorldLinePositions();
        if (positions.Length < 2)
        {
            return;
        }

        if (!TryResolveSpringAnchor(drawing, PhysicsDrawingEndpoint.Start, positions[0], out var start)
            || !TryResolveSpringAnchor(
                drawing,
                PhysicsDrawingEndpoint.End,
                positions[positions.Length - 1],
                out var end)
            || start.Body == end.Body)
        {
            return;
        }

        var runtime = drawing.gameObject.AddComponent<SandboxDrawingPhysicsRuntime>();
        runtime.ConfigureSpring(
            drawing,
            start.Body,
            start.LocalPoint,
            end.Body,
            end.LocalPoint,
            ResolveSpringStrength(drawing.SpringStiffness),
            ResolveSpringDamper(drawing.SpringStiffness));
        ActiveRuntimes.Add(runtime);
    }

    private static void TryActivateImpulse(PhysicsDrawingSelectable drawing)
    {
        if (!TryResolveImpulseAnchor(
                drawing,
                out var body,
                out var localPoint,
                out var direction,
                out var directionFollowsBody))
        {
            return;
        }

        var strength = ResolveImpulseStrength(drawing.ImpulseForce, drawing.ImpulseInstant);
        var runtime = drawing.gameObject.AddComponent<SandboxDrawingPhysicsRuntime>();
        runtime.ConfigureImpulse(
            drawing,
            body,
            localPoint,
            direction,
            strength,
            drawing.ImpulseInstant,
            directionFollowsBody);
        ActiveRuntimes.Add(runtime);
    }

    private static void TryActivateHinge(PhysicsDrawingSelectable drawing)
    {
        if (!drawing.TryGetHingeAttachment(
                out var placeable,
                out var pivot,
                out var bodyPoint,
                out var stringLength)
            || !TryGetUserPlaceableRigidbody(placeable, out var body))
        {
            return;
        }

        var runtime = drawing.gameObject.AddComponent<SandboxDrawingPhysicsRuntime>();
        runtime.ConfigureHinge(
            drawing,
            body,
            body.transform.InverseTransformPoint(bodyPoint),
            pivot,
            stringLength,
            ResolveHingeTetherStiffness(drawing.HingeTorque),
            ResolveHingeDamper(drawing.HingeTorque));
        ActiveRuntimes.Add(runtime);
    }

    private static bool TryResolveSpringAnchor(
        PhysicsDrawingSelectable drawing,
        PhysicsDrawingEndpoint endpoint,
        Vector3 fallbackPoint,
        out RuntimeAnchor anchor)
    {
        anchor = default;
        if (drawing.TryGetSpringEndpointAttachment(endpoint, out var placeable, out var worldPoint)
            && TryGetUserPlaceableRigidbody(placeable, out var attachedBody))
        {
            anchor = new RuntimeAnchor(attachedBody, attachedBody.transform.InverseTransformPoint(worldPoint));
            return true;
        }

        return TryResolvePlaceableAt(fallbackPoint, EndpointResolveRadius, out anchor);
    }

    private static bool TryResolveImpulseAnchor(
        PhysicsDrawingSelectable drawing,
        out Rigidbody body,
        out Vector3 localPoint,
        out Vector3 direction,
        out bool directionFollowsBody)
    {
        body = null;
        localPoint = default;
        direction = default;
        directionFollowsBody = false;
        if (drawing.TryGetImpulseAttachment(out var placeable, out var junction, out direction)
            && TryGetUserPlaceableRigidbody(placeable, out body))
        {
            localPoint = body.transform.InverseTransformPoint(junction);
            directionFollowsBody = true;
            return true;
        }

        var positions = drawing.GetWorldLinePositions();
        if (positions.Length < 2)
        {
            return false;
        }

        var start = positions[0];
        var end = positions[positions.Length - 1];
        direction = end - start;
        if (direction.sqrMagnitude <= 0.000001f)
        {
            return false;
        }

        direction.Normalize();
        if (!TryResolvePlaceableAlongSegment(start, end, out var anchor))
        {
            return false;
        }

        body = anchor.Body;
        localPoint = anchor.LocalPoint;
        return true;
    }

    private static bool TryResolvePlaceableAlongSegment(
        Vector3 start,
        Vector3 end,
        out RuntimeAnchor anchor)
    {
        anchor = default;
        if (TryResolvePlaceableAt(start, EndpointResolveRadius, out anchor)
            || TryResolvePlaceableAt(end, EndpointResolveRadius, out anchor)
            || TryResolvePlaceableAt((start + end) * 0.5f, EndpointResolveRadius, out anchor))
        {
            return true;
        }

        var segment = end - start;
        var length = segment.magnitude;
        if (length <= 0.0001f)
        {
            return false;
        }

        var hits = Physics.SphereCastAll(
            start,
            SegmentResolveRadius,
            segment / length,
            length,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);
        var bestDistance = float.MaxValue;
        RuntimeAnchor best = default;
        var hasBest = false;
        for (var i = 0; i < hits.Length; i++)
        {
            var collider = hits[i].collider;
            if (!TryGetUserPlaceableRigidbody(collider, out var body))
            {
                continue;
            }

            var point = hits[i].point;
            if (point == Vector3.zero)
            {
                point = ClosestPointOnSegment(start, end, body.worldCenterOfMass);
            }

            if (hits[i].distance >= bestDistance)
            {
                continue;
            }

            hasBest = true;
            bestDistance = hits[i].distance;
            best = new RuntimeAnchor(body, body.transform.InverseTransformPoint(point));
        }

        if (!hasBest)
        {
            return false;
        }

        anchor = best;
        return true;
    }

    private static bool TryResolvePlaceableAt(
        Vector3 worldPos,
        float radius,
        out RuntimeAnchor anchor)
    {
        anchor = default;
        var hits = Physics.OverlapSphere(
            worldPos,
            radius,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);
        var bestSq = float.MaxValue;
        var hasBest = false;
        for (var i = 0; i < hits.Length; i++)
        {
            var collider = hits[i];
            if (!TryGetUserPlaceableRigidbody(collider, out var body))
            {
                continue;
            }

            var placeable = body.GetComponentInParent<PlaceableAsset>();
            var hasVisualSurface = PlaceableSurfaceUtility.TryGetClosestVisibleMeshPoint(
                placeable,
                worldPos,
                out var visualSurface);
            if (!hasVisualSurface
                && !PlaceableSurfaceUtility.TryGetClosestColliderPoint(
                    collider,
                    worldPos,
                    out visualSurface))
            {
                continue;
            }

            var point = visualSurface.Point;
            if ((point - worldPos).sqrMagnitude <= 0.00000025f)
            {
                point = worldPos;
            }

            var distanceSq = (point - worldPos).sqrMagnitude;
            if (distanceSq > radius * radius)
            {
                continue;
            }

            if (distanceSq >= bestSq)
            {
                continue;
            }

            hasBest = true;
            bestSq = distanceSq;
            anchor = new RuntimeAnchor(body, body.transform.InverseTransformPoint(point));
        }

        return hasBest;
    }

    private static bool TryGetUserPlaceableRigidbody(
        PlaceableAsset placeable,
        out Rigidbody body)
    {
        body = placeable != null ? placeable.Rigidbody : null;
        return IsUserPlaceableRigidbody(body);
    }

    private static bool TryGetUserPlaceableRigidbody(
        Collider collider,
        out Rigidbody body)
    {
        body = collider != null ? collider.attachedRigidbody : null;
        return IsUserPlaceableRigidbody(body);
    }

    private static bool IsUserPlaceableRigidbody(Rigidbody body)
    {
        return body != null
               && body.GetComponentInParent<PlaceableAsset>() != null
               && body.GetComponentInParent<SpawnTemplateMarker>() == null;
    }

    private static void DestroyRuntime(SandboxDrawingPhysicsRuntime runtime)
    {
        if (runtime == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Object.Destroy(runtime);
        }
        else
        {
            Object.DestroyImmediate(runtime);
        }
    }

    private static Vector3 ClosestPointOnSegment(Vector3 start, Vector3 end, Vector3 point)
    {
        var segment = end - start;
        var lengthSq = segment.sqrMagnitude;
        if (lengthSq <= 0.00000001f)
        {
            return start;
        }

        var t = Vector3.Dot(point - start, segment) / lengthSq;
        return start + segment * Mathf.Clamp01(t);
    }

    private readonly struct RuntimeAnchor
    {
        public readonly Rigidbody Body;
        public readonly Vector3 LocalPoint;

        public RuntimeAnchor(Rigidbody body, Vector3 localPoint)
        {
            Body = body;
            LocalPoint = localPoint;
        }
    }
}
