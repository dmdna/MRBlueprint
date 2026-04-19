using UnityEngine;

/// <summary>
/// Phase D4 — when a stroke is finalized into a <see cref="PhysicsDrawingSelectable"/>, applies intent to nearby
/// <see cref="PlaceableAsset"/> rigidbodies (flick → impulse, straight line → spring). Only runs in
/// <see cref="SandboxEditorSessionMode.Draw"/> and not during sandbox simulation.
/// </summary>
public static class SandboxStrokePlaceablePhysicsApplier
{
    private const float EndpointResolveRadius = 0.14f;
    private const float ImpulseStrengthMin = 1.2f;
    private const float ImpulseStrengthMax = 9f;
    private const float SpringMin = 8f;
    private const float SpringMax = 140f;
    private const float DamperMin = 0.6f;
    private const float DamperMax = 12f;

    /// <summary>Called from <see cref="PhysicsDrawingSelectable.Initialize"/> after geometry is built.</summary>
    public static void TryApplyFromDrawing(PhysicsDrawingSelectable drawing)
    {
        if (drawing == null)
            return;

        if (SandboxEditorModeState.Current != SandboxEditorSessionMode.Draw)
            return;

        var sim = SandboxSimulationController.Instance;
        if (sim != null && sim.IsSimulating)
            return;

        switch (drawing.PhysicsIntent)
        {
            case PhysicsIntentType.Impulse:
                TryApplyImpulse(drawing);
                break;
            case PhysicsIntentType.Spring:
                TryApplySpring(drawing);
                break;
        }
    }

    private static bool IsUserPlaceableRigidbody(Rigidbody rb)
    {
        if (rb == null)
            return false;
        if (rb.GetComponentInParent<PlaceableAsset>() == null)
            return false;
        return rb.GetComponentInParent<SpawnTemplateMarker>() == null;
    }

    private static bool TryResolvePlaceableAt(Vector3 worldPos, out Rigidbody rb)
    {
        rb = null;
        var hits = Physics.OverlapSphere(worldPos, EndpointResolveRadius, Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);
        var bestSq = float.MaxValue;
        for (var i = 0; i < hits.Length; i++)
        {
            var c = hits[i];
            var r = c.attachedRigidbody;
            if (!IsUserPlaceableRigidbody(r))
                continue;

            var p = r.transform.position;
            var d = (p - worldPos).sqrMagnitude;
            if (d < bestSq)
            {
                bestSq = d;
                rb = r;
            }
        }

        return rb != null;
    }

    private static void TryApplyImpulse(PhysicsDrawingSelectable drawing)
    {
        var positions = drawing.GetWorldLinePositions();
        if (positions.Length < 2)
            return;

        var start = positions[0];
        var end = positions[^1];
        var dir = end - start;
        if (dir.sqrMagnitude < 1e-8f)
            return;
        dir.Normalize();

        var mid = (start + end) * 0.5f;
        if (!TryResolvePlaceableAt(mid, out var rb))
            return;

        var mag = Mathf.Lerp(ImpulseStrengthMin, ImpulseStrengthMax, drawing.ImpulseForce);
        rb.AddForce(dir * mag, ForceMode.Impulse);
    }

    private static void TryApplySpring(PhysicsDrawingSelectable drawing)
    {
        var positions = drawing.GetWorldLinePositions();
        if (positions.Length < 2)
            return;

        var wStart = positions[0];
        var wEnd = positions[^1];
        if (!TryResolvePlaceableAt(wStart, out var rbA) || !TryResolvePlaceableAt(wEnd, out var rbB))
            return;
        if (rbA == rbB)
            return;

        var joint = rbA.gameObject.AddComponent<SpringJoint>();
        joint.connectedBody = rbB;
        joint.autoConfigureConnectedAnchor = false;
        joint.anchor = rbA.transform.InverseTransformPoint(wStart);
        joint.connectedAnchor = rbB.transform.InverseTransformPoint(wEnd);
        joint.spring = Mathf.Lerp(SpringMin, SpringMax, drawing.SpringStiffness);
        joint.damper = Mathf.Lerp(DamperMin, DamperMax, drawing.SpringStiffness);
        var rest = Vector3.Distance(wStart, wEnd);
        joint.minDistance = Mathf.Max(0.01f, rest * 0.85f);
        joint.maxDistance = Mathf.Max(joint.minDistance + 0.02f, rest * 1.25f);
        joint.enableCollision = false;
    }
}
