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

    private const int MXInkPointerId = -12003;

    private XRRayInteractor _rayInteractor;
    private LineRenderer _lineRenderer;
    private Transform _runtimeRayOrigin;
    private Material _runtimeMaterial;
    private WorldSpaceUiRayPointer.State _uiPointerState;
    private bool _clusterBackWasPressed;
    private bool _clusterFrontWasPressed;
    private bool _stylusGizmoDragging;

    public static bool RearButtonSelectionTargetActive { get; private set; }
    public static bool FrontButtonShapeGrabTargetActive { get; private set; }

    private static readonly Color ValidColor = Color.white;
    private static readonly Color InvalidColor = new(1f, 0.35f, 0.35f, 1f);
    private static readonly Color BlockedColor = new(1f, 0.92f, 0.15f, 1f);

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
        RearButtonSelectionTargetActive = false;
        FrontButtonShapeGrabTargetActive = false;

        if (_runtimeMaterial != null)
        {
            Destroy(_runtimeMaterial);
        }
    }

    private void OnDisable()
    {
        EndGizmoDrag();
        PlaceableMultiGrabCoordinator.EndGrab(PlaceableMultiGrabCoordinator.MXInkSourceId);
        RearButtonSelectionTargetActive = false;
        FrontButtonShapeGrabTargetActive = false;
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
            _lineVisual.validColorGradient = BuildGradient(ValidColor);
            _lineVisual.invalidColorGradient = BuildGradient(InvalidColor);
            _lineVisual.blockedColorGradient = BuildGradient(BlockedColor);
        }
    }

    private void ApplyLineSetup()
    {
        _lineRenderer.positionCount = 2;
        _lineRenderer.useWorldSpace = true;
        _lineRenderer.alignment = LineAlignment.View;
        _lineRenderer.shadowCastingMode = ShadowCastingMode.Off;
        _lineRenderer.receiveShadows = false;

        if (_lineRenderer.sharedMaterial == null)
        {
            _runtimeMaterial = CreateLineMaterial();
            if (_runtimeMaterial != null)
            {
                _lineRenderer.sharedMaterial = _runtimeMaterial;
            }
        }

        _lineRenderer.startColor = ValidColor;
        _lineRenderer.endColor = ValidColor;
    }

    private StylusPointerState BuildPointerState()
    {
        if (!CanUseStylusRay() || _runtimeRayOrigin == null)
        {
            RearButtonSelectionTargetActive = false;
            FrontButtonShapeGrabTargetActive = false;
            return default;
        }

        var origin = _runtimeRayOrigin.position;
        var direction = _runtimeRayOrigin.forward;
        if (direction.sqrMagnitude <= 0.0001f)
        {
            RearButtonSelectionTargetActive = false;
            FrontButtonShapeGrabTargetActive = false;
            return default;
        }

        direction.Normalize();
        var pointerState = new StylusPointerState
        {
            IsUsable = true,
            Origin = origin,
            Direction = direction
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
                out var gizmoPart))
        {
            pointerState.HasHit = true;
            pointerState.Hit = hit;
            pointerState.HoveredShape = placeable;
            pointerState.HoveredGizmoPart = gizmoPart;
        }

        RearButtonSelectionTargetActive = pointerState.HasUiHit
                                          || pointerState.HoveredShape != null
                                          || pointerState.HoveredGizmoPart != null
                                          || _stylusGizmoDragging;
        FrontButtonShapeGrabTargetActive = ResolvePlaceableGrabTarget(pointerState) != null
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
    }

    private void ApplyManualLine(StylusPointerState pointerState)
    {
        if (_lineRenderer == null)
        {
            return;
        }

        var endPoint = pointerState.Origin + pointerState.Direction * Mathf.Max(0.01f, _drawerSelectionRayDistance);
        if (PlaceableMultiGrabCoordinator.TryGetSourceGrabPoint(
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

        var grabTarget = ResolvePlaceableGrabTarget(pointerState);
        if (clusterFrontPressed
            && !_clusterFrontWasPressed
            && pointerState.IsUsable
            && grabTarget != null)
        {
            PlaceableMultiGrabCoordinator.TryBeginGrab(
                sourceId,
                grabTarget,
                pointerState.Origin,
                pointerState.Direction,
                pointerState.HasHit ? pointerState.Hit.distance : _placeableRayDistance,
                _minGrabDistance,
                Mathf.Max(_minGrabDistance, _maxGrabDistance));

            PlaceableMultiGrabCoordinator.UpdateGrab(
                sourceId,
                pointerState.Origin,
                pointerState.Direction,
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
        out GizmoHandlePart hitGizmoPart)
    {
        var length = Mathf.Max(0.01f, maxDistance);
        var hits = Physics.RaycastAll(origin, direction, length, _placeableRaycastMask, QueryTriggerInteraction.Collide);
        if (hits == null || hits.Length == 0)
        {
            firstHit = default;
            hitPlaceable = null;
            hitGizmoPart = null;
            return false;
        }

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        firstHit = hits[0];
        hitPlaceable = null;
        hitGizmoPart = null;

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
        var shader = Shader.Find("Universal Render Pipeline/Unlit");
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
            color = ValidColor,
            enableInstancing = true
        };

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", ValidColor);
        }

        if (material.HasProperty("_Surface"))
        {
            material.SetFloat("_Surface", 1f);
        }

        if (material.HasProperty("_Blend"))
        {
            material.SetFloat("_Blend", 0f);
        }

        return material;
    }

    private struct StylusPointerState
    {
        public bool IsUsable;
        public Vector3 Origin;
        public Vector3 Direction;
        public bool HasHit;
        public RaycastHit Hit;
        public PlaceableAsset HoveredShape;
        public GizmoHandlePart HoveredGizmoPart;
        public bool HasUiHit;
        public WorldSpaceUiRayPointer.Hit UiHit;
    }
}
