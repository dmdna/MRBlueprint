using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// Editor main toolbar: Phase A3 shell, Phase B actions, Phase C simulate strip; Phase D1–D2 Edit/Draw mode + bars.
/// </summary>
public class SandboxEditorToolbarFrame : MonoBehaviour
{
    private const int MinimumOverlaySortingOrder = 500;
    private static readonly Color OptionsPanelBackground = new Color(0.035f, 0.04f, 0.048f, 0.98f);
    private static readonly Color OptionsPanelAccent = new Color(0.22f, 0.62f, 1f, 1f);
    private static readonly Color OptionsTextPrimary = new Color(0.93f, 0.97f, 1f, 1f);
    private static readonly Color OptionsTextSecondary = new Color(0.64f, 0.77f, 0.9f, 1f);
    private static readonly Color OptionsTrackBackground = new Color(0.12f, 0.17f, 0.22f, 0.95f);

    [SerializeField] private XRContentDrawerController drawerController;
    [SerializeField] private PlaceableTransformGizmo transformGizmo;
    [SerializeField] private SandboxDrawerHints drawerHints;
    [SerializeField] private string homeSceneName = "HomeMenu";

    [Header("Canvas (match PlaceableInspectorPanel for Quest vs desktop)")]
    [SerializeField] private bool useHeadsetAnchoredCanvas;
    [SerializeField] private Transform headsetPanelAnchor;
    [SerializeField] private Camera headsetPanelCamera;
    [SerializeField] private Vector3 headsetPanelLocalPosition = new Vector3(0f, 0.22f, 0.85f);
    [SerializeField] private Vector3 headsetPanelLocalEuler = new Vector3(-12f, 0f, 0f);
    [SerializeField] private float headsetPanelWorldScale = 0.0012f;
    [SerializeField] private Vector2 headsetPanelCanvasSize = new Vector2(900f, 72f);

    [Header("Layout")]
    [SerializeField] private float barHeight = 52f;
    [SerializeField] private int canvasSortOrder = 25;
    [SerializeField] private float toolbarIconHeight = 34f;
    [SerializeField] private bool startVisible = true;
    [SerializeField] private bool toolbarAtBottom;

    private GameObject _canvasRoot;
    private GameObject _optionsOverlayRoot;
    private GameObject _optionsCreditsRoot;
    private GameObject _controlSchemeOverlayRoot;
    private GameObject _mainToolbarBar;
    private GameObject _simToolbarBar;
    private GameObject _drawToolbarBar;
    private Button _drawModeToolbarButton;
    private Toggle _soundEffectsMuteToggle;
    private RawImage _simPauseButtonIcon;
    private Texture2D _texPause;
    private Texture2D _texResume;
    private SandboxSimulationController _simulation;
    private Text _sessionModeLabelText;
    private MRSettingsController _mrSettingsController;
    private MRSettingsUI _mrSettingsUI;

    private void Start()
    {
        SandboxEditorModeState.ResetToEditForPlaySession();

        EnsureEventSystem();
        if (drawerController == null)
            drawerController = FindFirstObjectByType<XRContentDrawerController>();
        if (transformGizmo == null)
            transformGizmo = FindFirstObjectByType<PlaceableTransformGizmo>();
        if (drawerHints == null)
            drawerHints = FindFirstObjectByType<SandboxDrawerHints>();

        EnsureMRSettingsController();

        _simulation = GetComponent<SandboxSimulationController>();
        if (_simulation == null)
            _simulation = gameObject.AddComponent<SandboxSimulationController>();
        _simulation.Configure(drawerController, transformGizmo);
        _simulation.StateChanged += OnSimulationStateChanged;

        BuildUi();
        BuildOptionsOverlay();
        BuildControlSchemeOverlay();
        ApplyToolbarVisualPriority();
        _simulation.RefreshAllPlaceablesGravity();

        SandboxEditorModeState.ModeChanged += OnSandboxSessionModeChanged;
        RefreshSessionModeLabel();
        RefreshShellBarsVisibility();
        ApplyDrawModeInteractionPolicy();
        SetToolbarVisible(startVisible);
    }

    private void OnDestroy()
    {
        SandboxEditorModeState.ModeChanged -= OnSandboxSessionModeChanged;

        if (_simulation != null)
            _simulation.StateChanged -= OnSimulationStateChanged;

        if (_mrSettingsController != null)
        {
            _mrSettingsController.StateChanged -= OnMRSettingsStateChanged;
            _mrSettingsController.RequestOpenSettings -= OnMRSettingsOpenRequested;
        }

        if (_canvasRoot != null)
            Destroy(_canvasRoot);
    }

    private void OnSandboxSessionModeChanged(SandboxEditorSessionMode _)
    {
        RefreshSessionModeLabel();
        RefreshShellBarsVisibility();
        ApplyDrawModeInteractionPolicy();
    }

    private void RefreshSessionModeLabel()
    {
        if (_sessionModeLabelText == null)
            return;

        var mode = SandboxEditorModeState.Current;
        _sessionModeLabelText.text = "Mode: " + SandboxEditorModeState.GetDisplayLabel(mode);
    }

    private static void EnsureEventSystem()
    {
        if (UnityEngine.Object.FindFirstObjectByType<EventSystem>() != null)
            return;

        var esGo = new GameObject("EventSystem");
        esGo.AddComponent<EventSystem>();
        esGo.AddComponent<InputSystemUIInputModule>();
    }

    private void EnsureMRSettingsController()
    {
        if (_mrSettingsController == null)
            _mrSettingsController = FindFirstObjectByType<MRSettingsController>(FindObjectsInactive.Include);
        if (_mrSettingsController == null)
            _mrSettingsController = gameObject.AddComponent<MRSettingsController>();

        _mrSettingsController.StateChanged -= OnMRSettingsStateChanged;
        _mrSettingsController.StateChanged += OnMRSettingsStateChanged;
        _mrSettingsController.RequestOpenSettings -= OnMRSettingsOpenRequested;
        _mrSettingsController.RequestOpenSettings += OnMRSettingsOpenRequested;
    }

    private void BuildUi()
    {
        var canvasGo = new GameObject("SandboxEditorToolbarCanvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.overrideSorting = true;
        canvas.sortingOrder = Mathf.Max(canvasSortOrder, MinimumOverlaySortingOrder);
        canvasGo.AddComponent<GraphicRaycaster>();

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        var canvasRect = canvasGo.GetComponent<RectTransform>();
        canvasRect.anchorMin = Vector2.zero;
        canvasRect.anchorMax = Vector2.one;
        canvasRect.offsetMin = Vector2.zero;
        canvasRect.offsetMax = Vector2.zero;

        if (!useHeadsetAnchoredCanvas)
        {
            canvasGo.transform.SetParent(transform, false);
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
        }
        else
        {
            var anchor = headsetPanelAnchor != null ? headsetPanelAnchor : Camera.main != null ? Camera.main.transform : transform;
            canvasGo.transform.SetParent(anchor, false);
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = headsetPanelCamera != null
                ? headsetPanelCamera
                : (anchor.TryGetComponent<Camera>(out var c) ? c : Camera.main);

            canvasRect.sizeDelta = headsetPanelCanvasSize;
            canvasRect.localPosition = headsetPanelLocalPosition;
            canvasRect.localRotation = Quaternion.Euler(headsetPanelLocalEuler);
            canvasRect.localScale = Vector3.one * headsetPanelWorldScale;

            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.dynamicPixelsPerUnit = 10f;
        }

        _canvasRoot = canvasGo;

        var tooltipHostGo = new GameObject("ToolbarTooltipHost");
        tooltipHostGo.transform.SetParent(canvasGo.transform, false);
        var tooltipHost = tooltipHostGo.AddComponent<SandboxEditorToolbarTooltipHost>();
        tooltipHost.Setup(canvasRect, canvas);

        var bar = new GameObject("ToolbarBar");
        _mainToolbarBar = bar;
        bar.transform.SetParent(canvasGo.transform, false);
        var barRt = bar.AddComponent<RectTransform>();
        ConfigureToolbarBarRect(barRt, 0);

        var barBg = bar.AddComponent<Image>();
        barBg.color = new Color32(0x00, 0x00, 0x00, 0xE0);

        var row = new GameObject("ToolbarRow");
        row.transform.SetParent(bar.transform, false);
        var rowRt = row.AddComponent<RectTransform>();
        rowRt.anchorMin = Vector2.zero;
        rowRt.anchorMax = Vector2.one;
        rowRt.offsetMin = new Vector2(8f, 4f);
        rowRt.offsetMax = new Vector2(-8f, -4f);

        var hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.spacing = 6f;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = true;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;

        AddSessionModeLabel(row.transform);

        // Icon files live under Assets/UI (icon_*.png); copies under Resources/UI load in players.
        var slots = new (string slotId, string iconFileBase, bool wired, UnityEngine.Events.UnityAction onClick, string tooltip)[]
        {
            ("Home", "icon_Home", true, OnHomeClicked, "Go to main menu"),
            ("Draw", "icon_Draw", true, OnDrawClicked,
                "Draw mode — stylus strokes apply to nearby cubes/spheres: flick → impulse, straight line → spring"),
            ("Options", "icon_Build", true, OnOptionsClicked, "Options"),
            ("Simulate", "icon_Simulate", true, OnSimulateClicked,
                "Simulate — run physics with each object’s gravity; exit restores layout from when you started"),
            ("Drawer", "icon_ContentDrawer", true, OnDrawerClicked, "Open or close the content drawer"),
            ("Clear", "icon_Trash", true, OnClearSceneClicked, "Remove all placed objects from the scene"),
            ("Help", "icon_Help", true, OnHelpClicked, "Show or hide keyboard / control hints"),
        };

        for (var i = 0; i < slots.Length; i++)
        {
            var (slotId, iconFileBase, wired, onClick, tooltip) = slots[i];
            AddSlotButton(row.transform, slotId, iconFileBase, wired, onClick, tooltipHost, tooltip);
        }

        var drawSlot = row.transform.Find("DrawSlot");
        if (drawSlot != null)
            _drawModeToolbarButton = drawSlot.GetComponent<Button>();

        BuildSimToolbar(canvasGo.transform, tooltipHost);
        BuildDrawToolbar(canvasGo.transform, tooltipHost);
    }

    private void AddSessionModeLabel(Transform row)
    {
        var go = new GameObject("SessionModeLabel");
        go.transform.SetParent(row, false);
        go.transform.SetAsFirstSibling();

        var le = go.AddComponent<LayoutElement>();
        le.flexibleWidth = 0f;
        le.preferredWidth = 132f;
        le.minWidth = 98f;

        var t = go.AddComponent<Text>();
        MrBlueprintUiFont.Apply(t);
        t.fontSize = 15;
        t.color = new Color(0.9f, 0.91f, 0.94f, 1f);
        t.alignment = TextAnchor.MiddleLeft;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Truncate;
        _sessionModeLabelText = t;

        var rt = t.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(4f, 0f);
        rt.offsetMax = Vector2.zero;
    }

    private static Texture2D TryLoadToolbarIcon(string iconFileBase)
    {
        if (string.IsNullOrEmpty(iconFileBase))
            return null;

#if UNITY_EDITOR
        return UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>($"Assets/UI/{iconFileBase}.png");
#else
        return Resources.Load<Texture2D>($"UI/{iconFileBase}");
#endif
    }

    private static Texture2D TryLoadUiTexture(string fileBase)
    {
        if (string.IsNullOrEmpty(fileBase))
            return null;

#if UNITY_EDITOR
        return UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>($"Assets/UI/{fileBase}.png");
#else
        var tex = Resources.Load<Texture2D>($"UI/{fileBase}");
        if (tex != null)
            return tex;
        return Resources.Load<Texture2D>(fileBase);
#endif
    }

    private void BuildSimToolbar(Transform canvasTransform, SandboxEditorToolbarTooltipHost tooltipHost)
    {
        _texPause = TryLoadToolbarIcon("icon_Pause");
        _texResume = TryLoadToolbarIcon("icon_Simulate");

        _simToolbarBar = new GameObject("SimToolbarBar");
        _simToolbarBar.transform.SetParent(canvasTransform, false);
        var simBarRt = _simToolbarBar.AddComponent<RectTransform>();
        ConfigureToolbarBarRect(simBarRt, toolbarAtBottom ? 0 : 1);

        var simBarBg = _simToolbarBar.AddComponent<Image>();
        simBarBg.color = new Color32(0x00, 0x00, 0x00, 0xE0);

        var row = new GameObject("SimToolbarRow");
        row.transform.SetParent(_simToolbarBar.transform, false);
        var rowRt = row.AddComponent<RectTransform>();
        rowRt.anchorMin = Vector2.zero;
        rowRt.anchorMax = Vector2.one;
        rowRt.offsetMin = new Vector2(8f, 4f);
        rowRt.offsetMax = new Vector2(-8f, -4f);

        var hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.spacing = 8f;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = true;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;

        AddSlotButton(row.transform, "SimExit", "icon_Stop", true, OnExitSimulationClicked, tooltipHost,
            "Exit simulation — restore poses from when you started");
        AddSlotButton(row.transform, "SimPause", "icon_Pause", true, OnTogglePauseClicked, tooltipHost,
            "Pause or resume physics stepping");

        var pauseSlot = row.transform.Find("SimPauseSlot");
        if (pauseSlot != null)
        {
            var iconTr = pauseSlot.Find("Icon");
            if (iconTr != null)
                _simPauseButtonIcon = iconTr.GetComponent<RawImage>();
        }

        if (_simPauseButtonIcon != null && _texPause != null)
            _simPauseButtonIcon.texture = _texPause;

        AddSlotButton(row.transform, "SimRestart", "icon_Restart", true, OnRestartSimulationClicked, tooltipHost,
            "Restart — rewind to sim start and keep running");

        _simToolbarBar.SetActive(false);
    }

    private void BuildDrawToolbar(Transform canvasTransform, SandboxEditorToolbarTooltipHost tooltipHost)
    {
        _drawToolbarBar = new GameObject("DrawToolbarBar");
        _drawToolbarBar.transform.SetParent(canvasTransform, false);
        var drawBarRt = _drawToolbarBar.AddComponent<RectTransform>();
        ConfigureToolbarBarRect(drawBarRt, 0);

        var drawBarBg = _drawToolbarBar.AddComponent<Image>();
        drawBarBg.color = new Color32(0x00, 0x00, 0x00, 0xE0);

        var row = new GameObject("DrawToolbarRow");
        row.transform.SetParent(_drawToolbarBar.transform, false);
        var rowRt = row.AddComponent<RectTransform>();
        rowRt.anchorMin = Vector2.zero;
        rowRt.anchorMax = Vector2.one;
        rowRt.offsetMin = new Vector2(8f, 4f);
        rowRt.offsetMax = new Vector2(-8f, -4f);

        var hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.spacing = 8f;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = true;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;

        AddSlotButton(row.transform, "Edit", "icon_Door", true, OnExitDrawClicked, tooltipHost,
            "Return to edit mode — place objects, drawer, simulate");
        AddSlotButton(row.transform, "DrawUndoLast", "icon_Scissors", true, OnDrawUndoLastStrokeClicked, tooltipHost,
            "Remove last stroke (same as stylus line undo when no line is selected)");
        AddSlotButton(row.transform, "DrawClearAll", "icon_Trash", true, OnDrawClearAllStrokesClicked, tooltipHost,
            "Clear all drawn strokes");

        _drawToolbarBar.SetActive(false);
    }

    private void ConfigureToolbarBarRect(RectTransform rect, int rowOffsetFromEdge)
    {
        if (rect == null)
            return;

        if (toolbarAtBottom)
        {
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, Mathf.Max(0, rowOffsetFromEdge) * barHeight);
        }
        else
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -Mathf.Max(0, rowOffsetFromEdge) * barHeight);
        }

        rect.sizeDelta = new Vector2(0f, barHeight);
    }

    private void AddSlotButton(Transform parent, string slotId, string iconFileBase, bool wired,
        UnityEngine.Events.UnityAction onClick, SandboxEditorToolbarTooltipHost tooltipHost, string tooltip)
    {
        var go = new GameObject(slotId + "Slot");
        go.transform.SetParent(parent, false);
        var side = Mathf.Max(36f, barHeight - 8f);
        var le = go.AddComponent<LayoutElement>();
        le.flexibleWidth = 0f;
        le.minWidth = side;
        le.preferredWidth = side;
        le.minHeight = side;
        le.preferredHeight = side;

        var img = go.AddComponent<Image>();
        img.color = wired
            ? new Color32(0x11, 0x1F, 0x2B, 0xF2)
            : new Color32(0x11, 0x1F, 0x2B, 0xA6);

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.interactable = true;

        if (wired && onClick != null)
            btn.onClick.AddListener(onClick);

        var tex = TryLoadToolbarIcon(iconFileBase);
        if (tex != null)
        {
            var iconGo = new GameObject("Icon");
            iconGo.transform.SetParent(go.transform, false);
            var raw = iconGo.AddComponent<RawImage>();
            raw.texture = tex;
            raw.raycastTarget = false;
            raw.color = wired ? Color.white : new Color(1f, 1f, 1f, 0.42f);

            var irt = iconGo.GetComponent<RectTransform>();
            irt.anchorMin = new Vector2(0.5f, 0.5f);
            irt.anchorMax = new Vector2(0.5f, 0.5f);
            irt.pivot = new Vector2(0.5f, 0.5f);
            var ih = Mathf.Max(8f, toolbarIconHeight);
            var iw = ih * (tex.width / Mathf.Max(1f, (float)tex.height));
            irt.sizeDelta = new Vector2(iw, ih);
            irt.anchoredPosition = Vector2.zero;
        }
        else
        {
            var txGo = new GameObject("LabelFallback");
            txGo.transform.SetParent(go.transform, false);
            var t = txGo.AddComponent<Text>();
            MrBlueprintUiFont.Apply(t);
            t.text = slotId;
            t.fontSize = wired ? 13 : 12;
            t.color = wired ? Color.white : new Color(1f, 1f, 1f, 0.45f);
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Truncate;

            var trt = txGo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(2f, 0f);
            trt.offsetMax = new Vector2(-2f, 0f);
        }

        if (tooltipHost != null)
        {
            var trig = go.AddComponent<SandboxEditorToolbarTooltipTrigger>();
            trig.Initialize(tooltipHost, go.GetComponent<RectTransform>(), tooltip);
        }
    }

    private void BuildOptionsOverlay()
    {
        if (_canvasRoot == null)
            return;

        var font = MrBlueprintUiFont.GetDefault();

        _optionsOverlayRoot = new GameObject("OptionsOverlay");
        _optionsOverlayRoot.transform.SetParent(_canvasRoot.transform, false);
        var ort = _optionsOverlayRoot.AddComponent<RectTransform>();
        ort.anchorMin = Vector2.zero;
        ort.anchorMax = Vector2.one;
        ort.offsetMin = Vector2.zero;
        ort.offsetMax = Vector2.zero;

        var dim = _optionsOverlayRoot.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.55f);

        var panel = new GameObject("OptionsPanel");
        panel.transform.SetParent(_optionsOverlayRoot.transform, false);
        var prt = panel.AddComponent<RectTransform>();
        prt.anchorMin = new Vector2(0.5f, 0.5f);
        prt.anchorMax = new Vector2(0.5f, 0.5f);
        prt.pivot = new Vector2(0.5f, 0.5f);
        prt.anchoredPosition = Vector2.zero;
        prt.sizeDelta = new Vector2(460f, 410f);

        var pbg = panel.AddComponent<Image>();
        pbg.color = OptionsPanelBackground;
        pbg.raycastTarget = true;

        var titleGo = new GameObject("Title");
        titleGo.transform.SetParent(panel.transform, false);
        var titleRt = titleGo.AddComponent<RectTransform>();
        titleRt.anchorMin = new Vector2(0.5f, 0.5f);
        titleRt.anchorMax = new Vector2(0.5f, 0.5f);
        titleRt.pivot = new Vector2(0.5f, 0.5f);
        titleRt.anchoredPosition = new Vector2(0f, 166f);
        titleRt.sizeDelta = new Vector2(300f, 34f);
        var title = titleGo.AddComponent<Text>();
        MrBlueprintUiFont.Apply(title, font);
        title.text = "Options";
        title.fontSize = 20;
        title.color = OptionsTextPrimary;
        title.alignment = TextAnchor.MiddleLeft;
        title.raycastTarget = false;

        CreateOptionsAccentRule(panel.transform, new Vector2(0f, 132f), 300f);

        _soundEffectsMuteToggle = CreateOptionsMuteToggle(panel.transform, font, new Vector2(0f, 102f));
        _soundEffectsMuteToggle.SetIsOnWithoutNotify(UiMenuSelectSoundHub.SoundEffectsMuted);

        HomeMenuController.CreateMenuButton(
            panel.transform,
            font,
            "MR Settings",
            new Vector2(0f, 51f),
            new Vector2(280f, 42f),
            ShowMRSettings);
        HomeMenuController.CreateMenuButton(
            panel.transform,
            font,
            "Credits",
            new Vector2(0f, 0f),
            new Vector2(280f, 42f),
            ShowOptionsCredits);
        HomeMenuController.CreateMenuButton(
            panel.transform,
            font,
            "Exit to Home Menu",
            new Vector2(0f, -51f),
            new Vector2(280f, 42f),
            OnHomeClicked);
        HomeMenuController.CreateMenuButton(
            panel.transform,
            font,
            "Quit App",
            new Vector2(0f, -102f),
            new Vector2(280f, 42f),
            OnQuitAppClicked);
        HomeMenuController.CreateMenuButton(
            panel.transform,
            font,
            "Close",
            new Vector2(0f, -164f),
            new Vector2(170f, 36f),
            () => SetOptionsVisible(false));

        MXInkStatusPill.Create(
            panel.transform,
            font,
            "MXInkOptionsStatus",
            new Vector2(1f, 1f),
            new Vector2(1f, 1f),
            new Vector2(1f, 1f),
            new Vector2(-16f, -14f),
            new Vector2(178f, 26f),
            13,
            7f);

        _optionsCreditsRoot = HomeMenuController.BuildCreditsPanel(
            _optionsOverlayRoot.transform,
            font,
            HideOptionsCredits);
        _optionsCreditsRoot.SetActive(false);

        _mrSettingsUI = _optionsOverlayRoot.AddComponent<MRSettingsUI>();
        _mrSettingsUI.Build(_optionsOverlayRoot.transform, font, _mrSettingsController, HideMRSettings,
            () => SetOptionsVisible(false));

        _optionsOverlayRoot.SetActive(false);
    }

    private Toggle CreateOptionsMuteToggle(Transform parent, Font font, Vector2 anchoredPosition)
    {
        var row = new GameObject("MuteRow");
        row.transform.SetParent(parent, false);
        var rowRt = row.AddComponent<RectTransform>();
        rowRt.anchorMin = new Vector2(0.5f, 0.5f);
        rowRt.anchorMax = new Vector2(0.5f, 0.5f);
        rowRt.pivot = new Vector2(0.5f, 0.5f);
        rowRt.anchoredPosition = anchoredPosition;
        rowRt.sizeDelta = new Vector2(280f, 34f);

        var label = CreateOptionsText(row.transform, "Label", "Mute", font, 16, TextAnchor.MiddleLeft);
        label.color = OptionsTextSecondary;
        var labelRt = label.GetComponent<RectTransform>();
        labelRt.anchorMin = new Vector2(0f, 0f);
        labelRt.anchorMax = new Vector2(0.7f, 1f);
        labelRt.offsetMin = Vector2.zero;
        labelRt.offsetMax = Vector2.zero;

        var toggleGo = new GameObject("MuteToggle");
        toggleGo.transform.SetParent(row.transform, false);
        var toggleRt = toggleGo.AddComponent<RectTransform>();
        toggleRt.anchorMin = new Vector2(1f, 0.5f);
        toggleRt.anchorMax = new Vector2(1f, 0.5f);
        toggleRt.pivot = new Vector2(0.5f, 0.5f);
        toggleRt.anchoredPosition = new Vector2(-18f, 0f);
        toggleRt.sizeDelta = new Vector2(28f, 28f);

        var toggleBg = toggleGo.AddComponent<Image>();
        toggleBg.color = OptionsTrackBackground;
        var toggle = toggleGo.AddComponent<Toggle>();
        toggle.targetGraphic = toggleBg;
        toggle.graphic = CreateOptionsToggleGraphic(toggleGo.transform);
        toggle.onValueChanged.AddListener(OnSoundEffectsMuteChanged);
        return toggle;
    }

    private static Text CreateOptionsText(Transform parent, string name, string text, Font font, int fontSize,
        TextAnchor alignment)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<Text>();
        MrBlueprintUiFont.Apply(t, font);
        t.text = text;
        t.fontSize = fontSize;
        t.color = OptionsTextPrimary;
        t.alignment = alignment;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Truncate;
        t.raycastTarget = false;
        return t;
    }

    private static Graphic CreateOptionsToggleGraphic(Transform parent)
    {
        var go = new GameObject("Checkmark");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(4f, 4f);
        rt.offsetMax = new Vector2(-4f, -4f);
        var image = go.AddComponent<Image>();
        image.color = OptionsPanelAccent;
        return image;
    }

    private static void CreateOptionsAccentRule(Transform parent, Vector2 anchoredPosition, float width)
    {
        var go = new GameObject("AccentRule");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPosition;
        rt.sizeDelta = new Vector2(width, 3f);
        var image = go.AddComponent<Image>();
        image.color = OptionsPanelAccent;
        image.raycastTarget = false;
    }

    private void BuildControlSchemeOverlay()
    {
        if (_canvasRoot == null)
            return;

        _controlSchemeOverlayRoot = new GameObject("ControlSchemeOverlay");
        _controlSchemeOverlayRoot.transform.SetParent(_canvasRoot.transform, false);
        var ort = _controlSchemeOverlayRoot.AddComponent<RectTransform>();
        ort.anchorMin = Vector2.zero;
        ort.anchorMax = Vector2.one;
        ort.offsetMin = Vector2.zero;
        ort.offsetMax = Vector2.zero;

        var dim = _controlSchemeOverlayRoot.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.6f);

        var panel = new GameObject("ControlSchemePanel");
        panel.transform.SetParent(_controlSchemeOverlayRoot.transform, false);
        var prt = panel.AddComponent<RectTransform>();
        prt.anchorMin = new Vector2(0.5f, 0.5f);
        prt.anchorMax = new Vector2(0.5f, 0.5f);
        prt.pivot = new Vector2(0.5f, 0.5f);
        prt.anchoredPosition = Vector2.zero;
        prt.sizeDelta = new Vector2(900f, 520f);

        var pbg = panel.AddComponent<Image>();
        pbg.color = new Color(0.05f, 0.07f, 0.1f, 0.98f);

        var imgGo = new GameObject("ControlSchemeImage");
        imgGo.transform.SetParent(panel.transform, false);
        var imgRt = imgGo.AddComponent<RectTransform>();
        imgRt.anchorMin = new Vector2(0.5f, 0.5f);
        imgRt.anchorMax = new Vector2(0.5f, 0.5f);
        imgRt.pivot = new Vector2(0.5f, 0.5f);
        imgRt.anchoredPosition = new Vector2(0f, 8f);
        imgRt.sizeDelta = new Vector2(860f, 470f);

        var raw = imgGo.AddComponent<RawImage>();
        raw.texture = TryLoadUiTexture("control-scheme");
        raw.color = Color.white;
        raw.raycastTarget = false;

        var closeGo = new GameObject("Close");
        closeGo.transform.SetParent(panel.transform, false);
        var closeRt = closeGo.AddComponent<RectTransform>();
        closeRt.anchorMin = new Vector2(0.5f, 0f);
        closeRt.anchorMax = new Vector2(0.5f, 0f);
        closeRt.pivot = new Vector2(0.5f, 0f);
        closeRt.anchoredPosition = new Vector2(0f, 14f);
        closeRt.sizeDelta = new Vector2(180f, 34f);
        var closeImg = closeGo.AddComponent<Image>();
        closeImg.color = new Color(0.2f, 0.24f, 0.3f, 1f);
        var closeBtn = closeGo.AddComponent<Button>();
        closeBtn.targetGraphic = closeImg;
        closeBtn.onClick.AddListener(() => SetControlSchemeVisible(false));

        var closeTxGo = new GameObject("Text");
        closeTxGo.transform.SetParent(closeGo.transform, false);
        var closeTx = closeTxGo.AddComponent<Text>();
        MrBlueprintUiFont.Apply(closeTx);
        closeTx.text = "Close";
        closeTx.fontSize = 14;
        closeTx.color = Color.white;
        closeTx.alignment = TextAnchor.MiddleCenter;
        var closeTxRt = closeTxGo.GetComponent<RectTransform>();
        closeTxRt.anchorMin = Vector2.zero;
        closeTxRt.anchorMax = Vector2.one;
        closeTxRt.offsetMin = Vector2.zero;
        closeTxRt.offsetMax = Vector2.zero;

        _controlSchemeOverlayRoot.SetActive(false);
    }

    private void ApplyToolbarVisualPriority()
    {
        if (_canvasRoot == null)
        {
            return;
        }

        var canvas = _canvasRoot.GetComponent<Canvas>();
        if (canvas != null)
        {
            canvas.overrideSorting = true;
            canvas.sortingOrder = Mathf.Max(canvas.sortingOrder, MinimumOverlaySortingOrder);
        }

        var graphics = _canvasRoot.GetComponentsInChildren<Graphic>(true);
        for (var i = 0; i < graphics.Length; i++)
        {
            PhysicsLensRenderUtility.ApplyUiPerspectiveOverlayMaterial(graphics[i]);
        }
    }

    private void OnSimulationStateChanged()
    {
        RefreshShellBarsVisibility();
        ApplyDrawModeInteractionPolicy();
    }

    private void RefreshShellBarsVisibility()
    {
        var sim = _simulation != null && _simulation.IsSimulating;
        var draw = SandboxEditorModeState.Current == SandboxEditorSessionMode.Draw;

        if (_mainToolbarBar != null)
            _mainToolbarBar.SetActive(!sim && !draw);

        if (_simToolbarBar != null)
            _simToolbarBar.SetActive(sim);

        if (_drawToolbarBar != null)
            _drawToolbarBar.SetActive(!sim && draw);

        if (_drawModeToolbarButton != null)
            _drawModeToolbarButton.interactable = !sim;

        UpdatePauseButtonIcon();
    }

    private void ApplyDrawModeInteractionPolicy()
    {
        if (transformGizmo == null)
            transformGizmo = FindFirstObjectByType<PlaceableTransformGizmo>();

        if (transformGizmo == null)
            return;

        if (SandboxEditorModeState.Current == SandboxEditorSessionMode.Draw)
        {
            transformGizmo.enabled = false;
            return;
        }

        if (_simulation == null || !_simulation.IsSimulating)
            transformGizmo.enabled = true;
    }

    private void OnDrawClicked()
    {
        if (_simulation != null && _simulation.IsSimulating)
            return;

        SetOptionsVisible(false);

        if (drawerController != null && drawerController.IsOpen)
            drawerController.CloseDrawer();

        if (transformGizmo != null && transformGizmo.IsDragging)
            transformGizmo.EndDrag();

        SandboxEditorModeState.SetSessionMode(SandboxEditorSessionMode.Draw);
        UiMenuSelectSoundHub.TryPlayFromInteraction();
    }

    private void OnExitDrawClicked()
    {
        SandboxEditorModeState.SetSessionMode(SandboxEditorSessionMode.Edit);
        UiMenuSelectSoundHub.TryPlayFromInteraction();
    }

    private static LineDrawing FindLineDrawingOrNull() =>
        UnityEngine.Object.FindFirstObjectByType<LineDrawing>(FindObjectsInactive.Include);

    private void OnDrawUndoLastStrokeClicked()
    {
        if (SandboxEditorModeState.Current != SandboxEditorSessionMode.Draw)
            return;

        UiMenuSelectSoundHub.SuppressDefaultButtonSound();
        DrawStrokeBridge.TryRemoveLastStroke(FindLineDrawingOrNull());
        UiMenuSelectSoundHub.TryPlayScissorCut();
    }

    private void OnDrawClearAllStrokesClicked()
    {
        if (SandboxEditorModeState.Current != SandboxEditorSessionMode.Draw)
            return;

        DrawStrokeBridge.TryClearAllStrokes(FindLineDrawingOrNull());
        UiMenuSelectSoundHub.TryPlayFromInteraction();
    }

    private void UpdatePauseButtonIcon()
    {
        if (_simPauseButtonIcon == null || _simulation == null)
            return;

        var tex = _simulation.IsPaused ? (_texResume != null ? _texResume : _texPause) : _texPause;
        if (tex != null)
            _simPauseButtonIcon.texture = tex;
        _simPauseButtonIcon.color = Color.white;
    }

    private void OnSimulateClicked()
    {
        _simulation?.EnterSimulation();
        UiMenuSelectSoundHub.TryPlayFromInteraction();
    }

    private void OnExitSimulationClicked()
    {
        _simulation?.ExitSimulation();
        UiMenuSelectSoundHub.TryPlayFromInteraction();
    }

    private void OnTogglePauseClicked()
    {
        _simulation?.TogglePause();
        UpdatePauseButtonIcon();
        UiMenuSelectSoundHub.TryPlayFromInteraction();
    }

    private void OnRestartSimulationClicked()
    {
        _simulation?.RestartSimulation();
        UpdatePauseButtonIcon();
        UiMenuSelectSoundHub.TryPlayFromInteraction();
    }

    private void SetOptionsVisible(bool visible)
    {
        if (_optionsOverlayRoot != null)
        {
            if (visible)
                RefreshOptionsControls();
            else
            {
                SetOptionsCreditsVisible(false);
                if (_mrSettingsUI != null)
                    _mrSettingsUI.SetVisible(false);
            }

            _optionsOverlayRoot.SetActive(visible);
        }
    }

    private void RefreshOptionsControls()
    {
        if (_soundEffectsMuteToggle != null)
            _soundEffectsMuteToggle.SetIsOnWithoutNotify(UiMenuSelectSoundHub.SoundEffectsMuted);
        if (_mrSettingsUI != null)
            _mrSettingsUI.RefreshFromController();
    }

    private void SetOptionsCreditsVisible(bool visible)
    {
        if (_optionsCreditsRoot != null)
            _optionsCreditsRoot.SetActive(visible);
    }

    private void ShowOptionsCredits()
    {
        if (_mrSettingsUI != null)
            _mrSettingsUI.SetVisible(false);
        SetOptionsCreditsVisible(true);
    }

    private void HideOptionsCredits()
    {
        SetOptionsCreditsVisible(false);
    }

    private void ShowMRSettings()
    {
        SetOptionsCreditsVisible(false);
        if (_mrSettingsUI != null)
            _mrSettingsUI.SetVisible(true);
    }

    private void HideMRSettings()
    {
        if (_mrSettingsUI != null)
            _mrSettingsUI.SetVisible(false);
    }

    private void OnMRSettingsStateChanged()
    {
        if (_mrSettingsUI != null)
            _mrSettingsUI.RefreshFromController();
    }

    private void OnMRSettingsOpenRequested()
    {
        SetToolbarVisible(true);
        SetOptionsVisible(true);
        ShowMRSettings();
    }

    private void OnSoundEffectsMuteChanged(bool muted)
    {
        UiMenuSelectSoundHub.SetSoundEffectsMuted(muted);
    }

    private void OnQuitAppClicked()
    {
        HomeMenuController.QuitApplication();
    }

    private void SetControlSchemeVisible(bool visible)
    {
        if (_controlSchemeOverlayRoot != null)
            _controlSchemeOverlayRoot.SetActive(visible);
    }

    public bool IsToolbarVisible => _canvasRoot != null && _canvasRoot.activeSelf;

    public void SetToolbarVisible(bool visible)
    {
        if (_canvasRoot == null)
            return;

        if (!visible)
            SetOptionsVisible(false);

        if (visible)
        {
            RefreshSessionModeLabel();
            RefreshShellBarsVisibility();
        }

        _canvasRoot.SetActive(visible);
    }

    public void ToggleToolbarVisible()
    {
        if (IsToolbarVisible && drawerController != null && drawerController.IsOpen)
            drawerController.CloseDrawer();

        SetToolbarVisible(!IsToolbarVisible);
    }

    public void ToggleSimulationShortcut()
    {
        if (_simulation == null)
        {
            _simulation = GetComponent<SandboxSimulationController>();
            if (_simulation == null)
                _simulation = gameObject.AddComponent<SandboxSimulationController>();
            _simulation.Configure(drawerController, transformGizmo);
        }

        if (_simulation.IsSimulating)
            _simulation.ExitSimulation();
        else
            _simulation.EnterSimulation();

        SetToolbarVisible(true);
        RefreshShellBarsVisibility();
        UpdatePauseButtonIcon();
    }

    public void ToggleOptionsVisible()
    {
        if (_canvasRoot != null && !_canvasRoot.activeSelf)
            SetToolbarVisible(true);

        var next = _optionsOverlayRoot == null || !_optionsOverlayRoot.activeSelf;
        SetOptionsVisible(next);
    }

    private void OnDrawerClicked()
    {
        if (drawerController != null)
            drawerController.ToggleDrawer();
    }

    private void OnClearSceneClicked()
    {
        UiMenuSelectSoundHub.SuppressDefaultButtonSound();

        if (_simulation != null && _simulation.IsSimulating)
            _simulation.ExitSimulation();

        if (drawerController != null && drawerController.IsOpen)
            drawerController.CloseDrawer();

        if (transformGizmo != null && transformGizmo.IsDragging)
            transformGizmo.EndDrag();

        var lineDrawing = FindLineDrawingOrNull();
        if (lineDrawing != null)
            DrawStrokeBridge.TryClearAllStrokes(lineDrawing);

        var placeables = FindObjectsByType<PlaceableAsset>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (var i = 0; i < placeables.Length; i++)
        {
            var p = placeables[i];
            if (p == null || p.gameObject == null)
                continue;
            if (p.GetComponent<SpawnTemplateMarker>() != null)
                continue;

            Destroy(p.gameObject);
        }

        if (AssetSelectionManager.Instance != null)
            AssetSelectionManager.Instance.ClearSelection();

        UiMenuSelectSoundHub.TryPlayClearScene();
    }

    private void OnHomeClicked()
    {
        if (string.IsNullOrEmpty(homeSceneName))
        {
            Debug.LogError("SandboxEditorToolbarFrame: home scene name is empty.");
            return;
        }

        if (_simulation != null && _simulation.IsSimulating)
            _simulation.ExitSimulation();

        SceneManager.LoadScene(homeSceneName, LoadSceneMode.Single);
    }

    private void OnHelpClicked()
    {
        var next = _controlSchemeOverlayRoot == null || !_controlSchemeOverlayRoot.activeSelf;
        SetControlSchemeVisible(next);
    }

    private void OnOptionsClicked()
    {
        ToggleOptionsVisible();
    }

    /// <summary>Phase D3 — invokes private <see cref="LineDrawing"/> helpers without editing <c>Assets/Logitech</c>.</summary>
    private static class DrawStrokeBridge
    {
        private static readonly MethodInfo RemoveLastLine = typeof(LineDrawing).GetMethod(
            "RemoveLastLine",
            BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly MethodInfo ClearAllLines = typeof(LineDrawing).GetMethod(
            "ClearAllLines",
            BindingFlags.Instance | BindingFlags.NonPublic);

        private static bool _loggedMissing;

        public static void TryRemoveLastStroke(LineDrawing drawing)
        {
            if (drawing == null || RemoveLastLine == null)
            {
                LogMissingOnce();
                return;
            }

            try
            {
                RemoveLastLine.Invoke(drawing, null);
            }
            catch (TargetInvocationException ex) when (ex.InnerException is ArgumentOutOfRangeException or IndexOutOfRangeException)
            {
            }
        }

        public static void TryClearAllStrokes(LineDrawing drawing)
        {
            if (drawing == null || ClearAllLines == null)
            {
                LogMissingOnce();
                return;
            }

            ClearAllLines.Invoke(drawing, null);
        }

        private static void LogMissingOnce()
        {
            if (_loggedMissing)
                return;
            _loggedMissing = true;
            Debug.LogWarning(
                "DrawStrokeBridge: could not resolve LineDrawing private methods (RemoveLastLine / ClearAllLines). Draw toolbar stroke actions are disabled.");
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (drawerController == null)
            drawerController = FindFirstObjectByType<XRContentDrawerController>();
    }
#endif
}
