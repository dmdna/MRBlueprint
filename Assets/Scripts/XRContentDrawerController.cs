using System;
using UnityEngine;

public enum XRControlMode
{
    Drawing,
    Selection
}

public class XRContentDrawerController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform xrCamera;
    [SerializeField] private Transform drawerRoot;
    [SerializeField] private FrostedGlassPanelAnimator panelAnimator;
    [SerializeField] private XRDrawerItemSelectionManager drawerItemSelection;

    [Header("Placement")]
    [SerializeField] private float distanceFromCamera = 1.2f;
    [SerializeField] private float verticalOffset = -0.1f;
    [SerializeField] private bool faceCamera = true;

    [Header("State")]
    [SerializeField] private bool isOpen;

    public bool IsOpen => isOpen;
    public XRControlMode CurrentMode => isOpen ? XRControlMode.Selection : XRControlMode.Drawing;

    public event Action<XRControlMode> ControlModeChanged;

    private void Start()
    {
        if (!isOpen && drawerRoot != null)
        {
            drawerRoot.gameObject.SetActive(false);
        }

        ControlModeChanged?.Invoke(CurrentMode);
    }

    public void ToggleDrawer()
    {
        if (isOpen)
        {
            CloseDrawer();
        }
        else
        {
            OpenDrawer();
        }
    }

    public void OpenDrawer()
    {
        if (isOpen)
        {
            return;
        }

        PositionDrawerInFrontOfPlayer();

        if (drawerRoot != null)
        {
            drawerRoot.gameObject.SetActive(true);
        }

        isOpen = true;
        ControlModeChanged?.Invoke(CurrentMode);

        if (panelAnimator != null)
        {
            panelAnimator.Open();
        }
    }

    public void CloseDrawer()
    {
        if (!isOpen)
        {
            return;
        }

        isOpen = false;
        ControlModeChanged?.Invoke(CurrentMode);
        drawerItemSelection?.ClearSelection();

        if (panelAnimator != null)
        {
            panelAnimator.Close(() =>
            {
                if (drawerRoot != null)
                {
                    drawerRoot.gameObject.SetActive(false);
                }
            });
        }
        else if (drawerRoot != null)
        {
            drawerRoot.gameObject.SetActive(false);
        }
    }

    private void PositionDrawerInFrontOfPlayer()
    {
        if (xrCamera == null || drawerRoot == null) return;

        Vector3 forward = xrCamera.forward;
        forward.y = 0f;
        forward.Normalize();

        Vector3 targetPos = xrCamera.position + forward * distanceFromCamera;
        targetPos.y = xrCamera.position.y + verticalOffset;

        drawerRoot.position = targetPos;

        if (faceCamera)
        {
            Vector3 lookDir = drawerRoot.position - xrCamera.position;
            lookDir.y = 0f;
            if (lookDir.sqrMagnitude > 0.0001f)
            {
                drawerRoot.rotation = Quaternion.LookRotation(lookDir.normalized);
            }
        }
    }
}
