using UnityEngine;

public static class PlaceableSurfaceUtility
{
    private const float MinTriangleAreaSqr = 0.000000000001f;
    private const float MinRayDeterminant = 0.0000001f;

    public readonly struct SurfacePoint
    {
        public readonly Vector3 Point;
        public readonly Vector3 Normal;
        public readonly float Distance;

        public SurfacePoint(Vector3 point, Vector3 normal, float distance)
        {
            Point = point;
            Normal = normal.sqrMagnitude > 0.000001f ? normal.normalized : Vector3.up;
            Distance = Mathf.Max(0f, distance);
        }
    }

    public static bool TryGetClosestVisibleMeshPoint(
        PlaceableAsset placeable,
        Vector3 worldPoint,
        out SurfacePoint surface)
    {
        surface = default;
        if (placeable == null)
        {
            return false;
        }

        var filters = placeable.GetComponentsInChildren<MeshFilter>();
        var hasSurface = false;
        var bestPoint = Vector3.zero;
        var bestNormal = Vector3.up;
        var bestDistanceSq = float.MaxValue;

        for (var i = 0; i < filters.Length; i++)
        {
            var filter = filters[i];
            if (!IsUsableVisibleMeshFilter(filter, out var mesh))
            {
                continue;
            }

            if (!TryGetClosestMeshPoint(
                    mesh,
                    filter.transform,
                    worldPoint,
                    out var candidate,
                    out var normal,
                    out var distanceSq)
                || distanceSq >= bestDistanceSq)
            {
                continue;
            }

            hasSurface = true;
            bestPoint = candidate;
            bestNormal = normal;
            bestDistanceSq = distanceSq;
        }

        if (!hasSurface)
        {
            return false;
        }

        surface = new SurfacePoint(bestPoint, bestNormal, Mathf.Sqrt(bestDistanceSq));
        return true;
    }

    public static bool TryRaycastVisibleMesh(
        PlaceableAsset placeable,
        Ray ray,
        float maxDistance,
        out SurfacePoint surface)
    {
        surface = default;
        if (placeable == null)
        {
            return false;
        }

        maxDistance = Mathf.Max(0.001f, maxDistance);
        var filters = placeable.GetComponentsInChildren<MeshFilter>();
        var hasHit = false;
        var bestPoint = Vector3.zero;
        var bestNormal = Vector3.up;
        var bestDistance = maxDistance;

        for (var i = 0; i < filters.Length; i++)
        {
            var filter = filters[i];
            if (!IsUsableVisibleMeshFilter(filter, out var mesh))
            {
                continue;
            }

            if (!TryRaycastMesh(
                    mesh,
                    filter.transform,
                    ray,
                    bestDistance,
                    false,
                    out var candidatePoint,
                    out var candidateNormal,
                    out var candidateDistance))
            {
                continue;
            }

            hasHit = true;
            bestPoint = candidatePoint;
            bestNormal = candidateNormal;
            bestDistance = candidateDistance;
        }

        if (!hasHit)
        {
            return false;
        }

        surface = new SurfacePoint(bestPoint, bestNormal, bestDistance);
        return true;
    }

    public static bool TryRaycastVisibleMeshExit(
        PlaceableAsset placeable,
        Ray ray,
        float maxDistance,
        out SurfacePoint surface)
    {
        surface = default;
        if (placeable == null)
        {
            return false;
        }

        maxDistance = Mathf.Max(0.001f, maxDistance);
        var filters = placeable.GetComponentsInChildren<MeshFilter>();
        var hasHit = false;
        var bestPoint = Vector3.zero;
        var bestNormal = Vector3.up;
        var bestDistance = 0f;

        for (var i = 0; i < filters.Length; i++)
        {
            var filter = filters[i];
            if (!IsUsableVisibleMeshFilter(filter, out var mesh))
            {
                continue;
            }

            if (!TryRaycastMesh(
                    mesh,
                    filter.transform,
                    ray,
                    maxDistance,
                    true,
                    out var candidatePoint,
                    out var candidateNormal,
                    out var candidateDistance)
                || candidateDistance <= bestDistance)
            {
                continue;
            }

            hasHit = true;
            bestPoint = candidatePoint;
            bestNormal = candidateNormal;
            bestDistance = candidateDistance;
        }

        if (!hasHit)
        {
            return false;
        }

        surface = new SurfacePoint(bestPoint, bestNormal, bestDistance);
        return true;
    }

    public static bool TryGetClosestColliderPoint(
        Collider collider,
        Vector3 worldPoint,
        out SurfacePoint surface)
    {
        surface = default;
        if (collider == null || !collider.enabled)
        {
            return false;
        }

        if (collider is MeshCollider meshCollider && meshCollider.sharedMesh == null)
        {
            return false;
        }

        if (collider is MeshCollider meshColliderWithMesh
            && !meshColliderWithMesh.convex)
        {
            return TryGetClosestMeshPoint(
                meshColliderWithMesh.sharedMesh,
                meshColliderWithMesh.transform,
                worldPoint,
                out var meshPoint,
                out var meshNormal,
                out var meshDistanceSq)
                && SetSurface(meshPoint, meshNormal, Mathf.Sqrt(meshDistanceSq), out surface);
        }

        var closest = collider.ClosestPoint(worldPoint);
        var normal = worldPoint - closest;
        if (normal.sqrMagnitude <= 0.000001f)
        {
            normal = closest - collider.bounds.center;
        }

        if (normal.sqrMagnitude <= 0.000001f)
        {
            normal = Vector3.up;
        }

        surface = new SurfacePoint(closest, normal, Vector3.Distance(worldPoint, closest));
        return true;
    }

    private static bool SetSurface(
        Vector3 point,
        Vector3 normal,
        float distance,
        out SurfacePoint surface)
    {
        surface = new SurfacePoint(point, normal, distance);
        return true;
    }

    private static bool IsUsableVisibleMeshFilter(MeshFilter filter, out Mesh mesh)
    {
        mesh = null;
        if (filter == null || !filter.gameObject.activeInHierarchy)
        {
            return false;
        }

        var renderer = filter.GetComponent<MeshRenderer>();
        if (renderer == null || !renderer.enabled)
        {
            return false;
        }

        mesh = filter.sharedMesh;
        if (mesh == null || mesh.vertexCount < 3)
        {
            return false;
        }

        try
        {
            return mesh.triangles != null && mesh.triangles.Length >= 3;
        }
        catch (UnityException)
        {
            return false;
        }
    }

    private static bool TryGetClosestMeshPoint(
        Mesh mesh,
        Transform meshTransform,
        Vector3 worldPoint,
        out Vector3 closestPoint,
        out Vector3 normal,
        out float distanceSq)
    {
        closestPoint = default;
        normal = Vector3.up;
        distanceSq = float.MaxValue;
        if (mesh == null || meshTransform == null)
        {
            return false;
        }

        Vector3[] vertices;
        int[] triangles;
        try
        {
            vertices = mesh.vertices;
            triangles = mesh.triangles;
        }
        catch (UnityException)
        {
            return false;
        }

        if (vertices == null || triangles == null || triangles.Length < 3)
        {
            return false;
        }

        var hasPoint = false;
        for (var i = 0; i < triangles.Length; i += 3)
        {
            var ia = triangles[i];
            var ib = triangles[i + 1];
            var ic = triangles[i + 2];
            if (ia < 0 || ib < 0 || ic < 0
                || ia >= vertices.Length
                || ib >= vertices.Length
                || ic >= vertices.Length)
            {
                continue;
            }

            var a = meshTransform.TransformPoint(vertices[ia]);
            var b = meshTransform.TransformPoint(vertices[ib]);
            var c = meshTransform.TransformPoint(vertices[ic]);
            var triangleNormal = Vector3.Cross(b - a, c - a);
            if (triangleNormal.sqrMagnitude <= MinTriangleAreaSqr)
            {
                continue;
            }

            var candidate = ClosestPointOnTriangle(worldPoint, a, b, c);
            var candidateDistanceSq = (worldPoint - candidate).sqrMagnitude;
            if (candidateDistanceSq >= distanceSq)
            {
                continue;
            }

            hasPoint = true;
            closestPoint = candidate;
            normal = triangleNormal.normalized;
            distanceSq = candidateDistanceSq;
        }

        return hasPoint;
    }

    private static bool TryRaycastMesh(
        Mesh mesh,
        Transform meshTransform,
        Ray ray,
        float maxDistance,
        bool pickFarthest,
        out Vector3 hitPoint,
        out Vector3 hitNormal,
        out float hitDistance)
    {
        hitPoint = default;
        hitNormal = Vector3.up;
        hitDistance = pickFarthest ? 0f : maxDistance;
        if (mesh == null || meshTransform == null)
        {
            return false;
        }

        Vector3[] vertices;
        int[] triangles;
        try
        {
            vertices = mesh.vertices;
            triangles = mesh.triangles;
        }
        catch (UnityException)
        {
            return false;
        }

        if (vertices == null || triangles == null || triangles.Length < 3)
        {
            return false;
        }

        var hasHit = false;
        for (var i = 0; i < triangles.Length; i += 3)
        {
            var ia = triangles[i];
            var ib = triangles[i + 1];
            var ic = triangles[i + 2];
            if (ia < 0 || ib < 0 || ic < 0
                || ia >= vertices.Length
                || ib >= vertices.Length
                || ic >= vertices.Length)
            {
                continue;
            }

            var a = meshTransform.TransformPoint(vertices[ia]);
            var b = meshTransform.TransformPoint(vertices[ib]);
            var c = meshTransform.TransformPoint(vertices[ic]);
            var triangleMaxDistance = pickFarthest ? maxDistance : hitDistance;
            if (!TryRaycastTriangle(ray, a, b, c, triangleMaxDistance, out var distance, out var normal))
            {
                continue;
            }

            if (pickFarthest && distance <= hitDistance)
            {
                continue;
            }

            hasHit = true;
            hitDistance = distance;
            hitPoint = ray.GetPoint(distance);
            hitNormal = normal;
        }

        return hasHit;
    }

    private static bool TryRaycastTriangle(
        Ray ray,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        float maxDistance,
        out float distance,
        out Vector3 normal)
    {
        distance = 0f;
        normal = Vector3.up;

        var edge1 = b - a;
        var edge2 = c - a;
        var triangleNormal = Vector3.Cross(edge1, edge2);
        if (triangleNormal.sqrMagnitude <= MinTriangleAreaSqr)
        {
            return false;
        }

        var pvec = Vector3.Cross(ray.direction, edge2);
        var determinant = Vector3.Dot(edge1, pvec);
        if (Mathf.Abs(determinant) <= MinRayDeterminant)
        {
            return false;
        }

        var inverseDeterminant = 1f / determinant;
        var tvec = ray.origin - a;
        var u = Vector3.Dot(tvec, pvec) * inverseDeterminant;
        if (u < 0f || u > 1f)
        {
            return false;
        }

        var qvec = Vector3.Cross(tvec, edge1);
        var v = Vector3.Dot(ray.direction, qvec) * inverseDeterminant;
        if (v < 0f || u + v > 1f)
        {
            return false;
        }

        distance = Vector3.Dot(edge2, qvec) * inverseDeterminant;
        if (distance < 0f || distance > maxDistance)
        {
            return false;
        }

        normal = triangleNormal.normalized;
        if (Vector3.Dot(normal, ray.direction) > 0f)
        {
            normal = -normal;
        }

        return true;
    }

    private static Vector3 ClosestPointOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        var ab = b - a;
        var ac = c - a;
        var ap = p - a;
        var d1 = Vector3.Dot(ab, ap);
        var d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0f && d2 <= 0f)
        {
            return a;
        }

        var bp = p - b;
        var d3 = Vector3.Dot(ab, bp);
        var d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0f && d4 <= d3)
        {
            return b;
        }

        var vc = d1 * d4 - d3 * d2;
        if (vc <= 0f && d1 >= 0f && d3 <= 0f)
        {
            var v = d1 / (d1 - d3);
            return a + ab * v;
        }

        var cp = p - c;
        var d5 = Vector3.Dot(ab, cp);
        var d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0f && d5 <= d6)
        {
            return c;
        }

        var vb = d5 * d2 - d1 * d6;
        if (vb <= 0f && d2 >= 0f && d6 <= 0f)
        {
            var w = d2 / (d2 - d6);
            return a + ac * w;
        }

        var va = d3 * d6 - d5 * d4;
        if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f)
        {
            var w = (d4 - d3) / (d4 - d3 + d5 - d6);
            return b + (c - b) * w;
        }

        var denom = 1f / (va + vb + vc);
        var vInside = vb * denom;
        var wInside = vc * denom;
        return a + ab * vInside + ac * wInside;
    }
}
