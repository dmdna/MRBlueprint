using UnityEngine;

/// <summary>
/// World-space ring under the selected <see cref="PlaceableAsset"/> for a clear “active object” cue (hackathon MVP).
/// </summary>
public sealed class SelectedObjectGroundRing : MonoBehaviour
{
    [SerializeField] private Color ringColor = new(1f, 0.82f, 0.15f, 1f);
    [SerializeField] [Min(8)] private int segments = 48;
    [SerializeField] private float padding = 0.1f;
    [SerializeField] private float yOffset = 0.03f;
    [SerializeField] private float lineWidth = 0.04f;
    [SerializeField] private float defaultRadius = 0.4f;

    private LineRenderer _line;
    private PlaceableAsset _target;

    private void Awake()
    {
        var ringGo = new GameObject("SelectionFootprintRing");
        ringGo.transform.SetParent(transform, false);
        _line = ringGo.AddComponent<LineRenderer>();
        _line.loop = true;
        _line.positionCount = segments;
        _line.useWorldSpace = true;
        _line.widthMultiplier = lineWidth;
        _line.numCornerVertices = 2;
        _line.numCapVertices = 2;
        _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _line.receiveShadows = false;

        var sh = Shader.Find("Sprites/Default");
        if (sh == null)
            sh = Shader.Find("Unlit/Color");
        if (sh != null)
            _line.material = new Material(sh);
        _line.startColor = ringColor;
        _line.endColor = ringColor;
        _line.enabled = false;
    }

    private void Start()
    {
        if (AssetSelectionManager.Instance != null)
        {
            AssetSelectionManager.Instance.OnSelectionChanged += OnSelectionChanged;
            OnSelectionChanged(AssetSelectionManager.Instance.SelectedAsset);
        }
    }

    private void OnDestroy()
    {
        if (AssetSelectionManager.Instance != null)
            AssetSelectionManager.Instance.OnSelectionChanged -= OnSelectionChanged;
    }

    private void LateUpdate()
    {
        if (_target == null || _line == null || !_line.enabled)
            return;
        UpdateRingGeometry();
    }

    private void OnSelectionChanged(PlaceableAsset asset)
    {
        _target = asset;
        if (_line == null)
            return;
        _line.enabled = asset != null;
        if (asset != null)
            UpdateRingGeometry();
    }

    private void UpdateRingGeometry()
    {
        if (_line == null || _target == null)
            return;

        if (!TryGetFootprint(_target, out var center, out var radius, out var y))
        {
            center = _target.transform.position;
            radius = defaultRadius;
            y = center.y + yOffset;
        }

        for (var i = 0; i < segments; i++)
        {
            var t = (float)i / segments * Mathf.PI * 2f;
            var x = center.x + Mathf.Cos(t) * radius;
            var z = center.z + Mathf.Sin(t) * radius;
            _line.SetPosition(i, new Vector3(x, y, z));
        }
    }

    private bool TryGetFootprint(PlaceableAsset p, out Vector3 centerXZ, out float radius, out float ringY)
    {
        centerXZ = default;
        radius = defaultRadius;
        ringY = 0f;

        var rends = p.GetRenderers();
        if (rends == null || rends.Length == 0)
            return false;

        var hasAny = false;
        var b = new Bounds(p.transform.position, Vector3.zero);
        foreach (var r in rends)
        {
            if (r == null)
                continue;
            if (!hasAny)
            {
                b = r.bounds;
                hasAny = true;
            }
            else
                b.Encapsulate(r.bounds);
        }

        if (!hasAny)
            return false;

        centerXZ = new Vector3(b.center.x, 0f, b.center.z);
        radius = Mathf.Max(Mathf.Max(b.extents.x, b.extents.z), 0.05f) + padding;
        ringY = b.min.y + yOffset;
        return true;
    }
}
