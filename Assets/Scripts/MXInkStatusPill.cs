using UnityEngine;
using UnityEngine.UI;

public sealed class MXInkStatusPill : MonoBehaviour
{
    private const string DetectedLabel = "MX Ink detected";
    private const string MissingLabel = "MX Ink is not detected";
    private const float ResolveRetryInterval = 0.5f;

    private static readonly Color DetectedBackground = new Color(0.05f, 0.8f, 0.34f, 0.52f);
    private static readonly Color MissingBackground = new Color(0.42f, 0.02f, 0.02f, 0.64f);
    private static readonly Color DetectedText = new Color(0.1f, 0.9f, 0.42f, 1f);
    private static readonly Color MissingText = new Color(1f, 0.28f, 0.28f, 1f);

    private static Sprite _pillSprite;

    private Image _background;
    private Text _label;
    private VrStylusHandler _stylusHandler;
    private bool _hasRenderedState;
    private bool _lastDetected;
    private float _nextResolveTime;

    public static MXInkStatusPill Create(
        Transform parent,
        Font font,
        string name,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 pivot,
        Vector2 anchoredPosition,
        Vector2 size,
        int fontSize = 15,
        float horizontalTextPadding = 12f)
    {
        var root = new GameObject(name);
        root.transform.SetParent(parent, false);

        var rect = root.AddComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;

        var background = root.AddComponent<Image>();
        background.sprite = GetPillSprite();
        background.type = Image.Type.Sliced;
        background.raycastTarget = false;

        var textGo = new GameObject("Text");
        textGo.transform.SetParent(root.transform, false);
        var textRect = textGo.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(horizontalTextPadding, 0f);
        textRect.offsetMax = new Vector2(-horizontalTextPadding, 0f);

        var label = textGo.AddComponent<Text>();
        MrBlueprintUiFont.Apply(label, font);
        label.fontSize = fontSize;
        label.alignment = TextAnchor.MiddleCenter;
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        label.verticalOverflow = VerticalWrapMode.Truncate;
        label.raycastTarget = false;

        var pill = root.AddComponent<MXInkStatusPill>();
        pill.Configure(background, label);
        pill.Refresh(true);
        return pill;
    }

    private static Sprite GetPillSprite()
    {
        if (_pillSprite != null)
        {
            return _pillSprite;
        }

        const int width = 64;
        const int height = 32;
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            name = "MXInkStatusPillSprite",
            hideFlags = HideFlags.HideAndDontSave,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        var pixels = new Color32[width * height];
        var centerY = height * 0.5f;
        var radius = centerY - 0.5f;
        var leftCenterX = radius + 0.5f;
        var rightCenterX = width - radius - 0.5f;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var sampleX = x + 0.5f;
                var sampleY = y + 0.5f;
                var dx = 0f;
                if (sampleX < leftCenterX)
                {
                    dx = sampleX - leftCenterX;
                }
                else if (sampleX > rightCenterX)
                {
                    dx = sampleX - rightCenterX;
                }

                var dy = sampleY - centerY;
                var distance = Mathf.Sqrt(dx * dx + dy * dy);
                var alpha = Mathf.Clamp01(radius + 0.75f - distance);
                pixels[y * width + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, true);

        _pillSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, width, height),
            new Vector2(0.5f, 0.5f),
            100f,
            0,
            SpriteMeshType.FullRect,
            new Vector4(16f, 16f, 16f, 16f));
        _pillSprite.name = "MXInkStatusPillSprite";
        _pillSprite.hideFlags = HideFlags.HideAndDontSave;
        return _pillSprite;
    }

    private void Awake()
    {
        CacheReferences();
        Refresh(true);
    }

    private void OnEnable()
    {
        Refresh(true);
    }

    private void Update()
    {
        Refresh(false);
    }

    private void Configure(Image background, Text label)
    {
        _background = background;
        _label = label;
    }

    private void CacheReferences()
    {
        if (_background == null)
        {
            _background = GetComponent<Image>();
        }

        if (_label == null)
        {
            _label = GetComponentInChildren<Text>(true);
        }
    }

    private void Refresh(bool force)
    {
        CacheReferences();

        var detected = IsDetectedAndUsable();
        if (!force && _hasRenderedState && detected == _lastDetected)
        {
            return;
        }

        _hasRenderedState = true;
        _lastDetected = detected;

        if (_background != null)
        {
            _background.color = detected ? DetectedBackground : MissingBackground;
        }

        if (_label != null)
        {
            _label.text = detected ? DetectedLabel : MissingLabel;
            _label.color = detected ? DetectedText : MissingText;
        }
    }

    private bool IsDetectedAndUsable()
    {
        ResolveStylusHandler();
        return _stylusHandler != null && _stylusHandler.IsMXInkDetectedAndUsable;
    }

    private void ResolveStylusHandler()
    {
        if (_stylusHandler != null || Time.unscaledTime < _nextResolveTime)
        {
            return;
        }

        _nextResolveTime = Time.unscaledTime + ResolveRetryInterval;
        _stylusHandler = FindFirstObjectByType<VrStylusHandler>(FindObjectsInactive.Include);
    }
}
