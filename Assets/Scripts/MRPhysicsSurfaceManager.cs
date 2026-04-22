using System.Collections.Generic;
using Meta.XR.MRUtilityKit;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using XRCommonUsages = UnityEngine.XR.CommonUsages;
using XRInputDevice = UnityEngine.XR.InputDevice;
using XRInputDeviceCharacteristics = UnityEngine.XR.InputDeviceCharacteristics;
using XRInputDevices = UnityEngine.XR.InputDevices;

public class MRPhysicsSurfaceManager : MonoBehaviour
{
    private const int FallbackFloorRenderQueue = (int)RenderQueue.AlphaTest - 10;

    private static readonly XRInputDeviceCharacteristics LeftControllerCharacteristics =
        XRInputDeviceCharacteristics.Left | XRInputDeviceCharacteristics.Controller;

    private static readonly XRInputDeviceCharacteristics RightControllerCharacteristics =
        XRInputDeviceCharacteristics.Right | XRInputDeviceCharacteristics.Controller;

    [Header("Effect Mesh")]
    [SerializeField] private EffectMesh[] effectMeshes;
    [SerializeField] private XRDrawerSpawner[] drawerSpawners;
    [SerializeField] private string physicsSurfaceLayerName = "SandboxGround";
    [SerializeField] private int fallbackLayerIndex = 3;
    [SerializeField] private bool activateEffectMeshObjects = true;
    [SerializeField] private bool enableEffectMeshColliders = true;

    [Header("Fallback Floor")]
    [SerializeField] private bool createFallbackFloor = true;
    [SerializeField] private float fallbackFloorY;
    [SerializeField] private float fallbackFloorSize = 8f;
    [SerializeField] private float fallbackFloorThickness = 0.025f;
    [SerializeField] private Color fallbackFloorColor = new(0.25f, 0.75f, 1f, 0.22f);
    [SerializeField] private float joystickAdjustSpeed = 0.35f;
    [SerializeField] private float joystickDeadzone = 0.18f;
    [SerializeField] private float controllerGrabGripThreshold = 0.55f;
    [SerializeField] private float grabInteractableRefreshInterval = 0.5f;

    private readonly List<XRInputDevice> _xrDevices = new();
    private readonly Dictionary<Renderer, Material[]> _effectMeshRuntimeMaterials = new();
    private XRGrabInteractable[] _grabInteractables = new XRGrabInteractable[0];
    private GameObject _fallbackFloor;
    private Transform _fallbackFloorTransform;
    private Renderer _fallbackFloorRenderer;
    private Material _fallbackFloorMaterial;
    private PhysicsMaterial _surfacePhysicsMaterial;
    private PlaceableTransformGizmo _transformGizmo;
    private float _nextGrabInteractableRefreshTime;
    private int _physicsSurfaceLayer;
    private bool _effectMeshesEnabled = true;
    private bool _effectMeshCollidersEnabled = true;
    private bool _effectMeshVisible = true;
    private bool _fallbackFloorForcedVisible;
    private bool _fallbackFloorRendererVisible = true;

    public float FallbackFloorY => fallbackFloorY;

    public void ApplyMRSettingsSurfaceMode(
        bool effectMeshesEnabled,
        bool effectMeshCollidersEnabled,
        bool effectMeshVisible,
        bool forceFallbackFloor,
        bool fallbackFloorVisible)
    {
        _effectMeshesEnabled = effectMeshesEnabled;
        _effectMeshCollidersEnabled = effectMeshCollidersEnabled;
        _effectMeshVisible = effectMeshVisible;
        _fallbackFloorForcedVisible = forceFallbackFloor;
        _fallbackFloorRendererVisible = fallbackFloorVisible;

        ConfigureEffectMeshes();
        RefreshFallbackFloorVisibility();
    }

    private void Awake()
    {
        _physicsSurfaceLayer = ResolvePhysicsSurfaceLayer();
        ResolveEffectMeshes();
        ResolveDrawerSpawners();
        ConfigureEffectMeshes();
        EnsureFallbackFloor();
        ApplyFallbackFloorPose();
        SyncSpawnerFallbackHeights();
        RefreshFallbackFloorVisibility();
    }

    private void Update()
    {
        ResolveEffectMeshes();
        ResolveDrawerSpawners();
        ConfigureEffectMeshes();
        RefreshFallbackFloorVisibility();

        if (_fallbackFloor != null && _fallbackFloor.activeSelf)
        {
            AdjustFallbackFloorFromControllers();
            ApplyFallbackFloorPose();
        }

        SyncSpawnerFallbackHeights();
    }

    private void OnDestroy()
    {
        if (_fallbackFloorMaterial != null)
        {
            Destroy(_fallbackFloorMaterial);
        }

        if (_surfacePhysicsMaterial != null)
        {
            Destroy(_surfacePhysicsMaterial);
        }

        foreach (var pair in _effectMeshRuntimeMaterials)
        {
            var materials = pair.Value;
            if (materials == null)
            {
                continue;
            }

            for (var i = 0; i < materials.Length; i++)
            {
                if (materials[i] != null)
                {
                    Destroy(materials[i]);
                }
            }
        }

        _effectMeshRuntimeMaterials.Clear();
    }

    private void ResolveEffectMeshes()
    {
        if (effectMeshes != null && effectMeshes.Length > 0)
        {
            return;
        }

        effectMeshes = FindObjectsByType<EffectMesh>(FindObjectsInactive.Include, FindObjectsSortMode.None);
    }

    private void ResolveDrawerSpawners()
    {
        if (drawerSpawners != null && drawerSpawners.Length > 0)
        {
            return;
        }

        drawerSpawners = FindObjectsByType<XRDrawerSpawner>(FindObjectsInactive.Include, FindObjectsSortMode.None);
    }

    private void ConfigureEffectMeshes()
    {
        if (effectMeshes == null)
        {
            return;
        }

        foreach (var effectMesh in effectMeshes)
        {
            if (effectMesh == null)
            {
                continue;
            }

            if (!_effectMeshesEnabled)
            {
                if (effectMesh.gameObject.activeSelf)
                {
                    effectMesh.gameObject.SetActive(false);
                }

                continue;
            }

            if (activateEffectMeshObjects && !effectMesh.gameObject.activeSelf)
            {
                effectMesh.gameObject.SetActive(true);
            }

            effectMesh.Layer = _physicsSurfaceLayer;
            effectMesh.HideMesh = !_effectMeshVisible;

            if (enableEffectMeshColliders && _effectMeshCollidersEnabled && !effectMesh.Colliders)
            {
                effectMesh.Colliders = true;
            }

            if (effectMesh.isActiveAndEnabled && enableEffectMeshColliders)
            {
                effectMesh.ToggleEffectMeshColliders(_effectMeshCollidersEnabled);
            }

            foreach (var generated in effectMesh.EffectMeshObjects.Values)
            {
                if (generated?.effectMeshGO != null)
                {
                    generated.effectMeshGO.layer = _physicsSurfaceLayer;
                    var renderer = generated.effectMeshGO.GetComponent<Renderer>();
                    ConfigureEffectMeshRenderer(renderer);
                    if (renderer != null)
                    {
                        renderer.enabled = _effectMeshVisible;
                    }
                }

                if (generated?.collider != null)
                {
                    generated.collider.enabled = _effectMeshCollidersEnabled;
                    ConfigureSurfaceCollider(generated.collider);
                }
            }
        }
    }

    private void ConfigureEffectMeshRenderer(Renderer renderer)
    {
        if (renderer == null || _effectMeshRuntimeMaterials.ContainsKey(renderer))
        {
            return;
        }

        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        var sharedMaterials = renderer.sharedMaterials;
        if (sharedMaterials == null || sharedMaterials.Length == 0)
        {
            return;
        }

        var runtimeMaterials = new Material[sharedMaterials.Length];
        for (var i = 0; i < sharedMaterials.Length; i++)
        {
            var source = sharedMaterials[i];
            if (source == null)
            {
                runtimeMaterials[i] = null;
                continue;
            }

            var runtime = new Material(source)
            {
                name = source.name + "_RuntimeNoDepth",
                renderQueue = (int)RenderQueue.Transparent - 20
            };
            ConfigureSurfaceVisualMaterial(runtime);
            runtimeMaterials[i] = runtime;
        }

        renderer.sharedMaterials = runtimeMaterials;
        _effectMeshRuntimeMaterials[renderer] = runtimeMaterials;
    }

    private static void ConfigureSurfaceVisualMaterial(Material material)
    {
        if (material == null)
        {
            return;
        }

        var color = new Color(0.25f, 0.75f, 1f, 0.22f);
        if (material.HasProperty("_BaseColor"))
        {
            color = material.GetColor("_BaseColor");
        }
        else if (material.HasProperty("_Color"))
        {
            color = material.GetColor("_Color");
        }

        color.a = Mathf.Min(color.a, 0.35f);
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }
        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }

        if (material.HasProperty("_Surface"))
        {
            material.SetFloat("_Surface", 1f);
        }

        if (material.HasProperty("_Blend"))
        {
            material.SetFloat("_Blend", 0f);
        }

        if (material.HasProperty("_SrcBlend"))
        {
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        }

        if (material.HasProperty("_DstBlend"))
        {
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        }

        if (material.HasProperty("_ZWrite"))
        {
            material.SetFloat("_ZWrite", 0f);
        }

        material.SetOverrideTag("RenderType", "Transparent");
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.EnableKeyword("_ALPHABLEND_ON");
    }

    private void RefreshFallbackFloorVisibility()
    {
        if (!createFallbackFloor)
        {
            if (_fallbackFloor != null)
            {
                _fallbackFloor.SetActive(false);
            }

            return;
        }

        EnsureFallbackFloor();
        var shouldShowFallback = _fallbackFloorForcedVisible || !HasEffectMeshCollider();
        if (_fallbackFloor.activeSelf != shouldShowFallback)
        {
            _fallbackFloor.SetActive(shouldShowFallback);
        }

        if (_fallbackFloorRenderer != null)
        {
            _fallbackFloorRenderer.enabled = shouldShowFallback && _fallbackFloorRendererVisible;
        }
    }

    private bool HasEffectMeshCollider()
    {
        if (effectMeshes == null)
        {
            return false;
        }

        foreach (var effectMesh in effectMeshes)
        {
            if (effectMesh == null || !effectMesh.isActiveAndEnabled)
            {
                continue;
            }

            foreach (var generated in effectMesh.EffectMeshObjects.Values)
            {
                var collider = generated?.collider;
                if (collider != null && collider.enabled && collider.gameObject.activeInHierarchy)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void EnsureFallbackFloor()
    {
        if (_fallbackFloor != null || !createFallbackFloor)
        {
            return;
        }

        _fallbackFloor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        _fallbackFloor.name = "FallbackAdjustableFloor";
        _fallbackFloor.layer = _physicsSurfaceLayer;
        _fallbackFloorTransform = _fallbackFloor.transform;
        _fallbackFloorRenderer = _fallbackFloor.GetComponent<Renderer>();

        var collider = _fallbackFloor.GetComponent<Collider>();
        if (collider != null)
        {
            collider.isTrigger = false;
            ConfigureSurfaceCollider(collider);
        }

        ConfigureFallbackMaterial();
    }

    private void ConfigureFallbackMaterial()
    {
        if (_fallbackFloorRenderer == null)
        {
            return;
        }

        _fallbackFloorMaterial = CreateFallbackMaterial();
        if (_fallbackFloorMaterial != null)
        {
            _fallbackFloorRenderer.sharedMaterial = _fallbackFloorMaterial;
        }

        _fallbackFloorRenderer.shadowCastingMode = ShadowCastingMode.Off;
        _fallbackFloorRenderer.receiveShadows = false;
    }

    private Material CreateFallbackMaterial()
    {
        var shader = Shader.Find("Universal Render Pipeline/Unlit");
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
            name = "FallbackFloorRuntimeMaterial",
            color = fallbackFloorColor,
            renderQueue = FallbackFloorRenderQueue
        };

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", fallbackFloorColor);
        }

        if (material.HasProperty("_Surface"))
        {
            material.SetFloat("_Surface", 1f);
        }

        if (material.HasProperty("_Blend"))
        {
            material.SetFloat("_Blend", 0f);
        }

        if (material.HasProperty("_SrcBlend"))
        {
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        }

        if (material.HasProperty("_DstBlend"))
        {
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        }

        if (material.HasProperty("_ZWrite"))
        {
            material.SetFloat("_ZWrite", 1f);
        }

        if (material.HasProperty("_ZTest"))
        {
            material.SetFloat("_ZTest", (float)CompareFunction.LessEqual);
        }

        material.SetOverrideTag("RenderType", "Transparent");
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.EnableKeyword("_ALPHABLEND_ON");

        return material;
    }

    private void ConfigureSurfaceCollider(Collider collider)
    {
        if (collider == null || collider.isTrigger)
        {
            return;
        }

        collider.sharedMaterial = GetOrCreateSurfacePhysicsMaterial();
    }

    private PhysicsMaterial GetOrCreateSurfacePhysicsMaterial()
    {
        if (_surfacePhysicsMaterial != null)
        {
            return _surfacePhysicsMaterial;
        }

        _surfacePhysicsMaterial = new PhysicsMaterial("EffectMeshObjectControlledSurface")
        {
            dynamicFriction = 1.25f,
            staticFriction = 1.5f,
            bounciness = 0f,
            frictionCombine = PhysicsMaterialCombine.Minimum,
            bounceCombine = PhysicsMaterialCombine.Maximum
        };

        return _surfacePhysicsMaterial;
    }

    private void ApplyFallbackFloorPose()
    {
        if (_fallbackFloorTransform == null)
        {
            return;
        }

        var thickness = Mathf.Max(0.001f, fallbackFloorThickness);
        _fallbackFloorTransform.position = new Vector3(0f, fallbackFloorY - thickness * 0.5f, 0f);
        _fallbackFloorTransform.localScale = new Vector3(fallbackFloorSize, thickness, fallbackFloorSize);
    }

    private void SyncSpawnerFallbackHeights()
    {
        if (drawerSpawners == null)
        {
            return;
        }

        foreach (var spawner in drawerSpawners)
        {
            if (spawner != null)
            {
                spawner.FallbackGroundY = fallbackFloorY;
            }
        }
    }

    private void AdjustFallbackFloorFromControllers()
    {
        if (IsAnyObjectGrabOrDragActive())
        {
            return;
        }

        var leftVertical = ReadJoystickVertical(LeftControllerCharacteristics);
        var rightVertical = ReadJoystickVertical(RightControllerCharacteristics);
        var vertical = Mathf.Abs(leftVertical) >= Mathf.Abs(rightVertical) ? leftVertical : rightVertical;

        if (Mathf.Abs(vertical) < joystickDeadzone)
        {
            return;
        }

        fallbackFloorY += vertical * joystickAdjustSpeed * Time.deltaTime;
    }

    private bool IsAnyObjectGrabOrDragActive()
    {
        if (NonStylusControllerRayVisuals.AnyControllerGrabActive
            || IsEndpointDragActive(PlaceableMultiGrabCoordinator.MXInkSourceId)
            || IsEndpointDragActive(PlaceableMultiGrabCoordinator.DirectStylusSourceId)
            || IsTransformGizmoDragging()
            || IsAnySelectedDraggableGrabInteractable())
        {
            return true;
        }

        // Floor height runs in Update, while some grab systems publish selection later in the frame.
        // Treating a held controller grip as grab intent prevents a one-frame floor-height jump.
        return IsControllerGripPressed(LeftControllerCharacteristics)
               || IsControllerGripPressed(RightControllerCharacteristics);
    }

    private static bool IsEndpointDragActive(int sourceId)
    {
        return PhysicsDrawingEndpointHandle.IsSourceRayDragging(sourceId);
    }

    private bool IsTransformGizmoDragging()
    {
        if (_transformGizmo == null)
        {
            _transformGizmo = FindFirstObjectByType<PlaceableTransformGizmo>(FindObjectsInactive.Include);
        }

        return _transformGizmo != null && _transformGizmo.IsDragging;
    }

    private bool IsAnySelectedDraggableGrabInteractable()
    {
        RefreshGrabInteractablesIfNeeded();

        for (var i = 0; i < _grabInteractables.Length; i++)
        {
            var grabInteractable = _grabInteractables[i];
            if (grabInteractable != null
                && grabInteractable.isActiveAndEnabled
                && grabInteractable.isSelected
                && IsDraggableGrabInteractable(grabInteractable))
            {
                return true;
            }
        }

        return false;
    }

    private void RefreshGrabInteractablesIfNeeded()
    {
        if (_grabInteractables.Length > 0
            && Time.unscaledTime < _nextGrabInteractableRefreshTime
            && !HasMissingGrabInteractable())
        {
            return;
        }

        _grabInteractables = FindObjectsByType<XRGrabInteractable>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        _nextGrabInteractableRefreshTime = Time.unscaledTime
                                           + Mathf.Max(0.05f, grabInteractableRefreshInterval);
    }

    private bool HasMissingGrabInteractable()
    {
        for (var i = 0; i < _grabInteractables.Length; i++)
        {
            if (_grabInteractables[i] == null)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsDraggableGrabInteractable(XRGrabInteractable grabInteractable)
    {
        return grabInteractable.GetComponentInParent<PlaceableAsset>() != null
               || grabInteractable.GetComponentInParent<PhysicsDrawingSelectable>() != null
               || grabInteractable.GetComponentInParent<SelectableAsset>() != null;
    }

    private bool IsControllerGripPressed(XRInputDeviceCharacteristics characteristics)
    {
        _xrDevices.Clear();
        XRInputDevices.GetDevicesWithCharacteristics(characteristics, _xrDevices);

        for (var i = 0; i < _xrDevices.Count; i++)
        {
            var device = _xrDevices[i];
            if (!device.isValid || IsLogitechStylus(device))
            {
                continue;
            }

            if (device.TryGetFeatureValue(XRCommonUsages.gripButton, out var gripPressed)
                && gripPressed)
            {
                return true;
            }

            if (device.TryGetFeatureValue(XRCommonUsages.grip, out var gripValue)
                && gripValue >= controllerGrabGripThreshold)
            {
                return true;
            }
        }

        return false;
    }

    private float ReadJoystickVertical(XRInputDeviceCharacteristics characteristics)
    {
        _xrDevices.Clear();
        XRInputDevices.GetDevicesWithCharacteristics(characteristics, _xrDevices);

        for (var i = 0; i < _xrDevices.Count; i++)
        {
            var device = _xrDevices[i];
            if (!device.isValid || IsLogitechStylus(device))
            {
                continue;
            }

            if (device.TryGetFeatureValue(XRCommonUsages.primary2DAxis, out var axis))
            {
                return axis.y;
            }
        }

        return 0f;
    }

    private int ResolvePhysicsSurfaceLayer()
    {
        var namedLayer = LayerMask.NameToLayer(physicsSurfaceLayerName);
        return namedLayer >= 0 ? namedLayer : fallbackLayerIndex;
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
}
