using UnityEngine;

public sealed class HingeOverlayController
{
    private const int RingSegments = 48;
    private const int ArcSegments = 28;

    private struct HingeTarget
    {
        public HingeJoint Joint;
        public SandboxDrawingPhysicsRuntime Runtime;
        public Rigidbody Body;
        public PhysicsLensConstraintSummary Summary;
    }

    private sealed class HingeVisual
    {
        public GameObject Root;
        public LineRenderer Axis;
        public LineRenderer Ring;
        public LineRenderer Needle;
        public LineRenderer LimitArc;
        public Vector3[] RingPoints;
        public Vector3[] ArcPoints;
    }

    private HingeTarget[] _targets;
    private HingeVisual[] _visuals;
    private Material _material;
    private int _targetCount;
    private int _used;

    public void Initialize(Transform parent, VisualizationConfig config)
    {
        var capacity = config != null ? config.MaxHingeOverlays : 18;
        _targets = new HingeTarget[Mathf.Max(1, capacity)];
        _visuals = new HingeVisual[Mathf.Max(1, capacity)];
        _material = VisualizationRenderUtility.CreateOverlayMaterial(
            "SimulationVisualizationHingeCompass",
            config != null ? config.HingeColor : Color.yellow);

        for (var i = 0; i < _visuals.Length; i++)
            _visuals[i] = CreateVisual(parent, i);
    }

    public void Dispose()
    {
        if (_visuals != null)
        {
            for (var i = 0; i < _visuals.Length; i++)
            {
                if (_visuals[i] != null && _visuals[i].Root != null)
                    Object.Destroy(_visuals[i].Root);
            }
        }

        if (_material != null)
            Object.Destroy(_material);

        _targets = null;
        _visuals = null;
        _material = null;
        _targetCount = 0;
        _used = 0;
    }

    public void RefreshTargets(BodyTelemetryTracker[] trackers, int trackerCount, VisualizationConfig config)
    {
        if (_targets == null || config == null)
            return;

        _targetCount = 0;
        var capacity = Mathf.Min(_targets.Length, config.MaxHingeOverlays);

        var joints = Object.FindObjectsByType<HingeJoint>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (var i = 0; i < joints.Length && _targetCount < capacity; i++)
        {
            var joint = joints[i];
            var body = joint != null ? joint.GetComponent<Rigidbody>() : null;
            if (!IsUserBody(body) && !IsUserBody(joint != null ? joint.connectedBody : null))
                continue;

            _targets[_targetCount++] = new HingeTarget
            {
                Joint = joint,
                Runtime = null,
                Body = body != null ? body : joint.connectedBody
            };
        }

        var runtimes = Object.FindObjectsByType<SandboxDrawingPhysicsRuntime>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        for (var i = 0; i < runtimes.Length && _targetCount < capacity; i++)
        {
            var runtime = runtimes[i];
            if (runtime == null || ContainsRuntime(runtime))
                continue;

            for (var bodyIndex = 0; bodyIndex < trackerCount && _targetCount < capacity; bodyIndex++)
            {
                var rb = trackers[bodyIndex] != null ? trackers[bodyIndex].Rigidbody : null;
                if (rb == null
                    || !runtime.TryGetPhysicsLensHingeTelemetry(
                        rb,
                        out _,
                        out _,
                        out _,
                        out _,
                        out _,
                        out _))
                {
                    continue;
                }

                _targets[_targetCount++] = new HingeTarget
                {
                    Joint = null,
                    Runtime = runtime,
                    Body = rb
                };
                break;
            }
        }
    }

    public void Render(VisualizationConfig config, Rigidbody selectedBody, Camera camera)
    {
        _used = 0;
        if (_targets == null || _visuals == null || config == null)
            return;

        for (var i = 0; i < _targetCount && _used < _visuals.Length; i++)
        {
            var target = _targets[i];
            if (!TryUpdateSummary(ref target, camera, config))
                continue;

            _targets[i] = target;
            var focus = selectedBody != null && target.Body == selectedBody;
            var load = Mathf.Max(target.Summary.TorqueMagnitude, target.Summary.NormalizedLimitProximity);
            var loadBlend = Mathf.Clamp01(load / Mathf.Max(0.001f, config.HingeLoadFocusThreshold));
            RenderVisual(_visuals[_used++], target.Summary, focus, loadBlend, config);
        }

        EndFrame();
    }

    public void HideAll()
    {
        _used = 0;
        EndFrame();
    }

    private void EndFrame()
    {
        if (_visuals == null)
            return;

        for (var i = _used; i < _visuals.Length; i++)
        {
            if (_visuals[i].Root != null && _visuals[i].Root.activeSelf)
                _visuals[i].Root.SetActive(false);
        }
    }

    private HingeVisual CreateVisual(Transform parent, int index)
    {
        var root = new GameObject("HingeCompass_" + index);
        root.transform.SetParent(parent, false);

        var visual = new HingeVisual
        {
            Root = root,
            Axis = CreateLine(root.transform, "Axis", false, 2),
            Ring = CreateLine(root.transform, "Ring", true, RingSegments),
            Needle = CreateLine(root.transform, "Needle", false, 2),
            LimitArc = CreateLine(root.transform, "LimitArc", false, ArcSegments),
            RingPoints = new Vector3[RingSegments],
            ArcPoints = new Vector3[ArcSegments]
        };
        root.SetActive(false);
        return visual;
    }

    private LineRenderer CreateLine(Transform parent, string name, bool loop, int pointCount)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var line = go.AddComponent<LineRenderer>();
        VisualizationRenderUtility.ConfigureLine(line, _material, loop);
        line.positionCount = pointCount;
        return line;
    }

    private void RenderVisual(
        HingeVisual visual,
        PhysicsLensConstraintSummary summary,
        bool focus,
        float loadBlend,
        VisualizationConfig config)
    {
        if (visual == null || visual.Root == null)
            return;

        var pivot = summary.WorldAnchorA;
        var axis = summary.AxisWorld.sqrMagnitude > 0.0001f ? summary.AxisWorld.normalized : Vector3.up;
        VisualizationRenderUtility.BuildBasis(axis, out var right, out var up);

        var radius = config.HingeRadius * (focus ? 1.15f : 0.82f);
        var color = Color.Lerp(config.HingeColor, config.HingeWarningColor, summary.NormalizedLimitProximity);
        color.a = focus ? 0.96f : Mathf.Lerp(0.26f, 0.72f, loadBlend);
        var width = Mathf.Lerp(config.HingeAmbientWidth, config.HingeFocusWidth, focus ? 1f : loadBlend);

        visual.Root.SetActive(true);
        SetLineColor(visual.Axis, color, width);
        visual.Axis.SetPosition(0, pivot - axis * (config.AxisRodLength * 0.5f));
        visual.Axis.SetPosition(1, pivot + axis * (config.AxisRodLength * 0.5f));

        SetLineColor(visual.Ring, color, width * 0.78f);
        for (var segment = 0; segment < RingSegments; segment++)
        {
            var angle = segment * Mathf.PI * 2f / RingSegments;
            visual.RingPoints[segment] = pivot
                                         + right * (Mathf.Cos(angle) * radius)
                                         + up * (Mathf.Sin(angle) * radius);
            visual.Ring.SetPosition(segment, visual.RingPoints[segment]);
        }

        var needleDirection = summary.WorldAnchorB - pivot;
        needleDirection = Vector3.ProjectOnPlane(needleDirection, axis);
        if (needleDirection.sqrMagnitude <= 0.0001f)
            needleDirection = Quaternion.AngleAxis(summary.HingeAngle, axis) * right;
        needleDirection.Normalize();

        SetLineColor(visual.Needle, color, width * 1.25f);
        visual.Needle.SetPosition(0, pivot);
        visual.Needle.SetPosition(1, pivot + needleDirection * radius);

        if (summary.HasHingeLimits)
        {
            visual.LimitArc.gameObject.SetActive(true);
            var arcColor = config.HingeColor;
            arcColor.a = focus ? 0.46f : 0.22f;
            SetLineColor(visual.LimitArc, arcColor, width * 0.56f);
            BuildLimitArc(visual, pivot, right, up, radius * 1.08f, summary.HingeMinLimit, summary.HingeMaxLimit);
        }
        else
        {
            visual.LimitArc.gameObject.SetActive(false);
        }
    }

    private static void BuildLimitArc(
        HingeVisual visual,
        Vector3 pivot,
        Vector3 right,
        Vector3 up,
        float radius,
        float minDegrees,
        float maxDegrees)
    {
        if (maxDegrees < minDegrees)
        {
            var swap = minDegrees;
            minDegrees = maxDegrees;
            maxDegrees = swap;
        }

        for (var i = 0; i < ArcSegments; i++)
        {
            var t = ArcSegments <= 1 ? 0f : i / (float)(ArcSegments - 1);
            var angle = Mathf.Lerp(minDegrees, maxDegrees, t) * Mathf.Deg2Rad;
            visual.ArcPoints[i] = pivot
                                  + right * (Mathf.Cos(angle) * radius)
                                  + up * (Mathf.Sin(angle) * radius);
            visual.LimitArc.SetPosition(i, visual.ArcPoints[i]);
        }
    }

    private static void SetLineColor(LineRenderer line, Color color, float width)
    {
        if (line == null)
            return;

        line.startColor = color;
        line.endColor = color;
        line.startWidth = width;
        line.endWidth = width;
    }

    private bool TryUpdateSummary(ref HingeTarget target, Camera camera, VisualizationConfig config)
    {
        if (target.Joint != null)
            return TryBuildHingeJointSummary(target.Joint, config, out target.Summary);

        if (target.Runtime != null && target.Body != null)
            return TryBuildRuntimeHingeSummary(target.Runtime, target.Body, camera, out target.Summary);

        return false;
    }

    private bool TryBuildHingeJointSummary(HingeJoint joint, VisualizationConfig config, out PhysicsLensConstraintSummary summary)
    {
        summary = default;
        if (joint == null)
            return false;

        var body = joint.GetComponent<Rigidbody>();
        var pivot = body != null ? body.transform.TransformPoint(joint.anchor) : joint.transform.TransformPoint(joint.anchor);
        var axis = joint.transform.TransformDirection(joint.axis);
        if (axis.sqrMagnitude <= 0.0001f)
            axis = Vector3.up;
        axis.Normalize();

        var reference = Vector3.ProjectOnPlane(joint.transform.forward, axis);
        if (reference.sqrMagnitude <= 0.0001f)
            reference = Vector3.ProjectOnPlane(joint.transform.up, axis);
        if (reference.sqrMagnitude <= 0.0001f)
            reference = Vector3.Cross(axis, Vector3.up);
        if (reference.sqrMagnitude <= 0.0001f)
            reference = Vector3.right;
        reference.Normalize();

        var connected = joint.connectedBody;
        var angularVelocity = body != null ? body.angularVelocity : Vector3.zero;
        if (connected != null)
            angularVelocity -= connected.angularVelocity;

        var signedAngularVelocity = Vector3.Dot(angularVelocity, axis) * Mathf.Rad2Deg;
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
            var warningDegrees = config != null ? config.HingeLimitWarningDegrees : 18f;
            proximity = 1f - Mathf.Clamp01(distanceToLimit / warningDegrees);
        }

        if (torque <= 0.001f)
            torque = Mathf.Abs(signedAngularVelocity) * Mathf.Max(0.1f, body != null ? body.mass : 1f) * 0.01f + proximity;

        summary = new PhysicsLensConstraintSummary
        {
            Kind = PhysicsLensConstraintKind.Hinge,
            IsValid = true,
            DisplayName = joint.name,
            WorldAnchorA = pivot,
            WorldAnchorB = pivot + Quaternion.AngleAxis(angle, axis) * reference,
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
        Rigidbody body,
        Camera camera,
        out PhysicsLensConstraintSummary summary)
    {
        summary = default;
        if (runtime == null
            || body == null
            || !runtime.TryGetPhysicsLensHingeTelemetry(
                body,
                out var pivot,
                out var bodyPoint,
                out var restLength,
                out var stiffness,
                out var damper,
                out var displayName))
        {
            return false;
        }

        var radial = bodyPoint - pivot;
        var length = radial.magnitude;
        if (length <= 0.0001f)
            radial = body.worldCenterOfMass - pivot;
        if (radial.sqrMagnitude <= 0.0001f)
            radial = Vector3.forward;
        radial.Normalize();

        var axis = Vector3.up;
        if (body.angularVelocity.sqrMagnitude > 0.0001f)
            axis = body.angularVelocity.normalized;
        else if (camera != null)
            axis = camera.transform.forward;

        if (Mathf.Abs(Vector3.Dot(axis.normalized, radial)) > 0.92f)
            axis = Vector3.Cross(radial, Vector3.up);
        if (axis.sqrMagnitude <= 0.0001f)
            axis = Vector3.Cross(radial, Vector3.right);
        axis.Normalize();

        var projectedNeedle = Vector3.ProjectOnPlane(radial, axis);
        if (projectedNeedle.sqrMagnitude <= 0.0001f)
            projectedNeedle = Vector3.Cross(axis, Vector3.up);
        if (projectedNeedle.sqrMagnitude <= 0.0001f)
            projectedNeedle = Vector3.right;
        projectedNeedle.Normalize();

        var reference = Vector3.ProjectOnPlane(Vector3.forward, axis);
        if (reference.sqrMagnitude <= 0.0001f)
            reference = Vector3.ProjectOnPlane(Vector3.right, axis);
        reference.Normalize();

        var pointVelocity = body.GetPointVelocity(bodyPoint);
        var outwardSpeed = Mathf.Max(0f, Vector3.Dot(pointVelocity, radial));
        var extension = Mathf.Max(0f, length - Mathf.Max(0.001f, restLength));
        var load = stiffness * extension + damper * outwardSpeed;
        var torque = Mathf.Max(0f, load * Mathf.Max(0.02f, length));
        var signedAngularVelocity = Vector3.Dot(body.angularVelocity, axis) * Mathf.Rad2Deg;

        summary = new PhysicsLensConstraintSummary
        {
            Kind = PhysicsLensConstraintKind.Hinge,
            IsValid = true,
            DisplayName = displayName,
            WorldAnchorA = pivot,
            WorldAnchorB = pivot + projectedNeedle,
            AxisWorld = axis,
            HingeAngle = Vector3.SignedAngle(reference, projectedNeedle, axis),
            HasHingeLimits = false,
            HingeMinLimit = 0f,
            HingeMaxLimit = 0f,
            SignedAngularVelocityDeg = signedAngularVelocity,
            TorqueMagnitude = torque,
            LoadMagnitude = torque,
            DistanceToLimit = float.PositiveInfinity,
            NormalizedLimitProximity = 0f,
            BreakRatio = -1f
        };
        return true;
    }

    private bool ContainsRuntime(SandboxDrawingPhysicsRuntime runtime)
    {
        for (var i = 0; i < _targetCount; i++)
        {
            if (_targets[i].Runtime == runtime)
                return true;
        }

        return false;
    }

    private static bool IsUserBody(Rigidbody body)
    {
        return body != null
               && body.GetComponentInParent<PlaceableAsset>() != null
               && body.GetComponentInParent<SpawnTemplateMarker>() == null;
    }

    private static float ResolveBreakRatio(float load, float breakValue)
    {
        if (breakValue <= 0f || float.IsInfinity(breakValue))
            return -1f;
        return Mathf.Clamp01(load / breakValue);
    }
}
