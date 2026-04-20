using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR;
using XRCommonUsages = UnityEngine.XR.CommonUsages;
using XRInputDevice = UnityEngine.XR.InputDevice;
using XRInputDevices = UnityEngine.XR.InputDevices;

public class NonStylusControllerRayVisuals : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private VrStylusHandler stylusHandler;
    [SerializeField] private XRContentDrawerController controlModeSource;
    [SerializeField] private XRDrawerItemSelectionManager drawerItemSelection;
    [SerializeField] private PlaceableTransformGizmo transformGizmo;
    [SerializeField] private Camera transformGizmoCamera;
    [SerializeField] private Transform leftControllerRayOrigin;
    [SerializeField] private Transform rightControllerRayOrigin;
    [SerializeField] private LineRenderer lineTemplate;

    [Header("Ray")]
    [SerializeField] private LayerMask raycastMask = ~0;
    [SerializeField] private float selectionRayLength = 8f;
    [SerializeField] private float drawingRayLength = 30f;
    [SerializeField] private bool requireTrackedController = true;
    [SerializeField] private bool showBothControllersWhenNoStylus = true;
    [SerializeField] private Vector3 localPositionOffset = Vector3.zero;
    [SerializeField] private Vector3 localEulerOffset = Vector3.zero;
    [SerializeField] private float triggerPressThreshold = 0.55f;
    [SerializeField] private float gripPressThreshold = 0.55f;
    [SerializeField] private float thumbstickDepthSpeed = 1.2f;
    [SerializeField] private float thumbstickDeadzone = 0.18f;
    [SerializeField] private float minGrabDistance = 0.25f;
    [SerializeField] private float maxGrabDistance = 8f;

    [Header("UI Pointer")]
    [SerializeField] private bool enableWorldUiPointer = true;
    [SerializeField] private string uiCanvasName = "PlaceableInspectorCanvas;SandboxEditorToolbarCanvas;HomeMenuCanvas";
    [SerializeField] private float uiRayDistance = 8f;

    [Header("Fallback Line Style")]
    [SerializeField] private float fallbackLineWidth = 0.002f;
    [SerializeField] private float endMarkerDiameter = 0.025f;
    [SerializeField] private float endMarkerSurfaceOffset = 0.002f;

    private const int LeftPointerId = -12001;
    private const int RightPointerId = -12002;
    private const int EndMarkerSegments = 32;
    private static readonly Color ControllerRayColor = new(0.78f, 0.78f, 0.78f, 0.38f);

    private ControllerRayState _leftRay;
    private ControllerRayState _rightRay;
    private WorldSpaceUiRayPointer.State _leftUiPointer;
    private WorldSpaceUiRayPointer.State _rightUiPointer;
    private Material _runtimeMaterial;
    private static Mesh _endMarkerMesh;
    private bool _leftTriggerWasPressed;
    private bool _rightTriggerWasPressed;
    private bool _leftGripWasPressed;
    private bool _rightGripWasPressed;
    private bool _leftThumbstickWasPressed;
    private bool _rightThumbstickWasPressed;
    private int _activeGizmoDragSourceId;

    public static bool AnyControllerGrabActive =>
        PlaceableMultiGrabCoordinator.AnyGrabActive
        || PhysicsDrawingEndpointHandle.IsSourceRayDragging(PlaceableMultiGrabCoordinator.LeftControllerSourceId)
        || PhysicsDrawingEndpointHandle.IsSourceRayDragging(PlaceableMultiGrabCoordinator.RightControllerSourceId);

    private void Awake()
    {
        EnsureHomeMenuCanvasFilter();
        ResolveReferences();
        EnsureRay(ref _leftRay, "LeftControllerRayVisual");
        EnsureRay(ref _rightRay, "RightControllerRayVisual");
    }

    private void LateUpdate()
    {
        ResolveReferences();
        EnsureRay(ref _leftRay, "LeftControllerRayVisual");
        EnsureRay(ref _rightRay, "RightControllerRayVisual");
        var leftPointer = UpdateRay(_leftRay, leftControllerRayOrigin, false);
        var rightPointer = UpdateRay(_rightRay, rightControllerRayOrigin, true);
        HandleGripGrab(false, leftPointer, _leftRay, ref _leftGripWasPressed);
        HandleGripGrab(true, rightPointer, _rightRay, ref _rightGripWasPressed);
        HandleThumbstickSnap(false, ref _leftThumbstickWasPressed);
        HandleThumbstickSnap(true, ref _rightThumbstickWasPressed);
        HandleTriggerSelection(false, leftPointer, ref _leftUiPointer, ref _leftTriggerWasPressed);
        HandleTriggerSelection(true, rightPointer, ref _rightUiPointer, ref _rightTriggerWasPressed);
    }

    private void OnDestroy()
    {
        EndControllerGrabs();
        EndControllerEndpointDrags();
        EndGizmoDrag();
        SetVisible(_leftRay, false);
        SetVisible(_rightRay, false);

        if (_runtimeMaterial != null)
        {
            Destroy(_runtimeMaterial);
        }
    }

    private void OnDisable()
    {
        EndControllerGrabs();
        EndControllerEndpointDrags();
        EndGizmoDrag();
        SetVisible(_leftRay, false);
        SetVisible(_rightRay, false);
        _leftThumbstickWasPressed = false;
        _rightThumbstickWasPressed = false;
    }

    private RayPointerState UpdateRay(ControllerRayState rayState, Transform rayOrigin, bool isRightHand)
    {
        if (rayState.Line == null || rayOrigin == null || !ShouldUseControllerHand(isRightHand))
        {
            SetVisible(rayState, false);
            return default;
        }

        var origin = rayOrigin.TransformPoint(ResolveLocalPositionOffset(isRightHand));
        var rayRotation = rayOrigin.rotation * Quaternion.Euler(ResolveLocalEulerOffset(isRightHand));
        var direction = rayRotation * Vector3.forward;
        if (direction.sqrMagnitude < 0.0001f)
        {
            SetVisible(rayState, false);
            return default;
        }

        direction.Normalize();
        var maxRayDistance = ResolveModeRayDistance();
        var pointerState = new RayPointerState
        {
            IsUsable = true,
            Origin = origin,
            Direction = direction,
            Rotation = rayRotation
        };
        var sourceId = ResolveControllerSourceId(isRightHand);

        if (PlaceableMultiGrabCoordinator.IsSourceDirectGrab(sourceId)
            || PhysicsDrawingEndpointHandle.IsSourceDirectDragging(sourceId))
        {
            pointerState.HasDirectHit = true;
            pointerState.DirectPoint = origin;
            pointerState.RayVisible = true;
            SetDirectMarker(rayState, origin, direction);
            return pointerState;
        }

        if (PhysicsDrawingEndpointHandle.TryFindNearestDirectHandle(
                origin,
                ResolveDirectInteractionRadius(),
                out var directEndpoint))
        {
            pointerState.HasDirectHit = true;
            pointerState.DirectPoint = origin;
            pointerState.HoveredDrawing = directEndpoint.Owner;
            pointerState.HoveredDrawingEndpoint = directEndpoint;
            pointerState.RayVisible = true;
            directEndpoint.MarkDirectHovered();
            SetDirectMarker(rayState, origin, direction);
            return pointerState;
        }

        if (TryGetDirectInteractionHit(origin, ResolveDirectInteractionRadius(), out var directHit))
        {
            pointerState.HasDirectHit = true;
            pointerState.DirectPoint = origin;
            pointerState.HoveredShape = directHit.Placeable;
            pointerState.HoveredDrawing = directHit.Drawing;
            pointerState.HoveredDrawingEndpoint = directHit.EndpointHandle;
            pointerState.RayVisible = true;

            if (directHit.EndpointHandle != null)
            {
                directHit.EndpointHandle.MarkDirectHovered();
            }

            SetDirectMarker(rayState, origin, direction);
            return pointerState;
        }

        if (WorldSpaceUiRayPointer.TryGetHit(
                enableWorldUiPointer,
                uiCanvasName,
                origin,
                direction,
                Mathf.Max(maxRayDistance, uiRayDistance),
                out var uiHit))
        {
            SetLine(rayState, origin, ResolveRayEndPoint(origin, direction, uiHit.Distance));
            pointerState.HasUiHit = true;
            pointerState.UiHit = uiHit;
            pointerState.RayVisible = true;
            return pointerState;
        }

        var mode = ResolveControlMode();
        if (mode == XRControlMode.Edit)
        {
            if (TryGetFirstRayHit(
                    origin,
                    direction,
                    selectionRayLength,
                    out var hit,
                    out var hitPlaceable,
                    out var hitDrawerItem,
                    out var hitGizmoPart,
                    out var hitDrawing,
                    out var hitEndpointHandle))
            {
                SetLine(rayState, origin, ResolveRayEndPoint(origin, direction, hit.distance));
                pointerState.HasHit = true;
                pointerState.Hit = hit;
                pointerState.HoveredShape = hitPlaceable;
                pointerState.HoveredDrawerItem = hitDrawerItem;
                pointerState.HoveredGizmoPart = hitGizmoPart;
                pointerState.HoveredDrawing = hitDrawing;
                pointerState.HoveredDrawingEndpoint = hitEndpointHandle;
                pointerState.RayVisible = true;

                if (hitDrawerItem != null && hitGizmoPart == null)
                {
                    ResolveDrawerItemSelection();
                    drawerItemSelection?.SelectItem(hitDrawerItem);
                }

                return pointerState;
            }

            SetLine(rayState, origin, origin + direction * Mathf.Max(0.01f, selectionRayLength), false);
            pointerState.RayVisible = true;
            return pointerState;
        }

        if (TryGetFirstRayHit(
                origin,
                direction,
                drawingRayLength,
                out var drawingHit,
                out var drawingPlaceable,
                out _,
                out var drawingGizmoPart,
                out var drawingSelectable,
                out var drawingEndpointHandle)
            && (drawingPlaceable != null
                || drawingGizmoPart != null
                || drawingSelectable != null
                || drawingEndpointHandle != null))
        {
            SetLine(rayState, origin, ResolveRayEndPoint(origin, direction, drawingHit.distance));
            pointerState.HasHit = true;
            pointerState.Hit = drawingHit;
            pointerState.HoveredShape = drawingPlaceable;
            pointerState.HoveredGizmoPart = drawingGizmoPart;
            pointerState.HoveredDrawing = drawingSelectable;
            pointerState.HoveredDrawingEndpoint = drawingEndpointHandle;
            pointerState.RayVisible = true;
            return pointerState;
        }

        SetVisible(rayState, false);
        return pointerState;
    }

    private bool TryGetFirstRayHit(
        Vector3 origin,
        Vector3 direction,
        float maxDistance,
        out RaycastHit firstHit,
        out PlaceableAsset hitPlaceable,
        out XRDrawerItem hitDrawerItem,
        out GizmoHandlePart hitGizmoPart,
        out PhysicsDrawingSelectable hitDrawing,
        out PhysicsDrawingEndpointHandle hitEndpointHandle)
    {
        var length = Mathf.Max(0.01f, maxDistance);
        var hits = Physics.RaycastAll(origin, direction, length, raycastMask, QueryTriggerInteraction.Collide);
        if (hits == null || hits.Length == 0)
        {
            firstHit = default;
            hitPlaceable = null;
            hitDrawerItem = null;
            hitGizmoPart = null;
            hitDrawing = null;
            hitEndpointHandle = null;
            return TryResolveEndpointRayFallback(
                origin,
                direction,
                length,
                out firstHit,
                out hitDrawing,
                out hitEndpointHandle);
        }

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        firstHit = hits[0];
        hitPlaceable = null;
        hitDrawerItem = null;
        hitGizmoPart = null;
        hitDrawing = null;
        hitEndpointHandle = null;

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
            hitDrawerItem = ResolveDrawerItem(hit.collider);
            if (hitDrawerItem != null)
            {
                firstHit = hit;
                return true;
            }
        }

        if (TryResolveEndpointRayFallback(
                origin,
                direction,
                length,
                out firstHit,
                out hitDrawing,
                out hitEndpointHandle))
        {
            return true;
        }

        foreach (var hit in hits)
        {
            hitEndpointHandle = hit.collider != null
                ? hit.collider.GetComponent<PhysicsDrawingEndpointHandle>()
                : null;
            if (hitEndpointHandle != null && hitEndpointHandle.IsEditable)
            {
                firstHit = hit;
                hitEndpointHandle.MarkRayHovered();
                hitDrawing = hitEndpointHandle.Owner;
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

        return false;
    }

    private static bool TryResolveEndpointRayFallback(
        Vector3 origin,
        Vector3 direction,
        float maxDistance,
        out RaycastHit hit,
        out PhysicsDrawingSelectable drawing,
        out PhysicsDrawingEndpointHandle endpointHandle)
    {
        hit = default;
        drawing = null;
        endpointHandle = null;
        if (!PhysicsDrawingEndpointHandle.TryFindNearestRayHandle(
                origin,
                direction,
                maxDistance,
                out endpointHandle,
                out var hitDistance,
                out var hitPoint))
        {
            return false;
        }

        endpointHandle.MarkRayHovered();
        drawing = endpointHandle.Owner;
        hit.distance = hitDistance;
        hit.point = hitPoint;
        return true;
    }

    private bool TryGetDirectInteractionHit(
        Vector3 origin,
        float radius,
        out DirectInteractionHit directHit)
    {
        directHit = default;
        radius = Mathf.Max(0.001f, radius);
        var colliders = Physics.OverlapSphere(origin, radius, raycastMask, QueryTriggerInteraction.Collide);
        if (colliders == null || colliders.Length == 0)
        {
            return false;
        }

        var nearestDistance = float.MaxValue;
        for (var i = 0; i < colliders.Length; i++)
        {
            var collider = colliders[i];
            var endpoint = collider != null ? collider.GetComponent<PhysicsDrawingEndpointHandle>() : null;
            if (endpoint == null || !endpoint.IsEditable)
            {
                continue;
            }

            TrySetDirectHitCandidate(
                collider,
                origin,
                radius,
                null,
                endpoint.Owner,
                endpoint,
                ref directHit,
                ref nearestDistance);
        }

        if (directHit.EndpointHandle != null)
        {
            return true;
        }

        nearestDistance = float.MaxValue;
        for (var i = 0; i < colliders.Length; i++)
        {
            var collider = colliders[i];
            var drawing = collider != null
                ? collider.GetComponentInParent<PhysicsDrawingSelectable>()
                : null;
            if (drawing == null)
            {
                continue;
            }

            TrySetDirectHitCandidate(
                collider,
                origin,
                radius,
                null,
                drawing,
                null,
                ref directHit,
                ref nearestDistance);
        }

        if (directHit.Drawing != null)
        {
            return true;
        }

        nearestDistance = float.MaxValue;
        for (var i = 0; i < colliders.Length; i++)
        {
            var collider = colliders[i];
            var placeable = collider != null
                ? collider.GetComponentInParent<PlaceableAsset>()
                : null;
            if (placeable == null)
            {
                continue;
            }

            TrySetDirectHitCandidate(
                collider,
                origin,
                radius,
                placeable,
                null,
                null,
                ref directHit,
                ref nearestDistance);
        }

        return directHit.Placeable != null;
    }

    private static void TrySetDirectHitCandidate(
        Collider collider,
        Vector3 origin,
        float maxDistance,
        PlaceableAsset placeable,
        PhysicsDrawingSelectable drawing,
        PhysicsDrawingEndpointHandle endpointHandle,
        ref DirectInteractionHit directHit,
        ref float nearestDistance)
    {
        if (collider == null)
        {
            return;
        }

        var closestPoint = collider.ClosestPoint(origin);
        var distance = Vector3.Distance(origin, closestPoint);
        if (distance > maxDistance || distance >= nearestDistance)
        {
            return;
        }

        nearestDistance = distance;
        directHit = new DirectInteractionHit
        {
            Point = closestPoint,
            Distance = distance,
            Placeable = placeable,
            Drawing = drawing,
            EndpointHandle = endpointHandle
        };
    }

    private static XRDrawerItem ResolveDrawerItem(Collider collider)
    {
        if (collider == null || collider.GetComponent<DrawerTilePickTarget>() == null)
        {
            return null;
        }

        return collider.GetComponentInParent<XRDrawerItem>();
    }

    private bool ShouldUseControllerHand(bool isRightHand)
    {
        if (stylusHandler != null)
        {
            var stylus = stylusHandler.CurrentState;
            if (stylus.isActive)
            {
                return stylus.isOnRightHand != isRightHand && IsTrackedNonStylusController(isRightHand);
            }

            if (!showBothControllersWhenNoStylus)
            {
                return false;
            }
        }

        return IsTrackedNonStylusController(isRightHand);
    }

    private bool IsTrackedNonStylusController(bool isRightHand)
    {
        var device = XRInputDevices.GetDeviceAtXRNode(isRightHand ? XRNode.RightHand : XRNode.LeftHand);
        if (!device.isValid)
        {
            return !requireTrackedController;
        }

        if (IsLogitechStylus(device))
        {
            return false;
        }

        return !device.TryGetFeatureValue(XRCommonUsages.isTracked, out var isTracked) || isTracked;
    }

    private void HandleTriggerSelection(
        bool isRightHand,
        RayPointerState pointerState,
        ref WorldSpaceUiRayPointer.State uiPointerState,
        ref bool triggerWasPressed)
    {
        var sourceId = ResolveControllerSourceId(isRightHand);
        if (PlaceableMultiGrabCoordinator.IsSourceGrabbing(sourceId)
            || PhysicsDrawingEndpointHandle.IsSourceRayDragging(sourceId))
        {
            triggerWasPressed = ReadTriggerPressed(isRightHand);
            return;
        }

        var triggerPressed = ShouldUseControllerHand(isRightHand) && ReadTriggerPressed(isRightHand);
        if (HandleGizmoDrag(sourceId, pointerState, triggerPressed, ref triggerWasPressed))
        {
            return;
        }

        if (WorldSpaceUiRayPointer.Handle(
                enableWorldUiPointer,
                pointerState.HasUiHit,
                pointerState.UiHit,
                triggerPressed,
                ref uiPointerState,
                isRightHand ? RightPointerId : LeftPointerId))
        {
            triggerWasPressed = triggerPressed;
            return;
        }

        if (triggerPressed && !triggerWasPressed)
        {
            if (pointerState.HoveredDrawerItem != null)
            {
                ResolveDrawerItemSelection();
                if (drawerItemSelection != null)
                {
                    drawerItemSelection.SelectItem(pointerState.HoveredDrawerItem);
                    drawerItemSelection.TryConfirmSpawnSelected();
                }
            }
            else if (pointerState.HoveredGizmoPart != null)
            {
                // Gizmo hover without a successful drag should not deselect the current object.
            }
            else if (pointerState.HoveredShape != null)
            {
                AssetSelectionManager.Instance?.SelectAsset(pointerState.HoveredShape);
            }
            else if (pointerState.HoveredDrawingEndpoint != null)
            {
                AssetSelectionManager.Instance?.SelectPhysicsDrawing(pointerState.HoveredDrawingEndpoint.Owner);
            }
            else if (pointerState.HoveredDrawing != null)
            {
                AssetSelectionManager.Instance?.SelectPhysicsDrawing(pointerState.HoveredDrawing);
            }
            else
            {
                AssetSelectionManager.Instance?.ClearSelection();
            }
        }

        triggerWasPressed = triggerPressed;
    }

    private bool HandleGizmoDrag(
        int sourceId,
        RayPointerState pointerState,
        bool triggerPressed,
        ref bool triggerWasPressed)
    {
        if (_activeGizmoDragSourceId != 0 && _activeGizmoDragSourceId != sourceId)
        {
            triggerWasPressed = triggerPressed;
            return true;
        }

        if (_activeGizmoDragSourceId == sourceId)
        {
            if (triggerPressed && pointerState.IsUsable)
            {
                var cam = ResolveTransformGizmoCamera();
                if (transformGizmo != null && cam != null)
                {
                    transformGizmo.Drag(new Ray(pointerState.Origin, pointerState.Direction), cam);
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

            triggerWasPressed = triggerPressed;
            return true;
        }

        if (!triggerPressed || triggerWasPressed || !pointerState.IsUsable || pointerState.HasUiHit)
        {
            return false;
        }

        ResolveTransformGizmo();
        var gizmo = transformGizmo;
        var gizmoCamera = ResolveTransformGizmoCamera();
        if (gizmo == null || gizmoCamera == null)
        {
            return false;
        }

        var ray = new Ray(pointerState.Origin, pointerState.Direction);
        var maxDistance = Mathf.Max(selectionRayLength, drawingRayLength, uiRayDistance);
        if (!gizmo.TryBeginDrag(ray, maxDistance, gizmoCamera))
        {
            return pointerState.HoveredGizmoPart != null;
        }

        _activeGizmoDragSourceId = sourceId;
        gizmo.Drag(ray, gizmoCamera);
        triggerWasPressed = triggerPressed;
        return true;
    }

    private bool ReadTriggerPressed(bool isRightHand)
    {
        var device = XRInputDevices.GetDeviceAtXRNode(isRightHand ? XRNode.RightHand : XRNode.LeftHand);
        if (!device.isValid || IsLogitechStylus(device))
        {
            return false;
        }

        if (device.TryGetFeatureValue(XRCommonUsages.triggerButton, out var triggerButtonPressed)
            && triggerButtonPressed)
        {
            return true;
        }

        return device.TryGetFeatureValue(XRCommonUsages.trigger, out var triggerValue)
               && triggerValue >= triggerPressThreshold;
    }

    private void HandleGripGrab(
        bool isRightHand,
        RayPointerState pointerState,
        ControllerRayState rayState,
        ref bool gripWasPressed)
    {
        var sourceId = ResolveControllerSourceId(isRightHand);
        var gripPressed = pointerState.IsUsable && ReadGripPressed(isRightHand);

        if (PhysicsDrawingEndpointHandle.IsSourceRayDragging(sourceId))
        {
            if (gripPressed)
            {
                if (PhysicsDrawingEndpointHandle.IsSourceDirectDragging(sourceId))
                {
                    UpdateEndpointDirectDrag(sourceId, pointerState, rayState);
                }
                else
                {
                    UpdateEndpointDrag(sourceId, pointerState, rayState, isRightHand);
                }
            }
            else
            {
                PhysicsDrawingEndpointHandle.EndRayDrag(sourceId);
            }

            gripWasPressed = gripPressed;
            return;
        }

        if (PlaceableMultiGrabCoordinator.IsSourceGrabbing(sourceId))
        {
            if (gripPressed)
            {
                if (PlaceableMultiGrabCoordinator.IsSourceDirectGrab(sourceId))
                {
                    UpdateDirectGrab(sourceId, pointerState, rayState);
                }
                else
                {
                    UpdateGrab(sourceId, pointerState, rayState, isRightHand);
                }
            }
            else
            {
                PlaceableMultiGrabCoordinator.EndGrab(sourceId);
            }

            gripWasPressed = gripPressed;
            return;
        }

        var endpointGrabTarget = pointerState.HoveredDrawingEndpoint;
        var placeableGrabTarget = ResolvePlaceableGrabTarget(pointerState);
        var drawingGrabTarget = ResolvePhysicsDrawingGrabTarget(pointerState);
        if (gripPressed && !gripWasPressed)
        {
            var grabStarted = false;
            if (endpointGrabTarget != null)
            {
                grabStarted = pointerState.HasDirectHit
                    ? PhysicsDrawingEndpointHandle.TryBeginDirectDrag(
                        sourceId,
                        endpointGrabTarget,
                        pointerState.DirectPoint)
                    : PhysicsDrawingEndpointHandle.TryBeginRayDrag(
                        sourceId,
                        endpointGrabTarget,
                        pointerState.Origin,
                        pointerState.Direction,
                        ResolvePointerHitDistance(pointerState),
                        minGrabDistance,
                        Mathf.Max(minGrabDistance, maxGrabDistance));
            }
            else if (placeableGrabTarget != null)
            {
                grabStarted = pointerState.HasDirectHit
                    ? PlaceableMultiGrabCoordinator.TryBeginDirectGrab(
                        sourceId,
                        placeableGrabTarget,
                        pointerState.DirectPoint,
                        pointerState.Rotation)
                    : PlaceableMultiGrabCoordinator.TryBeginGrab(
                        sourceId,
                        placeableGrabTarget,
                        pointerState.Origin,
                        pointerState.Direction,
                        pointerState.Rotation,
                        ResolvePointerHitDistance(pointerState),
                        minGrabDistance,
                        Mathf.Max(minGrabDistance, maxGrabDistance));
            }
            else if (drawingGrabTarget != null)
            {
                grabStarted = pointerState.HasDirectHit
                    ? PlaceableMultiGrabCoordinator.TryBeginDirectGrab(
                        sourceId,
                        drawingGrabTarget,
                        pointerState.DirectPoint,
                        pointerState.Rotation)
                    : PlaceableMultiGrabCoordinator.TryBeginGrab(
                        sourceId,
                        drawingGrabTarget,
                        pointerState.Origin,
                        pointerState.Direction,
                        pointerState.Rotation,
                        ResolvePointerHitDistance(pointerState),
                        minGrabDistance,
                        Mathf.Max(minGrabDistance, maxGrabDistance));
            }

            if (grabStarted)
            {
                if (PhysicsDrawingEndpointHandle.IsSourceDirectDragging(sourceId))
                {
                    UpdateEndpointDirectDrag(sourceId, pointerState, rayState);
                }
                else if (PhysicsDrawingEndpointHandle.IsSourceRayDragging(sourceId))
                {
                    UpdateEndpointDrag(sourceId, pointerState, rayState, isRightHand);
                }
                else if (PlaceableMultiGrabCoordinator.IsSourceDirectGrab(sourceId))
                {
                    UpdateDirectGrab(sourceId, pointerState, rayState);
                }
                else
                {
                    UpdateGrab(sourceId, pointerState, rayState, isRightHand);
                }
            }
        }

        gripWasPressed = gripPressed;
    }

    private void HandleThumbstickSnap(bool isRightHand, ref bool thumbstickWasPressed)
    {
        var pressed = ShouldUseControllerHand(isRightHand) && ReadThumbstickClickPressed(isRightHand);
        if (pressed && !thumbstickWasPressed)
        {
            var sourceId = ResolveControllerSourceId(isRightHand);
            if (!PlaceableMultiGrabCoordinator.IsSourceGrabbingPhysicsDrawing(sourceId)
                && !PhysicsDrawingEndpointHandle.IsSourceRayDragging(sourceId))
            {
                PlaceableMultiGrabCoordinator.SnapGrabbedPlaceablesUprightPreserveYaw();
            }
        }

        thumbstickWasPressed = pressed;
    }

    private static PlaceableAsset ResolvePlaceableGrabTarget(RayPointerState pointerState)
    {
        if (pointerState.HoveredShape != null)
        {
            return pointerState.HoveredShape;
        }

        return pointerState.HoveredGizmoPart != null
            ? AssetSelectionManager.Instance?.SelectedAsset
            : null;
    }

    private static PhysicsDrawingSelectable ResolvePhysicsDrawingGrabTarget(RayPointerState pointerState)
    {
        return pointerState.HoveredDrawing;
    }

    private float ResolvePointerHitDistance(RayPointerState pointerState)
    {
        return pointerState.HasHit ? pointerState.Hit.distance : selectionRayLength;
    }

    private bool ReadGripPressed(bool isRightHand)
    {
        var device = XRInputDevices.GetDeviceAtXRNode(isRightHand ? XRNode.RightHand : XRNode.LeftHand);
        if (!device.isValid || IsLogitechStylus(device))
        {
            return false;
        }

        if (device.TryGetFeatureValue(XRCommonUsages.gripButton, out var gripButtonPressed)
            && gripButtonPressed)
        {
            return true;
        }

        return device.TryGetFeatureValue(XRCommonUsages.grip, out var gripValue)
               && gripValue >= gripPressThreshold;
    }

    private float ReadThumbstickY(bool isRightHand)
    {
        var device = XRInputDevices.GetDeviceAtXRNode(isRightHand ? XRNode.RightHand : XRNode.LeftHand);
        if (!device.isValid || IsLogitechStylus(device))
        {
            return 0f;
        }

        if (!device.TryGetFeatureValue(XRCommonUsages.primary2DAxis, out var axis))
        {
            return 0f;
        }

        return Mathf.Abs(axis.y) >= thumbstickDeadzone ? axis.y : 0f;
    }

    private bool ReadThumbstickClickPressed(bool isRightHand)
    {
        var device = XRInputDevices.GetDeviceAtXRNode(isRightHand ? XRNode.RightHand : XRNode.LeftHand);
        return device.isValid
               && !IsLogitechStylus(device)
               && device.TryGetFeatureValue(XRCommonUsages.primary2DAxisClick, out var pressed)
               && pressed;
    }

    private void UpdateGrab(
        int sourceId,
        RayPointerState pointerState,
        ControllerRayState rayState,
        bool isRightHand)
    {
        if (!pointerState.IsUsable)
        {
            PlaceableMultiGrabCoordinator.EndGrab(sourceId);
            return;
        }

        var thumbstickY = ReadThumbstickY(isRightHand);
        PlaceableMultiGrabCoordinator.UpdateGrab(
            sourceId,
            pointerState.Origin,
            pointerState.Direction,
            pointerState.Rotation,
            thumbstickY * thumbstickDepthSpeed * Time.deltaTime,
            minGrabDistance,
            Mathf.Max(minGrabDistance, maxGrabDistance));

        if (PlaceableMultiGrabCoordinator.TryGetSourceGrabDistance(sourceId, out var grabDistance))
        {
            SetLine(
                rayState,
                pointerState.Origin,
                ResolveRayEndPoint(pointerState.Origin, pointerState.Direction, grabDistance));
        }
    }

    private void UpdateDirectGrab(
        int sourceId,
        RayPointerState pointerState,
        ControllerRayState rayState)
    {
        if (!pointerState.IsUsable)
        {
            PlaceableMultiGrabCoordinator.EndGrab(sourceId);
            return;
        }

        PlaceableMultiGrabCoordinator.UpdateDirectGrab(
            sourceId,
            pointerState.Origin,
            pointerState.Rotation);
        SetDirectMarker(rayState, pointerState.Origin, pointerState.Direction);
    }

    private void UpdateEndpointDrag(
        int sourceId,
        RayPointerState pointerState,
        ControllerRayState rayState,
        bool isRightHand)
    {
        if (!pointerState.IsUsable)
        {
            PhysicsDrawingEndpointHandle.EndRayDrag(sourceId);
            return;
        }

        var thumbstickY = ReadThumbstickY(isRightHand);
        PhysicsDrawingEndpointHandle.UpdateRayDrag(
            sourceId,
            pointerState.Origin,
            pointerState.Direction,
            thumbstickY * thumbstickDepthSpeed * Time.deltaTime,
            minGrabDistance,
            Mathf.Max(minGrabDistance, maxGrabDistance));

        if (PhysicsDrawingEndpointHandle.TryGetSourceGrabDistance(sourceId, out var grabDistance))
        {
            SetLine(
                rayState,
                pointerState.Origin,
                ResolveRayEndPoint(pointerState.Origin, pointerState.Direction, grabDistance));
        }
    }

    private void UpdateEndpointDirectDrag(
        int sourceId,
        RayPointerState pointerState,
        ControllerRayState rayState)
    {
        if (!pointerState.IsUsable)
        {
            PhysicsDrawingEndpointHandle.EndRayDrag(sourceId);
            return;
        }

        PhysicsDrawingEndpointHandle.UpdateDirectDrag(sourceId, pointerState.Origin);
        SetDirectMarker(rayState, pointerState.Origin, pointerState.Direction);
    }

    private static int ResolveControllerSourceId(bool isRightHand)
    {
        return isRightHand
            ? PlaceableMultiGrabCoordinator.RightControllerSourceId
            : PlaceableMultiGrabCoordinator.LeftControllerSourceId;
    }

    private static void EndControllerGrabs()
    {
        PlaceableMultiGrabCoordinator.EndGrab(PlaceableMultiGrabCoordinator.LeftControllerSourceId);
        PlaceableMultiGrabCoordinator.EndGrab(PlaceableMultiGrabCoordinator.RightControllerSourceId);
    }

    private static void EndControllerEndpointDrags()
    {
        PhysicsDrawingEndpointHandle.EndRayDrag(PlaceableMultiGrabCoordinator.LeftControllerSourceId);
        PhysicsDrawingEndpointHandle.EndRayDrag(PlaceableMultiGrabCoordinator.RightControllerSourceId);
    }

    private XRControlMode ResolveControlMode()
    {
        ResolveControlModeSource();
        return controlModeSource != null ? controlModeSource.CurrentMode : XRControlMode.Drawing;
    }

    private float ResolveModeRayDistance()
    {
        return ResolveControlMode() == XRControlMode.Edit ? selectionRayLength : drawingRayLength;
    }

    private Vector3 ResolveLocalPositionOffset(bool isRightHand)
    {
        var offset = localPositionOffset;
        offset.x = Mathf.Abs(offset.x) * (isRightHand ? -1f : 1f);
        return offset;
    }

    private Vector3 ResolveLocalEulerOffset(bool isRightHand)
    {
        var offset = localEulerOffset;
        offset.y = Mathf.Abs(offset.y) * (isRightHand ? -1f : 1f);
        return offset;
    }

    private float ResolveDirectInteractionRadius()
    {
        return Mathf.Max(0.001f, endMarkerDiameter * 0.5f);
    }

    private static Vector3 ResolveRayEndPoint(Vector3 origin, Vector3 direction, float distance)
    {
        return origin + direction * Mathf.Max(0.01f, distance);
    }

    private void SetLine(ControllerRayState rayState, Vector3 origin, Vector3 endPoint)
    {
        SetLine(rayState, origin, endPoint, true);
    }

    private void SetLine(ControllerRayState rayState, Vector3 origin, Vector3 endPoint, bool showEndMarker)
    {
        rayState.Line.SetPosition(0, origin);
        rayState.Line.SetPosition(1, endPoint);
        SetVisible(rayState, true);
        SetEndMarker(rayState, endPoint, endPoint - origin, showEndMarker);
    }

    private void SetDirectMarker(ControllerRayState rayState, Vector3 markerCenter, Vector3 direction)
    {
        if (rayState.Line != null && rayState.Line.enabled)
        {
            rayState.Line.enabled = false;
        }

        SetEndMarkerCenter(rayState, markerCenter, direction, true);
    }

    private static void SetVisible(ControllerRayState rayState, bool isVisible)
    {
        if (rayState.Line != null && rayState.Line.enabled != isVisible)
        {
            rayState.Line.enabled = isVisible;
        }

        if (!isVisible && rayState.EndMarker != null && rayState.EndMarker.gameObject.activeSelf)
        {
            rayState.EndMarker.gameObject.SetActive(false);
        }
    }

    private void SetEndMarker(
        ControllerRayState rayState,
        Vector3 endPoint,
        Vector3 rayVector,
        bool isVisible)
    {
        if (rayState.EndMarker == null)
        {
            return;
        }

        if (!isVisible || rayVector.sqrMagnitude <= 0.000001f)
        {
            rayState.EndMarker.gameObject.SetActive(false);
            return;
        }

        var direction = rayVector.normalized;
        SetEndMarkerCenter(
            rayState,
            endPoint - direction * Mathf.Max(0f, endMarkerSurfaceOffset),
            direction,
            true);
    }

    private void SetEndMarkerCenter(
        ControllerRayState rayState,
        Vector3 center,
        Vector3 direction,
        bool isVisible)
    {
        if (rayState.EndMarker == null)
        {
            return;
        }

        if (!isVisible || direction.sqrMagnitude <= 0.000001f)
        {
            rayState.EndMarker.gameObject.SetActive(false);
            return;
        }

        direction.Normalize();
        rayState.EndMarker.position = center;
        rayState.EndMarker.rotation = Quaternion.LookRotation(-direction, Vector3.up);
        rayState.EndMarker.localScale = Vector3.one * Mathf.Max(0.001f, endMarkerDiameter);
        if (!rayState.EndMarker.gameObject.activeSelf)
        {
            rayState.EndMarker.gameObject.SetActive(true);
        }
    }

    private void EnsureRay(ref ControllerRayState rayState, string name)
    {
        if (rayState.Line != null)
        {
            return;
        }

        var rayObject = new GameObject(name);
        rayObject.transform.SetParent(transform, false);
        rayState.Line = rayObject.AddComponent<LineRenderer>();
        ConfigureLine(rayState.Line);
        EnsureEndMarker(ref rayState, name + "EndMarker");
    }

    private void ConfigureLine(LineRenderer line)
    {
        line.positionCount = 2;
        line.useWorldSpace = true;
        line.enabled = false;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;

        line.sharedMaterial = GetOrCreateRuntimeMaterial();
        line.widthMultiplier = lineTemplate != null ? lineTemplate.widthMultiplier : fallbackLineWidth;
        line.widthCurve = lineTemplate != null ? lineTemplate.widthCurve : AnimationCurve.Constant(0f, 1f, 1f);
        line.startColor = ControllerRayColor;
        line.endColor = ControllerRayColor;
        line.colorGradient = BuildGradient(ControllerRayColor);
        line.alignment = lineTemplate != null ? lineTemplate.alignment : LineAlignment.View;
        line.textureMode = LineTextureMode.Stretch;
        line.numCornerVertices = lineTemplate != null ? lineTemplate.numCornerVertices : 0;
        line.numCapVertices = lineTemplate != null ? lineTemplate.numCapVertices : 0;
    }

    private void EnsureEndMarker(ref ControllerRayState rayState, string name)
    {
        if (rayState.EndMarker != null)
        {
            return;
        }

        var markerObject = new GameObject(name);
        markerObject.transform.SetParent(transform, false);
        var meshFilter = markerObject.AddComponent<MeshFilter>();
        meshFilter.sharedMesh = GetOrCreateEndMarkerMesh();
        rayState.EndMarkerRenderer = markerObject.AddComponent<MeshRenderer>();
        rayState.EndMarkerRenderer.sharedMaterial = GetOrCreateRuntimeMaterial();
        rayState.EndMarkerRenderer.shadowCastingMode = ShadowCastingMode.Off;
        rayState.EndMarkerRenderer.receiveShadows = false;
        rayState.EndMarker = markerObject.transform;
        markerObject.SetActive(false);
    }

    private Material GetOrCreateRuntimeMaterial()
    {
        if (_runtimeMaterial != null)
        {
            return _runtimeMaterial;
        }

        var shader = Shader.Find("MRBlueprint/RayNoStackTransparent");
        if (shader == null)
        {
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        }

        if (shader == null)
        {
            shader = Shader.Find("Sprites/Default");
        }

        if (shader == null)
        {
            return null;
        }

        _runtimeMaterial = new Material(shader)
        {
            name = "ControllerRayRuntimeMaterial",
            color = ControllerRayColor,
            enableInstancing = true
        };

        if (_runtimeMaterial.HasProperty("_BaseColor"))
        {
            _runtimeMaterial.SetColor("_BaseColor", ControllerRayColor);
        }

        if (_runtimeMaterial.HasProperty("_Color"))
        {
            _runtimeMaterial.SetColor("_Color", ControllerRayColor);
        }

        if (_runtimeMaterial.HasProperty("_BaseMap"))
        {
            _runtimeMaterial.SetTexture("_BaseMap", Texture2D.whiteTexture);
        }

        if (_runtimeMaterial.HasProperty("_MainTex"))
        {
            _runtimeMaterial.SetTexture("_MainTex", Texture2D.whiteTexture);
        }

        if (_runtimeMaterial.HasProperty("_Surface"))
        {
            _runtimeMaterial.SetFloat("_Surface", 1f);
        }

        if (_runtimeMaterial.HasProperty("_Blend"))
        {
            _runtimeMaterial.SetFloat("_Blend", 0f);
        }

        if (_runtimeMaterial.HasProperty("_Cull"))
        {
            _runtimeMaterial.SetFloat("_Cull", (float)CullMode.Off);
        }

        ConfigureTransparentMaterial(_runtimeMaterial);
        return _runtimeMaterial;
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
            name = "RayEndSphere"
        };
        _endMarkerMesh.vertices = vertices;
        _endMarkerMesh.triangles = triangles;
        _endMarkerMesh.RecalculateBounds();
        _endMarkerMesh.RecalculateNormals();
        return _endMarkerMesh;
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

    private void ResolveReferences()
    {
        ResolveControllerRayOrigins();
        ResolveControlModeSource();
        ResolveDrawerItemSelection();
        ResolveTransformGizmo();

        if (stylusHandler == null)
        {
            stylusHandler = FindFirstObjectByType<VrStylusHandler>(FindObjectsInactive.Include);
        }
    }

    private void ResolveControllerRayOrigins()
    {
        if (leftControllerRayOrigin == null)
        {
            leftControllerRayOrigin = FindTransformByName("LeftControllerAnchor");
        }

        if (rightControllerRayOrigin == null)
        {
            rightControllerRayOrigin = FindTransformByName("RightControllerAnchor");
        }
    }

    private void ResolveControlModeSource()
    {
        if (controlModeSource == null)
        {
            controlModeSource = FindFirstObjectByType<XRContentDrawerController>(FindObjectsInactive.Include);
        }
    }

    private void ResolveDrawerItemSelection()
    {
        if (drawerItemSelection == null)
        {
            drawerItemSelection = FindFirstObjectByType<XRDrawerItemSelectionManager>(FindObjectsInactive.Include);
        }
    }

    private void ResolveTransformGizmo()
    {
        if (transformGizmo == null)
        {
            transformGizmo = FindFirstObjectByType<PlaceableTransformGizmo>(FindObjectsInactive.Include);
        }
    }

    private void EnsureHomeMenuCanvasFilter()
    {
        uiCanvasName = IncludeCanvasName(uiCanvasName, "HomeMenuCanvas");
    }

    private static string IncludeCanvasName(string canvasNames, string requiredCanvasName)
    {
        if (string.IsNullOrWhiteSpace(canvasNames))
        {
            return requiredCanvasName;
        }

        var names = canvasNames.Split(new[] { ';', ',', '|' }, System.StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < names.Length; i++)
        {
            if (string.Equals(names[i].Trim(), requiredCanvasName, System.StringComparison.Ordinal))
            {
                return canvasNames;
            }
        }

        return canvasNames.TrimEnd(';', ',', '|', ' ') + ";" + requiredCanvasName;
    }

    private static Transform FindTransformByName(string objectName)
    {
        var transforms = FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (var transform in transforms)
        {
            if (transform != null && transform.name == objectName)
            {
                return transform;
            }
        }

        return null;
    }

    private Camera ResolveTransformGizmoCamera()
    {
        ResolveTransformGizmo();
        if (transformGizmoCamera != null && transformGizmoCamera.isActiveAndEnabled)
        {
            return transformGizmoCamera;
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
        if (_activeGizmoDragSourceId == 0)
        {
            return;
        }

        transformGizmo?.EndDrag();
        _activeGizmoDragSourceId = 0;
    }

    private static bool IsLogitechStylus(XRInputDevice device)
    {
        return ContainsDeviceText(device.name, "Logitech")
               || ContainsDeviceText(device.name, "MX Ink")
               || ContainsDeviceText(device.name, "Stylus")
               || ContainsDeviceText(device.manufacturer, "Logitech");
    }

    private static bool ContainsDeviceText(string value, string match)
    {
        return !string.IsNullOrEmpty(value)
               && value.IndexOf(match, System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private struct ControllerRayState
    {
        public LineRenderer Line;
        public Transform EndMarker;
        public MeshRenderer EndMarkerRenderer;
    }

    private struct DirectInteractionHit
    {
        public Vector3 Point;
        public float Distance;
        public PlaceableAsset Placeable;
        public PhysicsDrawingSelectable Drawing;
        public PhysicsDrawingEndpointHandle EndpointHandle;
    }

    private struct RayPointerState
    {
        public bool IsUsable;
        public bool RayVisible;
        public Vector3 Origin;
        public Vector3 Direction;
        public Quaternion Rotation;
        public bool HasDirectHit;
        public Vector3 DirectPoint;
        public bool HasHit;
        public RaycastHit Hit;
        public PlaceableAsset HoveredShape;
        public XRDrawerItem HoveredDrawerItem;
        public GizmoHandlePart HoveredGizmoPart;
        public PhysicsDrawingSelectable HoveredDrawing;
        public PhysicsDrawingEndpointHandle HoveredDrawingEndpoint;
        public bool HasUiHit;
        public WorldSpaceUiRayPointer.Hit UiHit;
    }
}
