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
    [SerializeField] private XRContentDrawerController _controlModeSource;
    [SerializeField] private XRDrawerItemSelectionManager _drawerItemSelection;
    [SerializeField] private bool _hideWhenStylusInactive = true;
    [SerializeField] private bool _hideOutsideSelectionMode = true;
    [SerializeField] private float _drawerSelectionRayDistance = 8f;
    [SerializeField] private LayerMask _drawerSelectionRaycastMask = ~0;
    [SerializeField] private Vector3 _localPositionOffset = Vector3.zero;
    [SerializeField] private Vector3 _localEulerOffset = Vector3.zero;

    private XRRayInteractor _rayInteractor;
    private LineRenderer _lineRenderer;
    private Transform _runtimeRayOrigin;
    private Material _runtimeMaterial;
    private bool _clusterBackWasPressed;

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

        ResolveControlModeSource();
        ResolveDrawerItemSelection();
        EnsureRayOrigin();
        ApplyLineSetup();
        ApplyRayBindings();
        UpdateVisibility();
    }

    private void LateUpdate()
    {
        ResolveControlModeSource();
        ResolveDrawerItemSelection();
        EnsureRayOrigin();
        ApplyRayBindings();
        UpdateVisibility();
        HandleSelectionModeInteractions();
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
        var stylusIsVisible = !_hideWhenStylusInactive
                              || _stylusHandler == null
                              || _stylusHandler.IsTrackingStylus;
        var modeIsVisible = !_hideOutsideSelectionMode || IsSelectionMode();
        var isVisible = stylusIsVisible && modeIsVisible;

        if (_lineRenderer.enabled != isVisible)
        {
            _lineRenderer.enabled = isVisible;
        }

        if (_rayInteractor.enabled != isVisible)
        {
            _rayInteractor.enabled = isVisible;
        }

        if (_lineVisual != null && _lineVisual.enabled != isVisible)
        {
            _lineVisual.enabled = isVisible;
        }
    }

    private void HandleSelectionModeInteractions()
    {
        var clusterBackPressed = _stylusHandler != null && _stylusHandler.CurrentState.cluster_back_value;

        if (!CanUseSelectionRay())
        {
            _clusterBackWasPressed = clusterBackPressed;
            return;
        }

        TrySelectDrawerItemUnderRay();

        if (clusterBackPressed && !_clusterBackWasPressed && _drawerItemSelection != null)
        {
            _drawerItemSelection.TryConfirmSpawnSelected();
        }

        _clusterBackWasPressed = clusterBackPressed;
    }

    private bool CanUseSelectionRay()
    {
        return IsSelectionMode()
               && (!_hideWhenStylusInactive || _stylusHandler == null || _stylusHandler.IsTrackingStylus);
    }

    private void TrySelectDrawerItemUnderRay()
    {
        if (_runtimeRayOrigin == null || _drawerItemSelection == null)
        {
            return;
        }

        var hits = Physics.RaycastAll(
            _runtimeRayOrigin.position,
            _runtimeRayOrigin.forward,
            _drawerSelectionRayDistance,
            _drawerSelectionRaycastMask,
            QueryTriggerInteraction.Collide);

        if (hits == null || hits.Length == 0)
        {
            return;
        }

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (var hit in hits)
        {
            if (hit.collider.GetComponent<DrawerTilePickTarget>() == null)
            {
                continue;
            }

            var drawerItem = hit.collider.GetComponentInParent<XRDrawerItem>();
            if (drawerItem == null)
            {
                continue;
            }

            _drawerItemSelection.SelectItem(drawerItem);
            return;
        }
    }

    private bool IsSelectionMode()
    {
        ResolveControlModeSource();
        return _controlModeSource != null && _controlModeSource.CurrentMode == XRControlMode.Selection;
    }

    private void ResolveControlModeSource()
    {
        if (_controlModeSource != null)
        {
            return;
        }

        _controlModeSource = FindFirstObjectByType<XRContentDrawerController>(FindObjectsInactive.Include);
    }

    private void ResolveDrawerItemSelection()
    {
        if (_drawerItemSelection != null)
        {
            return;
        }

        _drawerItemSelection = FindFirstObjectByType<XRDrawerItemSelectionManager>(FindObjectsInactive.Include);
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
