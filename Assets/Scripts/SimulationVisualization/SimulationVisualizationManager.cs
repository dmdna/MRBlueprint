using UnityEngine;

public sealed class SimulationVisualizationManager : MonoBehaviour
{
    [SerializeField] private VisualizationConfig config;

    private readonly OverlayDirector _director = new OverlayDirector();
    private BodyTelemetryTracker[] _trackers;
    private SandboxSimulationController _simulation;
    private AssetSelectionManager _selection;
    private PhysicsLensConfig _lensConfig;
    private int _trackerCount;
    private float _nextBodyScanTime;
    private float _nextConstraintScanTime;
    private bool _active;
    private bool _directorReady;
    private bool _selectionSubscribed;
    private bool _simulationSubscribed;

    public static bool RuntimeAvailable { get; private set; }

    private void Awake()
    {
        if (config == null)
            config = Resources.Load<VisualizationConfig>("SimulationVisualizationConfig");
        if (config == null)
            config = VisualizationConfig.CreateRuntimeDefault();

        _lensConfig = Resources.Load<PhysicsLensConfig>("PhysicsLensConfig");
        if (_lensConfig == null)
            _lensConfig = PhysicsLensConfig.CreateRuntimeDefault();

        RuntimeAvailable = config != null && config.FeatureEnabled;
    }

    private void Start()
    {
        if (config == null || !config.FeatureEnabled)
        {
            RuntimeAvailable = false;
            enabled = false;
            return;
        }

        EnsureTrackerPool();
        EnsureDirector();
        _director.SetVisible(false);
        ResolveDependencies();
        EvaluateActivation();
    }

    private void OnDestroy()
    {
        if (_selectionSubscribed && _selection != null)
        {
            _selection.OnSelectionChanged -= OnSelectionChanged;
            _selection.OnPhysicsDrawingSelectionChanged -= OnPhysicsDrawingSelectionChanged;
        }

        if (_simulationSubscribed && _simulation != null)
            _simulation.StateChanged -= OnSimulationStateChanged;

        _director.Dispose();
        RuntimeAvailable = false;
    }

    private void Update()
    {
        if (config == null || !config.FeatureEnabled)
            return;

        ResolveDependencies();
        EvaluateActivation();
        if (!_active)
            return;

        if (Time.unscaledTime >= _nextBodyScanTime)
        {
            RefreshBodies();
            _nextBodyScanTime = Time.unscaledTime + config.BodyRescanSeconds;
        }

        if (Time.unscaledTime >= _nextConstraintScanTime)
        {
            _director.RefreshConstraints(_trackers, _trackerCount, config);
            _nextConstraintScanTime = Time.unscaledTime + config.ConstraintRescanSeconds;
        }

        var camera = ResolveActiveCamera();
        var selectedBody = ResolveSelectedRigidbody();
        for (var i = 0; i < _trackerCount; i++)
        {
            var tracker = _trackers[i];
            if (tracker == null || !tracker.IsValid)
                continue;

            tracker.RefreshImportance(camera, tracker.Rigidbody == selectedBody, config);
        }

        _director.Render(_trackers, _trackerCount, selectedBody, camera, config);
    }

    private void FixedUpdate()
    {
        if (!_active || _simulation == null || _simulation.IsPaused)
            return;

        for (var i = 0; i < _trackerCount; i++)
        {
            var tracker = _trackers[i];
            if (tracker == null || !tracker.IsValid)
                continue;

            if (!tracker.Sample(Time.fixedDeltaTime, config))
                continue;

            if (tracker.HasNewImpact && tracker.LatestImpact.IsValid)
                _director.EmitImpact(tracker.LatestImpact.Point, tracker.LatestImpact.ImpulseMagnitude, config);
        }
    }

    private void ResolveDependencies()
    {
        if (_simulation == null)
            _simulation = SandboxSimulationController.Instance != null
                ? SandboxSimulationController.Instance
                : Object.FindFirstObjectByType<SandboxSimulationController>();

        if (_selection == null)
            _selection = AssetSelectionManager.Instance != null
                ? AssetSelectionManager.Instance
                : Object.FindFirstObjectByType<AssetSelectionManager>();

        if (!_simulationSubscribed && _simulation != null)
        {
            _simulation.StateChanged += OnSimulationStateChanged;
            _simulationSubscribed = true;
        }

        if (!_selectionSubscribed && _selection != null)
        {
            _selection.OnSelectionChanged += OnSelectionChanged;
            _selection.OnPhysicsDrawingSelectionChanged += OnPhysicsDrawingSelectionChanged;
            _selectionSubscribed = true;
        }
    }

    private void EvaluateActivation()
    {
        var shouldBeActive = _simulation != null && _simulation.IsSimulating;
        if (shouldBeActive == _active)
            return;

        if (shouldBeActive)
            EnterVisualization();
        else
            ExitVisualization();
    }

    private void EnterVisualization()
    {
        EnsureTrackerPool();
        EnsureDirector();
        _active = true;
        RuntimeAvailable = true;
        _director.SetVisible(true);
        _nextBodyScanTime = 0f;
        _nextConstraintScanTime = 0f;
        RefreshBodies();
        _director.RefreshConstraints(_trackers, _trackerCount, config);
    }

    private void ExitVisualization()
    {
        _active = false;
        RuntimeAvailable = config != null && config.FeatureEnabled;
        if (_trackers != null)
        {
            for (var i = 0; i < _trackerCount; i++)
                _trackers[i].Clear(_lensConfig);
        }

        _trackerCount = 0;
        _director.HideAll();
        _director.SetVisible(false);
    }

    private void RefreshBodies()
    {
        EnsureTrackerPool();
        for (var i = 0; i < _trackerCount; i++)
            _trackers[i].SeenThisScan = false;

        var placeables = Object.FindObjectsByType<PlaceableAsset>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        for (var i = 0; i < placeables.Length; i++)
        {
            var asset = placeables[i];
            if (!IsTrackableAsset(asset, out var rb))
                continue;

            var index = FindTrackerIndex(rb);
            if (index >= 0)
            {
                _trackers[index].SeenThisScan = true;
                continue;
            }

            if (_trackerCount >= _trackers.Length)
                continue;

            var tracker = _trackers[_trackerCount];
            tracker.Configure(asset, rb, config, _lensConfig);
            tracker.SeenThisScan = true;
            tracker.Sample(Time.fixedDeltaTime, config);
            _trackerCount++;
        }

        for (var i = _trackerCount - 1; i >= 0; i--)
        {
            if (_trackers[i].SeenThisScan && _trackers[i].IsValid)
                continue;

            RemoveTrackerAt(i);
        }
    }

    private void RemoveTrackerAt(int index)
    {
        if (_trackers == null || index < 0 || index >= _trackerCount)
            return;

        _trackers[index].Clear(_lensConfig);
        var last = _trackerCount - 1;
        if (index != last)
        {
            var removed = _trackers[index];
            _trackers[index] = _trackers[last];
            _trackers[last] = removed;
        }

        _trackerCount--;
    }

    private int FindTrackerIndex(Rigidbody rb)
    {
        if (rb == null || _trackers == null)
            return -1;

        for (var i = 0; i < _trackerCount; i++)
        {
            if (_trackers[i].Rigidbody == rb)
                return i;
        }

        return -1;
    }

    private void EnsureTrackerPool()
    {
        var desired = config != null ? config.MaxTrackedBodies : 96;
        if (_trackers != null && _trackers.Length == desired)
            return;

        _trackers = new BodyTelemetryTracker[desired];
        for (var i = 0; i < _trackers.Length; i++)
            _trackers[i] = new BodyTelemetryTracker();
        _trackerCount = 0;
    }

    private void EnsureDirector()
    {
        if (_directorReady)
            return;

        _director.Initialize(transform, config);
        _directorReady = true;
    }

    private Rigidbody ResolveSelectedRigidbody()
    {
        if (_selection == null || _selection.SelectedAsset == null)
            return null;

        var asset = _selection.SelectedAsset;
        return asset.Rigidbody != null ? asset.Rigidbody : asset.GetComponent<Rigidbody>();
    }

    private void OnSelectionChanged(PlaceableAsset asset)
    {
        if (!_active)
            return;

        var selectedBody = asset != null
            ? asset.Rigidbody != null ? asset.Rigidbody : asset.GetComponent<Rigidbody>()
            : null;
        for (var i = 0; i < _trackerCount; i++)
        {
            var tracker = _trackers[i];
            if (tracker != null)
                tracker.RefreshImportance(ResolveActiveCamera(), tracker.Rigidbody == selectedBody, config);
        }
    }

    private void OnPhysicsDrawingSelectionChanged(PhysicsDrawingSelectable drawing)
    {
        if (!_active)
            return;

        for (var i = 0; i < _trackerCount; i++)
        {
            var tracker = _trackers[i];
            if (tracker != null)
                tracker.RefreshImportance(ResolveActiveCamera(), false, config);
        }
    }

    private void OnSimulationStateChanged()
    {
        EvaluateActivation();
    }

    private static bool IsTrackableAsset(PlaceableAsset asset, out Rigidbody rb)
    {
        rb = null;
        if (asset == null || !asset.isActiveAndEnabled)
            return false;

        if (asset.GetComponentInParent<SpawnTemplateMarker>() != null)
            return false;

        rb = asset.Rigidbody != null ? asset.Rigidbody : asset.GetComponent<Rigidbody>();
        return rb != null;
    }

    private static Camera ResolveActiveCamera()
    {
        if (Camera.main != null && Camera.main.isActiveAndEnabled)
            return Camera.main;

        var cameras = Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (var i = 0; i < cameras.Length; i++)
        {
            if (cameras[i] != null && cameras[i].isActiveAndEnabled)
                return cameras[i];
        }

        return null;
    }
}
