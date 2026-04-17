using UnityEngine;
using System;

public class AssetSelectionManager : MonoBehaviour
{
    public static AssetSelectionManager Instance { get; private set; }

    public PlaceableAsset SelectedAsset { get; private set; }
    public PhysicsDrawingSelectable SelectedPhysicsDrawing { get; private set; }
    public bool HasSelection => SelectedAsset != null || SelectedPhysicsDrawing != null;

    public event Action<PlaceableAsset> OnSelectionChanged;
    public event Action<PhysicsDrawingSelectable> OnPhysicsDrawingSelectionChanged;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    public void SelectAsset(PlaceableAsset asset)
    {
        if (SelectedPhysicsDrawing != null)
        {
            SelectedPhysicsDrawing.SetSelected(false);
            SelectedPhysicsDrawing = null;
            OnPhysicsDrawingSelectionChanged?.Invoke(null);
        }

        SelectedAsset = asset;
        OnSelectionChanged?.Invoke(SelectedAsset);
    }

    public void SelectPhysicsDrawing(PhysicsDrawingSelectable drawing)
    {
        if (SelectedAsset != null)
        {
            SelectedAsset = null;
            OnSelectionChanged?.Invoke(null);
        }

        if (SelectedPhysicsDrawing != null && SelectedPhysicsDrawing != drawing)
        {
            SelectedPhysicsDrawing.SetSelected(false);
        }

        SelectedPhysicsDrawing = drawing;
        if (SelectedPhysicsDrawing != null)
        {
            SelectedPhysicsDrawing.SetSelected(true);
        }

        OnPhysicsDrawingSelectionChanged?.Invoke(SelectedPhysicsDrawing);
    }

    public void ClearSelection()
    {
        if (SelectedPhysicsDrawing != null)
        {
            SelectedPhysicsDrawing.SetSelected(false);
        }

        SelectedAsset = null;
        SelectedPhysicsDrawing = null;
        OnSelectionChanged?.Invoke(null);
        OnPhysicsDrawingSelectionChanged?.Invoke(null);
    }
}
