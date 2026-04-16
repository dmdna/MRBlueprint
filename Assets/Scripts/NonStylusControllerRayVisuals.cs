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

    [Header("Fallback Line Style")]
    [SerializeField] private float fallbackLineWidth = 0.002f;
    [SerializeField] private Color fallbackLineColor = Color.white;

    private ControllerRayState _leftRay;
    private ControllerRayState _rightRay;
    private Material _runtimeMaterial;
    private bool _leftTriggerWasPressed;
    private bool _rightTriggerWasPressed;

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
        var leftHoveredShape = UpdateRay(_leftRay, leftControllerRayOrigin, false);
        var rightHoveredShape = UpdateRay(_rightRay, rightControllerRayOrigin, true);
        HandleTriggerSelection(false, leftHoveredShape, ref _leftTriggerWasPressed);
        HandleTriggerSelection(true, rightHoveredShape, ref _rightTriggerWasPressed);
    }

    private void OnDestroy()
    {
        if (_runtimeMaterial != null)
        {
            Destroy(_runtimeMaterial);
        }
    }

    private PlaceableAsset UpdateRay(ControllerRayState rayState, Transform rayOrigin, bool isRightHand)
    {
        if (rayState.Line == null || rayOrigin == null || !ShouldUseControllerHand(isRightHand))
        {
            SetVisible(rayState, false);
            return null;
        }

        var origin = rayOrigin.TransformPoint(localPositionOffset);
        var direction = (rayOrigin.rotation * Quaternion.Euler(localEulerOffset)) * Vector3.forward;
        if (direction.sqrMagnitude < 0.0001f)
        {
            SetVisible(rayState, false);
            return null;
        }

        direction.Normalize();
        var mode = ResolveControlMode();
        if (mode == XRControlMode.Selection)
        {
            if (TryGetFirstRayHit(origin, direction, selectionRayLength, out var hit, out var hitPlaceable))
            {
                SetLine(rayState, origin, hit.point);
                return hitPlaceable;
            }

            SetLine(rayState, origin, origin + direction * Mathf.Max(0.01f, selectionRayLength));
            return null;
        }

        if (TryGetFirstRayHit(origin, direction, drawingRayLength, out var drawingHit, out var drawingPlaceable)
            && drawingPlaceable != null)
        {
            SetLine(rayState, origin, drawingHit.point);
            return drawingPlaceable;
        }

        SetVisible(rayState, false);
        return null;
    }

    private bool TryGetFirstRayHit(
        Vector3 origin,
        Vector3 direction,
        float maxDistance,
        out RaycastHit firstHit,
        out PlaceableAsset hitPlaceable)
    {
        var length = Mathf.Max(0.01f, maxDistance);
        var hits = Physics.RaycastAll(origin, direction, length, raycastMask, QueryTriggerInteraction.Collide);
        if (hits == null || hits.Length == 0)
        {
            firstHit = default;
            hitPlaceable = null;
            return false;
        }

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        firstHit = hits[0];
        hitPlaceable = firstHit.collider != null
            ? firstHit.collider.GetComponentInParent<PlaceableAsset>()
            : null;
        return true;
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

    private void HandleTriggerSelection(bool isRightHand, PlaceableAsset hoveredShape, ref bool triggerWasPressed)
    {
        var triggerPressed = ShouldUseControllerHand(isRightHand) && ReadTriggerPressed(isRightHand);
        if (triggerPressed && !triggerWasPressed)
        {
            if (hoveredShape != null)
            {
                AssetSelectionManager.Instance?.SelectAsset(hoveredShape);
            }
            else
            {
                AssetSelectionManager.Instance?.ClearSelection();
            }
        }

        triggerWasPressed = triggerPressed;
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

    private XRControlMode ResolveControlMode()
    {
        ResolveControlModeSource();
        return controlModeSource != null ? controlModeSource.CurrentMode : XRControlMode.Drawing;
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
}
