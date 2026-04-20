using UnityEngine;

public sealed class SpringOverlayController
{
    private struct SpringTarget
    {
        public SpringJoint Joint;
        public SandboxDrawingPhysicsRuntime Runtime;
        public Rigidbody BodyA;
        public Rigidbody BodyB;
        public PhysicsLensConstraintSummary Summary;
    }

    private struct SpringLine
    {
        public GameObject Root;
        public LineRenderer Line;
    }

    private SpringTarget[] _targets;
    private SpringLine[] _lines;
    private Material _material;
    private int _targetCount;
    private int _used;

    public void Initialize(Transform parent, VisualizationConfig config)
    {
        var capacity = config != null ? config.MaxSpringOverlays : 32;
        _targets = new SpringTarget[Mathf.Max(1, capacity)];
        _lines = new SpringLine[Mathf.Max(1, capacity)];
        _material = VisualizationRenderUtility.CreateOverlayMaterial(
            "SimulationVisualizationSpringLive",
            config != null ? config.SpringRestColor : Color.white);

        for (var i = 0; i < _lines.Length; i++)
            _lines[i] = CreateLine(parent, i);
    }

    public void Dispose()
    {
        if (_lines != null)
        {
            for (var i = 0; i < _lines.Length; i++)
            {
                if (_lines[i].Root != null)
                    Object.Destroy(_lines[i].Root);
            }
        }

        if (_material != null)
            Object.Destroy(_material);

        _targets = null;
        _lines = null;
        _material = null;
        _targetCount = 0;
        _used = 0;
    }

    public void RefreshTargets(BodyTelemetryTracker[] trackers, int trackerCount, VisualizationConfig config)
    {
        if (_targets == null || config == null)
            return;

        _targetCount = 0;
        var capacity = Mathf.Min(_targets.Length, config.MaxSpringOverlays);

        var joints = Object.FindObjectsByType<SpringJoint>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (var i = 0; i < joints.Length && _targetCount < capacity; i++)
        {
            var joint = joints[i];
            if (joint == null || !IsUserSpring(joint))
                continue;

            _targets[_targetCount++] = new SpringTarget
            {
                Joint = joint,
                Runtime = null,
                BodyA = joint.GetComponent<Rigidbody>(),
                BodyB = joint.connectedBody
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
                    || !runtime.TryGetPhysicsLensSpringTelemetry(
                        rb,
                        out var bodyA,
                        out var bodyB,
                        out _,
                        out _,
                        out _,
                        out _,
                        out _,
                        out _))
                {
                    continue;
                }

                _targets[_targetCount++] = new SpringTarget
                {
                    Joint = null,
                    Runtime = runtime,
                    BodyA = bodyA,
                    BodyB = bodyB
                };
                break;
            }
        }
    }

    public void Render(VisualizationConfig config, Rigidbody selectedBody)
    {
        _used = 0;
        if (_lines == null || _targets == null || config == null)
            return;

        for (var i = 0; i < _targetCount && _used < _lines.Length; i++)
        {
            var target = _targets[i];
            if (!TryUpdateSummary(ref target))
                continue;

            _targets[i] = target;
            var focus = selectedBody != null && (target.BodyA == selectedBody || target.BodyB == selectedBody);
            var loadBlend = Mathf.Clamp01(target.Summary.LoadMagnitude / Mathf.Max(0.001f, config.SpringLoadFocusThreshold));
            var line = _lines[_used++];
            RenderLine(line, target.Summary, focus, loadBlend, config);
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
        if (_lines == null)
            return;

        for (var i = _used; i < _lines.Length; i++)
        {
            if (_lines[i].Root != null && _lines[i].Root.activeSelf)
                _lines[i].Root.SetActive(false);
        }
    }

    private SpringLine CreateLine(Transform parent, int index)
    {
        var root = new GameObject("SpringLiveVisual_" + index);
        root.transform.SetParent(parent, false);
        var line = root.AddComponent<LineRenderer>();
        VisualizationRenderUtility.ConfigureLine(line, _material, false);
        line.positionCount = 2;
        root.SetActive(false);

        return new SpringLine
        {
            Root = root,
            Line = line
        };
    }

    private void RenderLine(SpringLine line, PhysicsLensConstraintSummary summary, bool focus, float loadBlend, VisualizationConfig config)
    {
        if (line.Root == null || line.Line == null)
            return;

        var color = ResolveSpringColor(summary, config);
        color.a = focus ? 0.98f : Mathf.Lerp(0.28f, 0.74f, loadBlend);
        var width = Mathf.Lerp(config.SpringAmbientWidth, config.SpringFocusWidth, focus ? 1f : loadBlend);

        line.Root.SetActive(true);
        line.Line.startColor = color;
        line.Line.endColor = color;
        line.Line.startWidth = width;
        line.Line.endWidth = width;
        line.Line.SetPosition(0, summary.WorldAnchorA);
        line.Line.SetPosition(1, summary.WorldAnchorB);
    }

    private Color ResolveSpringColor(PhysicsLensConstraintSummary summary, VisualizationConfig config)
    {
        if (summary.Extension < -0.01f)
            return config.SpringCompressionColor;
        if (summary.Extension > 0.01f)
            return Color.Lerp(config.SpringTensionColor, config.ImpactColor, Mathf.Clamp01(summary.LoadMagnitude / (config.SpringLoadFocusThreshold * 4f)));
        return config.SpringRestColor;
    }

    private bool TryUpdateSummary(ref SpringTarget target)
    {
        if (target.Joint != null)
            return TryBuildSpringJointSummary(target.Joint, out target.Summary);

        if (target.Runtime != null && target.BodyA != null)
            return TryBuildRuntimeSpringSummary(target.Runtime, target.BodyA, out target.Summary);

        return false;
    }

    private bool TryBuildSpringJointSummary(SpringJoint joint, out PhysicsLensConstraintSummary summary)
    {
        summary = default;
        if (joint == null)
            return false;

        var bodyA = joint.GetComponent<Rigidbody>();
        var bodyB = joint.connectedBody;
        if (bodyA == null && bodyB == null)
            return false;

        var anchorA = bodyA != null
            ? bodyA.transform.TransformPoint(joint.anchor)
            : joint.transform.TransformPoint(joint.anchor);
        var anchorB = bodyB != null ? bodyB.transform.TransformPoint(joint.connectedAnchor) : joint.connectedAnchor;
        var delta = anchorB - anchorA;
        var length = delta.magnitude;
        if (length <= 0.0001f)
            return false;

        var axis = delta / length;
        var rest = ResolveSpringRestLength(joint, length);
        var extension = length - rest;
        var va = bodyA != null ? bodyA.GetPointVelocity(anchorA) : Vector3.zero;
        var vb = bodyB != null ? bodyB.GetPointVelocity(anchorB) : Vector3.zero;
        var relativeSpeed = Vector3.Dot(vb - va, axis);
        var estimated = joint.spring * extension + joint.damper * relativeSpeed;
        var currentForce = joint.currentForce.magnitude;
        var signedLoad = currentForce > 0.001f
            ? Mathf.Sign(Mathf.Abs(extension) > 0.001f ? extension : estimated) * currentForce
            : estimated;

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
            LoadMagnitude = Mathf.Abs(signedLoad),
            SpringState = extension < -0.01f
                ? PhysicsLensSpringState.Compressing
                : extension > 0.01f ? PhysicsLensSpringState.Stretching : PhysicsLensSpringState.NearRest,
            BreakRatio = ResolveBreakRatio(Mathf.Abs(signedLoad), joint.breakForce),
            DistanceToLimit = float.PositiveInfinity
        };
        return true;
    }

    private bool TryBuildRuntimeSpringSummary(
        SandboxDrawingPhysicsRuntime runtime,
        Rigidbody target,
        out PhysicsLensConstraintSummary summary)
    {
        summary = default;
        if (runtime == null
            || target == null
            || !runtime.TryGetPhysicsLensSpringTelemetry(
                target,
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
        if (length <= 0.0001f)
            return false;

        var axis = delta / length;
        rest = Mathf.Max(0.001f, rest);
        var extension = length - rest;
        var va = bodyA != null ? bodyA.GetPointVelocity(anchorA) : Vector3.zero;
        var vb = bodyB != null ? bodyB.GetPointVelocity(anchorB) : Vector3.zero;
        var relativeSpeed = Vector3.Dot(vb - va, axis);
        var rawLoad = Mathf.Max(0f, strength * length + damper * relativeSpeed);
        var signedLoad = Mathf.Sign(Mathf.Abs(extension) > 0.001f ? extension : rawLoad) * rawLoad;

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
            LoadMagnitude = Mathf.Abs(signedLoad),
            SpringState = extension < -0.01f
                ? PhysicsLensSpringState.Compressing
                : extension > 0.01f ? PhysicsLensSpringState.Stretching : PhysicsLensSpringState.NearRest,
            BreakRatio = -1f,
            DistanceToLimit = float.PositiveInfinity
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

    private static bool IsUserSpring(SpringJoint joint)
    {
        if (joint == null)
            return false;

        var bodyA = joint.GetComponent<Rigidbody>();
        return IsUserBody(bodyA) || IsUserBody(joint.connectedBody);
    }

    private static bool IsUserBody(Rigidbody body)
    {
        return body != null
               && body.GetComponentInParent<PlaceableAsset>() != null
               && body.GetComponentInParent<SpawnTemplateMarker>() == null;
    }

    private static float ResolveSpringRestLength(SpringJoint joint, float currentLength)
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
            return Mathf.Max(0.001f, (joint.minDistance / 0.85f + joint.maxDistance / 1.25f) * 0.5f);

        if (joint.maxDistance > 0.001f)
            return Mathf.Max(0.001f, joint.maxDistance);

        if (joint.minDistance > 0.001f)
            return Mathf.Max(0.001f, joint.minDistance);

        return Mathf.Max(0.001f, currentLength);
    }

    private static float ResolveBreakRatio(float load, float breakValue)
    {
        if (breakValue <= 0f || float.IsInfinity(breakValue))
            return -1f;
        return Mathf.Clamp01(load / breakValue);
    }
}
