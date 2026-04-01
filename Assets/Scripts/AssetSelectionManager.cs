using UnityEngine;
using System;

public class AssetSelectionManager : MonoBehaviour
{
    public static AssetSelectionManager Instance { get; private set; }

    public PlaceableAsset SelectedAsset { get; private set; }

    public event Action<PlaceableAsset> OnSelectionChanged;

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
        SelectedAsset = asset;
        OnSelectionChanged?.Invoke(SelectedAsset);
    }

    public void ClearSelection()
    {
        SelectedAsset = null;
        OnSelectionChanged?.Invoke(null);
    }
}