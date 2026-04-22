using System;
using UnityEngine;
using UnityEngine.UI;

public class MRSettingsUI : MonoBehaviour
{
    private static readonly Color PanelBackground = new Color(0.035f, 0.04f, 0.048f, 0.98f);
    private static readonly Color Accent = new Color(0.22f, 0.62f, 1f, 1f);
    private static readonly Color TextPrimary = new Color(0.93f, 0.97f, 1f, 1f);
    private static readonly Color TextSecondary = new Color(0.64f, 0.77f, 0.9f, 1f);
    private static readonly Color TextDisabled = new Color(0.42f, 0.5f, 0.58f, 1f);
    private static readonly Color TrackBackground = new Color(0.12f, 0.17f, 0.22f, 0.95f);

    private MRSettingsController _controller;
    private Font _font;
    private Action _onBack;
    private Action _onClose;
    private bool _refreshing;

    private GameObject _root;
    private Text _modeText;
    private Text _statusText;
    private SettingRow _passthroughRow;
    private SettingRow _roomRow;
    private SettingRow _roomSetupRow;
    private SettingRow _blueprintRow;
    private Button _randomizeButton;
    private Image _randomizeButtonImage;
    private Text _randomizeButtonText;

    public void Build(Transform parent, Font font, MRSettingsController controller, Action onBack, Action onClose)
    {
        _font = font;
        _controller = controller;
        _onBack = onBack;
        _onClose = onClose;

        _root = new GameObject("MRSettingsPanelRoot");
        _root.transform.SetParent(parent, false);
        var rootRt = _root.AddComponent<RectTransform>();
        rootRt.anchorMin = Vector2.zero;
        rootRt.anchorMax = Vector2.one;
        rootRt.offsetMin = Vector2.zero;
        rootRt.offsetMax = Vector2.zero;

        var dim = _root.AddComponent<Image>();
        dim.color = new Color(0.01f, 0.014f, 0.02f, 0.92f);
        dim.raycastTarget = true;

        var panel = new GameObject("MRSettingsPanel");
        panel.transform.SetParent(_root.transform, false);
        var prt = panel.AddComponent<RectTransform>();
        prt.anchorMin = new Vector2(0.5f, 0.5f);
        prt.anchorMax = new Vector2(0.5f, 0.5f);
        prt.pivot = new Vector2(0.5f, 0.5f);
        prt.anchoredPosition = Vector2.zero;
        prt.sizeDelta = new Vector2(520f, 500f);

        var bg = panel.AddComponent<Image>();
        bg.color = PanelBackground;
        bg.raycastTarget = true;

        CreateTitle(panel.transform);
        CreateAccentRule(panel.transform, new Vector2(0f, 188f), 340f);

        _modeText = CreateText(panel.transform, "Mode", "", 15, TextAnchor.MiddleCenter);
        ConfigureRect(_modeText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 166f), new Vector2(420f, 26f));
        _modeText.color = TextSecondary;

        _passthroughRow = CreateSettingRow(panel.transform, "MR Passthrough", "Solid background when off.", 112f);
        _roomRow = CreateSettingRow(panel.transform, "MR Room", "Off uses Floor Only mode.", 55f);
        _roomSetupRow = CreateSettingRow(panel.transform, "Use Room Setup", "Quest room data when available.", -2f);
        _blueprintRow = CreateSettingRow(panel.transform, "Blueprint Visibility", "Shows or hides MR surface mesh visuals.", -59f);

        _passthroughRow.Toggle.onValueChanged.AddListener(value =>
        {
            if (!_refreshing)
            {
                _controller?.SetPassthroughEnabled(value);
            }
        });
        _roomRow.Toggle.onValueChanged.AddListener(value =>
        {
            if (!_refreshing)
            {
                _controller?.SetMRRoomEnabled(value);
            }
        });
        _roomSetupRow.Toggle.onValueChanged.AddListener(value =>
        {
            if (!_refreshing)
            {
                _controller?.SetUseRoomSetup(value);
            }
        });
        _blueprintRow.Toggle.onValueChanged.AddListener(value =>
        {
            if (!_refreshing)
            {
                _controller?.SetBlueprintVisible(value);
            }
        });

        _randomizeButton = HomeMenuController.CreateMenuButton(
            panel.transform,
            font,
            "Randomize Room",
            new Vector2(0f, -125f),
            new Vector2(250f, 38f),
            () => _controller?.RandomizeRoom());
        _randomizeButtonImage = _randomizeButton.GetComponent<Image>();
        _randomizeButtonText = _randomizeButton.GetComponentInChildren<Text>();

        _statusText = CreateText(panel.transform, "Status", "", 14, TextAnchor.MiddleCenter);
        ConfigureRect(_statusText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -171f), new Vector2(430f, 36f));
        _statusText.color = TextSecondary;

        HomeMenuController.CreateMenuButton(
            panel.transform,
            font,
            "Back",
            new Vector2(-86f, -221f),
            new Vector2(150f, 34f),
            () => _onBack?.Invoke());
        HomeMenuController.CreateMenuButton(
            panel.transform,
            font,
            "Close",
            new Vector2(86f, -221f),
            new Vector2(150f, 34f),
            () => _onClose?.Invoke());

        _root.SetActive(false);
        RefreshFromController();
    }

    public void SetVisible(bool visible)
    {
        if (_root == null)
        {
            return;
        }

        if (visible)
        {
            RefreshFromController();
        }

        _root.SetActive(visible);
    }

    public bool IsVisible => _root != null && _root.activeSelf;

    public void RefreshFromController()
    {
        if (_controller == null)
        {
            return;
        }

        _refreshing = true;
        var state = _controller.State;
        _passthroughRow.Toggle.SetIsOnWithoutNotify(state.PassthroughEnabled);
        _roomRow.Toggle.SetIsOnWithoutNotify(state.MRRoomEnabled);
        _roomSetupRow.Toggle.SetIsOnWithoutNotify(state.UseRoomSetup);
        _blueprintRow.Toggle.SetIsOnWithoutNotify(state.BlueprintVisible);

        _passthroughRow.SetEnabled(!_controller.IsApplying, "Solid background when off.");
        _roomRow.SetEnabled(!_controller.IsApplying, state.MRRoomEnabled ? "Full room surfaces." : "Floor Only mode.");
        _roomSetupRow.SetEnabled(_controller.UseRoomSetupControlAvailable,
            _controller.RoomSetupAvailable ? "Quest room data when available." : "Room Setup data not currently available.");
        _blueprintRow.SetEnabled(_controller.BlueprintControlAvailable,
            _controller.BlueprintControlAvailable
                ? "Shows or hides MR surface mesh visuals."
                : "Randomized room mesh stays visible.");

        var randomizeAvailable = _controller.RandomizeRoomAvailable;
        if (_randomizeButton != null)
        {
            _randomizeButton.interactable = randomizeAvailable;
        }

        if (_randomizeButtonImage != null)
        {
            _randomizeButtonImage.color = randomizeAvailable
                ? new Color(0.22f, 0.42f, 0.72f, 1f)
                : new Color(0.12f, 0.17f, 0.22f, 0.7f);
        }

        if (_randomizeButtonText != null)
        {
            _randomizeButtonText.color = randomizeAvailable ? Color.white : new Color(1f, 1f, 1f, 0.45f);
        }

        if (_modeText != null)
        {
            _modeText.text = GetModeLabel(_controller.RoomMode);
        }

        if (_statusText != null)
        {
            _statusText.text = _controller.StatusMessage;
        }

        _refreshing = false;
    }

    private void CreateTitle(Transform parent)
    {
        var title = CreateText(parent, "Title", "MR Settings", 22, TextAnchor.MiddleLeft);
        ConfigureRect(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 214f), new Vector2(340f, 34f));
        title.color = TextPrimary;
    }

    private SettingRow CreateSettingRow(Transform parent, string label, string helper, float y)
    {
        var row = new GameObject(label.Replace(" ", "") + "Row");
        row.transform.SetParent(parent, false);
        var rowRt = row.AddComponent<RectTransform>();
        rowRt.anchorMin = new Vector2(0.5f, 0.5f);
        rowRt.anchorMax = new Vector2(0.5f, 0.5f);
        rowRt.pivot = new Vector2(0.5f, 0.5f);
        rowRt.anchoredPosition = new Vector2(0f, y);
        rowRt.sizeDelta = new Vector2(390f, 50f);

        var labelText = CreateText(row.transform, "Label", label, 16, TextAnchor.MiddleLeft);
        var labelRt = labelText.rectTransform;
        labelRt.anchorMin = new Vector2(0f, 0.42f);
        labelRt.anchorMax = new Vector2(0.74f, 1f);
        labelRt.offsetMin = Vector2.zero;
        labelRt.offsetMax = Vector2.zero;
        labelText.color = TextPrimary;

        var helperText = CreateText(row.transform, "Helper", helper, 12, TextAnchor.MiddleLeft);
        var helperRt = helperText.rectTransform;
        helperRt.anchorMin = new Vector2(0f, 0f);
        helperRt.anchorMax = new Vector2(0.86f, 0.48f);
        helperRt.offsetMin = Vector2.zero;
        helperRt.offsetMax = Vector2.zero;
        helperText.color = TextSecondary;

        var toggleGo = new GameObject("Toggle");
        toggleGo.transform.SetParent(row.transform, false);
        var toggleRt = toggleGo.AddComponent<RectTransform>();
        toggleRt.anchorMin = new Vector2(1f, 0.5f);
        toggleRt.anchorMax = new Vector2(1f, 0.5f);
        toggleRt.pivot = new Vector2(0.5f, 0.5f);
        toggleRt.anchoredPosition = new Vector2(-18f, 0f);
        toggleRt.sizeDelta = new Vector2(28f, 28f);

        var toggleBg = toggleGo.AddComponent<Image>();
        toggleBg.color = TrackBackground;
        var toggle = toggleGo.AddComponent<Toggle>();
        toggle.targetGraphic = toggleBg;
        toggle.graphic = CreateToggleGraphic(toggleGo.transform);

        return new SettingRow(labelText, helperText, toggle, toggleBg);
    }

    private Text CreateText(Transform parent, string name, string text, int fontSize, TextAnchor alignment)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<Text>();
        MrBlueprintUiFont.Apply(t, _font);
        t.text = text;
        t.fontSize = fontSize;
        t.color = TextPrimary;
        t.alignment = alignment;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Truncate;
        t.raycastTarget = false;
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
        var image = go.AddComponent<Image>();
        image.color = Accent;
        return image;
    }

    private static void ConfigureRect(RectTransform rect, Vector2 anchor, Vector2 anchoredPosition, Vector2 size)
    {
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;
    }

    private static void CreateAccentRule(Transform parent, Vector2 anchoredPosition, float width)
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
        image.color = Accent;
        image.raycastTarget = false;
    }

    private static string GetModeLabel(MRSettingsRoomMode mode)
    {
        return mode switch
        {
            MRSettingsRoomMode.RoomSetup => "Real mixed reality room",
            MRSettingsRoomMode.RandomizedRoom => "Randomized room fallback",
            _ => "Floor Only mode"
        };
    }

    private readonly struct SettingRow
    {
        private readonly Text _label;
        private readonly Text _helper;
        private readonly Image _background;
        public readonly Toggle Toggle;

        public SettingRow(Text label, Text helper, Toggle toggle, Image background)
        {
            _label = label;
            _helper = helper;
            Toggle = toggle;
            _background = background;
        }

        public void SetEnabled(bool enabled, string helperText)
        {
            if (Toggle != null)
            {
                Toggle.interactable = enabled;
            }

            if (_label != null)
            {
                _label.color = enabled ? TextPrimary : TextDisabled;
            }

            if (_helper != null)
            {
                _helper.text = helperText;
                _helper.color = enabled ? TextSecondary : TextDisabled;
            }

            if (_background != null)
            {
                _background.color = enabled ? TrackBackground : new Color(0.08f, 0.1f, 0.12f, 0.65f);
            }
        }
    }
}
