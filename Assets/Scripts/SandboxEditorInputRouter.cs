using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Editor-friendly input: raycasts from a camera for drawer tiles and placeable assets,
/// toggles the content drawer from D, toggles the MR toolbar from either controller primary button,
/// and confirms spawn with Space.
/// Placeables: click without moving opens the inspector; drag moves the object in the plane
/// facing the camera (includes vertical / "into the air" motion, not only along the floor).
/// When a transform gizmo is present, handles are tried first (move / rotate / scale axes).
/// </summary>
public class SandboxEditorInputRouter : MonoBehaviour
{
    private static readonly UnityEngine.XR.InputDeviceCharacteristics LeftControllerCharacteristics =
        UnityEngine.XR.InputDeviceCharacteristics.Left | UnityEngine.XR.InputDeviceCharacteristics.Controller;

    private static readonly UnityEngine.XR.InputDeviceCharacteristics RightControllerCharacteristics =
        UnityEngine.XR.InputDeviceCharacteristics.Right | UnityEngine.XR.InputDeviceCharacteristics.Controller;

    [SerializeField] private Camera viewCamera;
    [SerializeField] private XRContentDrawerController drawerController;
    [SerializeField] private XRDrawerItemSelectionManager drawerItemSelection;
    [SerializeField] private PlaceableTransformGizmo transformGizmo;
    [SerializeField] private SandboxEditorToolbarFrame toolbarFrame;
    [SerializeField] private float maxRayDistance = 100f;
    [SerializeField] private LayerMask raycastMask = ~0;

    [Header("Placeable mouse drag")]
    [SerializeField] private float dragThresholdPixels = 10f;

    private PlaceableAsset _placeablePressCandidate;
    private Rigidbody _placeablePressRigidbody;
    private PhysicsDrawingSelectable _drawingPressCandidate;
    private Vector2 _placeablePressScreen;
    private bool _placeableDragging;

    private Plane _placeableDragPlane;
    private Vector3 _placeableDragGrabOffset;
    private bool _placeableDragPlaneReady;

    private readonly List<UnityEngine.XR.InputDevice> _xrDevices = new();
    private bool _leftPrimaryWasPressed;
    private bool _rightPrimaryWasPressed;
    private bool _leftSecondaryWasPressed;
    private bool _rightSecondaryWasPressed;
    private bool _leftMenuWasPressed;

    private void Reset()
    {
        viewCamera = Camera.main;
    }

    private void Update()
    {
        if (Keyboard.current != null)
        {
            if (Keyboard.current.dKey.wasPressedThisFrame && drawerController != null)
                drawerController.ToggleDrawer();

            if (Keyboard.current.spaceKey.wasPressedThisFrame
                && drawerItemSelection != null
                && drawerController != null
                && drawerController.IsOpen)
            {
                drawerItemSelection.TryConfirmSpawnSelected();
            }
        }

        var leftPrimaryPressed = IsControllerPrimaryPressed(LeftControllerCharacteristics);
        var rightPrimaryPressed = IsControllerPrimaryPressed(RightControllerCharacteristics);
        var primaryPressedThisFrame =
            (leftPrimaryPressed && !_leftPrimaryWasPressed) ||
            (rightPrimaryPressed && !_rightPrimaryWasPressed);

        _leftPrimaryWasPressed = leftPrimaryPressed;
        _rightPrimaryWasPressed = rightPrimaryPressed;

        if (primaryPressedThisFrame)
        {
            ResolveToolbarFrame();
            if (toolbarFrame != null)
                toolbarFrame.ToggleToolbarVisible();
        }

        var leftSecondaryPressed = IsControllerSecondaryPressed(LeftControllerCharacteristics);
        var rightSecondaryPressed = IsControllerSecondaryPressed(RightControllerCharacteristics);
        var secondaryPressedThisFrame =
            (leftSecondaryPressed && !_leftSecondaryWasPressed) ||
            (rightSecondaryPressed && !_rightSecondaryWasPressed);

        _leftSecondaryWasPressed = leftSecondaryPressed;
        _rightSecondaryWasPressed = rightSecondaryPressed;

        if (secondaryPressedThisFrame)
        {
            ResolveToolbarFrame();
            if (toolbarFrame != null)
                toolbarFrame.ToggleSimulationShortcut();
        }

        var leftMenuPressed = IsControllerMenuPressed(LeftControllerCharacteristics);
        if (leftMenuPressed && !_leftMenuWasPressed)
        {
            ResolveToolbarFrame();
            if (toolbarFrame != null)
                toolbarFrame.ToggleOptionsVisible();
        }

        _leftMenuWasPressed = leftMenuPressed;

        if (Mouse.current == null)
            return;

        var mouse = Mouse.current;
        var screenPos = mouse.position.ReadValue();

        if (mouse.leftButton.wasReleasedThisFrame)
        {
            var gizmoWasDragging = transformGizmo != null && transformGizmo.IsDragging;
            if (gizmoWasDragging)
                transformGizmo.EndDrag();

            if (!gizmoWasDragging
                && !_placeableDragging
                && _placeablePressCandidate != null
                && AssetSelectionManager.Instance != null)
                AssetSelectionManager.Instance.SelectAsset(_placeablePressCandidate);
            else if (!gizmoWasDragging
                     && !_placeableDragging
                     && _drawingPressCandidate != null
                     && AssetSelectionManager.Instance != null)
                AssetSelectionManager.Instance.SelectPhysicsDrawing(_drawingPressCandidate);

            _placeablePressCandidate = null;
            _placeablePressRigidbody = null;
            _drawingPressCandidate = null;
            _placeableDragging = false;
            _placeableDragPlaneReady = false;
        }

        var dragCamera = viewCamera != null ? viewCamera : Camera.main;
        if (mouse.leftButton.isPressed && dragCamera != null)
        {
            var dragRay = dragCamera.ScreenPointToRay(screenPos);
            if (transformGizmo != null && transformGizmo.IsDragging)
                transformGizmo.Drag(dragRay, dragCamera);
            else if (_placeablePressCandidate != null
                     && _placeablePressRigidbody != null)
            {
                if (!_placeableDragging
                    && Vector2.Distance(screenPos, _placeablePressScreen) >= dragThresholdPixels)
                {
                    _placeableDragging = true;
                    TryBeginPlaceableViewPlaneDrag(screenPos);
                }

                if (_placeableDragging)
                {
                    if (AssetSelectionManager.Instance != null
                        && AssetSelectionManager.Instance.SelectedAsset == _placeablePressCandidate)
                    {
                        AssetSelectionManager.Instance.ClearSelection();
                    }

                    TryMovePlaceableInViewPlane(screenPos);
                }
            }
        }

        if (!mouse.leftButton.wasPressedThisFrame)
            return;

        if (IsPointerOverUi())
            return;

        if (viewCamera == null)
            viewCamera = Camera.main;

        if (viewCamera == null)
            return;

        var ray = viewCamera.ScreenPointToRay(screenPos);
        if (transformGizmo != null && transformGizmo.TryBeginDrag(ray, maxRayDistance, viewCamera))
            return;
        var hits = Physics.RaycastAll(ray, maxRayDistance, raycastMask, QueryTriggerInteraction.Collide);
        if (hits == null || hits.Length == 0)
        {
            _placeablePressCandidate = null;
            _placeablePressRigidbody = null;
            _drawingPressCandidate = null;
            if (AssetSelectionManager.Instance != null)
                AssetSelectionManager.Instance.ClearSelection();
            return;
        }

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (var hit in hits)
        {
            if (hit.collider.GetComponent<DrawerTilePickTarget>() == null)
                continue;

            var drawerItem = hit.collider.GetComponentInParent<XRDrawerItem>();
            if (drawerItem != null && drawerItemSelection != null)
            {
                _placeablePressCandidate = null;
                _placeablePressRigidbody = null;
                _drawingPressCandidate = null;
                drawerItemSelection.SelectItem(drawerItem);
                return;
            }
        }

        foreach (var hit in hits)
        {
            if (hit.collider.GetComponent<GizmoHandlePart>() != null)
                continue;

            var drawing = hit.collider.GetComponentInParent<PhysicsDrawingSelectable>();
            if (drawing != null)
            {
                _placeablePressCandidate = null;
                _placeablePressRigidbody = null;
                _drawingPressCandidate = drawing;
                _placeableDragging = false;
                _placeableDragPlaneReady = false;
                return;
            }
        }

        foreach (var hit in hits)
        {
            if (hit.collider.GetComponent<GizmoHandlePart>() != null)
                continue;

            var placeable = hit.collider.GetComponentInParent<PlaceableAsset>();
            if (placeable != null)
            {
                _placeablePressCandidate = placeable;
                _placeablePressRigidbody = placeable.Rigidbody;
                _drawingPressCandidate = null;
                _placeablePressScreen = screenPos;
                _placeableDragging = false;
                _placeableDragPlaneReady = false;
                return;
            }
        }

        _placeablePressCandidate = null;
        _placeablePressRigidbody = null;
        _drawingPressCandidate = null;
        if (AssetSelectionManager.Instance != null)
            AssetSelectionManager.Instance.ClearSelection();
    }

    private bool IsControllerPrimaryPressed(UnityEngine.XR.InputDeviceCharacteristics characteristics)
    {
        return IsControllerButtonPressed(characteristics, UnityEngine.XR.CommonUsages.primaryButton);
    }

    private bool IsControllerSecondaryPressed(UnityEngine.XR.InputDeviceCharacteristics characteristics)
    {
        return IsControllerButtonPressed(characteristics, UnityEngine.XR.CommonUsages.secondaryButton);
    }

    private bool IsControllerMenuPressed(UnityEngine.XR.InputDeviceCharacteristics characteristics)
    {
        return IsControllerButtonPressed(characteristics, UnityEngine.XR.CommonUsages.menuButton);
    }

    private bool IsControllerButtonPressed(
        UnityEngine.XR.InputDeviceCharacteristics characteristics,
        UnityEngine.XR.InputFeatureUsage<bool> buttonUsage)
    {
        _xrDevices.Clear();
        UnityEngine.XR.InputDevices.GetDevicesWithCharacteristics(characteristics, _xrDevices);

        for (var i = 0; i < _xrDevices.Count; i++)
        {
            var device = _xrDevices[i];
            if (!device.isValid || IsLogitechStylus(device))
                continue;

            if (device.TryGetFeatureValue(buttonUsage, out var pressed) && pressed)
                return true;
        }

        return false;
    }

    private void ResolveToolbarFrame()
    {
        if (toolbarFrame != null)
        {
            return;
        }

        toolbarFrame = FindFirstObjectByType<SandboxEditorToolbarFrame>(FindObjectsInactive.Include);
    }

    private static bool IsLogitechStylus(UnityEngine.XR.InputDevice device)
    {
        return ContainsDeviceText(device.name, "Logitech")
               || ContainsDeviceText(device.name, "MX Ink")
               || ContainsDeviceText(device.name, "Stylus")
               || ContainsDeviceText(device.manufacturer, "Logitech");
    }

    private static bool ContainsDeviceText(string value, string match)
    {
        return !string.IsNullOrEmpty(value)
               && value.IndexOf(match, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// Fixed plane through the object when drag starts, facing the camera; mouse motion maps to 3D including Y.
    /// </summary>
    private void TryBeginPlaceableViewPlaneDrag(Vector2 screenPos)
    {
        var rb = _placeablePressRigidbody;
        if (rb == null)
            return;

        var cam = viewCamera.transform;
        _placeableDragPlane = new Plane(-cam.forward, rb.position);

        var ray = viewCamera.ScreenPointToRay(screenPos);
        if (_placeableDragPlane.Raycast(ray, out var dist))
        {
            var hitPoint = ray.GetPoint(dist);
            _placeableDragGrabOffset = rb.position - hitPoint;
            _placeableDragPlaneReady = true;
        }
        else
        {
            _placeableDragGrabOffset = Vector3.zero;
            _placeableDragPlaneReady = false;
        }
    }

    private void TryMovePlaceableInViewPlane(Vector2 screenPos)
    {
        var rb = _placeablePressRigidbody;
        if (rb == null)
            return;

        if (!_placeableDragPlaneReady)
            TryBeginPlaceableViewPlaneDrag(screenPos);

        if (!_placeableDragPlaneReady)
            return;

        var ray = viewCamera.ScreenPointToRay(screenPos);
        if (!_placeableDragPlane.Raycast(ray, out var dist))
            return;

        rb.position = ray.GetPoint(dist) + _placeableDragGrabOffset;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
    }

    private static bool IsPointerOverUi()
    {
        if (EventSystem.current == null || Mouse.current == null)
            return false;

        var eventData = new PointerEventData(EventSystem.current)
        {
            position = Mouse.current.position.ReadValue()
        };

        var results = new System.Collections.Generic.List<RaycastResult>();
        EventSystem.current.RaycastAll(eventData, results);
        return results.Count > 0;
    }
}
