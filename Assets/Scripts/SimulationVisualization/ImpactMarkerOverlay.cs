using UnityEngine;

public sealed class ImpactMarkerOverlay
{
    private const int RingSegments = 36;

    private sealed class ImpactInstance
    {
        public GameObject Root;
        public LineRenderer Ring;
        public Vector3[] Points;
        public Vector3 Point;
        public float StartTime;
        public float Duration;
        public float Impulse;
        public bool Active;
    }

    private ImpactInstance[] _items;
    private Material _material;
    private int _next;

    public void Initialize(Transform parent, VisualizationConfig config)
    {
        var capacity = config != null ? config.MaxImpactMarkers : 24;
        _material = VisualizationRenderUtility.CreateOverlayMaterial(
            "SimulationVisualizationImpactFlash",
            config != null ? config.ImpactColor : Color.red);
        _items = new ImpactInstance[capacity];
        for (var i = 0; i < _items.Length; i++)
            _items[i] = CreateInstance(parent, i);
    }

    public void Dispose()
    {
        if (_items != null)
        {
            for (var i = 0; i < _items.Length; i++)
            {
                if (_items[i] != null && _items[i].Root != null)
                    Object.Destroy(_items[i].Root);
            }
        }

        if (_material != null)
            Object.Destroy(_material);

        _items = null;
        _material = null;
        _next = 0;
    }

    public void Emit(Vector3 point, float impulse, VisualizationConfig config)
    {
        if (_items == null || _items.Length == 0 || config == null || impulse < config.ImpactMinImpulse)
            return;

        var item = _items[_next];
        _next = (_next + 1) % _items.Length;
        item.Point = point;
        item.Impulse = impulse;
        item.StartTime = Time.time;
        item.Duration = config.ImpactDuration;
        item.Active = true;
        item.Root.SetActive(true);
    }

    public void Update(Camera camera, VisualizationConfig config)
    {
        if (_items == null || config == null)
            return;

        var forward = camera != null ? camera.transform.forward : Vector3.forward;
        VisualizationRenderUtility.BuildBasis(forward, out var right, out var up);

        for (var i = 0; i < _items.Length; i++)
        {
            var item = _items[i];
            if (item == null || !item.Active)
                continue;

            var t = Mathf.Clamp01((Time.time - item.StartTime) / Mathf.Max(0.001f, item.Duration));
            if (t >= 1f)
            {
                item.Active = false;
                item.Root.SetActive(false);
                continue;
            }

            var impulseBlend = Mathf.Clamp01(item.Impulse / 12f);
            var radius = Mathf.Lerp(config.ImpactRadiusMin, config.ImpactRadiusMax, impulseBlend) * (0.35f + t * 0.9f);
            var alpha = (1f - t) * Mathf.Lerp(0.45f, 1f, impulseBlend);
            var color = config.ImpactColor;
            color.a = alpha;

            item.Ring.startColor = color;
            item.Ring.endColor = color;
            item.Ring.startWidth = Mathf.Lerp(0.012f, 0.028f, impulseBlend) * (1f - t * 0.35f);
            item.Ring.endWidth = item.Ring.startWidth;
            for (var segment = 0; segment < RingSegments; segment++)
            {
                var angle = segment * Mathf.PI * 2f / RingSegments;
                item.Points[segment] = item.Point
                                       + right * (Mathf.Cos(angle) * radius)
                                       + up * (Mathf.Sin(angle) * radius);
                item.Ring.SetPosition(segment, item.Points[segment]);
            }
        }
    }

    public void HideAll()
    {
        if (_items == null)
            return;

        for (var i = 0; i < _items.Length; i++)
        {
            _items[i].Active = false;
            if (_items[i].Root != null)
                _items[i].Root.SetActive(false);
        }
    }

    private ImpactInstance CreateInstance(Transform parent, int index)
    {
        var root = new GameObject("ImpactFlash_" + index);
        root.transform.SetParent(parent, false);
        var ring = root.AddComponent<LineRenderer>();
        VisualizationRenderUtility.ConfigureLine(ring, _material, true);
        ring.positionCount = RingSegments;
        root.SetActive(false);

        return new ImpactInstance
        {
            Root = root,
            Ring = ring,
            Points = new Vector3[RingSegments]
        };
    }
}
