using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
[RequireComponent(typeof(LineRenderer))]
public sealed class PhysicsDrawingSelectable : MonoBehaviour
{
    private const float ArrowConeLength = 0.03f;
    private const float ArrowConeRadius = 0.009f;

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

    private readonly List<GameObject> _colliders = new();
    private LineRenderer _lineRenderer;
    private LineRenderer _selectionAuraRenderer;
    private LineArrowTip _arrowTip;
    private Material _selectionAuraMaterial;
    private LineDrawing _owner;
    private Color _baseColor = Color.white;
    private bool _isHovered;
    private bool _isSelected;

    public string DisplayName => displayName;
    public PhysicsIntentType PhysicsIntent => physicsIntent;
    public string ShapeName => shapeName;
    public bool IsSelected => _isSelected;
    public float SpringStiffness => springStiffness;
    public float HingeTorque => hingeTorque;
    public float ImpulseForce => impulseForce;
    public bool ImpulseInstant => impulseInstant;

    private void Awake()
    {
        ResolveReferences();
        CacheBaseColor();
    }

    private void OnValidate()
    {
        springStiffness = Mathf.Clamp01(springStiffness);
        hingeTorque = Mathf.Clamp01(hingeTorque);
        impulseForce = Mathf.Clamp01(impulseForce);
        selectionAuraWidthMultiplier = Mathf.Max(1f, selectionAuraWidthMultiplier);
        selectionAuraConeScaleMultiplier = Mathf.Max(1f, selectionAuraConeScaleMultiplier);
        selectionAuraConeBaseOverlapFraction = Mathf.Max(0f, selectionAuraConeBaseOverlapFraction);
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

    private void RefreshGeometryAfterLineEdit()
    {
        if (_arrowTip != null && _lineRenderer != null)
        {
            _arrowTip.UpdateFromLine(_lineRenderer, ArrowConeLength, ArrowConeRadius);
        }

        RebuildColliders();
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

    private static string ResolveDisplayName(PhysicsGestureReadoutResult readout)
    {
        if (readout.PhysicsIntent != PhysicsIntentType.Unknown)
        {
            return readout.PhysicsIntent.ToString();
        }

        return string.IsNullOrEmpty(readout.ShapeName) ? "Drawing" : readout.ShapeName;
    }
}
