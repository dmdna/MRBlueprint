using UnityEngine;

[DisallowMultipleComponent]
public sealed class MXInkInputAdapter : MonoBehaviour
{
    [SerializeField] private StylusHandler stylusHandler;
    [SerializeField, Range(0.01f, 1f)] private float middlePressThreshold = 0.1f;

    public StylusHandler Stylus => stylusHandler;
    public bool HasStylus => stylusHandler != null;
    public bool MiddleButtonPressed => HasStylus
                                       && stylusHandler.CurrentState.cluster_middle_value >= middlePressThreshold;
    public bool TrackingAvailable
    {
        get
        {
            if (!HasStylus)
            {
                return false;
            }

            if (stylusHandler is VrStylusHandler vrStylus)
            {
                return vrStylus.IsTrackingStylus;
            }

            var state = stylusHandler.CurrentState;
            return state.isActive
                   && (state.positionIsValid || state.positionIsTracked || IsFinite(state.inkingPose.position));
        }
    }

    public Pose TipPose
    {
        get
        {
            if (stylusHandler is VrStylusHandler vrStylus && vrStylus.TipTransform != null)
            {
                return new Pose(vrStylus.TipTransform.position, vrStylus.TipTransform.rotation);
            }

            return HasStylus ? stylusHandler.CurrentState.inkingPose : new Pose(transform.position, transform.rotation);
        }
    }

    private void Awake()
    {
        ResolveStylus();
    }

    public void Configure(StylusHandler stylus)
    {
        if (stylus != null)
        {
            stylusHandler = stylus;
        }
    }

    public void ResolveStylus()
    {
        if (stylusHandler != null)
        {
            return;
        }

        stylusHandler = FindFirstObjectByType<StylusHandler>(FindObjectsInactive.Include);
    }

    private static bool IsFinite(Vector3 value)
    {
        return float.IsFinite(value.x)
               && float.IsFinite(value.y)
               && float.IsFinite(value.z);
    }
}
