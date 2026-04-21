using System.Collections.Generic;
using UnityEngine;

/// <summary>Procedural meshes for drawer placeables (cached, readable for MeshCollider baking).</summary>
public static class MrBlueprintPrimitiveMeshes
{
    private static readonly Dictionary<MrBlueprintPlaceableShapeKind, Mesh> Cache = new Dictionary<MrBlueprintPlaceableShapeKind, Mesh>(16);
    private static Mesh _builtinCylinder;

    public static Mesh GetOrCreate(MrBlueprintPlaceableShapeKind kind)
    {
        if (Cache.TryGetValue(kind, out var existing) && existing != null)
            return existing;

        Mesh mesh = kind switch
        {
            MrBlueprintPlaceableShapeKind.Cylinder => GetBuiltinCylinderMesh(),
            MrBlueprintPlaceableShapeKind.Octahedron => BuildOctahedron(),
            MrBlueprintPlaceableShapeKind.Pyramid => BuildSquarePyramid(),
            MrBlueprintPlaceableShapeKind.TriangularPrism => BuildTriangularPrism(),
            MrBlueprintPlaceableShapeKind.Cone => BuildCone(14),
            MrBlueprintPlaceableShapeKind.Hemisphere => BuildHemisphere(10, 5),
            MrBlueprintPlaceableShapeKind.Torus => BuildTorus(14, 8, 0.38f, 0.12f),
            MrBlueprintPlaceableShapeKind.Buckyball => BuildIcosahedron(),
            MrBlueprintPlaceableShapeKind.HexagonalPrism => BuildHexagonalPrism(),
            _ => BuildCubeFallback()
        };

        mesh.name = "MrBlueprint_" + kind;
        mesh.RecalculateBounds();
        RecalculateNormalsByShape(mesh, kind);
        mesh.RecalculateTangents();
        Cache[kind] = mesh;
        return mesh;
    }

    private static void RecalculateNormalsByShape(Mesh mesh, MrBlueprintPlaceableShapeKind kind)
    {
        // Faceted shapes should keep hard edges; rounded shapes should stay smooth.
        switch (kind)
        {
            case MrBlueprintPlaceableShapeKind.Cylinder:
            case MrBlueprintPlaceableShapeKind.Cone:
            case MrBlueprintPlaceableShapeKind.Hemisphere:
            case MrBlueprintPlaceableShapeKind.Torus:
            case MrBlueprintPlaceableShapeKind.Buckyball:
                mesh.RecalculateNormals();
                break;
            default:
                ApplyFlatShading(mesh);
                break;
        }
    }

    private static void ApplyFlatShading(Mesh mesh)
    {
        var srcVerts = mesh.vertices;
        var srcTris = mesh.triangles;
        var srcUv = mesh.uv;
        var hasUv = srcUv != null && srcUv.Length == srcVerts.Length;

        var flatVerts = new Vector3[srcTris.Length];
        var flatTris = new int[srcTris.Length];
        Vector2[] flatUv = null;
        if (hasUv)
            flatUv = new Vector2[srcTris.Length];

        for (var i = 0; i < srcTris.Length; i++)
        {
            var srcIndex = srcTris[i];
            flatVerts[i] = srcVerts[srcIndex];
            flatTris[i] = i;
            if (hasUv)
                flatUv[i] = srcUv[srcIndex];
        }

        mesh.Clear();
        mesh.vertices = flatVerts;
        mesh.triangles = flatTris;
        if (hasUv)
            mesh.uv = flatUv;

        mesh.RecalculateNormals();
    }

    private static Mesh GetBuiltinCylinderMesh()
    {
        if (_builtinCylinder != null)
            return _builtinCylinder;

        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        _builtinCylinder = Object.Instantiate(go.GetComponent<MeshFilter>().sharedMesh);
        _builtinCylinder.name = "BuiltinCylinderClone";
        Object.Destroy(go);
        return _builtinCylinder;
    }

    private static Mesh BuildCubeFallback()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var m = Object.Instantiate(go.GetComponent<MeshFilter>().sharedMesh);
        Object.Destroy(go);
        return m;
    }

    private static Mesh BuildOctahedron()
    {
        var verts = new List<Vector3>
        {
            new Vector3(0f, 0.5f, 0f),
            new Vector3(0f, -0.5f, 0f),
            new Vector3(0.5f, 0f, 0f),
            new Vector3(-0.5f, 0f, 0f),
            new Vector3(0f, 0f, 0.5f),
            new Vector3(0f, 0f, -0.5f),
        };
        var tris = new List<int>
        {
            0, 2, 4, 0, 4, 3, 0, 3, 5, 0, 5, 2,
            1, 4, 2, 1, 3, 4, 1, 5, 3, 1, 2, 5,
        };
        return BuildMesh(verts, tris);
    }

    private static Mesh BuildSquarePyramid()
    {
        float h = 0.5f;
        float w = 0.35f;
        var verts = new List<Vector3>
        {
            new Vector3(0f, h, 0f),
            new Vector3(-w, -h, w),
            new Vector3(w, -h, w),
            new Vector3(w, -h, -w),
            new Vector3(-w, -h, -w),
        };
        var tris = new List<int>
        {
            0, 2, 1, 0, 3, 2, 0, 4, 3, 0, 1, 4,
            1, 2, 3, 1, 3, 4,
        };
        return BuildMesh(verts, tris);
    }

    private static Mesh BuildTriangularPrism()
    {
        float h = 0.4f;
        float r = 0.35f;
        var verts = new List<Vector3>(6);
        for (var i = 0; i < 3; i++)
        {
            var a = Mathf.PI * 2f * i / 3f + Mathf.PI * 0.5f;
            verts.Add(new Vector3(Mathf.Cos(a) * r, h, Mathf.Sin(a) * r));
        }

        for (var i = 0; i < 3; i++)
        {
            var a = Mathf.PI * 2f * i / 3f + Mathf.PI * 0.5f;
            verts.Add(new Vector3(Mathf.Cos(a) * r, -h, Mathf.Sin(a) * r));
        }

        var tris = new List<int>
        {
            0, 1, 2,
            3, 5, 4,
            0, 3, 4, 0, 4, 1,
            1, 4, 5, 1, 5, 2,
            2, 5, 3, 2, 3, 0,
        };
        return BuildMesh(verts, tris);
    }

    private static Mesh BuildHexagonalPrism()
    {
        const int n = 6;
        float h = 0.35f;
        float r = 0.32f;
        var verts = new List<Vector3>(n * 2);
        for (var ring = 0; ring < 2; ring++)
        {
            var y = ring == 0 ? h : -h;
            for (var i = 0; i < n; i++)
            {
                var a = Mathf.PI * 2f * i / n;
                verts.Add(new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r));
            }
        }

        var tris = new List<int>();
        for (var i = 0; i < n; i++)
        {
            var i1 = (i + 1) % n;
            tris.AddRange(new[] { i, i1, i + n, i1, i1 + n, i + n });
        }

        for (var i = 1; i < n - 1; i++)
            tris.AddRange(new[] { 0, i, i + 1 });

        var top0 = n;
        for (var i = 1; i < n - 1; i++)
            tris.AddRange(new[] { top0, top0 + i + 1, top0 + i });

        return BuildMesh(verts, tris);
    }

    private static Mesh BuildCone(int segments)
    {
        float h = 0.5f;
        float r = 0.35f;
        var verts = new List<Vector3> { new Vector3(0f, h, 0f) };
        for (var i = 0; i <= segments; i++)
        {
            var a = Mathf.PI * 2f * i / segments;
            verts.Add(new Vector3(Mathf.Cos(a) * r, -h, Mathf.Sin(a) * r));
        }

        var tris = new List<int>();
        for (var i = 0; i < segments; i++)
        {
            tris.Add(0);
            tris.Add(i + 1);
            tris.Add(i + 2);
        }

        for (var i = 1; i < segments; i++)
            tris.AddRange(new[] { 1, i + 2, i + 1 });

        return BuildMesh(verts, tris);
    }

    private static Mesh BuildHemisphere(int lon, int latHalf)
    {
        var verts = new List<Vector3>();
        var tris = new List<int>();
        float radius = 0.45f;

        for (var iy = 0; iy <= latHalf; iy++)
        {
            var v = (float)iy / latHalf;
            var phi = v * Mathf.PI * 0.5f;
            for (var ix = 0; ix <= lon; ix++)
            {
                var u = (float)ix / lon;
                var theta = u * Mathf.PI * 2f;
                var y = Mathf.Cos(phi) * radius;
                var ring = Mathf.Sin(phi) * radius;
                verts.Add(new Vector3(Mathf.Cos(theta) * ring, y, Mathf.Sin(theta) * ring));
            }
        }

        var row = lon + 1;
        for (var iy = 0; iy < latHalf; iy++)
        {
            for (var ix = 0; ix < lon; ix++)
            {
                var a = iy * row + ix;
                var b = a + 1;
                var c = a + row;
                var d = c + 1;
                tris.AddRange(new[] { a, c, b, b, c, d });
            }
        }

        for (var ix = 1; ix < lon - 1; ix++)
        {
            var baseI = latHalf * row;
            tris.AddRange(new[] { baseI, baseI + ix + 1, baseI + ix });
        }

        return BuildMesh(verts, tris);
    }

    private static Mesh BuildTorus(int majorSeg, int minorSeg, float majorR, float minorR)
    {
        var verts = new List<Vector3>();
        var tris = new List<int>();

        for (var i = 0; i <= majorSeg; i++)
        {
            var u = (float)i / majorSeg * Mathf.PI * 2f;
            for (var j = 0; j <= minorSeg; j++)
            {
                var v = (float)j / minorSeg * Mathf.PI * 2f;
                var x = (majorR + minorR * Mathf.Cos(v)) * Mathf.Cos(u);
                var z = (majorR + minorR * Mathf.Cos(v)) * Mathf.Sin(u);
                var y = minorR * Mathf.Sin(v);
                verts.Add(new Vector3(x, y, z));
            }
        }

        var row = minorSeg + 1;
        for (var i = 0; i < majorSeg; i++)
        {
            for (var j = 0; j < minorSeg; j++)
            {
                var a = i * row + j;
                var b = a + 1;
                var c = a + row;
                var d = c + 1;
                tris.AddRange(new[] { a, c, b, b, c, d });
            }
        }

        return BuildMesh(verts, tris);
    }

    private static Mesh BuildIcosahedron()
    {
        const float t = 1.61803398875f;
        var verts = new List<Vector3>
        {
            new Vector3(-1f, t, 0f).normalized * 0.45f,
            new Vector3(1f, t, 0f).normalized * 0.45f,
            new Vector3(-1f, -t, 0f).normalized * 0.45f,
            new Vector3(1f, -t, 0f).normalized * 0.45f,
            new Vector3(0f, -1f, t).normalized * 0.45f,
            new Vector3(0f, 1f, t).normalized * 0.45f,
            new Vector3(0f, -1f, -t).normalized * 0.45f,
            new Vector3(0f, 1f, -t).normalized * 0.45f,
            new Vector3(t, 0f, -1f).normalized * 0.45f,
            new Vector3(t, 0f, 1f).normalized * 0.45f,
            new Vector3(-t, 0f, -1f).normalized * 0.45f,
            new Vector3(-t, 0f, 1f).normalized * 0.45f,
        };

        var tris = new List<int>
        {
            0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11,
            1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
            3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9,
            4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
        };
        return BuildMesh(verts, tris);
    }

    private static Mesh BuildMesh(List<Vector3> verts, List<int> tris)
    {
        var mesh = new Mesh
        {
            indexFormat = tris.Count > 65535
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16
        };
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        return mesh;
    }
}
