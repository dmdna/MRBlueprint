using UnityEngine;

/// <summary>
/// Selection for non–mouse paths (e.g. future XR direct). Editor mouse uses <see cref="SandboxEditorInputRouter"/>.
/// </summary>
public class SelectableAsset : MonoBehaviour
{
    private PlaceableAsset placeableAsset;

    private void Awake()
    {
        placeableAsset = GetComponent<PlaceableAsset>();
    }
}
