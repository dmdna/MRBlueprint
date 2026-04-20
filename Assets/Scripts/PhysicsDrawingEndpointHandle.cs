using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public enum PhysicsDrawingEndpoint
{
    Start,
    End
}

[DisallowMultipleComponent]
[RequireComponent(typeof(SphereCollider))]
public sealed class PhysicsDrawingEndpointHandle : MonoBehaviour
{
    private const int SphereSegments = 24;
    private const float MinRayDragDistance = 0f;
    private const float VisibilityFrameGrace = 1f;

    private static readonly List<PhysicsDrawingEndpointHandle> AllHandles = new();
    private static readonly Dictionary<int, RayDragState> ActiveRayDrags = new();
    private static readonly List<int> ScratchSourceIds = new();
    private static Mesh _sphereMesh;
    private static PhysicsDrawingEndpointHandle _directDragHandle;
    private static int _lastAnyInteractionFrame = -1000;

    [SerializeField] private PhysicsDrawingSelectable owner;
    [SerializeField] private PhysicsDrawingEndpoint endpoint;
    [SerializeField] private float visualDiameter = 0.045f;
    [SerializeField] private float colliderRadius = 0.035f;
    [SerializeField] private Color hoverColor = new(1f, 1f, 1f, 0.82f);
    [SerializeField] private Color dragColor = new(0.2f, 0.85f, 1f, 0.95f);
    [SerializeField] private Color unattachedBorderColor = new(0.42f, 0.42f, 0.42f, 0.5f);
    [SerializeField] private Color attachedBorderColor = new(0f, 0f, 0f, 0.78f);

    private MeshRenderer _renderer;
    private MeshRenderer _borderRenderer;
    private MeshFilter _meshFilter;
    private SphereCollider _collider;
    private Material _material;
    private Material _borderMaterial;
    private bool _isDragging;
    private int _lastRayHoverFrame = -1000;
    private int _lastDirectHoverFrame = -1000;

    public PhysicsDrawingSelectable Owner => owner;
    public PhysicsDrawingEndpoint Endpoint => endpoint;
    public bool IsEditable => owner != null && owner.SupportsEndpointEditing;

    public static bool AnyEndpointInteractionActive
    {
        get
        {
            PruneInvalidRayDrags();
            return _directDragHandle != null
                   || ActiveRayDrags.Count > 0
                   || Time.frameCount - _lastAnyInteractionFrame <= VisibilityFrameGrace;
        }
    }

    private void Awake()
    {
        EnsureReferences();
        ApplyVisualState(false);
    }

    private void OnEnable()
    {
        if (!AllHandles.Contains(this))
        {
            AllHandles.Add(this);
        }
    }

    private void LateUpdate()
    {
        RefreshVisibility();
    }

    private void OnDisable()
    {
        AllHandles.Remove(this);
        EndAllDragsForHandle(this);
        ApplyVisualState(false);
    }

    private void OnDestroy()
    {
        AllHandles.Remove(this);
        EndAllDragsForHandle(this);

        if (_material != null)
        {
            Destroy(_material);
        }

        if (_borderMaterial != null)
        {
            Destroy(_borderMaterial);
        }
    }

    public void Configure(
        PhysicsDrawingSelectable newOwner,
        PhysicsDrawingEndpoint newEndpoint,
        float newVisualDiameter,
        float newColliderRadius,
        Color newHoverColor,
        Color newDragColor)
    {
        owner = newOwner;
        endpoint = newEndpoint;
        visualDiameter = Mathf.Max(0.001f, newVisualDiameter);
        colliderRadius = Mathf.Max(visualDiameter * 0.5f, newColliderRadius);
        hoverColor = newHoverColor;
        dragColor = newDragColor;

        EnsureReferences();
        transform.localScale = Vector3.one * visualDiameter;
        _collider.radius = Mathf.Max(0.5f, colliderRadius / visualDiameter);
        SetMaterialColor(_isDragging ? dragColor : hoverColor);
        ApplyBorderMaterialColor();
        RefreshVisibility();
    }

    public void SetWorldPosition(Vector3 position)
    {
        transform.position = position;
    }

    public void MarkRayHovered()
    {
        _lastRayHoverFrame = Time.frameCount;
        MarkAnyInteraction();
        ApplyVisualState(true);
    }

    public void MarkDirectHovered()
    {
        _lastDirectHoverFrame = Time.frameCount;
        MarkAnyInteraction();
        ApplyVisualState(true);
    }

    public static bool HandleDirectStylus(StylusHandler stylusHandler)
    {
        PruneInvalidRayDrags();

        if (stylusHandler == null)
        {
            EndDirectDrag();
            return false;
        }

        var stylusState = stylusHandler.CurrentState;
        var stylusUsable = stylusHandler.CanDraw();
        var grabPressed = stylusState.cluster_front_value;
        var stylusPoint = stylusState.inkingPose.position;

        if (_directDragHandle != null)
        {
            if (grabPressed && stylusUsable && _directDragHandle.IsEditable)
            {
                _directDragHandle.SetEndpointWorldPosition(stylusPoint);
                _directDragHandle.MarkDirectHovered();
                _directDragHandle.SetDragging(true);
                return true;
            }

            EndDirectDrag();
            return true;
        }

        if (!stylusUsable)
        {
            return false;
        }

        var nearest = FindNearestDirectHandle(stylusPoint);
        if (nearest == null)
        {
            return false;
        }

        nearest.MarkDirectHovered();
        if (!grabPressed)
        {
            return false;
        }

        _directDragHandle = nearest;
        nearest.owner?.DetachSpringEndpointForDrag(nearest.endpoint);
        nearest.SetDragging(true);
        nearest.SetEndpointWorldPosition(stylusPoint);
        nearest.MarkDirectHovered();
        return true;
    }

    public static bool IsSourceRayDragging(int sourceId)
    {
        PruneInvalidRayDrags();
        return ActiveRayDrags.ContainsKey(sourceId);
    }

    public static bool IsSourceDirectDragging(int sourceId)
    {
        PruneInvalidRayDrags();
        return ActiveRayDrags.TryGetValue(sourceId, out var drag) && drag.IsDirect;
    }

    public static bool TryBeginRayDrag(
        int sourceId,
        PhysicsDrawingEndpointHandle handle,
        Vector3 origin,
        Vector3 direction,
        float hitDistance,
        float minDistance,
        float maxDistance)
    {
        if (handle == null || !handle.IsEditable || direction.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        EndRayDrag(sourceId);

        direction.Normalize();
        var maxGrabDistance = Mathf.Max(MinRayDragDistance, maxDistance);
        var distance = Mathf.Clamp(
            Mathf.Max(hitDistance, MinRayDragDistance),
            MinRayDragDistance,
            maxGrabDistance);

        var grabPoint = origin + direction * distance;
        ActiveRayDrags[sourceId] = new RayDragState
        {
            Handle = handle,
            Distance = distance,
            GrabPoint = grabPoint,
            IsDirect = false
        };

        handle.owner?.DetachSpringEndpointForDrag(handle.endpoint);
        handle.SetDragging(true);
        handle.SetEndpointWorldPosition(grabPoint);
        handle.MarkRayHovered();

        if (AssetSelectionManager.Instance != null && AssetSelectionManager.Instance.HasSelection)
        {
            AssetSelectionManager.Instance.ClearSelection();
        }

        return true;
    }

    public static bool TryBeginDirectDrag(
        int sourceId,
        PhysicsDrawingEndpointHandle handle,
        Vector3 grabPoint)
    {
        if (handle == null || !handle.IsEditable)
        {
            return false;
        }

        EndRayDrag(sourceId);

        ActiveRayDrags[sourceId] = new RayDragState
        {
            Handle = handle,
            Distance = 0f,
            GrabPoint = grabPoint,
            IsDirect = true
        };

        handle.owner?.DetachSpringEndpointForDrag(handle.endpoint);
        handle.SetDragging(true);
        handle.SetEndpointWorldPosition(grabPoint);
        handle.MarkDirectHovered();

        if (AssetSelectionManager.Instance != null && AssetSelectionManager.Instance.HasSelection)
        {
            AssetSelectionManager.Instance.ClearSelection();
        }

        return true;
    }

    public static bool UpdateRayDrag(
        int sourceId,
        Vector3 origin,
        Vector3 direction,
        float distanceDelta,
        float minDistance,
        float maxDistance)
    {
        PruneInvalidRayDrags();
        if (!ActiveRayDrags.TryGetValue(sourceId, out var drag))
        {
            return false;
        }

        if (drag.Handle == null || !drag.Handle.IsEditable || direction.sqrMagnitude <= 0.0001f)
        {
            EndRayDrag(sourceId);
            return false;
        }

        direction.Normalize();
        var maxGrabDistance = Mathf.Max(MinRayDragDistance, maxDistance);
        drag.Distance = Mathf.Clamp(
            drag.Distance + distanceDelta,
            MinRayDragDistance,
            maxGrabDistance);
        drag.GrabPoint = origin + direction * drag.Distance;
        drag.Handle.SetEndpointWorldPosition(drag.GrabPoint);
        drag.Handle.MarkRayHovered();
        drag.Handle.SetDragging(true);
        return true;
    }

    public static bool UpdateDirectDrag(int sourceId, Vector3 grabPoint)
    {
        PruneInvalidRayDrags();
        if (!ActiveRayDrags.TryGetValue(sourceId, out var drag))
        {
            return false;
        }

        if (drag.Handle == null || !drag.Handle.IsEditable)
        {
            EndRayDrag(sourceId);
            return false;
        }

        drag.GrabPoint = grabPoint;
        drag.Distance = 0f;
        drag.IsDirect = true;
        drag.Handle.SetEndpointWorldPosition(grabPoint);
        drag.Handle.MarkDirectHovered();
        drag.Handle.SetDragging(true);
        return true;
    }

    public static void EndRayDrag(int sourceId)
    {
        PruneInvalidRayDrags();
        if (!ActiveRayDrags.TryGetValue(sourceId, out var drag))
        {
            return;
        }

        if (drag.Handle != null)
        {
            drag.Handle.SetDragging(false);
            drag.Handle.owner?.TryAttachEndpointToPlaceableOnRelease(drag.Handle.endpoint);
        }

        ActiveRayDrags.Remove(sourceId);
    }

    public static bool TryGetSourceGrabPoint(int sourceId, out Vector3 grabPoint)
    {
        PruneInvalidRayDrags();
        if (ActiveRayDrags.TryGetValue(sourceId, out var drag))
        {
            grabPoint = drag.GrabPoint;
            return true;
        }

        grabPoint = default;
        return false;
    }

    public static bool TryGetSourceGrabDistance(int sourceId, out float distance)
    {
        PruneInvalidRayDrags();
        if (ActiveRayDrags.TryGetValue(sourceId, out var drag))
        {
            distance = drag.Distance;
            return true;
        }

        distance = default;
        return false;
    }

    public static bool TryFindNearestDirectHandle(
        Vector3 point,
        float radius,
        out PhysicsDrawingEndpointHandle handle)
    {
        handle = null;
        var bestDistance = float.MaxValue;
        radius = Mathf.Max(0.001f, radius);

        for (var i = AllHandles.Count - 1; i >= 0; i--)
        {
            var candidate = AllHandles[i];
            if (candidate == null)
            {
                AllHandles.RemoveAt(i);
                continue;
            }

            if (!candidate.isActiveAndEnabled || !candidate.IsEditable)
            {
                continue;
            }

            var hoverRadius = Mathf.Max(
                radius,
                candidate.colliderRadius,
                candidate.visualDiameter * 0.5f);
            var distance = Vector3.Distance(point, candidate.transform.position);
            if (distance > hoverRadius || distance >= bestDistance)
            {
                continue;
            }

            handle = candidate;
            bestDistance = distance;
        }

        return handle != null;
    }

    public static bool TryFindNearestRayHandle(
        Vector3 origin,
        Vector3 direction,
        float maxDistance,
        out PhysicsDrawingEndpointHandle handle,
        out float hitDistance,
        out Vector3 hitPoint)
    {
        handle = null;
        hitDistance = 0f;
        hitPoint = default;
        if (direction.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        direction.Normalize();
        maxDistance = Mathf.Max(0.01f, maxDistance);
        var bestDistanceToRay = float.MaxValue;
        var bestRayDistance = float.MaxValue;

        for (var i = AllHandles.Count - 1; i >= 0; i--)
        {
            var candidate = AllHandles[i];
            if (candidate == null)
            {
                AllHandles.RemoveAt(i);
                continue;
            }

            if (!candidate.isActiveAndEnabled || !candidate.IsEditable)
            {
                continue;
            }

            var toHandle = candidate.transform.position - origin;
            var rayDistance = Mathf.Clamp(Vector3.Dot(toHandle, direction), 0f, maxDistance);
            var closestPoint = origin + direction * rayDistance;
            var distanceToRay = Vector3.Distance(candidate.transform.position, closestPoint);
            var hoverRadius = Mathf.Max(candidate.colliderRadius, candidate.visualDiameter * 0.5f) * 2.2f;
            if (distanceToRay > hoverRadius)
            {
                continue;
            }

            if (distanceToRay > bestDistanceToRay + 0.0001f
                || (Mathf.Abs(distanceToRay - bestDistanceToRay) <= 0.0001f
                    && rayDistance >= bestRayDistance))
            {
                continue;
            }

            handle = candidate;
            hitDistance = rayDistance;
            hitPoint = closestPoint;
            bestRayDistance = rayDistance;
            bestDistanceToRay = distanceToRay;
        }

        return handle != null;
    }

    private static PhysicsDrawingEndpointHandle FindNearestDirectHandle(Vector3 stylusPoint)
    {
        PhysicsDrawingEndpointHandle nearest = null;
        var nearestDistance = float.MaxValue;

        for (var i = AllHandles.Count - 1; i >= 0; i--)
        {
            var handle = AllHandles[i];
            if (handle == null)
            {
                AllHandles.RemoveAt(i);
                continue;
            }

            if (!handle.isActiveAndEnabled || !handle.IsEditable)
            {
                continue;
            }

            var distance = Vector3.Distance(stylusPoint, handle.transform.position);
            var hoverRadius = Mathf.Max(handle.colliderRadius, handle.visualDiameter * 0.5f);
            if (distance > hoverRadius || distance >= nearestDistance)
            {
                continue;
            }

            nearest = handle;
            nearestDistance = distance;
        }

        return nearest;
    }

    private static void EndDirectDrag()
    {
        if (_directDragHandle == null)
        {
            return;
        }

        _directDragHandle.SetDragging(false);
        _directDragHandle.owner?.TryAttachEndpointToPlaceableOnRelease(_directDragHandle.endpoint);
        _directDragHandle = null;
    }

    private static void EndAllDragsForHandle(PhysicsDrawingEndpointHandle handle)
    {
        if (handle == null)
        {
            return;
        }

        if (_directDragHandle == handle)
        {
            _directDragHandle = null;
        }

        ScratchSourceIds.Clear();
        foreach (var pair in ActiveRayDrags)
        {
            if (pair.Value.Handle == handle)
            {
                ScratchSourceIds.Add(pair.Key);
            }
        }

        for (var i = 0; i < ScratchSourceIds.Count; i++)
        {
            ActiveRayDrags.Remove(ScratchSourceIds[i]);
        }

        handle.SetDragging(false);
    }

    private static void PruneInvalidRayDrags()
    {
        ScratchSourceIds.Clear();
        foreach (var pair in ActiveRayDrags)
        {
            var handle = pair.Value.Handle;
            if (handle == null || !handle.isActiveAndEnabled || !handle.IsEditable)
            {
                ScratchSourceIds.Add(pair.Key);
            }
        }

        for (var i = 0; i < ScratchSourceIds.Count; i++)
        {
            ActiveRayDrags.Remove(ScratchSourceIds[i]);
        }

        if (_directDragHandle != null
            && (!_directDragHandle.isActiveAndEnabled || !_directDragHandle.IsEditable))
        {
            _directDragHandle = null;
        }
    }

    private void SetEndpointWorldPosition(Vector3 worldPosition)
    {
        if (owner == null)
        {
            return;
        }

        owner.SetEndpointWorldPosition(endpoint, worldPosition);
    }

    private void SetDragging(bool dragging)
    {
        _isDragging = dragging;
        SetMaterialColor(_isDragging ? dragColor : hoverColor);
        RefreshVisibility();
    }

    private void RefreshVisibility()
    {
        var visible = _isDragging
                      || Time.frameCount - _lastRayHoverFrame <= VisibilityFrameGrace
                      || Time.frameCount - _lastDirectHoverFrame <= VisibilityFrameGrace;
        ApplyVisualState(visible && IsEditable);
    }

    private void EnsureReferences()
    {
        if (_meshFilter == null)
        {
            _meshFilter = GetComponent<MeshFilter>();
            if (_meshFilter == null)
            {
                _meshFilter = gameObject.AddComponent<MeshFilter>();
            }
        }

        _meshFilter.sharedMesh = GetOrCreateSphereMesh();

        if (_renderer == null)
        {
            _renderer = GetComponent<MeshRenderer>();
            if (_renderer == null)
            {
                _renderer = gameObject.AddComponent<MeshRenderer>();
            }
        }

        _renderer.shadowCastingMode = ShadowCastingMode.Off;
        _renderer.receiveShadows = false;

        if (_material == null)
        {
            _material = CreateHandleMaterial(hoverColor);
        }

        if (_material != null)
        {
            _renderer.sharedMaterial = _material;
        }

        EnsureBorderVisual();

        if (_collider == null)
        {
            _collider = GetComponent<SphereCollider>();
        }

        _collider.isTrigger = true;
        transform.localScale = Vector3.one * Mathf.Max(0.001f, visualDiameter);
        _collider.radius = Mathf.Max(0.5f, colliderRadius / Mathf.Max(0.001f, visualDiameter));
    }

    private void ApplyVisualState(bool visible)
    {
        EnsureReferences();
        if (_renderer.enabled != visible)
        {
            _renderer.enabled = visible;
        }

        if (_borderRenderer != null)
        {
            if (_borderRenderer.enabled != visible)
            {
                _borderRenderer.enabled = visible;
            }

            if (visible)
            {
                ApplyBorderMaterialColor();
            }
        }
    }

    private void SetMaterialColor(Color color)
    {
        EnsureReferences();
        if (_material == null)
        {
            return;
        }

        _material.color = color;
        if (_material.HasProperty("_BaseColor"))
        {
            _material.SetColor("_BaseColor", color);
        }

        if (_material.HasProperty("_Color"))
        {
            _material.SetColor("_Color", color);
        }

        ApplyBorderMaterialColor();
    }

    private void EnsureBorderVisual()
    {
        if (_borderRenderer != null)
        {
            return;
        }

        var borderObject = new GameObject("EndpointBorder");
        borderObject.transform.SetParent(transform, false);
        borderObject.transform.localPosition = Vector3.zero;
        borderObject.transform.localRotation = Quaternion.identity;
        borderObject.transform.localScale = Vector3.one * 1.24f;
        borderObject.layer = gameObject.layer;

        var borderMeshFilter = borderObject.AddComponent<MeshFilter>();
        borderMeshFilter.sharedMesh = GetOrCreateSphereMesh();
        _borderRenderer = borderObject.AddComponent<MeshRenderer>();
        _borderRenderer.shadowCastingMode = ShadowCastingMode.Off;
        _borderRenderer.receiveShadows = false;

        if (_borderMaterial == null)
        {
            _borderMaterial = CreateBorderMaterial(ResolveBorderColor());
            if (_borderMaterial != null)
            {
                _borderMaterial.renderQueue = (int)RenderQueue.Transparent + 29;
            }
        }

        if (_borderMaterial != null)
        {
            _borderRenderer.sharedMaterial = _borderMaterial;
        }

        _borderRenderer.enabled = false;
    }

    private void ApplyBorderMaterialColor()
    {
        EnsureBorderVisual();
        if (_borderMaterial == null)
        {
            return;
        }

        var color = ResolveBorderColor();
        _borderMaterial.color = color;
        if (_borderMaterial.HasProperty("_BaseColor"))
        {
            _borderMaterial.SetColor("_BaseColor", color);
        }

        if (_borderMaterial.HasProperty("_Color"))
        {
            _borderMaterial.SetColor("_Color", color);
        }
    }

    private Color ResolveBorderColor()
    {
        return IsAttachedEndpoint() ? attachedBorderColor : unattachedBorderColor;
    }

    private bool IsAttachedEndpoint()
    {
        return owner != null
               && owner.TryGetEndpointAttachment(endpoint, out _, out _);
    }

    private static void MarkAnyInteraction()
    {
        _lastAnyInteractionFrame = Time.frameCount;
    }

    private static Material CreateHandleMaterial(Color color)
    {
        var shader = Shader.Find("MRBlueprint/RayNoStackTransparent")
                     ?? Shader.Find("Universal Render Pipeline/Unlit")
                     ?? Shader.Find("Sprites/Default")
                     ?? Shader.Find("Unlit/Color")
                     ?? Shader.Find("Standard");
        if (shader == null)
        {
            return null;
        }

        var material = new Material(shader)
        {
            name = "PhysicsDrawingEndpointHandle",
            color = color,
            enableInstancing = true
        };

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }

        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
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
        material.renderQueue = (int)RenderQueue.Transparent + 30;
        return material;
    }

    private static Material CreateBorderMaterial(Color color)
    {
        var shader = Shader.Find("Universal Render Pipeline/Unlit")
                     ?? Shader.Find("Sprites/Default")
                     ?? Shader.Find("Unlit/Color")
                     ?? Shader.Find("Standard");
        if (shader == null)
        {
            return null;
        }

        var material = new Material(shader)
        {
            name = "PhysicsDrawingEndpointBorder",
            color = color,
            enableInstancing = true
        };

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }

        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
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
        material.renderQueue = (int)RenderQueue.Transparent + 29;
        return material;
    }

    private static Mesh GetOrCreateSphereMesh()
    {
        if (_sphereMesh != null)
        {
            return _sphereMesh;
        }

        var longitude = Mathf.Max(8, SphereSegments);
        var latitude = Mathf.Max(4, SphereSegments / 2);
        var ringCount = latitude - 1;
        var vertices = new Vector3[2 + ringCount * longitude];
        var triangles = new int[longitude * (2 + Mathf.Max(0, ringCount - 1) * 2) * 3];

        vertices[0] = Vector3.up * 0.5f;
        var vertexIndex = 1;
        for (var lat = 1; lat < latitude; lat++)
        {
            var theta = Mathf.PI * lat / latitude;
            var y = Mathf.Cos(theta) * 0.5f;
            var radius = Mathf.Sin(theta) * 0.5f;
            for (var lon = 0; lon < longitude; lon++)
            {
                var phi = Mathf.PI * 2f * lon / longitude;
                vertices[vertexIndex++] = new Vector3(
                    Mathf.Cos(phi) * radius,
                    y,
                    Mathf.Sin(phi) * radius);
            }
        }

        var bottomIndex = vertices.Length - 1;
        vertices[bottomIndex] = Vector3.down * 0.5f;
        var triangleIndex = 0;

        for (var lon = 0; lon < longitude; lon++)
        {
            var next = (lon + 1) % longitude;
            triangles[triangleIndex++] = 0;
            triangles[triangleIndex++] = 1 + next;
            triangles[triangleIndex++] = 1 + lon;
        }

        for (var ring = 0; ring < ringCount - 1; ring++)
        {
            var currentRing = 1 + ring * longitude;
            var nextRing = currentRing + longitude;
            for (var lon = 0; lon < longitude; lon++)
            {
                var next = (lon + 1) % longitude;
                triangles[triangleIndex++] = currentRing + lon;
                triangles[triangleIndex++] = nextRing + next;
                triangles[triangleIndex++] = nextRing + lon;
                triangles[triangleIndex++] = currentRing + lon;
                triangles[triangleIndex++] = currentRing + next;
                triangles[triangleIndex++] = nextRing + next;
            }
        }

        var lastRing = 1 + (ringCount - 1) * longitude;
        for (var lon = 0; lon < longitude; lon++)
        {
            var next = (lon + 1) % longitude;
            triangles[triangleIndex++] = bottomIndex;
            triangles[triangleIndex++] = lastRing + lon;
            triangles[triangleIndex++] = lastRing + next;
        }

        _sphereMesh = new Mesh
        {
            name = "PhysicsDrawingEndpointSphere"
        };
        _sphereMesh.vertices = vertices;
        _sphereMesh.triangles = triangles;
        _sphereMesh.RecalculateBounds();
        _sphereMesh.RecalculateNormals();
        return _sphereMesh;
    }

    private sealed class RayDragState
    {
        public PhysicsDrawingEndpointHandle Handle;
        public float Distance;
        public Vector3 GrabPoint;
        public bool IsDirect;
    }
}
