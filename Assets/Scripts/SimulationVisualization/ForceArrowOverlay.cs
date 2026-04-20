using UnityEngine;

public sealed class ForceArrowOverlay
{
    private sealed class ArrowInstance
    {
        public GameObject Root;
        public LineRenderer Shaft;
        public LineRenderer HeadA;
        public LineRenderer HeadB;
        public Vector3 SmoothedEnd;
        public bool HasSmoothedEnd;
    }

    private ArrowInstance[] _items;
    private Material _material;
    private int _used;

    public void Initialize(Transform parent, VisualizationConfig config)
    {
        var capacity = config != null ? config.MaxForceSpears : 8;
        _material = VisualizationRenderUtility.CreateOverlayMaterial(
            "SimulationVisualizationForceSpear",
            config != null ? config.OtherColor : Color.white);
        _items = new ArrowInstance[capacity];
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
        _used = 0;
    }

    public void BeginFrame()
    {
        _used = 0;
    }

    public void Render(BodyTelemetryTracker tracker, bool focus, VisualizationConfig config, Camera camera)
    {
        if (_items == null || _used >= _items.Length || tracker == null || config == null)
            return;

        var sample = tracker.CurrentSample;
        if (!focus && sample.IsSleeping)
            return;

        var force = tracker.ApproxNetForceVector;
        var magnitude = force.magnitude;
        if (magnitude < config.LowForceThreshold && !focus)
            return;

        Vector3 direction;
        if (magnitude > 0.0001f)
        {
            direction = force / magnitude;
        }
        else if (sample.GravityEnabled && Physics.gravity.sqrMagnitude > 0.0001f)
        {
            direction = Physics.gravity.normalized;
            magnitude = Mathf.Max(config.LowForceThreshold, sample.Mass * Physics.gravity.magnitude);
        }
        else
        {
            return;
        }

        var length = Mathf.Clamp(
            magnitude * config.ForceMetersPerNewton,
            focus ? config.ForceMinLength : config.ForceMinLength * 0.5f,
            config.ForceMaxLength);
        var start = sample.CenterOfMass;
        var rawEnd = start + direction * length;
        var item = _items[_used++];
        var dt = Application.isPlaying ? Time.deltaTime : 0.016f;
        var blend = 1f - Mathf.Exp(-config.ForceSmoothingSharpness * dt);
        if (!item.HasSmoothedEnd)
        {
            item.SmoothedEnd = rawEnd;
            item.HasSmoothedEnd = true;
        }
        else
        {
            item.SmoothedEnd = Vector3.Lerp(item.SmoothedEnd, rawEnd, blend);
        }

        var color = config.GetDriverColor(sample.DominantDriver);
        var alpha = focus ? 0.98f : Mathf.Clamp01(0.28f + tracker.Importance * 0.16f);
        color.a = alpha;
        var width = Mathf.Lerp(config.ForceBaseWidth, config.ForceFocusWidth, focus ? 1f : Mathf.Clamp01(tracker.Importance / 4f));

        item.Root.SetActive(true);
        SetLine(item.Shaft, start, item.SmoothedEnd, color, width);

        var arrowDir = item.SmoothedEnd - start;
        if (arrowDir.sqrMagnitude <= 0.000001f)
            arrowDir = direction;
        arrowDir.Normalize();
        var side = camera != null ? Vector3.Cross(arrowDir, camera.transform.forward) : Vector3.Cross(arrowDir, Vector3.up);
        if (side.sqrMagnitude <= 0.0001f)
            side = Vector3.Cross(arrowDir, Vector3.right);
        side.Normalize();

        var headLength = Mathf.Clamp(length * 0.22f, 0.035f, 0.11f);
        var headWidth = headLength * 0.42f;
        var back = item.SmoothedEnd - arrowDir * headLength;
        SetLine(item.HeadA, item.SmoothedEnd, back + side * headWidth, color, width);
        SetLine(item.HeadB, item.SmoothedEnd, back - side * headWidth, color, width);
    }

    public void EndFrame()
    {
        if (_items == null)
            return;

        for (var i = _used; i < _items.Length; i++)
        {
            if (_items[i].Root != null && _items[i].Root.activeSelf)
                _items[i].Root.SetActive(false);
            _items[i].HasSmoothedEnd = false;
        }
    }

    public void HideAll()
    {
        _used = 0;
        EndFrame();
    }

    private ArrowInstance CreateInstance(Transform parent, int index)
    {
        var root = new GameObject("ForceSpear_" + index);
        root.transform.SetParent(parent, false);
        var item = new ArrowInstance
        {
            Root = root,
            Shaft = CreateLine(root.transform, "Shaft"),
            HeadA = CreateLine(root.transform, "HeadA"),
            HeadB = CreateLine(root.transform, "HeadB")
        };
        root.SetActive(false);
        return item;
    }

    private LineRenderer CreateLine(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var line = go.AddComponent<LineRenderer>();
        VisualizationRenderUtility.ConfigureLine(line, _material, false);
        line.positionCount = 2;
        return line;
    }

    private static void SetLine(LineRenderer line, Vector3 start, Vector3 end, Color color, float width)
    {
        if (line == null)
            return;

        line.startWidth = width;
        line.endWidth = width * 0.72f;
        line.startColor = color;
        line.endColor = color;
        line.SetPosition(0, start);
        line.SetPosition(1, end);
    }
}
