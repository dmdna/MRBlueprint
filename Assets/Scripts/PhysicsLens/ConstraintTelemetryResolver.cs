using UnityEngine;

public sealed class ConstraintTelemetryResolver
{
    private Rigidbody _target;
    private PhysicsLensConfig _config;
    private SpringJoint _springJoint;
    private HingeJoint _hingeJoint;
    private SandboxDrawingPhysicsRuntime _runtimeSpring;
    private SandboxDrawingPhysicsRuntime _runtimeHinge;
    private PhysicsDrawingSelectable _hingeDrawing;
    private Vector3 _drawingHingeAxis = Vector3.up;
    private Vector3 _drawingReferenceVector = Vector3.forward;
    private float _hingeUnwrappedAngle;
    private float _hingePreviousRawAngle;
    private bool _hasHingeAngle;
    private float _nextRescanTime;
    private int _connectedConstraintCount;

    public void Configure(Rigidbody target, PhysicsLensConfig config)
    {
        _target = target;
        _config = config;
        _springJoint = null;
        _hingeJoint = null;
        _runtimeSpring = null;
        _runtimeHinge = null;
        _hingeDrawing = null;
        _drawingHingeAxis = Vector3.up;
        _drawingReferenceVector = Vector3.forward;
        ResetHingeAngleTracking();
        _nextRescanTime = 0f;
        _connectedConstraintCount = 0;
    }

    public PhysicsLensConstraintSummary Resolve(float now)
    {
        if (_target == null)
            return PhysicsLensConstraintSummary.None;

        if (now >= _nextRescanTime)
        {
            RefreshCandidates();
            var interval = _config != null ? _config.ConstraintRescanSeconds : 0.45f;
            _nextRescanTime = now + interval;
        }

        var hasSpring = TryBuildSpringSummary(_springJoint, out var spring);
        if (!hasSpring)
            hasSpring = TryBuildRuntimeSpringSummary(_runtimeSpring, out spring);
        var hasHinge = TryBuildHingeSummary(_hingeJoint, out var hinge);
        if (!hasHinge)
            hasHinge = TryBuildRuntimeHingeSummary(_runtimeHinge, out hinge);
        if (!hasHinge)
            hasHinge = TryBuildDrawingHingeSummary(out hinge);

        var summary = PhysicsLensConstraintSummary.None;
        var springLoad = hasSpring ? spring.LoadMagnitude : 0f;
        var hingeLoad = hasHinge ? Mathf.Max(hinge.TorqueMagnitude, hinge.NormalizedLimitProximity) : 0f;
        var springThreshold = _config != null ? _config.SpringDominanceThreshold : 1f;
        var hingeThreshold = _config != null ? _config.HingeDominanceThreshold : 0.15f;

        if (hasSpring && springLoad >= springThreshold && springLoad >= hingeLoad)
            summary = spring;
        else if (hasHinge && hingeLoad >= hingeThreshold)
            summary = hinge;

        summary.ConnectedConstraintCount = _connectedConstraintCount;
        PopulateTopLoads(ref summary, hasSpring, spring, hasHinge, hinge);
        return summary;
    }

    private void RefreshCandidates()
    {
        var previousRuntimeHinge = _runtimeHinge;
        var previousDrawing = _hingeDrawing;
        var previousAxis = _drawingHingeAxis;
        var previousReference = _drawingReferenceVector;
        var previousUnwrappedAngle = _hingeUnwrappedAngle;
        var previousRawAngle = _hingePreviousRawAngle;
        var previousHasAngle = _hasHingeAngle;

        _springJoint = null;
        _hingeJoint = null;
        _runtimeSpring = null;
        _runtimeHinge = null;
        _hingeDrawing = null;
        _connectedConstraintCount = 0;

        var bestSpringLoad = 0f;
        var bestHingeLoad = 0f;
        var joints = Object.FindObjectsByType<Joint>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (var i = 0; i < joints.Length; i++)
        {
            var joint = joints[i];
            if (!IsJointLinkedToTarget(joint))
                continue;

            _connectedConstraintCount++;

            if (joint is SpringJoint spring)
            {
                var load = EstimateSpringLoadForRanking(spring);
                if (_springJoint == null || load > bestSpringLoad)
                {
                    _springJoint = spring;
                    bestSpringLoad = load;
                }
            }
            else if (joint is HingeJoint hinge)
            {
                var load = EstimateHingeLoadForRanking(hinge);
                if (_hingeJoint == null || load > bestHingeLoad)
                {
                    _hingeJoint = hinge;
                    bestHingeLoad = load;
                }
            }
        }

        var runtimes = Object.FindObjectsByType<SandboxDrawingPhysicsRuntime>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        for (var i = 0; i < runtimes.Length; i++)
        {
            var runtime = runtimes[i];
            if (runtime == null)
                continue;

            if (runtime.TryGetPhysicsLensSpringTelemetry(
                    _target,
                    out _,
                    out _,
                    out _,
                    out _,
                    out _,
                    out _,
                    out _,
                    out _))
            {
                _connectedConstraintCount++;
                var runtimeSpringLoad = EstimateRuntimeSpringLoadForRanking(runtime);
                if (_runtimeSpring == null || runtimeSpringLoad > bestSpringLoad)
                {
                    _runtimeSpring = runtime;
                    bestSpringLoad = runtimeSpringLoad;
                }

                continue;
            }

            if (!runtime.TryGetPhysicsLensHingeTelemetry(
                    _target,
                    out _,
                    out _,
                    out _,
                    out _,
                    out _,
                    out _))
            {
                continue;
            }

            _connectedConstraintCount++;
            var runtimeHingeLoad = EstimateRuntimeHingeLoadForRanking(runtime);
            if (_runtimeHinge == null || runtimeHingeLoad > bestHingeLoad)
            {
                _runtimeHinge = runtime;
                bestHingeLoad = runtimeHingeLoad;
            }
        }

        var runtimeHingeDrawing = _runtimeHinge != null ? _runtimeHinge.SourceDrawing : null;
        var drawings = Object.FindObjectsByType<PhysicsDrawingSelectable>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        var bestDrawingLoad = 0f;
        for (var i = 0; i < drawings.Length; i++)
        {
            var drawing = drawings[i];
            if (drawing == null
                || drawing.PhysicsIntent != PhysicsIntentType.Hinge
                || drawing.AttachedPlaceable == null
                || drawing.AttachedPlaceable.Rigidbody != _target)
            {
                continue;
            }

            if (drawing == runtimeHingeDrawing)
            {
                _hingeDrawing = drawing;
                continue;
            }

            _connectedConstraintCount++;
            if (_runtimeHinge != null)
                continue;

            var load = Mathf.Lerp(0.05f, 1.25f, drawing.HingeTorque)
                       + _target.angularVelocity.magnitude * Mathf.Max(0.1f, _target.mass) * 0.08f;
            if (_hingeDrawing == null || load > bestDrawingLoad)
            {
                _hingeDrawing = drawing;
                bestDrawingLoad = load;
            }
        }

        if (_runtimeHinge == null && _hingeDrawing == null)
            return;

        if (_runtimeHinge == previousRuntimeHinge && _hingeDrawing == previousDrawing)
        {
            _drawingHingeAxis = previousAxis.sqrMagnitude > 0.0001f ? previousAxis.normalized : Vector3.up;
            _drawingReferenceVector = previousReference.sqrMagnitude > 0.0001f ? previousReference.normalized : Vector3.forward;
            _hingeUnwrappedAngle = previousUnwrappedAngle;
            _hingePreviousRawAngle = previousRawAngle;
            _hasHingeAngle = previousHasAngle;
            return;
        }

        ConfigureDrawingHingeFrame(_runtimeHinge, _hingeDrawing);
    }

    private bool IsJointLinkedToTarget(Joint joint)
    {
        if (joint == null || _target == null)
            return false;

        var body = joint.GetComponent<Rigidbody>();
        return body == _target || joint.connectedBody == _target;
    }

    private bool TryBuildSpringSummary(SpringJoint joint, out PhysicsLensConstraintSummary summary)
    {
        summary = default;
        if (joint == null || _target == null || !IsJointLinkedToTarget(joint))
            return false;

        var bodyA = joint.GetComponent<Rigidbody>();
        var bodyB = joint.connectedBody;
        var anchorA = ResolveJointAnchor(bodyA, joint.transform, joint.anchor);
        var anchorB = bodyB != null
            ? bodyB.transform.TransformPoint(joint.connectedAnchor)
            : joint.connectedAnchor;
        var delta = anchorB - anchorA;
        var length = delta.magnitude;
        var axis = length > 0.0001f ? delta / length : Vector3.up;
        var rest = ResolveSpringRestLength(joint, length);
        var extension = length - rest;
        var va = bodyA != null ? bodyA.GetPointVelocity(anchorA) : Vector3.zero;
        var vb = bodyB != null ? bodyB.GetPointVelocity(anchorB) : Vector3.zero;
        var relativeSpeed = Vector3.Dot(va - vb, axis);
        var estimatedSignedLoad = joint.spring * extension + joint.damper * relativeSpeed;
        var currentForce = joint.currentForce.magnitude;
        var signedLoad = currentForce > 0.001f
            ? Mathf.Sign(estimatedSignedLoad == 0f ? extension : estimatedSignedLoad) * currentForce
            : estimatedSignedLoad;
        var loadMagnitude = Mathf.Abs(signedLoad);

        summary = new PhysicsLensConstraintSummary
        {
            Kind = PhysicsLensConstraintKind.Spring,
            IsValid = true,
            DisplayName = joint.name,
            WorldAnchorA = anchorA,
            WorldAnchorB = anchorB,
            AxisWorld = axis,
            RestLength = rest,
            CurrentLength = length,
            Extension = extension,
            RelativeSpeed = relativeSpeed,
            SignedLoad = signedLoad,
            LoadMagnitude = loadMagnitude,
            SpringState = extension < -0.01f
                ? PhysicsLensSpringState.Compressing
                : extension > 0.01f
                    ? PhysicsLensSpringState.Stretching
                    : PhysicsLensSpringState.NearRest,
            BreakRatio = ResolveBreakRatio(loadMagnitude, joint.breakForce),
            DistanceToLimit = float.PositiveInfinity
        };
        return true;
    }

    private bool TryBuildRuntimeSpringSummary(
        SandboxDrawingPhysicsRuntime runtime,
        out PhysicsLensConstraintSummary summary)
    {
        summary = default;
        if (runtime == null
            || _target == null
            || !runtime.TryGetPhysicsLensSpringTelemetry(
                _target,
                out var bodyA,
                out var bodyB,
                out var anchorA,
                out var anchorB,
                out var rest,
                out var strength,
                out var damper,
                out var displayName))
        {
            return false;
        }

        var delta = anchorB - anchorA;
        var length = delta.magnitude;
        var axis = length > 0.0001f ? delta / length : Vector3.up;
        rest = Mathf.Max(0.001f, rest);
        var extension = length - rest;
        var va = bodyA != null ? bodyA.GetPointVelocity(anchorA) : Vector3.zero;
        var vb = bodyB != null ? bodyB.GetPointVelocity(anchorB) : Vector3.zero;
        var relativeSpeed = Vector3.Dot(vb - va, axis);
        var rawLoad = Mathf.Max(0f, strength * length + damper * relativeSpeed);
        var signedLoad = Mathf.Sign(Mathf.Abs(extension) > 0.001f ? extension : rawLoad) * rawLoad;
        var loadMagnitude = Mathf.Abs(signedLoad);

        summary = new PhysicsLensConstraintSummary
        {
            Kind = PhysicsLensConstraintKind.Spring,
            IsValid = true,
            DisplayName = displayName,
            WorldAnchorA = anchorA,
            WorldAnchorB = anchorB,
            AxisWorld = axis,
            RestLength = rest,
            CurrentLength = length,
            Extension = extension,
            RelativeSpeed = relativeSpeed,
            SignedLoad = signedLoad,
            LoadMagnitude = loadMagnitude,
            SpringState = extension < -0.01f
                ? PhysicsLensSpringState.Compressing
                : extension > 0.01f
                    ? PhysicsLensSpringState.Stretching
                    : PhysicsLensSpringState.NearRest,
            BreakRatio = -1f,
            DistanceToLimit = float.PositiveInfinity
        };
        return true;
    }

    private bool TryBuildHingeSummary(HingeJoint joint, out PhysicsLensConstraintSummary summary)
    {
        summary = default;
        if (joint == null || _target == null || !IsJointLinkedToTarget(joint))
            return false;

        var bodyA = joint.GetComponent<Rigidbody>();
        var bodyB = joint.connectedBody;
        var axis = joint.transform.TransformDirection(joint.axis);
        if (axis.sqrMagnitude <= 0.0001f)
            axis = Vector3.up;
        axis.Normalize();

        var relAngularVelocity = bodyA != null ? bodyA.angularVelocity : _target.angularVelocity;
        if (bodyB != null)
            relAngularVelocity -= bodyB.angularVelocity;
        var signedAngularVelocity = Vector3.Dot(relAngularVelocity, axis) * Mathf.Rad2Deg;
        var torque = joint.currentTorque.magnitude;
        var angle = joint.angle;
        var hasLimits = joint.useLimits;
        var minLimit = 0f;
        var maxLimit = 0f;
        var distanceToLimit = float.PositiveInfinity;
        var proximity = 0f;

        if (hasLimits)
        {
            var limits = joint.limits;
            minLimit = limits.min;
            maxLimit = limits.max;
            distanceToLimit = Mathf.Min(Mathf.Abs(angle - minLimit), Mathf.Abs(maxLimit - angle));
            var warningDegrees = _config != null ? _config.HingeLimitWarnDegrees : 18f;
            proximity = 1f - Mathf.Clamp01(distanceToLimit / Mathf.Max(0.001f, warningDegrees));
        }

        if (torque <= 0.001f)
            torque = Mathf.Abs(signedAngularVelocity) * Mathf.Max(0.1f, _target.mass) * 0.01f + proximity;

        summary = new PhysicsLensConstraintSummary
        {
            Kind = PhysicsLensConstraintKind.Hinge,
            IsValid = true,
            DisplayName = joint.name,
            AxisWorld = axis,
            HingeAngle = angle,
            HingeMinLimit = minLimit,
            HingeMaxLimit = maxLimit,
            HasHingeLimits = hasLimits,
            SignedAngularVelocityDeg = signedAngularVelocity,
            TorqueMagnitude = torque,
            LoadMagnitude = torque,
            DistanceToLimit = distanceToLimit,
            NormalizedLimitProximity = proximity,
            BreakRatio = ResolveBreakRatio(torque, joint.breakTorque)
        };
        return true;
    }

    private bool TryBuildRuntimeHingeSummary(
        SandboxDrawingPhysicsRuntime runtime,
        out PhysicsLensConstraintSummary summary)
    {
        summary = default;
        if (runtime == null
            || _target == null
            || !runtime.TryGetPhysicsLensHingeTelemetry(
                _target,
                out var pivot,
                out var bodyPoint,
                out var restLength,
                out var stiffness,
                out var damper,
                out var displayName))
        {
            return false;
        }

        var axis = _drawingHingeAxis.sqrMagnitude > 0.0001f ? _drawingHingeAxis.normalized : Vector3.up;
        if (!TryResolveHingeRadial(pivot, bodyPoint, axis, out var radial, out var radialDirection, out var radius))
            return false;

        var rawAngle = Vector3.SignedAngle(_drawingReferenceVector, radialDirection, axis);
        var angle = ResolveContinuousHingeAngle(rawAngle);
        var signedAngularVelocity = ResolveOrbitalAngularVelocity(_target, bodyPoint, axis, radialDirection, radius);
        var pointVelocity = _target.GetPointVelocity(bodyPoint);
        var radialDirection3D = radial.sqrMagnitude > 0.0001f ? radial.normalized : radialDirection;
        var outwardSpeed = Mathf.Max(0f, Vector3.Dot(pointVelocity, radialDirection3D));
        restLength = Mathf.Max(0.001f, restLength);
        var currentLength = Mathf.Max(radius, Vector3.Distance(pivot, bodyPoint));
        var extension = Mathf.Max(0f, currentLength - restLength);
        var load = Mathf.Max(0f, stiffness * extension + damper * outwardSpeed);
        var torque = load * Mathf.Max(0.02f, radius);
        var authoredTorque = ResolveAuthoredHingeTorque(runtime.SourceDrawing);
        if (torque <= 0.001f)
            torque = authoredTorque * (0.25f + Mathf.Abs(signedAngularVelocity) * 0.02f);

        summary = new PhysicsLensConstraintSummary
        {
            Kind = PhysicsLensConstraintKind.Hinge,
            IsValid = true,
            DisplayName = string.IsNullOrEmpty(displayName) ? "Hinge" : displayName,
            WorldAnchorA = pivot,
            WorldAnchorB = pivot + radialDirection * Mathf.Max(0.08f, radius),
            AxisWorld = axis,
            RestLength = restLength,
            CurrentLength = currentLength,
            Extension = extension,
            RelativeSpeed = outwardSpeed,
            SignedLoad = load,
            HingeAngle = angle,
            SignedAngularVelocityDeg = signedAngularVelocity,
            TorqueMagnitude = Mathf.Max(torque, authoredTorque * 0.25f),
            LoadMagnitude = Mathf.Max(torque, load),
            HasHingeLimits = false,
            DistanceToLimit = float.PositiveInfinity,
            NormalizedLimitProximity = 0f,
            BreakRatio = -1f
        };
        return true;
    }

    private bool TryBuildDrawingHingeSummary(out PhysicsLensConstraintSummary summary)
    {
        summary = default;
        if (_hingeDrawing == null || _target == null || _hingeDrawing.AttachedPlaceable == null)
            return false;

        if (_hingeDrawing.AttachedPlaceable.Rigidbody != _target)
            return false;

        var axis = _drawingHingeAxis.sqrMagnitude > 0.0001f ? _drawingHingeAxis.normalized : Vector3.up;
        if (!_hingeDrawing.TryGetHingeAttachment(out _, out var pivot, out var bodyPoint, out _)
            || !TryResolveHingeRadial(pivot, bodyPoint, axis, out _, out var radialDirection, out var radius))
        {
            return false;
        }

        var rawAngle = Vector3.SignedAngle(_drawingReferenceVector, radialDirection, axis);
        var angle = ResolveContinuousHingeAngle(rawAngle);
        var angularVelocity = ResolveOrbitalAngularVelocity(_target, bodyPoint, axis, radialDirection, radius);
        var authoredTorque = SandboxStrokePlaceablePhysicsApplier.ResolveHingeTorqueEstimate(_hingeDrawing.HingeTorque);
        var torque = authoredTorque * (0.25f + Mathf.Abs(angularVelocity) * 0.02f);

        summary = new PhysicsLensConstraintSummary
        {
            Kind = PhysicsLensConstraintKind.Hinge,
            IsValid = true,
            DisplayName = _hingeDrawing.DisplayName,
            WorldAnchorA = pivot,
            WorldAnchorB = pivot + radialDirection * Mathf.Max(0.08f, radius),
            AxisWorld = axis,
            HingeAngle = angle,
            SignedAngularVelocityDeg = angularVelocity,
            TorqueMagnitude = torque,
            LoadMagnitude = torque,
            HasHingeLimits = false,
            DistanceToLimit = float.PositiveInfinity,
            NormalizedLimitProximity = 0f,
            BreakRatio = -1f
        };
        return true;
    }

    private void ConfigureDrawingHingeFrame(SandboxDrawingPhysicsRuntime runtime, PhysicsDrawingSelectable drawing)
    {
        _drawingHingeAxis = Vector3.up;
        _drawingReferenceVector = Vector3.forward;
        ResetHingeAngleTracking();

        if (_target == null)
            return;

        if (drawing != null && TryResolveDrawingHingeAxis(drawing, out var drawingAxis))
            _drawingHingeAxis = drawingAxis;

        if (runtime != null
            && runtime.TryGetPhysicsLensHingeTelemetry(
                _target,
                out var runtimePivot,
                out var runtimeBodyPoint,
                out _,
                out _,
                out _,
                out _)
            && TryResolveHingeRadial(runtimePivot, runtimeBodyPoint, _drawingHingeAxis, out _, out var runtimeReference, out _))
        {
            _drawingReferenceVector = runtimeReference;
            return;
        }

        if (drawing != null
            && drawing.TryGetHingeAttachment(out _, out var pivot, out var bodyPoint, out _)
            && TryResolveHingeRadial(pivot, bodyPoint, _drawingHingeAxis, out _, out var drawingReference, out _))
        {
            _drawingReferenceVector = drawingReference;
            return;
        }

        var reference = Vector3.ProjectOnPlane(_target.transform.forward, _drawingHingeAxis);
        if (reference.sqrMagnitude <= 0.0001f)
            reference = Vector3.ProjectOnPlane(_target.transform.up, _drawingHingeAxis);
        if (reference.sqrMagnitude <= 0.0001f)
            reference = Vector3.Cross(_drawingHingeAxis, Vector3.up);
        if (reference.sqrMagnitude <= 0.0001f)
            reference = Vector3.right;

        _drawingReferenceVector = reference.normalized;
    }

    private static bool TryResolveDrawingHingeAxis(PhysicsDrawingSelectable drawing, out Vector3 axis)
    {
        axis = Vector3.up;
        if (drawing == null)
            return false;

        var positions = drawing.GetWorldLinePositions();
        if (positions != null && positions.Length >= 3)
        {
            var center = Vector3.zero;
            for (var i = 0; i < positions.Length; i++)
                center += positions[i];
            center /= positions.Length;

            var normal = Vector3.zero;
            var previous = positions[0] - center;
            for (var i = 1; i < positions.Length; i++)
            {
                var current = positions[i] - center;
                normal += Vector3.Cross(previous, current);
                previous = current;
            }

            if (normal.sqrMagnitude > 0.0001f)
            {
                axis = normal.normalized;
                return true;
            }
        }

        return false;
    }

    private static bool TryResolveHingeRadial(
        Vector3 pivot,
        Vector3 bodyPoint,
        Vector3 axis,
        out Vector3 radial,
        out Vector3 radialDirection,
        out float radius)
    {
        radial = bodyPoint - pivot;
        radialDirection = Vector3.ProjectOnPlane(radial, axis);
        radius = radialDirection.magnitude;
        if (radius <= 0.0001f)
        {
            radialDirection = Vector3.Cross(axis, Vector3.up);
            radius = radialDirection.magnitude;
        }

        if (radius <= 0.0001f)
        {
            radialDirection = Vector3.Cross(axis, Vector3.right);
            radius = radialDirection.magnitude;
        }

        if (radius <= 0.0001f)
            return false;

        radialDirection /= radius;
        return true;
    }

    private float ResolveContinuousHingeAngle(float rawAngle)
    {
        if (!_hasHingeAngle)
        {
            _hingeUnwrappedAngle = rawAngle;
            _hingePreviousRawAngle = rawAngle;
            _hasHingeAngle = true;
            return rawAngle;
        }

        _hingeUnwrappedAngle += Mathf.DeltaAngle(_hingePreviousRawAngle, rawAngle);
        _hingePreviousRawAngle = rawAngle;
        return _hingeUnwrappedAngle;
    }

    private void ResetHingeAngleTracking()
    {
        _hingeUnwrappedAngle = 0f;
        _hingePreviousRawAngle = 0f;
        _hasHingeAngle = false;
    }

    private static float ResolveOrbitalAngularVelocity(
        Rigidbody body,
        Vector3 bodyPoint,
        Vector3 axis,
        Vector3 radialDirection,
        float radius)
    {
        if (body == null)
            return 0f;

        if (radius > 0.001f && radialDirection.sqrMagnitude > 0.0001f)
        {
            var tangent = Vector3.Cross(axis.normalized, radialDirection.normalized);
            if (tangent.sqrMagnitude > 0.0001f)
                return Vector3.Dot(body.GetPointVelocity(bodyPoint), tangent.normalized) / radius * Mathf.Rad2Deg;
        }

        return Vector3.Dot(body.angularVelocity, axis.normalized) * Mathf.Rad2Deg;
    }

    private static float ResolveAuthoredHingeTorque(PhysicsDrawingSelectable drawing)
    {
        return drawing != null
            ? SandboxStrokePlaceablePhysicsApplier.ResolveHingeTorqueEstimate(drawing.HingeTorque)
            : 0.15f;
    }

    private float EstimateSpringLoadForRanking(SpringJoint joint)
    {
        if (!TryBuildSpringSummary(joint, out var summary))
            return 0f;
        return summary.LoadMagnitude;
    }

    private float EstimateHingeLoadForRanking(HingeJoint joint)
    {
        if (!TryBuildHingeSummary(joint, out var summary))
            return 0f;
        return Mathf.Max(summary.TorqueMagnitude, summary.NormalizedLimitProximity);
    }

    private float EstimateRuntimeSpringLoadForRanking(SandboxDrawingPhysicsRuntime runtime)
    {
        if (!TryBuildRuntimeSpringSummary(runtime, out var summary))
            return 0f;
        return summary.LoadMagnitude;
    }

    private float EstimateRuntimeHingeLoadForRanking(SandboxDrawingPhysicsRuntime runtime)
    {
        if (runtime == null
            || _target == null
            || !runtime.TryGetPhysicsLensHingeTelemetry(
                _target,
                out var pivot,
                out var bodyPoint,
                out var restLength,
                out var stiffness,
                out var damper,
                out _))
        {
            return 0f;
        }

        var distance = Vector3.Distance(pivot, bodyPoint);
        var radial = distance > 0.0001f ? (bodyPoint - pivot) / distance : Vector3.up;
        var pointVelocity = _target.GetPointVelocity(bodyPoint);
        var outwardSpeed = Mathf.Max(0f, Vector3.Dot(pointVelocity, radial));
        var extension = Mathf.Max(0f, distance - Mathf.Max(0.001f, restLength));
        var load = Mathf.Max(0f, stiffness * extension + damper * outwardSpeed);
        var authored = ResolveAuthoredHingeTorque(runtime.SourceDrawing);
        return Mathf.Max(load * Mathf.Max(0.02f, distance), authored, stiffness * 0.002f);
    }

    private float ResolveSpringRestLength(SpringJoint joint, float currentLength)
    {
        var inferred = InferSpringRestLength(joint, currentLength);
        var metadata = PhysicsLensSpringMetadata.GetOrCreate(joint, inferred);
        return metadata != null && metadata.HasRestLength ? metadata.RestLength : inferred;
    }

    private static float InferSpringRestLength(SpringJoint joint, float currentLength)
    {
        if (joint == null)
            return Mathf.Max(0.001f, currentLength);

        if (joint.minDistance > 0.001f && joint.maxDistance > joint.minDistance)
        {
            var fromMin = joint.minDistance / 0.85f;
            var fromMax = joint.maxDistance / 1.25f;
            return Mathf.Max(0.001f, (fromMin + fromMax) * 0.5f);
        }

        if (joint.maxDistance > 0.001f)
            return Mathf.Max(0.001f, joint.maxDistance);

        if (joint.minDistance > 0.001f)
            return Mathf.Max(0.001f, joint.minDistance);

        return Mathf.Max(0.001f, currentLength);
    }

    private static Vector3 ResolveJointAnchor(Rigidbody body, Transform fallback, Vector3 localAnchor)
    {
        if (body != null)
            return body.transform.TransformPoint(localAnchor);
        return fallback != null ? fallback.TransformPoint(localAnchor) : localAnchor;
    }

    private static float ResolveBreakRatio(float load, float breakValue)
    {
        if (breakValue <= 0f || float.IsInfinity(breakValue))
            return -1f;
        return Mathf.Clamp01(load / breakValue);
    }

    private static void PopulateTopLoads(
        ref PhysicsLensConstraintSummary summary,
        bool hasSpring,
        PhysicsLensConstraintSummary spring,
        bool hasHinge,
        PhysicsLensConstraintSummary hinge)
    {
        var nameA = string.Empty;
        var nameB = string.Empty;
        var loadA = 0f;
        var loadB = 0f;

        if (hasSpring)
            AddTopLoad(spring.DisplayName, spring.LoadMagnitude, ref nameA, ref loadA, ref nameB, ref loadB);
        if (hasHinge)
            AddTopLoad(hinge.DisplayName, Mathf.Max(hinge.TorqueMagnitude, hinge.NormalizedLimitProximity),
                ref nameA, ref loadA, ref nameB, ref loadB);

        summary.TopConstraintNameA = string.IsNullOrEmpty(nameA) ? "None" : nameA;
        summary.TopConstraintLoadA = loadA;
        summary.TopConstraintNameB = string.IsNullOrEmpty(nameB) ? "None" : nameB;
        summary.TopConstraintLoadB = loadB;
    }

    private static void AddTopLoad(
        string name,
        float load,
        ref string nameA,
        ref float loadA,
        ref string nameB,
        ref float loadB)
    {
        if (load > loadA)
        {
            nameB = nameA;
            loadB = loadA;
            nameA = name;
            loadA = load;
        }
        else if (load > loadB)
        {
            nameB = name;
            loadB = load;
        }
    }
}
