using UnityEngine;

public class SelectableAsset : MonoBehaviour
{
    private PlaceableAsset placeableAsset;

    private void Awake()
    {
        placeableAsset = GetComponent<PlaceableAsset>();
    }

    private void OnMouseDown()
    {
        // Works for desktop/testing if colliders exist.
        // For XR later, you can replace this with raycast-based selection.
        AssetSelectionManager.Instance.SelectAsset(placeableAsset);
    }
}