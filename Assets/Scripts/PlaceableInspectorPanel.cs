using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.InputSystem.UI;

/// <summary>
/// Minimal screen-space inspector for the selected <see cref="PlaceableAsset"/>.
/// Creates Canvas + EventSystem at runtime if missing so the MVP works without scene UI setup.
/// </summary>
public class PlaceableInspectorPanel : MonoBehaviour
{
    private const float MaxHeadsetPanelWorldScale = 0.00135f;
    private static readonly Vector2 ObjectPanelSize = new Vector2(300f, 492f);
    private static readonly Vector2 DrawingPanelSize = new Vector2(300f, 176f);
    private static readonly Color PanelBackground = new Color(0.035f, 0.04f, 0.048f, 0.94f);
    private static readonly Color PanelAccent = new Color(0.22f, 0.62f, 1f, 1f);
    private static readonly Color TextPrimary = new Color(0.93f, 0.97f, 1f, 1f);
    private static readonly Color TextSecondary = new Color(0.64f, 0.77f, 0.9f, 1f);
    private static readonly Color ChipBackground = new Color(0.08f, 0.12f, 0.16f, 0.95f);
    private static readonly Color TrackBackground = new Color(0.12f, 0.17f, 0.22f, 0.95f);
    private static readonly Color DangerColor = new Color(0.62f, 0.18f, 0.2f, 1f);

    [SerializeField] private float massMin = 0.1f;
    [SerializeField] private float massMax = 50f;
    [SerializeField] private float scaleMin = 0.2f;
    [SerializeField] private float scaleMax = 4f;
    [SerializeField] private float yawStepDegrees = 15f;
    [SerializeField] private bool useHeadsetAnchoredCanvas;
    [SerializeField] private Transform headsetPanelAnchor;
    [SerializeField] private Camera headsetPanelCamera;
    [SerializeField] private Vector3 headsetPanelLocalPosition = new Vector3(0.32f, 0f, 1.15f);
    [SerializeField] private Vector3 headsetPanelLocalEuler = Vector3.zero;
    [SerializeField] private float headsetPanelWorldScale = 0.00135f;
    [SerializeField] private Vector2 headsetPanelCanvasSize = new Vector2(320f, 492f);

    public static PlaceableInspectorPanel Instance { get; private set; }
    public Vector2 DockPanelSize => ObjectPanelSize;

    private PlaceableAsset _target;
    private PhysicsDrawingSelectable _drawingTarget;
    private Vector3 _scaleBasis = Vector3.one;
    private float _scaleUniformRef = 1f;
    private GameObject _canvasRoot;
    private GameObject _panelRoot;
    private GameObject _drawingPanelRoot;
    private Canvas _canvas;
    private CanvasScaler _canvasScaler;
    private RectTransform _canvasRect;
    private Slider _massSlider;
    private Slider _scaleSlider;
    private Slider _frictionSlider;
    private Slider _restitutionSlider;
    private Slider _valueSlider;
    private Slider _drawingSpringStiffnessSlider;
    private Slider _drawingHingeTorqueSlider;
    private Slider _drawingImpulseForceSlider;
    private Toggle _gravityToggle;
    private Toggle _drawingImpulseInstantToggle;
    private HueSaturationWheelControl _hueWheel;
    private GameObject _drawingSpringStiffnessRow;
    private GameObject _drawingHingeTorqueRow;
    private GameObject _drawingImpulseForceRow;
    private GameObject _drawingImpulseInstantRow;

    private float _h;
    private float _s;
    private float _v = 1f;
    private Text _titleLabel;
    private Text _drawingTitleLabel;
    private Text _massValueLabel;
    private Text _scaleValueLabel;
    private Text _frictionValueLabel;
    private Text _restitutionValueLabel;
    private Text _drawingSpringStiffnessValueLabel;
    private Text _drawingHingeTorqueValueLabel;
    private Text _drawingImpulseForceValueLabel;
    private bool _suppressCallbacks;
    private bool _lensDockActive;
    private bool _lensSettingsVisible = true;

    private float EffectiveScaleMax => Mathf.Lerp(scaleMin, scaleMax, 0.5f);

    private void Awake()
    {
        if (Instance == null || Instance == this)
        {
            Instance = this;
        }
    }

    private void Start()
    {
        EnsureEventSystem();
        BuildUi();

        if (AssetSelectionManager.Instance != null)
        {
            AssetSelectionManager.Instance.OnSelectionChanged += OnSelectionChanged;
            AssetSelectionManager.Instance.OnPhysicsDrawingSelectionChanged += OnDrawingSelectionChanged;
            OnSelectionChanged(AssetSelectionManager.Instance.SelectedAsset);
            OnDrawingSelectionChanged(AssetSelectionManager.Instance.SelectedPhysicsDrawing);
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        if (AssetSelectionManager.Instance != null)
        {
            AssetSelectionManager.Instance.OnSelectionChanged -= OnSelectionChanged;
            AssetSelectionManager.Instance.OnPhysicsDrawingSelectionChanged -= OnDrawingSelectionChanged;
        }

        if (_hueWheel != null)
            _hueWheel.HsChanged -= OnWheelHsChanged;

        if (_canvasRoot != null)
        {
            Destroy(_canvasRoot);
        }
    }

    private void OnSelectionChanged(PlaceableAsset asset)
    {
        _target = asset;
        if (_panelRoot == null)
        {
            return;
        }

        var hasTarget = _target != null;
        RefreshPanelVisibility();
        if (hasTarget && _drawingPanelRoot != null)
        {
            _drawingPanelRoot.SetActive(false);
        }

        if (!hasTarget)
        {
            return;
        }

        _scaleBasis = _target.GetScale();
        if (_scaleBasis == Vector3.zero)
        {
            _scaleBasis = Vector3.one;
        }

        _scaleUniformRef = Mathf.Max(_scaleBasis.x, _scaleBasis.y, _scaleBasis.z, 1e-4f);

        _suppressCallbacks = true;
        _titleLabel.text = string.IsNullOrEmpty(_target.AssetDisplayName) ? "Object" : _target.AssetDisplayName;
        _massSlider.SetValueWithoutNotify(Mathf.InverseLerp(massMin, massMax, _target.GetMass()));
        _scaleSlider.SetValueWithoutNotify(Mathf.InverseLerp(scaleMin, EffectiveScaleMax, _scaleUniformRef));
        if (_frictionSlider != null)
            _frictionSlider.SetValueWithoutNotify(_target.GetFriction());
        if (_restitutionSlider != null)
            _restitutionSlider.SetValueWithoutNotify(_target.GetRestitution());
        Color.RGBToHSV(_target.GetColor(), out _h, out _s, out _v);
        _valueSlider.SetValueWithoutNotify(_v);
        _hueWheel?.SetThumbFromHs(_h, _s);
        _gravityToggle.SetIsOnWithoutNotify(_target.GetUseGravity());
        RefreshObjectReadouts();
        _suppressCallbacks = false;
    }

    private void OnDrawingSelectionChanged(PhysicsDrawingSelectable drawing)
    {
        _drawingTarget = drawing;
        if (_drawingPanelRoot == null)
        {
            return;
        }

        var hasDrawing = _drawingTarget != null;
        RefreshPanelVisibility();
        if (!hasDrawing)
        {
            return;
        }

        if (_panelRoot != null)
        {
            _panelRoot.SetActive(false);
        }

        _drawingTitleLabel.text = string.IsNullOrEmpty(_drawingTarget.DisplayName)
            ? "Drawing"
            : _drawingTarget.DisplayName;

        RefreshDrawingControls();
    }

    public void DockToPhysicsLens(Camera camera, Vector3 worldPosition, Quaternion worldRotation, float worldScale, bool visible)
    {
        if (_canvasRoot == null || _canvas == null || _canvasScaler == null)
        {
            return;
        }

        _lensDockActive = true;
        _lensSettingsVisible = visible;

        _canvasRoot.transform.SetParent(null, true);
        _canvasRoot.transform.SetPositionAndRotation(worldPosition, worldRotation);
        _canvasRoot.transform.localScale = Vector3.one * Mathf.Max(0.0001f, worldScale);

        _canvas.renderMode = RenderMode.WorldSpace;
        _canvas.worldCamera = camera != null ? camera : Camera.main;
        if (_canvasRect != null)
        {
            _canvasRect.sizeDelta = ObjectPanelSize;
        }

        _canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        _canvasScaler.dynamicPixelsPerUnit = 10f;
        ApplyPanelPlacement(true);
        RefreshPanelVisibility();
    }

    public void ClearPhysicsLensDock()
    {
        if (!_lensDockActive)
        {
            return;
        }

        _lensDockActive = false;
        _lensSettingsVisible = true;
        ApplyDefaultCanvasPlacement();
        ApplyPanelPlacement(false);
        RefreshPanelVisibility();
    }

    public void SetPhysicsLensSettingsVisible(bool visible)
    {
        _lensSettingsVisible = visible;
        RefreshPanelVisibility();
    }

    private void RefreshPanelVisibility()
    {
        if (_panelRoot != null)
        {
            _panelRoot.SetActive(_target != null && (!_lensDockActive || _lensSettingsVisible));
        }

        if (_drawingPanelRoot != null)
        {
            _drawingPanelRoot.SetActive(_drawingTarget != null && !_lensDockActive);
        }
    }

    private void EnsureEventSystem()
    {
        if (Object.FindFirstObjectByType<EventSystem>() != null)
        {
            return;
        }

        var esGo = new GameObject("EventSystem");
        esGo.AddComponent<EventSystem>();
        esGo.AddComponent<InputSystemUIInputModule>();
    }

    private void BuildUi()
    {
        var canvasGo = new GameObject("PlaceableInspectorCanvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        ConfigureCanvas(canvasGo, canvas);
        canvasGo.AddComponent<GraphicRaycaster>();

        _panelRoot = new GameObject("Panel");
        _panelRoot.transform.SetParent(canvasGo.transform, false);
        var bg = _panelRoot.AddComponent<Image>();
        bg.color = PanelBackground;
        bg.raycastTarget = true;
        PhysicsLensRenderUtility.ApplyUiPerspectiveOverlayMaterial(bg);
        var rt = _panelRoot.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-12f, -12f);
        rt.sizeDelta = ObjectPanelSize;

        float y = -14f;
        const float rowH = 24f;
        const float gap = 7f;

        _titleLabel = CreateLabel(_panelRoot.transform, "Title", "Object", 18, ref y, 30f);
        CreateAccentRule(_panelRoot.transform, y + 2f, 248f);
        y -= 10f;

        CreateRotateButtonRow(_panelRoot.transform, ref y, rowH + 4, gap);

        _massSlider = CreateLabeledSliderWithValue(_panelRoot.transform, "Mass", ref y, rowH, gap, OnMassChanged, out _massValueLabel);
        _scaleSlider = CreateLabeledSliderWithValue(_panelRoot.transform, "Scale", ref y, rowH, gap, OnScaleChanged, out _scaleValueLabel);
        _frictionSlider = CreateLabeledSliderWithValue(_panelRoot.transform, "Friction", ref y, rowH, gap, OnFrictionChanged, out _frictionValueLabel);
        _restitutionSlider = CreateLabeledSliderWithValue(_panelRoot.transform, "Restitution", ref y, rowH, gap, OnRestitutionChanged, out _restitutionValueLabel);

        CreateLabel(_panelRoot.transform, "ColorHdr", "Color", 13, ref y, 18f);
        BuildHueSaturationWheel(_panelRoot.transform, ref y, gap);
        _valueSlider = CreateLabeledSlider(_panelRoot.transform, "Brightness", ref y, rowH, gap, OnBrightnessChanged);

        y -= gap;
        _gravityToggle = CreateLabeledToggle(_panelRoot.transform, "Gravity", ref y, rowH, gap, OnGravityChanged);

        CreateButton(_panelRoot.transform, "Delete", ref y, rowH + 6, gap, OnDeleteClicked);

        _panelRoot.SetActive(false);
        BuildDrawingUi(canvasGo.transform);
        ApplyPanelPlacement(false);
    }

    private void BuildDrawingUi(Transform canvasTransform)
    {
        _drawingPanelRoot = new GameObject("DrawingPanel");
        _drawingPanelRoot.transform.SetParent(canvasTransform, false);
        var bg = _drawingPanelRoot.AddComponent<Image>();
        bg.color = PanelBackground;
        bg.raycastTarget = true;
        PhysicsLensRenderUtility.ApplyUiPerspectiveOverlayMaterial(bg);
        var rt = _drawingPanelRoot.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-12f, -12f);
        rt.sizeDelta = DrawingPanelSize;

        float y = -14f;
        const float rowH = 24f;
        const float gap = 7f;
        _drawingTitleLabel = CreateLabel(_drawingPanelRoot.transform, "DrawingTitle", "Drawing", 18, ref y, 28f);
        CreateAccentRule(_drawingPanelRoot.transform, y + 2f, 248f);
        y -= 10f;
        _drawingSpringStiffnessSlider = CreateLabeledSliderWithValue(
            _drawingPanelRoot.transform, "Stiffness", ref y, rowH, gap, OnDrawingSpringStiffnessChanged,
            out _drawingSpringStiffnessRow, out _drawingSpringStiffnessValueLabel);
        _drawingHingeTorqueSlider = CreateLabeledSliderWithValue(
            _drawingPanelRoot.transform, "Torque", ref y, rowH, gap, OnDrawingHingeTorqueChanged,
            out _drawingHingeTorqueRow, out _drawingHingeTorqueValueLabel);
        _drawingImpulseForceSlider = CreateLabeledSliderWithValue(
            _drawingPanelRoot.transform, "Force", ref y, rowH, gap, OnDrawingImpulseForceChanged,
            out _drawingImpulseForceRow, out _drawingImpulseForceValueLabel);
        _drawingImpulseInstantToggle = CreateLabeledToggle(
            _drawingPanelRoot.transform, "Instant", ref y, rowH, gap, OnDrawingImpulseInstantChanged,
            out _drawingImpulseInstantRow);
        var deleteY = -132f;
        CreateButton(_drawingPanelRoot.transform, "Delete", ref deleteY, rowH + 6, gap, OnDrawingDeleteClicked);
        HideAllDrawingControlRows();
        _drawingPanelRoot.SetActive(false);
    }

    private void ConfigureCanvas(GameObject canvasGo, Canvas canvas)
    {
        _canvasRoot = canvasGo;
        _canvas = canvas;
        _canvasRect = canvasGo.GetComponent<RectTransform>();
        _canvasScaler = canvasGo.AddComponent<CanvasScaler>();
        ApplyDefaultCanvasPlacement();
    }

    private void ApplyDefaultCanvasPlacement()
    {
        if (_canvasRoot == null || _canvas == null || _canvasScaler == null)
        {
            return;
        }

        if (!useHeadsetAnchoredCanvas)
        {
            _canvasRoot.transform.SetParent(transform, false);
            _canvasRoot.transform.localPosition = Vector3.zero;
            _canvasRoot.transform.localRotation = Quaternion.identity;
            _canvasRoot.transform.localScale = Vector3.one;
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.worldCamera = null;
            _canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            return;
        }

        var anchor = ResolveHeadsetPanelAnchor();
        _canvasRoot.transform.SetParent(anchor != null ? anchor : transform, false);
        _canvas.renderMode = RenderMode.WorldSpace;
        _canvas.worldCamera = ResolveHeadsetPanelCamera(anchor);

        if (_canvasRect != null)
        {
            _canvasRect.sizeDelta = new Vector2(
                Mathf.Max(headsetPanelCanvasSize.x, ObjectPanelSize.x),
                Mathf.Max(headsetPanelCanvasSize.y, ObjectPanelSize.y));
            _canvasRect.localPosition = headsetPanelLocalPosition;
            _canvasRect.localRotation = Quaternion.Euler(headsetPanelLocalEuler);
            _canvasRect.localScale = Vector3.one * Mathf.Clamp(
                headsetPanelWorldScale,
                0.0005f,
                MaxHeadsetPanelWorldScale);
        }

        _canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        _canvasScaler.dynamicPixelsPerUnit = 10f;
    }

    private void ApplyPanelPlacement(bool docked)
    {
        ApplyPanelPlacement(_panelRoot, ObjectPanelSize, docked);
        ApplyPanelPlacement(_drawingPanelRoot, DrawingPanelSize, docked);
    }

    private static void ApplyPanelPlacement(GameObject panelRoot, Vector2 size, bool docked)
    {
        if (panelRoot == null)
        {
            return;
        }

        var rect = panelRoot.GetComponent<RectTransform>();
        if (rect == null)
        {
            return;
        }

        rect.sizeDelta = size;
        if (docked)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            return;
        }

        rect.anchorMin = new Vector2(1f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = new Vector2(-12f, -12f);
    }

    private Transform ResolveHeadsetPanelAnchor()
    {
        if (headsetPanelAnchor != null)
        {
            return headsetPanelAnchor;
        }

        var mainCamera = Camera.main;
        return mainCamera != null ? mainCamera.transform : null;
    }

    private Camera ResolveHeadsetPanelCamera(Transform anchor)
    {
        if (headsetPanelCamera != null)
        {
            return headsetPanelCamera;
        }

        if (anchor != null && anchor.TryGetComponent<Camera>(out var anchorCamera))
        {
            return anchorCamera;
        }

        return Camera.main;
    }

    private void OnMassChanged(float t)
    {
        if (_suppressCallbacks || _target == null)
        {
            return;
        }

        _target.SetMass(Mathf.Lerp(massMin, massMax, t));
        RefreshObjectReadouts();
    }

    private void OnScaleChanged(float t)
    {
        if (_suppressCallbacks || _target == null)
        {
            return;
        }

        var u = Mathf.Lerp(scaleMin, EffectiveScaleMax, t);
        _target.SetScale(_scaleBasis * (u / _scaleUniformRef));
        RefreshObjectReadouts();
    }

    private void OnFrictionChanged(float t)
    {
        if (_suppressCallbacks || _target == null)
        {
            return;
        }

        _target.SetFriction(t);
        RefreshObjectReadouts();
    }

    private void OnRestitutionChanged(float t)
    {
        if (_suppressCallbacks || _target == null)
        {
            return;
        }

        _target.SetRestitution(t);
        RefreshObjectReadouts();
    }

    private void OnWheelHsChanged(float h, float s)
    {
        if (_suppressCallbacks || _target == null)
            return;

        _h = h;
        _s = s;
        ApplyColorFromHsv();
    }

    private void OnBrightnessChanged(float _)
    {
        if (_suppressCallbacks || _target == null)
            return;

        _v = _valueSlider.value;
        ApplyColorFromHsv();
    }

    private void ApplyColorFromHsv()
    {
        if (_target == null)
            return;

        var c = Color.HSVToRGB(_h, _s, _v);
        c.a = 1f;
        _target.SetColor(c);
    }

    private void BuildHueSaturationWheel(Transform parent, ref float y, float gap)
    {
        const float blockH = 126f;
        var row = new GameObject("ColorWheelRow");
        row.transform.SetParent(parent, false);
        var rowRt = row.AddComponent<RectTransform>();
        rowRt.anchorMin = new Vector2(0f, 1f);
        rowRt.anchorMax = new Vector2(1f, 1f);
        rowRt.pivot = new Vector2(0.5f, 1f);
        rowRt.anchoredPosition = new Vector2(0f, y);
        rowRt.sizeDelta = new Vector2(-20f, blockH);

        var wheelGo = new GameObject("HueSatWheel");
        wheelGo.transform.SetParent(row.transform, false);
        var wheelRt = wheelGo.AddComponent<RectTransform>();
        wheelRt.anchorMin = new Vector2(0.5f, 0.5f);
        wheelRt.anchorMax = new Vector2(0.5f, 0.5f);
        wheelRt.pivot = new Vector2(0.5f, 0.5f);
        wheelRt.anchoredPosition = Vector2.zero;
        wheelRt.sizeDelta = new Vector2(116f, 116f);

        const int texSize = 168;
        var tex = HueSaturationWheelControl.CreateHueSaturationDiskTexture(texSize);
        var spr = Sprite.Create(tex, new Rect(0, 0, texSize, texSize), new Vector2(0.5f, 0.5f), 100f);
        var wheelImg = wheelGo.AddComponent<Image>();
        wheelImg.sprite = spr;
        wheelImg.preserveAspect = true;
        wheelImg.raycastTarget = true;
        PhysicsLensRenderUtility.ApplyUiPerspectiveOverlayMaterial(wheelImg);

        var thumbGo = new GameObject("Thumb");
        thumbGo.transform.SetParent(wheelGo.transform, false);
        var thumbRt = thumbGo.AddComponent<RectTransform>();
        thumbRt.anchorMin = new Vector2(0.5f, 0.5f);
        thumbRt.anchorMax = new Vector2(0.5f, 0.5f);
        thumbRt.pivot = new Vector2(0.5f, 0.5f);
        thumbRt.sizeDelta = new Vector2(14f, 14f);
        var thumbImg = thumbGo.AddComponent<Image>();
        var whiteTex = Texture2D.whiteTexture;
        thumbImg.sprite = Sprite.Create(
            whiteTex, new Rect(0, 0, whiteTex.width, whiteTex.height), new Vector2(0.5f, 0.5f), 100f);
        thumbImg.color = TextPrimary;
        thumbImg.raycastTarget = false;
        PhysicsLensRenderUtility.ApplyUiPerspectiveOverlayMaterial(thumbImg);

        _hueWheel = wheelGo.AddComponent<HueSaturationWheelControl>();
        _hueWheel.Init(thumbRt);
        _hueWheel.HsChanged += OnWheelHsChanged;

        y -= blockH + gap;
    }

    private void OnGravityChanged(bool on)
    {
        if (_suppressCallbacks || _target == null)
        {
            return;
        }

        _target.SetUseGravity(on);
    }

    private void RefreshObjectReadouts()
    {
        if (_target == null)
        {
            return;
        }

        if (_massValueLabel != null)
        {
            _massValueLabel.text = _target.GetMass().ToString("0.##") + " kg";
        }

        if (_scaleValueLabel != null && _scaleSlider != null)
        {
            _scaleValueLabel.text = Mathf.RoundToInt(Mathf.Clamp01(_scaleSlider.value) * 100f) + "%";
        }

        if (_frictionValueLabel != null)
        {
            _frictionValueLabel.text = "mu " + _target.GetDynamicFrictionCoefficient().ToString("0.00");
        }

        if (_restitutionValueLabel != null)
        {
            _restitutionValueLabel.text = "e " + _target.GetRestitutionCoefficient().ToString("0.00");
        }
    }

    private void RefreshDrawingReadouts()
    {
        if (_drawingTarget == null)
        {
            return;
        }

        if (_drawingSpringStiffnessValueLabel != null)
        {
            _drawingSpringStiffnessValueLabel.text = FormatDrawingNumber(
                SandboxStrokePlaceablePhysicsApplier.ResolveSpringStrength(_drawingTarget.SpringStiffness),
                "N/m");
        }

        if (_drawingHingeTorqueValueLabel != null)
        {
            _drawingHingeTorqueValueLabel.text = FormatDrawingNumber(
                SandboxStrokePlaceablePhysicsApplier.ResolveHingeTorqueEstimate(_drawingTarget.HingeTorque),
                "N·m");
        }

        if (_drawingImpulseForceValueLabel != null)
        {
            _drawingImpulseForceValueLabel.text = FormatDrawingNumber(
                SandboxStrokePlaceablePhysicsApplier.ResolveImpulseStrength(
                    _drawingTarget.ImpulseForce,
                    _drawingTarget.ImpulseInstant),
                _drawingTarget.ImpulseInstant ? "N·s" : "N");
        }
    }

    private static string FormatDrawingNumber(float value, string unit)
    {
        var abs = Mathf.Abs(value);
        var number = abs >= 100f
            ? value.ToString("0")
            : abs >= 10f
                ? value.ToString("0.0")
                : value.ToString("0.00");
        return number + " " + unit;
    }

    private void OnDrawingSpringStiffnessChanged(float value)
    {
        if (_suppressCallbacks || _drawingTarget == null)
        {
            return;
        }

        _drawingTarget.SetSpringStiffness(value);
        RefreshDrawingReadouts();
    }

    private void OnDrawingHingeTorqueChanged(float value)
    {
        if (_suppressCallbacks || _drawingTarget == null)
        {
            return;
        }

        _drawingTarget.SetHingeTorque(value);
        RefreshDrawingReadouts();
    }

    private void OnDrawingImpulseForceChanged(float value)
    {
        if (_suppressCallbacks || _drawingTarget == null)
        {
            return;
        }

        _drawingTarget.SetImpulseForce(value);
        RefreshDrawingReadouts();
    }

    private void OnDrawingImpulseInstantChanged(bool instant)
    {
        if (_suppressCallbacks || _drawingTarget == null)
        {
            return;
        }

        _drawingTarget.SetImpulseInstant(instant);
        RefreshDrawingReadouts();
    }

    private void OnDrawingDeleteClicked()
    {
        if (_drawingTarget == null)
        {
            return;
        }

        UiMenuSelectSoundHub.TryPlayDeleteObject();
        _drawingTarget.Delete();
    }

    private void RefreshDrawingControls()
    {
        if (_drawingTarget == null)
        {
            HideAllDrawingControlRows();
            return;
        }

        _suppressCallbacks = true;
        _drawingSpringStiffnessSlider.SetValueWithoutNotify(_drawingTarget.SpringStiffness);
        _drawingHingeTorqueSlider.SetValueWithoutNotify(_drawingTarget.HingeTorque);
        _drawingImpulseForceSlider.SetValueWithoutNotify(_drawingTarget.ImpulseForce);
        _drawingImpulseInstantToggle.SetIsOnWithoutNotify(_drawingTarget.ImpulseInstant);
        RefreshDrawingReadouts();
        _suppressCallbacks = false;

        switch (_drawingTarget.PhysicsIntent)
        {
            case PhysicsIntentType.Spring:
                SetVisibleDrawingControlRows(_drawingSpringStiffnessRow);
                break;
            case PhysicsIntentType.Hinge:
                SetVisibleDrawingControlRows(_drawingHingeTorqueRow);
                break;
            case PhysicsIntentType.Impulse:
                SetVisibleDrawingControlRows(_drawingImpulseForceRow, _drawingImpulseInstantRow);
                break;
            default:
                HideAllDrawingControlRows();
                break;
        }
    }

    private void HideAllDrawingControlRows()
    {
        SetDrawingRowActive(_drawingSpringStiffnessRow, false);
        SetDrawingRowActive(_drawingHingeTorqueRow, false);
        SetDrawingRowActive(_drawingImpulseForceRow, false);
        SetDrawingRowActive(_drawingImpulseInstantRow, false);
    }

    private void SetVisibleDrawingControlRows(params GameObject[] rows)
    {
        HideAllDrawingControlRows();

        const float rowH = 24f;
        const float gap = 7f;
        var y = -56f;

        for (var i = 0; i < rows.Length; i++)
        {
            var row = rows[i];
            if (row == null)
            {
                continue;
            }

            SetDrawingRowActive(row, true);
            var rt = row.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.anchoredPosition = new Vector2(0f, y);
            }

            y -= rowH + gap;
        }
    }

    private static void SetDrawingRowActive(GameObject row, bool active)
    {
        if (row != null)
        {
            row.SetActive(active);
        }
    }

    private void OnDeleteClicked()
    {
        if (_target == null)
        {
            return;
        }

        UiMenuSelectSoundHub.TryPlayDeleteObject();
        _target.Delete();
    }

    private void OnRotateYaw(float deltaDegrees)
    {
        if (_target == null)
        {
            return;
        }

        _target.RotateWorldY(deltaDegrees);
    }

    private void CreateRotateButtonRow(Transform parent, ref float y, float rowH, float gap)
    {
        var row = new GameObject("RotateRow");
        row.transform.SetParent(parent, false);
        var rowRt = row.AddComponent<RectTransform>();
        rowRt.anchorMin = new Vector2(0f, 1f);
        rowRt.anchorMax = new Vector2(1f, 1f);
        rowRt.pivot = new Vector2(0.5f, 1f);
        rowRt.anchoredPosition = new Vector2(0f, y);
        rowRt.sizeDelta = new Vector2(-20f, rowH);

        CreateSplitButton(row.transform, "Rotate left", 0f, 0.48f,
            () => OnRotateYaw(-yawStepDegrees));
        CreateSplitButton(row.transform, "Rotate right", 0.52f, 1f,
            () => OnRotateYaw(yawStepDegrees));

        y -= rowH + gap;
    }

    private static void CreateSplitButton(Transform row, string caption, float anchorMinX, float anchorMaxX, UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject(caption.Replace(" ", "") + "Btn");
        go.transform.SetParent(row, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(anchorMinX, 0f);
        rt.anchorMax = new Vector2(anchorMaxX, 1f);
        const float pad = 3f;
        rt.offsetMin = new Vector2(anchorMinX < 0.01f ? 0f : pad, 0f);
        rt.offsetMax = new Vector2(anchorMaxX > 0.99f ? 0f : -pad, 0f);

        var img = go.AddComponent<Image>();
        img.color = ChipBackground;
        PhysicsLensRenderUtility.ApplyUiPerspectiveOverlayMaterial(img);
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        ApplyButtonColors(btn, img, PanelAccent);
        btn.onClick.AddListener(onClick);
        var tx = CreateText(go.transform, "T", caption, 13, TextAnchor.MiddleCenter);
        var trt = tx.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = Vector2.zero;
        trt.offsetMax = Vector2.zero;
    }

    private static Text CreateLabel(Transform parent, string name, string text, int fontSize, ref float y, float height)
    {
        var t = CreateText(parent, name, text, fontSize, TextAnchor.UpperLeft);
        t.color = fontSize >= 16 ? TextPrimary : TextSecondary;
        var rt = t.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(10f, y);
        rt.sizeDelta = new Vector2(-20f, height);
        y -= height + 4f;
        return t;
    }

    private Slider CreateLabeledSlider(Transform parent, string label, ref float y, float rowH, float gap, UnityEngine.Events.UnityAction<float> onChanged)
    {
        GameObject unusedRow;
        Text unusedValue;
        return CreateLabeledSlider(parent, label, ref y, rowH, gap, onChanged, out unusedRow, false, out unusedValue);
    }

    private Slider CreateLabeledSliderWithValue(
        Transform parent,
        string label,
        ref float y,
        float rowH,
        float gap,
        UnityEngine.Events.UnityAction<float> onChanged,
        out Text valueText)
    {
        GameObject unusedRow;
        return CreateLabeledSlider(parent, label, ref y, rowH, gap, onChanged, out unusedRow, true, out valueText);
    }

    private Slider CreateLabeledSliderWithValue(
        Transform parent,
        string label,
        ref float y,
        float rowH,
        float gap,
        UnityEngine.Events.UnityAction<float> onChanged,
        out GameObject row,
        out Text valueText)
    {
        return CreateLabeledSlider(parent, label, ref y, rowH, gap, onChanged, out row, true, out valueText);
    }

    private Slider CreateLabeledSlider(
        Transform parent,
        string label,
        ref float y,
        float rowH,
        float gap,
        UnityEngine.Events.UnityAction<float> onChanged,
        out GameObject row)
    {
        Text unusedValue;
        return CreateLabeledSlider(parent, label, ref y, rowH, gap, onChanged, out row, false, out unusedValue);
    }

    private Slider CreateLabeledSlider(
        Transform parent,
        string label,
        ref float y,
        float rowH,
        float gap,
        UnityEngine.Events.UnityAction<float> onChanged,
        out GameObject row,
        bool includeValue,
        out Text valueText)
    {
        valueText = null;
        row = new GameObject(label + "Row");
        row.transform.SetParent(parent, false);
        var rowRt = row.AddComponent<RectTransform>();
        rowRt.anchorMin = new Vector2(0f, 1f);
        rowRt.anchorMax = new Vector2(1f, 1f);
        rowRt.pivot = new Vector2(0.5f, 1f);
        rowRt.anchoredPosition = new Vector2(0f, y);
        rowRt.sizeDelta = new Vector2(-20f, rowH);

        var lt = CreateText(row.transform, "L", label, 12, TextAnchor.MiddleLeft);
        lt.color = TextSecondary;
        var lrt = lt.GetComponent<RectTransform>();
        lrt.anchorMin = new Vector2(0f, 0f);
        lrt.anchorMax = new Vector2(includeValue ? 0.28f : 0.38f, 1f);
        lrt.offsetMin = Vector2.zero;
        lrt.offsetMax = Vector2.zero;

        if (includeValue)
        {
            valueText = CreateText(row.transform, "Value", "", 12, TextAnchor.MiddleRight);
            valueText.color = TextPrimary;
            valueText.horizontalOverflow = HorizontalWrapMode.Overflow;
            var vrt = valueText.GetComponent<RectTransform>();
            vrt.anchorMin = new Vector2(0.76f, 0f);
            vrt.anchorMax = new Vector2(1f, 1f);
            vrt.offsetMin = Vector2.zero;
            vrt.offsetMax = Vector2.zero;
        }

        var sliderGo = new GameObject("Slider");
        sliderGo.transform.SetParent(row.transform, false);
        var srt = sliderGo.AddComponent<RectTransform>();
        srt.anchorMin = new Vector2(includeValue ? 0.31f : 0.4f, 0.5f);
        srt.anchorMax = new Vector2(includeValue ? 0.73f : 1f, 0.5f);
        srt.pivot = new Vector2(0.5f, 0.5f);
        srt.anchoredPosition = Vector2.zero;
        srt.sizeDelta = new Vector2(0f, 12f);

        var bg = new GameObject("Background");
        bg.transform.SetParent(sliderGo.transform, false);
        var bgRt = bg.AddComponent<RectTransform>();
        bgRt.anchorMin = Vector2.zero;
        bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = Vector2.zero;
        bgRt.offsetMax = Vector2.zero;
        var bgIm = bg.AddComponent<Image>();
        bgIm.color = TrackBackground;
        PhysicsLensRenderUtility.ApplyUiPerspectiveOverlayMaterial(bgIm);

        var fillArea = new GameObject("Fill Area");
        fillArea.transform.SetParent(sliderGo.transform, false);
        var faRt = fillArea.AddComponent<RectTransform>();
        faRt.anchorMin = Vector2.zero;
        faRt.anchorMax = Vector2.one;
        faRt.offsetMin = new Vector2(4f, 4f);
        faRt.offsetMax = new Vector2(-4f, -4f);

        var fill = new GameObject("Fill");
        fill.transform.SetParent(fillArea.transform, false);
        var fRt = fill.AddComponent<RectTransform>();
        fRt.anchorMin = Vector2.zero;
        fRt.anchorMax = new Vector2(0.5f, 1f);
        fRt.offsetMin = Vector2.zero;
        fRt.offsetMax = Vector2.zero;
        var fIm = fill.AddComponent<Image>();
        fIm.color = PanelAccent;
        PhysicsLensRenderUtility.ApplyUiPerspectiveOverlayMaterial(fIm);

        var handleArea = new GameObject("Handle Slide Area");
        handleArea.transform.SetParent(sliderGo.transform, false);
        var haRt = handleArea.AddComponent<RectTransform>();
        haRt.anchorMin = Vector2.zero;
        haRt.anchorMax = Vector2.one;
        haRt.offsetMin = Vector2.zero;
        haRt.offsetMax = Vector2.zero;

        var handle = new GameObject("Handle");
        handle.transform.SetParent(handleArea.transform, false);
        var hRt = handle.AddComponent<RectTransform>();
        hRt.sizeDelta = new Vector2(14f, 18f);
        var hIm = handle.AddComponent<Image>();
        hIm.color = TextPrimary;
        PhysicsLensRenderUtility.ApplyUiPerspectiveOverlayMaterial(hIm);

        var slider = sliderGo.AddComponent<Slider>();
        slider.fillRect = fRt;
        slider.targetGraphic = hIm;
        slider.handleRect = hRt;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.wholeNumbers = false;
        slider.onValueChanged.AddListener(onChanged);

        y -= rowH + gap;
        return slider;
    }

    private Toggle CreateLabeledToggle(
        Transform parent,
        string label,
        ref float y,
        float rowH,
        float gap,
        UnityEngine.Events.UnityAction<bool> onChanged)
    {
        GameObject unusedRow;
        return CreateLabeledToggle(parent, label, ref y, rowH, gap, onChanged, out unusedRow);
    }

    private Toggle CreateLabeledToggle(
        Transform parent,
        string label,
        ref float y,
        float rowH,
        float gap,
        UnityEngine.Events.UnityAction<bool> onChanged,
        out GameObject row)
    {
        row = new GameObject(label + "Row");
        row.transform.SetParent(parent, false);
        var rowRt = row.AddComponent<RectTransform>();
        rowRt.anchorMin = new Vector2(0f, 1f);
        rowRt.anchorMax = new Vector2(1f, 1f);
        rowRt.pivot = new Vector2(0.5f, 1f);
        rowRt.anchoredPosition = new Vector2(0f, y);
        rowRt.sizeDelta = new Vector2(-20f, rowH);

        var labelText = CreateText(row.transform, label, label, 14, TextAnchor.MiddleLeft);
        labelText.color = TextSecondary;
        var labelTextRt = labelText.GetComponent<RectTransform>();
        labelTextRt.anchorMin = new Vector2(0f, 0f);
        labelTextRt.anchorMax = new Vector2(0.55f, 1f);
        labelTextRt.offsetMin = Vector2.zero;
        labelTextRt.offsetMax = Vector2.zero;

        var toggleGo = new GameObject(label + "Toggle");
        toggleGo.transform.SetParent(row.transform, false);
        var toggleRt = toggleGo.AddComponent<RectTransform>();
        toggleRt.anchorMin = new Vector2(0.6f, 0.5f);
        toggleRt.anchorMax = new Vector2(0.6f, 0.5f);
        toggleRt.sizeDelta = new Vector2(28f, 28f);
        toggleRt.anchoredPosition = Vector2.zero;
        var toggleBg = toggleGo.AddComponent<Image>();
        toggleBg.color = TrackBackground;
        PhysicsLensRenderUtility.ApplyUiPerspectiveOverlayMaterial(toggleBg);
        var toggle = toggleGo.AddComponent<Toggle>();
        toggle.targetGraphic = toggleBg;
        toggle.graphic = CreateToggleGraphic(toggleGo.transform);
        toggle.onValueChanged.AddListener(onChanged);

        y -= rowH + gap;
        return toggle;
    }

    private static Button CreateButton(Transform parent, string caption, ref float y, float height, float gap, UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject(caption + "Button");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, y);
        rt.sizeDelta = new Vector2(-20f, height);
        var img = go.AddComponent<Image>();
        img.color = DangerColor;
        PhysicsLensRenderUtility.ApplyUiPerspectiveOverlayMaterial(img);
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        ApplyButtonColors(btn, img, DangerColor);
        btn.onClick.AddListener(onClick);
        var tx = CreateText(go.transform, "T", caption, 14, TextAnchor.MiddleCenter);
        var trt = tx.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = Vector2.zero;
        trt.offsetMax = Vector2.zero;
        y -= height + gap;
        return btn;
    }

    private static Text CreateText(Transform parent, string name, string value, int fontSize, TextAnchor alignment)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<Text>();
        MrBlueprintUiFont.Apply(t);
        t.text = value;
        t.fontSize = fontSize;
        t.color = TextPrimary;
        t.alignment = alignment;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Truncate;
        t.raycastTarget = false;
        PhysicsLensRenderUtility.ApplyUiPerspectiveOverlayMaterial(t);
        return t;
    }

    private static Graphic CreateToggleGraphic(Transform parent)
    {
        var go = new GameObject("Checkmark");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(4f, 4f);
        rt.offsetMax = new Vector2(-4f, -4f);
        var im = go.AddComponent<Image>();
        im.color = PanelAccent;
        PhysicsLensRenderUtility.ApplyUiPerspectiveOverlayMaterial(im);
        return im;
    }

    private static void CreateAccentRule(Transform parent, float y, float width)
    {
        var go = new GameObject("AccentRule");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(0f, y);
        rt.sizeDelta = new Vector2(width, 3f);
        var image = go.AddComponent<Image>();
        image.color = PanelAccent;
        image.raycastTarget = false;
        PhysicsLensRenderUtility.ApplyUiPerspectiveOverlayMaterial(image);
    }

    private static void ApplyButtonColors(Button button, Graphic targetGraphic, Color accent)
    {
        if (button == null)
            return;

        var colors = button.colors;
        colors.normalColor = targetGraphic != null ? targetGraphic.color : ChipBackground;
        colors.highlightedColor = Color.Lerp(colors.normalColor, accent, 0.42f);
        colors.pressedColor = Color.Lerp(colors.normalColor, accent, 0.7f);
        colors.selectedColor = colors.highlightedColor;
        colors.fadeDuration = 0.08f;
        button.colors = colors;
    }
}
