using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[DisallowMultipleComponent]
[DefaultExecutionOrder(25)]
public sealed class MeshSketchController : MonoBehaviour
{
    private static readonly List<MeshSketchController> Controllers = new();

    [Header("Feature Gate")]
    [SerializeField] private bool featureEnabled = true;
    [SerializeField] private MXInkInputAdapter inputAdapter;
    [SerializeField] private XRContentDrawerController contentDrawer;

    [Header("Pointer Over UI")]
    [SerializeField] private string uiCanvasNames = "PlaceableInspectorCanvas;SandboxEditorToolbarCanvas;HomeMenuCanvas";
    [SerializeField] private float uiRayDistance = 8f;
    [SerializeField] private float uiCanvasRescanSeconds = 0.5f;

    [Header("Authoring")]
    [SerializeField] private MeshSketchSettings settings = new();
    [SerializeField] private Material authoredMeshMaterial;
    [SerializeField] private MeshSketchFeedback feedback;

    private readonly StrokeCapture _strokeCapture = new();
    private readonly SnapSolver _snapSolver = new();
    private readonly LoopResolver _loopResolver = new();
    private readonly FaceBuilder _faceBuilder = new();
    private readonly List<Vector3> _snappedPoints = new();
    private readonly List<MeshSketchSnapResult> _snaps = new();
    private readonly List<Canvas> _uiCanvases = new();
    private readonly List<RaycastResult> _uiRaycastResults = new();

    private MeshSketchState _state;
    private bool _reservedUntilMiddleRelease;
    private float _nextReferenceResolveTime;
    private float _nextUiCanvasScanTime;
    private MeshSketchResolveResult _lastPreview;
    private bool _rearUndoWasPressed;
    private bool _hasUndoableInvalidSketch;

    public bool CanAuthorMesh => featureEnabled
                                 && MeshDrawingModeState.IsActive
                                 && IsEditMode()
                                 && !IsSimulationActive()
                                 && !IsPointerOverUi();

    public bool IsSketching => _state == MeshSketchState.Sketching;
    public bool IsInputReserved => IsSketching || _reservedUntilMiddleRelease;

    private void Awake()
    {
        ResolveReferences(true);
        if (feedback == null)
        {
            feedback = gameObject.AddComponent<MeshSketchFeedback>();
        }
    }

    private void OnEnable()
    {
        if (!Controllers.Contains(this))
        {
            Controllers.Add(this);
        }

        SceneManager.sceneLoaded += OnSceneLoaded;
        MeshDrawingModeState.ActiveChanged -= OnMeshDrawingModeChanged;
        MeshDrawingModeState.ActiveChanged += OnMeshDrawingModeChanged;
    }

    private void OnDisable()
    {
        Controllers.Remove(this);
        SceneManager.sceneLoaded -= OnSceneLoaded;
        MeshDrawingModeState.ActiveChanged -= OnMeshDrawingModeChanged;
        CancelSketch(false);
        ClearInvalidSketchFeedback();
    }

    public void Configure(MXInkInputAdapter adapter, XRContentDrawerController drawer)
    {
        if (adapter != null)
        {
            inputAdapter = adapter;
        }

        if (drawer != null)
        {
            contentDrawer = drawer;
        }
    }

    public static bool ShouldReserveMiddleButton(StylusHandler stylusHandler)
    {
        if (stylusHandler == null || stylusHandler.CurrentState.cluster_middle_value <= 0f)
        {
            return false;
        }

        for (var i = Controllers.Count - 1; i >= 0; i--)
        {
            var controller = Controllers[i];
            if (controller == null)
            {
                Controllers.RemoveAt(i);
                continue;
            }

            if (!controller.isActiveAndEnabled)
            {
                continue;
            }

            if (controller.ShouldReserveMiddleButtonFor(stylusHandler))
            {
                return true;
            }
        }

        return false;
    }

    private void Update()
    {
        ResolveReferences(false);

        if (MeshDrawingModeState.IsActive && (!IsEditMode() || IsSimulationActive()))
        {
            MeshDrawingModeState.SetActive(false);
        }

        if (inputAdapter == null || !featureEnabled)
        {
            CancelSketch(false);
            ClearInvalidSketchFeedback();
            _rearUndoWasPressed = false;
            return;
        }

        inputAdapter.ResolveStylus();
        var middlePressed = inputAdapter.MiddleButtonPressed;
        var rearUndoPressed = IsRearUndoPressed();
        if (MeshDrawingModeState.IsActive
            && rearUndoPressed
            && !_rearUndoWasPressed
            && TryUndoLastMeshElement())
        {
            _rearUndoWasPressed = rearUndoPressed;
            return;
        }

        _rearUndoWasPressed = rearUndoPressed;

        if (_reservedUntilMiddleRelease)
        {
            if (!middlePressed)
            {
                _reservedUntilMiddleRelease = false;
            }

            return;
        }

        if (_state == MeshSketchState.Sketching)
        {
            if (!middlePressed)
            {
                CompleteSketch();
                return;
            }

            if (!CanContinueSketch())
            {
                CancelSketch(true);
                return;
            }

            UpdateSketch();
            return;
        }

        if (middlePressed && CanBeginSketch())
        {
            BeginSketch();
        }
    }

    private bool ShouldReserveMiddleButtonFor(StylusHandler stylusHandler)
    {
        ResolveReferences(false);
        if (inputAdapter == null || inputAdapter.Stylus != stylusHandler)
        {
            return false;
        }

        if (IsInputReserved)
        {
            return true;
        }

        return stylusHandler.CurrentState.cluster_middle_value > 0f && CanBeginSketch();
    }

    private bool CanBeginSketch()
    {
        return inputAdapter != null
               && inputAdapter.MiddleButtonPressed
               && inputAdapter.TrackingAvailable
               && CanAuthorMesh;
    }

    private bool CanContinueSketch()
    {
        return inputAdapter != null
               && inputAdapter.TrackingAvailable
               && CanAuthorMesh;
    }

    private void BeginSketch()
    {
        ClearInvalidSketchFeedback();
        MXInkEditableMeshTopology.DeleteInvalidTopologies();
        settings.Clamp();
        _state = MeshSketchState.Sketching;
        _strokeCapture.Begin(inputAdapter.TipPose.position);
        UpdateSketch();
    }

    private void UpdateSketch()
    {
        var tip = inputAdapter.TipPose.position;
        _strokeCapture.Append(tip, settings);
        BuildSnappedStroke(_strokeCapture.BuildProcessed(settings), out var closurePreview, out var lastSnap);

        _lastPreview = null;
        if (closurePreview && _snappedPoints.Count >= 3)
        {
            _lastPreview = _loopResolver.ResolveClosedLoop(_snappedPoints, settings);
        }
        else if (_snappedPoints.Count >= 3 && _loopResolver.TryInferPlane(_snappedPoints, out var plane))
        {
            _lastPreview = new MeshSketchResolveResult
            {
                Plane = plane
            };
        }

        feedback?.Render(_snappedPoints, lastSnap, closurePreview, _lastPreview);
    }

    private void CompleteSketch()
    {
        BuildSnappedStroke(_strokeCapture.BuildProcessed(settings), out var closurePreview, out var lastSnap);
        if (_snappedPoints.Count == 0)
        {
            RejectSketch(Vector3.zero);
            return;
        }

        var resolvedLoop = _loopResolver.ResolveClosedLoop(_snappedPoints, settings);
        if (resolvedLoop.IsValidClosedLoop)
        {
            var topology = _faceBuilder.CommitFace(
                resolvedLoop,
                _snaps,
                settings,
                authoredMeshMaterial,
                out var before,
                out var createdTopology);
            if (topology != null)
            {
                MXInkMeshUndoIntegration.Instance.RecordTopologyEdit(topology, before, createdTopology);
                AssetSelectionManager.Instance?.SelectAsset(topology.GetComponent<PlaceableAsset>());
                _hasUndoableInvalidSketch = false;
                EndSketchSession();
                return;
            }
        }

        feedback?.Render(_snappedPoints, lastSnap, closurePreview, resolvedLoop);
        RejectSketch(_snappedPoints[_snappedPoints.Count - 1]);
    }

    private void RejectSketch(Vector3 feedbackPoint)
    {
        EndSketchSession(false);
        feedback?.ShowInvalid(feedbackPoint);
        _hasUndoableInvalidSketch = true;
        _reservedUntilMiddleRelease = inputAdapter != null && inputAdapter.MiddleButtonPressed;
    }

    private void CancelSketch(bool reserveUntilRelease)
    {
        if (_state != MeshSketchState.Sketching && !_reservedUntilMiddleRelease)
        {
            return;
        }

        EndSketchSession();
        _reservedUntilMiddleRelease = reserveUntilRelease
                                      && inputAdapter != null
                                      && inputAdapter.MiddleButtonPressed;
    }

    private void EndSketchSession(bool hideFeedback = true)
    {
        _state = MeshSketchState.Idle;
        _strokeCapture.Clear();
        _snappedPoints.Clear();
        _snaps.Clear();
        _lastPreview = null;
        if (hideFeedback)
        {
            feedback?.HideAll();
            _hasUndoableInvalidSketch = false;
        }
    }

    private bool IsRearUndoPressed()
    {
        if (inputAdapter == null || inputAdapter.Stylus == null)
        {
            return false;
        }

        var state = inputAdapter.Stylus.CurrentState;
        return state.cluster_back_value || state.cluster_back_double_tap_value;
    }

    private bool TryUndoLastMeshElement()
    {
        if (_state == MeshSketchState.Sketching
            || MXInkRayInteractorBinder.RearButtonHoverTargetActive
            || IsPointerOverUi())
        {
            return false;
        }

        if (_hasUndoableInvalidSketch)
        {
            ClearInvalidSketchFeedback();
            UiMenuSelectSoundHub.TryPlayScissorCut();
            return true;
        }

        if (MXInkEditableMeshTopology.DeleteInvalidTopologies() > 0)
        {
            UiMenuSelectSoundHub.TryPlayScissorCut();
            return true;
        }

        if (MXInkMeshUndoIntegration.Instance.TryUndo())
        {
            UiMenuSelectSoundHub.TryPlayScissorCut();
            return true;
        }

        return false;
    }

    private void ClearInvalidSketchFeedback()
    {
        if (!_hasUndoableInvalidSketch && (feedback == null || !feedback.HasVisibleFeedback))
        {
            return;
        }

        feedback?.HideAll();
        _hasUndoableInvalidSketch = false;
    }

    private void BuildSnappedStroke(
        IReadOnlyList<Vector3> points,
        out bool closurePreview,
        out MeshSketchSnapResult lastSnap)
    {
        _snappedPoints.Clear();
        _snaps.Clear();
        closurePreview = false;
        lastSnap = MeshSketchSnapResult.None(Vector3.zero);

        if (points == null || points.Count == 0)
        {
            return;
        }

        for (var i = 0; i < points.Count; i++)
        {
            var snap = _snapSolver.Solve(points[i], MXInkEditableMeshTopology.Active, settings);
            var snappedPoint = snap.HasSnap ? snap.Point : points[i];
            _snaps.Add(snap);
            _snappedPoints.Add(snappedPoint);
        }

        if (_snappedPoints.Count >= 3)
        {
            var lastIndex = _snappedPoints.Count - 1;
            var closureDistance = Vector3.Distance(_snappedPoints[0], _snappedPoints[lastIndex]);
            if (closureDistance <= settings.closureSnapRadius)
            {
                closurePreview = true;
                _snappedPoints[lastIndex] = _snappedPoints[0];
                _snaps[lastIndex] = new MeshSketchSnapResult
                {
                    Kind = MeshSketchSnapKind.Closure,
                    Point = _snappedPoints[0],
                    VertexIndex = -1,
                    EdgeIndex = -1
                };
            }
        }

        lastSnap = _snaps.Count > 0 ? _snaps[_snaps.Count - 1] : MeshSketchSnapResult.None(points[points.Count - 1]);
    }

    private bool IsEditMode()
    {
        ResolveReferences(false);
        return contentDrawer != null && contentDrawer.CurrentMode == XRControlMode.Edit;
    }

    private static bool IsSimulationActive()
    {
        var sim = SandboxSimulationController.Instance;
        return sim != null && sim.IsSimulating;
    }

    private bool IsPointerOverUi()
    {
        if (inputAdapter == null || EventSystem.current == null)
        {
            return false;
        }

        var pose = inputAdapter.TipPose;
        var direction = pose.rotation * Vector3.forward;
        if (direction.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        direction.Normalize();
        RefreshUiCanvasesIfNeeded();
        if (_uiCanvases.Count == 0)
        {
            return false;
        }

        var ray = new Ray(pose.position, direction);
        var maxDistance = Mathf.Max(0.01f, uiRayDistance);
        for (var i = 0; i < _uiCanvases.Count; i++)
        {
            var canvas = _uiCanvases[i];
            if (canvas == null || !canvas.isActiveAndEnabled || canvas.renderMode != RenderMode.WorldSpace)
            {
                continue;
            }

            var rect = canvas.transform as RectTransform;
            var raycaster = canvas.GetComponent<GraphicRaycaster>();
            if (rect == null || raycaster == null || !raycaster.isActiveAndEnabled)
            {
                continue;
            }

            var plane = new Plane(rect.forward, rect.position);
            if (!plane.Raycast(ray, out var distance) || distance < 0f || distance > maxDistance)
            {
                continue;
            }

            var screenPoint = RectTransformUtility.WorldToScreenPoint(canvas.worldCamera, ray.GetPoint(distance));
            var eventData = new PointerEventData(EventSystem.current)
            {
                position = screenPoint
            };

            _uiRaycastResults.Clear();
            raycaster.Raycast(eventData, _uiRaycastResults);
            if (_uiRaycastResults.Count > 0)
            {
                return true;
            }
        }

        return false;
    }

    private void RefreshUiCanvasesIfNeeded()
    {
        if (Time.unscaledTime < _nextUiCanvasScanTime && _uiCanvases.Count > 0)
        {
            return;
        }

        _nextUiCanvasScanTime = Time.unscaledTime + Mathf.Max(0.1f, uiCanvasRescanSeconds);
        _uiCanvases.Clear();
        var canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (var i = 0; i < canvases.Length; i++)
        {
            var canvas = canvases[i];
            if (canvas != null && CanvasMatchesFilter(canvas))
            {
                _uiCanvases.Add(canvas);
            }
        }
    }

    private bool CanvasMatchesFilter(Canvas canvas)
    {
        if (canvas == null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(uiCanvasNames))
        {
            return true;
        }

        var names = uiCanvasNames.Split(new[] { ';', ',', '|' }, System.StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < names.Length; i++)
        {
            var canvasName = names[i].Trim();
            if (canvasName == "*"
                || string.Equals(canvas.name, canvasName, System.StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private void ResolveReferences(bool force)
    {
        if (!force && Time.unscaledTime < _nextReferenceResolveTime)
        {
            return;
        }

        _nextReferenceResolveTime = Time.unscaledTime + 0.5f;

        if (inputAdapter == null)
        {
            inputAdapter = GetComponent<MXInkInputAdapter>()
                           ?? FindFirstObjectByType<MXInkInputAdapter>(FindObjectsInactive.Include);
        }

        if (contentDrawer == null)
        {
            contentDrawer = FindFirstObjectByType<XRContentDrawerController>(FindObjectsInactive.Include);
        }

        if (feedback == null)
        {
            feedback = GetComponent<MeshSketchFeedback>();
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        _uiCanvases.Clear();
        ResolveReferences(true);
    }

    private void OnMeshDrawingModeChanged(bool active)
    {
        if (active)
        {
            return;
        }

        CancelSketch(false);
        ClearInvalidSketchFeedback();
        MXInkEditableMeshTopology.DeleteInvalidTopologies();
        _rearUndoWasPressed = false;
    }

    private enum MeshSketchState
    {
        Idle,
        Sketching
    }
}
