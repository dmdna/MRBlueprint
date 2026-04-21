using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public sealed class MeshSketchFeedback : MonoBehaviour
{
    [SerializeField] private float lineWidth = 0.006f;
    [SerializeField] private float highlightDiameter = 0.04f;
    [SerializeField] private Color ghostColor = new(0.2f, 0.9f, 1f, 0.9f);
    [SerializeField] private Color snapColor = new(1f, 0.85f, 0.2f, 0.96f);
    [SerializeField] private Color closureColor = new(0.3f, 1f, 0.45f, 0.96f);
    [SerializeField] private Color invalidColor = new(1f, 0.18f, 0.14f, 0.95f);
    [SerializeField] private Color previewFaceColor = new(0.2f, 0.85f, 1f, 0.24f);

    private LineRenderer _ghostLine;
    private LineRenderer _edgeLine;
    private MeshRenderer _snapRenderer;
    private MeshRenderer _closureRenderer;
    private MeshFilter _previewFilter;
    private MeshRenderer _previewRenderer;
    private Mesh _previewMesh;
    private Mesh _sphereMesh;
    private readonly List<Vector3> _previewVertices = new();
    private Material _ghostMaterial;
    private Material _snapMaterial;
    private Material _closureMaterial;
    private Material _invalidMaterial;
    private Material _previewMaterial;
    private float _invalidUntil;

    private void Awake()
    {
        EnsureVisuals();
        HideAll();
    }

    private void OnDestroy()
    {
        DestroyMaterial(_ghostMaterial);
        DestroyMaterial(_snapMaterial);
        DestroyMaterial(_closureMaterial);
        DestroyMaterial(_invalidMaterial);
        DestroyMaterial(_previewMaterial);
        if (_previewMesh != null)
        {
            Destroy(_previewMesh);
        }

        if (_sphereMesh != null)
        {
            Destroy(_sphereMesh);
        }
    }

    private void Update()
    {
        if (_invalidUntil <= 0f || Time.time < _invalidUntil || _ghostLine == null || _ghostLine.enabled)
        {
            return;
        }

        if (_snapRenderer != null)
        {
            _snapRenderer.enabled = false;
        }

        _invalidUntil = 0f;
    }

    public void Render(
        IReadOnlyList<Vector3> points,
        MeshSketchSnapResult snap,
        bool closurePreview,
        MeshSketchResolveResult facePreview)
    {
        EnsureVisuals();
        var invalidActive = Time.time < _invalidUntil;
        var lineMaterial = invalidActive ? _invalidMaterial : _ghostMaterial;
        _ghostLine.sharedMaterial = lineMaterial;
        _ghostLine.startColor = invalidActive ? invalidColor : ghostColor;
        _ghostLine.endColor = invalidActive ? invalidColor : ghostColor;
        _ghostLine.startWidth = lineWidth;
        _ghostLine.endWidth = lineWidth;
        _ghostLine.positionCount = points != null ? points.Count : 0;
        if (points != null)
        {
            for (var i = 0; i < points.Count; i++)
            {
                _ghostLine.SetPosition(i, points[i]);
            }
        }

        _ghostLine.enabled = _ghostLine.positionCount > 0;

        RenderSnap(snap);
        RenderClosure(points, closurePreview);
        RenderPreviewFace(facePreview);
    }

    public void ShowInvalid(Vector3 position)
    {
        EnsureVisuals();
        _invalidUntil = Time.time + 0.35f;
        _snapRenderer.sharedMaterial = _invalidMaterial;
        _snapRenderer.transform.position = position;
        _snapRenderer.transform.localScale = Vector3.one * highlightDiameter;
        _snapRenderer.enabled = true;
    }

    public void HideAll()
    {
        EnsureVisuals();
        _ghostLine.enabled = false;
        _edgeLine.enabled = false;
        _snapRenderer.enabled = false;
        _closureRenderer.enabled = false;
        _previewRenderer.enabled = false;
    }

    private void RenderSnap(MeshSketchSnapResult snap)
    {
        var hasSnap = snap.HasSnap && snap.Kind != MeshSketchSnapKind.Closure;
        _snapRenderer.enabled = hasSnap;
        _edgeLine.enabled = hasSnap && snap.Kind == MeshSketchSnapKind.Edge;
        if (!hasSnap)
        {
            return;
        }

        _snapRenderer.sharedMaterial = _snapMaterial;
        _snapRenderer.transform.position = snap.Point;
        _snapRenderer.transform.localScale = Vector3.one * highlightDiameter;

        if (snap.Kind == MeshSketchSnapKind.Edge)
        {
            _edgeLine.sharedMaterial = _snapMaterial;
            _edgeLine.startColor = snapColor;
            _edgeLine.endColor = snapColor;
            _edgeLine.startWidth = lineWidth * 1.8f;
            _edgeLine.endWidth = lineWidth * 1.8f;
            _edgeLine.positionCount = 2;
            _edgeLine.SetPosition(0, snap.EdgeStart);
            _edgeLine.SetPosition(1, snap.EdgeEnd);
        }
    }

    private void RenderClosure(IReadOnlyList<Vector3> points, bool closurePreview)
    {
        _closureRenderer.enabled = closurePreview && points != null && points.Count > 0;
        if (!_closureRenderer.enabled)
        {
            return;
        }

        _closureRenderer.transform.position = points[0];
        _closureRenderer.transform.localScale = Vector3.one * highlightDiameter * 1.2f;
    }

    private void RenderPreviewFace(MeshSketchResolveResult facePreview)
    {
        var visible = facePreview != null && facePreview.IsValidClosedLoop && facePreview.Triangles.Count >= 3;
        _previewRenderer.enabled = visible;
        if (!visible)
        {
            return;
        }

        if (_previewMesh == null)
        {
            _previewMesh = new Mesh
            {
                name = "MXInkFacePreview"
            };
        }

        _previewMesh.Clear();
        _previewVertices.Clear();
        for (var i = 0; i < facePreview.Points.Count; i++)
        {
            _previewVertices.Add(transform.InverseTransformPoint(facePreview.Points[i]));
        }

        _previewMesh.SetVertices(_previewVertices);
        _previewMesh.SetTriangles(facePreview.Triangles, 0);
        _previewMesh.RecalculateBounds();
        _previewMesh.RecalculateNormals();
        _previewFilter.sharedMesh = _previewMesh;
    }

    private void EnsureVisuals()
    {
        if (_ghostLine != null)
        {
            return;
        }

        _ghostMaterial = CreateMaterial("MXInkMeshGhost", ghostColor);
        _snapMaterial = CreateMaterial("MXInkMeshSnap", snapColor);
        _closureMaterial = CreateMaterial("MXInkMeshClosure", closureColor);
        _invalidMaterial = CreateMaterial("MXInkMeshInvalid", invalidColor);
        _previewMaterial = CreateMaterial("MXInkMeshFacePreview", previewFaceColor);
        _sphereMesh = BuildSphereMesh(18);

        _ghostLine = CreateLine("GhostPolyline", _ghostMaterial);
        _edgeLine = CreateLine("SnapEdge", _snapMaterial);

        _snapRenderer = CreateSphere("SnapHighlight", _snapMaterial);
        _closureRenderer = CreateSphere("ClosureHighlight", _closureMaterial);

        var preview = new GameObject("FacePreview");
        preview.transform.SetParent(transform, false);
        _previewFilter = preview.AddComponent<MeshFilter>();
        _previewRenderer = preview.AddComponent<MeshRenderer>();
        _previewRenderer.sharedMaterial = _previewMaterial;
        _previewRenderer.shadowCastingMode = ShadowCastingMode.Off;
        _previewRenderer.receiveShadows = false;
    }

    private LineRenderer CreateLine(string objectName, Material material)
    {
        var go = new GameObject(objectName);
        go.transform.SetParent(transform, false);
        var line = go.AddComponent<LineRenderer>();
        line.sharedMaterial = material;
        line.useWorldSpace = true;
        line.alignment = LineAlignment.View;
        line.textureMode = LineTextureMode.Stretch;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.positionCount = 0;
        return line;
    }

    private MeshRenderer CreateSphere(string objectName, Material material)
    {
        var go = new GameObject(objectName);
        go.transform.SetParent(transform, false);
        var filter = go.AddComponent<MeshFilter>();
        filter.sharedMesh = _sphereMesh;
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return renderer;
    }

    private static Material CreateMaterial(string materialName, Color color)
    {
        var shader = Shader.Find("MRBlueprint/RayNoStackTransparent")
                     ?? Shader.Find("Universal Render Pipeline/Unlit")
                     ?? Shader.Find("Sprites/Default")
                     ?? Shader.Find("Unlit/Color")
                     ?? Shader.Find("Standard");
        if (shader == null)
        {
            return null;
        }

        var material = new Material(shader)
        {
            name = materialName,
            color = color,
            enableInstancing = true
        };

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }

        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }

        if (material.HasProperty("_Surface"))
        {
            material.SetFloat("_Surface", 1f);
        }

        if (material.HasProperty("_Blend"))
        {
            material.SetFloat("_Blend", 0f);
        }

        if (material.HasProperty("_Cull"))
        {
            material.SetFloat("_Cull", (float)CullMode.Off);
        }

        material.SetOverrideTag("RenderType", "Transparent");
        material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        material.SetInt("_ZWrite", 0);
        material.EnableKeyword("_ALPHABLEND_ON");
        material.renderQueue = (int)RenderQueue.Transparent + 20;
        return material;
    }

    private static Mesh BuildSphereMesh(int segments)
    {
        var longitude = Mathf.Max(8, segments);
        var latitude = Mathf.Max(4, segments / 2);
        var ringCount = latitude - 1;
        var vertices = new Vector3[2 + ringCount * longitude];
        var triangles = new int[longitude * (2 + Mathf.Max(0, ringCount - 1) * 2) * 3];

        vertices[0] = Vector3.up * 0.5f;
        var vertexIndex = 1;
        for (var lat = 1; lat < latitude; lat++)
        {
            var theta = Mathf.PI * lat / latitude;
            var y = Mathf.Cos(theta) * 0.5f;
            var radius = Mathf.Sin(theta) * 0.5f;
            for (var lon = 0; lon < longitude; lon++)
            {
                var phi = Mathf.PI * 2f * lon / longitude;
                vertices[vertexIndex++] = new Vector3(
                    Mathf.Cos(phi) * radius,
                    y,
                    Mathf.Sin(phi) * radius);
            }
        }

        var bottomIndex = vertices.Length - 1;
        vertices[bottomIndex] = Vector3.down * 0.5f;
        var triangleIndex = 0;
        for (var lon = 0; lon < longitude; lon++)
        {
            var next = (lon + 1) % longitude;
            triangles[triangleIndex++] = 0;
            triangles[triangleIndex++] = 1 + next;
            triangles[triangleIndex++] = 1 + lon;
        }

        for (var ring = 0; ring < ringCount - 1; ring++)
        {
            var currentRing = 1 + ring * longitude;
            var nextRing = currentRing + longitude;
            for (var lon = 0; lon < longitude; lon++)
            {
                var next = (lon + 1) % longitude;
                triangles[triangleIndex++] = currentRing + lon;
                triangles[triangleIndex++] = nextRing + next;
                triangles[triangleIndex++] = nextRing + lon;
                triangles[triangleIndex++] = currentRing + lon;
                triangles[triangleIndex++] = currentRing + next;
                triangles[triangleIndex++] = nextRing + next;
            }
        }

        var lastRing = 1 + (ringCount - 1) * longitude;
        for (var lon = 0; lon < longitude; lon++)
        {
            var next = (lon + 1) % longitude;
            triangles[triangleIndex++] = bottomIndex;
            triangles[triangleIndex++] = lastRing + lon;
            triangles[triangleIndex++] = lastRing + next;
        }

        var mesh = new Mesh
        {
            name = "MXInkFeedbackSphere"
        };
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
        return mesh;
    }

    private static void DestroyMaterial(Material material)
    {
        if (material != null)
        {
            Destroy(material);
        }
    }
}
