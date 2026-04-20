using UnityEngine;
using UnityEngine.UI;

public sealed class TimelineGraphRenderer : MonoBehaviour
{
    private const float TopLabelCenterInset = 13f;
    private const float BottomLabelCenterInset = 15f;
    private const float PlotHorizontalInset = 16f;
    private const float PlotBottomInset = 31f;
    private const float PlotTopInset = 31f;

    private PhysicsLensConfig _config;
    private RectTransform _root;
    private PhysicsLensSample[] _sampleScratch;
    private Mesh _lineMesh;
    private Mesh _spikeMesh;
    private Mesh _markerMesh;
    private Mesh _gridMesh;
    private Vector3[] _lineVertices;
    private Color[] _lineColors;
    private Vector3[] _spikeVertices;
    private Color[] _spikeColors;
    private Vector3[] _markerVertices;
    private Color[] _markerColors;
    private Vector3[] _gridVertices;
    private Color[] _gridColors;
    private Transform _head;
    private MeshRenderer _headRenderer;
    private Text _modeLabel;
    private Text _axisLabel;
    private Text _timeLabel;
    private Text _scaleLabel;
    private float _smoothMaxSpeed = 1f;
    private float _smoothMaxForce = 1f;
    private int _maxSegments;
    private int _maxMarkers;
    private Vector2 _graphSize;

    public void Initialize(RectTransform root, PhysicsLensConfig config, Font font)
    {
        _root = root;
        _config = config;
        _graphSize = config != null ? config.CompactGraphSize : new Vector2(374f, 176f);
        var maxSamples = config != null ? config.MaxTelemetrySamples : 512;
        _sampleScratch = new PhysicsLensSample[maxSamples];
        _maxSegments = Mathf.Max(1, maxSamples - 1);
        _maxMarkers = Mathf.Max(16, maxSamples);

        BuildMeshes();
        BuildLabels(font);
        SetSize(_graphSize);
    }

    public void SetSize(Vector2 size)
    {
        _graphSize = size;
        if (_root != null)
            _root.sizeDelta = size;
        RepositionLabels();
        RefreshGrid();
    }

    public void Render(PhysicsTelemetryTracker tracker)
    {
        if (tracker == null || !tracker.HasSamples || _sampleScratch == null)
        {
            ClearDynamicMeshes();
            return;
        }

        var count = tracker.CopySamples(_sampleScratch);
        if (count < 2)
        {
            ClearDynamicMeshes();
            return;
        }

        var now = _sampleScratch[count - 1].Time;
        var history = _config != null ? _config.HistorySeconds : 7.5f;
        var oldestTime = now - history;
        var targetMaxSpeed = 0.1f;
        var targetMaxForce = 0.1f;

        for (var i = 0; i < count; i++)
        {
            if (_sampleScratch[i].Time < oldestTime)
                continue;
            if (_sampleScratch[i].Speed > targetMaxSpeed)
                targetMaxSpeed = _sampleScratch[i].Speed;
            if (_sampleScratch[i].ApproxNetForce > targetMaxForce)
                targetMaxForce = _sampleScratch[i].ApproxNetForce;
        }

        var sharpness = _config != null ? _config.GraphAutoscaleSharpness : 5f;
        var blend = 1f - Mathf.Exp(-sharpness * Time.unscaledDeltaTime);
        _smoothMaxSpeed = Mathf.Lerp(_smoothMaxSpeed, targetMaxSpeed * 1.12f, blend);
        _smoothMaxForce = Mathf.Lerp(_smoothMaxForce, targetMaxForce * 1.12f, blend);
        _smoothMaxSpeed = Mathf.Max(0.1f, _smoothMaxSpeed);
        _smoothMaxForce = Mathf.Max(0.1f, _smoothMaxForce);

        BuildLine(count, oldestTime, history);
        BuildSpikesAndMarkers(count, oldestTime, history);
        UpdateHead(_sampleScratch[count - 1], oldestTime, history);

        if (_scaleLabel != null)
            _scaleLabel.text = _smoothMaxSpeed.ToString("0.0") + " m/s";
    }

    private void BuildMeshes()
    {
        var material = PhysicsLensRenderUtility.CreateVertexColorMaterial("PhysicsLensTimelineMaterial");
        CreateMeshObject("TimelineGrid", material, out _gridMesh);
        CreateMeshObject("TimelineRibbon", material, out _lineMesh);
        CreateMeshObject("TimelineSpikes", material, out _spikeMesh);
        CreateMeshObject("TimelineMarkers", material, out _markerMesh);

        _lineVertices = new Vector3[_maxSegments * 4];
        _lineColors = new Color[_lineVertices.Length];
        _spikeVertices = new Vector3[_maxMarkers * 4];
        _spikeColors = new Color[_spikeVertices.Length];
        _markerVertices = new Vector3[_maxMarkers * 4];
        _markerColors = new Color[_markerVertices.Length];
        _gridVertices = new Vector3[32 * 4];
        _gridColors = new Color[_gridVertices.Length];

        AssignQuadTriangles(_lineMesh, _maxSegments);
        AssignQuadTriangles(_spikeMesh, _maxMarkers);
        AssignQuadTriangles(_markerMesh, _maxMarkers);
        AssignQuadTriangles(_gridMesh, 32);

        var headGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        headGo.name = "TimelineCurrentHead";
        headGo.transform.SetParent(transform, false);
        headGo.transform.localPosition = new Vector3(0f, 0f, -9f);
        headGo.transform.localScale = Vector3.one * 10f;
        var collider = headGo.GetComponent<Collider>();
        if (collider != null)
            Destroy(collider);
        _head = headGo.transform;
        _headRenderer = headGo.GetComponent<MeshRenderer>();
        if (_headRenderer != null)
            _headRenderer.sharedMaterial = PhysicsLensRenderUtility.CreateTintMaterial("PhysicsLensTimelineHead", Color.white);
    }

    private void BuildLabels(Font font)
    {
        _modeLabel = PhysicsLensRenderUtility.CreateText(transform, "TimelineModeLabel", font, 18, TextAnchor.UpperLeft,
            _config != null ? _config.TextSecondary : Color.gray, new Vector2(-150f, 78f), new Vector2(160f, 24f));
        _modeLabel.text = "Motion Timeline";

        _axisLabel = PhysicsLensRenderUtility.CreateText(transform, "TimelineAxisLabel", font, 14, TextAnchor.UpperLeft,
            _config != null ? _config.TextSecondary : Color.gray, new Vector2(-166f, -90f), new Vector2(130f, 22f));
        _axisLabel.text = "speed";

        _timeLabel = PhysicsLensRenderUtility.CreateText(transform, "TimelineTimeLabel", font, 14, TextAnchor.LowerRight,
            _config != null ? _config.TextSecondary : Color.gray, new Vector2(142f, -90f), new Vector2(150f, 22f));
        _timeLabel.text = "last seconds";

        _scaleLabel = PhysicsLensRenderUtility.CreateText(transform, "TimelineScaleLabel", font, 14, TextAnchor.UpperRight,
            _config != null ? _config.TextSecondary : Color.gray, new Vector2(151f, 78f), new Vector2(140f, 22f));
        RepositionLabels();
    }

    private void RepositionLabels()
    {
        var halfWidth = _graphSize.x * 0.5f;
        var halfHeight = _graphSize.y * 0.5f;
        SetLabelRect(_modeLabel, new Vector2(-halfWidth + 18f, halfHeight - TopLabelCenterInset), new Vector2(176f, 22f), new Vector2(0f, 0.5f));
        SetLabelRect(_scaleLabel, new Vector2(halfWidth - 16f, halfHeight - TopLabelCenterInset), new Vector2(128f, 22f), new Vector2(1f, 0.5f));
        SetLabelRect(_axisLabel, new Vector2(-halfWidth + 18f, -halfHeight + BottomLabelCenterInset), new Vector2(120f, 20f), new Vector2(0f, 0.5f));
        SetLabelRect(_timeLabel, new Vector2(halfWidth - 16f, -halfHeight + BottomLabelCenterInset), new Vector2(132f, 20f), new Vector2(1f, 0.5f));
    }

    private void CreateMeshObject(string name, Material material, out Mesh mesh)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, 0f, -8f);
        var filter = go.AddComponent<MeshFilter>();
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        mesh = new Mesh { name = name + "Mesh" };
        mesh.MarkDynamic();
        filter.sharedMesh = mesh;
    }

    private static void AssignQuadTriangles(Mesh mesh, int quadCount)
    {
        mesh.vertices = new Vector3[quadCount * 4];
        mesh.colors = new Color[quadCount * 4];
        var triangles = new int[quadCount * 6];
        for (var i = 0; i < quadCount; i++)
        {
            var v = i * 4;
            var t = i * 6;
            triangles[t] = v;
            triangles[t + 1] = v + 1;
            triangles[t + 2] = v + 2;
            triangles[t + 3] = v + 2;
            triangles[t + 4] = v + 1;
            triangles[t + 5] = v + 3;
        }

        mesh.triangles = triangles;
    }

    private void BuildLine(int count, float oldestTime, float history)
    {
        ClearArrays(_lineVertices, _lineColors);
        var segmentIndex = 0;
        var halfWidth = _graphSize.x * 0.5f;
        var halfHeight = _graphSize.y * 0.5f;
        var bottom = -halfHeight + PlotBottomInset;
        var usableHeight = _graphSize.y - PlotBottomInset - PlotTopInset;
        var baseWidth = _config != null ? _config.TimelineBaseWidth : 4f;
        var forceWidth = _config != null ? _config.TimelineForceWidth : 8f;

        for (var i = 1; i < count && segmentIndex < _maxSegments; i++)
        {
            var a = _sampleScratch[i - 1];
            var b = _sampleScratch[i];
            if (b.Time < oldestTime)
                continue;

            var p0 = SampleToPoint(a, oldestTime, history, halfWidth, bottom, usableHeight);
            var p1 = SampleToPoint(b, oldestTime, history, halfWidth, bottom, usableHeight);
            var direction = p1 - p0;
            if (direction.sqrMagnitude <= 0.0001f)
                continue;

            direction.Normalize();
            var normal = new Vector3(-direction.y, direction.x, 0f);
            var forceT = Mathf.Clamp01(b.ApproxNetForce / _smoothMaxForce);
            var width = baseWidth + forceWidth * forceT;
            var offset = normal * width * 0.5f;
            var vertex = segmentIndex * 4;

            _lineVertices[vertex] = p0 - offset;
            _lineVertices[vertex + 1] = p0 + offset;
            _lineVertices[vertex + 2] = p1 - offset;
            _lineVertices[vertex + 3] = p1 + offset;

            var color = _config != null ? _config.GetDriverColor(b.DominantDriver) : Color.white;
            var ageT = Mathf.Clamp01((b.Time - oldestTime) / history);
            color.a = Mathf.Lerp(0.28f, 0.95f, ageT);
            _lineColors[vertex] = color;
            _lineColors[vertex + 1] = color;
            _lineColors[vertex + 2] = color;
            _lineColors[vertex + 3] = color;
            segmentIndex++;
        }

        ApplyMesh(_lineMesh, _lineVertices, _lineColors);
    }

    private void BuildSpikesAndMarkers(int count, float oldestTime, float history)
    {
        ClearArrays(_spikeVertices, _spikeColors);
        ClearArrays(_markerVertices, _markerColors);

        var spikeIndex = 0;
        var markerIndex = 0;
        var halfWidth = _graphSize.x * 0.5f;
        var halfHeight = _graphSize.y * 0.5f;
        var bottom = -halfHeight + PlotBottomInset;
        var usableHeight = _graphSize.y - PlotBottomInset - PlotTopInset;

        for (var i = 0; i < count; i++)
        {
            var sample = _sampleScratch[i];
            if (sample.Time < oldestTime)
                continue;

            if (sample.HasImpactEvent && spikeIndex < _maxMarkers)
            {
                var t = Mathf.Clamp01((sample.Time - oldestTime) / history);
                var x = Mathf.Lerp(-halfWidth + 12f, halfWidth - 12f, t);
                var height = Mathf.Clamp(sample.ImpactImpulse * 12f, 18f, usableHeight);
                AddRect(_spikeVertices, _spikeColors, spikeIndex++, x - 1.5f, bottom, 3f, height,
                    _config != null ? _config.ImpactColor : Color.red);
            }

            if ((sample.DominantDriver == PhysicsLensDriver.UserForce
                 || sample.DominantDriver == PhysicsLensDriver.Spring
                 || sample.DominantDriver == PhysicsLensDriver.HingeJoint)
                && markerIndex < _maxMarkers)
            {
                var point = SampleToPoint(sample, oldestTime, history, halfWidth, bottom, usableHeight);
                AddDiamond(_markerVertices, _markerColors, markerIndex++, point, 6f,
                    _config != null ? _config.GetDriverColor(sample.DominantDriver) : Color.white);
            }
        }

        ApplyMesh(_spikeMesh, _spikeVertices, _spikeColors);
        ApplyMesh(_markerMesh, _markerVertices, _markerColors);
    }

    private Vector3 SampleToPoint(PhysicsLensSample sample, float oldestTime, float history, float halfWidth, float bottom, float usableHeight)
    {
        var t = Mathf.Clamp01((sample.Time - oldestTime) / Mathf.Max(0.001f, history));
        var x = Mathf.Lerp(-halfWidth + PlotHorizontalInset, halfWidth - PlotHorizontalInset, t);
        var y = bottom + Mathf.Clamp01(sample.Speed / _smoothMaxSpeed) * usableHeight;
        return new Vector3(x, y, 0f);
    }

    private void UpdateHead(PhysicsLensSample sample, float oldestTime, float history)
    {
        if (_head == null)
            return;

        var halfWidth = _graphSize.x * 0.5f;
        var halfHeight = _graphSize.y * 0.5f;
        var point = SampleToPoint(sample, oldestTime, history, halfWidth, -halfHeight + PlotBottomInset, _graphSize.y - PlotBottomInset - PlotTopInset);
        _head.localPosition = new Vector3(point.x, point.y, -13f);
        var pulse = 1f + Mathf.Sin(Time.unscaledTime * 9f) * 0.14f;
        _head.localScale = Vector3.one * 10f * pulse;

        if (_headRenderer != null && _headRenderer.sharedMaterial != null)
            _headRenderer.sharedMaterial.color = _config != null ? _config.GetDriverColor(sample.DominantDriver) : Color.white;
    }

    private void RefreshGrid()
    {
        if (_gridMesh == null || _gridVertices == null)
            return;

        ClearArrays(_gridVertices, _gridColors);
        var color = _config != null ? _config.GraphGrid : new Color(1f, 1f, 1f, 0.3f);
        var halfWidth = _graphSize.x * 0.5f;
        var halfHeight = _graphSize.y * 0.5f;
        var left = -halfWidth + PlotHorizontalInset;
        var right = halfWidth - PlotHorizontalInset;
        var bottom = -halfHeight + PlotBottomInset;
        var top = halfHeight - PlotTopInset;
        var index = 0;

        for (var i = 0; i < 4; i++)
        {
            var y = Mathf.Lerp(bottom, top, i / 3f);
            AddLineRect(_gridVertices, _gridColors, index++, new Vector3(left, y, 0f), new Vector3(right, y, 0f), 1f, color);
        }

        for (var i = 0; i < 5; i++)
        {
            var x = Mathf.Lerp(left, right, i / 4f);
            AddLineRect(_gridVertices, _gridColors, index++, new Vector3(x, bottom, 0f), new Vector3(x, top, 0f), 1f, color);
        }

        ApplyMesh(_gridMesh, _gridVertices, _gridColors);
    }

    private void ClearDynamicMeshes()
    {
        if (_lineVertices != null)
        {
            ClearArrays(_lineVertices, _lineColors);
            ApplyMesh(_lineMesh, _lineVertices, _lineColors);
        }

        if (_spikeVertices != null)
        {
            ClearArrays(_spikeVertices, _spikeColors);
            ApplyMesh(_spikeMesh, _spikeVertices, _spikeColors);
        }

        if (_markerVertices != null)
        {
            ClearArrays(_markerVertices, _markerColors);
            ApplyMesh(_markerMesh, _markerVertices, _markerColors);
        }
    }

    private static void AddRect(Vector3[] vertices, Color[] colors, int quadIndex, float x, float y, float width, float height, Color color)
    {
        var v = quadIndex * 4;
        vertices[v] = new Vector3(x, y, 0f);
        vertices[v + 1] = new Vector3(x + width, y, 0f);
        vertices[v + 2] = new Vector3(x, y + height, 0f);
        vertices[v + 3] = new Vector3(x + width, y + height, 0f);
        colors[v] = color;
        colors[v + 1] = color;
        colors[v + 2] = color;
        colors[v + 3] = color;
    }

    private static void AddDiamond(Vector3[] vertices, Color[] colors, int quadIndex, Vector3 center, float size, Color color)
    {
        var v = quadIndex * 4;
        vertices[v] = center + new Vector3(0f, size, 0f);
        vertices[v + 1] = center + new Vector3(size, 0f, 0f);
        vertices[v + 2] = center + new Vector3(-size, 0f, 0f);
        vertices[v + 3] = center + new Vector3(0f, -size, 0f);
        colors[v] = color;
        colors[v + 1] = color;
        colors[v + 2] = color;
        colors[v + 3] = color;
    }

    private static void AddLineRect(Vector3[] vertices, Color[] colors, int quadIndex, Vector3 a, Vector3 b, float width, Color color)
    {
        var direction = b - a;
        if (direction.sqrMagnitude <= 0.0001f)
            return;
        direction.Normalize();
        var normal = new Vector3(-direction.y, direction.x, 0f) * width * 0.5f;
        var v = quadIndex * 4;
        vertices[v] = a - normal;
        vertices[v + 1] = a + normal;
        vertices[v + 2] = b - normal;
        vertices[v + 3] = b + normal;
        colors[v] = color;
        colors[v + 1] = color;
        colors[v + 2] = color;
        colors[v + 3] = color;
    }

    private static void ApplyMesh(Mesh mesh, Vector3[] vertices, Color[] colors)
    {
        if (mesh == null)
            return;
        mesh.vertices = vertices;
        mesh.colors = colors;
        mesh.RecalculateBounds();
    }

    private static void ClearArrays(Vector3[] vertices, Color[] colors)
    {
        if (vertices == null || colors == null)
            return;

        var clear = new Color(1f, 1f, 1f, 0f);
        for (var i = 0; i < vertices.Length; i++)
        {
            vertices[i] = Vector3.zero;
            colors[i] = clear;
        }
    }

    private static void SetLabelRect(Text text, Vector2 anchoredPosition, Vector2 size, Vector2 pivot)
    {
        if (text == null)
            return;

        var rect = text.rectTransform;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = pivot;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;
    }
}
