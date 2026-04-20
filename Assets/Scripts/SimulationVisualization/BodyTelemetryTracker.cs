using UnityEngine;

public sealed class BodyTelemetryTracker
{
    private readonly ConstraintTelemetryResolver _constraintResolver = new ConstraintTelemetryResolver();
    private readonly VisualizationForceLedger _forceLedger = new VisualizationForceLedger();
    private Rigidbody _rb;
    private PlaceableAsset _asset;
    private CollisionEventCache _collisionCache;
    private PhysicsLensForceEventCache _forceEventCache;
    private Renderer[] _renderers;
    private Collider[] _colliders;
    private Vector3[] _trail;
    private int _trailHead;
    private int _trailCount;
    private Vector3 _previousVelocity;
    private bool _hasPreviousVelocity;
    private float _lastImpactTime = -1f;

    public bool SeenThisScan { get; set; }
    public Rigidbody Rigidbody => _rb;
    public PlaceableAsset Asset => _asset;
    public VisualizationForceLedger ForceLedger => _forceLedger;
    public PhysicsLensSample CurrentSample { get; private set; }
    public PhysicsLensConstraintSummary Constraint { get; private set; }
    public PhysicsLensCollisionEvent LatestImpact { get; private set; }
    public Bounds ApproxBounds { get; private set; }
    public Vector3 ApproxNetForceVector { get; private set; }
    public float Importance { get; private set; }
    public bool HasNewImpact { get; private set; }

    public bool IsValid => _rb != null && _asset != null && _rb.gameObject.activeInHierarchy;

    public void Configure(PlaceableAsset asset, Rigidbody rb, VisualizationConfig config, PhysicsLensConfig lensConfig)
    {
        _asset = asset;
        _rb = rb;
        _collisionCache = CollisionEventCache.GetOrAdd(rb);
        _forceEventCache = rb != null ? rb.GetComponent<PhysicsLensForceEventCache>() : null;
        _constraintResolver.Configure(rb, lensConfig);
        _renderers = asset != null ? asset.GetRenderers() : null;
        _colliders = rb != null ? rb.GetComponentsInChildren<Collider>() : null;
        EnsureTrail(config);
        _trailHead = 0;
        _trailCount = 0;
        _previousVelocity = Vector3.zero;
        _hasPreviousVelocity = false;
        _lastImpactTime = -1f;
        Importance = 0f;
        HasNewImpact = false;
        LatestImpact = default;
        CurrentSample = default;
        Constraint = PhysicsLensConstraintSummary.None;
        ApproxNetForceVector = Vector3.zero;
        _forceLedger.Clear();
        RefreshApproxBounds();
    }

    public void Clear(PhysicsLensConfig lensConfig)
    {
        _rb = null;
        _asset = null;
        _collisionCache = null;
        _forceEventCache = null;
        _renderers = null;
        _colliders = null;
        _trailHead = 0;
        _trailCount = 0;
        _previousVelocity = Vector3.zero;
        _hasPreviousVelocity = false;
        _lastImpactTime = -1f;
        Importance = 0f;
        HasNewImpact = false;
        LatestImpact = default;
        CurrentSample = default;
        Constraint = PhysicsLensConstraintSummary.None;
        ApproxNetForceVector = Vector3.zero;
        _forceLedger.Clear();
        _constraintResolver.Configure(null, lensConfig);
    }

    public bool Sample(float fixedDeltaTime, VisualizationConfig config)
    {
        HasNewImpact = false;
        if (_rb == null || config == null)
            return false;

        EnsureTrail(config);
        RefreshApproxBounds();

        var now = Time.time;
        var velocity = _rb.linearVelocity;
        var acceleration = Vector3.zero;
        if (_hasPreviousVelocity)
            acceleration = (velocity - _previousVelocity) / Mathf.Max(0.0001f, fixedDeltaTime);

        _previousVelocity = velocity;
        _hasPreviousVelocity = true;
        ApproxNetForceVector = acceleration * Mathf.Max(0.0001f, _rb.mass);

        Constraint = _constraintResolver.Resolve(now);
        LatestImpact = _collisionCache != null ? _collisionCache.Latest : default;
        if (_forceEventCache == null)
            _forceEventCache = _rb.GetComponent<PhysicsLensForceEventCache>();
        var latestUserForce = _forceEventCache != null ? _forceEventCache.LatestUserForce : default;
        _forceLedger.Update(_rb, _asset, ApproxNetForceVector, Constraint, LatestImpact, latestUserForce, config, fixedDeltaTime);

        HasNewImpact = LatestImpact.IsValid && LatestImpact.Time > _lastImpactTime;
        if (HasNewImpact)
            _lastImpactTime = LatestImpact.Time;

        var mass = Mathf.Max(0.0001f, _rb.mass);
        var speed = velocity.magnitude;
        CurrentSample = new PhysicsLensSample
        {
            Time = now,
            CenterOfMass = _rb.worldCenterOfMass,
            Velocity = velocity,
            AngularVelocity = _rb.angularVelocity,
            Acceleration = acceleration,
            Speed = speed,
            AngularSpeedDeg = _rb.angularVelocity.magnitude * Mathf.Rad2Deg,
            ApproxNetForce = ApproxNetForceVector.magnitude,
            Momentum = mass * speed,
            LinearKineticEnergy = 0.5f * mass * speed * speed,
            PotentialEnergy = 0f,
            Mass = mass,
            GravityEnabled = _rb.useGravity,
            IsKinematic = _rb.isKinematic,
            IsSleeping = _rb.IsSleeping(),
            DominantDriver = _forceLedger.DominantDriver,
            HasImpactEvent = HasNewImpact,
            ImpactImpulse = HasNewImpact ? LatestImpact.ImpulseMagnitude : 0f,
            SpringExtension = Constraint.Kind == PhysicsLensConstraintKind.Spring ? Constraint.Extension : 0f,
            SpringRelativeSpeed = Constraint.Kind == PhysicsLensConstraintKind.Spring ? Constraint.RelativeSpeed : 0f,
            SpringLoad = Constraint.Kind == PhysicsLensConstraintKind.Spring ? Constraint.SignedLoad : 0f,
            HingeAngle = Constraint.Kind == PhysicsLensConstraintKind.Hinge ? Constraint.HingeAngle : 0f,
            HingeAngularVelocity = Constraint.Kind == PhysicsLensConstraintKind.Hinge ? Constraint.SignedAngularVelocityDeg : 0f,
            HingeTorque = Constraint.Kind == PhysicsLensConstraintKind.Hinge ? Constraint.TorqueMagnitude : 0f,
            HingeLimitProximity = Constraint.Kind == PhysicsLensConstraintKind.Hinge ? Constraint.NormalizedLimitProximity : 0f
        };

        _trail[_trailHead] = CurrentSample.CenterOfMass;
        _trailHead = (_trailHead + 1) % _trail.Length;
        if (_trailCount < _trail.Length)
            _trailCount++;

        return true;
    }

    public void RefreshImportance(Camera camera, bool selected, VisualizationConfig config)
    {
        if (_rb == null || config == null)
        {
            Importance = 0f;
            return;
        }

        var sample = CurrentSample;
        var score = selected ? config.SelectedImportanceBoost : 0f;

        score += Mathf.Clamp01(sample.Speed / 1.5f) * 1.2f;
        score += Mathf.Clamp01(sample.AngularSpeedDeg / 240f) * 0.7f;
        score += Mathf.Clamp01(sample.ApproxNetForce / Mathf.Max(0.001f, config.LowForceThreshold * 16f)) * 1.5f;

        if (Constraint.IsValid)
        {
            if (Constraint.Kind == PhysicsLensConstraintKind.Spring)
                score += Mathf.Clamp01(Constraint.LoadMagnitude / config.SpringLoadFocusThreshold) * 1.7f;
            else if (Constraint.Kind == PhysicsLensConstraintKind.Hinge)
                score += Mathf.Clamp01(Mathf.Max(Constraint.TorqueMagnitude, Constraint.NormalizedLimitProximity)
                                       / config.HingeLoadFocusThreshold) * 1.7f;

            if (Constraint.BreakRatio > 0f)
                score += Constraint.BreakRatio;
        }

        if (LatestImpact.IsValid)
        {
            var age = Time.time - LatestImpact.Time;
            if (age <= config.RecentImpactSeconds)
                score += (1f - Mathf.Clamp01(age / config.RecentImpactSeconds)) * 2.1f;
        }

        if (camera != null)
        {
            var distance = Vector3.Distance(camera.transform.position, sample.CenterOfMass);
            var relevance = 1f - Mathf.InverseLerp(
                config.CameraDistanceForFullRelevance,
                config.CameraDistanceForLowRelevance,
                distance);
            score *= Mathf.Lerp(0.45f, 1.15f, Mathf.Clamp01(relevance));
        }

        if (sample.IsSleeping && !selected)
            score *= config.SleepingImportanceMultiplier;

        Importance = score;
    }

    public int CopyTrail(Vector3[] destination)
    {
        if (destination == null || destination.Length == 0 || _trail == null || _trailCount == 0)
            return 0;

        var copied = 0;
        var oldest = _trailHead - _trailCount;
        if (oldest < 0)
            oldest += _trail.Length;

        for (var i = 0; i < _trailCount && copied < destination.Length; i++)
        {
            var index = (oldest + i) % _trail.Length;
            destination[copied++] = _trail[index];
        }

        return copied;
    }

    private void EnsureTrail(VisualizationConfig config)
    {
        var desired = config != null ? config.MaxTrailSamples : 192;
        if (_trail != null && _trail.Length == desired)
            return;

        _trail = new Vector3[desired];
        _trailHead = 0;
        _trailCount = 0;
    }

    private void RefreshApproxBounds()
    {
        var center = _rb != null ? _rb.worldCenterOfMass : Vector3.zero;
        var bounds = new Bounds(center, Vector3.one * 0.18f);
        var hasBounds = false;

        if (_renderers != null)
        {
            for (var i = 0; i < _renderers.Length; i++)
            {
                var renderer = _renderers[i];
                if (renderer == null)
                    continue;

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }
        }

        if (!hasBounds && _colliders != null)
        {
            for (var i = 0; i < _colliders.Length; i++)
            {
                var collider = _colliders[i];
                if (collider == null)
                    continue;

                if (!hasBounds)
                {
                    bounds = collider.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(collider.bounds);
                }
            }
        }

        ApproxBounds = bounds;
    }
}
