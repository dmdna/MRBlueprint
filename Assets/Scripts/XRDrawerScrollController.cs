using UnityEngine;

public class XRDrawerScrollController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform contentRoot;
    [SerializeField] private Transform dragReference;

    [Header("Scroll Settings")]
    [SerializeField] private float scrollSensitivity = 1.5f;
    [SerializeField] private float minY = -1.0f;
    [SerializeField] private float maxY = 1.0f;
    [SerializeField] private bool dragging;

    private Vector3 lastDragWorldPos;

    public void BeginDrag(Transform controllerTransform)
    {
        dragReference = controllerTransform;
        dragging = true;
        lastDragWorldPos = dragReference.position;
    }

    public void EndDrag()
    {
        dragging = false;
        dragReference = null;
    }

    private void Update()
    {
        if (!dragging || dragReference == null || contentRoot == null) return;

        Vector3 currentPos = dragReference.position;
        float deltaY = currentPos.y - lastDragWorldPos.y;

        Vector3 localPos = contentRoot.localPosition;
        localPos.y += deltaY * scrollSensitivity;
        localPos.y = Mathf.Clamp(localPos.y, minY, maxY);

        contentRoot.localPosition = localPos;
        lastDragWorldPos = currentPos;
    }
}