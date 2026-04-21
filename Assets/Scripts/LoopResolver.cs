using System.Collections.Generic;
using UnityEngine;

public sealed class LoopResolver
{
    private readonly List<Vector3> _unique = new();
    private readonly List<Vector2> _planePoints = new();
    private readonly List<int> _polygon = new();

    public MeshSketchResolveResult ResolveClosedLoop(
        IReadOnlyList<Vector3> points,
        MeshSketchSettings settings)
    {
        var result = new MeshSketchResolveResult();
        settings?.Clamp();
        var minEdge = settings != null ? settings.minEdgeLength : 0.01f;
        var closureRadius = settings != null ? settings.closureSnapRadius : 0.04f;
        var planarityTolerance = settings != null ? settings.planarityTolerance : 0.02f;

        CopyUnique(points, minEdge, _unique);
        if (_unique.Count > 1 && Vector3.Distance(_unique[0], _unique[_unique.Count - 1]) <= closureRadius)
        {
            _unique.RemoveAt(_unique.Count - 1);
        }

        if (_unique.Count < 3)
        {
            result.SetInvalid(MeshSketchResolveStatus.TooFewPoints, "Need at least three stable points.");
            return result;
        }

        if (points == null || points.Count == 0
            || Vector3.Distance(_unique[0], points[points.Count - 1]) > closureRadius)
        {
            result.SetInvalid(MeshSketchResolveStatus.NotClosed, "Endpoint is outside closure tolerance.");
            return result;
        }

        if (!TryFitPlane(_unique, out var plane))
        {
            result.SetInvalid(MeshSketchResolveStatus.Degenerate, "Loop does not define a stable plane.");
            return result;
        }

        var maxPlaneDistance = 0f;
        for (var i = 0; i < _unique.Count; i++)
        {
            maxPlaneDistance = Mathf.Max(
                maxPlaneDistance,
                Mathf.Abs(Vector3.Dot(_unique[i] - plane.Origin, plane.Normal)));
        }

        if (maxPlaneDistance > planarityTolerance)
        {
            result.SetInvalid(MeshSketchResolveStatus.NonPlanar, "Loop is too far from a single plane.");
            return result;
        }

        _planePoints.Clear();
        for (var i = 0; i < _unique.Count; i++)
        {
            _planePoints.Add(plane.ToPlane(_unique[i]));
        }

        if (HasDegenerateEdges(_planePoints, minEdge))
        {
            result.SetInvalid(MeshSketchResolveStatus.Degenerate, "Loop has edges that are too short.");
            return result;
        }

        if (HasSelfIntersection(_planePoints))
        {
            result.SetInvalid(MeshSketchResolveStatus.SelfIntersecting, "Loop intersects itself.");
            return result;
        }

        var area = SignedArea(_planePoints);
        if (Mathf.Abs(area) <= minEdge * minEdge)
        {
            result.SetInvalid(MeshSketchResolveStatus.Degenerate, "Loop area is too small.");
            return result;
        }

        result.Points.AddRange(_unique);
        result.Plane = plane;
        if (area < 0f)
        {
            result.Points.Reverse();
            _planePoints.Reverse();
        }

        if (!Triangulate(_planePoints, result.Triangles))
        {
            result.SetInvalid(MeshSketchResolveStatus.TriangulationFailed, "Could not triangulate this loop.");
            return result;
        }

        result.Status = MeshSketchResolveStatus.ValidClosedLoop;
        result.Message = "Face ready.";
        return result;
    }

    public MeshSketchResolveResult ResolveOpenChain(
        IReadOnlyList<Vector3> points,
        MeshSketchSettings settings)
    {
        var result = new MeshSketchResolveResult();
        settings?.Clamp();
        var minEdge = settings != null ? settings.minEdgeLength : 0.01f;

        CopyUnique(points, minEdge, result.Points);
        if (result.Points.Count < 2)
        {
            result.SetInvalid(MeshSketchResolveStatus.TooFewPoints, "Need at least two stable points.");
            return result;
        }

        for (var i = 0; i < result.Points.Count - 1; i++)
        {
            if (Vector3.Distance(result.Points[i], result.Points[i + 1]) < minEdge)
            {
                result.SetInvalid(MeshSketchResolveStatus.Degenerate, "Open chain has edges that are too short.");
                return result;
            }
        }

        result.Status = MeshSketchResolveStatus.ValidOpenChain;
        result.Message = "Construction edge chain ready.";
        return result;
    }

    public bool TryInferPlane(IReadOnlyList<Vector3> points, out MeshSketchPlane plane)
    {
        return TryFitPlane(points, out plane);
    }

    private static void CopyUnique(IReadOnlyList<Vector3> source, float minEdge, List<Vector3> destination)
    {
        destination.Clear();
        if (source == null)
        {
            return;
        }

        for (var i = 0; i < source.Count; i++)
        {
            var point = source[i];
            if (destination.Count > 0
                && Vector3.Distance(destination[destination.Count - 1], point) < minEdge)
            {
                continue;
            }

            destination.Add(point);
        }
    }

    private static bool TryFitPlane(IReadOnlyList<Vector3> points, out MeshSketchPlane plane)
    {
        plane = default;
        if (points == null || points.Count < 3)
        {
            return false;
        }

        var origin = Vector3.zero;
        for (var i = 0; i < points.Count; i++)
        {
            origin += points[i];
        }

        origin /= points.Count;

        var normal = Vector3.zero;
        for (var i = 0; i < points.Count; i++)
        {
            var current = points[i];
            var next = points[(i + 1) % points.Count];
            normal.x += (current.y - next.y) * (current.z + next.z);
            normal.y += (current.z - next.z) * (current.x + next.x);
            normal.z += (current.x - next.x) * (current.y + next.y);
        }

        if (normal.sqrMagnitude <= 0.000001f)
        {
            for (var a = 0; a < points.Count - 2 && normal.sqrMagnitude <= 0.000001f; a++)
            {
                for (var b = a + 1; b < points.Count - 1 && normal.sqrMagnitude <= 0.000001f; b++)
                {
                    for (var c = b + 1; c < points.Count && normal.sqrMagnitude <= 0.000001f; c++)
                    {
                        normal = Vector3.Cross(points[b] - points[a], points[c] - points[a]);
                    }
                }
            }
        }

        if (normal.sqrMagnitude <= 0.000001f)
        {
            return false;
        }

        var axis = Vector3.zero;
        for (var i = 1; i < points.Count; i++)
        {
            axis = points[i] - points[0];
            if (axis.sqrMagnitude > 0.000001f)
            {
                break;
            }
        }

        plane = new MeshSketchPlane(origin, normal, axis);
        return true;
    }

    private static bool HasDegenerateEdges(IReadOnlyList<Vector2> points, float minEdge)
    {
        for (var i = 0; i < points.Count; i++)
        {
            var next = (i + 1) % points.Count;
            if (Vector2.Distance(points[i], points[next]) < minEdge)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasSelfIntersection(IReadOnlyList<Vector2> points)
    {
        for (var i = 0; i < points.Count; i++)
        {
            var iNext = (i + 1) % points.Count;
            for (var j = i + 1; j < points.Count; j++)
            {
                var jNext = (j + 1) % points.Count;
                if (i == j || iNext == j || jNext == i)
                {
                    continue;
                }

                if (i == 0 && jNext == 0)
                {
                    continue;
                }

                if (SegmentsIntersect(points[i], points[iNext], points[j], points[jNext]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool SegmentsIntersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        const float eps = 0.000001f;
        var ab1 = Cross(b - a, c - a);
        var ab2 = Cross(b - a, d - a);
        var cd1 = Cross(d - c, a - c);
        var cd2 = Cross(d - c, b - c);

        if (Mathf.Abs(ab1) <= eps && OnSegment(a, b, c))
        {
            return true;
        }

        if (Mathf.Abs(ab2) <= eps && OnSegment(a, b, d))
        {
            return true;
        }

        if (Mathf.Abs(cd1) <= eps && OnSegment(c, d, a))
        {
            return true;
        }

        if (Mathf.Abs(cd2) <= eps && OnSegment(c, d, b))
        {
            return true;
        }

        return (ab1 > eps && ab2 < -eps || ab1 < -eps && ab2 > eps)
               && (cd1 > eps && cd2 < -eps || cd1 < -eps && cd2 > eps);
    }

    private static bool OnSegment(Vector2 a, Vector2 b, Vector2 p)
    {
        const float eps = 0.000001f;
        return p.x >= Mathf.Min(a.x, b.x) - eps
               && p.x <= Mathf.Max(a.x, b.x) + eps
               && p.y >= Mathf.Min(a.y, b.y) - eps
               && p.y <= Mathf.Max(a.y, b.y) + eps;
    }

    private static float Cross(Vector2 a, Vector2 b)
    {
        return a.x * b.y - a.y * b.x;
    }

    private static float SignedArea(IReadOnlyList<Vector2> points)
    {
        var area = 0f;
        for (var i = 0; i < points.Count; i++)
        {
            var next = (i + 1) % points.Count;
            area += points[i].x * points[next].y - points[next].x * points[i].y;
        }

        return area * 0.5f;
    }

    private bool Triangulate(IReadOnlyList<Vector2> points, List<int> triangles)
    {
        triangles.Clear();
        _polygon.Clear();
        for (var i = 0; i < points.Count; i++)
        {
            _polygon.Add(i);
        }

        var guard = points.Count * points.Count;
        while (_polygon.Count > 3 && guard-- > 0)
        {
            var clipped = false;
            for (var i = 0; i < _polygon.Count; i++)
            {
                var previous = _polygon[(i - 1 + _polygon.Count) % _polygon.Count];
                var current = _polygon[i];
                var next = _polygon[(i + 1) % _polygon.Count];

                if (!IsEar(points, previous, current, next, _polygon))
                {
                    continue;
                }

                triangles.Add(previous);
                triangles.Add(current);
                triangles.Add(next);
                _polygon.RemoveAt(i);
                clipped = true;
                break;
            }

            if (!clipped)
            {
                return false;
            }
        }

        if (_polygon.Count != 3)
        {
            return false;
        }

        triangles.Add(_polygon[0]);
        triangles.Add(_polygon[1]);
        triangles.Add(_polygon[2]);
        return true;
    }

    private static bool IsEar(
        IReadOnlyList<Vector2> points,
        int previous,
        int current,
        int next,
        IReadOnlyList<int> polygon)
    {
        var a = points[previous];
        var b = points[current];
        var c = points[next];
        if (Cross(b - a, c - b) <= 0.000001f)
        {
            return false;
        }

        for (var i = 0; i < polygon.Count; i++)
        {
            var candidate = polygon[i];
            if (candidate == previous || candidate == current || candidate == next)
            {
                continue;
            }

            if (PointInTriangle(points[candidate], a, b, c))
            {
                return false;
            }
        }

        return true;
    }

    private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        const float eps = 0.000001f;
        var ab = Cross(b - a, p - a);
        var bc = Cross(c - b, p - b);
        var ca = Cross(a - c, p - c);
        return ab >= -eps && bc >= -eps && ca >= -eps;
    }
}
