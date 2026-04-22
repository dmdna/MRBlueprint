using System;
using System.Collections;
using System.Threading.Tasks;
using Meta.XR.MRUtilityKit;
using UnityEngine;

public class MRSettingsController : MonoBehaviour
{
    private static readonly Color SolidBackgroundColor = new Color(0.0588f, 0.1216f, 0.1843f, 1f);

    [SerializeField] private MRUK mruk;
    [SerializeField] private MRPhysicsSurfaceManager surfaceManager;
    [SerializeField] private OVRPassthroughLayer[] passthroughLayers;
    [SerializeField] private Camera[] controlledCameras;

    private readonly System.Collections.Generic.Dictionary<Camera, CameraState> _cameraDefaults = new();
    private int _applyVersion;
    private int _lastRandomRoomIndex = -1;
    private bool _startupFlowStarted;
    private bool _fallbackMessageShown;

    public event Action StateChanged;
    public event Action RequestOpenSettings;

    public MRSettingsState State { get; private set; } = MRSettingsState.FloorOnlyFallbackDefaults();
    public MRSettingsRoomMode RoomMode { get; private set; } = MRSettingsRoomMode.FloorOnly;
    public bool RoomSetupAvailable { get; private set; }
    public bool IsApplying { get; private set; }
    public string StatusMessage { get; private set; } = "Checking Room Setup...";

    public bool RandomizeRoomAvailable => !State.UseRoomSetup && State.MRRoomEnabled && !IsApplying;
    public bool UseRoomSetupControlAvailable => !IsApplying;
    public bool BlueprintControlAvailable => !(State.MRRoomEnabled && !State.UseRoomSetup) && !IsApplying;

    private void Awake()
    {
        ResolveSceneReferences();
        CacheCameraDefaults();
        ApplyPassthrough(State.PassthroughEnabled);
    }

    private void Start()
    {
        if (!_startupFlowStarted)
        {
            _startupFlowStarted = true;
            StartCoroutine(RunStartupFlow());
        }
    }

    public void SetPassthroughEnabled(bool enabled)
    {
        var next = State;
        next.PassthroughEnabled = enabled;
        ApplyRequestedState(next, false);
    }

    public void SetMRRoomEnabled(bool enabled)
    {
        var next = State;
        next.MRRoomEnabled = enabled;
        ApplyRequestedState(next, false);
    }

    public void SetUseRoomSetup(bool enabled)
    {
        var next = State;
        next.UseRoomSetup = enabled;
        ApplyRequestedState(next, false);
    }

    public void SetBlueprintVisible(bool visible)
    {
        var next = State;
        next.BlueprintVisible = visible;
        ApplyRequestedState(next, false);
    }

    public void RandomizeRoom()
    {
        if (!RandomizeRoomAvailable)
        {
            return;
        }

        var next = State;
        next.UseRoomSetup = false;
        next.MRRoomEnabled = true;
        next.BlueprintVisible = true;
        ApplyRequestedState(next, false, true);
    }

    private IEnumerator RunStartupFlow()
    {
        ResolveSceneReferences();
        State = MRSettingsState.RealRoomDefaults();
        StateChanged?.Invoke();

        var loadTask = TryLoadRoomSetup(true);
        while (!loadTask.IsCompleted)
        {
            yield return null;
        }

        var roomSetupReady = loadTask.IsCompleted && loadTask.Result;
        RoomSetupAvailable = roomSetupReady;
        if (roomSetupReady)
        {
            StatusMessage = "Using Quest Room Setup.";
            RoomMode = MRSettingsRoomMode.RoomSetup;
            ApplyRequestedState(MRSettingsState.RealRoomDefaults(), true);
        }
        else
        {
            ShowFallbackMessageOnce();
            RoomMode = MRSettingsRoomMode.FloorOnly;
            ApplyRequestedState(MRSettingsState.FloorOnlyFallbackDefaults(), true);
            RequestOpenSettings?.Invoke();
        }
    }

    private void ApplyRequestedState(MRSettingsState requested, bool startupDecision, bool forceRandomize = false)
    {
        var resolved = ResolveDependencies(requested, startupDecision);
        State = resolved;
        StateChanged?.Invoke();
        _ = ApplyResolvedStateAsync(resolved, forceRandomize);
    }

    private MRSettingsState ResolveDependencies(MRSettingsState requested, bool startupDecision)
    {
        var resolved = requested;

        // Dependency rule: Room Setup is a full-room mode, so enabling it always enables MR Room.
        if (resolved.UseRoomSetup)
        {
            resolved.MRRoomEnabled = true;
        }

        // Dependency rule: floor-only mode cannot use Room Setup.
        if (!resolved.MRRoomEnabled)
        {
            resolved.UseRoomSetup = false;
        }

        // Dependency rule: randomized rooms keep their blueprint visible to avoid implying real-room alignment.
        if (resolved.MRRoomEnabled && !resolved.UseRoomSetup)
        {
            resolved.BlueprintVisible = true;
        }

        return resolved;
    }

    private async Task ApplyResolvedStateAsync(MRSettingsState resolved, bool forceRandomize)
    {
        var version = ++_applyVersion;
        IsApplying = true;
        StateChanged?.Invoke();

        ResolveSceneReferences();
        ApplyPassthrough(resolved.PassthroughEnabled);

        if (!resolved.MRRoomEnabled)
        {
            RoomMode = MRSettingsRoomMode.FloorOnly;
            ClearLoadedRooms();
            ApplySurfaceMode(effectMeshesEnabled: false, effectMeshVisible: false, forceFallbackFloor: true,
                fallbackFloorVisible: resolved.BlueprintVisible);
            StatusMessage = "Floor Only mode.";
            CompleteApply(version);
            return;
        }

        if (resolved.UseRoomSetup)
        {
            var loaded = RoomMode == MRSettingsRoomMode.RoomSetup && HasLoadedRooms();
            if (!loaded)
            {
                StatusMessage = RoomSetupAvailable ? "Loading Quest Room Setup..." : "Starting Quest Room Setup...";
                StateChanged?.Invoke();
                loaded = await TryLoadRoomSetup(!RoomSetupAvailable);
            }

            if (version != _applyVersion)
            {
                return;
            }

            if (loaded)
            {
                RoomSetupAvailable = true;
                RoomMode = MRSettingsRoomMode.RoomSetup;
                ApplySurfaceMode(effectMeshesEnabled: true, effectMeshVisible: resolved.BlueprintVisible,
                    forceFallbackFloor: false, fallbackFloorVisible: false);
                StatusMessage = "Using Quest Room Setup.";
            }
            else
            {
                RoomSetupAvailable = false;
                ShowFallbackMessageOnce();
                State = MRSettingsState.FloorOnlyFallbackDefaults();
                RoomMode = MRSettingsRoomMode.FloorOnly;
                ClearLoadedRooms();
                ApplySurfaceMode(effectMeshesEnabled: false, effectMeshVisible: false, forceFallbackFloor: true,
                    fallbackFloorVisible: State.BlueprintVisible);
                RequestOpenSettings?.Invoke();
            }

            CompleteApply(version);
            return;
        }

        var randomLoaded = !forceRandomize && RoomMode == MRSettingsRoomMode.RandomizedRoom && HasLoadedRooms();
        if (!randomLoaded)
        {
            randomLoaded = await LoadRandomRoomPrefab();
        }

        if (version != _applyVersion)
        {
            return;
        }

        if (randomLoaded)
        {
            RoomMode = MRSettingsRoomMode.RandomizedRoom;
            ApplySurfaceMode(effectMeshesEnabled: true, effectMeshVisible: true, forceFallbackFloor: false,
                fallbackFloorVisible: false);
            StatusMessage = "Using randomized room.";
        }
        else
        {
            ShowFallbackMessageOnce();
            State = MRSettingsState.FloorOnlyFallbackDefaults();
            RoomMode = MRSettingsRoomMode.FloorOnly;
            ClearLoadedRooms();
            ApplySurfaceMode(effectMeshesEnabled: false, effectMeshVisible: false, forceFallbackFloor: true,
                fallbackFloorVisible: State.BlueprintVisible);
            RequestOpenSettings?.Invoke();
        }

        CompleteApply(version);
    }

    private void CompleteApply(int version)
    {
        if (version != _applyVersion)
        {
            return;
        }

        IsApplying = false;
        StateChanged?.Invoke();
    }

    private async Task<bool> TryLoadRoomSetup(bool requestSpaceSetup)
    {
        ResolveSceneReferences();
        if (mruk == null)
        {
            return false;
        }

        try
        {
            var result = await mruk.LoadSceneFromDevice(requestSpaceSetup);
            return result == MRUK.LoadDeviceResult.Success && HasLoadedRooms();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"MRSettingsController: Room Setup load failed: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> LoadRandomRoomPrefab()
    {
        ResolveSceneReferences();
        var prefabs = mruk != null && mruk.SceneSettings != null ? mruk.SceneSettings.RoomPrefabs : null;
        if (mruk == null || prefabs == null || prefabs.Length == 0)
        {
            return false;
        }

        var index = UnityEngine.Random.Range(0, prefabs.Length);
        if (prefabs.Length > 1)
        {
            var guard = 0;
            while (index == _lastRandomRoomIndex && guard++ < 8)
            {
                index = UnityEngine.Random.Range(0, prefabs.Length);
            }
        }

        var prefab = prefabs[index];
        if (prefab == null)
        {
            return false;
        }

        try
        {
            var result = await mruk.LoadSceneFromPrefab(prefab, true);
            _lastRandomRoomIndex = index;
            return result == MRUK.LoadDeviceResult.Success && HasLoadedRooms();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"MRSettingsController: Random room load failed: {ex.Message}");
            return false;
        }
    }

    private void ApplySurfaceMode(bool effectMeshesEnabled, bool effectMeshVisible, bool forceFallbackFloor,
        bool fallbackFloorVisible)
    {
        ResolveSceneReferences();
        if (surfaceManager == null)
        {
            return;
        }

        surfaceManager.ApplyMRSettingsSurfaceMode(
            effectMeshesEnabled,
            effectMeshCollidersEnabled: effectMeshesEnabled,
            effectMeshVisible,
            forceFallbackFloor,
            fallbackFloorVisible);
    }

    private void ApplyPassthrough(bool enabled)
    {
        ResolveSceneReferences();

        if (passthroughLayers != null)
        {
            for (var i = 0; i < passthroughLayers.Length; i++)
            {
                var layer = passthroughLayers[i];
                if (layer == null)
                {
                    continue;
                }

                layer.hidden = !enabled;
                if (enabled && !layer.gameObject.activeSelf)
                {
                    layer.gameObject.SetActive(true);
                }
            }
        }

        if (enabled && OVRManager.instance != null)
        {
            OVRManager.instance.isInsightPassthroughEnabled = true;
        }

        ApplyCameraBackground(enabled);
    }

    private void ApplyCameraBackground(bool passthroughEnabled)
    {
        ResolveSceneReferences();
        if (controlledCameras == null)
        {
            return;
        }

        for (var i = 0; i < controlledCameras.Length; i++)
        {
            var camera = controlledCameras[i];
            if (camera == null)
            {
                continue;
            }

            if (!_cameraDefaults.ContainsKey(camera))
            {
                _cameraDefaults[camera] = new CameraState(camera);
            }

            if (passthroughEnabled)
            {
                _cameraDefaults[camera].Restore(camera);
            }
            else
            {
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = SolidBackgroundColor;
            }
        }
    }

    private void CacheCameraDefaults()
    {
        ResolveSceneReferences();
        _cameraDefaults.Clear();
        if (controlledCameras == null)
        {
            return;
        }

        for (var i = 0; i < controlledCameras.Length; i++)
        {
            var camera = controlledCameras[i];
            if (camera != null)
            {
                _cameraDefaults[camera] = new CameraState(camera);
            }
        }
    }

    private void ClearLoadedRooms()
    {
        if (mruk != null && HasLoadedRooms())
        {
            mruk.ClearScene();
        }
    }

    private bool HasLoadedRooms()
    {
        return mruk != null && mruk.Rooms != null && mruk.Rooms.Count > 0;
    }

    private void ShowFallbackMessageOnce()
    {
        StatusMessage = "Room Setup not available. Using fallback MR mode.";
        if (_fallbackMessageShown)
        {
            return;
        }

        _fallbackMessageShown = true;
        Debug.Log(StatusMessage);
    }

    private void ResolveSceneReferences()
    {
        if (mruk == null)
        {
            mruk = MRUK.Instance != null
                ? MRUK.Instance
                : FindFirstObjectByType<MRUK>(FindObjectsInactive.Include);
        }

        if (surfaceManager == null)
        {
            surfaceManager = FindFirstObjectByType<MRPhysicsSurfaceManager>(FindObjectsInactive.Include);
        }

        if (passthroughLayers == null || passthroughLayers.Length == 0)
        {
            passthroughLayers = FindObjectsByType<OVRPassthroughLayer>(FindObjectsInactive.Include,
                FindObjectsSortMode.None);
        }

        if (controlledCameras == null || controlledCameras.Length == 0)
        {
            controlledCameras = FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        }
    }

    private readonly struct CameraState
    {
        private readonly CameraClearFlags _clearFlags;
        private readonly Color _backgroundColor;

        public CameraState(Camera camera)
        {
            _clearFlags = camera.clearFlags;
            _backgroundColor = camera.backgroundColor;
        }

        public void Restore(Camera camera)
        {
            camera.clearFlags = _clearFlags;
            camera.backgroundColor = _backgroundColor;
        }
    }
}
