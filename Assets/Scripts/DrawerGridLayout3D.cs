using UnityEngine;

/// <summary>Runs <see cref="Start"/> before <see cref="XRDrawerItem"/> so grid positions exist before tiles cache rest pose.</summary>
[DefaultExecutionOrder(-40)]
public class DrawerGridLayout3D : MonoBehaviour
{
    [SerializeField] private bool layoutOnStart = true;

    [SerializeField] private int columns = 3;
    [SerializeField] private float spacingX = 0.3f;
    [SerializeField] private float spacingY = 0.25f;
    [SerializeField] private float zOffset = -0.02f;

    private void Start()
    {
        if (layoutOnStart)
            LayoutChildren();
    }

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

            var drawerItem = child.GetComponent<XRDrawerItem>();
            if (drawerItem != null)
                drawerItem.SyncRestPoseFromTransform();
        }
    }
}