using System.Collections.Generic;
using UnityEngine;

public sealed class SnapSolver
{
    public MeshSketchSnapResult Solve(
        Vector3 point,
        IReadOnlyList<MXInkEditableMeshTopology> topologies,
        MeshSketchSettings settings)
    {
        settings?.Clamp();
        var vertexRadius = settings != null ? settings.vertexSnapRadius : 0.035f;
        var edgeRadius = settings != null ? settings.edgeSnapRadius : 0.03f;

        var bestVertex = MeshSketchSnapResult.None(point);
        var bestVertexDistance = vertexRadius;

        if (topologies != null)
        {
            for (var t = 0; t < topologies.Count; t++)
            {
                var topology = topologies[t];
                if (topology == null || !topology.isActiveAndEnabled)
                {
                    continue;
                }

                for (var v = 0; v < topology.VertexCount; v++)
                {
                    var world = topology.GetVertexWorld(v);
                    var distance = Vector3.Distance(point, world);
                    if (distance > bestVertexDistance)
                    {
                        continue;
                    }

                    bestVertexDistance = distance;
                    bestVertex = new MeshSketchSnapResult
                    {
                        Kind = MeshSketchSnapKind.Vertex,
                        Point = world,
                        Topology = topology,
                        VertexIndex = v,
                        EdgeIndex = -1
                    };
                }
            }
        }

        if (bestVertex.HasSnap)
        {
            return bestVertex;
        }

        var bestEdge = MeshSketchSnapResult.None(point);
        var bestEdgeDistance = edgeRadius;

        if (topologies != null)
        {
            for (var t = 0; t < topologies.Count; t++)
            {
                var topology = topologies[t];
                if (topology == null || !topology.isActiveAndEnabled)
                {
                    continue;
                }

                for (var e = 0; e < topology.EdgeCount; e++)
                {
                    if (!topology.TryGetEdgeWorld(e, out var a, out var b))
                    {
                        continue;
                    }

                    var closest = ClosestPointOnSegment(a, b, point);
                    var distance = Vector3.Distance(point, closest);
                    if (distance > bestEdgeDistance)
                    {
                        continue;
                    }

                    bestEdgeDistance = distance;
                    bestEdge = new MeshSketchSnapResult
                    {
                        Kind = MeshSketchSnapKind.Edge,
                        Point = closest,
                        Topology = topology,
                        VertexIndex = -1,
                        EdgeIndex = e,
                        EdgeStart = a,
                        EdgeEnd = b
                    };
                }
            }
        }

        return bestEdge;
    }

    private static Vector3 ClosestPointOnSegment(Vector3 a, Vector3 b, Vector3 point)
    {
        var segment = b - a;
        var lengthSq = segment.sqrMagnitude;
        if (lengthSq <= 0.0000001f)
        {
            return a;
        }

        var t = Mathf.Clamp01(Vector3.Dot(point - a, segment) / lengthSq);
        return a + segment * t;
    }
}
