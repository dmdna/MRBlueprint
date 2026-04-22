using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Shared hover label for toolbar slots (one instance per toolbar canvas).
/// </summary>
public sealed class SandboxEditorToolbarTooltipHost : MonoBehaviour
{
    private const float TooltipGap = 6f;
    private const float TooltipMargin = 8f;
    private const float TooltipMinWidth = 72f;
    private const float TooltipMaxWidth = 320f;
    private const float TooltipMinHeight = 30f;
    private const float TooltipMaxHeight = 160f;

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
        _panelRt.anchorMin = _panelRt.anchorMax = new Vector2(0.5f, 0f);
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
        _text.verticalOverflow = VerticalWrapMode.Overflow;
        _text.raycastTarget = false;
    }

    public void Show(string message, RectTransform slotRt)
    {
        if (_panelRt == null || _text == null || slotRt == null || _canvasRect == null)
            return;

        _text.text = !string.IsNullOrEmpty(message) && message.Trim().Length > 0
            ? message.Trim()
            : "Toolbar action";
        _panelRt.SetParent(_canvasRect, false);
        _panelRt.SetAsLastSibling();
        _panelRt.gameObject.SetActive(true);

        Canvas.ForceUpdateCanvases();
        const float padX = 20f;
        const float padY = 14f;
        var w = Mathf.Min(TooltipMaxWidth, Mathf.Max(TooltipMinWidth, _text.preferredWidth + padX));
        _panelRt.sizeDelta = new Vector2(w, TooltipMaxHeight);
        Canvas.ForceUpdateCanvases();
        var h = Mathf.Min(TooltipMaxHeight, Mathf.Max(TooltipMinHeight, _text.preferredHeight + padY));
        _panelRt.sizeDelta = new Vector2(w, h);

        slotRt.GetWorldCorners(_corners);
        var topMid = (_corners[1] + _corners[2]) * 0.5f;

        Camera eventCam = null;
        if (_canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            eventCam = _canvas.worldCamera != null ? _canvas.worldCamera : Camera.main;

        var topScreen = RectTransformUtility.WorldToScreenPoint(eventCam, topMid);
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, topScreen, eventCam, out var topLocal))
        {
            Hide();
            return;
        }

        var canvasBounds = _canvasRect.rect;
        var pivot = new Vector2(0.5f, 0f);
        var position = topLocal + new Vector2(0f, TooltipGap);

        if (_canvas == null || _canvas.renderMode != RenderMode.WorldSpace)
        {
            var minX = canvasBounds.xMin + TooltipMargin + (w * pivot.x);
            var maxX = canvasBounds.xMax - TooltipMargin - (w * (1f - pivot.x));
            var minY = canvasBounds.yMin + TooltipMargin + (h * pivot.y);
            var maxY = canvasBounds.yMax - TooltipMargin - (h * (1f - pivot.y));
            position = new Vector2(
                ClampPivotPosition(position.x, minX, maxX),
                ClampPivotPosition(position.y, minY, maxY));
        }

        _panelRt.anchorMin = _panelRt.anchorMax = new Vector2(0.5f, 0f);
        _panelRt.pivot = pivot;
        _panelRt.anchoredPosition = new Vector2(position.x, position.y - canvasBounds.yMin);
    }

    private static float ClampPivotPosition(float value, float min, float max)
    {
        return min <= max ? Mathf.Clamp(value, min, max) : (min + max) * 0.5f;
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
