using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Default UI click feedback (dashboard), plus a few dedicated clips (drawer, delete, draw/clear tools).
/// </summary>
public sealed class UiMenuSelectSoundHub : MonoBehaviour
{
    private const string SoundEffectsMutedPrefsKey = "MRBlueprint.SoundEffectsMuted";

    [Header("Default UI")]
    [Tooltip("Plays for generic buttons/toggles/dropdowns and explicit TryPlayFromInteraction call sites.")]
    [SerializeField] private AudioClip menuSelectClip;
    [SerializeField, Range(0f, 1f)] private float volume = 0.2f;

    [Header("Dedicated one-shots")]
    [SerializeField] private AudioClip drawerOpenClip;
    [SerializeField] private AudioClip drawerCloseClip;
    [SerializeField] private AudioClip deleteObjectClip;
    [SerializeField] private AudioClip scissorCutClip;
    [SerializeField] private AudioClip clearSceneClip;
    [SerializeField, Range(0f, 1f)] private float effectVolume = 0.28f;

    [Tooltip("Avoid double triggers when multiple input paths handle the same click.")]
    [SerializeField] private float minSecondsBetweenPlays = 0.055f;

    private AudioSource _audio;
    private float _lastPlayUnscaled = -999f;
    private static UiMenuSelectSoundHub _instance;
    private static float _suppressDefaultUntilUnscaled = -999f;
    private static bool _soundEffectsMuted;
    private static bool _mutePreferenceLoaded;
    private readonly List<RaycastResult> _raycastScratch = new(16);

    public static bool SoundEffectsMuted
    {
        get
        {
            EnsureMutePreferenceLoaded();
            return _soundEffectsMuted;
        }
    }

    public static void SetSoundEffectsMuted(bool muted)
    {
        EnsureMutePreferenceLoaded();
        if (_soundEffectsMuted == muted)
        {
            return;
        }

        _soundEffectsMuted = muted;
        PlayerPrefs.SetInt(SoundEffectsMutedPrefsKey, muted ? 1 : 0);
        PlayerPrefs.Save();

        if (muted && _instance != null && _instance._audio != null)
        {
            _instance._audio.Stop();
        }
    }

    private static void EnsureMutePreferenceLoaded()
    {
        if (_mutePreferenceLoaded)
        {
            return;
        }

        _soundEffectsMuted = PlayerPrefs.GetInt(SoundEffectsMutedPrefsKey, 0) != 0;
        _mutePreferenceLoaded = true;
    }

    private void Awake()
    {
        _instance = this;
        EnsureMutePreferenceLoaded();
        _audio = GetComponent<AudioSource>();
        if (_audio == null)
            _audio = gameObject.AddComponent<AudioSource>();
        _audio.playOnAwake = false;
        _audio.loop = false;
        _audio.spatialBlend = 0f;
        _audio.dopplerLevel = 0f;
    }

    private void OnDestroy()
    {
        if (_instance == this)
            _instance = null;
    }

    /// <summary>Skips the next default menu clicks (e.g. after drawer open/close so the toolbar button does not also beep).</summary>
    public static void SuppressDefaultButtonSound(float seconds = 0.14f)
    {
        var until = Time.unscaledTime + seconds;
        if (until > _suppressDefaultUntilUnscaled)
            _suppressDefaultUntilUnscaled = until;
    }

    /// <summary>XR world UI and other code paths that do not go through mouse raycasts.</summary>
    public static void TryPlayFromInteraction()
    {
        if (_instance == null)
            _instance = FindFirstObjectByType<UiMenuSelectSoundHub>();
        _instance?.TryPlayInternal();
    }

    public static void TryPlayDrawerOpen()
    {
        if (_instance == null)
            _instance = FindFirstObjectByType<UiMenuSelectSoundHub>();
        _instance?.PlayEffect(_instance.drawerOpenClip);
    }

    public static void TryPlayDrawerClose()
    {
        if (_instance == null)
            _instance = FindFirstObjectByType<UiMenuSelectSoundHub>();
        _instance?.PlayEffect(_instance.drawerCloseClip);
    }

    public static void TryPlayDeleteObject()
    {
        SuppressDefaultButtonSound();
        if (_instance == null)
            _instance = FindFirstObjectByType<UiMenuSelectSoundHub>();
        _instance?.PlayEffect(_instance.deleteObjectClip);
    }

    public static void TryPlayScissorCut()
    {
        if (_instance == null)
            _instance = FindFirstObjectByType<UiMenuSelectSoundHub>();
        _instance?.PlayEffect(_instance.scissorCutClip);
    }

    public static void TryPlayClearScene()
    {
        if (_instance == null)
            _instance = FindFirstObjectByType<UiMenuSelectSoundHub>();
        _instance?.PlayEffect(_instance.clearSceneClip);
    }

    private void PlayEffect(AudioClip clip)
    {
        if (SoundEffectsMuted || clip == null || _audio == null)
            return;

        var t = Time.unscaledTime;
        if (t - _lastPlayUnscaled < minSecondsBetweenPlays)
            return;

        _lastPlayUnscaled = t;
        _audio.PlayOneShot(clip, effectVolume);
    }

    private void TryPlayInternal()
    {
        if (SoundEffectsMuted || menuSelectClip == null || _audio == null)
            return;

        var t = Time.unscaledTime;
        if (t < _suppressDefaultUntilUnscaled)
            return;

        if (t - _lastPlayUnscaled < minSecondsBetweenPlays)
            return;

        _lastPlayUnscaled = t;
        _audio.PlayOneShot(menuSelectClip, volume);
    }

    private void Update()
    {
        if (SoundEffectsMuted || menuSelectClip == null || _audio == null)
            return;

        var mouse = Mouse.current;
        if (mouse == null || !mouse.leftButton.wasReleasedThisFrame)
            return;

        if (EventSystem.current == null)
            return;

        var ped = new PointerEventData(EventSystem.current)
        {
            position = mouse.position.ReadValue()
        };

        _raycastScratch.Clear();
        EventSystem.current.RaycastAll(ped, _raycastScratch);
        if (_raycastScratch.Count == 0)
            return;

        var go = _raycastScratch[0].gameObject;
        if (go == null)
            return;

        if (go.GetComponentInParent<Button>() == null
            && go.GetComponentInParent<Toggle>() == null
            && go.GetComponentInParent<Dropdown>() == null)
            return;

        TryPlayInternal();
    }
}
