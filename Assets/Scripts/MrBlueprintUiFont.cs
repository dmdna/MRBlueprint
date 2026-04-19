using UnityEngine;

/// <summary>
/// Default UI font for legacy <see cref="UnityEngine.UI.Text"/> (ZT Nature). Editor loads from <c>Assets/Fonts</c>;
/// players load the copy under <c>Resources/Fonts</c>.
/// </summary>
public static class MrBlueprintUiFont
{
    private const string EditorFontPath = "Assets/Fonts/ZT Nature - OT/ZTNature-Regular.otf";
    private const string ResourcesFontPath = "Fonts/ZTNature-Regular";

    private static Font _cached;

    public static Font GetDefault()
    {
        if (_cached != null)
            return _cached;

#if UNITY_EDITOR
        _cached = UnityEditor.AssetDatabase.LoadAssetAtPath<Font>(EditorFontPath);
#endif
        if (_cached == null)
            _cached = Resources.Load<Font>(ResourcesFontPath);

        if (_cached == null)
        {
            _cached = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                      ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
        }

        return _cached;
    }
}
