using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using XRInputDevice = UnityEngine.XR.InputDevice;
using InputSystemDevice = UnityEngine.InputSystem.InputDevice;
using XRCommonUsages = UnityEngine.XR.CommonUsages;

public class VrStylusHandler : StylusHandler
{
    [SerializeField] private GameObject _mxInk_model;
    [SerializeField] private GameObject _tip;
    [SerializeField] private GameObject _cluster_front;
    [SerializeField] private GameObject _cluster_middle;
    [SerializeField] private GameObject _cluster_back;

    [SerializeField] private GameObject _left_touch_controller;
    [SerializeField] private GameObject _right_touch_controller;

    public Color active_color = Color.green;
    public Color double_tap_active_color = Color.cyan;
    public Color default_color = Color.white;

    private float _hapticClickDuration = 0.011f;
    private float _hapticClickAmplitude = 1.0f;

    public Transform TipTransform => _tip != null ? _tip.transform : transform;
    public bool IsTrackingStylus => _stylus.isActive && _currentTrackedDevice.isValid;

    [SerializeField]
    private InputActionReference _middleActionRef;
    [SerializeField]
    private InputActionReference _tipActionRef;
    [SerializeField]
    private InputActionReference _grabActionRef;
    [SerializeField]
    private InputActionReference _optionActionRef;

    private XRInputDevice _currentTrackedDevice;
    private MeshRenderer _tipRenderer;
    private MeshRenderer _clusterFrontRenderer;
    private MeshRenderer _clusterMiddleRenderer;
    private MeshRenderer _clusterBackRenderer;
    private MaterialPropertyBlock _tipPropertyBlock;
    private MaterialPropertyBlock _clusterFrontPropertyBlock;
    private MaterialPropertyBlock _clusterMiddlePropertyBlock;
    private MaterialPropertyBlock _clusterBackPropertyBlock;

    private void Awake()
    {
        _tipActionRef.action.Enable();
        _grabActionRef.action.Enable();
        _optionActionRef.action.Enable();
        _middleActionRef.action.Enable();

        _stylus.isActive = false;
        InputSystem.onDeviceChange += OnDeviceChange;
        InputDevices.deviceConnected += DeviceConnected;

        CacheRenderers();
    }

    private void OnEnable()
    {
        RefreshStylusState();
    }

    private void OnDestroy()
    {
        InputSystem.onDeviceChange -= OnDeviceChange;
        InputDevices.deviceConnected -= DeviceConnected;
    }

    private void DeviceConnected(XRInputDevice device)
    {
        Debug.Log($"Device connected: {device.name}");
        RefreshStylusState();
    }

    private void OnDeviceChange(InputSystemDevice device, InputDeviceChange change)
    {
        if (device.name.ToLower().Contains("logitech"))
        {
            switch (change)
            {
                case InputDeviceChange.Disconnected:
                    _tipActionRef.action.Disable();
                    _grabActionRef.action.Disable();
                    _optionActionRef.action.Disable();
                    _middleActionRef.action.Disable();
                    break;
                case InputDeviceChange.Reconnected:
                    _tipActionRef.action.Enable();
                    _grabActionRef.action.Enable();
                    _optionActionRef.action.Enable();
                    _middleActionRef.action.Enable();
                    break;
            }
        }

        RefreshStylusState();
    }

    void Update()
    {
        RefreshStylusState();

        if (_stylus.isActive)
        {
            GetControllerTransform(_currentTrackedDevice);
        }

        _stylus.inkingPose.position = transform.position;
        _stylus.inkingPose.rotation = transform.rotation;
        _stylus.tip_value = _tipActionRef.action.ReadValue<float>();
        _stylus.cluster_middle_value = _middleActionRef.action.ReadValue<float>();
        _stylus.cluster_front_value = _grabActionRef.action.IsPressed();
        _stylus.cluster_back_value = _optionActionRef.action.IsPressed();

        _stylus.any = _stylus.tip_value > 0 || _stylus.cluster_front_value ||
                        _stylus.cluster_middle_value > 0 || _stylus.cluster_back_value ||
                        _stylus.cluster_back_double_tap_value;

        SetRendererColor(_tipRenderer, _tipPropertyBlock, _stylus.tip_value > 0 ? active_color : default_color);
        SetRendererColor(_clusterFrontRenderer, _clusterFrontPropertyBlock, _stylus.cluster_front_value ? active_color : default_color);
        SetRendererColor(_clusterMiddleRenderer, _clusterMiddlePropertyBlock, _stylus.cluster_middle_value > 0 ? active_color : default_color);
        SetRendererColor(_clusterBackRenderer, _clusterBackPropertyBlock, _stylus.cluster_back_value ? active_color : default_color);

    }

    void GetControllerTransform(XRInputDevice device)
    {
        if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.devicePosition, out Vector3 position))
        {
            if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.deviceRotation, out Quaternion rotation))
            {
                // Apply transform to a GameObject if needed
                transform.position = position;
                transform.rotation = rotation;
            }
        }
    }

    public void TriggerHapticPulse(float amplitude, float duration)
    {
        var device = _currentTrackedDevice.isValid
            ? _currentTrackedDevice
            : InputDevices.GetDeviceAtXRNode(_stylus.isOnRightHand ? XRNode.RightHand : XRNode.LeftHand);
        device.SendHapticImpulse(0, amplitude, duration);
    }

    public void TriggerHapticClick()
    {
        TriggerHapticPulse(_hapticClickAmplitude, _hapticClickDuration);
    }

    private void RefreshStylusState()
    {
        var rightDevice = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        var leftDevice = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);

        var rightIsStylus = IsTrackedStylusDevice(rightDevice);
        var leftIsStylus = IsTrackedStylusDevice(leftDevice);

        if (rightIsStylus)
        {
            _stylus.isOnRightHand = true;
            _stylus.isActive = true;
            _currentTrackedDevice = rightDevice;
        }
        else if (leftIsStylus)
        {
            _stylus.isOnRightHand = false;
            _stylus.isActive = true;
            _currentTrackedDevice = leftDevice;
        }
        else
        {
            _stylus.isActive = false;
            _currentTrackedDevice = default;
        }

        if (_mxInk_model != null)
        {
            _mxInk_model.SetActive(_stylus.isActive);
        }

        if (_left_touch_controller != null)
        {
            _left_touch_controller.SetActive(!_stylus.isActive || _stylus.isOnRightHand);
        }

        if (_right_touch_controller != null)
        {
            _right_touch_controller.SetActive(!_stylus.isActive || !_stylus.isOnRightHand);
        }
    }

    private static bool IsTrackedStylusDevice(XRInputDevice device)
    {
        if (!device.isValid || string.IsNullOrEmpty(device.name))
        {
            return false;
        }

        if (!device.name.ToLower().Contains("logitech"))
        {
            return false;
        }

        if (device.TryGetFeatureValue(XRCommonUsages.isTracked, out bool isTracked) && !isTracked)
        {
            return false;
        }

        return device.TryGetFeatureValue(XRCommonUsages.devicePosition, out _) &&
               device.TryGetFeatureValue(XRCommonUsages.deviceRotation, out _);
    }

    private void CacheRenderers()
    {
        _tipRenderer = _tip != null ? _tip.GetComponent<MeshRenderer>() : null;
        _clusterFrontRenderer = _cluster_front != null ? _cluster_front.GetComponent<MeshRenderer>() : null;
        _clusterMiddleRenderer = _cluster_middle != null ? _cluster_middle.GetComponent<MeshRenderer>() : null;
        _clusterBackRenderer = _cluster_back != null ? _cluster_back.GetComponent<MeshRenderer>() : null;

        _tipPropertyBlock = new MaterialPropertyBlock();
        _clusterFrontPropertyBlock = new MaterialPropertyBlock();
        _clusterMiddlePropertyBlock = new MaterialPropertyBlock();
        _clusterBackPropertyBlock = new MaterialPropertyBlock();
    }

    private static void SetRendererColor(Renderer targetRenderer, MaterialPropertyBlock propertyBlock, Color color)
    {
        if (targetRenderer == null || propertyBlock == null)
        {
            return;
        }

        targetRenderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetColor("_Color", color);
        propertyBlock.SetColor("_BaseColor", color);
        targetRenderer.SetPropertyBlock(propertyBlock);
    }
}
