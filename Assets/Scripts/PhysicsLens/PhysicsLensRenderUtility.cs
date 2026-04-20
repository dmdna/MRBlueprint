using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

public static class PhysicsLensRenderUtility
{
    private static Material _uiOverlayMaterial;
    private static Material _uiPerspectiveOverlayMaterial;

    public static Material CreateVertexColorMaterial(string name)
    {
        var shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");

        var material = new Material(shader)
        {
            name = name,
            color = Color.white
        };

        ConfigureOverlayDepth(material);
        return material;
    }

    public static Material CreateTintMaterial(string name, Color color)
    {
        var material = CreateVertexColorMaterial(name);
        material.color = color;
        return material;
    }

    public static Text CreateText(
        Transform parent,
        string name,
        Font font,
        int size,
        TextAnchor alignment,
        Color color,
        Vector2 anchoredPosition,
        Vector2 sizeDelta)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rect = go.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = sizeDelta;

        var text = go.AddComponent<Text>();
        text.font = font != null ? font : MrBlueprintUiFont.GetDefault();
        text.fontSize = size;
        text.alignment = alignment;
        text.color = color;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        ApplyUiOverlayMaterial(text);
        return text;
    }

    public static Image CreateImage(Transform parent, string name, Color color, Vector2 anchoredPosition, Vector2 sizeDelta)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rect = go.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = sizeDelta;

        var image = go.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        ApplyUiOverlayMaterial(image);
        return image;
    }

    public static void ApplyUiOverlayMaterial(Graphic graphic)
    {
        if (graphic == null)
        {
            return;
        }

        var material = GetUiOverlayMaterial();
        if (material != null)
        {
            graphic.material = material;
        }
    }

    public static void ApplyUiPerspectiveOverlayMaterial(Graphic graphic)
    {
        if (graphic == null)
        {
            return;
        }

        var material = GetUiPerspectiveOverlayMaterial();
        if (material != null)
        {
            graphic.material = material;
        }
    }

    private static Material GetUiOverlayMaterial()
    {
        if (_uiOverlayMaterial != null)
        {
            return _uiOverlayMaterial;
        }

        var shader = FindUiOverlayShader();
        if (shader == null)
        {
            return null;
        }

        _uiOverlayMaterial = new Material(shader)
        {
            name = "MRBlueprintUiOverlayNoDepth",
            hideFlags = HideFlags.HideAndDontSave,
            renderQueue = (int)RenderQueue.Overlay - 10
        };
        ConfigureOverlayDepth(_uiOverlayMaterial);
        return _uiOverlayMaterial;
    }

    private static Material GetUiPerspectiveOverlayMaterial()
    {
        if (_uiPerspectiveOverlayMaterial != null)
        {
            return _uiPerspectiveOverlayMaterial;
        }

        var shader = FindUiOverlayShader();
        if (shader == null)
        {
            return null;
        }

        _uiPerspectiveOverlayMaterial = new Material(shader)
        {
            name = "MRBlueprintUiPerspectiveOverlay",
            hideFlags = HideFlags.HideAndDontSave,
            renderQueue = (int)RenderQueue.Overlay - 8
        };
        ConfigurePerspectiveOverlayDepth(_uiPerspectiveOverlayMaterial);
        return _uiPerspectiveOverlayMaterial;
    }

    private static Shader FindUiOverlayShader()
    {
        var shader = Shader.Find("MRBlueprint/UiDepthOverlay");
        if (shader == null)
        {
            shader = Shader.Find("UI/Default");
        }

        if (shader == null)
        {
            shader = Shader.Find("Sprites/Default");
        }

        return shader;
    }

    private static void ConfigureOverlayDepth(Material material)
    {
        if (material == null)
        {
            return;
        }

        if (material.HasProperty("_ZTest"))
        {
            material.SetInt("_ZTest", (int)CompareFunction.Always);
        }

        if (material.HasProperty("_ZWrite"))
        {
            material.SetInt("_ZWrite", 0);
        }

        if (material.HasProperty("_AlphaClipThreshold"))
        {
            material.SetFloat("_AlphaClipThreshold", 0.001f);
        }

        material.renderQueue = (int)RenderQueue.Overlay - 10;
    }

    private static void ConfigurePerspectiveOverlayDepth(Material material)
    {
        if (material == null)
        {
            return;
        }

        if (material.HasProperty("_ZTest"))
        {
            material.SetInt("_ZTest", (int)CompareFunction.Always);
        }

        if (material.HasProperty("_ZWrite"))
        {
            material.SetInt("_ZWrite", 1);
        }

        if (material.HasProperty("_AlphaClipThreshold"))
        {
            material.SetFloat("_AlphaClipThreshold", 0.001f);
        }

        material.renderQueue = (int)RenderQueue.Overlay - 8;
    }
}
