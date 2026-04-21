using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Soft menu-style click feedback for uGUI (mouse) and a few explicit call sites (XR / drawer).
/// </summary>
public sealed class UiMenuSelectSoundHub : MonoBehaviour
{
    [SerializeField] private AudioClip menuSelectClip;
    [SerializeField, Range(0f, 1f)] private float volume = 0.2f;
    [Tooltip("Avoid double triggers when multiple input paths handle the same click.")]
    [SerializeField] private float minSecondsBetweenPlays = 0.055f;

    private AudioSource _audio;
    private float _lastPlayUnscaled = -999f;
    private static UiMenuSelectSoundHub _instance;
    private readonly List<RaycastResult> _raycastScratch = new(16);

    private void Awake()
    {
        _instance = this;
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

    /// <summary>XR world UI and other code paths that do not go through mouse raycasts.</summary>
    public static void TryPlayFromInteraction()
    {
        if (_instance == null)
            _instance = FindFirstObjectByType<UiMenuSelectSoundHub>();
        _instance?.TryPlayInternal();
    }

    private void TryPlayInternal()
    {
        if (menuSelectClip == null || _audio == null)
            return;

        var t = Time.unscaledTime;
        if (t - _lastPlayUnscaled < minSecondsBetweenPlays)
            return;

        _lastPlayUnscaled = t;
        _audio.PlayOneShot(menuSelectClip, volume);
    }

    private void Update()
    {
        if (menuSelectClip == null || _audio == null)
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
