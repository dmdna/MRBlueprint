using System;
using UnityEngine;

[Serializable]
public sealed class MeshSketchSettings
{
    [Min(0.001f)] public float vertexSnapRadius = 0.035f;
    [Min(0.001f)] public float edgeSnapRadius = 0.03f;
    [Min(0.001f)] public float closureSnapRadius = 0.045f;
    [Min(0.0001f)] public float planarityTolerance = 0.018f;
    [Min(0.0001f)] public float minEdgeLength = 0.012f;
    [Min(0.0001f)] public float pointSampleSpacing = 0.012f;
    [Range(0f, 1f)] public float smoothingStrength = 0.35f;

    public void Clamp()
    {
        vertexSnapRadius = Mathf.Max(0.001f, vertexSnapRadius);
        edgeSnapRadius = Mathf.Max(0.001f, edgeSnapRadius);
        closureSnapRadius = Mathf.Max(0.001f, closureSnapRadius);
        planarityTolerance = Mathf.Max(0.0001f, planarityTolerance);
        minEdgeLength = Mathf.Max(0.0001f, minEdgeLength);
        pointSampleSpacing = Mathf.Max(0.0001f, pointSampleSpacing);
        smoothingStrength = Mathf.Clamp01(smoothingStrength);
    }
}
