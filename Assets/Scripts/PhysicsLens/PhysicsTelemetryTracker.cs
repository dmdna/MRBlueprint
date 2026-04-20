using UnityEngine;

public sealed class PhysicsTelemetryTracker
{
    private PhysicsLensConfig _config;
    private Rigidbody _rb;
    private PlaceableAsset _asset;
    private CollisionEventCache _collisionCache;
    private PhysicsLensForceEventCache _forceEventCache;
    private readonly ConstraintTelemetryResolver _constraintResolver = new ConstraintTelemetryResolver();
    private readonly ForceLedger _forceLedger = new ForceLedger();
    private PhysicsLensSample[] _samples;
    private Renderer[] _renderers;
    private Collider[] _colliders;
    private int _head;
    private int _count;
    private Vector3 _previousVelocity;
    private bool _hasPreviousVelocity;
    private float _lastRecordedImpactTime = -1f;

    public Rigidbody TargetRigidbody => _rb;
    public PlaceableAsset TargetAsset => _asset;
    public CollisionEventCache CollisionCache => _collisionCache;
    public ForceLedger ForceLedger => _forceLedger;
    public PhysicsLensSample CurrentSample { get; private set; }
    public PhysicsLensConstraintSummary Constraint { get; private set; }
    public Bounds ApproxBounds { get; private set; }
    public bool HasSamples => _count > 0;
    public int Count => _count;

    public void Configure(PlaceableAsset asset, Rigidbody rb, PhysicsLensConfig config)
    {
        _asset = asset;
        _rb = rb;
        _config = config;
        _collisionCache = CollisionEventCache.GetOrAdd(rb);
        _collisionCache?.Clear();
        _forceEventCache = rb != null ? rb.GetComponent<PhysicsLensForceEventCache>() : null;
        _constraintResolver.Configure(rb, config);
        EnsureSampleBuffer();
        _head = 0;
        _count = 0;
        _hasPreviousVelocity = false;
        _previousVelocity = Vector3.zero;
        _lastRecordedImpactTime = -1f;
        CurrentSample = default;
        Constraint = PhysicsLensConstraintSummary.None;
        _forceLedger.Clear();
        _renderers = asset != null ? asset.GetRenderers() : null;
        _colliders = rb != null ? rb.GetComponentsInChildren<Collider>() : null;
        RefreshApproxBounds();
    }

    public void Clear()
    {
        _asset = null;
        _rb = null;
        _collisionCache = null;
        _forceEventCache = null;
        _constraintResolver.Configure(null, _config);
        _head = 0;
        _count = 0;
        _hasPreviousVelocity = false;
        CurrentSample = default;
        Constraint = PhysicsLensConstraintSummary.None;
        _forceLedger.Clear();
        _renderers = null;
        _colliders = null;
    }

    public bool Sample(float fixedDeltaTime)
    {
        if (_rb == null || _config == null)
            return false;

        EnsureSampleBuffer();
        RefreshApproxBounds();

        var now = Time.time;
        var velocity = _rb.linearVelocity;
        var acceleration = Vector3.zero;
        if (_hasPreviousVelocity)
            acceleration = (velocity - _previousVelocity) / Mathf.Max(0.0001f, fixedDeltaTime);

        _previousVelocity = velocity;
        _hasPreviousVelocity = true;

        Constraint = _constraintResolver.Resolve(now);

        var mass = Mathf.Max(0.0001f, _rb.mass);
        var speed = velocity.magnitude;
        var angularSpeedDeg = _rb.angularVelocity.magnitude * Mathf.Rad2Deg;
        var approxNetForce = mass * acceleration.magnitude;
        var latestImpact = _collisionCache != null ? _collisionCache.Latest : default;
        if (_forceEventCache == null)
            _forceEventCache = _rb.GetComponent<PhysicsLensForceEventCache>();
        var latestUserForce = _forceEventCache != null ? _forceEventCache.LatestUserForce : default;
        _forceLedger.Update(_rb, approxNetForce, Constraint, latestImpact, latestUserForce, _config, fixedDeltaTime);

        var hasNewImpact = latestImpact.IsValid && latestImpact.Time > _lastRecordedImpactTime;
        if (hasNewImpact)
            _lastRecordedImpactTime = latestImpact.Time;

        CurrentSample = new PhysicsLensSample
        {
            Time = now,
            CenterOfMass = _rb.worldCenterOfMass,
            Velocity = velocity,
            AngularVelocity = _rb.angularVelocity,
            Acceleration = acceleration,
            Speed = speed,
            AngularSpeedDeg = angularSpeedDeg,
            ApproxNetForce = approxNetForce,
            Momentum = mass * speed,
            LinearKineticEnergy = 0.5f * mass * speed * speed,
            PotentialEnergy = mass * Physics.gravity.magnitude * (_rb.worldCenterOfMass.y - _config.PotentialZeroPlaneY),
            Mass = mass,
            GravityEnabled = _rb.useGravity,
            IsKinematic = _rb.isKinematic,
            IsSleeping = _rb.IsSleeping(),
            DominantDriver = _forceLedger.DominantDriver,
            HasImpactEvent = hasNewImpact,
            ImpactImpulse = hasNewImpact ? latestImpact.ImpulseMagnitude : 0f,
            SpringExtension = Constraint.Kind == PhysicsLensConstraintKind.Spring ? Constraint.Extension : 0f,
            SpringRelativeSpeed = Constraint.Kind == PhysicsLensConstraintKind.Spring ? Constraint.RelativeSpeed : 0f,
            SpringLoad = Constraint.Kind == PhysicsLensConstraintKind.Spring ? Constraint.SignedLoad : 0f,
            HingeAngle = Constraint.Kind == PhysicsLensConstraintKind.Hinge ? Constraint.HingeAngle : 0f,
            HingeAngularVelocity = Constraint.Kind == PhysicsLensConstraintKind.Hinge ? Constraint.SignedAngularVelocityDeg : 0f,
            HingeTorque = Constraint.Kind == PhysicsLensConstraintKind.Hinge ? Constraint.TorqueMagnitude : 0f,
            HingeLimitProximity = Constraint.Kind == PhysicsLensConstraintKind.Hinge ? Constraint.NormalizedLimitProximity : 0f
        };

        _samples[_head] = CurrentSample;
        _head = (_head + 1) % _samples.Length;
        if (_count < _samples.Length)
            _count++;

        return true;
    }

    public int CopySamples(PhysicsLensSample[] destination)
    {
        if (destination == null || destination.Length == 0 || _samples == null || _count == 0)
            return 0;

        var copied = 0;
        var oldest = _head - _count;
        if (oldest < 0)
            oldest += _samples.Length;

        for (var i = 0; i < _count && copied < destination.Length; i++)
        {
            var index = (oldest + i) % _samples.Length;
            destination[copied++] = _samples[index];
        }

        return copied;
    }

    private void EnsureSampleBuffer()
    {
        var desired = _config != null ? _config.MaxTelemetrySamples : 512;
        if (_samples != null && _samples.Length == desired)
            return;

        _samples = new PhysicsLensSample[desired];
        _head = 0;
        _count = 0;
    }

    private void RefreshApproxBounds()
    {
        var hasBounds = false;
        var bounds = new Bounds(_rb != null ? _rb.worldCenterOfMass : Vector3.zero, Vector3.one * 0.12f);

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

        if (!hasBounds && _rb != null)
            bounds = new Bounds(_rb.worldCenterOfMass, Vector3.one * 0.18f);

        ApproxBounds = bounds;
    }
}
