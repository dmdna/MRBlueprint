using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
[RequireComponent(typeof(XRDrawerItem))]
public sealed class MXInkMeshDrawerButton : MonoBehaviour
{
    private const string ButtonName = "DrawerItem_MeshDrawingToggle";

    [SerializeField] private XRContentDrawerController drawerController;
    [SerializeField] private Vector3 localPosition = new(0.56f, 0.47f, -0.055f);
    [SerializeField] private Vector2 buttonSize = new(0.11f, 0.11f);
    [SerializeField] private float buttonDepth = 0.018f;
    [SerializeField] private Color inactiveColor = new(0.1f, 0.34f, 0.5f, 0.96f);
    [SerializeField] private Color activeColor = new(0.56f, 0.14f, 0.16f, 0.96f);
    [SerializeField] private Color iconColor = Color.white;

    private readonly List<TransformState> _hiddenSiblings = new();
    private MeshRenderer _plateRenderer;
    private Material _plateMaterial;
    private Material _iconMaterial;
    private LineRenderer _pencilLineA;
    private LineRenderer _pencilLineB;
    private LineRenderer _xLineA;
    private LineRenderer _xLineB;
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

        var root = new GameObject(ButtonName);
        root.transform.SetParent(drawer.DrawerRoot, false);
        root.transform.localPosition = new Vector3(0.56f, 0.47f, -0.055f);
        root.transform.localRotation = Quaternion.identity;
        root.transform.localScale = Vector3.one;

        var drawerItem = root.AddComponent<XRDrawerItem>();
        drawerItem.SetIdleAnimation(0f, 1f, 0f);

        var button = root.AddComponent<MXInkMeshDrawerButton>();
        button.Configure(drawer);
        button.EnsureVisuals();
        button.RefreshVisualState();
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

        transform.localPosition = localPosition;
        transform.localRotation = Quaternion.identity;
        ApplyDrawerContentVisibility();
    }

    public void Configure(XRContentDrawerController drawer)
    {
        if (drawer != null)
        {
            drawerController = drawer;
        }
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
        var drawerRoot = drawerController.DrawerRoot;
        for (var i = 0; i < drawerRoot.childCount; i++)
        {
            var child = drawerRoot.GetChild(i);
            if (child == transform || child.IsChildOf(transform))
            {
                continue;
            }

            _hiddenSiblings.Add(new TransformState(child, child.gameObject.activeSelf));
            child.gameObject.SetActive(false);
        }

        _contentHidden = true;
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

        SetLineVisible(_pencilLineA, !active);
        SetLineVisible(_pencilLineB, !active);
        SetLineVisible(_xLineA, active);
        SetLineVisible(_xLineB, active);
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
        _iconMaterial = CreateMaterial("MXInkMeshDrawerButtonIcon", iconColor);

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

        _pencilLineA = CreateIconLine("PencilBody");
        _pencilLineB = CreateIconLine("PencilTip");
        _xLineA = CreateIconLine("CloseA");
        _xLineB = CreateIconLine("CloseB");
        ConfigureIconLines();
    }

    private LineRenderer CreateIconLine(string objectName)
    {
        var go = new GameObject(objectName);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, 0f, -buttonDepth * 0.65f);
        go.transform.localRotation = Quaternion.identity;

        var line = go.AddComponent<LineRenderer>();
        line.sharedMaterial = _iconMaterial;
        line.useWorldSpace = false;
        line.alignment = LineAlignment.View;
        line.textureMode = LineTextureMode.Stretch;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.startWidth = 0.012f;
        line.endWidth = 0.012f;
        line.startColor = iconColor;
        line.endColor = iconColor;
        line.positionCount = 2;
        return line;
    }

    private void ConfigureIconLines()
    {
        SetLine(_pencilLineA, new Vector3(-0.032f, -0.026f, 0f), new Vector3(0.026f, 0.032f, 0f));
        SetLine(_pencilLineB, new Vector3(0.026f, 0.032f, 0f), new Vector3(0.04f, 0.018f, 0f));
        SetLine(_xLineA, new Vector3(-0.034f, -0.034f, 0f), new Vector3(0.034f, 0.034f, 0f));
        SetLine(_xLineB, new Vector3(-0.034f, 0.034f, 0f), new Vector3(0.034f, -0.034f, 0f));
    }

    private static void SetLine(LineRenderer line, Vector3 a, Vector3 b)
    {
        if (line == null)
        {
            return;
        }

        line.SetPosition(0, a);
        line.SetPosition(1, b);
    }

    private static void SetLineVisible(LineRenderer line, bool visible)
    {
        if (line != null && line.enabled != visible)
        {
            line.enabled = visible;
        }
    }

    private static Material CreateMaterial(string materialName, Color color)
    {
        var shader = Shader.Find("MRBlueprint/RayNoStackTransparent")
                     ?? Shader.Find("Universal Render Pipeline/Unlit")
                     ?? Shader.Find("Sprites/Default")
                     ?? Shader.Find("Unlit/Color")
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
        material.SetInt("_ZWrite", 0);
        material.EnableKeyword("_ALPHABLEND_ON");
        material.renderQueue = (int)RenderQueue.Transparent + 24;
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
