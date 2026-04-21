using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Shared hover label for toolbar slots (one instance per toolbar canvas).
/// </summary>
public sealed class SandboxEditorToolbarTooltipHost : MonoBehaviour
{
    private RectTransform _canvasRect;
    private Canvas _canvas;
    private RectTransform _panelRt;
    private Text _text;
    private readonly Vector3[] _corners = new Vector3[4];

    public void Setup(RectTransform canvasRect, Canvas canvas)
    {
        _canvasRect = canvasRect;
        _canvas = canvas;

        var panelGo = new GameObject("TooltipPanel");
        panelGo.transform.SetParent(transform, false);
        _panelRt = panelGo.AddComponent<RectTransform>();
        _panelRt.anchorMin = _panelRt.anchorMax = new Vector2(0.5f, 0.5f);
        _panelRt.pivot = new Vector2(0.5f, 0f);
        _panelRt.gameObject.SetActive(false);

        var bg = panelGo.AddComponent<Image>();
        bg.color = new Color(0.08f, 0.09f, 0.12f, 0.96f);
        bg.raycastTarget = false;

        var textGo = new GameObject("TooltipText");
        textGo.transform.SetParent(panelGo.transform, false);
        var trt = textGo.AddComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(10f, 8f);
        trt.offsetMax = new Vector2(-10f, -8f);

        _text = textGo.AddComponent<Text>();
        MrBlueprintUiFont.Apply(_text);
        _text.fontSize = 14;
        _text.color = new Color(0.95f, 0.95f, 0.97f, 1f);
        _text.alignment = TextAnchor.MiddleCenter;
        _text.horizontalOverflow = HorizontalWrapMode.Wrap;
        _text.verticalOverflow = VerticalWrapMode.Truncate;
        _text.raycastTarget = false;
    }

    public void Show(string message, RectTransform slotRt)
    {
        if (_panelRt == null || _text == null || slotRt == null || _canvasRect == null)
            return;

        _text.text = string.IsNullOrEmpty(message) ? " " : message;
        _panelRt.SetParent(_canvasRect, false);
        _panelRt.SetAsLastSibling();

        Canvas.ForceUpdateCanvases();
        const float padX = 20f;
        const float padY = 14f;
        var w = Mathf.Min(320f, Mathf.Max(72f, _text.preferredWidth + padX));
        var h = Mathf.Min(96f, Mathf.Max(30f, _text.preferredHeight + padY));
        _panelRt.sizeDelta = new Vector2(w, h);

        slotRt.GetWorldCorners(_corners);
        var topMid = (_corners[1] + _corners[2]) * 0.5f;

        Camera eventCam = null;
        if (_canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            eventCam = _canvas.worldCamera != null ? _canvas.worldCamera : Camera.main;

        var screen = RectTransformUtility.WorldToScreenPoint(eventCam, topMid);
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, screen, eventCam, out var local))
            return;

        _panelRt.anchorMin = _panelRt.anchorMax = new Vector2(0.5f, 0.5f);
        _panelRt.pivot = new Vector2(0.5f, 0f);
        _panelRt.anchoredPosition = local + new Vector2(0f, 10f);
        _panelRt.gameObject.SetActive(true);
    }

    public void Hide()
    {
        if (_panelRt != null)
            _panelRt.gameObject.SetActive(false);
    }
}

/// <summary>
/// Per-slot hover → <see cref="SandboxEditorToolbarTooltipHost"/>.
/// </summary>
public sealed class SandboxEditorToolbarTooltipTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private SandboxEditorToolbarTooltipHost _host;
    private RectTransform _slotRt;
    private string _message;

    public void Initialize(SandboxEditorToolbarTooltipHost host, RectTransform slotRt, string message)
    {
        _host = host;
        _slotRt = slotRt;
        _message = message ?? string.Empty;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        _host?.Show(_message, _slotRt);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        _host?.Hide();
    }
}
