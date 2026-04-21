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
    [SerializeField, Range(0.05f, 1f)] private float attachedHingeAlpha = 0.38f;
    [SerializeField] private Color attachmentIndicatorColor = Color.white;
    [SerializeField] private Color attachmentIndicatorOutlineColor = Color.black;

    private readonly List<GameObject> _colliders = new();
    private LineRenderer _lineRenderer;
    private LineRenderer _selectionAuraRenderer;
    private LineRenderer _attachmentLineRenderer;
    private LineArrowTip _arrowTip;
    private PhysicsDrawingEndpointHandle _startHandle;
    private PhysicsDrawingEndpointHandle _endHandle;
    private Material _selectionAuraMaterial;
    private Material _attachmentMaterial;
    private Material _attachmentOutlineMaterial;
    private GameObject _attachmentSphere;
    private GameObject _attachmentOutlineSphere;
    private GameObject _secondaryAttachmentSphere;
    private GameObject _secondaryAttachmentOutlineSphere;
    private MeshRenderer _attachmentSphereRenderer;
    private MeshRenderer _attachmentOutlineSphereRenderer;
    private MeshRenderer _secondaryAttachmentSphereRenderer;
    private MeshRenderer _secondaryAttachmentOutlineSphereRenderer;
    private LineDrawing _owner;
    private PlaceableAsset _attachedPlaceable;
    private PlaceableAsset _attachedStartPlaceable;
    private PlaceableAsset _attachedEndPlaceable;
    private PlaceableAsset _previewPlaceable;
    private Vector3[] _attachedLocalLinePositions;
    private Vector3 _attachedLocalJunction;
    private Quaternion _attachedLocalFrame = Quaternion.identity;
    private Vector3 _attachedStartLocalPoint;
    private Vector3 _attachedEndLocalPoint;
    private Vector3 _previewJunctionPoint;
    private Vector3 _attachedLastPosition;
    private Vector3 _attachedLastScale;
    private Quaternion _attachedLastRotation;
    private Vector3 _attachedStartLastPosition;
    private Vector3 _attachedStartLastScale;
    private Quaternion _attachedStartLastRotation;
    private Vector3 _attachedEndLastPosition;
    private Vector3 _attachedEndLastScale;
    private Quaternion _attachedEndLastRotation;
    private PhysicsDrawingEndpoint _attachedLinearEndpoint = PhysicsDrawingEndpoint.Start;
    private Color _baseColor = Color.white;
    private bool _isHovered;
    private bool _isSelected;
    private bool _isApplyingAttachmentFollow;
    private bool _hasAttachedLocalPose;

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
    public PlaceableAsset AttachedPlaceable => _attachedPlaceable != null
        ? _attachedPlaceable
        : _attachedStartPlaceable != null
            ? _attachedStartPlaceable
            : _attachedEndPlaceable;
    public bool IsAttachedToPlaceable =>
        _attachedPlaceable != null
        || _attachedStartPlaceable != null
        || _attachedEndPlaceable != null;
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
        attachedHingeAlpha = Mathf.Clamp(attachedHingeAlpha, 0.05f, 1f);
    }

    private void LateUpdate()
    {
        if (physicsIntent == PhysicsIntentType.Hinge && IsSandboxSimulationActive())
        {
            RefreshAttachmentVisual();
            return;
        }

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

        if (_attachmentOutlineMaterial != null)
        {
            Destroy(_attachmentOutlineMaterial);
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
        return placeable != null
               && (_attachedPlaceable == placeable
                   || _attachedStartPlaceable == placeable
                   || _attachedEndPlaceable == placeable);
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
        if (!IsAttachedToPlaceable)
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
        if (physicsIntent == PhysicsIntentType.Spring)
        {
            if (!TryResolveLinearAttachmentProbeForEndpoint(placeable, linearEndpoint, out var endpointProbe))
            {
                return false;
            }

            return AttachSpringEndpoint(placeable, endpointProbe);
        }

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
        ApplyHighlightState();
        return true;
    }

    public bool TryAttachToPlaceablesOnRelease()
    {
        if (!CanAttachToPlaceable)
        {
            return false;
        }

        if (physicsIntent != PhysicsIntentType.Spring)
        {
            if (!TryFindAttachmentCandidate(out var placeable, out _, out var endpoint))
            {
                HideAttachmentPreview();
                return false;
            }

            return AttachToPlaceable(placeable, endpoint);
        }

        var attachedAny = false;
        if (TryFindLinearEndpointAttachmentCandidate(
                PhysicsDrawingEndpoint.Start,
                null,
                out var startPlaceable,
                out var startProbe)
            && startProbe.CandidateDistance <= attachmentSnapDistance)
        {
            attachedAny |= AttachSpringEndpoint(startPlaceable, startProbe);
        }

        if (TryFindLinearEndpointAttachmentCandidate(
                PhysicsDrawingEndpoint.End,
                _attachedStartPlaceable,
                out var endPlaceable,
                out var endProbe)
            && endProbe.CandidateDistance <= attachmentSnapDistance)
        {
            attachedAny |= AttachSpringEndpoint(endPlaceable, endProbe);
        }

        if (!attachedAny)
        {
            HideAttachmentPreview();
        }

        return attachedAny;
    }

    public bool TryAttachEndpointToPlaceableOnRelease(PhysicsDrawingEndpoint endpoint)
    {
        if (physicsIntent == PhysicsIntentType.Impulse)
        {
            if (!TryFindLinearEndpointAttachmentCandidate(
                    endpoint,
                    null,
                    out var impulsePlaceable,
                    out var impulseProbe)
                || impulseProbe.CandidateDistance > attachmentSnapDistance)
            {
                RefreshAttachmentVisual();
                return false;
            }

            return AttachLinearEndpoint(impulsePlaceable, impulseProbe);
        }

        if (physicsIntent != PhysicsIntentType.Spring)
        {
            return false;
        }

        var excludedPlaceable = endpoint == PhysicsDrawingEndpoint.Start
            ? _attachedEndPlaceable
            : _attachedStartPlaceable;
        if (!TryFindLinearEndpointAttachmentCandidate(
                endpoint,
                excludedPlaceable,
                out var placeable,
                out var probe)
            || probe.CandidateDistance > attachmentSnapDistance)
        {
            RefreshAttachmentVisual();
            return false;
        }

        return AttachSpringEndpoint(placeable, probe);
    }

    public void DetachSpringEndpointForDrag(PhysicsDrawingEndpoint endpoint)
    {
        if (physicsIntent == PhysicsIntentType.Impulse)
        {
            if (_attachedPlaceable != null && _attachedLinearEndpoint == endpoint)
            {
                DetachFromPlaceable();
            }

            return;
        }

        if (physicsIntent != PhysicsIntentType.Spring)
        {
            return;
        }

        var changed = false;
        if (endpoint == PhysicsDrawingEndpoint.Start && _attachedStartPlaceable != null)
        {
            _attachedStartPlaceable = null;
            changed = true;
        }
        else if (endpoint == PhysicsDrawingEndpoint.End && _attachedEndPlaceable != null)
        {
            _attachedEndPlaceable = null;
            changed = true;
        }

        if (!changed)
        {
            return;
        }

        CaptureAttachmentLocalGeometry();
        RefreshAttachmentVisual();
    }

    public void DetachFromPlaceable()
    {
        _attachedPlaceable = null;
        _attachedStartPlaceable = null;
        _attachedEndPlaceable = null;
        _attachedLocalLinePositions = null;
        _hasAttachedLocalPose = false;
        _previewPlaceable = null;
        SetAttachmentVisualsVisible(false, false);
        ApplyHighlightState();
    }

    private bool AttachSpringEndpoint(PlaceableAsset placeable, LinearAttachmentProbe probe)
    {
        if (placeable == null || physicsIntent != PhysicsIntentType.Spring)
        {
            return false;
        }

        _attachedPlaceable = null;
        _attachedLocalLinePositions = null;
        _hasAttachedLocalPose = false;
        SetEndpointWorldPosition(probe.Endpoint, probe.SnapPoint);
        var attachedPoint = ResolveLinearEndpointWorldPosition(probe.Endpoint);
        if (probe.Endpoint == PhysicsDrawingEndpoint.Start)
        {
            _attachedStartPlaceable = placeable;
            _attachedStartLocalPoint = placeable.transform.InverseTransformPoint(attachedPoint);
            CacheSpringEndpointTransform(PhysicsDrawingEndpoint.Start);
        }
        else
        {
            _attachedEndPlaceable = placeable;
            _attachedEndLocalPoint = placeable.transform.InverseTransformPoint(attachedPoint);
            CacheSpringEndpointTransform(PhysicsDrawingEndpoint.End);
        }

        _previewPlaceable = null;
        CaptureSpringEndpointAttachmentGeometry();
        RefreshAttachmentVisual();
        return true;
    }

    private bool AttachLinearEndpoint(PlaceableAsset placeable, LinearAttachmentProbe probe)
    {
        if (placeable == null || physicsIntent == PhysicsIntentType.Hinge)
        {
            return false;
        }

        _attachedStartPlaceable = null;
        _attachedEndPlaceable = null;
        _attachedPlaceable = placeable;
        _attachedLinearEndpoint = probe.Endpoint;
        _previewPlaceable = null;

        SetEndpointWorldPosition(probe.Endpoint, probe.SnapPoint);
        CaptureAttachmentLocalGeometry();
        RefreshAttachmentVisual();
        ApplyHighlightState();
        return true;
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
        var hasLocalPose = _hasAttachedLocalPose;

        _isApplyingAttachmentFollow = true;
        SetWorldLinePositions(worldPositions);
        _isApplyingAttachmentFollow = false;

        var newJunction = ResolveAttachmentJunctionWorldPosition(_attachedLinearEndpoint);
        if (hasLocalPose
            && TryResolveAttachmentFollowPose(
                GetWorldLinePositions(),
                _attachedLinearEndpoint,
                out var worldJunction,
                out var worldFrame))
        {
            var targetRotation = worldFrame * Quaternion.Inverse(_attachedLocalFrame);
            SetAttachedPlaceablePose(
                worldJunction - targetRotation * _attachedLocalJunction,
                targetRotation);
        }
        else
        {
            var delta = newJunction - oldJunction;
            if (delta.sqrMagnitude > 0.00000001f)
            {
                MoveAttachedPlaceableBy(delta);
            }
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

    public bool TryGetSpringEndpointAttachment(
        PhysicsDrawingEndpoint endpoint,
        out PlaceableAsset placeable,
        out Vector3 worldPoint)
    {
        placeable = null;
        worldPoint = default;
        if (physicsIntent != PhysicsIntentType.Spring)
        {
            return false;
        }

        placeable = endpoint == PhysicsDrawingEndpoint.Start
            ? _attachedStartPlaceable
            : _attachedEndPlaceable;
        if (placeable == null)
        {
            return false;
        }

        worldPoint = endpoint == PhysicsDrawingEndpoint.Start
            ? placeable.transform.TransformPoint(_attachedStartLocalPoint)
            : placeable.transform.TransformPoint(_attachedEndLocalPoint);
        return true;
    }

    public bool TryGetEndpointAttachment(
        PhysicsDrawingEndpoint endpoint,
        out PlaceableAsset placeable,
        out Vector3 worldPoint)
    {
        if (TryGetSpringEndpointAttachment(endpoint, out placeable, out worldPoint))
        {
            return true;
        }

        placeable = null;
        worldPoint = default;
        if (physicsIntent != PhysicsIntentType.Impulse
            || _attachedPlaceable == null
            || _attachedLinearEndpoint != endpoint)
        {
            return false;
        }

        placeable = _attachedPlaceable;
        worldPoint = ResolveLinearEndpointWorldPosition(endpoint);
        return true;
    }

    public bool TryGetImpulseAttachment(
        out PlaceableAsset placeable,
        out Vector3 junction,
        out Vector3 direction)
    {
        placeable = _attachedPlaceable;
        junction = default;
        direction = default;
        if (physicsIntent != PhysicsIntentType.Impulse
            || placeable == null
            || !TryResolveImpulseDirection(out direction))
        {
            return false;
        }

        junction = ResolveLinearEndpointWorldPosition(_attachedLinearEndpoint);
        if (TryResolveLinearAttachmentProbeForEndpoint(
                placeable,
                _attachedLinearEndpoint,
                out var probe))
        {
            junction = probe.JunctionPoint;
        }

        return true;
    }

    public bool TryGetHingeAttachment(
        out PlaceableAsset placeable,
        out Vector3 pivot,
        out Vector3 bodyPoint,
        out float stringLength)
    {
        placeable = _attachedPlaceable;
        pivot = ResolveHingeAttachmentCenter();
        bodyPoint = default;
        stringLength = 0f;
        if (physicsIntent != PhysicsIntentType.Hinge || placeable == null)
        {
            return false;
        }

        bodyPoint = ResolveHingeAttachmentBodyPoint(placeable);
        stringLength = Mathf.Max(0.01f, Vector3.Distance(pivot, bodyPoint));
        return true;
    }

    public void RefreshAttachmentVisualState()
    {
        RefreshAttachmentVisual();
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
            return TryResolveHingeRingAttachmentPoint(placeable, out _, out distance);
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

    private bool TryFindLinearEndpointAttachmentCandidate(
        PhysicsDrawingEndpoint endpoint,
        PlaceableAsset excludedPlaceable,
        out PlaceableAsset candidate,
        out LinearAttachmentProbe probe)
    {
        candidate = null;
        probe = default;
        if (physicsIntent == PhysicsIntentType.Hinge)
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
                || placeable == excludedPlaceable
                || !placeable.isActiveAndEnabled
                || !TryResolveLinearAttachmentProbeForEndpoint(placeable, endpoint, out var endpointProbe))
            {
                continue;
            }

            if (endpointProbe.CandidateDistance >= bestDistance)
            {
                continue;
            }

            bestDistance = endpointProbe.CandidateDistance;
            candidate = placeable;
            probe = endpointProbe;
        }

        return candidate != null;
    }

    private bool TryResolveLinearAttachmentProbeForEndpoint(
        PlaceableAsset placeable,
        PhysicsDrawingEndpoint endpoint,
        out LinearAttachmentProbe probe)
    {
        probe = default;
        if (placeable == null || physicsIntent == PhysicsIntentType.Hinge)
        {
            return false;
        }

        var startPoint = ResolveLinearEndpointWorldPosition(PhysicsDrawingEndpoint.Start);
        var endPoint = ResolveLinearEndpointWorldPosition(PhysicsDrawingEndpoint.End);
        return endpoint == PhysicsDrawingEndpoint.Start
            ? TryResolveLinearEndpointContact(
                placeable,
                endpoint,
                startPoint,
                endPoint,
                out probe)
            : TryResolveLinearEndpointContact(
                placeable,
                endpoint,
                endPoint,
                startPoint,
                out probe);
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

    private bool TryResolveAttachmentFollowPose(
        IReadOnlyList<Vector3> positions,
        PhysicsDrawingEndpoint linearEndpoint,
        out Vector3 junction,
        out Quaternion frame)
    {
        junction = Vector3.zero;
        frame = Quaternion.identity;
        if (positions == null || positions.Count < 2)
        {
            return false;
        }

        junction = ResolveAttachmentJunctionPosition(positions, linearEndpoint);
        return physicsIntent == PhysicsIntentType.Hinge
            ? TryResolveHingeAttachmentFollowFrame(positions, out frame)
            : TryResolveLinearAttachmentFollowFrame(positions, out frame);
    }

    private Vector3 ResolveAttachmentJunctionPosition(
        IReadOnlyList<Vector3> positions,
        PhysicsDrawingEndpoint linearEndpoint)
    {
        if (positions == null || positions.Count == 0)
        {
            return transform.position;
        }

        if (physicsIntent == PhysicsIntentType.Hinge)
        {
            return CalculateWorldBoundsCenter(positions);
        }

        if (linearEndpoint == PhysicsDrawingEndpoint.Start)
        {
            return positions[0];
        }

        return physicsIntent == PhysicsIntentType.Impulse
            ? GetVisualEndpointWorldPosition(positions)
            : positions[positions.Count - 1];
    }

    private bool TryResolveLinearAttachmentFollowFrame(
        IReadOnlyList<Vector3> worldPositions,
        out Quaternion frame)
    {
        frame = Quaternion.identity;
        if (worldPositions == null || worldPositions.Count < 2)
        {
            return false;
        }

        var start = worldPositions[0];
        var end = physicsIntent == PhysicsIntentType.Impulse
            ? GetVisualEndpointWorldPosition(worldPositions)
            : worldPositions[worldPositions.Count - 1];
        var forward = end - start;
        var upCandidate = ResolveLineUpCandidate(worldPositions, start, forward);
        return TryBuildAttachmentFrame(forward, upCandidate, out frame);
    }

    private static Vector3 ResolveLineUpCandidate(
        IReadOnlyList<Vector3> worldPositions,
        Vector3 origin,
        Vector3 forward)
    {
        if (worldPositions == null || forward.sqrMagnitude <= 0.000001f)
        {
            return Vector3.up;
        }

        var direction = forward.normalized;
        var best = Vector3.zero;
        var bestMagnitude = 0f;
        for (var i = 1; i < worldPositions.Count - 1; i++)
        {
            var relative = worldPositions[i] - origin;
            var perpendicular = relative - direction * Vector3.Dot(relative, direction);
            var magnitude = perpendicular.sqrMagnitude;
            if (magnitude <= bestMagnitude)
            {
                continue;
            }

            best = perpendicular;
            bestMagnitude = magnitude;
        }

        return bestMagnitude > 0.000001f ? best : Vector3.up;
    }

    private static bool TryResolveHingeAttachmentFollowFrame(
        IReadOnlyList<Vector3> worldPositions,
        out Quaternion frame)
    {
        frame = Quaternion.identity;
        if (worldPositions == null || worldPositions.Count < 3)
        {
            return false;
        }

        var center = CalculateWorldBoundsCenter(worldPositions);
        if (!TryResolveHingeNormal(worldPositions, center, out var normal))
        {
            return false;
        }

        var normalDirection = normal.normalized;
        var upCandidate = Vector3.zero;
        var bestMagnitude = 0f;
        for (var i = 0; i < worldPositions.Count; i++)
        {
            var relative = worldPositions[i] - center;
            var projected = relative - normalDirection * Vector3.Dot(relative, normalDirection);
            var magnitude = projected.sqrMagnitude;
            if (magnitude <= bestMagnitude)
            {
                continue;
            }

            upCandidate = projected;
            bestMagnitude = magnitude;
        }

        return TryBuildAttachmentFrame(normalDirection, upCandidate, out frame);
    }

    private static bool TryResolveHingeNormal(
        IReadOnlyList<Vector3> worldPositions,
        Vector3 center,
        out Vector3 normal)
    {
        normal = Vector3.zero;
        for (var i = 0; i < worldPositions.Count; i++)
        {
            var current = worldPositions[i] - center;
            var next = worldPositions[(i + 1) % worldPositions.Count] - center;
            normal += Vector3.Cross(current, next);
        }

        if (normal.sqrMagnitude > 0.000001f)
        {
            return true;
        }

        var origin = worldPositions[0];
        for (var i = 1; i < worldPositions.Count - 1; i++)
        {
            var candidate = Vector3.Cross(
                worldPositions[i] - origin,
                worldPositions[i + 1] - origin);
            if (candidate.sqrMagnitude <= normal.sqrMagnitude)
            {
                continue;
            }

            normal = candidate;
        }

        return normal.sqrMagnitude > 0.000001f;
    }

    private static bool TryBuildAttachmentFrame(
        Vector3 forward,
        Vector3 upCandidate,
        out Quaternion frame)
    {
        frame = Quaternion.identity;
        if (forward.sqrMagnitude <= 0.000001f)
        {
            return false;
        }

        var forwardDirection = forward.normalized;
        var up = upCandidate - forwardDirection * Vector3.Dot(upCandidate, forwardDirection);
        if (up.sqrMagnitude <= 0.000001f)
        {
            up = Vector3.up - forwardDirection * Vector3.Dot(Vector3.up, forwardDirection);
        }

        if (up.sqrMagnitude <= 0.000001f)
        {
            up = Vector3.right - forwardDirection * Vector3.Dot(Vector3.right, forwardDirection);
        }

        if (up.sqrMagnitude <= 0.000001f)
        {
            return false;
        }

        frame = Quaternion.LookRotation(forwardDirection, up.normalized);
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

    private bool TryResolveImpulseDirection(out Vector3 direction)
    {
        direction = Vector3.zero;
        var positions = GetWorldLinePositions();
        if (positions.Length < 2)
        {
            return false;
        }

        var start = positions[0];
        var end = GetVisualEndpointWorldPosition(positions);
        direction = end - start;
        if (direction.sqrMagnitude <= 0.000001f)
        {
            return false;
        }

        direction.Normalize();
        return true;
    }

    private Vector3 ResolveHingeAttachmentBodyPoint(PlaceableAsset placeable)
    {
        return TryResolveHingeRingAttachmentPoint(placeable, out var bodyPoint, out _)
            ? bodyPoint
            : ResolvePlaceableCenter(placeable);
    }

    private bool TryResolveHingeRingAttachmentPoint(
        PlaceableAsset placeable,
        out Vector3 bodyPoint,
        out float distance)
    {
        bodyPoint = default;
        distance = float.MaxValue;
        if (placeable == null || physicsIntent != PhysicsIntentType.Hinge)
        {
            return false;
        }

        var positions = GetWorldLinePositions();
        if (positions.Length < 2)
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

            if (TryResolveHingeRingAttachmentPointForCollider(
                    collider,
                    positions,
                    out var candidatePoint,
                    out var candidateDistance)
                && candidateDistance < distance)
            {
                hasCandidate = true;
                bodyPoint = candidatePoint;
                distance = candidateDistance;
            }
        }

        if (hasCandidate)
        {
            return true;
        }

        if (!TryGetPlaceableWorldBounds(placeable, out var bounds))
        {
            bodyPoint = ResolvePlaceableCenter(placeable);
            distance = GetDistanceFromPointToHingeRing(positions, bodyPoint);
            return true;
        }

        var boundsPoint = bounds.ClosestPoint(ResolveHingeAttachmentCenter());
        bodyPoint = boundsPoint;
        distance = GetDistanceFromPointToHingeRing(positions, boundsPoint);
        return true;
    }

    private bool TryResolveHingeRingAttachmentPointForCollider(
        Collider collider,
        IReadOnlyList<Vector3> ringPositions,
        out Vector3 bodyPoint,
        out float distance)
    {
        bodyPoint = default;
        distance = float.MaxValue;
        if (collider == null || ringPositions == null || ringPositions.Count < 2)
        {
            return false;
        }

        var hasCandidate = false;
        var segmentCount = GetHingeRingSegmentCount(ringPositions);
        for (var i = 0; i < segmentCount; i++)
        {
            var start = ringPositions[i];
            var end = ringPositions[(i + 1) % ringPositions.Count];
            if ((end - start).sqrMagnitude <= 0.00000001f)
            {
                continue;
            }

            var ringPoint = ClosestPointOnSegment(start, end, collider.bounds.center);
            var colliderPoint = collider.ClosestPoint(ringPoint);
            for (var iteration = 0; iteration < 2; iteration++)
            {
                ringPoint = ClosestPointOnSegment(start, end, colliderPoint);
                colliderPoint = collider.ClosestPoint(ringPoint);
            }

            var candidateDistance = Vector3.Distance(ringPoint, colliderPoint);
            if (candidateDistance >= distance)
            {
                continue;
            }

            hasCandidate = true;
            bodyPoint = colliderPoint;
            distance = candidateDistance;
        }

        return hasCandidate;
    }

    private float GetDistanceFromPointToHingeRing(IReadOnlyList<Vector3> ringPositions, Vector3 point)
    {
        if (ringPositions == null || ringPositions.Count < 2)
        {
            return float.MaxValue;
        }

        var best = float.MaxValue;
        var segmentCount = GetHingeRingSegmentCount(ringPositions);
        for (var i = 0; i < segmentCount; i++)
        {
            var start = ringPositions[i];
            var end = ringPositions[(i + 1) % ringPositions.Count];
            var closest = ClosestPointOnSegment(start, end, point);
            best = Mathf.Min(best, Vector3.Distance(point, closest));
        }

        return best;
    }

    private int GetHingeRingSegmentCount(IReadOnlyList<Vector3> ringPositions)
    {
        if (ringPositions == null)
        {
            return 0;
        }

        if (ringPositions.Count < 3)
        {
            return Mathf.Max(0, ringPositions.Count - 1);
        }

        return physicsIntent == PhysicsIntentType.Hinge
            ? ringPositions.Count
            : ringPositions.Count - 1;
    }

    private static Vector3 ClosestPointOnSegment(Vector3 start, Vector3 end, Vector3 point)
    {
        var segment = end - start;
        var lengthSq = segment.sqrMagnitude;
        if (lengthSq <= 0.00000001f)
        {
            return start;
        }

        var t = Vector3.Dot(point - start, segment) / lengthSq;
        return start + segment * Mathf.Clamp01(t);
    }

    private bool HasSpringEndpointAttachment()
    {
        return _attachedStartPlaceable != null || _attachedEndPlaceable != null;
    }

    private void FollowSpringEndpointAttachments()
    {
        var hasStart = _attachedStartPlaceable != null;
        var hasEnd = _attachedEndPlaceable != null;
        if (!hasStart && !hasEnd)
        {
            return;
        }

        if (hasStart != hasEnd)
        {
            FollowSingleSpringEndpointAttachment(hasStart
                ? PhysicsDrawingEndpoint.Start
                : PhysicsDrawingEndpoint.End);
            return;
        }

        var changed = false;
        if (hasStart)
        {
            if (!IsSpringEndpointAttachmentValid(_attachedStartPlaceable))
            {
                _attachedStartPlaceable = null;
                hasStart = false;
                changed = true;
            }
            else if (HasSpringEndpointTransformChanged(PhysicsDrawingEndpoint.Start))
            {
                changed = true;
            }
        }

        if (hasEnd)
        {
            if (!IsSpringEndpointAttachmentValid(_attachedEndPlaceable))
            {
                _attachedEndPlaceable = null;
                hasEnd = false;
                changed = true;
            }
            else if (HasSpringEndpointTransformChanged(PhysicsDrawingEndpoint.End))
            {
                changed = true;
            }
        }

        if (!hasStart && !hasEnd)
        {
            DetachFromPlaceable();
            return;
        }

        if (!changed)
        {
            return;
        }

        _isApplyingAttachmentFollow = true;
        if (hasStart)
        {
            SetEndpointWorldPosition(
                PhysicsDrawingEndpoint.Start,
                _attachedStartPlaceable.transform.TransformPoint(_attachedStartLocalPoint));
        }

        if (hasEnd)
        {
            SetEndpointWorldPosition(
                PhysicsDrawingEndpoint.End,
                _attachedEndPlaceable.transform.TransformPoint(_attachedEndLocalPoint));
        }

        _isApplyingAttachmentFollow = false;

        if (hasStart)
        {
            CacheSpringEndpointTransform(PhysicsDrawingEndpoint.Start);
        }

        if (hasEnd)
        {
            CacheSpringEndpointTransform(PhysicsDrawingEndpoint.End);
        }

        RefreshAttachmentVisual();
    }

    private void FollowSingleSpringEndpointAttachment(PhysicsDrawingEndpoint endpoint)
    {
        var placeable = endpoint == PhysicsDrawingEndpoint.Start
            ? _attachedStartPlaceable
            : _attachedEndPlaceable;
        if (!IsSpringEndpointAttachmentValid(placeable))
        {
            DetachFromPlaceable();
            return;
        }

        if (_attachedLocalLinePositions == null || _attachedLocalLinePositions.Length == 0)
        {
            CaptureSpringEndpointAttachmentGeometry();
            return;
        }

        if (!HasSpringEndpointTransformChanged(endpoint))
        {
            return;
        }

        var placeableTransform = placeable.transform;
        var positions = new Vector3[_attachedLocalLinePositions.Length];
        for (var i = 0; i < positions.Length; i++)
        {
            positions[i] = placeableTransform.TransformPoint(_attachedLocalLinePositions[i]);
        }

        _isApplyingAttachmentFollow = true;
        SetWorldLinePositions(positions);
        _isApplyingAttachmentFollow = false;
        CacheSpringEndpointTransform(endpoint);
        RefreshAttachmentVisual();
    }

    private static bool IsSpringEndpointAttachmentValid(PlaceableAsset placeable)
    {
        return placeable != null && placeable.isActiveAndEnabled;
    }

    private bool HasSpringEndpointTransformChanged(PhysicsDrawingEndpoint endpoint)
    {
        var placeable = endpoint == PhysicsDrawingEndpoint.Start
            ? _attachedStartPlaceable
            : _attachedEndPlaceable;
        if (placeable == null)
        {
            return false;
        }

        var placeableTransform = placeable.transform;
        var lastPosition = endpoint == PhysicsDrawingEndpoint.Start
            ? _attachedStartLastPosition
            : _attachedEndLastPosition;
        var lastRotation = endpoint == PhysicsDrawingEndpoint.Start
            ? _attachedStartLastRotation
            : _attachedEndLastRotation;
        var lastScale = endpoint == PhysicsDrawingEndpoint.Start
            ? _attachedStartLastScale
            : _attachedEndLastScale;

        return (placeableTransform.position - lastPosition).sqrMagnitude > 0.00000001f
               || Quaternion.Angle(placeableTransform.rotation, lastRotation) > 0.01f
               || (placeableTransform.lossyScale - lastScale).sqrMagnitude > 0.00000001f;
    }

    private void CacheSpringEndpointTransform(PhysicsDrawingEndpoint endpoint)
    {
        var placeable = endpoint == PhysicsDrawingEndpoint.Start
            ? _attachedStartPlaceable
            : _attachedEndPlaceable;
        if (placeable == null)
        {
            return;
        }

        var placeableTransform = placeable.transform;
        if (endpoint == PhysicsDrawingEndpoint.Start)
        {
            _attachedStartLastPosition = placeableTransform.position;
            _attachedStartLastRotation = placeableTransform.rotation;
            _attachedStartLastScale = placeableTransform.lossyScale;
        }
        else
        {
            _attachedEndLastPosition = placeableTransform.position;
            _attachedEndLastRotation = placeableTransform.rotation;
            _attachedEndLastScale = placeableTransform.lossyScale;
        }
    }

    private void FollowAttachedPlaceable()
    {
        if (physicsIntent == PhysicsIntentType.Hinge && IsSandboxSimulationActive())
        {
            return;
        }

        if (physicsIntent == PhysicsIntentType.Spring && HasSpringEndpointAttachment())
        {
            FollowSpringEndpointAttachments();
            return;
        }

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
        if (physicsIntent == PhysicsIntentType.Spring && HasSpringEndpointAttachment())
        {
            CaptureSpringEndpointAttachmentGeometry();
            return;
        }

        if (_attachedPlaceable == null)
        {
            _attachedLocalLinePositions = null;
            _hasAttachedLocalPose = false;
            return;
        }

        var placeableTransform = _attachedPlaceable.transform;
        var worldPositions = GetWorldLinePositions();
        _attachedLocalLinePositions = new Vector3[worldPositions.Length];
        for (var i = 0; i < worldPositions.Length; i++)
        {
            _attachedLocalLinePositions[i] = placeableTransform.InverseTransformPoint(worldPositions[i]);
        }

        if (TryResolveAttachmentFollowPose(
                worldPositions,
                _attachedLinearEndpoint,
                out var worldJunction,
                out var worldFrame))
        {
            _attachedLocalJunction = placeableTransform.InverseTransformPoint(worldJunction);
            _attachedLocalFrame = Quaternion.Inverse(placeableTransform.rotation) * worldFrame;
            _hasAttachedLocalPose = true;
        }
        else
        {
            _hasAttachedLocalPose = false;
        }

        CacheAttachedPlaceableTransform();
    }

    private static bool IsSandboxSimulationActive()
    {
        var sim = SandboxSimulationController.Instance;
        return sim != null && sim.IsSimulating;
    }

    private void CaptureSpringEndpointAttachmentGeometry()
    {
        var singleAttachmentPlaceable = _attachedStartPlaceable != null && _attachedEndPlaceable == null
            ? _attachedStartPlaceable
            : _attachedEndPlaceable != null && _attachedStartPlaceable == null
                ? _attachedEndPlaceable
                : null;

        if (singleAttachmentPlaceable != null)
        {
            var placeableTransform = singleAttachmentPlaceable.transform;
            var worldPositions = GetWorldLinePositions();
            _attachedLocalLinePositions = new Vector3[worldPositions.Length];
            for (var i = 0; i < worldPositions.Length; i++)
            {
                _attachedLocalLinePositions[i] = placeableTransform.InverseTransformPoint(worldPositions[i]);
            }
        }
        else
        {
            _attachedLocalLinePositions = null;
        }

        if (_attachedStartPlaceable != null)
        {
            _attachedStartLocalPoint = _attachedStartPlaceable.transform.InverseTransformPoint(
                ResolveLinearEndpointWorldPosition(PhysicsDrawingEndpoint.Start));
            CacheSpringEndpointTransform(PhysicsDrawingEndpoint.Start);
        }

        if (_attachedEndPlaceable != null)
        {
            _attachedEndLocalPoint = _attachedEndPlaceable.transform.InverseTransformPoint(
                ResolveLinearEndpointWorldPosition(PhysicsDrawingEndpoint.End));
            CacheSpringEndpointTransform(PhysicsDrawingEndpoint.End);
        }
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

    private void SetAttachedPlaceablePose(Vector3 targetPosition, Quaternion targetRotation)
    {
        if (_attachedPlaceable == null)
        {
            return;
        }

        _attachedPlaceable.SetPose(targetPosition, targetRotation);
    }

    private void RefreshAttachmentVisual()
    {
        if (_previewPlaceable == null
            && physicsIntent == PhysicsIntentType.Spring
            && HasSpringEndpointAttachment())
        {
            RefreshSpringEndpointAttachmentVisual();
            return;
        }

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
        if (_attachmentOutlineSphere != null)
        {
            _attachmentOutlineSphere.transform.position = junction;
            _attachmentOutlineSphere.transform.rotation = Quaternion.identity;
            _attachmentOutlineSphere.transform.localScale = Vector3.one * attachmentIndicatorDiameter * 1.18f;
            _attachmentOutlineSphere.layer = gameObject.layer;
        }

        if (_attachmentSphere != null)
        {
            _attachmentSphere.transform.position = junction;
            _attachmentSphere.transform.rotation = Quaternion.identity;
            _attachmentSphere.transform.localScale = Vector3.one * attachmentIndicatorDiameter;
            _attachmentSphere.layer = gameObject.layer;
        }

        if (_attachmentLineRenderer != null)
        {
            var lineTarget = isHinge
                ? ResolveHingeAttachmentBodyPoint(placeable)
                : ResolvePlaceableCenter(placeable);
            _attachmentLineRenderer.gameObject.layer = gameObject.layer;
            _attachmentLineRenderer.widthMultiplier = hingeAttachmentLineWidth;
            _attachmentLineRenderer.SetPosition(0, junction);
            _attachmentLineRenderer.SetPosition(1, lineTarget);
        }

        SetAttachmentVisualsVisible(true, isHinge);
    }

    private void RefreshSpringEndpointAttachmentVisual()
    {
        var hasStart = _attachedStartPlaceable != null;
        var hasEnd = _attachedEndPlaceable != null;
        if (!hasStart && !hasEnd)
        {
            SetAttachmentVisualsVisible(false, false);
            return;
        }

        EnsureAttachmentVisuals();
        var primarySet = false;
        if (hasStart)
        {
            SetAttachmentSpherePair(
                _attachmentOutlineSphere,
                _attachmentSphere,
                ResolveEndpointAttachmentVisualJunction(
                    _attachedStartPlaceable,
                    PhysicsDrawingEndpoint.Start,
                    ResolveLinearEndpointWorldPosition(PhysicsDrawingEndpoint.Start)));
            primarySet = true;
        }

        if (hasEnd)
        {
            var endJunction = ResolveEndpointAttachmentVisualJunction(
                _attachedEndPlaceable,
                PhysicsDrawingEndpoint.End,
                ResolveLinearEndpointWorldPosition(PhysicsDrawingEndpoint.End));
            if (primarySet)
            {
                SetAttachmentSpherePair(
                    _secondaryAttachmentOutlineSphere,
                    _secondaryAttachmentSphere,
                    endJunction);
            }
            else
            {
                SetAttachmentSpherePair(
                    _attachmentOutlineSphere,
                    _attachmentSphere,
                    endJunction);
            }
        }

        SetAttachmentVisualsVisible(true, false, hasStart && hasEnd);
    }

    private Vector3 ResolveEndpointAttachmentVisualJunction(
        PlaceableAsset placeable,
        PhysicsDrawingEndpoint endpoint,
        Vector3 fallback)
    {
        return TryResolveLinearAttachmentProbeForEndpoint(placeable, endpoint, out var probe)
            ? probe.JunctionPoint
            : fallback;
    }

    private void SetAttachmentSpherePair(
        GameObject outlineSphere,
        GameObject sphere,
        Vector3 junction)
    {
        if (outlineSphere != null)
        {
            outlineSphere.transform.position = junction;
            outlineSphere.transform.rotation = Quaternion.identity;
            outlineSphere.transform.localScale = Vector3.one * attachmentIndicatorDiameter * 1.18f;
            outlineSphere.layer = gameObject.layer;
        }

        if (sphere != null)
        {
            sphere.transform.position = junction;
            sphere.transform.rotation = Quaternion.identity;
            sphere.transform.localScale = Vector3.one * attachmentIndicatorDiameter;
            sphere.layer = gameObject.layer;
        }
    }

    private void EnsureAttachmentVisuals()
    {
        if (_attachmentOutlineSphere == null)
        {
            _attachmentOutlineSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _attachmentOutlineSphere.name = "PhysicsAttachmentJunctionOutline";
            _attachmentOutlineSphere.transform.SetParent(transform, true);
            _attachmentOutlineSphere.layer = gameObject.layer;

            var outlineCollider = _attachmentOutlineSphere.GetComponent<Collider>();
            if (outlineCollider != null)
            {
                Destroy(outlineCollider);
            }

            _attachmentOutlineSphereRenderer = _attachmentOutlineSphere.GetComponent<MeshRenderer>();
            if (_attachmentOutlineSphereRenderer != null)
            {
                _attachmentOutlineSphereRenderer.shadowCastingMode = ShadowCastingMode.Off;
                _attachmentOutlineSphereRenderer.receiveShadows = false;
                _attachmentOutlineSphereRenderer.sharedMaterial = GetAttachmentOutlineMaterial();
            }

            _attachmentOutlineSphere.SetActive(false);
        }

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

        if (_secondaryAttachmentOutlineSphere == null)
        {
            _secondaryAttachmentOutlineSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _secondaryAttachmentOutlineSphere.name = "PhysicsAttachmentJunctionOutlineSecondary";
            _secondaryAttachmentOutlineSphere.transform.SetParent(transform, true);
            _secondaryAttachmentOutlineSphere.layer = gameObject.layer;

            var secondaryOutlineCollider = _secondaryAttachmentOutlineSphere.GetComponent<Collider>();
            if (secondaryOutlineCollider != null)
            {
                Destroy(secondaryOutlineCollider);
            }

            _secondaryAttachmentOutlineSphereRenderer = _secondaryAttachmentOutlineSphere.GetComponent<MeshRenderer>();
            if (_secondaryAttachmentOutlineSphereRenderer != null)
            {
                _secondaryAttachmentOutlineSphereRenderer.shadowCastingMode = ShadowCastingMode.Off;
                _secondaryAttachmentOutlineSphereRenderer.receiveShadows = false;
                _secondaryAttachmentOutlineSphereRenderer.sharedMaterial = GetAttachmentOutlineMaterial();
            }

            _secondaryAttachmentOutlineSphere.SetActive(false);
        }

        if (_secondaryAttachmentSphere == null)
        {
            _secondaryAttachmentSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _secondaryAttachmentSphere.name = "PhysicsAttachmentJunctionSecondary";
            _secondaryAttachmentSphere.transform.SetParent(transform, true);
            _secondaryAttachmentSphere.layer = gameObject.layer;

            var secondaryCollider = _secondaryAttachmentSphere.GetComponent<Collider>();
            if (secondaryCollider != null)
            {
                Destroy(secondaryCollider);
            }

            _secondaryAttachmentSphereRenderer = _secondaryAttachmentSphere.GetComponent<MeshRenderer>();
            if (_secondaryAttachmentSphereRenderer != null)
            {
                _secondaryAttachmentSphereRenderer.shadowCastingMode = ShadowCastingMode.Off;
                _secondaryAttachmentSphereRenderer.receiveShadows = false;
                _secondaryAttachmentSphereRenderer.sharedMaterial = GetAttachmentMaterial();
            }

            _secondaryAttachmentSphere.SetActive(false);
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
        ApplyAttachmentOutlineMaterialColor();
    }

    private void SetAttachmentVisualsVisible(bool sphereVisible, bool lineVisible)
    {
        SetAttachmentVisualsVisible(sphereVisible, lineVisible, false);
    }

    private void SetAttachmentVisualsVisible(bool sphereVisible, bool lineVisible, bool secondarySphereVisible)
    {
        if (_attachmentSphere != null && _attachmentSphere.activeSelf != sphereVisible)
        {
            _attachmentSphere.SetActive(sphereVisible);
        }

        if (_attachmentOutlineSphere != null && _attachmentOutlineSphere.activeSelf != sphereVisible)
        {
            _attachmentOutlineSphere.SetActive(sphereVisible);
        }

        if (_secondaryAttachmentSphere != null
            && _secondaryAttachmentSphere.activeSelf != secondarySphereVisible)
        {
            _secondaryAttachmentSphere.SetActive(secondarySphereVisible);
        }

        if (_secondaryAttachmentOutlineSphere != null
            && _secondaryAttachmentOutlineSphere.activeSelf != secondarySphereVisible)
        {
            _secondaryAttachmentOutlineSphere.SetActive(secondarySphereVisible);
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
        ConfigureTransparentMaterial(_attachmentMaterial);
        _attachmentMaterial.renderQueue = (int)RenderQueue.Transparent + 15;
        ApplyAttachmentMaterialColor();
        return _attachmentMaterial;
    }

    private Material GetAttachmentOutlineMaterial()
    {
        if (_attachmentOutlineMaterial != null)
        {
            return _attachmentOutlineMaterial;
        }

        var shader = Shader.Find("Universal Render Pipeline/Unlit")
                     ?? Shader.Find("Sprites/Default")
                     ?? Shader.Find("Unlit/Color")
                     ?? Shader.Find("Standard");
        if (shader == null)
        {
            return null;
        }

        _attachmentOutlineMaterial = new Material(shader)
        {
            name = "PhysicsDrawingAttachmentIndicatorOutline",
            color = attachmentIndicatorOutlineColor,
            enableInstancing = true
        };
        ConfigureTransparentMaterial(_attachmentOutlineMaterial);
        _attachmentOutlineMaterial.renderQueue = (int)RenderQueue.Transparent + 14;
        ApplyAttachmentOutlineMaterialColor();
        return _attachmentOutlineMaterial;
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

    private void ApplyAttachmentOutlineMaterialColor()
    {
        if (_attachmentOutlineMaterial == null)
        {
            return;
        }

        _attachmentOutlineMaterial.color = attachmentIndicatorOutlineColor;
        if (_attachmentOutlineMaterial.HasProperty("_BaseColor"))
        {
            _attachmentOutlineMaterial.SetColor("_BaseColor", attachmentIndicatorOutlineColor);
        }

        if (_attachmentOutlineMaterial.HasProperty("_Color"))
        {
            _attachmentOutlineMaterial.SetColor("_Color", attachmentIndicatorOutlineColor);
        }

        if (_attachmentOutlineMaterial.HasProperty("_Cull"))
        {
            _attachmentOutlineMaterial.SetFloat("_Cull", (float)CullMode.Front);
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
        if (IsAttachedToPlaceable && !_isApplyingAttachmentFollow)
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

        var color = _baseColor;
        if (ShouldRenderAttachedHingeTranslucent())
        {
            color.a = Mathf.Min(color.a, attachedHingeAlpha);
        }

        if (_lineRenderer != null && _lineRenderer.material != null)
        {
            if (color.a < 0.999f)
            {
                ConfigureTransparentMaterial(_lineRenderer.material);
            }

            _lineRenderer.material.color = color;
            _lineRenderer.startColor = color;
            _lineRenderer.endColor = color;
        }

        if (_arrowTip != null)
        {
            _arrowTip.SetColor(color);
        }
    }

    private bool ShouldRenderAttachedHingeTranslucent()
    {
        return physicsIntent == PhysicsIntentType.Hinge && _attachedPlaceable != null;
    }

    private static void ConfigureTransparentMaterial(Material material)
    {
        if (material == null)
        {
            return;
        }

        if (material.HasProperty("_Surface"))
        {
            material.SetFloat("_Surface", 1f);
        }

        if (material.HasProperty("_Blend"))
        {
            material.SetFloat("_Blend", 0f);
        }

        if (material.HasProperty("_Cull"))
        {
            material.SetFloat("_Cull", (float)CullMode.Off);
        }

        material.SetOverrideTag("RenderType", "Transparent");
        material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        material.SetInt("_ZWrite", 0);
        material.DisableKeyword("_ALPHATEST_ON");
        material.EnableKeyword("_ALPHABLEND_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.renderQueue = (int)RenderQueue.Transparent;
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
        _selectionAuraMaterial.renderQueue = (int)RenderQueue.Transparent + 20;
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
