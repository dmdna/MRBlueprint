using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;

[DisallowMultipleComponent]
[DefaultExecutionOrder(-100)]
[RequireComponent(typeof(XRRayInteractor))]
[RequireComponent(typeof(LineRenderer))]
public class MXInkRayInteractorBinder : MonoBehaviour
{
    [SerializeField] private VrStylusHandler _stylusHandler;
    [SerializeField] private XRInteractorLineVisual _lineVisual;
    [SerializeField] private Transform _explicitRayOrigin;
    [SerializeField] private XRContentDrawerController _controlModeSource;
    [SerializeField] private XRDrawerItemSelectionManager _drawerItemSelection;
    [SerializeField] private PlaceableTransformGizmo _transformGizmo;
    [SerializeField] private Camera _transformGizmoCamera;
    [SerializeField] private bool _hideWhenStylusInactive = true;
    [SerializeField] private bool _hideOutsideSelectionMode = true;
    [SerializeField] private float _drawerSelectionRayDistance = 8f;
    [SerializeField] private LayerMask _drawerSelectionRaycastMask = ~0;
    [SerializeField] private Vector3 _localPositionOffset = Vector3.zero;
    [SerializeField] private Vector3 _localEulerOffset = Vector3.zero;

    [Header("Placeable and UI Pointer")]
    [SerializeField] private bool _enableWorldUiPointer = true;
    [SerializeField] private string _uiCanvasName = "PlaceableInspectorCanvas";
    [SerializeField] private float _uiRayDistance = 8f;
    [SerializeField] private float _placeableRayDistance = 8f;
    [SerializeField] private LayerMask _placeableRaycastMask = ~0;
    [SerializeField] private float _minGrabDistance = 0.25f;
    [SerializeField] private float _maxGrabDistance = 8f;
    [SerializeField] private float _endMarkerDiameter = 0.025f;
    [SerializeField] private float _endMarkerSurfaceOffset = 0.002f;

    private const int MXInkPointerId = -12003;
    private const int EndMarkerSegments = 32;

    private XRRayInteractor _rayInteractor;
    private LineRenderer _lineRenderer;
    private Transform _runtimeRayOrigin;
    private Transform _endMarker;
    private MeshRenderer _endMarkerRenderer;
    private Material _runtimeMaterial;
    private static Mesh _endMarkerMesh;
    private WorldSpaceUiRayPointer.State _uiPointerState;
    private bool _clusterBackWasPressed;
    private bool _clusterFrontWasPressed;
    private bool _stylusGizmoDragging;
    private bool _rearButtonSelectionLatch;

    public static bool RearButtonSelectionTargetActive { get; private set; }
    public static bool FrontButtonShapeGrabTargetActive { get; private set; }

    private static readonly Color RayColor = new(0f, 0f, 0f, 0.95f);

    private void Awake()
    {
        _rayInteractor = GetComponent<XRRayInteractor>();
        _lineRenderer = GetComponent<LineRenderer>();

        if (_lineVisual == null)
        {
            _lineVisual = GetComponent<XRInteractorLineVisual>();
        }

        if (_stylusHandler == null)
        {
            _stylusHandler = GetComponentInParent<VrStylusHandler>();
        }

        ResolveControlModeSource();
        ResolveDrawerItemSelection();
        ResolveTransformGizmo();
        EnsureRayOrigin();
        ApplyLineSetup();
        ApplyRayBindings();
        UpdateVisibility(default);
    }

    private void Update()
    {
        ResolveControlModeSource();
        ResolveTransformGizmo();
        EnsureRayOrigin();
        ApplyRayBindings();
        BuildPointerState();
    }

    private void LateUpdate()
    {
        ResolveControlModeSource();
        ResolveDrawerItemSelection();
        ResolveTransformGizmo();
        EnsureRayOrigin();
        ApplyRayBindings();
        var pointerState = BuildPointerState();
        HandleFrontButtonGrab(pointerState);
        HandleRearButtonInteractions(pointerState);
        UpdateVisibility(pointerState);
    }

    private void OnDestroy()
    {
        EndGizmoDrag();
        PlaceableMultiGrabCoordinator.EndGrab(PlaceableMultiGrabCoordinator.MXInkSourceId);
        HideEndMarker();
        RearButtonSelectionTargetActive = false;
        FrontButtonShapeGrabTargetActive = false;
        _rearButtonSelectionLatch = false;

        if (_runtimeMaterial != null)
        {
            Destroy(_runtimeMaterial);
        }
    }

    private void OnDisable()
    {
        EndGizmoDrag();
        PlaceableMultiGrabCoordinator.EndGrab(PlaceableMultiGrabCoordinator.MXInkSourceId);
        HideEndMarker();
        RearButtonSelectionTargetActive = false;
        FrontButtonShapeGrabTargetActive = false;
        _rearButtonSelectionLatch = false;
    }

    private void EnsureRayOrigin()
    {
        var targetOrigin = ResolveTargetOrigin();
        if (targetOrigin == null)
        {
            return;
        }

        if (_runtimeRayOrigin == null)
        {
            var rayOriginObject = new GameObject("MXInkRayOrigin");
            _runtimeRayOrigin = rayOriginObject.transform;
        }

        if (_runtimeRayOrigin.parent != targetOrigin)
        {
            _runtimeRayOrigin.SetParent(targetOrigin, false);
        }

        _runtimeRayOrigin.localPosition = _localPositionOffset;
        _runtimeRayOrigin.localRotation = Quaternion.Euler(_localEulerOffset);
    }

    private Transform ResolveTargetOrigin()
    {
        if (_explicitRayOrigin != null)
        {
            return _explicitRayOrigin;
        }

        if (_stylusHandler != null)
        {
            return _stylusHandler.TipTransform;
        }

        return transform.parent;
    }

    private void ApplyRayBindings()
    {
        if (_runtimeRayOrigin == null)
        {
            return;
        }

        _rayInteractor.rayOriginTransform = _runtimeRayOrigin;

        if (_lineVisual != null)
        {
            _lineVisual.overrideInteractorLineOrigin = true;
            _lineVisual.lineOriginTransform = _runtimeRayOrigin;
            _lineVisual.setLineColorGradient = true;
            _lineVisual.validColorGradient = BuildGradient(RayColor);
            _lineVisual.invalidColorGradient = BuildGradient(RayColor);
            _lineVisual.blockedColorGradient = BuildGradient(RayColor);
        }
    }

    private void ApplyLineSetup()
    {
        _lineRenderer.positionCount = 2;
        _lineRenderer.useWorldSpace = true;
        _lineRenderer.alignment = LineAlignment.View;
        _lineRenderer.shadowCastingMode = ShadowCastingMode.Off;
        _lineRenderer.receiveShadows = false;

        if (_runtimeMaterial == null)
        {
            _runtimeMaterial = CreateLineMaterial();
        }

        if (_runtimeMaterial != null)
        {
            _lineRenderer.sharedMaterial = _runtimeMaterial;
        }

        _lineRenderer.textureMode = LineTextureMode.Stretch;
        _lineRenderer.startColor = RayColor;
        _lineRenderer.endColor = RayColor;
        EnsureEndMarker();
    }

    private StylusPointerState BuildPointerState()
    {
        if (!CanUseStylusRay() || _runtimeRayOrigin == null)
        {
            RearButtonSelectionTargetActive = false;
            FrontButtonShapeGrabTargetActive = false;
            _rearButtonSelectionLatch = false;
            return default;
        }

        var origin = _runtimeRayOrigin.position;
        var direction = _runtimeRayOrigin.forward;
        if (direction.sqrMagnitude <= 0.0001f)
        {
            RearButtonSelectionTargetActive = false;
            FrontButtonShapeGrabTargetActive = false;
            _rearButtonSelectionLatch = false;
            return default;
        }

        direction.Normalize();
        var pointerState = new StylusPointerState
        {
            IsUsable = true,
            Origin = origin,
            Direction = direction,
            Rotation = _runtimeRayOrigin.rotation
        };

        if (WorldSpaceUiRayPointer.TryGetHit(
                _enableWorldUiPointer,
                _uiCanvasName,
                origin,
                direction,
                Mathf.Max(_uiRayDistance, _placeableRayDistance),
                out var uiHit))
        {
            pointerState.HasUiHit = true;
            pointerState.UiHit = uiHit;
        }

        if (TryGetFirstRayHit(
                origin,
                direction,
                _placeableRayDistance,
                out var hit,
                out var placeable,
                out var gizmoPart,
                out var drawing))
        {
            pointerState.HasHit = true;
            pointerState.Hit = hit;
            pointerState.HoveredShape = placeable;
            pointerState.HoveredGizmoPart = gizmoPart;
            pointerState.HoveredDrawing = drawing;
        }

        var rearButtonPressed = _stylusHandler != null && _stylusHandler.CurrentState.cluster_back_value;
        var selectionExists = AssetSelectionManager.Instance != null && AssetSelectionManager.Instance.HasSelection;
        if (!rearButtonPressed)
        {
            _rearButtonSelectionLatch = false;
        }
        else if (selectionExists)
        {
            _rearButtonSelectionLatch = true;
        }

        RearButtonSelectionTargetActive = pointerState.HasUiHit
                                          || pointerState.HoveredShape != null
                                          || pointerState.HoveredGizmoPart != null
                                          || pointerState.HoveredDrawing != null
                                          || _stylusGizmoDragging
                                          || selectionExists
                                          || _rearButtonSelectionLatch;
        FrontButtonShapeGrabTargetActive = ResolvePlaceableGrabTarget(pointerState) != null
                                           || ResolvePhysicsDrawingGrabTarget(pointerState) != null
                                           || PlaceableMultiGrabCoordinator.IsSourceGrabbing(
                                               PlaceableMultiGrabCoordinator.MXInkSourceId);
        return pointerState;
    }

    private void UpdateVisibility(StylusPointerState pointerState)
    {
        var stylusIsVisible = !_hideWhenStylusInactive
                              || _stylusHandler == null
                              || _stylusHandler.IsTrackingStylus;
        var isSelectionMode = IsSelectionMode();
        var isGrabbing = PlaceableMultiGrabCoordinator.IsSourceGrabbing(PlaceableMultiGrabCoordinator.MXInkSourceId);
        var modeIsVisible = !_hideOutsideSelectionMode || isSelectionMode;
        var hasManualTarget = pointerState.HasUiHit
                              || pointerState.HoveredShape != null
                              || pointerState.HoveredGizmoPart != null
                              || pointerState.HoveredDrawing != null
                              || isGrabbing
                              || _stylusGizmoDragging;
        var isVisible = stylusIsVisible && (modeIsVisible || hasManualTarget);
        var useManualLine = isVisible && pointerState.IsUsable && hasManualTarget;

        if (useManualLine)
        {
            ApplyManualLine(pointerState);
        }

        if (_lineRenderer.enabled != isVisible)
        {
            _lineRenderer.enabled = isVisible;
        }

        if (_rayInteractor.enabled != isVisible && !useManualLine)
        {
            _rayInteractor.enabled = isVisible;
        }
        else if (useManualLine && _rayInteractor.enabled)
        {
            _rayInteractor.enabled = false;
        }

        if (_lineVisual != null && _lineVisual.enabled != isVisible && !useManualLine)
        {
            _lineVisual.enabled = isVisible;
        }
        else if (useManualLine && _lineVisual != null && _lineVisual.enabled)
        {
            _lineVisual.enabled = false;
        }

        if (isVisible
            && pointerState.IsUsable
            && TryResolveEndMarkerPoint(pointerState, out var markerPoint))
        {
            SetEndMarker(markerPoint, pointerState.Direction, true);
        }
        else
        {
            HideEndMarker();
        }
    }

    private void ApplyManualLine(StylusPointerState pointerState)
    {
        if (_lineRenderer == null)
        {
            return;
        }

        var endPoint = pointerState.Origin + pointerState.Direction * Mathf.Max(0.01f, _drawerSelectionRayDistance);
        if (PlaceableMultiGrabCoordinator.IsSourceGrabbingPhysicsDrawing(PlaceableMultiGrabCoordinator.MXInkSourceId)
            && pointerState.HasHit
            && pointerState.HoveredDrawing != null)
        {
            endPoint = pointerState.Hit.point;
        }
        else if (PlaceableMultiGrabCoordinator.TryGetSourceGrabPoint(
                PlaceableMultiGrabCoordinator.MXInkSourceId,
                out var grabPoint))
        {
            endPoint = grabPoint;
        }
        else if (pointerState.HasUiHit)
        {
            endPoint = pointerState.UiHit.WorldPoint;
        }
        else if (pointerState.HasHit)
        {
            endPoint = pointerState.Hit.point;
        }

        _lineRenderer.positionCount = 2;
        _lineRenderer.SetPosition(0, pointerState.Origin);
        _lineRenderer.SetPosition(1, endPoint);
    }

    private bool TryResolveEndMarkerPoint(StylusPointerState pointerState, out Vector3 point)
    {
        if (pointerState.HasUiHit)
        {
            point = pointerState.UiHit.WorldPoint;
            return true;
        }

        if (PlaceableMultiGrabCoordinator.IsSourceGrabbingPhysicsDrawing(PlaceableMultiGrabCoordinator.MXInkSourceId)
            && pointerState.HasHit
            && pointerState.HoveredDrawing != null)
        {
            point = pointerState.Hit.point;
            return true;
        }

        if (pointerState.HasHit)
        {
            point = pointerState.Hit.point;
            return true;
        }

        if (PlaceableMultiGrabCoordinator.TryGetSourceGrabPoint(
                PlaceableMultiGrabCoordinator.MXInkSourceId,
                out var grabPoint))
        {
            point = grabPoint;
            return true;
        }

        point = default;
        return false;
    }

    private void EnsureEndMarker()
    {
        if (_endMarker != null)
        {
            if (_endMarkerRenderer != null && _runtimeMaterial != null)
            {
                _endMarkerRenderer.sharedMaterial = _runtimeMaterial;
            }

            return;
        }

        var markerObject = new GameObject("MXInkRayEndMarker");
        markerObject.transform.SetParent(transform, false);
        var meshFilter = markerObject.AddComponent<MeshFilter>();
        meshFilter.sharedMesh = GetOrCreateEndMarkerMesh();
        _endMarkerRenderer = markerObject.AddComponent<MeshRenderer>();
        _endMarkerRenderer.sharedMaterial = _runtimeMaterial;
        _endMarkerRenderer.shadowCastingMode = ShadowCastingMode.Off;
        _endMarkerRenderer.receiveShadows = false;
        _endMarker = markerObject.transform;
        markerObject.SetActive(false);
    }

    private void SetEndMarker(Vector3 endPoint, Vector3 rayDirection, bool visible)
    {
        EnsureEndMarker();
        if (_endMarker == null)
        {
            return;
        }

        if (!visible || rayDirection.sqrMagnitude <= 0.000001f)
        {
            if (_endMarker.gameObject.activeSelf)
            {
                _endMarker.gameObject.SetActive(false);
            }

            return;
        }

        var direction = rayDirection.normalized;
        _endMarker.position = endPoint - direction * Mathf.Max(0f, _endMarkerSurfaceOffset);
        _endMarker.rotation = Quaternion.LookRotation(-direction, Vector3.up);
        _endMarker.localScale = Vector3.one * Mathf.Max(0.001f, _endMarkerDiameter);
        if (!_endMarker.gameObject.activeSelf)
        {
            _endMarker.gameObject.SetActive(true);
        }
    }

    private void HideEndMarker()
    {
        if (_endMarker != null && _endMarker.gameObject.activeSelf)
        {
            _endMarker.gameObject.SetActive(false);
        }
    }

    private void HandleRearButtonInteractions(StylusPointerState pointerState)
    {
        var clusterBackPressed = _stylusHandler != null && _stylusHandler.CurrentState.cluster_back_value;

        if (!pointerState.IsUsable)
        {
            EndGizmoDrag();
            WorldSpaceUiRayPointer.Handle(
                _enableWorldUiPointer,
                false,
                default,
                false,
                ref _uiPointerState,
                MXInkPointerId);
            _clusterBackWasPressed = clusterBackPressed;
            return;
        }

        if (HandleGizmoDrag(pointerState, clusterBackPressed))
        {
            _clusterBackWasPressed = clusterBackPressed;
            return;
        }

        var uiHandled = WorldSpaceUiRayPointer.Handle(
            _enableWorldUiPointer,
            pointerState.HasUiHit,
            pointerState.UiHit,
            clusterBackPressed,
            ref _uiPointerState,
            MXInkPointerId);

        var drawerItemUnderRay = false;
        if (!pointerState.HasUiHit
            && pointerState.HoveredShape == null
            && pointerState.HoveredGizmoPart == null
            && pointerState.HoveredDrawing == null
            && IsSelectionMode())
        {
            drawerItemUnderRay = TrySelectDrawerItemUnderRay();
        }

        if (clusterBackPressed && !_clusterBackWasPressed && !uiHandled)
        {
            if (pointerState.HoveredShape != null)
            {
                AssetSelectionManager.Instance?.SelectAsset(pointerState.HoveredShape);
            }
            else if (pointerState.HoveredDrawing != null)
            {
                AssetSelectionManager.Instance?.SelectPhysicsDrawing(pointerState.HoveredDrawing);
            }
            else if (pointerState.HoveredGizmoPart != null)
            {
                // Gizmo hover without a successful drag should not deselect the current object.
            }
            else if (drawerItemUnderRay && _drawerItemSelection != null)
            {
                _drawerItemSelection.TryConfirmSpawnSelected();
            }
            else
            {
                AssetSelectionManager.Instance?.ClearSelection();
            }
        }

        _clusterBackWasPressed = clusterBackPressed;
    }

    private bool HandleGizmoDrag(StylusPointerState pointerState, bool rearButtonPressed)
    {
        if (_stylusGizmoDragging)
        {
            if (rearButtonPressed && pointerState.IsUsable)
            {
                var dragCamera = ResolveTransformGizmoCamera();
                if (_transformGizmo != null && dragCamera != null)
                {
                    _transformGizmo.Drag(new Ray(pointerState.Origin, pointerState.Direction), dragCamera);
                }
                else
                {
                    EndGizmoDrag();
                }
            }
            else
            {
                EndGizmoDrag();
            }

            return true;
        }

        if (!rearButtonPressed || _clusterBackWasPressed || !pointerState.IsUsable || pointerState.HasUiHit)
        {
            return false;
        }

        ResolveTransformGizmo();
        var cam = ResolveTransformGizmoCamera();
        if (_transformGizmo == null || cam == null)
        {
            return false;
        }

        var ray = new Ray(pointerState.Origin, pointerState.Direction);
        var maxDistance = Mathf.Max(_placeableRayDistance, _drawerSelectionRayDistance, _uiRayDistance);
        if (!_transformGizmo.TryBeginDrag(ray, maxDistance, cam))
        {
            return pointerState.HoveredGizmoPart != null;
        }

        _stylusGizmoDragging = true;
        _transformGizmo.Drag(ray, cam);
        return true;
    }

    private void HandleFrontButtonGrab(StylusPointerState pointerState)
    {
        var clusterFrontPressed = _stylusHandler != null && _stylusHandler.CurrentState.cluster_front_value;
        var sourceId = PlaceableMultiGrabCoordinator.MXInkSourceId;

        if (PlaceableMultiGrabCoordinator.IsSourceGrabbing(sourceId))
        {
            if (clusterFrontPressed && pointerState.IsUsable)
            {
                PlaceableMultiGrabCoordinator.UpdateGrab(
                    sourceId,
                    pointerState.Origin,
                    pointerState.Direction,
                    pointerState.Rotation,
                    0f,
                    _minGrabDistance,
                    Mathf.Max(_minGrabDistance, _maxGrabDistance));
            }
            else
            {
                PlaceableMultiGrabCoordinator.EndGrab(sourceId);
            }

            _clusterFrontWasPressed = clusterFrontPressed;
            return;
        }

        var placeableGrabTarget = ResolvePlaceableGrabTarget(pointerState);
        var drawingGrabTarget = ResolvePhysicsDrawingGrabTarget(pointerState);
        if (clusterFrontPressed
            && !_clusterFrontWasPressed
            && pointerState.IsUsable
            && (placeableGrabTarget != null || drawingGrabTarget != null))
        {
            if (placeableGrabTarget != null)
            {
                PlaceableMultiGrabCoordinator.TryBeginGrab(
                    sourceId,
                    placeableGrabTarget,
                    pointerState.Origin,
                    pointerState.Direction,
                    pointerState.HasHit ? pointerState.Hit.distance : _placeableRayDistance,
                    _minGrabDistance,
                    Mathf.Max(_minGrabDistance, _maxGrabDistance));
            }
            else
            {
                PlaceableMultiGrabCoordinator.TryBeginGrab(
                    sourceId,
                    drawingGrabTarget,
                    pointerState.Origin,
                    pointerState.Direction,
                    pointerState.Rotation,
                    pointerState.HasHit ? pointerState.Hit.distance : _placeableRayDistance,
                    _minGrabDistance,
                    Mathf.Max(_minGrabDistance, _maxGrabDistance));
            }

            PlaceableMultiGrabCoordinator.UpdateGrab(
                sourceId,
                pointerState.Origin,
                pointerState.Direction,
                pointerState.Rotation,
                0f,
                _minGrabDistance,
                Mathf.Max(_minGrabDistance, _maxGrabDistance));
        }

        _clusterFrontWasPressed = clusterFrontPressed;
    }

    private static PlaceableAsset ResolvePlaceableGrabTarget(StylusPointerState pointerState)
    {
        if (pointerState.HoveredShape != null)
        {
            return pointerState.HoveredShape;
        }

        return pointerState.HoveredGizmoPart != null
            ? AssetSelectionManager.Instance?.SelectedAsset
            : null;
    }

    private static PhysicsDrawingSelectable ResolvePhysicsDrawingGrabTarget(StylusPointerState pointerState)
    {
        return pointerState.HoveredDrawing;
    }

    private bool CanUseStylusRay()
    {
        return !_hideWhenStylusInactive || _stylusHandler == null || _stylusHandler.IsTrackingStylus;
    }

    private bool TrySelectDrawerItemUnderRay()
    {
        if (_runtimeRayOrigin == null || _drawerItemSelection == null)
        {
            return false;
        }

        var hits = Physics.RaycastAll(
            _runtimeRayOrigin.position,
            _runtimeRayOrigin.forward,
            _drawerSelectionRayDistance,
            _drawerSelectionRaycastMask,
            QueryTriggerInteraction.Collide);

        if (hits == null || hits.Length == 0)
        {
            return false;
        }

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (var hit in hits)
        {
            if (hit.collider.GetComponent<DrawerTilePickTarget>() == null)
            {
                continue;
            }

            var drawerItem = hit.collider.GetComponentInParent<XRDrawerItem>();
            if (drawerItem == null)
            {
                continue;
            }

            _drawerItemSelection.SelectItem(drawerItem);
            return true;
        }

        return false;
    }

    private bool TryGetFirstRayHit(
        Vector3 origin,
        Vector3 direction,
        float maxDistance,
        out RaycastHit firstHit,
        out PlaceableAsset hitPlaceable,
        out GizmoHandlePart hitGizmoPart,
        out PhysicsDrawingSelectable hitDrawing)
    {
        var length = Mathf.Max(0.01f, maxDistance);
        var hits = Physics.RaycastAll(origin, direction, length, _placeableRaycastMask, QueryTriggerInteraction.Collide);
        if (hits == null || hits.Length == 0)
        {
            firstHit = default;
            hitPlaceable = null;
            hitGizmoPart = null;
            hitDrawing = null;
            return false;
        }

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        firstHit = hits[0];
        hitPlaceable = null;
        hitGizmoPart = null;
        hitDrawing = null;

        foreach (var hit in hits)
        {
            hitGizmoPart = hit.collider != null ? hit.collider.GetComponent<GizmoHandlePart>() : null;
            if (hitGizmoPart != null)
            {
                firstHit = hit;
                return true;
            }
        }

        foreach (var hit in hits)
        {
            hitDrawing = hit.collider != null
                ? hit.collider.GetComponentInParent<PhysicsDrawingSelectable>()
                : null;
            if (hitDrawing != null)
            {
                firstHit = hit;
                return true;
            }
        }

        foreach (var hit in hits)
        {
            hitPlaceable = hit.collider != null
                ? hit.collider.GetComponentInParent<PlaceableAsset>()
                : null;
            if (hitPlaceable != null)
            {
                firstHit = hit;
                return true;
            }
        }

        return true;
    }

    private bool IsSelectionMode()
    {
        ResolveControlModeSource();
        return _controlModeSource != null && _controlModeSource.CurrentMode == XRControlMode.Selection;
    }

    private void ResolveControlModeSource()
    {
        if (_controlModeSource != null)
        {
            return;
        }

        _controlModeSource = FindFirstObjectByType<XRContentDrawerController>(FindObjectsInactive.Include);
    }

    private void ResolveDrawerItemSelection()
    {
        if (_drawerItemSelection != null)
        {
            return;
        }

        _drawerItemSelection = FindFirstObjectByType<XRDrawerItemSelectionManager>(FindObjectsInactive.Include);
    }

    private void ResolveTransformGizmo()
    {
        if (_transformGizmo != null)
        {
            return;
        }

        _transformGizmo = FindFirstObjectByType<PlaceableTransformGizmo>(FindObjectsInactive.Include);
    }

    private Camera ResolveTransformGizmoCamera()
    {
        ResolveTransformGizmo();
        if (_transformGizmoCamera != null && _transformGizmoCamera.isActiveAndEnabled)
        {
            return _transformGizmoCamera;
        }

        if (Camera.main != null && Camera.main.isActiveAndEnabled)
        {
            return Camera.main;
        }

        var cameras = FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (var camera in cameras)
        {
            if (camera != null && camera.isActiveAndEnabled)
            {
                return camera;
            }
        }

        return null;
    }

    private void EndGizmoDrag()
    {
        if (!_stylusGizmoDragging)
        {
            return;
        }

        _transformGizmo?.EndDrag();
        _stylusGizmoDragging = false;
    }

    private static Gradient BuildGradient(Color color)
    {
        var gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(color, 0f),
                new GradientColorKey(color, 1f),
            },
            new[]
            {
                new GradientAlphaKey(color.a, 0f),
                new GradientAlphaKey(color.a, 1f),
            });
        return gradient;
    }

    private static Material CreateLineMaterial()
    {
        var shader = Shader.Find("MRBlueprint/RayNoStackTransparent");
        if (shader == null)
        {
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        }

        if (shader == null)
        {
            shader = Shader.Find("Universal Render Pipeline/Lit");
        }

        if (shader == null)
        {
            shader = Shader.Find("Sprites/Default");
        }

        if (shader == null)
        {
            return null;
        }

        var material = new Material(shader)
        {
            name = "MXInkRayRuntimeMaterial",
            color = RayColor,
            enableInstancing = true
        };

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", RayColor);
        }

        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", RayColor);
        }

        if (material.HasProperty("_BaseMap"))
        {
            material.SetTexture("_BaseMap", Texture2D.whiteTexture);
        }

        if (material.HasProperty("_MainTex"))
        {
            material.SetTexture("_MainTex", Texture2D.whiteTexture);
        }

        if (material.HasProperty("_Surface"))
        {
            material.SetFloat("_Surface", 1f);
        }

        if (material.HasProperty("_Blend"))
        {
            material.SetFloat("_Blend", 0f);
        }

        if (material.HasProperty("_Cull"))
        {
            material.SetFloat("_Cull", (float)CullMode.Off);
        }

        ConfigureTransparentMaterial(material);
        return material;
    }

    private static void ConfigureTransparentMaterial(Material material)
    {
        if (material == null)
        {
            return;
        }

        material.SetOverrideTag("RenderType", "Transparent");
        material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        material.SetInt("_ZWrite", 0);
        material.DisableKeyword("_ALPHATEST_ON");
        material.EnableKeyword("_ALPHABLEND_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.renderQueue = (int)RenderQueue.Transparent;
    }

    private static Mesh GetOrCreateEndMarkerMesh()
    {
        if (_endMarkerMesh != null)
        {
            return _endMarkerMesh;
        }

        var longitude = Mathf.Max(8, EndMarkerSegments);
        var latitude = Mathf.Max(4, EndMarkerSegments / 2);
        var ringCount = latitude - 1;
        var vertices = new Vector3[2 + ringCount * longitude];
        var triangles = new int[longitude * (2 + Mathf.Max(0, ringCount - 1) * 2) * 3];

        vertices[0] = Vector3.up * 0.5f;
        var vertexIndex = 1;
        for (var lat = 1; lat < latitude; lat++)
        {
            var theta = Mathf.PI * lat / latitude;
            var y = Mathf.Cos(theta) * 0.5f;
            var radius = Mathf.Sin(theta) * 0.5f;
            for (var lon = 0; lon < longitude; lon++)
            {
                var phi = Mathf.PI * 2f * lon / longitude;
                vertices[vertexIndex++] = new Vector3(
                    Mathf.Cos(phi) * radius,
                    y,
                    Mathf.Sin(phi) * radius);
            }
        }

        var bottomIndex = vertices.Length - 1;
        vertices[bottomIndex] = Vector3.down * 0.5f;
        var triangleIndex = 0;

        for (var lon = 0; lon < longitude; lon++)
        {
            var next = (lon + 1) % longitude;
            triangles[triangleIndex++] = 0;
            triangles[triangleIndex++] = 1 + next;
            triangles[triangleIndex++] = 1 + lon;
        }

        for (var ring = 0; ring < ringCount - 1; ring++)
        {
            var currentRing = 1 + ring * longitude;
            var nextRing = currentRing + longitude;
            for (var lon = 0; lon < longitude; lon++)
            {
                var next = (lon + 1) % longitude;
                triangles[triangleIndex++] = currentRing + lon;
                triangles[triangleIndex++] = nextRing + next;
                triangles[triangleIndex++] = nextRing + lon;
                triangles[triangleIndex++] = currentRing + lon;
                triangles[triangleIndex++] = currentRing + next;
                triangles[triangleIndex++] = nextRing + next;
            }
        }

        var lastRing = 1 + (ringCount - 1) * longitude;
        for (var lon = 0; lon < longitude; lon++)
        {
            var next = (lon + 1) % longitude;
            triangles[triangleIndex++] = bottomIndex;
            triangles[triangleIndex++] = lastRing + lon;
            triangles[triangleIndex++] = lastRing + next;
        }

        _endMarkerMesh = new Mesh
        {
            name = "MXInkRayEndSphere"
        };
        _endMarkerMesh.vertices = vertices;
        _endMarkerMesh.triangles = triangles;
        _endMarkerMesh.RecalculateBounds();
        _endMarkerMesh.RecalculateNormals();
        return _endMarkerMesh;
    }

    private struct StylusPointerState
    {
        public bool IsUsable;
        public Vector3 Origin;
        public Vector3 Direction;
        public Quaternion Rotation;
        public bool HasHit;
        public RaycastHit Hit;
        public PlaceableAsset HoveredShape;
        public GizmoHandlePart HoveredGizmoPart;
        public PhysicsDrawingSelectable HoveredDrawing;
        public bool HasUiHit;
        public WorldSpaceUiRayPointer.Hit UiHit;
    }
}
