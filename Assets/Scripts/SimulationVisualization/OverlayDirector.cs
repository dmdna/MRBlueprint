using UnityEngine;

public sealed class OverlayDirector
{
    private readonly ForceArrowOverlay _forceArrows = new ForceArrowOverlay();
    private readonly VelocityRibbonOverlay _velocityRibbons = new VelocityRibbonOverlay();
    private readonly ImpactMarkerOverlay _impactMarkers = new ImpactMarkerOverlay();
    private readonly SpringOverlayController _springs = new SpringOverlayController();
    private readonly HingeOverlayController _hinges = new HingeOverlayController();

    private GameObject _root;
    private Transform _bodyRoot;
    private Transform _constraintRoot;
    private Transform _eventRoot;

    public void Initialize(Transform parent, VisualizationConfig config)
    {
        _root = new GameObject("SimulationVisualizationOverlays");
        _root.transform.SetParent(parent, false);

        _bodyRoot = CreateChild(_root.transform, "BodyOverlays");
        _constraintRoot = CreateChild(_root.transform, "ConstraintOverlays");
        _eventRoot = CreateChild(_root.transform, "EventOverlays");

        _forceArrows.Initialize(_bodyRoot, config);
        _velocityRibbons.Initialize(_bodyRoot, config);
        _impactMarkers.Initialize(_eventRoot, config);
        _springs.Initialize(_constraintRoot, config);
        _hinges.Initialize(_constraintRoot, config);
    }

    public void Dispose()
    {
        _forceArrows.Dispose();
        _velocityRibbons.Dispose();
        _impactMarkers.Dispose();
        _springs.Dispose();
        _hinges.Dispose();

        if (_root != null)
            Object.Destroy(_root);

        _root = null;
        _bodyRoot = null;
        _constraintRoot = null;
        _eventRoot = null;
    }

    public void SetVisible(bool visible)
    {
        if (_root != null && _root.activeSelf != visible)
            _root.SetActive(visible);
    }

    public void RefreshConstraints(BodyTelemetryTracker[] trackers, int trackerCount, VisualizationConfig config)
    {
        _springs.RefreshTargets(trackers, trackerCount, config);
        _hinges.RefreshTargets(trackers, trackerCount, config);
    }

    public void EmitImpact(Vector3 point, float impulse, VisualizationConfig config)
    {
        _impactMarkers.Emit(point, impulse, config);
    }

    public void Render(
        BodyTelemetryTracker[] trackers,
        int trackerCount,
        Rigidbody selectedBody,
        Camera camera,
        VisualizationConfig config)
    {
        if (_root == null || trackers == null || config == null)
            return;

        _forceArrows.BeginFrame();
        _velocityRibbons.BeginFrame();

        if (selectedBody != null)
        {
            var selectedTracker = FindTracker(trackers, trackerCount, selectedBody);
            if (selectedTracker != null)
                RenderBody(selectedTracker, true, config, camera);
        }

        for (var i = 0; i < trackerCount; i++)
        {
            var tracker = trackers[i];
            if (tracker == null || !tracker.IsValid || tracker.Rigidbody == selectedBody)
                continue;

            if (tracker.Importance < config.FocusImportanceThreshold)
                continue;

            RenderBody(tracker, false, config, camera);
        }

        _forceArrows.EndFrame();
        _velocityRibbons.EndFrame();
        _springs.Render(config, selectedBody);
        _hinges.Render(config, selectedBody, camera);
        _impactMarkers.Update(camera, config);
    }

    public void HideAll()
    {
        _forceArrows.HideAll();
        _velocityRibbons.HideAll();
        _impactMarkers.HideAll();
        _springs.HideAll();
        _hinges.HideAll();
    }

    private void RenderBody(BodyTelemetryTracker tracker, bool focus, VisualizationConfig config, Camera camera)
    {
        _forceArrows.Render(tracker, focus, config, camera);
        _velocityRibbons.Render(tracker, focus, config);
    }

    private static BodyTelemetryTracker FindTracker(BodyTelemetryTracker[] trackers, int trackerCount, Rigidbody body)
    {
        if (body == null)
            return null;

        for (var i = 0; i < trackerCount; i++)
        {
            if (trackers[i] != null && trackers[i].Rigidbody == body)
                return trackers[i];
        }

        return null;
    }

    private static Transform CreateChild(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.transform;
    }
}
