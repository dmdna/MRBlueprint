using UnityEngine;
#if UNITY_EDITOR
using UnityEngine.XR;
#endif

/// <summary>
/// In the Editor, Meta / OpenXR often leaves the HMD pose at floor height when no headset is connected,
/// so the sandbox floor reads as a thin horizon line. Lifts <see cref="TrackingSpace"/> to standing eye height
/// only while the XR head device is not actually providing a pose. Player builds always skip this logic.
/// </summary>
public sealed class SandboxEditorTrackingSpaceHeight : MonoBehaviour
{
    [SerializeField] private float standingEyeHeightMeters = 1.55f;

    private Vector3 _initialLocalPosition;

    private void Awake()
    {
        _initialLocalPosition = transform.localPosition;
    }

    private void LateUpdate()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
            return;

        var head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
        var headTracked = head.isValid &&
            head.TryGetFeatureValue(CommonUsages.devicePosition, out var p) &&
            p.sqrMagnitude > 1e-6f;

        transform.localPosition = headTracked ? _initialLocalPosition : _initialLocalPosition + Vector3.up * standingEyeHeightMeters;
#endif
    }
}
