using UnityEngine;
using UnityEngine.SpatialTracking;

[DisallowMultipleComponent]
public class MetaAnchorPoseRelay : MonoBehaviour
{
    [Header("Tracked Sources")]
    [SerializeField] private Transform _headSource;
    [SerializeField] private Transform _leftHandSource;
    [SerializeField] private Transform _rightHandSource;

    [Header("App Targets")]
    [SerializeField] private Transform _headTarget;
    [SerializeField] private Transform _leftHandTarget;
    [SerializeField] private Transform _rightHandTarget;

    [Header("Drivers To Disable")]
    [SerializeField] private TrackedPoseDriver _headPoseDriver;
    [SerializeField] private TrackedPoseDriver _leftHandPoseDriver;
    [SerializeField] private TrackedPoseDriver _rightHandPoseDriver;

    private void Awake()
    {
        DisablePoseDrivers();
        SyncTargets();
    }

    private void OnEnable()
    {
        DisablePoseDrivers();
        Application.onBeforeRender += HandleBeforeRender;
        SyncTargets();
    }

    private void OnDisable()
    {
        Application.onBeforeRender -= HandleBeforeRender;
    }

    private void LateUpdate()
    {
        SyncTargets();
    }

    private void HandleBeforeRender()
    {
        SyncTargets();
    }

    private void DisablePoseDrivers()
    {
        DisablePoseDriver(_headPoseDriver);
        DisablePoseDriver(_leftHandPoseDriver);
        DisablePoseDriver(_rightHandPoseDriver);
    }

    private void SyncTargets()
    {
        SyncTransform(_headSource, _headTarget);
        SyncTransform(_leftHandSource, _leftHandTarget);
        SyncTransform(_rightHandSource, _rightHandTarget);
    }

    private static void DisablePoseDriver(TrackedPoseDriver poseDriver)
    {
        if (poseDriver != null && poseDriver.enabled)
        {
            poseDriver.enabled = false;
        }
    }

    private static void SyncTransform(Transform source, Transform target)
    {
        if (source == null || target == null)
        {
            return;
        }

        target.SetPositionAndRotation(source.position, source.rotation);
    }
}
