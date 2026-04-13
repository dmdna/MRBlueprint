using UnityEngine;

/// <summary>
/// Optional per-renderer tint via MaterialPropertyBlock. Off by default so the object keeps its real color
/// while <see cref="SelectedObjectBoundsOutline"/> / ground ring show selection.
/// </summary>
public class SelectedAssetHighlight : MonoBehaviour
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    [SerializeField] private bool applyMaterialTint;
    [SerializeField] private Color highlightTint = new(1f, 0.92f, 0.35f, 1f);
    [SerializeField] private float highlightMix = 0.45f;

    private PlaceableAsset _current;
    private MaterialPropertyBlock _mpb;

    private void Awake()
    {
        _mpb = new MaterialPropertyBlock();
    }

    private void Start()
    {
        if (AssetSelectionManager.Instance != null)
        {
            AssetSelectionManager.Instance.OnSelectionChanged += HandleSelectionChanged;
            HandleSelectionChanged(AssetSelectionManager.Instance.SelectedAsset);
        }
    }

    private void OnDestroy()
    {
        if (AssetSelectionManager.Instance != null)
        {
            AssetSelectionManager.Instance.OnSelectionChanged -= HandleSelectionChanged;
        }

        ClearHighlight();
    }

    private void HandleSelectionChanged(PlaceableAsset asset)
    {
        ClearHighlight();
        _current = asset;
        if (_current == null)
        {
            return;
        }

        if (!applyMaterialTint)
        {
            return;
        }

        foreach (var r in _current.GetRenderers())
        {
            if (r == null)
            {
                continue;
            }

            _mpb.Clear();
            r.GetPropertyBlock(_mpb);
            var baseCol = ResolveBaseColor(r);
            _mpb.SetColor(UsesBaseColor(r) ? BaseColorId : ColorId, Color.Lerp(baseCol, highlightTint, highlightMix));
            r.SetPropertyBlock(_mpb);
        }
    }

    private void ClearHighlight()
    {
        if (_current == null)
        {
            return;
        }

        foreach (var r in _current.GetRenderers())
        {
            if (r == null)
            {
                continue;
            }

            r.SetPropertyBlock(null);
        }

        _current = null;
    }

    private static bool UsesBaseColor(Renderer r)
    {
        var m = r.sharedMaterial;
        return m != null && m.HasProperty(BaseColorId);
    }

    private static Color ResolveBaseColor(Renderer r)
    {
        var m = r.sharedMaterial;
        if (m == null)
        {
            return Color.white;
        }

        if (m.HasProperty(BaseColorId))
        {
            return m.GetColor(BaseColorId);
        }

        if (m.HasProperty(ColorId))
        {
            return m.GetColor(ColorId);
        }

        return Color.white;
    }
}
