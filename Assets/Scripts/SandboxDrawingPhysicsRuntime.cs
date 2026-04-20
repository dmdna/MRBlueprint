using UnityEngine;

public sealed class SandboxDrawingPhysicsRuntime : MonoBehaviour
{
    private const float MinDistance = 0.001f;
    private const float HingeProjectionStartStretch = 1.08f;
    private const float HingeProjectionFraction = 0.18f;

    private enum RuntimeMode
    {
        Spring,
        Impulse,
        Hinge
    }

    private PhysicsDrawingSelectable _drawing;
    private RuntimeMode _mode;
    private Rigidbody _bodyA;
    private Rigidbody _bodyB;
    private Vector3 _localPointA;
    private Vector3 _localPointB;
    private Vector3 _direction;
    private Vector3 _localDirection;
    private Vector3 _fixedPivot;
    private float _strength;
    private float _damper;
    private float _restLength;
    private bool _instantImpulse;
    private bool _instantApplied;
    private bool _directionFollowsBody;

    public void ConfigureSpring(
        PhysicsDrawingSelectable drawing,
        Rigidbody bodyA,
        Vector3 localPointA,
        Rigidbody bodyB,
        Vector3 localPointB,
        float strength,
        float damper)
    {
        _drawing = drawing;
        _mode = RuntimeMode.Spring;
        _bodyA = bodyA;
        _localPointA = localPointA;
        _bodyB = bodyB;
        _localPointB = localPointB;
        _strength = Mathf.Max(0f, strength);
        _damper = Mathf.Max(0f, damper);
        _instantImpulse = false;
        _instantApplied = false;
        _directionFollowsBody = false;
    }

    public void ConfigureImpulse(
        PhysicsDrawingSelectable drawing,
        Rigidbody body,
        Vector3 localPoint,
        Vector3 direction,
        float strength,
        bool instant,
        bool directionFollowsBody)
    {
        _drawing = drawing;
        _mode = RuntimeMode.Impulse;
        _bodyA = body;
        _localPointA = localPoint;
        _direction = direction.sqrMagnitude > 0.000001f ? direction.normalized : Vector3.forward;
        _localDirection = body != null ? body.transform.InverseTransformDirection(_direction) : _direction;
        _strength = Mathf.Max(0f, strength);
        _damper = 0f;
        _instantImpulse = instant;
        _instantApplied = false;
        _directionFollowsBody = directionFollowsBody;
    }

    public void ConfigureHinge(
        PhysicsDrawingSelectable drawing,
        Rigidbody body,
        Vector3 localPoint,
        Vector3 fixedPivot,
        float stringLength,
        float stiffness,
        float damper)
    {
        _drawing = drawing;
        _mode = RuntimeMode.Hinge;
        _bodyA = body;
        _localPointA = localPoint;
        _fixedPivot = fixedPivot;
        _restLength = Mathf.Max(MinDistance, stringLength);
        _strength = Mathf.Max(0f, stiffness);
        _damper = Mathf.Max(0f, damper);
        _instantImpulse = false;
        _instantApplied = false;
        _directionFollowsBody = false;
    }

    private void FixedUpdate()
    {
        if (!IsSimulationStepping())
        {
            return;
        }

        switch (_mode)
        {
            case RuntimeMode.Spring:
                ApplySpring();
                break;
            case RuntimeMode.Impulse:
                ApplyImpulse();
                break;
            case RuntimeMode.Hinge:
                ApplyHinge();
                break;
        }
    }

    private bool IsSimulationStepping()
    {
        var sim = SandboxSimulationController.Instance;
        return sim != null
               && sim.IsSimulating
               && !sim.IsPaused
               && _drawing != null
               && _bodyA != null;
    }

    private void ApplySpring()
    {
        if (_bodyB == null || _bodyA == _bodyB)
        {
            return;
        }

        var pointA = _bodyA.transform.TransformPoint(_localPointA);
        var pointB = _bodyB.transform.TransformPoint(_localPointB);
        var delta = pointB - pointA;
        var distance = delta.magnitude;
        if (distance <= MinDistance)
        {
            return;
        }

        var direction = delta / distance;
        var relativeVelocity = _bodyB.GetPointVelocity(pointB) - _bodyA.GetPointVelocity(pointA);
        var separatingSpeed = Vector3.Dot(relativeVelocity, direction);
        var forceMagnitude = Mathf.Max(0f, _strength * distance + _damper * separatingSpeed);
        var force = direction * forceMagnitude;

        _bodyA.AddForceAtPosition(force, pointA, ForceMode.Force);
        _bodyB.AddForceAtPosition(-force, pointB, ForceMode.Force);
    }

    private void ApplyImpulse()
    {
        if (_instantImpulse && _instantApplied)
        {
            return;
        }

        var worldPoint = _bodyA.transform.TransformPoint(_localPointA);
        var direction = _directionFollowsBody
            ? _bodyA.transform.TransformDirection(_localDirection).normalized
            : _direction;
        _bodyA.AddForceAtPosition(
            direction * _strength,
            worldPoint,
            _instantImpulse ? ForceMode.Impulse : ForceMode.Force);
        _instantApplied = true;
    }

    private void ApplyHinge()
    {
        var worldPoint = _bodyA.transform.TransformPoint(_localPointA);
        var fromPivot = worldPoint - _fixedPivot;
        var distance = fromPivot.magnitude;
        if (distance <= _restLength || distance <= MinDistance)
        {
            return;
        }

        var awayDirection = fromPivot / distance;
        var towardPivot = -awayDirection;
        var pointVelocity = _bodyA.GetPointVelocity(worldPoint);
        var outwardSpeed = Mathf.Max(0f, Vector3.Dot(pointVelocity, awayDirection));
        var extension = distance - _restLength;
        var forceMagnitude = _strength * extension + _damper * outwardSpeed;

        _bodyA.AddForceAtPosition(towardPivot * forceMagnitude, worldPoint, ForceMode.Force);

        if (distance <= _restLength * HingeProjectionStartStretch)
        {
            return;
        }

        var correction = towardPivot * (extension * HingeProjectionFraction);
        _bodyA.position += correction;
    }
}
