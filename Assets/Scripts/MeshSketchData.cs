using System.Collections.Generic;
using UnityEngine;

public enum MeshSketchSnapKind
{
    None,
    Vertex,
    Edge,
    Closure
}

public enum MeshSketchResolveStatus
{
    None,
    ValidClosedLoop,
    ValidOpenChain,
    TooFewPoints,
    NotClosed,
    NonPlanar,
    SelfIntersecting,
    Degenerate,
    TriangulationFailed
}

public readonly struct MeshSketchPlane
{
    public readonly Vector3 Origin;
    public readonly Vector3 Normal;
    public readonly Vector3 AxisX;
    public readonly Vector3 AxisY;

    public MeshSketchPlane(Vector3 origin, Vector3 normal, Vector3 axisX)
    {
        Origin = origin;
        Normal = normal.sqrMagnitude > 0.000001f ? normal.normalized : Vector3.up;
        var projectedAxis = Vector3.ProjectOnPlane(axisX, Normal);
        if (projectedAxis.sqrMagnitude <= 0.000001f)
        {
            projectedAxis = Vector3.Cross(Normal, Mathf.Abs(Vector3.Dot(Normal, Vector3.up)) > 0.9f
                ? Vector3.right
                : Vector3.up);
        }

        projectedAxis.Normalize();
        AxisX = projectedAxis;
        AxisY = Vector3.Cross(Normal, AxisX).normalized;
    }

    public Vector2 ToPlane(Vector3 worldPoint)
    {
        var delta = worldPoint - Origin;
        return new Vector2(Vector3.Dot(delta, AxisX), Vector3.Dot(delta, AxisY));
    }

    public Vector3 FromPlane(Vector2 planePoint)
    {
        return Origin + AxisX * planePoint.x + AxisY * planePoint.y;
    }
}

public struct MeshSketchSnapResult
{
    public MeshSketchSnapKind Kind;
    public Vector3 Point;
    public MXInkEditableMeshTopology Topology;
    public int VertexIndex;
    public int EdgeIndex;
    public Vector3 EdgeStart;
    public Vector3 EdgeEnd;

    public bool HasSnap => Kind != MeshSketchSnapKind.None;

    public static MeshSketchSnapResult None(Vector3 point)
    {
        return new MeshSketchSnapResult
        {
            Kind = MeshSketchSnapKind.None,
            Point = point,
            VertexIndex = -1,
            EdgeIndex = -1
        };
    }
}

public sealed class MeshSketchResolveResult
{
    public MeshSketchResolveStatus Status = MeshSketchResolveStatus.None;
    public string Message;
    public readonly List<Vector3> Points = new();
    public readonly List<int> Triangles = new();
    public MeshSketchPlane Plane;

    public bool IsValidClosedLoop => Status == MeshSketchResolveStatus.ValidClosedLoop;
    public bool IsValidOpenChain => Status == MeshSketchResolveStatus.ValidOpenChain;

    public void Clear()
    {
        Status = MeshSketchResolveStatus.None;
        Message = string.Empty;
        Points.Clear();
        Triangles.Clear();
        Plane = default;
    }

    public void SetInvalid(MeshSketchResolveStatus status, string message)
    {
        Status = status;
        Message = message;
        Points.Clear();
        Triangles.Clear();
    }
}
