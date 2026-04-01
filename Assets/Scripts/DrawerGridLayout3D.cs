using UnityEngine;

public class DrawerGridLayout3D : MonoBehaviour
{
    [SerializeField] private int columns = 3;
    [SerializeField] private float spacingX = 0.3f;
    [SerializeField] private float spacingY = 0.25f;
    [SerializeField] private float zOffset = -0.02f;

    [ContextMenu("Layout Children")]
    public void LayoutChildren()
    {
        int childCount = transform.childCount;
        if (childCount == 0) return;

        int rows = Mathf.CeilToInt(childCount / (float)columns);

        for (int i = 0; i < childCount; i++)
        {
            Transform child = transform.GetChild(i);

            int row = i / columns;
            int col = i % columns;

            float totalWidth = (columns - 1) * spacingX;
            float totalHeight = (rows - 1) * spacingY;

            float x = col * spacingX - totalWidth * 0.5f;
            float y = totalHeight * 0.5f - row * spacingY;
            float z = zOffset;

            child.localPosition = new Vector3(x, y, z);
        }
    }
}