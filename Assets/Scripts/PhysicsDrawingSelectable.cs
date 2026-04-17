using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(LineRenderer))]
public sealed class PhysicsDrawingSelectable : MonoBehaviour
{
    [SerializeField] private string displayName = "Drawing";
    [SerializeField] private PhysicsIntentType physicsIntent = PhysicsIntentType.Unknown;
    [SerializeField] private string shapeName = "Unknown";
    [SerializeField] private Color highlightColor = Color.yellow;
    [SerializeField] private float colliderRadius = 0.025f;
    [SerializeField, Range(0f, 1f)] private float springStiffness = 0.5f;
    [SerializeField, Range(0f, 1f)] private float hingeTorque = 0.5f;
    [SerializeField, Range(0f, 1f)] private float impulseForce = 0.5f;
    [SerializeField] private bool impulseInstant;

    private readonly List<GameObject> _colliders = new();
    private LineRenderer _lineRenderer;
    private LineArrowTip _arrowTip;
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
    }

    private void OnDestroy()
    {
        if (AssetSelectionManager.Instance != null
            && AssetSelectionManager.Instance.SelectedPhysicsDrawing == this)
        {
            AssetSelectionManager.Instance.ClearSelection();
        }
    }

    public void Initialize(PhysicsGestureReadoutResult readout, Color selectedHighlightColor)
    {
        ResolveReferences();
        highlightColor = selectedHighlightColor;

        if (readout != null)
        {
            physicsIntent = readout.PhysicsIntent;
            shapeName = string.IsNullOrEmpty(readout.ShapeName) ? "Unknown" : readout.ShapeName;
            displayName = ResolveDisplayName(readout);
        }

        CacheBaseColor();
        RebuildColliders();
        ApplyHighlightState();
    }

    public void SetHovered(bool hovered)
    {
        _isHovered = hovered;
        ApplyHighlightState();
    }

    public void SetSelected(bool selected)
    {
        _isSelected = selected;
        ApplyHighlightState();
    }

    public void SetSpringStiffness(float value)
    {
        springStiffness = Mathf.Clamp01(value);
    }

    public void SetHingeTorque(float value)
    {
        hingeTorque = Mathf.Clamp01(value);
    }

    public void SetImpulseForce(float value)
    {
        impulseForce = Mathf.Clamp01(value);
    }

    public void SetImpulseInstant(bool instant)
    {
        impulseInstant = instant;
    }

    public void RebuildColliders()
    {
        ResolveReferences();
        ClearColliders();

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
        var color = _isSelected || _isHovered ? highlightColor : _baseColor;

        if (_lineRenderer != null && _lineRenderer.material != null)
        {
            _lineRenderer.material.color = color;
        }

        if (_arrowTip != null)
        {
            _arrowTip.SetColor(color);
        }
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
