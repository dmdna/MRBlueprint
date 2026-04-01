using UnityEngine;

public class XRDrawerItemSelectionManager : MonoBehaviour
{
    [SerializeField] private Transform xrCamera;
    [SerializeField] private XRDrawerSpawner drawerSpawner;

    private XRDrawerItem currentSelected;

    public void SelectItem(XRDrawerItem item)
    {
        if (item == null) return;

        if (currentSelected != null && currentSelected != item)
        {
            currentSelected.Deselect(xrCamera);
        }

        currentSelected = item;
        currentSelected.Select(xrCamera);
    }

    public void ConfirmSpawnSelected()
    {
        if (currentSelected == null) return;

        if (drawerSpawner != null)
        {
            drawerSpawner.SpawnFromDrawerItem(currentSelected);
        }
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