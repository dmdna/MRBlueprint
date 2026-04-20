using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

public sealed class PhysicsLensManager : MonoBehaviour
{
    [SerializeField] private PhysicsLensConfig config;

    private readonly PhysicsTelemetryTracker _tracker = new PhysicsTelemetryTracker();
    private PhysicsLensPanelController _panel;
    private SandboxSimulationController _simulation;
    private AssetSelectionManager _selection;
    private PlaceableAsset _targetAsset;
    private Rigidbody _targetRigidbody;
    private float _nextTextRefreshTime;
    private bool _selectionSubscribed;
    private bool _simulationSubscribed;

    public static bool RuntimeAvailable { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void BootstrapInSandboxScene()
    {
        if (UnityEngine.Object.FindFirstObjectByType<PhysicsLensManager>() != null)
            return;

        if (UnityEngine.Object.FindFirstObjectByType<SandboxSimulationController>() == null
            && UnityEngine.Object.FindFirstObjectByType<SandboxEditorToolbarFrame>() == null
            && UnityEngine.Object.FindFirstObjectByType<AssetSelectionManager>() == null)
        {
            return;
        }

        var go = new GameObject("PhysicsLensManager");
        go.AddComponent<PhysicsLensManager>();
    }

    private void Awake()
    {
        if (config == null)
            config = Resources.Load<PhysicsLensConfig>("PhysicsLensConfig");
        if (config == null)
            config = PhysicsLensConfig.CreateRuntimeDefault();
    }

    private void Start()
    {
        if (config == null || !config.FeatureEnabled)
        {
            RuntimeAvailable = false;
            enabled = false;
            return;
        }

        RuntimeAvailable = true;
        EnsureEventSystem();
        BuildPanel();
        ResolveDependencies();
        EvaluateCurrentSelection();
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

        RuntimeAvailable = false;
    }

    private void Update()
    {
        if (config == null || !config.FeatureEnabled)
            return;

        ResolveDependencies();

        if (!IsInSimulateMode())
        {
            if (_targetAsset != null || _targetRigidbody != null)
                CloseLens();
            return;
        }

        if (_targetAsset == null || _targetRigidbody == null)
        {
            CloseLens();
            return;
        }

        if (Time.unscaledTime >= _nextTextRefreshTime)
        {
            _panel.UpdateTelemetry(_tracker);
            _nextTextRefreshTime = Time.unscaledTime + config.TextRefreshSeconds;
        }
    }

    private void FixedUpdate()
    {
        if (!IsInSimulateMode() || _simulation == null || _simulation.IsPaused)
            return;

        if (_targetRigidbody == null)
            return;

        _tracker.Sample(Time.fixedDeltaTime);
    }

    private void LateUpdate()
    {
        if (_targetRigidbody == null || _targetAsset == null || _panel == null)
            return;

        var camera = ResolveActiveCamera();
        if (camera == null)
            return;

        _panel.UpdateFollow(camera, _targetRigidbody.worldCenterOfMass, _tracker.ApproxBounds);
        _panel.RenderGraph(_tracker);
    }

    private void BuildPanel()
    {
        if (_panel != null)
            return;

        var panelGo = new GameObject("PhysicsLensWorldPanel");
        panelGo.transform.SetParent(transform, false);
        _panel = panelGo.AddComponent<PhysicsLensPanelController>();
        _panel.Initialize(config);
        _panel.PinPressed += OnPanelPinPressed;
    }

    private void ResolveDependencies()
    {
        if (_simulation == null)
            _simulation = SandboxSimulationController.Instance != null
                ? SandboxSimulationController.Instance
                : UnityEngine.Object.FindFirstObjectByType<SandboxSimulationController>();

        if (_selection == null)
            _selection = AssetSelectionManager.Instance != null
                ? AssetSelectionManager.Instance
                : UnityEngine.Object.FindFirstObjectByType<AssetSelectionManager>();

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

    private void EvaluateCurrentSelection()
    {
        if (!IsInSimulateMode() || _selection == null)
        {
            CloseLens();
            return;
        }

        if (_selection.SelectedPhysicsDrawing != null)
        {
            CloseLens();
            return;
        }

        if (_selection.SelectedAsset != null)
            OpenForAsset(_selection.SelectedAsset, false);
        else
            CloseLens();
    }

    private void OnSelectionChanged(PlaceableAsset asset)
    {
        if (!IsInSimulateMode())
        {
            CloseLens();
            return;
        }

        if (asset == null)
        {
            CloseLens();
            return;
        }

        if (asset == _targetAsset && _targetRigidbody != null)
        {
            _panel.SetExpanded(true, true);
            _panel.UpdateTelemetry(_tracker);
            return;
        }

        OpenForAsset(asset, false);
    }

    private void OnPhysicsDrawingSelectionChanged(PhysicsDrawingSelectable drawing)
    {
        if (drawing != null && IsInSimulateMode())
            CloseLens();
    }

    private void OnSimulationStateChanged()
    {
        if (!IsInSimulateMode())
        {
            CloseLens();
            return;
        }

        EvaluateCurrentSelection();
    }

    private void OpenForAsset(PlaceableAsset asset, bool expanded)
    {
        if (asset == null)
        {
            CloseLens();
            return;
        }

        var rb = asset.Rigidbody != null ? asset.Rigidbody : asset.GetComponent<Rigidbody>();
        if (rb == null)
        {
            CloseLens();
            return;
        }

        _targetAsset = asset;
        _targetRigidbody = rb;
        _tracker.Configure(asset, rb, config);
        _tracker.Sample(Time.fixedDeltaTime);
        _panel.SetExpanded(expanded, expanded);
        _panel.SetOpen(true, false);
        _panel.UpdateTelemetry(_tracker);
        _nextTextRefreshTime = 0f;

        var camera = ResolveActiveCamera();
        if (camera != null)
            _panel.UpdateFollow(camera, rb.worldCenterOfMass, _tracker.ApproxBounds);
    }

    private void CloseLens()
    {
        _targetAsset = null;
        _targetRigidbody = null;
        _tracker.Clear();

        if (_panel != null)
            _panel.SetOpen(false, false);
    }

    private void OnPanelPinPressed()
    {
        if (_targetAsset == null || _targetRigidbody == null)
            return;

        var next = !_panel.IsExpanded || !_panel.IsPinned;
        _panel.SetExpanded(next, next);
        _panel.UpdateTelemetry(_tracker);
    }

    private bool IsInSimulateMode()
    {
        return _simulation != null && _simulation.IsSimulating;
    }

    private static Camera ResolveActiveCamera()
    {
        if (Camera.main != null && Camera.main.isActiveAndEnabled)
            return Camera.main;

        var cameras = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (var i = 0; i < cameras.Length; i++)
        {
            if (cameras[i] != null && cameras[i].isActiveAndEnabled)
                return cameras[i];
        }

        return null;
    }

    private static void EnsureEventSystem()
    {
        if (UnityEngine.Object.FindFirstObjectByType<EventSystem>() != null)
            return;

        var esGo = new GameObject("EventSystem");
        esGo.AddComponent<EventSystem>();
        esGo.AddComponent<InputSystemUIInputModule>();
    }
}
