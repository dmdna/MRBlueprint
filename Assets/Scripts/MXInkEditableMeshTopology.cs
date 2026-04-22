using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[DisallowMultipleComponent]
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public sealed class MXInkEditableMeshTopology : MonoBehaviour
{
    private static readonly List<MXInkEditableMeshTopology> ActiveTopologies = new();

    [SerializeField] private List<Vector3> vertices = new();
    [SerializeField] private List<MXInkTopologyFace> faces = new();
    [SerializeField] private List<MXInkTopologyEdge> constructionEdges = new();
    [SerializeField] private MeshFilter meshFilter;
    [SerializeField] private MeshRenderer meshRenderer;
    [SerializeField] private MeshCollider meshCollider;
    [SerializeField] private Material runtimeMaterial;
    [SerializeField] private float constructionEdgeWidth = 0.006f;

    private bool _ownsRuntimeMaterial;
    private readonly List<MXInkTopologyEdge> _edges = new();
    private readonly List<LineRenderer> _constructionLineRenderers = new();
    private readonly HashSet<ulong> _edgeKeys = new();
    private Mesh _runtimeMesh;
    private Transform _constructionEdgeRoot;

    public static IReadOnlyList<MXInkEditableMeshTopology> Active => ActiveTopologies;
    public int VertexCount => vertices.Count;
    public bool HasRenderableFaces => CountRenderableTriangles() > 0;
    public int EdgeCount
    {
        get
        {
            EnsureEdgeCache();
            return _edges.Count;
        }
    }

    private void Awake()
    {
        EnsureComponents();
        RebuildMesh();
    }

    private void OnEnable()
    {
        if (!ActiveTopologies.Contains(this))
        {
            ActiveTopologies.Add(this);
        }
    }

    private void OnDisable()
    {
        ActiveTopologies.Remove(this);
    }

    private void OnDestroy()
    {
        ActiveTopologies.Remove(this);
        if (_runtimeMesh != null)
        {
            Destroy(_runtimeMesh);
        }

        if (_ownsRuntimeMaterial && runtimeMaterial != null)
        {
            Destroy(runtimeMaterial);
        }
    }

    public static MXInkEditableMeshTopology CreateNew(Vector3 origin, Material material)
    {
        var go = new GameObject("MXInk Authored Mesh");
        go.transform.position = origin;
        go.transform.rotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        var filter = go.AddComponent<MeshFilter>();
        var renderer = go.AddComponent<MeshRenderer>();
        var collider = go.AddComponent<MeshCollider>();
        collider.convex = true;
        var rb = go.AddComponent<Rigidbody>();
        rb.mass = 1f;
        rb.useGravity = true;

        var topology = go.AddComponent<MXInkEditableMeshTopology>();
        topology.meshFilter = filter;
        topology.meshRenderer = renderer;
        topology.meshCollider = collider;
        var awakeCreatedMaterial = topology.runtimeMaterial;
        var awakeOwnedMaterial = topology._ownsRuntimeMaterial;
        topology.runtimeMaterial = material != null ? material : awakeCreatedMaterial != null ? awakeCreatedMaterial : CreateDefaultMaterial();
        topology._ownsRuntimeMaterial = material == null && topology.runtimeMaterial != null;
        if (material != null && awakeOwnedMaterial && awakeCreatedMaterial != null)
        {
            Destroy(awakeCreatedMaterial);
        }

        renderer.sharedMaterial = topology.runtimeMaterial;
        topology.EnsureComponents();

        var placeable = go.AddComponent<PlaceableAsset>();
        placeable.SetAssetDisplayNameForRuntime("Authored Mesh");
        go.AddComponent<SelectableAsset>();
        go.AddComponent<XRGrabInteractable>();
        go.AddComponent<PlaceableXRGrabBridge>();
        go.AddComponent<CollisionEventCache>();
        go.AddComponent<PhysicsLensForceEventCache>();
        return topology;
    }

    public static int DeleteInvalidTopologies()
    {
        var deletedCount = 0;
        for (var i = ActiveTopologies.Count - 1; i >= 0; i--)
        {
            var topology = ActiveTopologies[i];
            if (topology == null)
            {
                ActiveTopologies.RemoveAt(i);
                continue;
            }

            if (topology.HasRenderableFaces)
            {
                continue;
            }

            ActiveTopologies.RemoveAt(i);
            MXInkMeshUndoIntegration.DiscardTopologyRecords(topology);
            if (AssetSelectionManager.Instance != null
                && AssetSelectionManager.Instance.SelectedAsset == topology.GetComponent<PlaceableAsset>())
            {
                AssetSelectionManager.Instance.ClearSelection();
            }

            deletedCount++;
            if (Application.isPlaying)
            {
                Destroy(topology.gameObject);
            }
            else
            {
                DestroyImmediate(topology.gameObject);
            }
        }

        return deletedCount;
    }

    public Vector3 GetVertexWorld(int index)
    {
        if (index < 0 || index >= vertices.Count)
        {
            return transform.position;
        }

        return transform.TransformPoint(vertices[index]);
    }

    public bool TryGetEdgeWorld(int edgeIndex, out Vector3 a, out Vector3 b)
    {
        EnsureEdgeCache();
        if (edgeIndex < 0 || edgeIndex >= _edges.Count)
        {
            a = default;
            b = default;
            return false;
        }

        var edge = _edges[edgeIndex];
        if (edge.A < 0 || edge.A >= vertices.Count || edge.B < 0 || edge.B >= vertices.Count)
        {
            a = default;
            b = default;
            return false;
        }

        a = transform.TransformPoint(vertices[edge.A]);
        b = transform.TransformPoint(vertices[edge.B]);
        return true;
    }

    public MXInkTopologySnapshot CaptureSnapshot()
    {
        return new MXInkTopologySnapshot(vertices, faces, constructionEdges);
    }

    public void RestoreSnapshot(MXInkTopologySnapshot snapshot)
    {
        if (snapshot == null)
        {
            return;
        }

        vertices.Clear();
        vertices.AddRange(snapshot.Vertices);

        faces.Clear();
        for (var i = 0; i < snapshot.Faces.Count; i++)
        {
            faces.Add(snapshot.Faces[i].Clone());
        }

        constructionEdges.Clear();
        constructionEdges.AddRange(snapshot.ConstructionEdges);
        RebuildMesh();
    }

    public bool AddFaceWorld(
        IReadOnlyList<Vector3> worldLoop,
        IReadOnlyList<int> localTriangles,
        float weldRadius)
    {
        if (worldLoop == null || worldLoop.Count < 3 || localTriangles == null || localTriangles.Count < 3)
        {
            return false;
        }

        var face = new MXInkTopologyFace();
        for (var i = 0; i < worldLoop.Count; i++)
        {
            face.VertexIndices.Add(FindOrAddVertexWorld(worldLoop[i], weldRadius));
        }

        for (var i = 0; i < localTriangles.Count; i++)
        {
            var localIndex = localTriangles[i];
            if (localIndex < 0 || localIndex >= face.VertexIndices.Count)
            {
                return false;
            }

            face.TriangleIndices.Add(face.VertexIndices[localIndex]);
        }

        faces.Add(face);
        RebuildMesh();
        return true;
    }

    public bool AddConstructionEdgesWorld(IReadOnlyList<Vector3> worldChain, float weldRadius)
    {
        if (worldChain == null || worldChain.Count < 2)
        {
            return false;
        }

        var previous = FindOrAddVertexWorld(worldChain[0], weldRadius);
        for (var i = 1; i < worldChain.Count; i++)
        {
            var current = FindOrAddVertexWorld(worldChain[i], weldRadius);
            if (current != previous)
            {
                constructionEdges.Add(new MXInkTopologyEdge(previous, current));
            }

            previous = current;
        }

        RebuildMesh();
        return true;
    }

    public void RebuildMesh()
    {
        EnsureComponents();
        if (_runtimeMesh == null)
        {
            _runtimeMesh = new Mesh
            {
                name = "MXInkEditableMesh"
            };
        }

        var triangles = new List<int>();
        for (var f = 0; f < faces.Count; f++)
        {
            var face = faces[f];
            if (face == null || face.TriangleIndices == null)
            {
                continue;
            }

            for (var i = 0; i < face.TriangleIndices.Count; i++)
            {
                var index = face.TriangleIndices[i];
                if (index >= 0 && index < vertices.Count)
                {
                    triangles.Add(index);
                }
            }
        }

        _runtimeMesh.Clear();
        _runtimeMesh.indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        _runtimeMesh.SetVertices(vertices);
        _runtimeMesh.SetTriangles(triangles, 0);
        _runtimeMesh.RecalculateBounds();
        _runtimeMesh.RecalculateNormals();
        if (triangles.Count > 0)
        {
            _runtimeMesh.RecalculateTangents();
        }

        meshFilter.sharedMesh = _runtimeMesh;
        if (meshRenderer.sharedMaterial == null)
        {
            meshRenderer.sharedMaterial = runtimeMaterial != null ? runtimeMaterial : CreateDefaultMaterial();
        }

        if (meshCollider != null)
        {
            meshCollider.convex = true;
            meshCollider.sharedMesh = null;
            meshCollider.sharedMesh = triangles.Count > 0 ? _runtimeMesh : null;
        }

        RebuildEdgeCache();
        RebuildConstructionEdgeRenderers();
    }

    private int FindOrAddVertexWorld(Vector3 worldPoint, float weldRadius)
    {
        var radiusSq = Mathf.Max(0.0001f, weldRadius);
        radiusSq *= radiusSq;
        var bestIndex = -1;
        var bestDistance = radiusSq;

        for (var i = 0; i < vertices.Count; i++)
        {
            var distance = (transform.TransformPoint(vertices[i]) - worldPoint).sqrMagnitude;
            if (distance > bestDistance)
            {
                continue;
            }

            bestDistance = distance;
            bestIndex = i;
        }

        if (bestIndex >= 0)
        {
            return bestIndex;
        }

        vertices.Add(transform.InverseTransformPoint(worldPoint));
        return vertices.Count - 1;
    }

    private void EnsureComponents()
    {
        if (meshFilter == null)
        {
            meshFilter = GetComponent<MeshFilter>();
        }

        if (meshRenderer == null)
        {
            meshRenderer = GetComponent<MeshRenderer>();
        }

        if (meshCollider == null)
        {
            meshCollider = GetComponent<MeshCollider>();
        }

        if (meshRenderer != null)
        {
            meshRenderer.shadowCastingMode = ShadowCastingMode.On;
            meshRenderer.receiveShadows = true;
            if (meshRenderer.sharedMaterial == null)
            {
                if (runtimeMaterial == null)
                {
                    runtimeMaterial = CreateDefaultMaterial();
                    _ownsRuntimeMaterial = runtimeMaterial != null;
                }

                meshRenderer.sharedMaterial = runtimeMaterial;
            }
        }
    }

    private int CountRenderableTriangles()
    {
        var count = 0;
        for (var f = 0; f < faces.Count; f++)
        {
            var face = faces[f];
            if (face == null || face.TriangleIndices == null)
            {
                continue;
            }

            for (var i = 0; i < face.TriangleIndices.Count; i++)
            {
                var index = face.TriangleIndices[i];
                if (index >= 0 && index < vertices.Count)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private void RebuildConstructionEdgeRenderers()
    {
        EnsureConstructionEdgeRoot();
        var visibleCount = 0;
        for (var i = 0; i < constructionEdges.Count; i++)
        {
            var edge = constructionEdges[i];
            if (edge.A < 0 || edge.B < 0 || edge.A >= vertices.Count || edge.B >= vertices.Count)
            {
                continue;
            }

            var line = GetOrCreateConstructionLine(visibleCount++);
            line.positionCount = 2;
            line.SetPosition(0, vertices[edge.A]);
            line.SetPosition(1, vertices[edge.B]);
            line.enabled = true;
        }

        for (var i = visibleCount; i < _constructionLineRenderers.Count; i++)
        {
            if (_constructionLineRenderers[i] != null)
            {
                _constructionLineRenderers[i].enabled = false;
            }
        }
    }

    private void EnsureConstructionEdgeRoot()
    {
        if (_constructionEdgeRoot != null)
        {
            return;
        }

        var root = transform.Find("ConstructionEdges");
        if (root == null)
        {
            var go = new GameObject("ConstructionEdges");
            go.transform.SetParent(transform, false);
            root = go.transform;
        }

        _constructionEdgeRoot = root;
    }

    private LineRenderer GetOrCreateConstructionLine(int index)
    {
        while (_constructionLineRenderers.Count <= index)
        {
            var go = new GameObject("ConstructionEdge");
            go.transform.SetParent(_constructionEdgeRoot, false);
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = meshRenderer != null ? meshRenderer.sharedMaterial : runtimeMaterial;
            line.useWorldSpace = false;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.startWidth = constructionEdgeWidth;
            line.endWidth = constructionEdgeWidth;
            line.startColor = new Color(1f, 0.86f, 0.18f, 0.95f);
            line.endColor = new Color(1f, 0.86f, 0.18f, 0.95f);
            _constructionLineRenderers.Add(line);
        }

        var renderer = _constructionLineRenderers[index];
        if (renderer != null)
        {
            renderer.startWidth = constructionEdgeWidth;
            renderer.endWidth = constructionEdgeWidth;
            renderer.sharedMaterial = meshRenderer != null ? meshRenderer.sharedMaterial : runtimeMaterial;
        }

        return renderer;
    }

    private void EnsureEdgeCache()
    {
        if (_edges.Count == 0 && (faces.Count > 0 || constructionEdges.Count > 0))
        {
            RebuildEdgeCache();
        }
    }

    private void RebuildEdgeCache()
    {
        _edges.Clear();
        _edgeKeys.Clear();

        for (var f = 0; f < faces.Count; f++)
        {
            var face = faces[f];
            if (face == null || face.VertexIndices == null || face.VertexIndices.Count < 2)
            {
                continue;
            }

            for (var i = 0; i < face.VertexIndices.Count; i++)
            {
                var a = face.VertexIndices[i];
                var b = face.VertexIndices[(i + 1) % face.VertexIndices.Count];
                AddCachedEdge(a, b);
            }
        }

        for (var i = 0; i < constructionEdges.Count; i++)
        {
            AddCachedEdge(constructionEdges[i].A, constructionEdges[i].B);
        }
    }

    private void AddCachedEdge(int a, int b)
    {
        if (a < 0 || b < 0 || a == b || a >= vertices.Count || b >= vertices.Count)
        {
            return;
        }

        var key = BuildEdgeKey(a, b);
        if (!_edgeKeys.Add(key))
        {
            return;
        }

        _edges.Add(new MXInkTopologyEdge(a, b));
    }

    private static ulong BuildEdgeKey(int a, int b)
    {
        var min = (uint)Mathf.Min(a, b);
        var max = (uint)Mathf.Max(a, b);
        return ((ulong)min << 32) | max;
    }

    private static Material CreateDefaultMaterial()
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit")
                     ?? Shader.Find("Universal Render Pipeline/Unlit")
                     ?? Shader.Find("Standard")
                     ?? Shader.Find("Sprites/Default");
        if (shader == null)
        {
            return null;
        }

        var color = new Color(0.18f, 0.72f, 0.92f, 0.78f);
        var material = new Material(shader)
        {
            name = "MXInkEditableMeshMaterial",
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
        material.renderQueue = (int)RenderQueue.Transparent + 8;
        return material;
    }
}

[Serializable]
public sealed class MXInkTopologyFace
{
    public List<int> VertexIndices = new();
    public List<int> TriangleIndices = new();

    public MXInkTopologyFace Clone()
    {
        return new MXInkTopologyFace
        {
            VertexIndices = new List<int>(VertexIndices),
            TriangleIndices = new List<int>(TriangleIndices)
        };
    }
}

[Serializable]
public struct MXInkTopologyEdge
{
    public int A;
    public int B;

    public MXInkTopologyEdge(int a, int b)
    {
        A = a;
        B = b;
    }
}

public sealed class MXInkTopologySnapshot
{
    public readonly List<Vector3> Vertices;
    public readonly List<MXInkTopologyFace> Faces;
    public readonly List<MXInkTopologyEdge> ConstructionEdges;

    public MXInkTopologySnapshot(
        IReadOnlyList<Vector3> vertices,
        IReadOnlyList<MXInkTopologyFace> faces,
        IReadOnlyList<MXInkTopologyEdge> constructionEdges)
    {
        Vertices = new List<Vector3>(vertices);
        Faces = new List<MXInkTopologyFace>();
        for (var i = 0; i < faces.Count; i++)
        {
            Faces.Add(faces[i].Clone());
        }

        ConstructionEdges = new List<MXInkTopologyEdge>(constructionEdges);
    }
}
