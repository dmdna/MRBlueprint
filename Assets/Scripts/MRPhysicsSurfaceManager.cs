using System.Collections.Generic;
using Meta.XR.MRUtilityKit;
using UnityEngine;
using UnityEngine.Rendering;
using XRCommonUsages = UnityEngine.XR.CommonUsages;
using XRInputDevice = UnityEngine.XR.InputDevice;
using XRInputDeviceCharacteristics = UnityEngine.XR.InputDeviceCharacteristics;
using XRInputDevices = UnityEngine.XR.InputDevices;

public class MRPhysicsSurfaceManager : MonoBehaviour
{
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

    private readonly List<XRInputDevice> _xrDevices = new();
    private GameObject _fallbackFloor;
    private Transform _fallbackFloorTransform;
    private Renderer _fallbackFloorRenderer;
    private Material _fallbackFloorMaterial;
    private int _physicsSurfaceLayer;

    public float FallbackFloorY => fallbackFloorY;

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

            if (activateEffectMeshObjects && !effectMesh.gameObject.activeSelf)
            {
                effectMesh.gameObject.SetActive(true);
            }

            effectMesh.Layer = _physicsSurfaceLayer;

            if (enableEffectMeshColliders && !effectMesh.Colliders)
            {
                effectMesh.Colliders = true;
            }

            if (effectMesh.isActiveAndEnabled && enableEffectMeshColliders)
            {
                effectMesh.ToggleEffectMeshColliders(true);
            }

            foreach (var generated in effectMesh.EffectMeshObjects.Values)
            {
                if (generated?.effectMeshGO != null)
                {
                    generated.effectMeshGO.layer = _physicsSurfaceLayer;
                }
            }
        }
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
        var shouldShowFallback = !HasEffectMeshCollider();
        if (_fallbackFloor.activeSelf != shouldShowFallback)
        {
            _fallbackFloor.SetActive(shouldShowFallback);
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
            renderQueue = 3000
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
            material.SetFloat("_ZWrite", 0f);
        }

        material.SetOverrideTag("RenderType", "Transparent");
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.EnableKeyword("_ALPHABLEND_ON");

        return material;
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
        if (PlaceableMultiGrabCoordinator.AnyGrabActive)
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
