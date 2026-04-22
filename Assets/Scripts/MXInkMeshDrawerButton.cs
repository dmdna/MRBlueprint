using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
[RequireComponent(typeof(XRDrawerItem))]
public sealed class MXInkMeshDrawerButton : MonoBehaviour
{
    private const string ButtonName = "DrawerItem_MeshDrawingToggle";
    private const string PencilPrefabPath = "Assets/Prefabs/pencil.fbx";

    [SerializeField] private XRContentDrawerController drawerController;
    [SerializeField] private GameObject pencilPrefab;
    [SerializeField] private Vector3 localPosition = new(0.56f, 0.47f, -0.055f);
    [SerializeField] private Vector2 buttonSize = new(0.11f, 0.11f);
    [SerializeField] private float buttonDepth = 0.018f;
    [SerializeField] private float pencilMargin = 0.006f;
    [SerializeField] private float pencilSurfaceOffset = 0.0025f;
    [SerializeField] private bool pencilTipAtPositiveLongAxis;
    [SerializeField] private float xMargin = 0.014f;
    [SerializeField] private float xBarThickness = 0.011f;
    [SerializeField] private float xBarDepth = 0.004f;
    [SerializeField] private float xSurfaceOffset = 0.003f;
    [SerializeField] private Color inactiveColor = new(0.1f, 0.34f, 0.5f, 0.96f);
    [SerializeField] private Color activeColor = new(0.56f, 0.14f, 0.16f, 0.96f);
    [SerializeField] private Color iconColor = Color.white;

    private readonly List<TransformState> _hiddenSiblings = new();
    private MeshRenderer _plateRenderer;
    private Material _plateMaterial;
    private Material _iconMaterial;
    private Transform _pencilIconRoot;
    private GameObject _pencilModelInstance;
    private GameObject _xBarA;
    private GameObject _xBarB;
    private int _lastLayoutChildCount = -1;
    private bool _contentHidden;

    public static MXInkMeshDrawerButton EnsureForDrawer(XRContentDrawerController drawer)
    {
        if (drawer == null || drawer.DrawerRoot == null)
        {
            return null;
        }

        var existing = drawer.DrawerRoot.GetComponentInChildren<MXInkMeshDrawerButton>(true);
        if (existing != null)
        {
            existing.Configure(drawer);
            return existing;
        }

        var parent = ResolveDrawerItemParent(drawer);
        var root = new GameObject(ButtonName);
        root.transform.SetParent(parent != null ? parent : drawer.DrawerRoot, false);
        root.transform.localPosition = new Vector3(0.56f, 0.47f, -0.055f);
        root.transform.localRotation = Quaternion.identity;
        root.transform.localScale = Vector3.one;

        var drawerItem = root.AddComponent<XRDrawerItem>();
        drawerItem.SetIdleAnimation(0f, 1f, 0f);

        var button = root.AddComponent<MXInkMeshDrawerButton>();
        button.Configure(drawer);
        button.EnsureVisuals();
        button.RefreshVisualState();
        button.EnsureDrawerGridSlot(forceLayout: true);
        return button;
    }

    private void Awake()
    {
        EnsureVisuals();
    }

    private void OnEnable()
    {
        MeshDrawingModeState.ActiveChanged -= OnMeshDrawingModeChanged;
        MeshDrawingModeState.ActiveChanged += OnMeshDrawingModeChanged;
        RefreshVisualState();
        ApplyDrawerContentVisibility();
    }

    private void OnDisable()
    {
        MeshDrawingModeState.ActiveChanged -= OnMeshDrawingModeChanged;
        if (_contentHidden)
        {
            RestoreDrawerContent();
        }
    }

    private void OnDestroy()
    {
        MeshDrawingModeState.ActiveChanged -= OnMeshDrawingModeChanged;
        if (_plateMaterial != null)
        {
            Destroy(_plateMaterial);
        }

        if (_iconMaterial != null)
        {
            Destroy(_iconMaterial);
        }
    }

    private void LateUpdate()
    {
        if (drawerController == null)
        {
            drawerController = FindFirstObjectByType<XRContentDrawerController>(FindObjectsInactive.Include);
        }

        var inGrid = EnsureDrawerGridSlot(forceLayout: false);
        if (!inGrid)
        {
            transform.localPosition = localPosition;
        }

        transform.localRotation = Quaternion.identity;
        ApplyDrawerContentVisibility();
    }

    public void Configure(XRContentDrawerController drawer)
    {
        if (drawer != null)
        {
            drawerController = drawer;
            if (drawer.MeshDrawingButtonPencilPrefab != null)
            {
                pencilPrefab = drawer.MeshDrawingButtonPencilPrefab;
            }
        }

        EnsureDrawerGridSlot(forceLayout: true);
        EnsurePencilModel();
        RefreshVisualState();
    }

    public void ToggleMeshDrawingMode()
    {
        if (MeshDrawingModeState.IsActive)
        {
            MeshDrawingModeState.SetActive(false);
            return;
        }

        SandboxEditorModeState.SetSessionMode(SandboxEditorSessionMode.Edit);
        MeshDrawingModeState.SetActive(true);
    }

    private void OnMeshDrawingModeChanged(bool _)
    {
        RefreshVisualState();
        ApplyDrawerContentVisibility();
    }

    private void ApplyDrawerContentVisibility()
    {
        if (drawerController == null || drawerController.DrawerRoot == null)
        {
            return;
        }

        if (MeshDrawingModeState.IsActive)
        {
            HideDrawerContent();
        }
        else
        {
            RestoreDrawerContent();
        }
    }

    private void HideDrawerContent()
    {
        if (_contentHidden)
        {
            return;
        }

        _hiddenSiblings.Clear();
        var contentParent = ResolveContentVisibilityParent();
        HideChildrenUnder(contentParent);

        if (drawerController != null && drawerController.DrawerRoot != contentParent)
        {
            HideChildrenUnder(drawerController.DrawerRoot);
        }


        _contentHidden = true;
    }

    private void HideChildrenUnder(Transform parent)
    {
        if (parent == null)
        {
            return;
        }

        for (var i = 0; i < parent.childCount; i++)
        {
            TryHideDrawerTransform(parent.GetChild(i));
        }
    }

    private void TryHideDrawerTransform(Transform target)
    {
        if (target == null
            || target == transform
            || transform.IsChildOf(target)
            || target.IsChildOf(transform))
        {
            return;
        }

        for (var i = 0; i < _hiddenSiblings.Count; i++)
        {
            if (_hiddenSiblings[i].Transform == target)
            {
                return;
            }
        }

        _hiddenSiblings.Add(new TransformState(target, target.gameObject.activeSelf));
        target.gameObject.SetActive(false);
    }

    private void RestoreDrawerContent()
    {
        if (!_contentHidden)
        {
            return;
        }

        var shouldShowContent = drawerController == null || drawerController.IsOpen;
        for (var i = 0; i < _hiddenSiblings.Count; i++)
        {
            var state = _hiddenSiblings[i];
            if (state.Transform == null)
            {
                continue;
            }

            state.Transform.gameObject.SetActive(shouldShowContent && state.WasActiveSelf);
        }

        _hiddenSiblings.Clear();
        _contentHidden = false;
    }

    private void RefreshVisualState()
    {
        EnsureVisuals();
        var active = MeshDrawingModeState.IsActive;
        var color = active ? activeColor : inactiveColor;
        if (_plateMaterial != null)
        {
            SetMaterialColor(_plateMaterial, color);
        }

        SetPencilVisible(!active);
        ApplyXBarLayout();
        SetXVisible(active);
    }

    private void EnsureVisuals()
    {
        if (_plateRenderer != null)
        {
            return;
        }

        var drawerItem = GetComponent<XRDrawerItem>();
        drawerItem.SetSpawnPrefab(null);
        drawerItem.SetIdleAnimation(0f, 1f, 0f);

        _plateMaterial = CreateMaterial("MXInkMeshDrawerButtonPlate", inactiveColor);
        _iconMaterial = CreateOpaqueIconMaterial("MXInkMeshDrawerButtonIcon", iconColor);

        var plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
        plate.name = "ButtonPlate";
        plate.transform.SetParent(transform, false);
        plate.transform.localPosition = Vector3.zero;
        plate.transform.localRotation = Quaternion.identity;
        plate.transform.localScale = new Vector3(buttonSize.x, buttonSize.y, buttonDepth);
        _plateRenderer = plate.GetComponent<MeshRenderer>();
        _plateRenderer.sharedMaterial = _plateMaterial;
        _plateRenderer.shadowCastingMode = ShadowCastingMode.Off;
        _plateRenderer.receiveShadows = false;

        var collider = plate.GetComponent<BoxCollider>();
        collider.isTrigger = true;
        collider.size = Vector3.one * 1.08f;

        var pick = plate.GetComponent<DrawerTilePickTarget>();
        if (pick == null)
        {
            pick = plate.AddComponent<DrawerTilePickTarget>();
        }

        pick.SetCaptionForRuntime(string.Empty);

        EnsurePencilModel();
        _xBarA = CreateXBar("CloseA");
        _xBarB = CreateXBar("CloseB");
        ApplyXBarLayout();
    }

    private void EnsurePencilModel()
    {
        if (_pencilIconRoot != null)
        {
            ApplyPencilLayout();
            return;
        }

        var prefab = ResolvePencilPrefab();
        var root = new GameObject("PencilIcon");
        root.transform.SetParent(transform, false);
        _pencilIconRoot = root.transform;

        _pencilModelInstance = prefab != null
            ? Instantiate(prefab, _pencilIconRoot)
            : CreateProceduralPencilIcon(_pencilIconRoot);
        if (_pencilModelInstance == null)
        {
            return;
        }

        _pencilModelInstance.name = "PencilModel";
        _pencilModelInstance.transform.localPosition = Vector3.zero;
        _pencilModelInstance.transform.localRotation = Quaternion.identity;
        _pencilModelInstance.transform.localScale = Vector3.one;

        RemoveModelColliders(_pencilModelInstance);
        ConfigurePencilRenderers(_pencilModelInstance);
        if (!HasRenderablePencil(_pencilModelInstance))
        {
            DestroyUnityObject(_pencilModelInstance);
            _pencilModelInstance = CreateProceduralPencilIcon(_pencilIconRoot);
            ConfigurePencilRenderers(_pencilModelInstance);
        }

        ApplyPencilLayout();
    }

    private GameObject ResolvePencilPrefab()
    {
        if (pencilPrefab != null)
        {
            return pencilPrefab;
        }

        if (drawerController != null && drawerController.MeshDrawingButtonPencilPrefab != null)
        {
            pencilPrefab = drawerController.MeshDrawingButtonPencilPrefab;
            return pencilPrefab;
        }

#if UNITY_EDITOR
        pencilPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PencilPrefabPath);
#endif
        return pencilPrefab;
    }

    private GameObject CreateProceduralPencilIcon(Transform parent)
    {
        var root = new GameObject("ProceduralPencilIcon");
        root.transform.SetParent(parent, false);

        CreatePencilPart(root.transform, "Body", new Vector3(0.08f, 0f, 0f), new Vector3(0.68f, 0.12f, 0.05f));
        CreatePencilPart(root.transform, "Tip", new Vector3(-0.34f, 0f, 0f), new Vector3(0.16f, 0.12f, 0.05f));
        CreatePencilPart(root.transform, "Eraser", new Vector3(0.48f, 0f, 0f), new Vector3(0.12f, 0.12f, 0.05f));

        return root;
    }

    private void CreatePencilPart(Transform parent, string objectName, Vector3 localPosition, Vector3 localScale)
    {
        var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
        part.name = objectName;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPosition;
        part.transform.localRotation = Quaternion.identity;
        part.transform.localScale = localScale;

        var renderer = part.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = _iconMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        var collider = part.GetComponent<Collider>();
        if (collider != null)
        {
            collider.enabled = false;
            DestroyUnityObject(collider);
        }
    }

    private static bool HasRenderablePencil(GameObject modelRoot)
    {
        if (modelRoot == null)
        {
            return false;
        }

        var renderers = modelRoot.GetComponentsInChildren<Renderer>(true);
        for (var i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null && renderers[i].enabled)
            {
                return true;
            }
        }

        return false;
    }

    private void ApplyPencilLayout()
    {
        if (_pencilIconRoot == null || _pencilModelInstance == null)
        {
            return;
        }

        _pencilIconRoot.localPosition = Vector3.zero;
        _pencilIconRoot.localRotation = Quaternion.identity;
        _pencilIconRoot.localScale = Vector3.one;

        if (!TryGetLocalRenderBounds(_pencilIconRoot, out var localBounds))
        {
            return;
        }

        _pencilModelInstance.transform.localPosition -= localBounds.center;
        if (!TryGetLocalRenderBounds(_pencilIconRoot, out localBounds))
        {
            return;
        }

        var longAxis = ResolveLongAxis(localBounds.size);
        var targetAxis = (pencilTipAtPositiveLongAxis
            ? new Vector3(-1f, -1f, 0f)
            : new Vector3(1f, 1f, 0f)).normalized;
        var rotation = Quaternion.FromToRotation(longAxis, targetAxis);
        var rotatedBounds = CalculateRotatedBounds(localBounds, rotation);
        var usableWidth = Mathf.Max(0.001f, buttonSize.x - pencilMargin * 2f);
        var usableHeight = Mathf.Max(0.001f, buttonSize.y - pencilMargin * 2f);
        var fitScale = Mathf.Min(
            usableWidth / Mathf.Max(0.001f, rotatedBounds.size.x),
            usableHeight / Mathf.Max(0.001f, rotatedBounds.size.y));
        var crossSectionScale = Mathf.Min(1f, fitScale);
        var lengthScale = ResolvePencilLengthScale(
            localBounds,
            rotation,
            longAxis,
            crossSectionScale,
            usableWidth,
            usableHeight);
        var scale = BuildAxisScale(longAxis, crossSectionScale, lengthScale);
        var scaledRotatedBounds = CalculateScaledRotatedBounds(localBounds, rotation, scale);

        var frontFaceZ = -buttonDepth * 0.5f;
        var localZ = frontFaceZ - pencilSurfaceOffset - scaledRotatedBounds.max.z;

        _pencilIconRoot.localPosition = new Vector3(0f, 0f, localZ);
        _pencilIconRoot.localRotation = rotation;
        _pencilIconRoot.localScale = scale;
    }

    private static float ResolvePencilLengthScale(
        Bounds bounds,
        Quaternion rotation,
        Vector3 longAxis,
        float crossSectionScale,
        float usableWidth,
        float usableHeight)
    {
        var low = Mathf.Max(0.001f, crossSectionScale);
        var high = Mathf.Max(1f, low);

        for (var i = 0; i < 16 && FitsButtonFace(bounds, rotation, BuildAxisScale(longAxis, crossSectionScale, high), usableWidth, usableHeight); i++)
        {
            high *= 2f;
        }

        for (var i = 0; i < 24; i++)
        {
            var mid = (low + high) * 0.5f;
            if (FitsButtonFace(bounds, rotation, BuildAxisScale(longAxis, crossSectionScale, mid), usableWidth, usableHeight))
            {
                low = mid;
            }
            else
            {
                high = mid;
            }
        }

        return low;
    }

    private static Vector3 BuildAxisScale(Vector3 longAxis, float crossSectionScale, float lengthScale)
    {
        if (Mathf.Abs(longAxis.x) > 0.5f)
        {
            return new Vector3(lengthScale, crossSectionScale, crossSectionScale);
        }

        if (Mathf.Abs(longAxis.y) > 0.5f)
        {
            return new Vector3(crossSectionScale, lengthScale, crossSectionScale);
        }

        return new Vector3(crossSectionScale, crossSectionScale, lengthScale);
    }

    private static bool FitsButtonFace(
        Bounds bounds,
        Quaternion rotation,
        Vector3 scale,
        float usableWidth,
        float usableHeight)
    {
        var scaledBounds = CalculateScaledRotatedBounds(bounds, rotation, scale);
        return scaledBounds.size.x <= usableWidth + 0.0001f
               && scaledBounds.size.y <= usableHeight + 0.0001f;
    }

    private static Vector3 ResolveLongAxis(Vector3 size)
    {
        if (size.x >= size.y && size.x >= size.z)
        {
            return Vector3.right;
        }

        return size.y >= size.z ? Vector3.up : Vector3.forward;
    }

    private static Bounds CalculateRotatedBounds(Bounds bounds, Quaternion rotation)
    {
        return CalculateScaledRotatedBounds(bounds, rotation, Vector3.one);
    }

    private static Bounds CalculateScaledRotatedBounds(Bounds bounds, Quaternion rotation, Vector3 scale)
    {
        var min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        var max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        var center = bounds.center;
        var extents = bounds.extents;

        for (var x = -1; x <= 1; x += 2)
        {
            for (var y = -1; y <= 1; y += 2)
            {
                for (var z = -1; z <= 1; z += 2)
                {
                    var corner = center + Vector3.Scale(extents, new Vector3(x, y, z));
                    var rotated = rotation * Vector3.Scale(corner, scale);
                    min = Vector3.Min(min, rotated);
                    max = Vector3.Max(max, rotated);
                }
            }
        }

        var rotatedBounds = new Bounds();
        rotatedBounds.SetMinMax(min, max);
        return rotatedBounds;
    }

    private static bool TryGetLocalRenderBounds(Transform root, out Bounds bounds)
    {
        bounds = default;
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        var hasBounds = false;

        for (var i = 0; i < renderers.Length; i++)
        {
            var renderer = renderers[i];
            if (renderer == null)
            {
                continue;
            }

            var rendererBounds = renderer.bounds;
            var center = rendererBounds.center;
            var extents = rendererBounds.extents;
            for (var x = -1; x <= 1; x += 2)
            {
                for (var y = -1; y <= 1; y += 2)
                {
                    for (var z = -1; z <= 1; z += 2)
                    {
                        var worldCorner = center + Vector3.Scale(extents, new Vector3(x, y, z));
                        var localCorner = root.InverseTransformPoint(worldCorner);
                        if (hasBounds)
                        {
                            bounds.Encapsulate(localCorner);
                        }
                        else
                        {
                            bounds = new Bounds(localCorner, Vector3.zero);
                            hasBounds = true;
                        }
                    }
                }
            }
        }

        return hasBounds;
    }

    private bool EnsureDrawerGridSlot(bool forceLayout)
    {
        if (drawerController == null)
        {
            return false;
        }

        var parent = ResolveDrawerItemParent(drawerController);
        if (parent == null)
        {
            return false;
        }

        var grid = parent.GetComponent<DrawerGridLayout3D>();
        var changed = false;
        if (transform.parent != parent)
        {
            transform.SetParent(parent, false);
            changed = true;
        }

        if (transform.GetSiblingIndex() != parent.childCount - 1)
        {
            transform.SetAsLastSibling();
            changed = true;
        }

        if (grid == null)
        {
            if (changed || forceLayout)
            {
                transform.localPosition = localPosition;
                GetComponent<XRDrawerItem>()?.SyncRestPoseFromTransform();
            }

            return false;
        }

        if (changed || forceLayout || _lastLayoutChildCount != parent.childCount)
        {
            grid.LayoutChildren();
            _lastLayoutChildCount = parent.childCount;
        }

        return true;
    }

    private Transform ResolveContentVisibilityParent()
    {
        if (transform.parent != null && transform.parent.GetComponent<DrawerGridLayout3D>() != null)
        {
            return transform.parent;
        }

        return drawerController != null && drawerController.DrawerRoot != null
            ? drawerController.DrawerRoot
            : transform.parent;
    }

    private static Transform ResolveDrawerItemParent(XRContentDrawerController drawer)
    {
        if (drawer == null || drawer.DrawerRoot == null)
        {
            return null;
        }

        var grid = drawer.DrawerRoot.GetComponentInChildren<DrawerGridLayout3D>(true);
        return grid != null ? grid.transform : drawer.DrawerRoot;
    }

    private static void RemoveModelColliders(GameObject modelRoot)
    {
        var colliders = modelRoot.GetComponentsInChildren<Collider>(true);
        for (var i = 0; i < colliders.Length; i++)
        {
            if (Application.isPlaying)
            {
                Destroy(colliders[i]);
            }
            else
            {
                DestroyImmediate(colliders[i]);
            }
        }
    }

    private void ConfigurePencilRenderers(GameObject modelRoot)
    {
        if (modelRoot == null)
        {
            return;
        }

        var renderers = modelRoot.GetComponentsInChildren<Renderer>(true);
        for (var i = 0; i < renderers.Length; i++)
        {
            var renderer = renderers[i];
            if (renderer == null)
            {
                continue;
            }

            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            if (_iconMaterial != null)
            {
                var materialCount = Mathf.Max(1, renderer.sharedMaterials.Length);
                var materials = new Material[materialCount];
                for (var j = 0; j < materials.Length; j++)
                {
                    materials[j] = _iconMaterial;
                }

                renderer.sharedMaterials = materials;
            }
        }
    }

    private GameObject CreateXBar(string objectName)
    {
        var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bar.name = objectName;
        bar.transform.SetParent(transform, false);

        var renderer = bar.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = _iconMaterial;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        var collider = bar.GetComponent<Collider>();
        if (collider != null)
        {
            collider.enabled = false;
            DestroyUnityObject(collider);
        }

        return bar;
    }

    private void ApplyXBarLayout()
    {
        if (_xBarA == null || _xBarB == null)
        {
            return;
        }

        var insetSquareSize = Mathf.Max(0.001f, Mathf.Min(buttonSize.x, buttonSize.y) - xMargin * 2f);
        var thickness = Mathf.Min(Mathf.Max(0.001f, xBarThickness), insetSquareSize * 0.35f);
        var length = Mathf.Max(0.001f, insetSquareSize * Mathf.Sqrt(2f) - thickness);
        var z = -buttonDepth * 0.5f - xSurfaceOffset - xBarDepth * 0.5f;

        ApplyXBarTransform(_xBarA, 45f, length, thickness, z);
        ApplyXBarTransform(_xBarB, -45f, length, thickness, z);
    }

    private void ApplyXBarTransform(GameObject bar, float angle, float length, float thickness, float z)
    {
        bar.transform.localPosition = new Vector3(0f, 0f, z);
        bar.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
        bar.transform.localScale = new Vector3(length, thickness, xBarDepth);
    }

    private void SetXVisible(bool visible)
    {
        SetGameObjectVisible(_xBarA, visible);
        SetGameObjectVisible(_xBarB, visible);
    }

    private void SetPencilVisible(bool visible)
    {
        SetGameObjectVisible(_pencilIconRoot != null ? _pencilIconRoot.gameObject : null, visible);
    }

    private static void SetGameObjectVisible(GameObject go, bool visible)
    {
        if (go != null && go.activeSelf != visible)
        {
            go.SetActive(visible);
        }
    }

    private static void DestroyUnityObject(Object obj)
    {
        if (Application.isPlaying)
        {
            Destroy(obj);
        }
        else
        {
            DestroyImmediate(obj);
        }
    }

    private static Material CreateMaterial(string materialName, Color color)
    {
        var shader = Shader.Find("Universal Render Pipeline/Unlit")
                     ?? Shader.Find("Unlit/Color")
                     ?? Shader.Find("Universal Render Pipeline/Lit")
                     ?? Shader.Find("Sprites/Default")
                     ?? Shader.Find("Standard");
        if (shader == null)
        {
            return null;
        }

        var material = new Material(shader)
        {
            name = materialName,
            color = color,
            enableInstancing = true
        };

        SetMaterialColor(material, color);
        material.SetOverrideTag("RenderType", "Transparent");
        material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        material.SetInt("_ZWrite", 1);
        if (material.HasProperty("_ZTest"))
        {
            material.SetInt("_ZTest", (int)CompareFunction.LessEqual);
        }

        material.EnableKeyword("_ALPHABLEND_ON");
        material.renderQueue = (int)RenderQueue.Transparent - 10;
        return material;
    }

    private static Material CreateOpaqueIconMaterial(string materialName, Color color)
    {
        var shader = Shader.Find("Universal Render Pipeline/Unlit")
                     ?? Shader.Find("Unlit/Color")
                     ?? Shader.Find("Universal Render Pipeline/Lit")
                     ?? Shader.Find("Standard");
        if (shader == null)
        {
            return null;
        }

        color.a = 1f;
        var material = new Material(shader)
        {
            name = materialName,
            color = color,
            enableInstancing = true,
            renderQueue = (int)RenderQueue.Geometry + 20
        };

        SetMaterialColor(material, color);
        material.SetOverrideTag("RenderType", "Opaque");
        material.SetInt("_SrcBlend", (int)BlendMode.One);
        material.SetInt("_DstBlend", (int)BlendMode.Zero);
        material.SetInt("_ZWrite", 1);
        material.DisableKeyword("_ALPHABLEND_ON");
        if (material.HasProperty("_Surface"))
        {
            material.SetFloat("_Surface", 0f);
        }

        return material;
    }

    private static void SetMaterialColor(Material material, Color color)
    {
        if (material == null)
        {
            return;
        }

        material.color = color;
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

        if (material.HasProperty("_Cull"))
        {
            material.SetFloat("_Cull", (float)CullMode.Off);
        }
    }

    private readonly struct TransformState
    {
        public readonly Transform Transform;
        public readonly bool WasActiveSelf;

        public TransformState(Transform transform, bool wasActiveSelf)
        {
            Transform = transform;
            WasActiveSelf = wasActiveSelf;
        }
    }
}
