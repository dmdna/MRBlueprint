using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;

[DisallowMultipleComponent]
[RequireComponent(typeof(XRRayInteractor))]
[RequireComponent(typeof(LineRenderer))]
public class MXInkRayInteractorBinder : MonoBehaviour
{
    [SerializeField] private VrStylusHandler _stylusHandler;
    [SerializeField] private XRInteractorLineVisual _lineVisual;
    [SerializeField] private Transform _explicitRayOrigin;
    [SerializeField] private bool _hideWhenStylusInactive = true;
    [SerializeField] private Vector3 _localPositionOffset = Vector3.zero;
    [SerializeField] private Vector3 _localEulerOffset = Vector3.zero;

    private XRRayInteractor _rayInteractor;
    private LineRenderer _lineRenderer;
    private Transform _runtimeRayOrigin;
    private Material _runtimeMaterial;

    private static readonly Color ValidColor = Color.white;
    private static readonly Color InvalidColor = new(1f, 0.35f, 0.35f, 1f);
    private static readonly Color BlockedColor = new(1f, 0.92f, 0.15f, 1f);

    private void Awake()
    {
        _rayInteractor = GetComponent<XRRayInteractor>();
        _lineRenderer = GetComponent<LineRenderer>();

        if (_lineVisual == null)
        {
            _lineVisual = GetComponent<XRInteractorLineVisual>();
        }

        if (_stylusHandler == null)
        {
            _stylusHandler = GetComponentInParent<VrStylusHandler>();
        }

        EnsureRayOrigin();
        ApplyLineSetup();
        ApplyRayBindings();
    }

    private void LateUpdate()
    {
        EnsureRayOrigin();
        ApplyRayBindings();
        UpdateVisibility();
    }

    private void OnDestroy()
    {
        if (_runtimeMaterial != null)
        {
            Destroy(_runtimeMaterial);
        }
    }

    private void EnsureRayOrigin()
    {
        var targetOrigin = ResolveTargetOrigin();
        if (targetOrigin == null)
        {
            return;
        }

        if (_runtimeRayOrigin == null)
        {
            var rayOriginObject = new GameObject("MXInkRayOrigin");
            _runtimeRayOrigin = rayOriginObject.transform;
        }

        if (_runtimeRayOrigin.parent != targetOrigin)
        {
            _runtimeRayOrigin.SetParent(targetOrigin, false);
        }

        _runtimeRayOrigin.localPosition = _localPositionOffset;
        _runtimeRayOrigin.localRotation = Quaternion.Euler(_localEulerOffset);
    }

    private Transform ResolveTargetOrigin()
    {
        if (_explicitRayOrigin != null)
        {
            return _explicitRayOrigin;
        }

        if (_stylusHandler != null)
        {
            return _stylusHandler.TipTransform;
        }

        return transform.parent;
    }

    private void ApplyRayBindings()
    {
        if (_runtimeRayOrigin == null)
        {
            return;
        }

        _rayInteractor.rayOriginTransform = _runtimeRayOrigin;

        if (_lineVisual != null)
        {
            _lineVisual.overrideInteractorLineOrigin = true;
            _lineVisual.lineOriginTransform = _runtimeRayOrigin;
            _lineVisual.setLineColorGradient = true;
            _lineVisual.validColorGradient = BuildGradient(ValidColor);
            _lineVisual.invalidColorGradient = BuildGradient(InvalidColor);
            _lineVisual.blockedColorGradient = BuildGradient(BlockedColor);
        }
    }

    private void ApplyLineSetup()
    {
        _lineRenderer.alignment = LineAlignment.View;
        _lineRenderer.shadowCastingMode = ShadowCastingMode.Off;
        _lineRenderer.receiveShadows = false;

        if (_lineRenderer.sharedMaterial == null)
        {
            _runtimeMaterial = CreateLineMaterial();
            if (_runtimeMaterial != null)
            {
                _lineRenderer.sharedMaterial = _runtimeMaterial;
            }
        }

        _lineRenderer.startColor = ValidColor;
        _lineRenderer.endColor = ValidColor;
    }

    private void UpdateVisibility()
    {
        if (!_hideWhenStylusInactive || _stylusHandler == null)
        {
            return;
        }

        var isVisible = _stylusHandler.CurrentState.isActive;
        if (_lineRenderer.enabled != isVisible)
        {
            _lineRenderer.enabled = isVisible;
        }
    }

    private static Gradient BuildGradient(Color color)
    {
        var gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(color, 0f),
                new GradientColorKey(color, 1f),
            },
            new[]
            {
                new GradientAlphaKey(color.a, 0f),
                new GradientAlphaKey(color.a, 1f),
            });
        return gradient;
    }

    private static Material CreateLineMaterial()
    {
        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
        {
            shader = Shader.Find("Universal Render Pipeline/Lit");
        }

        if (shader == null)
        {
            shader = Shader.Find("Sprites/Default");
        }

        if (shader == null)
        {
            return null;
        }

        var material = new Material(shader)
        {
            name = "MXInkRayRuntimeMaterial",
            color = ValidColor,
            enableInstancing = true
        };

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", ValidColor);
        }

        if (material.HasProperty("_Surface"))
        {
            material.SetFloat("_Surface", 1f);
        }

        if (material.HasProperty("_Blend"))
        {
            material.SetFloat("_Blend", 0f);
        }

        return material;
    }
}
