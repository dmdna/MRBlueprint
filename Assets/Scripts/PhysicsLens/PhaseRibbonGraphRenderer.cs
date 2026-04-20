using UnityEngine;
using UnityEngine.UI;

public sealed class PhaseRibbonGraphRenderer : MonoBehaviour
{
    private PhysicsLensConfig _config;
    private RectTransform _root;
    private PhysicsLensGraphMode _mode = PhysicsLensGraphMode.SpringPhaseRibbon;
    private PhysicsLensSample[] _sampleScratch;
    private Mesh _ribbonMesh;
    private Mesh _axisMesh;
    private Mesh _limitMesh;
    private Vector3[] _ribbonVertices;
    private Color[] _ribbonColors;
    private Vector3[] _axisVertices;
    private Color[] _axisColors;
    private Vector3[] _limitVertices;
    private Color[] _limitColors;
    private Transform _head;
    private MeshRenderer _headRenderer;
    private Text _modeLabel;
    private Text _xLabel;
    private Text _yLabel;
    private Text _zLabel;
    private Text _scaleLabel;
    private Vector2 _graphSize;
    private float _smoothXRange = 1f;
    private float _smoothYRange = 1f;
    private int _maxSegments;

    public void Initialize(RectTransform root, PhysicsLensConfig config, Font font)
    {
        _root = root;
        _config = config;
        _graphSize = config != null ? config.CompactGraphSize : new Vector2(374f, 176f);
        var maxSamples = config != null ? config.MaxTelemetrySamples : 512;
        _sampleScratch = new PhysicsLensSample[maxSamples];
        _maxSegments = Mathf.Max(1, maxSamples - 1);
        BuildMeshes();
        BuildLabels(font);
        SetSize(_graphSize);
    }

    public void SetMode(PhysicsLensGraphMode mode)
    {
        _mode = mode == PhysicsLensGraphMode.HingePhaseRibbon
            ? PhysicsLensGraphMode.HingePhaseRibbon
            : PhysicsLensGraphMode.SpringPhaseRibbon;
        RefreshLabels();
    }

    public void SetSize(Vector2 size)
    {
        _graphSize = size;
        if (_root != null)
            _root.sizeDelta = size;
        RefreshAxes();
    }

    public void Render(PhysicsTelemetryTracker tracker, PhysicsLensConstraintSummary constraint)
    {
        if (tracker == null || !tracker.HasSamples)
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
        var targetX = 0.05f;
        var targetY = 0.05f;

        for (var i = 0; i < count; i++)
        {
            var sample = _sampleScratch[i];
            if (sample.Time < oldestTime)
                continue;

            var x = ResolveX(sample);
            var y = ResolveY(sample);
            if (Mathf.Abs(x) > targetX)
                targetX = Mathf.Abs(x);
            if (Mathf.Abs(y) > targetY)
                targetY = Mathf.Abs(y);
        }

        targetX *= 1.18f;
        targetY *= 1.18f;
        var sharpness = _config != null ? _config.GraphAutoscaleSharpness : 5f;
        var blend = 1f - Mathf.Exp(-sharpness * Time.unscaledDeltaTime);
        _smoothXRange = Mathf.Lerp(_smoothXRange, targetX, blend);
        _smoothYRange = Mathf.Lerp(_smoothYRange, targetY, blend);
        _smoothXRange = Mathf.Max(0.01f, _smoothXRange);
        _smoothYRange = Mathf.Max(0.01f, _smoothYRange);

        BuildRibbon(count, oldestTime, history);
        BuildLimitPlanes(constraint);
        UpdateHead(_sampleScratch[count - 1], oldestTime, history);

        if (_scaleLabel != null)
            _scaleLabel.text = "range " + _smoothXRange.ToString(_mode == PhysicsLensGraphMode.HingePhaseRibbon ? "0" : "0.00");
    }

    private void BuildMeshes()
    {
        var material = PhysicsLensRenderUtility.CreateVertexColorMaterial("PhysicsLensPhaseRibbonMaterial");
        CreateMeshObject("PhaseAxes", material, out _axisMesh);
        CreateMeshObject("PhaseRibbon", material, out _ribbonMesh);
        CreateMeshObject("PhaseLimitPlanes", material, out _limitMesh);

        _ribbonVertices = new Vector3[_maxSegments * 4];
        _ribbonColors = new Color[_ribbonVertices.Length];
        _axisVertices = new Vector3[16 * 4];
        _axisColors = new Color[_axisVertices.Length];
        _limitVertices = new Vector3[2 * 4];
        _limitColors = new Color[_limitVertices.Length];
        AssignQuadTriangles(_ribbonMesh, _maxSegments);
        AssignQuadTriangles(_axisMesh, 16);
        AssignQuadTriangles(_limitMesh, 2);

        var headGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        headGo.name = "PhaseCurrentHead";
        headGo.transform.SetParent(transform, false);
        headGo.transform.localPosition = new Vector3(0f, 0f, -12f);
        headGo.transform.localScale = Vector3.one * 10f;
        var collider = headGo.GetComponent<Collider>();
        if (collider != null)
            Destroy(collider);
        _head = headGo.transform;
        _headRenderer = headGo.GetComponent<MeshRenderer>();
        if (_headRenderer != null)
            _headRenderer.sharedMaterial = PhysicsLensRenderUtility.CreateTintMaterial("PhysicsLensPhaseHead", Color.white);
    }

    private void BuildLabels(Font font)
    {
        var secondary = _config != null ? _config.TextSecondary : Color.gray;
        _modeLabel = PhysicsLensRenderUtility.CreateText(transform, "PhaseModeLabel", font, 18, TextAnchor.UpperLeft,
            secondary, new Vector2(-150f, 78f), new Vector2(210f, 24f));
        _xLabel = PhysicsLensRenderUtility.CreateText(transform, "PhaseXLabel", font, 14, TextAnchor.LowerLeft,
            secondary, new Vector2(-164f, -90f), new Vector2(160f, 22f));
        _yLabel = PhysicsLensRenderUtility.CreateText(transform, "PhaseYLabel", font, 14, TextAnchor.UpperLeft,
            secondary, new Vector2(-166f, 53f), new Vector2(170f, 22f));
        _zLabel = PhysicsLensRenderUtility.CreateText(transform, "PhaseZLabel", font, 14, TextAnchor.LowerRight,
            secondary, new Vector2(145f, -90f), new Vector2(160f, 22f));
        _scaleLabel = PhysicsLensRenderUtility.CreateText(transform, "PhaseScaleLabel", font, 14, TextAnchor.UpperRight,
            secondary, new Vector2(150f, 78f), new Vector2(140f, 22f));
        RefreshLabels();
    }

    private void RefreshLabels()
    {
        if (_mode == PhysicsLensGraphMode.HingePhaseRibbon)
        {
            if (_modeLabel != null)
                _modeLabel.text = "Hinge Phase Ribbon";
            if (_xLabel != null)
                _xLabel.text = "angle";
            if (_yLabel != null)
                _yLabel.text = "angular speed";
        }
        else
        {
            if (_modeLabel != null)
                _modeLabel.text = "Spring Phase Ribbon";
            if (_xLabel != null)
                _xLabel.text = "extension";
            if (_yLabel != null)
                _yLabel.text = "relative speed";
        }

        if (_zLabel != null)
            _zLabel.text = "time";
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

    private void BuildRibbon(int count, float oldestTime, float history)
    {
        ClearArrays(_ribbonVertices, _ribbonColors);
        var segmentIndex = 0;

        for (var i = 1; i < count && segmentIndex < _maxSegments; i++)
        {
            var previous = _sampleScratch[i - 1];
            var current = _sampleScratch[i];
            if (current.Time < oldestTime)
                continue;

            var p0 = SampleToPoint(previous, oldestTime, history);
            var p1 = SampleToPoint(current, oldestTime, history);
            var direction = p1 - p0;
            if (direction.sqrMagnitude <= 0.0001f)
                continue;

            direction.Normalize();
            var normal = Vector3.Cross(direction, Vector3.back);
            if (normal.sqrMagnitude <= 0.0001f)
                normal = Vector3.up;
            normal.Normalize();

            var width = _config != null ? _config.PhaseRibbonWidth : 5.5f;
            var offset = normal * width * 0.5f;
            var vertex = segmentIndex * 4;
            _ribbonVertices[vertex] = p0 - offset;
            _ribbonVertices[vertex + 1] = p0 + offset;
            _ribbonVertices[vertex + 2] = p1 - offset;
            _ribbonVertices[vertex + 3] = p1 + offset;

            var color = ResolveColor(current);
            var ageT = Mathf.Clamp01((current.Time - oldestTime) / history);
            color.a = Mathf.Lerp(0.24f, 0.98f, ageT);
            _ribbonColors[vertex] = color;
            _ribbonColors[vertex + 1] = color;
            _ribbonColors[vertex + 2] = color;
            _ribbonColors[vertex + 3] = color;
            segmentIndex++;
        }

        ApplyMesh(_ribbonMesh, _ribbonVertices, _ribbonColors);
    }

    private void BuildLimitPlanes(PhysicsLensConstraintSummary constraint)
    {
        ClearArrays(_limitVertices, _limitColors);

        if (_mode != PhysicsLensGraphMode.HingePhaseRibbon
            || !constraint.IsValid
            || !constraint.HasHingeLimits)
        {
            ApplyMesh(_limitMesh, _limitVertices, _limitColors);
            return;
        }

        var warningColor = _config != null ? _config.ImpactColor : Color.red;
        warningColor.a = 0.18f;
        AddLimitPlane(0, constraint.HingeMinLimit, warningColor);
        AddLimitPlane(1, constraint.HingeMaxLimit, warningColor);
        ApplyMesh(_limitMesh, _limitVertices, _limitColors);
    }

    private void AddLimitPlane(int quadIndex, float angle, Color color)
    {
        var halfWidth = _graphSize.x * 0.5f - 24f;
        var halfHeight = _graphSize.y * 0.5f - 28f;
        var depth = _config != null ? _config.PhaseDepth : 72f;
        var x = Mathf.Clamp(angle / Mathf.Max(0.001f, _smoothXRange), -1f, 1f) * halfWidth;
        var zOld = depth * 0.5f;
        var zNow = -depth * 0.5f;
        var v = quadIndex * 4;
        _limitVertices[v] = new Vector3(x, -halfHeight, zOld);
        _limitVertices[v + 1] = new Vector3(x, halfHeight, zOld);
        _limitVertices[v + 2] = new Vector3(x, -halfHeight, zNow);
        _limitVertices[v + 3] = new Vector3(x, halfHeight, zNow);
        _limitColors[v] = color;
        _limitColors[v + 1] = color;
        _limitColors[v + 2] = color;
        _limitColors[v + 3] = color;
    }

    private void UpdateHead(PhysicsLensSample sample, float oldestTime, float history)
    {
        if (_head == null)
            return;

        var point = SampleToPoint(sample, oldestTime, history);
        _head.localPosition = point + new Vector3(0f, 0f, -5f);
        var pulse = 1f + Mathf.Sin(Time.unscaledTime * 8.5f) * 0.14f;
        _head.localScale = Vector3.one * 10f * pulse;

        if (_headRenderer != null && _headRenderer.sharedMaterial != null)
            _headRenderer.sharedMaterial.color = ResolveColor(sample);
    }

    private Vector3 SampleToPoint(PhysicsLensSample sample, float oldestTime, float history)
    {
        var halfWidth = _graphSize.x * 0.5f - 24f;
        var halfHeight = _graphSize.y * 0.5f - 28f;
        var depth = _config != null ? _config.PhaseDepth : 72f;
        var x = Mathf.Clamp(ResolveX(sample) / Mathf.Max(0.001f, _smoothXRange), -1f, 1f) * halfWidth;
        var y = Mathf.Clamp(ResolveY(sample) / Mathf.Max(0.001f, _smoothYRange), -1f, 1f) * halfHeight;
        var t = Mathf.Clamp01((sample.Time - oldestTime) / Mathf.Max(0.001f, history));
        var z = Mathf.Lerp(depth * 0.5f, -depth * 0.5f, t);
        return new Vector3(x, y, z);
    }

    private float ResolveX(PhysicsLensSample sample)
    {
        return _mode == PhysicsLensGraphMode.HingePhaseRibbon ? sample.HingeAngle : sample.SpringExtension;
    }

    private float ResolveY(PhysicsLensSample sample)
    {
        return _mode == PhysicsLensGraphMode.HingePhaseRibbon ? sample.HingeAngularVelocity : sample.SpringRelativeSpeed;
    }

    private Color ResolveColor(PhysicsLensSample sample)
    {
        if (_config == null)
            return Color.white;

        if (_mode == PhysicsLensGraphMode.HingePhaseRibbon)
        {
            var t = Mathf.Clamp01(Mathf.Max(sample.HingeLimitProximity, sample.HingeTorque * 0.12f));
            return Color.Lerp(_config.HingeColor, _config.ImpactColor, t);
        }

        var x = sample.SpringExtension;
        if (x < -0.005f)
            return Color.Lerp(_config.SpringRestColor, _config.SpringCompressionColor, Mathf.Clamp01(Mathf.Abs(x) / _smoothXRange));
        if (x > 0.005f)
            return Color.Lerp(_config.SpringRestColor, _config.SpringTensionColor, Mathf.Clamp01(Mathf.Abs(x) / _smoothXRange));
        return _config.SpringRestColor;
    }

    private void RefreshAxes()
    {
        if (_axisMesh == null || _axisVertices == null)
            return;

        ClearArrays(_axisVertices, _axisColors);
        var color = _config != null ? _config.GraphGrid : new Color(1f, 1f, 1f, 0.4f);
        var accent = _config != null ? _config.PanelAccent : Color.cyan;
        var halfWidth = _graphSize.x * 0.5f - 24f;
        var halfHeight = _graphSize.y * 0.5f - 28f;
        var depth = _config != null ? _config.PhaseDepth : 72f;
        var index = 0;

        AddLineRect(_axisVertices, _axisColors, index++, new Vector3(-halfWidth, 0f, 0f), new Vector3(halfWidth, 0f, 0f), 1.5f, accent);
        AddLineRect(_axisVertices, _axisColors, index++, new Vector3(0f, -halfHeight, 0f), new Vector3(0f, halfHeight, 0f), 1.5f, accent);
        AddLineRect(_axisVertices, _axisColors, index++, new Vector3(0f, 0f, depth * 0.5f), new Vector3(0f, 0f, -depth * 0.5f), 1.5f, accent);

        for (var i = 0; i < 4; i++)
        {
            var x = Mathf.Lerp(-halfWidth, halfWidth, i / 3f);
            AddLineRect(_axisVertices, _axisColors, index++, new Vector3(x, -halfHeight, depth * 0.5f),
                new Vector3(x, halfHeight, depth * 0.5f), 0.7f, color);
        }

        for (var i = 0; i < 3; i++)
        {
            var y = Mathf.Lerp(-halfHeight, halfHeight, i / 2f);
            AddLineRect(_axisVertices, _axisColors, index++, new Vector3(-halfWidth, y, depth * 0.5f),
                new Vector3(halfWidth, y, depth * 0.5f), 0.7f, color);
        }

        ApplyMesh(_axisMesh, _axisVertices, _axisColors);
    }

    private void ClearDynamicMeshes()
    {
        if (_ribbonVertices != null)
        {
            ClearArrays(_ribbonVertices, _ribbonColors);
            ApplyMesh(_ribbonMesh, _ribbonVertices, _ribbonColors);
        }

        if (_limitVertices != null)
        {
            ClearArrays(_limitVertices, _limitColors);
            ApplyMesh(_limitMesh, _limitVertices, _limitColors);
        }
    }

    private static void AddLineRect(Vector3[] vertices, Color[] colors, int quadIndex, Vector3 a, Vector3 b, float width, Color color)
    {
        var direction = b - a;
        if (direction.sqrMagnitude <= 0.0001f)
            return;
        direction.Normalize();
        var normal = Vector3.Cross(direction, Vector3.back);
        if (normal.sqrMagnitude <= 0.0001f)
            normal = Vector3.up;
        normal.Normalize();
        normal *= width * 0.5f;

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
}
