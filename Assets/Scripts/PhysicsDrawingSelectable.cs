using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
[RequireComponent(typeof(LineRenderer))]
public sealed class PhysicsDrawingSelectable : MonoBehaviour
{
    private const float ArrowConeLength = 0.03f;
    private const float ArrowConeRadius = 0.009f;
    private const float AttachmentContactEpsilon = 0.0005f;
    private const float AttachmentRaycastPadding = 0.05f;

    [SerializeField] private string displayName = "Drawing";
    [SerializeField] private PhysicsIntentType physicsIntent = PhysicsIntentType.Unknown;
    [SerializeField] private string shapeName = "Unknown";
    [SerializeField] private Color highlightColor = Color.yellow;
    [SerializeField] private float colliderRadius = 0.025f;
    [SerializeField, Range(0f, 1f)] private float springStiffness = 0.5f;
    [SerializeField, Range(0f, 1f)] private float hingeTorque = 0.5f;
    [SerializeField, Range(0f, 1f)] private float impulseForce = 0.5f;
    [SerializeField] private bool impulseInstant;
    [SerializeField] private Color springZeroStiffnessColor = Color.cyan;
    [SerializeField] private Color springMidStiffnessColor = Color.yellow;
    [SerializeField] private Color springFullStiffnessColor = Color.red;
    [SerializeField] private Color selectionAuraColor = new Color(1f, 1f, 1f, 0.24f);
    [SerializeField] private float selectionAuraWidthMultiplier = 2.8f;
    [SerializeField] private float selectionAuraConeScaleMultiplier = 1.5f;
    [SerializeField] private float selectionAuraConeBaseOverlapFraction = 0.35f;
    [SerializeField] private float endpointHandleDiameter = 0.045f;
    [SerializeField] private float endpointHandleColliderRadius = 0.035f;
    [SerializeField] private Color endpointHandleHoverColor = new Color(1f, 1f, 1f, 0.82f);
    [SerializeField] private Color endpointHandleDragColor = new Color(0.2f, 0.85f, 1f, 0.95f);
    [SerializeField] private float attachmentSnapDistance = 0.18f;
    [SerializeField] private float linearAttachmentSurfaceTolerance = 0.015f;
    [SerializeField] private float linearAttachmentSurfaceOffset = 0.004f;
    [SerializeField] private float attachmentIndicatorDiameter = 0.06f;
    [SerializeField] private float hingeAttachmentLineWidth = 0.01f;
    [SerializeField] private Color attachmentIndicatorColor = Color.white;

    private readonly List<GameObject> _colliders = new();
    private LineRenderer _lineRenderer;
    private LineRenderer _selectionAuraRenderer;
    private LineRenderer _attachmentLineRenderer;
    private LineArrowTip _arrowTip;
    private PhysicsDrawingEndpointHandle _startHandle;
    private PhysicsDrawingEndpointHandle _endHandle;
    private Material _selectionAuraMaterial;
    private Material _attachmentMaterial;
    private GameObject _attachmentSphere;
    private MeshRenderer _attachmentSphereRenderer;
    private LineDrawing _owner;
    private PlaceableAsset _attachedPlaceable;
    private PlaceableAsset _previewPlaceable;
    private Vector3[] _attachedLocalLinePositions;
    private Vector3 _previewJunctionPoint;
    private Vector3 _attachedLastPosition;
    private Vector3 _attachedLastScale;
    private Quaternion _attachedLastRotation;
    private PhysicsDrawingEndpoint _attachedLinearEndpoint = PhysicsDrawingEndpoint.Start;
    private Color _baseColor = Color.white;
    private bool _isHovered;
    private bool _isSelected;
    private bool _isApplyingAttachmentFollow;

    public string DisplayName => displayName;
    public PhysicsIntentType PhysicsIntent => physicsIntent;
    public string ShapeName => shapeName;
    public bool IsSelected => _isSelected;
    public bool SupportsEndpointEditing => physicsIntent == PhysicsIntentType.Spring || physicsIntent == PhysicsIntentType.Impulse;
    public bool SupportsRadiusScaling => physicsIntent == PhysicsIntentType.Hinge;
    public bool CanAttachToPlaceable =>
        physicsIntent == PhysicsIntentType.Spring
        || physicsIntent == PhysicsIntentType.Impulse
        || physicsIntent == PhysicsIntentType.Hinge;
    public PlaceableAsset AttachedPlaceable => _attachedPlaceable;
    public bool IsAttachedToPlaceable => _attachedPlaceable != null;
    public float SpringStiffness => springStiffness;
    public float HingeTorque => hingeTorque;
    public float ImpulseForce => impulseForce;
    public bool ImpulseInstant => impulseInstant;

    private void Awake()
    {
        ResolveReferences();
        CacheBaseColor();
        RefreshEndpointHandles();
    }

    private void OnValidate()
    {
        springStiffness = Mathf.Clamp01(springStiffness);
        hingeTorque = Mathf.Clamp01(hingeTorque);
        impulseForce = Mathf.Clamp01(impulseForce);
        selectionAuraWidthMultiplier = Mathf.Max(1f, selectionAuraWidthMultiplier);
        selectionAuraConeScaleMultiplier = Mathf.Max(1f, selectionAuraConeScaleMultiplier);
        selectionAuraConeBaseOverlapFraction = Mathf.Max(0f, selectionAuraConeBaseOverlapFraction);
        endpointHandleDiameter = Mathf.Max(0.001f, endpointHandleDiameter);
        endpointHandleColliderRadius = Mathf.Max(endpointHandleDiameter * 0.5f, endpointHandleColliderRadius);
        attachmentSnapDistance = Mathf.Max(0.001f, attachmentSnapDistance);
        linearAttachmentSurfaceTolerance = Mathf.Max(0.001f, linearAttachmentSurfaceTolerance);
        linearAttachmentSurfaceOffset = Mathf.Max(0f, linearAttachmentSurfaceOffset);
        attachmentIndicatorDiameter = Mathf.Max(0.001f, attachmentIndicatorDiameter);
        hingeAttachmentLineWidth = Mathf.Max(0.001f, hingeAttachmentLineWidth);
    }

    private void LateUpdate()
    {
        FollowAttachedPlaceable();
    }

    private void OnDestroy()
    {
        if (AssetSelectionManager.Instance != null
            && AssetSelectionManager.Instance.SelectedPhysicsDrawing == this)
        {
            AssetSelectionManager.Instance.ClearSelection();
        }

        if (_selectionAuraMaterial != null)
        {
            Destroy(_selectionAuraMaterial);
        }

        if (_attachmentMaterial != null)
        {
            Destroy(_attachmentMaterial);
        }
    }

    public void Initialize(PhysicsGestureReadoutResult readout, Color selectedHighlightColor)
    {
        Initialize(readout, selectedHighlightColor, _baseColor);
    }

    public void SetOwner(LineDrawing owner)
    {
        _owner = owner;
    }

    public void Initialize(PhysicsGestureReadoutResult readout, Color selectedHighlightColor, Color zeroStiffnessColor)
    {
        ResolveReferences();
        highlightColor = selectedHighlightColor;
        springZeroStiffnessColor = zeroStiffnessColor;

        if (readout != null)
        {
            physicsIntent = readout.PhysicsIntent;
            shapeName = string.IsNullOrEmpty(readout.ShapeName) ? "Unknown" : readout.ShapeName;
            displayName = ResolveDisplayName(readout);
        }

        CacheBaseColor();
        RefreshPhysicsColor();
        RebuildColliders();
        ApplyHighlightState();

        SandboxStrokePlaceablePhysicsApplier.TryApplyFromDrawing(this);
    }

    public void SetHovered(bool hovered)
    {
        _isHovered = hovered;
        ApplyHighlightState();
        SetSelectionAuraVisible(_isSelected || _isHovered);
    }

    public void SetSelected(bool selected)
    {
        _isSelected = selected;
        ApplyHighlightState();
        SetSelectionAuraVisible(_isSelected || _isHovered);
    }

    public void SetSpringStiffness(float value)
    {
        springStiffness = Mathf.Clamp01(value);
        RefreshSpringColor();
    }

    public void SetHingeTorque(float value)
    {
        hingeTorque = Mathf.Clamp01(value);
        RefreshHingeColor();
    }

    public void SetImpulseForce(float value)
    {
        impulseForce = Mathf.Clamp01(value);
        RefreshImpulseColor();
    }

    public void SetImpulseInstant(bool instant)
    {
        impulseInstant = instant;
    }

    public Vector3 GetGrabPosition()
    {
        ResolveReferences();
        if (_lineRenderer == null || _lineRenderer.positionCount == 0)
        {
            return transform.position;
        }

        var first = GetWorldLinePosition(0);
        var bounds = new Bounds(first, Vector3.zero);
        for (var i = 1; i < _lineRenderer.positionCount; i++)
        {
            bounds.Encapsulate(GetWorldLinePosition(i));
        }

        return bounds.center;
    }

    public void SetGrabPosition(Vector3 position)
    {
        TranslateLine(position - GetGrabPosition());
    }

    public void TranslateLine(Vector3 worldDelta)
    {
        ResolveReferences();
        if (_lineRenderer == null
            || _lineRenderer.positionCount == 0
            || worldDelta.sqrMagnitude <= 0.00000001f)
        {
            return;
        }

        var delta = _lineRenderer.useWorldSpace
            ? worldDelta
            : transform.InverseTransformVector(worldDelta);
        for (var i = 0; i < _lineRenderer.positionCount; i++)
        {
            _lineRenderer.SetPosition(i, _lineRenderer.GetPosition(i) + delta);
        }

        RefreshGeometryAfterLineEdit();
    }

    public Vector3[] GetWorldLinePositions()
    {
        ResolveReferences();
        if (_lineRenderer == null || _lineRenderer.positionCount == 0)
        {
            return new Vector3[0];
        }

        var positions = new Vector3[_lineRenderer.positionCount];
        for (var i = 0; i < positions.Length; i++)
        {
            positions[i] = GetWorldLinePosition(i);
        }

        return positions;
    }

    public void SetWorldLinePositions(IReadOnlyList<Vector3> worldPositions)
    {
        ResolveReferences();
        if (_lineRenderer == null || worldPositions == null || worldPositions.Count == 0)
        {
            return;
        }

        _lineRenderer.positionCount = worldPositions.Count;
        for (var i = 0; i < worldPositions.Count; i++)
        {
            var position = _lineRenderer.useWorldSpace
                ? worldPositions[i]
                : transform.InverseTransformPoint(worldPositions[i]);
            _lineRenderer.SetPosition(i, position);
        }

        RefreshGeometryAfterLineEdit();
    }

    public void SetScaledWorldLinePositions(
        IReadOnlyList<Vector3> initialWorldPositions,
        Vector3 targetCenter,
        float scaleFactor)
    {
        SetTransformedWorldLinePositions(
            initialWorldPositions,
            targetCenter,
            Quaternion.identity,
            scaleFactor);
    }

    public void SetTransformedWorldLinePositions(
        IReadOnlyList<Vector3> initialWorldPositions,
        Vector3 targetCenter,
        Quaternion rotation,
        float scaleFactor)
    {
        ResolveReferences();
        if (!SupportsRadiusScaling
            || _lineRenderer == null
            || initialWorldPositions == null
            || initialWorldPositions.Count == 0)
        {
            return;
        }

        scaleFactor = Mathf.Max(0.001f, scaleFactor);
        var initialCenter = CalculateWorldBoundsCenter(initialWorldPositions);
        var scaledPositions = new Vector3[initialWorldPositions.Count];
        for (var i = 0; i < initialWorldPositions.Count; i++)
        {
            scaledPositions[i] = targetCenter + rotation * ((initialWorldPositions[i] - initialCenter) * scaleFactor);
        }

        if (TrySetAttachedGroupWorldLinePositions(scaledPositions))
        {
            return;
        }

        SetWorldLinePositions(scaledPositions);
    }

    public bool SetEndpointWorldPosition(PhysicsDrawingEndpoint endpoint, Vector3 worldPosition)
    {
        ResolveReferences();
        if (!SupportsEndpointEditing || _lineRenderer == null || _lineRenderer.positionCount < 2)
        {
            return false;
        }

        var positions = GetWorldLinePositions();
        if (positions.Length < 2)
        {
            return false;
        }

        var startIndex = 0;
        var endIndex = positions.Length - 1;
        var oldStart = positions[startIndex];
        var oldEnd = GetVisualEndpointWorldPosition(positions);
        var newStart = endpoint == PhysicsDrawingEndpoint.Start ? worldPosition : oldStart;
        var newEnd = endpoint == PhysicsDrawingEndpoint.End ? worldPosition : oldEnd;
        var newLineEnd = GetLineEndFromVisualEndpoint(newStart, newEnd);

        var oldAxis = oldEnd - oldStart;
        var newAxis = newEnd - newStart;
        var oldLength = oldAxis.magnitude;
        var newLength = newAxis.magnitude;

        if (oldLength <= 0.0001f || newLength <= 0.0001f || positions.Length == 2)
        {
            positions[startIndex] = newStart;
            positions[endIndex] = newLineEnd;
            SetWorldLinePositions(positions);
            return true;
        }

        var oldDirection = oldAxis / oldLength;
        var newDirection = newAxis / newLength;
        var rotation = Quaternion.FromToRotation(oldDirection, newDirection);
        for (var i = 0; i < positions.Length; i++)
        {
            var relative = positions[i] - oldStart;
            var alongDistance = Vector3.Dot(relative, oldDirection);
            var perpendicular = relative - oldDirection * alongDistance;
            var normalizedAlong = alongDistance / oldLength;
            positions[i] = newStart + newDirection * (normalizedAlong * newLength) + rotation * perpendicular;
        }

        positions[startIndex] = newStart;
        positions[endIndex] = newLineEnd;
        SetWorldLinePositions(positions);
        return true;
    }

    public bool IsAttachedTo(PlaceableAsset placeable)
    {
        return placeable != null && _attachedPlaceable == placeable;
    }

    public bool TryFindAttachmentCandidate(
        out PlaceableAsset candidate,
        out Vector3 junctionPoint,
        out PhysicsDrawingEndpoint linearEndpoint)
    {
        candidate = null;
        junctionPoint = default;
        linearEndpoint = PhysicsDrawingEndpoint.Start;

        if (!CanAttachToPlaceable)
        {
            return false;
        }

        var placeables = FindObjectsByType<PlaceableAsset>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        var bestDistance = float.MaxValue;

        for (var i = 0; i < placeables.Length; i++)
        {
            var placeable = placeables[i];
            if (placeable == null
                || !placeable.isActiveAndEnabled
                || !TryResolveAttachmentProbeForPlaceable(
                    placeable,
                    out var probePoint,
                    out var probeEndpoint,
                    out var distance))
            {
                continue;
            }

            if (distance >= bestDistance)
            {
                continue;
            }

            bestDistance = distance;
            candidate = placeable;
            junctionPoint = probePoint;
            linearEndpoint = probeEndpoint;
        }

        return candidate != null && bestDistance <= attachmentSnapDistance;
    }

    public void ShowAttachmentPreview(
        PlaceableAsset placeable,
        Vector3 junctionPoint)
    {
        if (placeable == null || !CanAttachToPlaceable)
        {
            HideAttachmentPreview();
            return;
        }

        _previewPlaceable = placeable;
        _previewJunctionPoint = junctionPoint;
        RefreshAttachmentVisual();
    }

    public void HideAttachmentPreview()
    {
        _previewPlaceable = null;
        if (_attachedPlaceable == null)
        {
            SetAttachmentVisualsVisible(false, false);
            return;
        }

        RefreshAttachmentVisual();
    }

    public bool AttachToPlaceable(PlaceableAsset placeable, PhysicsDrawingEndpoint linearEndpoint)
    {
        if (placeable == null || !CanAttachToPlaceable)
        {
            return false;
        }

        _previewPlaceable = null;
        if (physicsIntent != PhysicsIntentType.Hinge
            && TryResolveLinearAttachmentProbe(placeable, out var linearProbe))
        {
            linearEndpoint = linearProbe.Endpoint;
            SetEndpointWorldPosition(linearProbe.Endpoint, linearProbe.SnapPoint);
        }

        _attachedPlaceable = placeable;
        _attachedLinearEndpoint = linearEndpoint;
        CaptureAttachmentLocalGeometry();
        RefreshAttachmentVisual();
        return true;
    }

    public void DetachFromPlaceable()
    {
        _attachedPlaceable = null;
        _attachedLocalLinePositions = null;
        _previewPlaceable = null;
        SetAttachmentVisualsVisible(false, false);
    }

    public bool TryMoveAttachedGroupToGrabPosition(Vector3 targetPosition)
    {
        if (_attachedPlaceable == null)
        {
            return false;
        }

        var delta = targetPosition - GetGrabPosition();
        if (delta.sqrMagnitude <= 0.00000001f)
        {
            return true;
        }

        MoveAttachedPlaceableBy(delta);
        TranslateLine(delta);
        CaptureAttachmentLocalGeometry();
        RefreshAttachmentVisual();
        return true;
    }

    public bool TrySetAttachedGroupWorldLinePositions(IReadOnlyList<Vector3> worldPositions)
    {
        if (_attachedPlaceable == null || worldPositions == null || worldPositions.Count == 0)
        {
            return false;
        }

        var oldJunction = ResolveAttachmentJunctionWorldPosition(_attachedLinearEndpoint);
        _isApplyingAttachmentFollow = true;
        SetWorldLinePositions(worldPositions);
        _isApplyingAttachmentFollow = false;

        var newJunction = ResolveAttachmentJunctionWorldPosition(_attachedLinearEndpoint);
        var delta = newJunction - oldJunction;
        if (delta.sqrMagnitude > 0.00000001f)
        {
            MoveAttachedPlaceableBy(delta);
        }

        CaptureAttachmentLocalGeometry();
        RefreshAttachmentVisual();
        return true;
    }

    public bool TryGetEndpointHandle(
        PhysicsDrawingEndpoint endpoint,
        out PhysicsDrawingEndpointHandle handle)
    {
        RefreshEndpointHandles();
        handle = endpoint == PhysicsDrawingEndpoint.Start ? _startHandle : _endHandle;
        return handle != null && handle.isActiveAndEnabled;
    }

    public void Delete()
    {
        if (AssetSelectionManager.Instance != null
            && AssetSelectionManager.Instance.SelectedPhysicsDrawing == this)
        {
            AssetSelectionManager.Instance.ClearSelection();
        }

        if (_owner == null)
        {
            _owner = FindFirstObjectByType<LineDrawing>();
        }

        if (_owner != null)
        {
            _owner.DeleteLine(gameObject);
            return;
        }

        Destroy(gameObject);
    }

    public void RebuildColliders()
    {
        ResolveReferences();
        ClearColliders();
        RefreshSelectionAuraGeometry();
        RefreshEndpointHandles();

        if (_lineRenderer == null || _lineRenderer.positionCount < 2)
        {
            return;
        }

        for (var i = 0; i < _lineRenderer.positionCount - 1; i++)
        {
            var start = _lineRenderer.GetPosition(i);
            var end = _lineRenderer.GetPosition(i + 1);
            var segment = end - start;
            var length = segment.magnitude;
            if (length <= 0.0001f)
            {
                continue;
            }

            var colliderObject = new GameObject("DrawingPickSegment");
            colliderObject.transform.SetParent(transform, true);
            colliderObject.transform.position = (start + end) * 0.5f;
            colliderObject.transform.rotation = Quaternion.FromToRotation(Vector3.up, segment.normalized);
            colliderObject.layer = gameObject.layer;

            var capsule = colliderObject.AddComponent<CapsuleCollider>();
            capsule.isTrigger = true;
            capsule.direction = 1;
            capsule.radius = Mathf.Max(0.001f, colliderRadius);
            capsule.height = length + capsule.radius * 2f;
            _colliders.Add(colliderObject);
        }
    }

    private Vector3 GetWorldLinePosition(int index)
    {
        var position = _lineRenderer.GetPosition(index);
        return _lineRenderer.useWorldSpace ? position : transform.TransformPoint(position);
    }

    private static Vector3 CalculateWorldBoundsCenter(IReadOnlyList<Vector3> worldPositions)
    {
        if (worldPositions == null || worldPositions.Count == 0)
        {
            return Vector3.zero;
        }

        var bounds = new Bounds(worldPositions[0], Vector3.zero);
        for (var i = 1; i < worldPositions.Count; i++)
        {
            bounds.Encapsulate(worldPositions[i]);
        }

        return bounds.center;
    }

    private bool TryResolveAttachmentProbeForPlaceable(
        PlaceableAsset placeable,
        out Vector3 junctionPoint,
        out PhysicsDrawingEndpoint linearEndpoint,
        out float distance)
    {
        junctionPoint = default;
        linearEndpoint = PhysicsDrawingEndpoint.Start;
        distance = float.MaxValue;

        if (placeable == null || !CanAttachToPlaceable)
        {
            return false;
        }

        if (physicsIntent == PhysicsIntentType.Hinge)
        {
            junctionPoint = ResolveHingeAttachmentCenter();
            return TryGetDistanceToPlaceable(placeable, junctionPoint, out distance);
        }

        if (!TryResolveLinearAttachmentProbe(placeable, out var probe))
        {
            return false;
        }

        junctionPoint = probe.JunctionPoint;
        linearEndpoint = probe.Endpoint;
        distance = probe.CandidateDistance;
        return true;
    }

    private bool TryResolveLinearAttachmentProbe(
        PlaceableAsset placeable,
        out LinearAttachmentProbe probe)
    {
        probe = default;
        if (placeable == null || physicsIntent == PhysicsIntentType.Hinge)
        {
            return false;
        }

        var startPoint = ResolveLinearEndpointWorldPosition(PhysicsDrawingEndpoint.Start);
        var endPoint = ResolveLinearEndpointWorldPosition(PhysicsDrawingEndpoint.End);
        var hasStart = TryResolveLinearEndpointContact(
            placeable,
            PhysicsDrawingEndpoint.Start,
            startPoint,
            endPoint,
            out var startProbe);
        var hasEnd = TryResolveLinearEndpointContact(
            placeable,
            PhysicsDrawingEndpoint.End,
            endPoint,
            startPoint,
            out var endProbe);

        if (!hasStart && !hasEnd)
        {
            return false;
        }

        if (!hasStart)
        {
            probe = endProbe;
            return true;
        }

        if (!hasEnd)
        {
            probe = startProbe;
            return true;
        }

        probe = PickBetterLinearAttachmentProbe(startProbe, endProbe);
        return true;
    }

    private bool TryResolveLinearEndpointContact(
        PlaceableAsset placeable,
        PhysicsDrawingEndpoint endpoint,
        Vector3 endpointPoint,
        Vector3 oppositePoint,
        out LinearAttachmentProbe probe)
    {
        probe = default;
        if (placeable == null)
        {
            return false;
        }

        var colliders = placeable.GetComponentsInChildren<Collider>();
        var hasSurface = false;
        var best = default(LinearAttachmentProbe);
        var bestScore = float.MaxValue;

        for (var i = 0; i < colliders.Length; i++)
        {
            var collider = colliders[i];
            if (collider == null || !collider.enabled)
            {
                continue;
            }

            var closestPoint = collider.ClosestPoint(endpointPoint);
            var closestDistance = Vector3.Distance(endpointPoint, closestPoint);
            var endpointInsideOrOnSurface = closestDistance <= AttachmentContactEpsilon;
            if (endpointInsideOrOnSurface)
            {
                if (!TryResolveInsideSurfacePoint(
                        collider,
                        placeable,
                        endpointPoint,
                        oppositePoint,
                        out closestPoint,
                        out var insideNormal,
                        out var edgeDistance))
                {
                    insideNormal = ResolveFallbackSurfaceNormal(
                        collider,
                        placeable,
                        endpointPoint,
                        oppositePoint);
                    edgeDistance = 0f;
                }

                var candidate = BuildLinearAttachmentProbe(
                    endpoint,
                    closestPoint,
                    insideNormal,
                    0f,
                    edgeDistance,
                    true);
                var score = edgeDistance;
                if (!hasSurface || score < bestScore)
                {
                    hasSurface = true;
                    best = candidate;
                    bestScore = score;
                }

                continue;
            }

            if (closestDistance > linearAttachmentSurfaceTolerance)
            {
                continue;
            }

            var normal = endpointPoint - closestPoint;
            if (normal.sqrMagnitude <= 0.000001f)
            {
                normal = ResolveFallbackSurfaceNormal(collider, placeable, endpointPoint, oppositePoint);
            }
            else
            {
                normal.Normalize();
            }

            var outsideCandidate = BuildLinearAttachmentProbe(
                endpoint,
                closestPoint,
                normal,
                closestDistance,
                closestDistance,
                false);
            if (!hasSurface || closestDistance < bestScore)
            {
                hasSurface = true;
                best = outsideCandidate;
                bestScore = closestDistance;
            }
        }

        if (!hasSurface)
        {
            return false;
        }

        probe = best;
        return true;
    }

    private LinearAttachmentProbe BuildLinearAttachmentProbe(
        PhysicsDrawingEndpoint endpoint,
        Vector3 surfacePoint,
        Vector3 normal,
        float candidateDistance,
        float edgeDistance,
        bool endpointInside)
    {
        if (normal.sqrMagnitude <= 0.000001f)
        {
            normal = Vector3.up;
        }
        else
        {
            normal.Normalize();
        }

        return new LinearAttachmentProbe
        {
            Endpoint = endpoint,
            JunctionPoint = surfacePoint,
            SnapPoint = surfacePoint + normal * linearAttachmentSurfaceOffset,
            CandidateDistance = candidateDistance,
            EdgeDistance = edgeDistance,
            EndpointInside = endpointInside
        };
    }

    private static LinearAttachmentProbe PickBetterLinearAttachmentProbe(
        LinearAttachmentProbe first,
        LinearAttachmentProbe second)
    {
        if (first.EndpointInside != second.EndpointInside)
        {
            return first.EndpointInside ? first : second;
        }

        var firstScore = first.EndpointInside ? first.EdgeDistance : first.CandidateDistance;
        var secondScore = second.EndpointInside ? second.EdgeDistance : second.CandidateDistance;
        return firstScore <= secondScore ? first : second;
    }

    private bool TryResolveInsideSurfacePoint(
        Collider collider,
        PlaceableAsset placeable,
        Vector3 insidePoint,
        Vector3 oppositePoint,
        out Vector3 surfacePoint,
        out Vector3 normal,
        out float edgeDistance)
    {
        surfacePoint = insidePoint;
        normal = Vector3.up;
        edgeDistance = 0f;
        if (collider == null)
        {
            return false;
        }

        if (!IsPointInsideOrOnCollider(collider, oppositePoint)
            && TryRaycastSegment(collider, oppositePoint, insidePoint, out var lineHit))
        {
            surfacePoint = lineHit.point;
            normal = lineHit.normal.sqrMagnitude > 0.000001f
                ? lineHit.normal.normalized
                : ResolveFallbackSurfaceNormal(collider, placeable, surfacePoint, oppositePoint);
            edgeDistance = Vector3.Distance(insidePoint, surfacePoint);
            return true;
        }

        normal = ResolveFallbackSurfaceNormal(collider, placeable, insidePoint, oppositePoint);
        var rayDistance = Mathf.Max(
            collider.bounds.extents.magnitude + AttachmentRaycastPadding,
            Vector3.Distance(insidePoint, collider.bounds.center) + AttachmentRaycastPadding);
        var rayOrigin = insidePoint + normal * rayDistance;
        if (collider.Raycast(new Ray(rayOrigin, -normal), out var radialHit, rayDistance + AttachmentRaycastPadding))
        {
            surfacePoint = radialHit.point;
            normal = radialHit.normal.sqrMagnitude > 0.000001f
                ? radialHit.normal.normalized
                : normal;
            edgeDistance = Vector3.Distance(insidePoint, surfacePoint);
            return true;
        }

        if (TryResolveBoundsSurfacePoint(collider.bounds, insidePoint, out surfacePoint, out normal))
        {
            edgeDistance = Vector3.Distance(insidePoint, surfacePoint);
            return true;
        }

        return false;
    }

    private static bool TryRaycastSegment(Collider collider, Vector3 start, Vector3 end, out RaycastHit hit)
    {
        hit = default;
        if (collider == null)
        {
            return false;
        }

        var segment = end - start;
        var length = segment.magnitude;
        if (length <= 0.000001f)
        {
            return false;
        }

        return collider.Raycast(new Ray(start, segment / length), out hit, length + AttachmentContactEpsilon);
    }

    private static bool IsPointInsideOrOnCollider(Collider collider, Vector3 point)
    {
        if (collider == null)
        {
            return false;
        }

        return Vector3.Distance(collider.ClosestPoint(point), point) <= AttachmentContactEpsilon;
    }

    private static Vector3 ResolveFallbackSurfaceNormal(
        Collider collider,
        PlaceableAsset placeable,
        Vector3 point,
        Vector3 oppositePoint)
    {
        var center = collider != null
            ? collider.bounds.center
            : placeable != null
                ? ResolvePlaceableCenter(placeable)
                : Vector3.zero;
        var normal = point - center;
        if (normal.sqrMagnitude > 0.000001f)
        {
            return normal.normalized;
        }

        normal = point - oppositePoint;
        if (normal.sqrMagnitude > 0.000001f)
        {
            return normal.normalized;
        }

        if (collider != null
            && TryResolveBoundsSurfacePoint(collider.bounds, point, out _, out normal)
            && normal.sqrMagnitude > 0.000001f)
        {
            return normal.normalized;
        }

        return Vector3.up;
    }

    private static bool TryResolveBoundsSurfacePoint(
        Bounds bounds,
        Vector3 point,
        out Vector3 surfacePoint,
        out Vector3 normal)
    {
        surfacePoint = point;
        normal = Vector3.up;
        if (bounds.size.sqrMagnitude <= 0.000001f)
        {
            return false;
        }

        var min = bounds.min;
        var max = bounds.max;
        var bestDistance = Mathf.Abs(point.x - min.x);
        var axis = 0;
        var useMax = false;

        var distance = Mathf.Abs(max.x - point.x);
        if (distance < bestDistance)
        {
            bestDistance = distance;
            axis = 0;
            useMax = true;
        }

        distance = Mathf.Abs(point.y - min.y);
        if (distance < bestDistance)
        {
            bestDistance = distance;
            axis = 1;
            useMax = false;
        }

        distance = Mathf.Abs(max.y - point.y);
        if (distance < bestDistance)
        {
            bestDistance = distance;
            axis = 1;
            useMax = true;
        }

        distance = Mathf.Abs(point.z - min.z);
        if (distance < bestDistance)
        {
            bestDistance = distance;
            axis = 2;
            useMax = false;
        }

        distance = Mathf.Abs(max.z - point.z);
        if (distance < bestDistance)
        {
            axis = 2;
            useMax = true;
        }

        switch (axis)
        {
            case 0:
                surfacePoint.x = useMax ? max.x : min.x;
                normal = useMax ? Vector3.right : Vector3.left;
                break;
            case 1:
                surfacePoint.y = useMax ? max.y : min.y;
                normal = useMax ? Vector3.up : Vector3.down;
                break;
            default:
                surfacePoint.z = useMax ? max.z : min.z;
                normal = useMax ? Vector3.forward : Vector3.back;
                break;
        }

        return true;
    }

    private Vector3 ResolveAttachmentJunctionWorldPosition(PhysicsDrawingEndpoint linearEndpoint)
    {
        if (physicsIntent == PhysicsIntentType.Hinge)
        {
            return ResolveHingeAttachmentCenter();
        }

        return ResolveLinearEndpointWorldPosition(linearEndpoint);
    }

    private Vector3 ResolveLinearEndpointWorldPosition(PhysicsDrawingEndpoint endpoint)
    {
        var positions = GetWorldLinePositions();
        if (positions.Length == 0)
        {
            return transform.position;
        }

        if (endpoint == PhysicsDrawingEndpoint.Start)
        {
            return positions[0];
        }

        return physicsIntent == PhysicsIntentType.Impulse
            ? GetVisualEndpointWorldPosition(positions)
            : positions[positions.Length - 1];
    }

    private Vector3 ResolveHingeAttachmentCenter()
    {
        var positions = GetWorldLinePositions();
        if (positions.Length == 0)
        {
            return transform.position;
        }

        return CalculateWorldBoundsCenter(positions);
    }

    private void FollowAttachedPlaceable()
    {
        if (_attachedPlaceable == null)
        {
            if (_attachedLocalLinePositions != null)
            {
                DetachFromPlaceable();
            }

            return;
        }

        if (!CanAttachToPlaceable)
        {
            DetachFromPlaceable();
            return;
        }

        if (_attachedLocalLinePositions == null || _attachedLocalLinePositions.Length == 0)
        {
            CaptureAttachmentLocalGeometry();
            return;
        }

        var placeableTransform = _attachedPlaceable.transform;
        if (!HasAttachedPlaceableTransformChanged(placeableTransform))
        {
            return;
        }

        var positions = new Vector3[_attachedLocalLinePositions.Length];
        for (var i = 0; i < positions.Length; i++)
        {
            positions[i] = placeableTransform.TransformPoint(_attachedLocalLinePositions[i]);
        }

        _isApplyingAttachmentFollow = true;
        SetWorldLinePositions(positions);
        _isApplyingAttachmentFollow = false;
        CacheAttachedPlaceableTransform();
        RefreshAttachmentVisual();
    }

    private void CaptureAttachmentLocalGeometry()
    {
        if (_attachedPlaceable == null)
        {
            _attachedLocalLinePositions = null;
            return;
        }

        var placeableTransform = _attachedPlaceable.transform;
        var worldPositions = GetWorldLinePositions();
        _attachedLocalLinePositions = new Vector3[worldPositions.Length];
        for (var i = 0; i < worldPositions.Length; i++)
        {
            _attachedLocalLinePositions[i] = placeableTransform.InverseTransformPoint(worldPositions[i]);
        }

        CacheAttachedPlaceableTransform();
    }

    private bool HasAttachedPlaceableTransformChanged(Transform placeableTransform)
    {
        if (placeableTransform == null)
        {
            return false;
        }

        return (placeableTransform.position - _attachedLastPosition).sqrMagnitude > 0.00000001f
               || Quaternion.Angle(placeableTransform.rotation, _attachedLastRotation) > 0.01f
               || (placeableTransform.lossyScale - _attachedLastScale).sqrMagnitude > 0.00000001f;
    }

    private void CacheAttachedPlaceableTransform()
    {
        if (_attachedPlaceable == null)
        {
            return;
        }

        var placeableTransform = _attachedPlaceable.transform;
        _attachedLastPosition = placeableTransform.position;
        _attachedLastRotation = placeableTransform.rotation;
        _attachedLastScale = placeableTransform.lossyScale;
    }

    private void MoveAttachedPlaceableBy(Vector3 delta)
    {
        if (_attachedPlaceable == null || delta.sqrMagnitude <= 0.00000001f)
        {
            return;
        }

        var placeableTransform = _attachedPlaceable.transform;
        var targetPosition = placeableTransform.position + delta;
        var rb = _attachedPlaceable.Rigidbody;
        if (rb != null)
        {
            rb.position = targetPosition;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        placeableTransform.position = targetPosition;
    }

    private void RefreshAttachmentVisual()
    {
        var placeable = _previewPlaceable != null ? _previewPlaceable : _attachedPlaceable;
        if (placeable == null)
        {
            SetAttachmentVisualsVisible(false, false);
            return;
        }

        var isHinge = physicsIntent == PhysicsIntentType.Hinge;
        var junction = _previewPlaceable != null
            ? _previewJunctionPoint
            : ResolveAttachmentJunctionWorldPosition(_attachedLinearEndpoint);
        if (!isHinge && TryResolveLinearAttachmentProbe(placeable, out var linearProbe))
        {
            junction = linearProbe.JunctionPoint;
        }

        EnsureAttachmentVisuals();
        if (_attachmentSphere != null)
        {
            _attachmentSphere.transform.position = junction;
            _attachmentSphere.transform.rotation = Quaternion.identity;
            _attachmentSphere.transform.localScale = Vector3.one * attachmentIndicatorDiameter;
            _attachmentSphere.layer = gameObject.layer;
        }

        if (_attachmentLineRenderer != null)
        {
            _attachmentLineRenderer.gameObject.layer = gameObject.layer;
            _attachmentLineRenderer.widthMultiplier = hingeAttachmentLineWidth;
            _attachmentLineRenderer.SetPosition(0, junction);
            _attachmentLineRenderer.SetPosition(1, ResolvePlaceableCenter(placeable));
        }

        SetAttachmentVisualsVisible(true, isHinge);
    }

    private void EnsureAttachmentVisuals()
    {
        if (_attachmentSphere == null)
        {
            _attachmentSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _attachmentSphere.name = "PhysicsAttachmentJunction";
            _attachmentSphere.transform.SetParent(transform, true);
            _attachmentSphere.layer = gameObject.layer;

            var sphereCollider = _attachmentSphere.GetComponent<Collider>();
            if (sphereCollider != null)
            {
                Destroy(sphereCollider);
            }

            _attachmentSphereRenderer = _attachmentSphere.GetComponent<MeshRenderer>();
            if (_attachmentSphereRenderer != null)
            {
                _attachmentSphereRenderer.shadowCastingMode = ShadowCastingMode.Off;
                _attachmentSphereRenderer.receiveShadows = false;
                _attachmentSphereRenderer.sharedMaterial = GetAttachmentMaterial();
            }

            _attachmentSphere.SetActive(false);
        }

        if (_attachmentLineRenderer == null)
        {
            var lineObject = new GameObject("PhysicsAttachmentHingeLine");
            lineObject.transform.SetParent(transform, true);
            lineObject.layer = gameObject.layer;
            _attachmentLineRenderer = lineObject.AddComponent<LineRenderer>();
            _attachmentLineRenderer.positionCount = 2;
            _attachmentLineRenderer.useWorldSpace = true;
            _attachmentLineRenderer.alignment = LineAlignment.View;
            _attachmentLineRenderer.numCapVertices = 8;
            _attachmentLineRenderer.numCornerVertices = 2;
            _attachmentLineRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _attachmentLineRenderer.receiveShadows = false;
            _attachmentLineRenderer.sharedMaterial = GetAttachmentMaterial();
            _attachmentLineRenderer.startColor = attachmentIndicatorColor;
            _attachmentLineRenderer.endColor = attachmentIndicatorColor;
            _attachmentLineRenderer.enabled = false;
        }

        ApplyAttachmentMaterialColor();
    }

    private void SetAttachmentVisualsVisible(bool sphereVisible, bool lineVisible)
    {
        if (_attachmentSphere != null && _attachmentSphere.activeSelf != sphereVisible)
        {
            _attachmentSphere.SetActive(sphereVisible);
        }

        if (_attachmentLineRenderer != null && _attachmentLineRenderer.enabled != lineVisible)
        {
            _attachmentLineRenderer.enabled = lineVisible;
        }
    }

    private Material GetAttachmentMaterial()
    {
        if (_attachmentMaterial != null)
        {
            return _attachmentMaterial;
        }

        var shader = Shader.Find("Universal Render Pipeline/Unlit")
                     ?? Shader.Find("Sprites/Default")
                     ?? Shader.Find("Unlit/Color")
                     ?? Shader.Find("Standard");
        if (shader == null)
        {
            return null;
        }

        _attachmentMaterial = new Material(shader)
        {
            name = "PhysicsDrawingAttachmentIndicator",
            color = attachmentIndicatorColor,
            enableInstancing = true
        };
        ApplyAttachmentMaterialColor();
        return _attachmentMaterial;
    }

    private void ApplyAttachmentMaterialColor()
    {
        if (_attachmentMaterial == null)
        {
            return;
        }

        _attachmentMaterial.color = attachmentIndicatorColor;
        if (_attachmentMaterial.HasProperty("_BaseColor"))
        {
            _attachmentMaterial.SetColor("_BaseColor", attachmentIndicatorColor);
        }

        if (_attachmentMaterial.HasProperty("_Color"))
        {
            _attachmentMaterial.SetColor("_Color", attachmentIndicatorColor);
        }
    }

    private static bool TryGetDistanceToPlaceable(
        PlaceableAsset placeable,
        Vector3 point,
        out float distance)
    {
        distance = float.MaxValue;
        if (placeable == null)
        {
            return false;
        }

        var hasCandidate = false;
        var colliders = placeable.GetComponentsInChildren<Collider>();
        for (var i = 0; i < colliders.Length; i++)
        {
            var collider = colliders[i];
            if (collider == null || !collider.enabled)
            {
                continue;
            }

            var closest = collider.ClosestPoint(point);
            var candidateDistance = Vector3.Distance(point, closest);
            if (candidateDistance >= distance)
            {
                continue;
            }

            hasCandidate = true;
            distance = candidateDistance;
        }

        if (hasCandidate)
        {
            return true;
        }

        if (TryGetPlaceableWorldBounds(placeable, out var bounds))
        {
            distance = Vector3.Distance(point, bounds.ClosestPoint(point));
            return true;
        }

        distance = Vector3.Distance(point, placeable.transform.position);
        return true;
    }

    private static Vector3 ResolvePlaceableCenter(PlaceableAsset placeable)
    {
        if (placeable == null)
        {
            return Vector3.zero;
        }

        return TryGetPlaceableWorldBounds(placeable, out var bounds)
            ? bounds.center
            : placeable.transform.position;
    }

    private static bool TryGetPlaceableWorldBounds(PlaceableAsset placeable, out Bounds bounds)
    {
        bounds = default;
        if (placeable == null)
        {
            return false;
        }

        var renderers = placeable.GetRenderers();
        if (renderers == null || renderers.Length == 0)
        {
            return false;
        }

        var hasBounds = false;
        for (var i = 0; i < renderers.Length; i++)
        {
            var renderer = renderers[i];
            if (renderer == null)
            {
                continue;
            }

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

        return hasBounds;
    }

    private void RefreshGeometryAfterLineEdit()
    {
        if (_arrowTip != null && _lineRenderer != null)
        {
            _arrowTip.UpdateFromLine(_lineRenderer, ArrowConeLength, ArrowConeRadius);
        }

        RebuildColliders();
        RefreshEndpointHandles();
        if (_attachedPlaceable != null && !_isApplyingAttachmentFollow)
        {
            CaptureAttachmentLocalGeometry();
        }

        RefreshAttachmentVisual();
    }

    private void ResolveReferences()
    {
        if (_lineRenderer == null)
        {
            _lineRenderer = GetComponent<LineRenderer>();
        }

        if (_arrowTip == null)
        {
            _arrowTip = GetComponent<LineArrowTip>();
        }
    }

    private void RefreshEndpointHandles()
    {
        ResolveReferences();
        if (!SupportsEndpointEditing || _lineRenderer == null || _lineRenderer.positionCount < 2)
        {
            SetEndpointHandleActive(_startHandle, false);
            SetEndpointHandleActive(_endHandle, false);
            return;
        }

        EnsureEndpointHandle(ref _startHandle, PhysicsDrawingEndpoint.Start, "StartEndpointHandle");
        EnsureEndpointHandle(ref _endHandle, PhysicsDrawingEndpoint.End, "EndEndpointHandle");

        ConfigureEndpointHandle(_startHandle, PhysicsDrawingEndpoint.Start);
        ConfigureEndpointHandle(_endHandle, PhysicsDrawingEndpoint.End);

        _startHandle.SetWorldPosition(GetWorldLinePosition(0));
        _endHandle.SetWorldPosition(GetEndHandleWorldPosition());
        SetEndpointHandleActive(_startHandle, true);
        SetEndpointHandleActive(_endHandle, true);
    }

    private Vector3 GetEndHandleWorldPosition()
    {
        ResolveReferences();
        if (_lineRenderer == null || _lineRenderer.positionCount == 0)
        {
            return transform.position;
        }

        var positions = GetWorldLinePositions();
        return GetVisualEndpointWorldPosition(positions);
    }

    private Vector3 GetVisualEndpointWorldPosition(IReadOnlyList<Vector3> worldPositions)
    {
        if (worldPositions == null || worldPositions.Count == 0)
        {
            return transform.position;
        }

        var lineEnd = worldPositions[worldPositions.Count - 1];
        if (physicsIntent != PhysicsIntentType.Impulse || worldPositions.Count < 2)
        {
            return lineEnd;
        }

        var previous = worldPositions[worldPositions.Count - 2];
        var direction = lineEnd - previous;
        if (direction.sqrMagnitude <= 0.000001f)
        {
            return lineEnd;
        }

        return lineEnd + direction.normalized * ArrowConeLength;
    }

    private Vector3 GetLineEndFromVisualEndpoint(Vector3 visualStart, Vector3 visualEnd)
    {
        if (physicsIntent != PhysicsIntentType.Impulse)
        {
            return visualEnd;
        }

        var direction = visualEnd - visualStart;
        var distance = direction.magnitude;
        if (distance <= ArrowConeLength + 0.0001f)
        {
            return visualStart;
        }

        return visualEnd - direction / distance * ArrowConeLength;
    }

    private void EnsureEndpointHandle(
        ref PhysicsDrawingEndpointHandle handle,
        PhysicsDrawingEndpoint endpoint,
        string handleName)
    {
        if (handle != null)
        {
            return;
        }

        var handleObject = new GameObject(handleName);
        handleObject.transform.SetParent(transform, true);
        handleObject.layer = gameObject.layer;
        handle = handleObject.AddComponent<PhysicsDrawingEndpointHandle>();
        ConfigureEndpointHandle(handle, endpoint);
    }

    private void ConfigureEndpointHandle(PhysicsDrawingEndpointHandle handle, PhysicsDrawingEndpoint endpoint)
    {
        if (handle == null)
        {
            return;
        }

        handle.gameObject.layer = gameObject.layer;
        handle.Configure(
            this,
            endpoint,
            endpointHandleDiameter,
            endpointHandleColliderRadius,
            endpointHandleHoverColor,
            endpointHandleDragColor);
    }

    private static void SetEndpointHandleActive(PhysicsDrawingEndpointHandle handle, bool active)
    {
        if (handle != null && handle.gameObject.activeSelf != active)
        {
            handle.gameObject.SetActive(active);
        }
    }

    private void CacheBaseColor()
    {
        ResolveReferences();
        if (_lineRenderer != null && _lineRenderer.material != null)
        {
            _baseColor = _lineRenderer.material.color;
        }
    }

    private void ApplyHighlightState()
    {
        ResolveReferences();

        if (_lineRenderer != null && _lineRenderer.material != null)
        {
            _lineRenderer.material.color = _baseColor;
        }

        if (_arrowTip != null)
        {
            _arrowTip.SetColor(_baseColor);
        }
    }

    private void RefreshSpringColor()
    {
        if (physicsIntent != PhysicsIntentType.Spring)
        {
            return;
        }

        _baseColor = EvaluateSettingColor(springStiffness);
        ApplyHighlightState();
    }

    private void RefreshHingeColor()
    {
        if (physicsIntent != PhysicsIntentType.Hinge)
        {
            return;
        }

        _baseColor = EvaluateSettingColor(hingeTorque);
        ApplyHighlightState();
    }

    private void RefreshImpulseColor()
    {
        if (physicsIntent != PhysicsIntentType.Impulse)
        {
            return;
        }

        _baseColor = EvaluateSettingColor(impulseForce);
        ApplyHighlightState();
    }

    private void RefreshPhysicsColor()
    {
        RefreshSpringColor();
        RefreshHingeColor();
        RefreshImpulseColor();
    }

    private Color EvaluateSettingColor(float value)
    {
        value = Mathf.Clamp01(value);
        if (value <= 0.5f)
        {
            return Color.Lerp(springZeroStiffnessColor, springMidStiffnessColor, value * 2f);
        }

        return Color.Lerp(springMidStiffnessColor, springFullStiffnessColor, (value - 0.5f) * 2f);
    }

    private void SetSelectionAuraVisible(bool visible)
    {
        if (!visible)
        {
            if (_selectionAuraRenderer != null)
            {
                _selectionAuraRenderer.gameObject.SetActive(false);
            }

            SetArrowTipAuraVisible(false);
            return;
        }

        EnsureSelectionAura();
        RefreshSelectionAuraGeometry();
        if (_selectionAuraRenderer != null && _selectionAuraRenderer.sharedMaterial != null)
        {
            _selectionAuraRenderer.gameObject.SetActive(true);
        }

        SetArrowTipAuraVisible(true);
    }

    private void EnsureSelectionAura()
    {
        if (_selectionAuraRenderer != null)
        {
            return;
        }

        var auraObject = new GameObject("SelectionAura");
        auraObject.transform.SetParent(transform, false);
        auraObject.layer = gameObject.layer;
        _selectionAuraRenderer = auraObject.AddComponent<LineRenderer>();
        _selectionAuraRenderer.shadowCastingMode = ShadowCastingMode.Off;
        _selectionAuraRenderer.receiveShadows = false;
        var material = GetSelectionAuraMaterial();
        if (material != null)
        {
            _selectionAuraRenderer.sharedMaterial = material;
        }

        _selectionAuraRenderer.gameObject.SetActive(false);
    }

    private Material GetSelectionAuraMaterial()
    {
        if (_selectionAuraMaterial != null)
        {
            return _selectionAuraMaterial;
        }

        var shader = Shader.Find("MRBlueprint/PhysicsDrawingAuraMaxBlend")
            ?? Shader.Find("Sprites/Default")
            ?? Shader.Find("Universal Render Pipeline/Unlit")
            ?? Shader.Find("Unlit/Color")
            ?? Shader.Find("Standard")
            ?? (_lineRenderer != null && _lineRenderer.material != null ? _lineRenderer.material.shader : null);
        if (shader == null)
        {
            return null;
        }

        _selectionAuraMaterial = new Material(shader);
        _selectionAuraMaterial.name = "PhysicsDrawingSelectionAura";
        SetAuraMaterialColor(_selectionAuraMaterial, selectionAuraColor);
        _selectionAuraMaterial.renderQueue = 1990;
        return _selectionAuraMaterial;
    }

    private void RefreshSelectionAuraGeometry()
    {
        if ((!_isSelected && !_isHovered) || _lineRenderer == null || _selectionAuraRenderer == null)
        {
            return;
        }

        _selectionAuraRenderer.positionCount = _lineRenderer.positionCount;
        for (var i = 0; i < _lineRenderer.positionCount; i++)
        {
            _selectionAuraRenderer.SetPosition(i, _lineRenderer.GetPosition(i));
        }

        _selectionAuraRenderer.useWorldSpace = _lineRenderer.useWorldSpace;
        _selectionAuraRenderer.loop = _lineRenderer.loop;
        _selectionAuraRenderer.alignment = _lineRenderer.alignment;
        _selectionAuraRenderer.textureMode = _lineRenderer.textureMode;
        _selectionAuraRenderer.numCapVertices = Mathf.Max(_lineRenderer.numCapVertices, 8);
        _selectionAuraRenderer.numCornerVertices = Mathf.Max(_lineRenderer.numCornerVertices, 8);
        _selectionAuraRenderer.sortingLayerID = _lineRenderer.sortingLayerID;
        _selectionAuraRenderer.sortingOrder = _lineRenderer.sortingOrder - 1;
        TrimSelectionAuraBeforeArrowTip();
        _selectionAuraRenderer.widthCurve = ScaleWidthCurve(_lineRenderer.widthCurve, selectionAuraWidthMultiplier);
        _selectionAuraRenderer.startWidth = _lineRenderer.startWidth * selectionAuraWidthMultiplier;
        _selectionAuraRenderer.endWidth = _lineRenderer.endWidth * selectionAuraWidthMultiplier;

        var material = GetSelectionAuraMaterial();
        if (material == null)
        {
            return;
        }

        SetAuraMaterialColor(material, selectionAuraColor);
        _selectionAuraRenderer.sharedMaterial = material;
        SetArrowTipAuraVisible(true);
    }

    private static void SetAuraMaterialColor(Material material, Color color)
    {
        if (material == null)
        {
            return;
        }

        material.color = color;
        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }
    }

    private void SetArrowTipAuraVisible(bool visible)
    {
        ResolveReferences();
        if (_arrowTip == null)
        {
            return;
        }

        var material = visible ? GetSelectionAuraMaterial() : null;
        _arrowTip.SetAuraVisible(
            visible,
            material,
            selectionAuraColor,
            selectionAuraConeScaleMultiplier,
            selectionAuraConeBaseOverlapFraction);
    }

    private void TrimSelectionAuraBeforeArrowTip()
    {
        if (_arrowTip == null
            || _selectionAuraRenderer == null
            || _selectionAuraRenderer.positionCount < 2
            || !_arrowTip.TryGetAuraBasePosition(selectionAuraConeBaseOverlapFraction, out var auraBasePosition))
        {
            return;
        }

        var endIndex = _selectionAuraRenderer.positionCount - 1;
        var previousPosition = _selectionAuraRenderer.GetPosition(endIndex - 1);
        var endPosition = _selectionAuraRenderer.GetPosition(endIndex);
        var segment = endPosition - previousPosition;
        var segmentLength = segment.magnitude;
        if (segmentLength <= 0.0001f)
        {
            return;
        }

        var direction = segment / segmentLength;
        var distanceToAuraBase = Vector3.Dot(auraBasePosition - previousPosition, direction);
        distanceToAuraBase = Mathf.Clamp(distanceToAuraBase, 0f, segmentLength);
        _selectionAuraRenderer.SetPosition(endIndex, previousPosition + direction * distanceToAuraBase);
    }

    private AnimationCurve ScaleWidthCurve(AnimationCurve source, float multiplier)
    {
        if (source == null || source.length == 0)
        {
            var fallback = new AnimationCurve();
            fallback.AddKey(0f, Mathf.Max(_lineRenderer.startWidth, 0.001f) * multiplier);
            fallback.AddKey(1f, Mathf.Max(_lineRenderer.endWidth, 0.001f) * multiplier);
            return fallback;
        }

        var keys = source.keys;
        for (var i = 0; i < keys.Length; i++)
        {
            keys[i].value *= multiplier;
            keys[i].inTangent *= multiplier;
            keys[i].outTangent *= multiplier;
        }

        return new AnimationCurve(keys);
    }

    private void ClearColliders()
    {
        for (var i = 0; i < _colliders.Count; i++)
        {
            if (_colliders[i] != null)
            {
                Destroy(_colliders[i]);
            }
        }

        _colliders.Clear();
    }

    private struct LinearAttachmentProbe
    {
        public PhysicsDrawingEndpoint Endpoint;
        public Vector3 JunctionPoint;
        public Vector3 SnapPoint;
        public float CandidateDistance;
        public float EdgeDistance;
        public bool EndpointInside;
    }

    private static string ResolveDisplayName(PhysicsGestureReadoutResult readout)
    {
        if (readout.PhysicsIntent != PhysicsIntentType.Unknown)
        {
            return readout.PhysicsIntent.ToString();
        }

        return string.IsNullOrEmpty(readout.ShapeName) ? "Drawing" : readout.ShapeName;
    }
}
