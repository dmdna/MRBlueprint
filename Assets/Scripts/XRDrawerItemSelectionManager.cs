using UnityEngine;

public class XRDrawerItemSelectionManager : MonoBehaviour
{
    [SerializeField] private Transform xrCamera;
    [SerializeField] private XRDrawerSpawner drawerSpawner;

    private XRDrawerItem currentSelected;

    public void SelectItem(XRDrawerItem item)
    {
        if (item == null) return;

        if (currentSelected == item)
        {
            currentSelected.Select(xrCamera);
            return;
        }

        if (currentSelected != null && currentSelected != item)
        {
            currentSelected.Deselect(xrCamera);
        }

        currentSelected = item;
        currentSelected.Select(xrCamera);
        UiMenuSelectSoundHub.TryPlayFromInteraction();
    }

    /// <summary>
    /// Spawns only when a drawer tile with a valid prefab is selected. Clears tile selection after spawn.
    /// </summary>
    public bool TryConfirmSpawnSelected()
    {
        if (currentSelected == null)
        {
            return false;
        }

        var meshDrawingButton = currentSelected.GetComponentInParent<MXInkMeshDrawerButton>();
        if (meshDrawingButton != null)
        {
            meshDrawingButton.ToggleMeshDrawingMode();
            ClearSelection();
            UiMenuSelectSoundHub.TryPlayFromInteraction();
            return true;
        }

        if (currentSelected.SpawnPrefab == null)
        {
            return false;
        }

        if (drawerSpawner == null)
        {
            return false;
        }

        drawerSpawner.SpawnFromDrawerItem(currentSelected);
        ClearSelection();
        return true;
    }

    public void ClearSelection()
    {
        if (currentSelected != null)
        {
            currentSelected.Deselect(xrCamera);
            currentSelected = null;
        }
    }
}
