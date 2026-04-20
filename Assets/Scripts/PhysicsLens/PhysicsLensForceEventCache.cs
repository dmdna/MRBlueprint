using UnityEngine;

[DisallowMultipleComponent]
public sealed class PhysicsLensForceEventCache : MonoBehaviour
{
    public PhysicsLensForceEvent LatestUserForce { get; private set; }

    public static void ReportUserImpulse(Rigidbody rb, Vector3 impulse)
    {
        ReportUserForce(rb, impulse, true);
    }

    public static void ReportUserForce(Rigidbody rb, Vector3 forceOrImpulse, bool isImpulse)
    {
        if ((!PhysicsLensManager.RuntimeAvailable && !SimulationVisualizationManager.RuntimeAvailable) || rb == null)
            return;

        var magnitude = forceOrImpulse.magnitude;
        if (magnitude <= 0.0001f)
            return;

        var cache = rb.GetComponent<PhysicsLensForceEventCache>();
        if (cache == null)
            cache = rb.gameObject.AddComponent<PhysicsLensForceEventCache>();

        cache.LatestUserForce = new PhysicsLensForceEvent
        {
            IsValid = true,
            Time = Time.time,
            Magnitude = magnitude,
            Direction = forceOrImpulse / magnitude,
            Label = "User force",
            IsImpulse = isImpulse
        };
    }
}
