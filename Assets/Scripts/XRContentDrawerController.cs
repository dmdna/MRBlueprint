using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public enum XRControlMode
{
    Drawing = 0,
    Edit = 1,
    Selection = Edit
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

    [Header("Rendering")]
    [SerializeField] private bool depthSortRaysWithDrawerItems = true;
    [SerializeField] private int drawerItemDepthRenderQueue = (int)RenderQueue.Transparent - 10;

    private readonly List<Material> _runtimeDrawerMaterials = new();
    private bool _drawerRenderingConfigured;

    public bool IsOpen => isOpen;
    public Transform DrawerRoot => drawerRoot;
    public XRControlMode CurrentMode =>
        SandboxEditorModeState.Current == SandboxEditorSessionMode.Draw
            ? XRControlMode.Drawing
            : XRControlMode.Edit;

    public event Action<XRControlMode> ControlModeChanged;

    private void Start()
    {
        ConfigureDrawerContentRendering();

        if (!isOpen && drawerRoot != null)
        {
            drawerRoot.gameObject.SetActive(false);
        }

        ControlModeChanged?.Invoke(CurrentMode);
    }

    private void OnEnable()
    {
        SandboxEditorModeState.ModeChanged -= OnSandboxEditorModeChanged;
        SandboxEditorModeState.ModeChanged += OnSandboxEditorModeChanged;
    }

    private void OnDisable()
    {
        SandboxEditorModeState.ModeChanged -= OnSandboxEditorModeChanged;
    }

    private void OnDestroy()
    {
        for (var i = 0; i < _runtimeDrawerMaterials.Count; i++)
        {
            if (_runtimeDrawerMaterials[i] != null)
            {
                Destroy(_runtimeDrawerMaterials[i]);
            }
        }

        _runtimeDrawerMaterials.Clear();
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

        ConfigureDrawerContentRendering();
        PositionDrawerInFrontOfPlayer();

        if (drawerRoot != null)
        {
            drawerRoot.gameObject.SetActive(true);
        }

        isOpen = true;

        UiMenuSelectSoundHub.SuppressDefaultButtonSound();
        UiMenuSelectSoundHub.TryPlayDrawerOpen();

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

        if (MeshDrawingModeState.IsActive)
        {
            MeshDrawingModeState.SetActive(false);
        }

        UiMenuSelectSoundHub.SuppressDefaultButtonSound();
        UiMenuSelectSoundHub.TryPlayDrawerClose();

        isOpen = false;
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

    private void ConfigureDrawerContentRendering()
    {
        if (_drawerRenderingConfigured || !depthSortRaysWithDrawerItems || drawerRoot == null)
        {
            return;
        }

        var drawerItems = drawerRoot.GetComponentsInChildren<XRDrawerItem>(true);
        foreach (var drawerItem in drawerItems)
        {
            if (drawerItem == null)
            {
                continue;
            }

            var renderers = drawerItem.GetComponentsInChildren<Renderer>(true);
            foreach (var drawerRenderer in renderers)
            {
                if (drawerRenderer == null)
                {
                    continue;
                }

                var sharedMaterials = drawerRenderer.sharedMaterials;
                if (sharedMaterials == null || sharedMaterials.Length == 0)
                {
                    continue;
                }

                var runtimeMaterials = new Material[sharedMaterials.Length];
                for (var i = 0; i < sharedMaterials.Length; i++)
                {
                    var source = sharedMaterials[i];
                    if (source == null)
                    {
                        continue;
                    }

                    var material = new Material(source)
                    {
                        name = source.name + " (Drawer Runtime)",
                        renderQueue = drawerItemDepthRenderQueue
                    };

                    if (material.HasProperty("_ZWrite"))
                    {
                        material.SetFloat("_ZWrite", 1f);
                    }

                    _runtimeDrawerMaterials.Add(material);
                    runtimeMaterials[i] = material;
                }

                drawerRenderer.sharedMaterials = runtimeMaterials;
            }
        }

        _drawerRenderingConfigured = true;
    }

    private void OnSandboxEditorModeChanged(SandboxEditorSessionMode _)
    {
        ControlModeChanged?.Invoke(CurrentMode);
    }
}
