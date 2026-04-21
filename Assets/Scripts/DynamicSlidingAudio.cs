using UnityEngine;

/// <summary>
/// During sandbox simulation: looping 3D air-rush while the body has clear air below and is moving downward under gravity.
/// Volume follows downward speed (smoothed). Uses a short ground probe (not collision counts) so free-fall is detected reliably.
/// </summary>
[RequireComponent(typeof(Rigidbody), typeof(AudioSource))]
public sealed class DynamicSlidingAudio : MonoBehaviour
{
    [SerializeField] private AudioClip slidingClip;

    [Tooltip("Max distance below center-of-mass to search for support. If nothing (except self) is this close, treat as in-air for audio.")]
    [SerializeField] private float groundProbeDistance = 0.35f;

    [SerializeField] private LayerMask groundCheckMask = Physics.DefaultRaycastLayers;

    [Tooltip("Downward speed (m/s) at or above which normalized loudness begins (before maxVolume).")]
    [SerializeField] private float minSpeedForSound = 0.1f;

    [Tooltip("Downward speed (m/s) at or above which normalized loudness reaches 1 (before maxVolume).")]
    [SerializeField] private float maxSpeedForFullVolume = 7f;

    [SerializeField] private float fadeInSpeed = 4f;

    [SerializeField] private float fadeOutSpeed = 5.5f;

    [Tooltip("Final volume is smoothed loudness * this (0–1 typical).")]
    [SerializeField] private float maxVolume = 0.65f;

    [Tooltip("Downward speeds below this map to zero (reduces jitter).")]
    [SerializeField] private float pauseThreshold = 0.04f;

    private Rigidbody _rb;
    private AudioSource _audio;
    private readonly RaycastHit[] _groundHits = new RaycastHit[12];
    private float _smoothedLoudness;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        UnityEngine.Assertions.Assert.IsNotNull(_rb);
        _audio = GetComponent<AudioSource>();
        _audio.loop = true;
        _audio.playOnAwake = false;
        _audio.spatialBlend = 1f;
        if (slidingClip != null)
            _audio.clip = slidingClip;
    }

    private void FixedUpdate()
    {
        var sim = SandboxSimulationController.Instance
                  ?? UnityEngine.Object.FindFirstObjectByType<SandboxSimulationController>();

        var simRunning = sim != null && sim.IsSimulating && !sim.IsPaused;

        var goal = 0f;
        if (simRunning
            && _rb != null
            && !_rb.isKinematic
            && _rb.useGravity
            && !HasForeignGroundWithinProbe())
        {
            var vDown = Mathf.Max(0f, -_rb.linearVelocity.y);
            if (vDown < pauseThreshold)
                vDown = 0f;
            if (vDown >= minSpeedForSound)
                goal = Mathf.Clamp01(Mathf.InverseLerp(minSpeedForSound, maxSpeedForFullVolume, vDown));
        }

        var dt = Time.fixedDeltaTime;
        var fadeT = goal > _smoothedLoudness
            ? Mathf.Clamp01(fadeInSpeed * dt)
            : Mathf.Clamp01(fadeOutSpeed * dt);
        _smoothedLoudness = Mathf.Lerp(_smoothedLoudness, goal, fadeT);

        var vol = _smoothedLoudness * maxVolume;
        _audio.volume = vol;

        if (vol <= 1e-4f)
        {
            if (_audio.isPlaying)
                _audio.Pause();
        }
        else
        {
            if (!_audio.isPlaying && _audio.clip != null)
                _audio.Play();
        }
    }

    /// <summary>True if a non-self collider lies within <see cref="groundProbeDistance"/> below the rigidbody CoM.</summary>
    private bool HasForeignGroundWithinProbe()
    {
        var origin = _rb.worldCenterOfMass;
        var n = Physics.RaycastNonAlloc(
            origin,
            Vector3.down,
            _groundHits,
            groundProbeDistance,
            groundCheckMask,
            QueryTriggerInteraction.Ignore);

        for (var i = 0; i < n; i++)
        {
            var c = _groundHits[i].collider;
            if (c == null)
                continue;
            if (c.attachedRigidbody == _rb)
                continue;
            return true;
        }

        return false;
    }
}
