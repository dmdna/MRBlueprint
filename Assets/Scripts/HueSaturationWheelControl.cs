using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Click/drag on a hue–saturation disk (center = white, rim = full saturation). Emits H and S in [0,1].
/// </summary>
[RequireComponent(typeof(Image))]
public sealed class HueSaturationWheelControl : MonoBehaviour, IPointerDownHandler, IDragHandler
{
    public System.Action<float, float> HsChanged;

    private RectTransform _thumb;
    private RectTransform _wheelRect;
    private Image _image;

    public void Init(RectTransform thumbTransform)
    {
        _thumb = thumbTransform;
    }

    private void Awake()
    {
        _image = GetComponent<Image>();
        _wheelRect = _image.rectTransform;
    }

    public void OnPointerDown(PointerEventData eventData) => Sample(eventData);

    public void OnDrag(PointerEventData eventData) => Sample(eventData);

    /// <summary>Move the thumb to match H/S without firing <see cref="HsChanged"/>.</summary>
    public void SetThumbFromHs(float h, float s)
    {
        var maxR = GetMaxRadius();
        var ang = h * (Mathf.PI * 2f);
        var pos = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * (s * maxR);
        if (_thumb != null)
            _thumb.anchoredPosition = pos;
    }

    private void Sample(PointerEventData eventData)
    {
        var cam = eventData.pressEventCamera;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _wheelRect, eventData.position, cam, out var local))
            return;

        var maxR = GetMaxRadius();
        var r = local.magnitude;
        if (r > maxR)
            local = local * (maxR / Mathf.Max(r, 1e-6f));

        var s = Mathf.Clamp01(maxR > 1e-6f ? local.magnitude / maxR : 0f);
        var h = Mathf.Repeat(Mathf.Atan2(local.y, local.x) / (Mathf.PI * 2f), 1f);

        if (_thumb != null)
            _thumb.anchoredPosition = local;

        HsChanged?.Invoke(h, s);
    }

    private float GetMaxRadius()
    {
        return Mathf.Max(1f, Mathf.Min(_wheelRect.rect.width, _wheelRect.rect.height) * 0.5f - 6f);
    }

    public static Texture2D CreateHueSaturationDiskTexture(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        var half = size * 0.5f;
        for (var py = 0; py < size; py++)
        {
            for (var px = 0; px < size; px++)
            {
                var vx = (px + 0.5f - half) / half;
                var vy = (py + 0.5f - half) / half;
                var r = Mathf.Sqrt(vx * vx + vy * vy);
                if (r > 1f)
                {
                    tex.SetPixel(px, py, Color.clear);
                    continue;
                }

                var hue = Mathf.Repeat(Mathf.Atan2(vy, vx) / (Mathf.PI * 2f), 1f);
                var sat = r;
                tex.SetPixel(px, py, Color.HSVToRGB(hue, sat, 1f));
            }
        }

        tex.Apply(false, true);
        return tex;
    }
}
