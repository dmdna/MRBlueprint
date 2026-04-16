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
    [SerializeField] private float headsetPanelWorldScale = 0.0015f;
    [SerializeField] private Vector2 headsetPanelCanvasSize = new Vector2(320f, 456f);

    private PlaceableAsset _target;
    private Vector3 _scaleBasis = Vector3.one;
    private float _scaleUniformRef = 1f;
    private GameObject _canvasRoot;
    private GameObject _panelRoot;
    private Slider _massSlider;
    private Slider _scaleSlider;
    private Slider _valueSlider;
    private Toggle _gravityToggle;
    private HueSaturationWheelControl _hueWheel;

    private float _h;
    private float _s;
    private float _v = 1f;
    private Text _titleLabel;
    private bool _suppressCallbacks;

    private void Start()
    {
        EnsureEventSystem();
        BuildUi();

        if (AssetSelectionManager.Instance != null)
        {
            AssetSelectionManager.Instance.OnSelectionChanged += OnSelectionChanged;
            OnSelectionChanged(AssetSelectionManager.Instance.SelectedAsset);
        }
    }

    private void OnDestroy()
    {
        if (AssetSelectionManager.Instance != null)
        {
            AssetSelectionManager.Instance.OnSelectionChanged -= OnSelectionChanged;
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
        _panelRoot.SetActive(hasTarget);
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
        _scaleSlider.SetValueWithoutNotify(Mathf.InverseLerp(scaleMin, scaleMax, _scaleUniformRef));
        Color.RGBToHSV(_target.GetColor(), out _h, out _s, out _v);
        _valueSlider.SetValueWithoutNotify(_v);
        _hueWheel?.SetThumbFromHs(_h, _s);
        _gravityToggle.SetIsOnWithoutNotify(_target.GetUseGravity());
        _suppressCallbacks = false;
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
        bg.color = new Color(0.08f, 0.08f, 0.1f, 0.92f);
        var rt = _panelRoot.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-12f, -12f);
        rt.sizeDelta = new Vector2(280f, 432f);

        float y = -10f;
        const float rowH = 22f;
        const float gap = 6f;

        _titleLabel = CreateLabel(_panelRoot.transform, "Title", "Object", 16, ref y, rowH + 4);

        CreateRotateButtonRow(_panelRoot.transform, ref y, rowH + 4, gap);

        _massSlider = CreateLabeledSlider(_panelRoot.transform, "Mass", ref y, rowH, gap, OnMassChanged);
        _scaleSlider = CreateLabeledSlider(_panelRoot.transform, "Scale", ref y, rowH, gap, OnScaleChanged);

        CreateLabel(_panelRoot.transform, "ColorHdr", "Color", 12, ref y, 16f);
        BuildHueSaturationWheel(_panelRoot.transform, ref y, gap);
        _valueSlider = CreateLabeledSlider(_panelRoot.transform, "Brightness", ref y, rowH, gap, OnBrightnessChanged);

        y -= gap;
        var gravRow = new GameObject("GravityRow");
        gravRow.transform.SetParent(_panelRoot.transform, false);
        var gravRt = gravRow.AddComponent<RectTransform>();
        gravRt.anchorMin = new Vector2(0f, 1f);
        gravRt.anchorMax = new Vector2(1f, 1f);
        gravRt.pivot = new Vector2(0.5f, 1f);
        gravRt.anchoredPosition = new Vector2(0f, y);
        gravRt.sizeDelta = new Vector2(-20f, rowH);

        var gravText = CreateText(gravRow.transform, "Gravity", "Gravity", 14, TextAnchor.MiddleLeft);
        var gravTextRt = gravText.GetComponent<RectTransform>();
        gravTextRt.anchorMin = new Vector2(0f, 0f);
        gravTextRt.anchorMax = new Vector2(0.55f, 1f);
        gravTextRt.offsetMin = Vector2.zero;
        gravTextRt.offsetMax = Vector2.zero;

        var toggleGo = new GameObject("GravityToggle");
        toggleGo.transform.SetParent(gravRow.transform, false);
        var toggleRt = toggleGo.AddComponent<RectTransform>();
        toggleRt.anchorMin = new Vector2(0.6f, 0.5f);
        toggleRt.anchorMax = new Vector2(0.6f, 0.5f);
        toggleRt.sizeDelta = new Vector2(28f, 28f);
        toggleRt.anchoredPosition = Vector2.zero;
        var toggleBg = toggleGo.AddComponent<Image>();
        toggleBg.color = new Color(0.25f, 0.25f, 0.28f, 1f);
        _gravityToggle = toggleGo.AddComponent<Toggle>();
        _gravityToggle.targetGraphic = toggleBg;
        _gravityToggle.graphic = CreateToggleGraphic(toggleGo.transform);
        _gravityToggle.onValueChanged.AddListener(OnGravityChanged);
        y -= rowH + gap;

        CreateButton(_panelRoot.transform, "Delete", ref y, rowH + 6, gap, OnDeleteClicked);

        _panelRoot.SetActive(false);
    }

    private void ConfigureCanvas(GameObject canvasGo, Canvas canvas)
    {
        _canvasRoot = canvasGo;
        var scaler = canvasGo.AddComponent<CanvasScaler>();

        if (!useHeadsetAnchoredCanvas)
        {
            canvasGo.transform.SetParent(transform, false);
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            return;
        }

        var anchor = ResolveHeadsetPanelAnchor();
        canvasGo.transform.SetParent(anchor != null ? anchor : transform, false);
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = ResolveHeadsetPanelCamera(anchor);

        var canvasRect = canvasGo.GetComponent<RectTransform>();
        canvasRect.sizeDelta = headsetPanelCanvasSize;
        canvasRect.localPosition = headsetPanelLocalPosition;
        canvasRect.localRotation = Quaternion.Euler(headsetPanelLocalEuler);
        canvasRect.localScale = Vector3.one * headsetPanelWorldScale;

        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        scaler.dynamicPixelsPerUnit = 10f;
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
    }

    private void OnScaleChanged(float t)
    {
        if (_suppressCallbacks || _target == null)
        {
            return;
        }

        var u = Mathf.Lerp(scaleMin, scaleMax, t);
        _target.SetScale(_scaleBasis * (u / _scaleUniformRef));
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
        thumbImg.color = Color.white;
        thumbImg.raycastTarget = false;

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

    private void OnDeleteClicked()
    {
        if (_target == null)
        {
            return;
        }

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
        img.color = new Color(0.22f, 0.38f, 0.62f, 1f);
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
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
        var row = new GameObject(label + "Row");
        row.transform.SetParent(parent, false);
        var rowRt = row.AddComponent<RectTransform>();
        rowRt.anchorMin = new Vector2(0f, 1f);
        rowRt.anchorMax = new Vector2(1f, 1f);
        rowRt.pivot = new Vector2(0.5f, 1f);
        rowRt.anchoredPosition = new Vector2(0f, y);
        rowRt.sizeDelta = new Vector2(-20f, rowH);

        var lt = CreateText(row.transform, "L", label, 12, TextAnchor.MiddleLeft);
        var lrt = lt.GetComponent<RectTransform>();
        lrt.anchorMin = new Vector2(0f, 0f);
        lrt.anchorMax = new Vector2(0.38f, 1f);
        lrt.offsetMin = Vector2.zero;
        lrt.offsetMax = Vector2.zero;

        var sliderGo = new GameObject("Slider");
        sliderGo.transform.SetParent(row.transform, false);
        var srt = sliderGo.AddComponent<RectTransform>();
        srt.anchorMin = new Vector2(0.4f, 0.5f);
        srt.anchorMax = new Vector2(1f, 0.5f);
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
        bgIm.color = new Color(0.2f, 0.2f, 0.22f, 1f);

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
        fIm.color = new Color(0.35f, 0.65f, 1f, 1f);

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
        hIm.color = Color.white;

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
        img.color = new Color(0.55f, 0.18f, 0.18f, 1f);
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
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
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                 ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
        t.text = value;
        t.fontSize = fontSize;
        t.color = Color.white;
        t.alignment = alignment;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
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
        im.color = new Color(0.3f, 0.85f, 0.45f, 1f);
        return im;
    }
}
