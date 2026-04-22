using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.XR;
using XRInputDevice = UnityEngine.XR.InputDevice;
using InputSystemDevice = UnityEngine.InputSystem.InputDevice;
using XRCommonUsages = UnityEngine.XR.CommonUsages;

public class VrStylusHandler : StylusHandler
{
    private const int DeviceVisualRenderQueue = (int)RenderQueue.Overlay - 4;
    private const int DeviceVisualSortingOrder = 620;
    private const int DeviceVisualRefreshIntervalFrames = 30;
    private const string DeviceVisualMaterialSuffix = " (MRBlueprintDeviceOverlay)";

    [SerializeField] private GameObject _mxInk_model;
    [SerializeField] private GameObject _tip;
    [SerializeField] private GameObject _cluster_front;
    [SerializeField] private GameObject _cluster_middle;
    [SerializeField] private GameObject _cluster_back;

    [SerializeField] private GameObject _left_touch_controller;
    [SerializeField] private GameObject _right_touch_controller;
    [SerializeField] private Transform _trackingSpace;

    public Color active_color = Color.green;
    public Color double_tap_active_color = Color.cyan;
    public Color default_color = Color.white;

    private float _hapticClickDuration = 0.011f;
    private float _hapticClickAmplitude = 1.0f;

    public Transform TipTransform => _tip != null ? _tip.transform : transform;
    public bool IsTrackingStylus => _stylus.isActive && _currentTrackedDevice.isValid;
    public bool IsMXInkMeshVisible => _mxInk_model != null ? _mxInk_model.activeInHierarchy : IsTrackingStylus;
    public bool IsMXInkDetectedAndUsable => IsTrackingStylus && IsMXInkMeshVisible && CanDraw();

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
    private readonly List<DeviceVisualPriorityBinding> _deviceVisualPriorityBindings = new();
    private int _lastDeviceVisualPriorityRefreshFrame = -1000;

    private void Awake()
    {
        _tipActionRef.action.Enable();
        _grabActionRef.action.Enable();
        _optionActionRef.action.Enable();
        _middleActionRef.action.Enable();

        _stylus.isActive = false;
        _trackingSpace = ResolveTrackingSpace();
        InputSystem.onDeviceChange += OnDeviceChange;
        InputDevices.deviceConnected += DeviceConnected;

        CacheRenderers();
        RefreshDeviceVisualPriority(true);
    }

    private void OnEnable()
    {
        RefreshStylusState();
        RefreshDeviceVisualPriority(true);
    }

    private void OnDisable()
    {
        RestoreDeviceVisualPriority();
    }

    private void OnDestroy()
    {
        InputSystem.onDeviceChange -= OnDeviceChange;
        InputDevices.deviceConnected -= DeviceConnected;
        RestoreDeviceVisualPriority();
        DestroyDeviceVisualPriorityMaterials();
    }

    private void DeviceConnected(XRInputDevice device)
    {
        Debug.Log($"Device connected: {device.name}");
        RefreshStylusState();
        RefreshDeviceVisualPriority(true);
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
        RefreshDeviceVisualPriority(true);
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

        RefreshDeviceVisualPriority(false);
    }

    void GetControllerTransform(XRInputDevice device)
    {
        if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.devicePosition, out Vector3 position))
        {
            if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.deviceRotation, out Quaternion rotation))
            {
                ApplyTrackingSpacePose(position, rotation);
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

    private void ApplyTrackingSpacePose(Vector3 localPosition, Quaternion localRotation)
    {
        _trackingSpace = ResolveTrackingSpace();

        if (_trackingSpace != null)
        {
            transform.SetPositionAndRotation(
                _trackingSpace.TransformPoint(localPosition),
                _trackingSpace.rotation * localRotation);
            return;
        }

        if (transform.parent != null)
        {
            transform.localPosition = localPosition;
            transform.localRotation = localRotation;
            return;
        }

        transform.SetPositionAndRotation(localPosition, localRotation);
    }

    private Transform ResolveTrackingSpace()
    {
        if (_trackingSpace != null)
        {
            return _trackingSpace;
        }

        for (Transform current = transform.parent; current != null; current = current.parent)
        {
            var trackingSpace = current.Find("TrackingSpace");
            if (trackingSpace != null)
            {
                return trackingSpace;
            }
        }

        return null;
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

    private void RefreshDeviceVisualPriority(bool force)
    {
        if (force
            || Time.frameCount - _lastDeviceVisualPriorityRefreshFrame
            >= DeviceVisualRefreshIntervalFrames)
        {
            _lastDeviceVisualPriorityRefreshFrame = Time.frameCount;
            EnsureDeviceVisualPriority(_mxInk_model);
            EnsureDeviceVisualPriority(_left_touch_controller);
            EnsureDeviceVisualPriority(_right_touch_controller);
            PruneInvalidDeviceVisualPriorityBindings();
        }

        ApplyDeviceVisualPriority();
    }

    private void EnsureDeviceVisualPriority(GameObject root)
    {
        if (root == null)
        {
            return;
        }

        var renderers = root.GetComponentsInChildren<Renderer>(true);
        for (var i = 0; i < renderers.Length; i++)
        {
            var renderer = renderers[i];
            if (renderer is MeshRenderer || renderer is SkinnedMeshRenderer)
            {
                EnsureDeviceVisualPriority(renderer);
            }
        }
    }

    private void EnsureDeviceVisualPriority(Renderer renderer)
    {
        if (renderer == null || FindDeviceVisualPriorityBinding(renderer) != null)
        {
            return;
        }

        _deviceVisualPriorityBindings.Add(new DeviceVisualPriorityBinding
        {
            Renderer = renderer,
            OriginalMaterials = renderer.sharedMaterials,
            OriginalSortingOrder = renderer.sortingOrder,
            RuntimeMaterials = CreateDeviceVisualPriorityMaterials(renderer.sharedMaterials)
        });
    }

    private DeviceVisualPriorityBinding FindDeviceVisualPriorityBinding(Renderer renderer)
    {
        for (var i = 0; i < _deviceVisualPriorityBindings.Count; i++)
        {
            var binding = _deviceVisualPriorityBindings[i];
            if (binding != null && binding.Renderer == renderer)
            {
                return binding;
            }
        }

        return null;
    }

    private static Material[] CreateDeviceVisualPriorityMaterials(Material[] sourceMaterials)
    {
        if (sourceMaterials == null || sourceMaterials.Length == 0)
        {
            return System.Array.Empty<Material>();
        }

        var materials = new Material[sourceMaterials.Length];
        for (var i = 0; i < sourceMaterials.Length; i++)
        {
            var source = sourceMaterials[i];
            if (source == null)
            {
                continue;
            }

            materials[i] = new Material(source)
            {
                name = source.name + DeviceVisualMaterialSuffix,
                hideFlags = HideFlags.DontSave,
                renderQueue = DeviceVisualRenderQueue
            };
        }

        return materials;
    }

    private void ApplyDeviceVisualPriority()
    {
        for (var i = _deviceVisualPriorityBindings.Count - 1; i >= 0; i--)
        {
            var binding = _deviceVisualPriorityBindings[i];
            if (!IsValidDeviceVisualPriorityBinding(binding))
            {
                DestroyDeviceVisualPriorityMaterials(binding);
                _deviceVisualPriorityBindings.RemoveAt(i);
                continue;
            }

            binding.Renderer.sortingOrder = DeviceVisualSortingOrder;

            if (MaterialsNeedRefresh(binding))
            {
                DestroyDeviceVisualPriorityMaterials(binding.RuntimeMaterials);
                binding.OriginalMaterials = binding.Renderer.sharedMaterials;
                binding.RuntimeMaterials = CreateDeviceVisualPriorityMaterials(binding.OriginalMaterials);
            }

            if (!MaterialsMatch(binding.Renderer.sharedMaterials, binding.RuntimeMaterials))
            {
                binding.Renderer.sharedMaterials = binding.RuntimeMaterials;
            }
        }
    }

    private static bool MaterialsNeedRefresh(DeviceVisualPriorityBinding binding)
    {
        if (binding == null || binding.Renderer == null)
        {
            return false;
        }

        var current = binding.Renderer.sharedMaterials;
        return !MaterialsMatch(current, binding.RuntimeMaterials)
               && !MaterialsMatch(current, binding.OriginalMaterials);
    }

    private static bool MaterialsMatch(Material[] a, Material[] b)
    {
        if (a == null || b == null)
        {
            return a == b;
        }

        if (a.Length != b.Length)
        {
            return false;
        }

        for (var i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i])
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsValidDeviceVisualPriorityBinding(DeviceVisualPriorityBinding binding)
    {
        return binding != null && binding.Renderer != null;
    }

    private void PruneInvalidDeviceVisualPriorityBindings()
    {
        for (var i = _deviceVisualPriorityBindings.Count - 1; i >= 0; i--)
        {
            var binding = _deviceVisualPriorityBindings[i];
            if (IsValidDeviceVisualPriorityBinding(binding))
            {
                continue;
            }

            DestroyDeviceVisualPriorityMaterials(binding);
            _deviceVisualPriorityBindings.RemoveAt(i);
        }
    }

    private void RestoreDeviceVisualPriority()
    {
        for (var i = 0; i < _deviceVisualPriorityBindings.Count; i++)
        {
            var binding = _deviceVisualPriorityBindings[i];
            if (binding?.Renderer == null)
            {
                continue;
            }

            binding.Renderer.sharedMaterials = binding.OriginalMaterials;
            binding.Renderer.sortingOrder = binding.OriginalSortingOrder;
        }
    }

    private void DestroyDeviceVisualPriorityMaterials()
    {
        for (var i = 0; i < _deviceVisualPriorityBindings.Count; i++)
        {
            DestroyDeviceVisualPriorityMaterials(_deviceVisualPriorityBindings[i]);
        }

        _deviceVisualPriorityBindings.Clear();
    }

    private static void DestroyDeviceVisualPriorityMaterials(DeviceVisualPriorityBinding binding)
    {
        if (binding == null)
        {
            return;
        }

        DestroyDeviceVisualPriorityMaterials(binding.RuntimeMaterials);
        binding.RuntimeMaterials = null;
    }

    private static void DestroyDeviceVisualPriorityMaterials(Material[] materials)
    {
        if (materials == null)
        {
            return;
        }

        for (var i = 0; i < materials.Length; i++)
        {
            if (materials[i] != null)
            {
                Destroy(materials[i]);
            }
        }
    }

    private sealed class DeviceVisualPriorityBinding
    {
        public Renderer Renderer;
        public Material[] OriginalMaterials;
        public Material[] RuntimeMaterials;
        public int OriginalSortingOrder;
    }
}
