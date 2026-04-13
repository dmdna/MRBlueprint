using UnityEngine;

public enum GizmoHandleKind
{
    MoveX,
    MoveY,
    MoveZ,
    RotateX,
    RotateY,
    RotateZ,
    ScaleX,
    ScaleY,
    ScaleZ,
}

/// <summary>
/// Marks a collider as part of the runtime transform gizmo; picked by <see cref="PlaceableTransformGizmo"/>.
/// </summary>
public sealed class GizmoHandlePart : MonoBehaviour
{
    public GizmoHandleKind Kind;
}
