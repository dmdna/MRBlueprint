using System.Collections.Generic;
using UnityEngine;

public sealed class FaceBuilder
{
    public MXInkEditableMeshTopology CommitFace(
        MeshSketchResolveResult resolvedLoop,
        IReadOnlyList<MeshSketchSnapResult> snaps,
        MeshSketchSettings settings,
        Material material,
        out MXInkTopologySnapshot beforeSnapshot,
        out bool createdTopology)
    {
        beforeSnapshot = null;
        createdTopology = false;
        if (resolvedLoop == null || !resolvedLoop.IsValidClosedLoop)
        {
            return null;
        }

        var topology = ResolveTargetTopology(snaps);
        if (topology == null)
        {
            topology = MXInkEditableMeshTopology.CreateNew(resolvedLoop.Points[0], material);
            createdTopology = true;
        }

        beforeSnapshot = topology.CaptureSnapshot();
        var weldRadius = settings != null ? Mathf.Max(settings.vertexSnapRadius, settings.edgeSnapRadius) : 0.035f;
        if (!topology.AddFaceWorld(resolvedLoop.Points, resolvedLoop.Triangles, weldRadius))
        {
            topology.RestoreSnapshot(beforeSnapshot);
            return null;
        }

        return topology;
    }

    public MXInkEditableMeshTopology CommitOpenChain(
        MeshSketchResolveResult resolvedChain,
        IReadOnlyList<MeshSketchSnapResult> snaps,
        MeshSketchSettings settings,
        Material material,
        out MXInkTopologySnapshot beforeSnapshot,
        out bool createdTopology)
    {
        beforeSnapshot = null;
        createdTopology = false;
        if (resolvedChain == null || !resolvedChain.IsValidOpenChain)
        {
            return null;
        }

        var topology = ResolveTargetTopology(snaps);
        if (topology == null)
        {
            topology = MXInkEditableMeshTopology.CreateNew(resolvedChain.Points[0], material);
            createdTopology = true;
        }

        beforeSnapshot = topology.CaptureSnapshot();
        var weldRadius = settings != null ? Mathf.Max(settings.vertexSnapRadius, settings.edgeSnapRadius) : 0.035f;
        if (!topology.AddConstructionEdgesWorld(resolvedChain.Points, weldRadius))
        {
            topology.RestoreSnapshot(beforeSnapshot);
            return null;
        }

        return topology;
    }

    private static MXInkEditableMeshTopology ResolveTargetTopology(IReadOnlyList<MeshSketchSnapResult> snaps)
    {
        if (snaps != null)
        {
            for (var i = 0; i < snaps.Count; i++)
            {
                if (snaps[i].Topology != null)
                {
                    return snaps[i].Topology;
                }
            }
        }

        var selected = AssetSelectionManager.Instance != null
            ? AssetSelectionManager.Instance.SelectedAsset
            : null;
        return selected != null
            ? selected.GetComponent<MXInkEditableMeshTopology>()
            : null;
    }
}
