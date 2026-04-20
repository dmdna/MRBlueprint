using UnityEngine;
using UnityEngine.UI;

public static class PhysicsLensRenderUtility
{
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
        return image;
    }
}
