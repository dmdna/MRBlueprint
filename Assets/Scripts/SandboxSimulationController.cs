using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Phase C — sandbox simulation: snapshot on enter, exit/restart restore that snapshot, pause toggles <see cref="Physics.autoSimulation"/>.
/// While editing (not simulating), optional zero gravity on placeables; during sim, each object's gravity follows inspector preference.
/// </summary>
public sealed class SandboxSimulationController : MonoBehaviour
{
    public static SandboxSimulationController Instance { get; private set; }

    [Header("Edit vs sim")]
    [Tooltip("When true, rigidbodies use no gravity while editing (easier mid-air placement). Inspector Gravity toggle stores preference for simulate mode.")]
    [SerializeField] private bool zeroGravityWhileEditing = true;

    [Header("References (optional — filled by SandboxEditorToolbarFrame.Configure)")]
    [SerializeField] private XRContentDrawerController drawerController;
    [SerializeField] private PlaceableTransformGizmo transformGizmo;

    private readonly List<RigidbodySnapshot> _snapshot = new List<RigidbodySnapshot>(32);

    public bool IsSimulating { get; private set; }
    public bool IsPaused { get; private set; }
    public bool ZeroGravityInEdit => zeroGravityWhileEditing;

    public event Action StateChanged;

    private struct RigidbodySnapshot
    {
        public Rigidbody Rb;
        public Vector3 Pos;
        public Quaternion Rot;
        public Vector3 Vel;
        public Vector3 AngVel;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance != this)
            return;

        Instance = null;

        if (IsSimulating)
        {
            Physics.autoSimulation = true;
            if (transformGizmo != null)
                transformGizmo.enabled = true;
        }
    }

    public void Configure(XRContentDrawerController drawer, PlaceableTransformGizmo gizmo)
    {
        if (drawer != null)
            drawerController = drawer;
        if (gizmo != null)
            transformGizmo = gizmo;
    }

    public void EnterSimulation()
    {
        if (IsSimulating)
            return;

        SandboxEditorModeState.SetSessionMode(SandboxEditorSessionMode.Edit);

        CaptureSnapshot();

        if (drawerController != null && drawerController.IsOpen)
            drawerController.CloseDrawer();

        if (transformGizmo != null)
        {
            if (transformGizmo.IsDragging)
                transformGizmo.EndDrag();
            transformGizmo.enabled = false;
        }

        IsSimulating = true;
        IsPaused = false;
        Physics.autoSimulation = true;

        RefreshAllPlaceablesGravity();
        Notify();
    }

    /// <summary>Restore snapshot, leave edit mode, re-enable gizmo.</summary>
    public void ExitSimulation()
    {
        if (!IsSimulating)
            return;

        RestoreSnapshot();

        IsSimulating = false;
        IsPaused = false;
        Physics.autoSimulation = true;

        if (transformGizmo != null)
            transformGizmo.enabled = true;

        RefreshAllPlaceablesGravity();
        Notify();
    }

    public void SetPaused(bool paused)
    {
        if (!IsSimulating)
            return;

        IsPaused = paused;
        Physics.autoSimulation = !paused;
        Notify();
    }

    public void TogglePause()
    {
        if (!IsSimulating)
            return;
        SetPaused(!IsPaused);
    }

    /// <summary>Rewind to snapshot taken on <see cref="EnterSimulation"/> and keep sim running (unpaused).</summary>
    public void RestartSimulation()
    {
        if (!IsSimulating)
            return;

        RestoreSnapshot();
        IsPaused = false;
        Physics.autoSimulation = true;
        Notify();
    }

    public void RefreshAllPlaceablesGravity()
    {
        var list = FindObjectsByType<PlaceableAsset>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (var i = 0; i < list.Length; i++)
        {
            if (list[i] != null)
                list[i].ApplySandboxGravityPolicy();
        }
    }

    private void CaptureSnapshot()
    {
        _snapshot.Clear();
        var list = FindObjectsByType<PlaceableAsset>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (var i = 0; i < list.Length; i++)
        {
            var rb = list[i].Rigidbody;
            if (rb == null)
                continue;

            _snapshot.Add(new RigidbodySnapshot
            {
                Rb = rb,
                Pos = rb.position,
                Rot = rb.rotation,
                Vel = rb.linearVelocity,
                AngVel = rb.angularVelocity,
            });
        }
    }

    private void RestoreSnapshot()
    {
        for (var i = 0; i < _snapshot.Count; i++)
        {
            var s = _snapshot[i];
            if (s.Rb == null)
                continue;

            s.Rb.position = s.Pos;
            s.Rb.rotation = s.Rot;
            s.Rb.linearVelocity = s.Vel;
            s.Rb.angularVelocity = s.AngVel;
        }
    }

    private void Notify() => StateChanged?.Invoke();
}
