using System;
using UnityEngine;
using UnityEngine.UI;

public sealed class PhysicsLensPanelController : MonoBehaviour
{
    private static readonly string[] CauseLabels = { "Gravity", "User", "Spring", "Hinge", "Impact", "Friction", "Other" };

    private PhysicsLensConfig _config;
    private Canvas _canvas;
    private CanvasGroup _canvasGroup;
    private RectTransform _panelRect;
    private RectTransform _graphRoot;
    private RectTransform _timelineRoot;
    private RectTransform _phaseRoot;
    private TimelineGraphRenderer _timeline;
    private PhaseRibbonGraphRenderer _phase;
    private LineRenderer _leader;
    private Text _titleText;
    private Text _stateBadgeText;
    private Text _massChipText;
    private Text _gravityChipText;
    private Text _pinText;
    private Text _settingsText;
    private readonly Text[] _heroLabels = new Text[3];
    private readonly Text[] _heroValues = new Text[3];
    private readonly Image[] _causeImages = new Image[CauseLabels.Length];
    private readonly Text[] _causeTexts = new Text[CauseLabels.Length];
    private Text _eventText;
    private Text _insightText;
    private GameObject _expandedRoot;
    private readonly Text[] _expandedValues = new Text[11];
    private Button _advancedButton;
    private Text _advancedButtonText;
    private GameObject _advancedRoot;
    private Text _advancedText;
    private PhysicsLensGraphMode _graphMode = PhysicsLensGraphMode.MotionTimeline;
    private bool _isExpanded;
    private bool _isPinned;
    private bool _isOpen;
    private float _targetAlpha;
    private Vector3 _smoothedPosition;
    private Quaternion _smoothedRotation = Quaternion.identity;
    private bool _hasSmoothedPose;

    public event Action PinPressed;
    public event Action SettingsPressed;

    public bool IsExpanded => _isExpanded;
    public bool IsPinned => _isPinned;
    public bool IsOpen => _isOpen;
    public bool IsSettingsOpen { get; private set; } = true;
    public Vector2 CurrentPanelSize => _config != null
        ? (_isExpanded ? _config.ExpandedPanelSize : _config.CompactPanelSize)
        : new Vector2(430f, 520f);
    public float CanvasWorldScale => _config != null ? _config.CanvasWorldScale : 0.00165f;

    public void Initialize(PhysicsLensConfig config)
    {
        _config = config;
        BuildWorldCanvas();
        BuildPanel();
        BuildLeader();
        SetExpanded(false, false);
        SetOpen(false, true);
    }

    public void SetCamera(Camera camera)
    {
        if (_canvas != null && camera != null)
            _canvas.worldCamera = camera;
    }

    public void SetOpen(bool open, bool immediate)
    {
        _isOpen = open;
        _targetAlpha = open ? 1f : 0f;

        if (open)
        {
            if (_panelRect != null)
                _panelRect.gameObject.SetActive(true);
            if (_leader != null)
                _leader.enabled = true;
            _hasSmoothedPose = false;
        }

        if (_canvasGroup != null)
        {
            if (immediate)
                _canvasGroup.alpha = _targetAlpha;
            _canvasGroup.blocksRaycasts = open;
            _canvasGroup.interactable = open;
        }

        if (!open && immediate)
        {
            if (_panelRect != null)
                _panelRect.gameObject.SetActive(false);
            if (_leader != null)
                _leader.enabled = false;
        }
    }

    public void SetExpanded(bool expanded, bool pinned)
    {
        _isExpanded = expanded;
        _isPinned = pinned;

        if (_expandedRoot != null)
            _expandedRoot.SetActive(expanded);
        if (_pinText != null)
            _pinText.text = pinned ? "Pinned" : "Pin";
        if (_advancedRoot != null && !expanded)
            _advancedRoot.SetActive(false);

        ApplyLayout();
    }

    public void SetSettingsOpen(bool open)
    {
        IsSettingsOpen = open;
        if (_settingsText != null)
        {
            _settingsText.color = open
                ? (_config != null ? _config.PanelAccent : Color.cyan)
                : (_config != null ? _config.TextPrimary : Color.white);
        }
    }

    public bool TryGetSettingsDockPose(Vector2 settingsSize, out Vector3 position, out Quaternion rotation, out float worldScale)
    {
        position = default;
        rotation = default;
        worldScale = CanvasWorldScale;

        if (!_isOpen || _panelRect == null)
        {
            return false;
        }

        var panelSize = CurrentPanelSize;
        var localX = panelSize.x * 0.5f + settingsSize.x * 0.5f + 18f;
        position = transform.TransformPoint(new Vector3(localX, 0f, 0f));
        rotation = transform.rotation;
        worldScale = CanvasWorldScale;
        return true;
    }

    public void UpdateTelemetry(PhysicsTelemetryTracker tracker)
    {
        if (tracker == null || tracker.TargetRigidbody == null)
            return;

        var sample = tracker.CurrentSample;
        var constraint = tracker.Constraint;
        var ledger = tracker.ForceLedger;
        var rb = tracker.TargetRigidbody;
        var latestImpact = tracker.CollisionCache != null ? tracker.CollisionCache.Latest : default;
        var graphMode = PhysicsInsightGenerator.ResolveGraphMode(constraint);
        SetGraphMode(graphMode);

        var asset = tracker.TargetAsset;
        _titleText.text = asset != null && !string.IsNullOrEmpty(asset.AssetDisplayName)
            ? asset.AssetDisplayName
            : rb.name;
        _stateBadgeText.text = PhysicsInsightGenerator.BuildStateBadge(rb);
        _massChipText.text = "Mass " + sample.Mass.ToString("0.##") + " kg";
        _gravityChipText.text = sample.GravityEnabled ? "Gravity On" : "Gravity Off";

        UpdateHeroMetrics(sample, constraint, graphMode);
        UpdateCauseStrip(ledger);
        _eventText.text = PhysicsInsightGenerator.BuildLastEvent(latestImpact, constraint, ledger, _config);
        _insightText.text = PhysicsInsightGenerator.BuildInsight(sample, constraint, ledger, latestImpact, _config);

        if (_isExpanded)
            UpdateExpandedMetrics(tracker, sample, constraint, latestImpact);
    }

    public void RenderGraph(PhysicsTelemetryTracker tracker)
    {
        if (!_isOpen || tracker == null)
            return;

        if (_graphMode == PhysicsLensGraphMode.MotionTimeline)
            _timeline.Render(tracker);
        else
            _phase.Render(tracker, tracker.Constraint);
    }

    public void UpdateFollow(Camera camera, Vector3 centerOfMass, Bounds bounds)
    {
        if (camera == null || _config == null)
            return;

        SetCamera(camera);

        var viewport = camera.WorldToViewportPoint(centerOfMass);
        var side = viewport.x > 0.52f ? -1f : 1f;
        var radius = Mathf.Max(bounds.extents.x, Mathf.Max(bounds.extents.y, bounds.extents.z));
        radius = Mathf.Max(radius, _config.MinObjectClearance);
        var towardCamera = camera.transform.position - centerOfMass;
        if (towardCamera.sqrMagnitude <= 0.0001f)
            towardCamera = -camera.transform.forward;
        towardCamera.Normalize();

        var targetPosition = centerOfMass
                             + camera.transform.right * side * (_config.PanelHorizontalOffset + radius)
                             + camera.transform.up * (_config.PanelVerticalOffset + radius * 0.18f)
                             + towardCamera * _config.PanelForwardOffset;

        var initialLookDirection = targetPosition - camera.transform.position;
        if (initialLookDirection.sqrMagnitude <= 0.0001f)
            initialLookDirection = camera.transform.forward;
        var initialRotation = Quaternion.LookRotation(initialLookDirection.normalized, camera.transform.up);
        var positionBlend = 1f - Mathf.Exp(-_config.PanelFollowSharpness * Time.unscaledDeltaTime);
        if (!_hasSmoothedPose)
        {
            _smoothedPosition = targetPosition;
            _smoothedRotation = initialRotation;
            _hasSmoothedPose = true;
        }
        _smoothedPosition = Vector3.Lerp(_smoothedPosition, targetPosition, positionBlend);

        var lookDirection = _smoothedPosition - camera.transform.position;
        if (lookDirection.sqrMagnitude <= 0.0001f)
            lookDirection = camera.transform.forward;
        var targetRotation = Quaternion.LookRotation(lookDirection.normalized, camera.transform.up);
        var rotationBlend = 1f - Mathf.Exp(-_config.PanelRotationSharpness * Time.unscaledDeltaTime);
        _smoothedRotation = Quaternion.Slerp(_smoothedRotation, targetRotation, rotationBlend);

        transform.SetPositionAndRotation(_smoothedPosition, _smoothedRotation);

        if (_leader != null)
        {
            _leader.enabled = _canvasGroup == null || _canvasGroup.alpha > 0.01f;
            _leader.SetPosition(0, centerOfMass);
            _leader.SetPosition(1, _smoothedPosition);
            var color = _config.PanelAccent;
            color.a = (_canvasGroup != null ? _canvasGroup.alpha : 1f) * 0.72f;
            _leader.startColor = color;
            _leader.endColor = new Color(color.r, color.g, color.b, color.a * 0.35f);
        }
    }

    private void LateUpdate()
    {
        if (_canvasGroup == null || _config == null)
            return;

        var blend = 1f - Mathf.Exp(-_config.PanelFadeSharpness * Time.unscaledDeltaTime);
        _canvasGroup.alpha = Mathf.Lerp(_canvasGroup.alpha, _targetAlpha, blend);
        if (_leader != null && _leader.enabled)
        {
            var color = _leader.startColor;
            color.a = _canvasGroup.alpha * 0.72f;
            _leader.startColor = color;
            _leader.endColor = new Color(color.r, color.g, color.b, color.a * 0.35f);
        }

        if (!_isOpen && _canvasGroup.alpha <= 0.015f)
        {
            _canvasGroup.alpha = 0f;
            if (_panelRect != null)
                _panelRect.gameObject.SetActive(false);
            if (_leader != null)
                _leader.enabled = false;
        }
    }

    private void BuildWorldCanvas()
    {
        _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.WorldSpace;
        _canvas.worldCamera = Camera.main;
        gameObject.AddComponent<GraphicRaycaster>();
        _canvasGroup = gameObject.AddComponent<CanvasGroup>();

        var rect = gameObject.GetComponent<RectTransform>();
        rect.sizeDelta = _config != null ? _config.CompactPanelSize : new Vector2(430f, 520f);
        transform.localScale = Vector3.one * (_config != null ? _config.CanvasWorldScale : 0.00165f);
    }

    private void BuildPanel()
    {
        var font = MrBlueprintUiFont.GetDefault();
        var panel = new GameObject("PhysicsLensPanel");
        panel.transform.SetParent(transform, false);
        _panelRect = panel.AddComponent<RectTransform>();
        _panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        _panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        _panelRect.pivot = new Vector2(0.5f, 0.5f);

        var background = panel.AddComponent<Image>();
        background.color = _config != null ? _config.PanelBackground : new Color(0.03f, 0.04f, 0.04f, 0.94f);
        background.raycastTarget = false;

        PhysicsLensRenderUtility.CreateImage(panel.transform, "AccentRule",
            _config != null ? _config.PanelAccent : Color.cyan, Vector2.zero, new Vector2(360f, 3f));

        _titleText = PhysicsLensRenderUtility.CreateText(panel.transform, "Title", font, 26, TextAnchor.MiddleLeft,
            _config.TextPrimary, Vector2.zero, new Vector2(260f, 36f));
        _stateBadgeText = CreateChipText(panel.transform, "StateBadge", font, 16);
        _massChipText = CreateChipText(panel.transform, "MassChip", font, 15);
        _gravityChipText = CreateChipText(panel.transform, "GravityChip", font, 15);
        CreatePinButton(panel.transform, font);
        CreateSettingsButton(panel.transform, font);

        for (var i = 0; i < 3; i++)
        {
            _heroLabels[i] = PhysicsLensRenderUtility.CreateText(panel.transform, "HeroLabel" + i, font, 14,
                TextAnchor.MiddleCenter, _config.TextSecondary, Vector2.zero, new Vector2(118f, 20f));
            _heroValues[i] = PhysicsLensRenderUtility.CreateText(panel.transform, "HeroValue" + i, font, 24,
                TextAnchor.MiddleCenter, _config.TextPrimary, Vector2.zero, new Vector2(122f, 32f));
        }

        BuildCauseStrip(panel.transform, font);

        _eventText = CreateChipText(panel.transform, "LastEvent", font, 16);
        _insightText = PhysicsLensRenderUtility.CreateText(panel.transform, "Insight", font, 18, TextAnchor.MiddleCenter,
            _config.TextPrimary, Vector2.zero, new Vector2(360f, 42f));

        BuildExpandedRoot(panel.transform, font);
        BuildGraphs(panel.transform, font);
    }

    private void BuildLeader()
    {
        var leaderGo = new GameObject("PhysicsLensLeaderLine");
        leaderGo.transform.SetParent(transform, false);
        _leader = leaderGo.AddComponent<LineRenderer>();
        _leader.positionCount = 2;
        _leader.useWorldSpace = true;
        _leader.widthMultiplier = 0.006f;
        _leader.numCapVertices = 6;
        _leader.numCornerVertices = 4;
        _leader.sharedMaterial = PhysicsLensRenderUtility.CreateTintMaterial("PhysicsLensLeaderMaterial",
            _config != null ? _config.PanelAccent : Color.cyan);
        _leader.enabled = false;
    }

    private void BuildCauseStrip(Transform parent, Font font)
    {
        for (var i = 0; i < CauseLabels.Length; i++)
        {
            var chip = new GameObject("Cause" + CauseLabels[i]);
            chip.transform.SetParent(parent, false);
            var rect = chip.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            var image = chip.AddComponent<Image>();
            image.color = _config.ChipBackground;
            image.raycastTarget = false;
            _causeImages[i] = image;

            _causeTexts[i] = PhysicsLensRenderUtility.CreateText(chip.transform, "Label", font, 12,
                TextAnchor.MiddleCenter, _config.TextPrimary, Vector2.zero, new Vector2(58f, 20f));
            _causeTexts[i].text = CauseLabels[i];
        }
    }

    private void BuildExpandedRoot(Transform parent, Font font)
    {
        _expandedRoot = new GameObject("ExpandedMetrics");
        _expandedRoot.transform.SetParent(parent, false);
        var rect = _expandedRoot.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);

        var labels = new[]
        {
            "Linear speed", "Angular speed", "Acceleration", "Momentum", "Linear energy", "Potential energy",
            "Constraints", "Top load 1", "Top load 2", "Last collision", "Break ratio"
        };

        for (var i = 0; i < labels.Length; i++)
        {
            var x = i < 6 ? -132f : 132f;
            var y = 46f - (i % 6) * 20f;
            PhysicsLensRenderUtility.CreateText(_expandedRoot.transform, "MetricLabel" + i, font, 12,
                TextAnchor.MiddleLeft, _config.TextSecondary, new Vector2(x - 72f, y), new Vector2(108f, 18f)).text = labels[i];
            _expandedValues[i] = PhysicsLensRenderUtility.CreateText(_expandedRoot.transform, "MetricValue" + i, font, 13,
                TextAnchor.MiddleRight, _config.TextPrimary, new Vector2(x + 38f, y), new Vector2(96f, 18f));
        }

        _advancedButton = CreateSmallButton(_expandedRoot.transform, "AdvancedButton", "Advanced", font, new Vector2(196f, -56f), new Vector2(88f, 22f));
        _advancedButton.onClick.AddListener(ToggleAdvanced);
        _advancedButtonText = _advancedButton.GetComponentInChildren<Text>();

        _advancedRoot = new GameObject("AdvancedTransformData");
        _advancedRoot.transform.SetParent(_expandedRoot.transform, false);
        var advancedRect = _advancedRoot.AddComponent<RectTransform>();
        advancedRect.anchorMin = new Vector2(0.5f, 0.5f);
        advancedRect.anchorMax = new Vector2(0.5f, 0.5f);
        advancedRect.pivot = new Vector2(0.5f, 0.5f);
        advancedRect.anchoredPosition = new Vector2(0f, -56f);
        advancedRect.sizeDelta = new Vector2(430f, 30f);
        _advancedText = PhysicsLensRenderUtility.CreateText(_advancedRoot.transform, "AdvancedText", font, 12,
            TextAnchor.MiddleCenter, _config.TextSecondary, Vector2.zero, new Vector2(420f, 28f));
        _advancedRoot.SetActive(false);
        _expandedRoot.SetActive(false);
    }

    private void BuildGraphs(Transform parent, Font font)
    {
        _graphRoot = new GameObject("GraphRoot").AddComponent<RectTransform>();
        _graphRoot.transform.SetParent(parent, false);
        _graphRoot.anchorMin = new Vector2(0.5f, 0.5f);
        _graphRoot.anchorMax = new Vector2(0.5f, 0.5f);
        _graphRoot.pivot = new Vector2(0.5f, 0.5f);

        var graphBackground = PhysicsLensRenderUtility.CreateImage(_graphRoot, "GraphBackground",
            new Color(0f, 0f, 0f, 0.16f), Vector2.zero, _config.CompactGraphSize);
        graphBackground.raycastTarget = false;

        _timelineRoot = new GameObject("TimelineGraph").AddComponent<RectTransform>();
        _timelineRoot.transform.SetParent(_graphRoot, false);
        _timeline = _timelineRoot.gameObject.AddComponent<TimelineGraphRenderer>();
        _timeline.Initialize(_timelineRoot, _config, font);

        _phaseRoot = new GameObject("PhaseRibbonGraph").AddComponent<RectTransform>();
        _phaseRoot.transform.SetParent(_graphRoot, false);
        _phase = _phaseRoot.gameObject.AddComponent<PhaseRibbonGraphRenderer>();
        _phase.Initialize(_phaseRoot, _config, font);

        SetGraphMode(PhysicsLensGraphMode.MotionTimeline);
    }

    private Text CreateChipText(Transform parent, string name, Font font, int fontSize)
    {
        var image = PhysicsLensRenderUtility.CreateImage(parent, name + "Background", _config.ChipBackground, Vector2.zero, new Vector2(120f, 26f));
        image.raycastTarget = false;
        return PhysicsLensRenderUtility.CreateText(image.transform, name, font, fontSize, TextAnchor.MiddleCenter,
            _config.TextPrimary, Vector2.zero, new Vector2(112f, 24f));
    }

    private void CreatePinButton(Transform parent, Font font)
    {
        var button = CreateSmallButton(parent, "PinButton", "Pin", font, Vector2.zero, new Vector2(64f, 28f));
        button.onClick.AddListener(HandlePinClicked);
        _pinText = button.GetComponentInChildren<Text>();
    }

    private void CreateSettingsButton(Transform parent, Font font)
    {
        var button = CreateSmallButton(parent, "SettingsButton", "⚙", font, Vector2.zero, new Vector2(34f, 28f));
        button.onClick.AddListener(HandleSettingsClicked);
        _settingsText = button.GetComponentInChildren<Text>();
        SetSettingsOpen(IsSettingsOpen);
    }

    private Button CreateSmallButton(Transform parent, string name, string label, Font font, Vector2 anchoredPosition, Vector2 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rect = go.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;

        var image = go.AddComponent<Image>();
        image.color = _config != null ? _config.ChipBackground : new Color(0.12f, 0.12f, 0.12f, 0.95f);
        var button = go.AddComponent<Button>();
        var colors = button.colors;
        colors.normalColor = image.color;
        colors.highlightedColor = _config != null ? _config.PanelAccent : Color.cyan;
        colors.pressedColor = _config != null ? _config.PanelAccent * 0.8f : Color.cyan;
        colors.selectedColor = colors.highlightedColor;
        colors.fadeDuration = 0.08f;
        button.colors = colors;

        PhysicsLensRenderUtility.CreateText(go.transform, "Label", font, 14, TextAnchor.MiddleCenter,
            _config != null ? _config.TextPrimary : Color.white, Vector2.zero, size).text = label;
        return button;
    }

    private void ApplyLayout()
    {
        if (_panelRect == null || _config == null)
            return;

        var size = _isExpanded ? _config.ExpandedPanelSize : _config.CompactPanelSize;
        _panelRect.sizeDelta = size;
        var canvasRect = GetComponent<RectTransform>();
        if (canvasRect != null)
            canvasRect.sizeDelta = size;

        var halfW = size.x * 0.5f;
        var halfH = size.y * 0.5f;

        SetRect(_titleText.rectTransform, new Vector2(-halfW + 28f, halfH - 34f), new Vector2(Mathf.Max(120f, size.x - 330f), 36f), new Vector2(0f, 0.5f));
        SetRect(_stateBadgeText.transform.parent as RectTransform, new Vector2(halfW - 182f, halfH - 34f), new Vector2(92f, 26f), new Vector2(0.5f, 0.5f));
        SetRect(_pinText.transform.parent as RectTransform, new Vector2(halfW - 91f, halfH - 34f), new Vector2(64f, 28f), new Vector2(0.5f, 0.5f));
        SetRect(_settingsText.transform.parent as RectTransform, new Vector2(halfW - 34f, halfH - 34f), new Vector2(34f, 28f), new Vector2(0.5f, 0.5f));

        SetRect(_massChipText.transform.parent as RectTransform, new Vector2(-halfW + 78f, halfH - 76f), new Vector2(126f, 26f), new Vector2(0.5f, 0.5f));
        SetRect(_gravityChipText.transform.parent as RectTransform, new Vector2(-halfW + 210f, halfH - 76f), new Vector2(126f, 26f), new Vector2(0.5f, 0.5f));

        for (var i = 0; i < 3; i++)
        {
            var x = Mathf.Lerp(-halfW + 78f, halfW - 78f, i / 2f);
            SetRect(_heroLabels[i].rectTransform, new Vector2(x, halfH - 122f), new Vector2(122f, 20f), new Vector2(0.5f, 0.5f));
            SetRect(_heroValues[i].rectTransform, new Vector2(x, halfH - 150f), new Vector2(128f, 34f), new Vector2(0.5f, 0.5f));
        }

        var causeY = halfH - 194f;
        var causeWidth = Mathf.Min(58f, (size.x - 56f) / CauseLabels.Length);
        for (var i = 0; i < CauseLabels.Length; i++)
        {
            var rect = _causeImages[i].transform as RectTransform;
            var x = -halfW + 28f + causeWidth * 0.5f + i * causeWidth;
            SetRect(rect, new Vector2(x, causeY), new Vector2(causeWidth - 5f, 24f), new Vector2(0.5f, 0.5f));
            SetRect(_causeTexts[i].rectTransform, Vector2.zero, new Vector2(causeWidth - 8f, 20f), new Vector2(0.5f, 0.5f));
        }

        SetRect(_eventText.transform.parent as RectTransform, new Vector2(0f, halfH - 232f), new Vector2(size.x - 56f, 28f), new Vector2(0.5f, 0.5f));
        SetRect(_eventText.rectTransform, Vector2.zero, new Vector2(size.x - 72f, 24f), new Vector2(0.5f, 0.5f));
        SetRect(_insightText.rectTransform, new Vector2(0f, halfH - 272f), new Vector2(size.x - 56f, 42f), new Vector2(0.5f, 0.5f));

        var graphSize = _isExpanded ? _config.ExpandedGraphSize : _config.CompactGraphSize;
        var graphY = -halfH + 24f + graphSize.y * 0.5f;
        SetRect(_graphRoot, new Vector2(0f, graphY), graphSize, new Vector2(0.5f, 0.5f));
        if (_graphRoot.childCount > 0)
        {
            var bg = _graphRoot.GetChild(0) as RectTransform;
            SetRect(bg, Vector2.zero, graphSize, new Vector2(0.5f, 0.5f));
        }

        _timeline.SetSize(graphSize);
        _phase.SetSize(graphSize);

        var accent = _panelRect.Find("AccentRule") as RectTransform;
        if (accent != null)
            SetRect(accent, new Vector2(0f, halfH - 56f), new Vector2(size.x - 52f, 3f), new Vector2(0.5f, 0.5f));

        if (_expandedRoot != null)
        {
            var expandedRect = _expandedRoot.transform as RectTransform;
            SetRect(expandedRect, new Vector2(0f, 0f), new Vector2(size.x - 56f, 124f), new Vector2(0.5f, 0.5f));
        }
    }

    private void SetGraphMode(PhysicsLensGraphMode mode)
    {
        if (_graphMode == mode && _timelineRoot != null && _timelineRoot.gameObject.activeSelf == (mode == PhysicsLensGraphMode.MotionTimeline))
            return;

        _graphMode = mode;
        if (_timelineRoot != null)
            _timelineRoot.gameObject.SetActive(mode == PhysicsLensGraphMode.MotionTimeline);
        if (_phaseRoot != null)
            _phaseRoot.gameObject.SetActive(mode != PhysicsLensGraphMode.MotionTimeline);
        if (_phase != null && mode != PhysicsLensGraphMode.MotionTimeline)
            _phase.SetMode(mode);
    }

    private void UpdateHeroMetrics(PhysicsLensSample sample, PhysicsLensConstraintSummary constraint, PhysicsLensGraphMode graphMode)
    {
        if (graphMode == PhysicsLensGraphMode.SpringPhaseRibbon)
        {
            SetHero(0, "Speed", PhysicsLensFormat.ShortNumber(sample.Speed, "m/s"));
            SetHero(1, "Spring Load", PhysicsLensFormat.ShortNumber(Mathf.Abs(constraint.SignedLoad), "N"));
            SetHero(2, constraint.Extension < 0f ? "Compression" : "Extension", PhysicsLensFormat.SignedDistance(constraint.Extension));
            return;
        }

        if (graphMode == PhysicsLensGraphMode.HingePhaseRibbon)
        {
            SetHero(0, "Angle", constraint.HingeAngle.ToString("0.0") + " deg");
            SetHero(1, "Torque", PhysicsLensFormat.ShortNumber(constraint.TorqueMagnitude, "N·m"));
            SetHero(2, "Limit", PhysicsInsightGenerator.BuildConstraintLimitText(constraint));
            return;
        }

        SetHero(0, "Speed", PhysicsLensFormat.ShortNumber(sample.Speed, "m/s"));
        SetHero(1, "Approx. Net Force", PhysicsLensFormat.ShortNumber(sample.ApproxNetForce, "N"));
        SetHero(2, constraint.ConnectedConstraintCount > 0 ? "Load" : "Energy",
            constraint.ConnectedConstraintCount > 0
                ? PhysicsLensFormat.ShortNumber(constraint.LoadMagnitude, "N")
                : PhysicsLensFormat.ShortNumber(sample.LinearKineticEnergy, "J"));
    }

    private void SetHero(int index, string label, string value)
    {
        _heroLabels[index].text = label;
        _heroValues[index].text = value;
    }

    private void UpdateCauseStrip(ForceLedger ledger)
    {
        if (ledger == null)
            return;

        var max = Mathf.Max(0.001f, ledger.MaxValue());
        for (var i = 0; i < _causeImages.Length; i++)
        {
            var driver = CauseIndexToDriver(i);
            var t = Mathf.Clamp01(ledger.GetValue(driver) / max);
            var color = _config.GetDriverColor(driver);
            color.a = Mathf.Lerp(0.18f, 0.86f, t);
            _causeImages[i].color = color;
            _causeTexts[i].color = t > 0.32f ? Color.black : _config.TextPrimary;
        }
    }

    private void UpdateExpandedMetrics(
        PhysicsTelemetryTracker tracker,
        PhysicsLensSample sample,
        PhysicsLensConstraintSummary constraint,
        PhysicsLensCollisionEvent latestImpact)
    {
        _expandedValues[0].text = PhysicsLensFormat.ShortNumber(sample.Speed, "m/s");
        _expandedValues[1].text = sample.AngularSpeedDeg.ToString("0.0") + " deg/s";
        _expandedValues[2].text = PhysicsLensFormat.ShortNumber(sample.Acceleration.magnitude, "m/s2");
        _expandedValues[3].text = PhysicsLensFormat.ShortNumber(sample.Momentum, "kg m/s");
        _expandedValues[4].text = PhysicsLensFormat.ShortNumber(sample.LinearKineticEnergy, "J");
        _expandedValues[5].text = PhysicsLensFormat.ShortNumber(sample.PotentialEnergy, "J");
        _expandedValues[6].text = constraint.ConnectedConstraintCount.ToString();
        _expandedValues[7].text = FormatTopLoad(constraint.TopConstraintNameA, constraint.TopConstraintLoadA);
        _expandedValues[8].text = FormatTopLoad(constraint.TopConstraintNameB, constraint.TopConstraintLoadB);
        _expandedValues[9].text = latestImpact.IsValid
            ? FormatCollisionSummary(latestImpact)
            : "None";
        _expandedValues[10].text = constraint.BreakRatio >= 0f ? (constraint.BreakRatio * 100f).ToString("0") + "%" : "n/a";

        if (_advancedRoot != null && _advancedRoot.activeSelf && tracker.TargetRigidbody != null)
        {
            var t = tracker.TargetRigidbody.transform;
            _advancedText.text = "Pos " + FormatVector(t.position)
                                 + "   Rot " + FormatVector(t.eulerAngles)
                                 + "   Scale " + FormatVector(t.lossyScale);
        }
    }

    private static string FormatTopLoad(string name, float load)
    {
        if (string.IsNullOrEmpty(name) || name == "None")
            return "None";
        return name + " " + load.ToString("0.0");
    }

    private static string FormatCollisionSummary(PhysicsLensCollisionEvent evt)
    {
        var text = evt.PartnerName + " " + evt.ImpulseMagnitude.ToString("0.0") + " N·s";
        if (evt.Restitution > 0.05f)
            text += " e " + evt.Restitution.ToString("0.00");
        return text;
    }

    private static string FormatVector(Vector3 v)
    {
        return v.x.ToString("0.00") + ", " + v.y.ToString("0.00") + ", " + v.z.ToString("0.00");
    }

    private static PhysicsLensDriver CauseIndexToDriver(int index)
    {
        switch (index)
        {
            case 0:
                return PhysicsLensDriver.Gravity;
            case 1:
                return PhysicsLensDriver.UserForce;
            case 2:
                return PhysicsLensDriver.Spring;
            case 3:
                return PhysicsLensDriver.HingeJoint;
            case 4:
                return PhysicsLensDriver.Impact;
            case 5:
                return PhysicsLensDriver.Friction;
            default:
                return PhysicsLensDriver.Other;
        }
    }

    private void HandlePinClicked()
    {
        PinPressed?.Invoke();
    }

    private void HandleSettingsClicked()
    {
        SettingsPressed?.Invoke();
    }

    private void ToggleAdvanced()
    {
        if (_advancedRoot == null)
            return;

        var next = !_advancedRoot.activeSelf;
        _advancedRoot.SetActive(next);
        if (_advancedButtonText != null)
            _advancedButtonText.text = next ? "Hide Raw" : "Advanced";
    }

    private static void SetRect(RectTransform rect, Vector2 anchoredPosition, Vector2 size, Vector2 pivot)
    {
        if (rect == null)
            return;

        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = pivot;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;
    }
}
