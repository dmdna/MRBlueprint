using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Always-visible screen hints for drawer/spawn controls (editor mouse path).
/// </summary>
public class SandboxDrawerHints : MonoBehaviour
{
    [SerializeField] private bool startVisible;
    [SerializeField] private string hintText =
        "D — open / close drawer\nClick a tile (Cube / Sphere) to select\nSpace — spawn selected object (drawer must be open)";

    private GameObject _hintsCanvasRoot;

    public bool AreHintsVisible => _hintsCanvasRoot != null && _hintsCanvasRoot.activeSelf;

    public void SetHintsVisible(bool visible)
    {
        if (_hintsCanvasRoot != null)
            _hintsCanvasRoot.SetActive(visible);
    }

    private void Start()
    {
        var canvasGo = new GameObject("SandboxHintsCanvas");
        canvasGo.transform.SetParent(transform, false);
        _hintsCanvasRoot = canvasGo;
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5000;
        canvasGo.AddComponent<GraphicRaycaster>();

        var panel = new GameObject("HintsPanel");
        panel.transform.SetParent(canvasGo.transform, false);
        var bg = panel.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.55f);
        bg.raycastTarget = false;
        var prt = panel.GetComponent<RectTransform>();
        prt.anchorMin = new Vector2(0f, 0f);
        prt.anchorMax = new Vector2(0f, 0f);
        prt.pivot = new Vector2(0f, 0f);
        prt.anchoredPosition = new Vector2(12f, 12f);
        prt.sizeDelta = new Vector2(340f, 92f);

        var textGo = new GameObject("HintsText");
        textGo.transform.SetParent(panel.transform, false);
        var trt = textGo.AddComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(10f, 8f);
        trt.offsetMax = new Vector2(-10f, -8f);

        var text = textGo.AddComponent<Text>();
        MrBlueprintUiFont.Apply(text);
        text.text = hintText;
        text.fontSize = 15;
        text.color = new Color(0.95f, 0.95f, 0.95f, 1f);
        text.alignment = TextAnchor.UpperLeft;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;

        SetHintsVisible(startVisible);
    }
}
