using UnityEngine;
using UnityEngine.Rendering;

public static class VisualizationRenderUtility
{
    public static Material CreateOverlayMaterial(string name, Color color)
    {
        var shader = Shader.Find("MRBlueprint/PhysicsDrawingAuraMaxBlend")
                     ?? Shader.Find("Sprites/Default")
                     ?? Shader.Find("Universal Render Pipeline/Unlit")
                     ?? Shader.Find("Unlit/Color");
        if (shader == null)
            shader = Shader.Find("Standard");

        var material = new Material(shader)
        {
            name = name,
            color = color,
            enableInstancing = true
        };

        ConfigureTransparent(material);
        material.renderQueue = (int)RenderQueue.Transparent + 26;
        return material;
    }

    public static void ApplyColor(Material material, Color color)
    {
        if (material == null)
            return;

        material.color = color;
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", color);
    }

    public static void ConfigureLine(LineRenderer line, Material material, bool loop)
    {
        if (line == null)
            return;

        line.useWorldSpace = true;
        line.loop = loop;
        line.alignment = LineAlignment.View;
        line.textureMode = LineTextureMode.Stretch;
        line.numCapVertices = 8;
        line.numCornerVertices = 4;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.sharedMaterial = material;
    }

    public static Color WithAlpha(Color color, float alpha)
    {
        color.a = alpha;
        return color;
    }

    public static void BuildBasis(Vector3 normal, out Vector3 right, out Vector3 up)
    {
        if (normal.sqrMagnitude <= 0.0001f)
            normal = Vector3.forward;
        normal.Normalize();

        right = Vector3.Cross(Vector3.up, normal);
        if (right.sqrMagnitude <= 0.0001f)
            right = Vector3.Cross(Vector3.right, normal);
        right.Normalize();

        up = Vector3.Cross(normal, right);
        if (up.sqrMagnitude <= 0.0001f)
            up = Vector3.up;
        else
            up.Normalize();
    }

    private static void ConfigureTransparent(Material material)
    {
        if (material == null)
            return;

        if (material.HasProperty("_Surface"))
            material.SetFloat("_Surface", 1f);
        if (material.HasProperty("_Blend"))
            material.SetFloat("_Blend", 0f);
        if (material.HasProperty("_Cull"))
            material.SetFloat("_Cull", (float)CullMode.Off);

        material.SetOverrideTag("RenderType", "Transparent");
        material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        material.SetInt("_ZWrite", 0);
        if (material.HasProperty("_ZTest"))
            material.SetInt("_ZTest", (int)CompareFunction.Always);
        material.DisableKeyword("_ALPHATEST_ON");
        material.EnableKeyword("_ALPHABLEND_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
    }
}
