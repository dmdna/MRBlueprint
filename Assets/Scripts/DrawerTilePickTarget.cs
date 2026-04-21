using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Marks the collider that counts as the clickable drawer tile (not the panel background).
/// Optionally spawns a small world-space caption under the preview mesh.
/// </summary>
public class DrawerTilePickTarget : MonoBehaviour
{
    [SerializeField] private string caption = string.Empty;
    [SerializeField] private bool showCaption;

    /// <summary>Assign before <see cref="Start"/> when building drawer tiles from code.</summary>
    public void SetCaptionForRuntime(string value) => caption = value ?? string.Empty;
    [SerializeField] private Vector3 captionLocalOffset = new(0f, -0.12f, 0.02f);
    [SerializeField] private float captionCanvasScale = 0.0035f;

    private void Start()
    {
        var existingCaption = transform.Find("TileCaption");
        if (existingCaption != null)
        {
            Destroy(existingCaption.gameObject);
        }

        if (!showCaption || string.IsNullOrWhiteSpace(caption))
        {
            return;
        }

        var canvasGo = new GameObject("TileCaption");
        canvasGo.transform.SetParent(transform, false);
        canvasGo.transform.localPosition = captionLocalOffset;
        canvasGo.transform.localRotation = Quaternion.identity;
        canvasGo.transform.localScale = Vector3.one * captionCanvasScale;

        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        var rt = canvasGo.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(280f, 64f);

        var textGo = new GameObject("Text");
        textGo.transform.SetParent(canvasGo.transform, false);
        var trt = textGo.AddComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = Vector2.zero;
        trt.offsetMax = Vector2.zero;

        var text = textGo.AddComponent<Text>();
        text.font = MrBlueprintUiFont.GetDefault();
        text.text = caption;
        text.fontSize = 42;
        text.color = Color.white;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
    }
}
