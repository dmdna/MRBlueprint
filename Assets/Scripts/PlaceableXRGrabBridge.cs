using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Keeps <see cref="AssetSelectionManager"/> in sync when a placeable is grabbed via XR (ray / direct).
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(XRGrabInteractable))]
public sealed class PlaceableXRGrabBridge : MonoBehaviour
{
    private XRGrabInteractable _grab;
    private PlaceableAsset _placeable;
    private bool _defaultTrackRotation = true;

    private void Awake()
    {
        _grab = GetComponent<XRGrabInteractable>();
        _placeable = GetComponent<PlaceableAsset>();
        _defaultTrackRotation = _grab == null || _grab.trackRotation;
    }

    private void LateUpdate()
    {
        if (_grab == null || _placeable == null || !_grab.isSelected)
        {
            return;
        }

        _grab.trackRotation = _defaultTrackRotation
                              && !PlaceableMultiGrabCoordinator.IsPlaceableRotationLocked(_placeable);
    }

    private void OnEnable()
    {
        _grab.selectEntered.AddListener(OnSelectEntered);
        _grab.selectExited.AddListener(OnSelectExited);
    }

    private void OnDisable()
    {
        _grab.selectEntered.RemoveListener(OnSelectEntered);
        _grab.selectExited.RemoveListener(OnSelectExited);
        if (_placeable != null)
        {
            PlaceableMultiGrabCoordinator.NotifyExternalPlaceableGrabEnded(_placeable);
        }

        RestoreTrackRotation();
    }

    private void OnSelectEntered(SelectEnterEventArgs args)
    {
        if (_placeable != null)
        {
            PlaceableMultiGrabCoordinator.NotifyExternalPlaceableGrabStarted(_placeable);
        }

        if (_placeable != null && AssetSelectionManager.Instance != null)
            AssetSelectionManager.Instance.SelectAsset(_placeable);
    }

    private void OnSelectExited(SelectExitEventArgs args)
    {
        if (_placeable != null)
        {
            PlaceableMultiGrabCoordinator.NotifyExternalPlaceableGrabEnded(_placeable);
        }

        RestoreTrackRotation();
    }

    private void RestoreTrackRotation()
    {
        if (_grab != null)
        {
            _grab.trackRotation = _defaultTrackRotation;
        }
    }
}
