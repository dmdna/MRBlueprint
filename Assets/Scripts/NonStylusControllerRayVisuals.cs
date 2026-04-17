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
    [SerializeField] private string uiCanvasName = "PlaceableInspectorCanvas";
    [SerializeField] private float uiRayDistance = 8f;

    [Header("Fallback Line Style")]
    [SerializeField] private float fallbackLineWidth = 0.002f;
    [SerializeField] private Color fallbackLineColor = Color.white;

    private const int LeftPointerId = -12001;
    private const int RightPointerId = -12002;

    private ControllerRayState _leftRay;
    private ControllerRayState _rightRay;
    private WorldSpaceUiRayPointer.State _leftUiPointer;
    private WorldSpaceUiRayPointer.State _rightUiPointer;
    private Material _runtimeMaterial;
    private bool _leftTriggerWasPressed;
    private bool _rightTriggerWasPressed;
    private bool _leftGripWasPressed;
    private bool _rightGripWasPressed;
    private int _activeGizmoDragSourceId;

    public static bool AnyControllerGrabActive => PlaceableMultiGrabCoordinator.AnyGrabActive;

    private void Awake()
    {
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
        HandleTriggerSelection(false, leftPointer, ref _leftUiPointer, ref _leftTriggerWasPressed);
        HandleTriggerSelection(true, rightPointer, ref _rightUiPointer, ref _rightTriggerWasPressed);
    }

    private void OnDestroy()
    {
        EndControllerGrabs();
        EndGizmoDrag();

        if (_runtimeMaterial != null)
        {
            Destroy(_runtimeMaterial);
        }
    }

    private void OnDisable()
    {
        EndControllerGrabs();
        EndGizmoDrag();
    }

    private RayPointerState UpdateRay(ControllerRayState rayState, Transform rayOrigin, bool isRightHand)
    {
        if (rayState.Line == null || rayOrigin == null || !ShouldUseControllerHand(isRightHand))
        {
            SetVisible(rayState, false);
            return default;
        }

        var origin = rayOrigin.TransformPoint(localPositionOffset);
        var direction = (rayOrigin.rotation * Quaternion.Euler(localEulerOffset)) * Vector3.forward;
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
            Direction = direction
        };

        if (WorldSpaceUiRayPointer.TryGetHit(
                enableWorldUiPointer,
                uiCanvasName,
                origin,
                direction,
                Mathf.Max(maxRayDistance, uiRayDistance),
                out var uiHit))
        {
            SetLine(rayState, origin, uiHit.WorldPoint);
            pointerState.HasUiHit = true;
            pointerState.UiHit = uiHit;
            pointerState.RayVisible = true;
            return pointerState;
        }

        var mode = ResolveControlMode();
        if (mode == XRControlMode.Selection)
        {
            if (TryGetFirstRayHit(
                    origin,
                    direction,
                    selectionRayLength,
                    out var hit,
                    out var hitPlaceable,
                    out var hitDrawerItem,
                    out var hitGizmoPart,
                    out var hitDrawing))
            {
                SetLine(rayState, origin, hit.point);
                pointerState.HasHit = true;
                pointerState.Hit = hit;
                pointerState.HoveredShape = hitPlaceable;
                pointerState.HoveredDrawerItem = hitDrawerItem;
                pointerState.HoveredGizmoPart = hitGizmoPart;
                pointerState.HoveredDrawing = hitDrawing;
                pointerState.RayVisible = true;

                if (hitDrawerItem != null && hitGizmoPart == null)
                {
                    ResolveDrawerItemSelection();
                    drawerItemSelection?.SelectItem(hitDrawerItem);
                }

                return pointerState;
            }

            SetLine(rayState, origin, origin + direction * Mathf.Max(0.01f, selectionRayLength));
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
                out var drawingSelectable)
            && (drawingPlaceable != null || drawingGizmoPart != null || drawingSelectable != null))
        {
            SetLine(rayState, origin, drawingHit.point);
            pointerState.HasHit = true;
            pointerState.Hit = drawingHit;
            pointerState.HoveredShape = drawingPlaceable;
            pointerState.HoveredGizmoPart = drawingGizmoPart;
            pointerState.HoveredDrawing = drawingSelectable;
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
        out PhysicsDrawingSelectable hitDrawing)
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
            return false;
        }

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        firstHit = hits[0];
        hitPlaceable = null;
        hitDrawerItem = null;
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
            hitDrawerItem = ResolveDrawerItem(hit.collider);
            if (hitDrawerItem != null)
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
        if (PlaceableMultiGrabCoordinator.IsSourceGrabbing(sourceId))
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

        if (PlaceableMultiGrabCoordinator.IsSourceGrabbing(sourceId))
        {
            if (gripPressed)
            {
                UpdateGrab(sourceId, pointerState, rayState, isRightHand);
            }
            else
            {
                PlaceableMultiGrabCoordinator.EndGrab(sourceId);
            }

            gripWasPressed = gripPressed;
            return;
        }

        var grabTarget = ResolvePlaceableGrabTarget(pointerState);
        if (gripPressed && !gripWasPressed && grabTarget != null)
        {
            PlaceableMultiGrabCoordinator.TryBeginGrab(
                sourceId,
                grabTarget,
                pointerState.Origin,
                pointerState.Direction,
                pointerState.HasHit ? pointerState.Hit.distance : selectionRayLength,
                minGrabDistance,
                Mathf.Max(minGrabDistance, maxGrabDistance));
            UpdateGrab(sourceId, pointerState, rayState, isRightHand);
        }

        gripWasPressed = gripPressed;
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
            thumbstickY * thumbstickDepthSpeed * Time.deltaTime,
            minGrabDistance,
            Mathf.Max(minGrabDistance, maxGrabDistance));

        if (PlaceableMultiGrabCoordinator.TryGetSourceGrabPoint(sourceId, out var grabPoint))
        {
            SetLine(rayState, pointerState.Origin, grabPoint);
        }
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

    private XRControlMode ResolveControlMode()
    {
        ResolveControlModeSource();
        return controlModeSource != null ? controlModeSource.CurrentMode : XRControlMode.Drawing;
    }

    private float ResolveModeRayDistance()
    {
        return ResolveControlMode() == XRControlMode.Selection ? selectionRayLength : drawingRayLength;
    }

    private void SetLine(ControllerRayState rayState, Vector3 origin, Vector3 endPoint)
    {
        rayState.Line.SetPosition(0, origin);
        rayState.Line.SetPosition(1, endPoint);
        SetVisible(rayState, true);
    }

    private static void SetVisible(ControllerRayState rayState, bool isVisible)
    {
        if (rayState.Line != null && rayState.Line.enabled != isVisible)
        {
            rayState.Line.enabled = isVisible;
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
    }

    private void ConfigureLine(LineRenderer line)
    {
        line.positionCount = 2;
        line.useWorldSpace = true;
        line.enabled = false;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;

        if (lineTemplate != null)
        {
            line.sharedMaterial = lineTemplate.sharedMaterial;
            line.widthMultiplier = lineTemplate.widthMultiplier;
            line.widthCurve = lineTemplate.widthCurve;
            line.colorGradient = lineTemplate.colorGradient;
            line.alignment = lineTemplate.alignment;
            line.textureMode = lineTemplate.textureMode;
            line.numCornerVertices = lineTemplate.numCornerVertices;
            line.numCapVertices = lineTemplate.numCapVertices;
            return;
        }

        line.sharedMaterial = GetOrCreateRuntimeMaterial();
        line.widthMultiplier = fallbackLineWidth;
        line.startColor = fallbackLineColor;
        line.endColor = fallbackLineColor;
        line.alignment = LineAlignment.View;
    }

    private Material GetOrCreateRuntimeMaterial()
    {
        if (_runtimeMaterial != null)
        {
            return _runtimeMaterial;
        }

        var shader = Shader.Find("Universal Render Pipeline/Unlit");
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
            color = fallbackLineColor,
            enableInstancing = true
        };

        if (_runtimeMaterial.HasProperty("_BaseColor"))
        {
            _runtimeMaterial.SetColor("_BaseColor", fallbackLineColor);
        }

        if (_runtimeMaterial.HasProperty("_Surface"))
        {
            _runtimeMaterial.SetFloat("_Surface", 1f);
        }

        if (_runtimeMaterial.HasProperty("_Blend"))
        {
            _runtimeMaterial.SetFloat("_Blend", 0f);
        }

        return _runtimeMaterial;
    }

    private void ResolveReferences()
    {
        ResolveControlModeSource();
        ResolveDrawerItemSelection();
        ResolveTransformGizmo();

        if (stylusHandler == null)
        {
            stylusHandler = FindFirstObjectByType<VrStylusHandler>(FindObjectsInactive.Include);
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
    }

    private struct RayPointerState
    {
        public bool IsUsable;
        public bool RayVisible;
        public Vector3 Origin;
        public Vector3 Direction;
        public bool HasHit;
        public RaycastHit Hit;
        public PlaceableAsset HoveredShape;
        public XRDrawerItem HoveredDrawerItem;
        public GizmoHandlePart HoveredGizmoPart;
        public PhysicsDrawingSelectable HoveredDrawing;
        public bool HasUiHit;
        public WorldSpaceUiRayPointer.Hit UiHit;
    }
}
