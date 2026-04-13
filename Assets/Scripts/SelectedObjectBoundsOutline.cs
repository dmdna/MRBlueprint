using UnityEngine;

/// <summary>
/// Draws a world-axis wireframe around the selected object’s combined renderer bounds so the mesh keeps its real color.
/// </summary>
public sealed class SelectedObjectBoundsOutline : MonoBehaviour
{
    [SerializeField] private Color outlineColor = new(0.2f, 0.95f, 1f, 1f);
    [SerializeField] private float lineWidth = 0.025f;
    [SerializeField] private float boundsPadding = 0.04f;

    private static readonly int[,] EdgeCorners =
    {
        { 0, 1 }, { 1, 2 }, { 2, 3 }, { 3, 0 },
        { 4, 5 }, { 5, 6 }, { 6, 7 }, { 7, 4 },
        { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 },
    };

    private LineRenderer[] _edges;
    private PlaceableAsset _target;
    private readonly Vector3[] _cornerScratch = new Vector3[8];

    private void Awake()
    {
        var root = new GameObject("BoundsOutlineEdges");
        root.transform.SetParent(transform, false);
        _edges = new LineRenderer[12];
        var sh = Shader.Find("Sprites/Default");
        if (sh == null)
            sh = Shader.Find("Unlit/Color");
        var mat = sh != null ? new Material(sh) : null;

        for (var i = 0; i < 12; i++)
        {
            var go = new GameObject("Edge" + i);
            go.transform.SetParent(root.transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.loop = false;
            lr.positionCount = 2;
            lr.useWorldSpace = true;
            lr.widthMultiplier = lineWidth;
            lr.numCornerVertices = 1;
            lr.numCapVertices = 1;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            if (mat != null)
                lr.material = mat;
            lr.startColor = outlineColor;
            lr.endColor = outlineColor;
            lr.enabled = false;
            _edges[i] = lr;
        }
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
        if (_target == null)
            return;

        if (!TryGetWorldBounds(_target, out var b))
            return;

        b.Expand(boundsPadding);
        FillCorners(b, _cornerScratch);
        for (var e = 0; e < 12; e++)
        {
            var lr = _edges[e];
            lr.SetPosition(0, _cornerScratch[EdgeCorners[e, 0]]);
            lr.SetPosition(1, _cornerScratch[EdgeCorners[e, 1]]);
        }
    }

    private void OnSelectionChanged(PlaceableAsset asset)
    {
        _target = asset;
        var on = asset != null;
        if (_edges != null)
        {
            foreach (var lr in _edges)
            {
                if (lr != null)
                    lr.enabled = on;
            }
        }
    }

    private static bool TryGetWorldBounds(PlaceableAsset p, out Bounds b)
    {
        b = default;
        var rends = p.GetRenderers();
        if (rends == null || rends.Length == 0)
            return false;

        var has = false;
        foreach (var r in rends)
        {
            if (r == null)
                continue;
            if (!has)
            {
                b = r.bounds;
                has = true;
            }
            else
                b.Encapsulate(r.bounds);
        }

        return has;
    }

    private static void FillCorners(Bounds b, Vector3[] corners)
    {
        var mn = b.min;
        var mx = b.max;
        corners[0] = new Vector3(mn.x, mn.y, mn.z);
        corners[1] = new Vector3(mx.x, mn.y, mn.z);
        corners[2] = new Vector3(mx.x, mn.y, mx.z);
        corners[3] = new Vector3(mn.x, mn.y, mx.z);
        corners[4] = new Vector3(mn.x, mx.y, mn.z);
        corners[5] = new Vector3(mx.x, mx.y, mn.z);
        corners[6] = new Vector3(mx.x, mx.y, mx.z);
        corners[7] = new Vector3(mn.x, mx.y, mx.z);
    }
}
