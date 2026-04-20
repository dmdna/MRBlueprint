using UnityEngine;

public sealed class VelocityRibbonOverlay
{
    private sealed class RibbonInstance
    {
        public GameObject Root;
        public LineRenderer Line;
        public Vector3[] Points;
    }

    private RibbonInstance[] _items;
    private Material _material;
    private int _used;

    public void Initialize(Transform parent, VisualizationConfig config)
    {
        var capacity = config != null ? config.MaxVelocityRibbons : 6;
        var samples = config != null ? config.MaxTrailSamples : 192;
        _material = VisualizationRenderUtility.CreateOverlayMaterial(
            "SimulationVisualizationVelocityRibbon",
            config != null ? config.RibbonColor : Color.cyan);
        _items = new RibbonInstance[capacity];
        for (var i = 0; i < _items.Length; i++)
            _items[i] = CreateInstance(parent, i, samples);
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
        _used = 0;
    }

    public void BeginFrame()
    {
        _used = 0;
    }

    public void Render(BodyTelemetryTracker tracker, bool focus, VisualizationConfig config)
    {
        if (_items == null || _used >= _items.Length || tracker == null || config == null)
            return;

        var sample = tracker.CurrentSample;
        if (!focus && sample.Speed < config.RibbonMinSpeed)
            return;

        if (!focus && tracker.Importance < config.FocusImportanceThreshold)
            return;

        var item = _items[_used++];
        var count = tracker.CopyTrail(item.Points);
        if (count < 2)
        {
            item.Root.SetActive(false);
            return;
        }

        var widthBlend = Mathf.Clamp01(sample.Speed / config.RibbonSpeedForFullWidth);
        var width = Mathf.Lerp(config.RibbonBaseWidth, config.RibbonFastWidth, widthBlend);
        var colorStart = config.RibbonColor;
        var colorEnd = config.RibbonColor;
        colorStart.a = focus ? 0.08f : 0.04f;
        colorEnd.a = focus ? 0.86f : Mathf.Clamp01(0.24f + tracker.Importance * 0.08f);

        item.Root.SetActive(true);
        item.Line.positionCount = count;
        item.Line.startColor = colorStart;
        item.Line.endColor = colorEnd;
        item.Line.startWidth = width * 0.25f;
        item.Line.endWidth = width;
        for (var i = 0; i < count; i++)
            item.Line.SetPosition(i, item.Points[i]);
    }

    public void EndFrame()
    {
        if (_items == null)
            return;

        for (var i = _used; i < _items.Length; i++)
        {
            if (_items[i].Root != null && _items[i].Root.activeSelf)
                _items[i].Root.SetActive(false);
        }
    }

    public void HideAll()
    {
        _used = 0;
        EndFrame();
    }

    private RibbonInstance CreateInstance(Transform parent, int index, int samples)
    {
        var root = new GameObject("VelocityRibbon_" + index);
        root.transform.SetParent(parent, false);
        var line = root.AddComponent<LineRenderer>();
        VisualizationRenderUtility.ConfigureLine(line, _material, false);
        line.positionCount = 0;
        root.SetActive(false);

        return new RibbonInstance
        {
            Root = root,
            Line = line,
            Points = new Vector3[Mathf.Max(2, samples)]
        };
    }
}
