using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Editor-style move / rotate / scale handles for the selected <see cref="PlaceableAsset"/> (mouse path).
/// Lives on a dedicated layer; <see cref="SandboxEditorInputRouter"/> tries this before body drags.
/// </summary>
public sealed class PlaceableTransformGizmo : MonoBehaviour
{
    private const string GizmoLayerName = "TransformGizmo";
    /// <summary>Matches SkySandbox TagManager user slot after UI when the named layer is missing at runtime.</summary>
    private const int FallbackGizmoLayerIndex = 6;

    [SerializeField] private Camera scaleReferenceCamera;
    [SerializeField] private float moveSensitivity = 1f;
    [SerializeField] private float rotateSensitivity = 1f;
    [SerializeField] private float scaleSensitivity = 0.35f;
    [SerializeField] private float minScaleAxis = 0.08f;
    [Tooltip("Extra world-space clearance so handles sit outside the mesh, not inside large scaled objects.")]
    [SerializeField] private float boundsSurfacePadding = 0.18f;
    [Tooltip("Local length of move arrows in gizmo space (must match BuildHandles).")]
    [SerializeField] private float moveArrowLocalLength = 0.52f;
    [Tooltip("Smooth uniform scale so distance/bounds changes do not pop every frame (XR head jitter, physics).")]
    [SerializeField] private float gizmoScaleSmoothTime = 0.09f;
    [Tooltip("Smooth renderer bounds extent before driving scale (reduces micro-jitter while the rigidbody settles).")]
    [SerializeField] private float boundsExtentSmoothTime = 0.14f;

    private int _gizmoLayer = -1;
    private GameObject _visualRoot;
    private Transform _target;
    private Rigidbody _targetRb;
    private PlaceableAsset _placeableAsset;

    private GizmoHandleKind? _activeKind;
    private Vector3 _dragLastWorld;
    private Vector3 _rotatePrevDir;

    private PlaceableAsset _lastSmoothingSelection;
    private float _smoothedUniformScale = 1f;
    private float _uniformScaleVel;
    private float _smoothedMaxExtent = 0.25f;
    private float _maxExtentVel;

    private static Material CreateHandleMaterial(Color color, float alpha = 0.58f)
    {
        // Unlit is reliable for small runtime primitives (no lighting / URP surface setup issues).
        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");

        var m = new Material(shader);
        var c = new Color(color.r, color.g, color.b, alpha);
        if (m.HasProperty("_BaseColor"))
            m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color"))
            m.SetColor("_Color", c);
        m.color = c;

        if (alpha < 0.99f)
        {
            m.renderQueue = 3000;
            m.SetOverrideTag("RenderType", "Transparent");
            if (m.HasProperty("_Surface"))
                m.SetFloat("_Surface", 1f);
            if (m.HasProperty("_Blend"))
                m.SetFloat("_Blend", 0f);
            if (m.HasProperty("_SrcBlend"))
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (m.HasProperty("_DstBlend"))
                m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty("_ZWrite"))
                m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.EnableKeyword("_ALPHABLEND_ON");
        }
        else
        {
            m.renderQueue = 2450;
        }

        return m;
    }

    private static void ConfigureGizmoRenderer(MeshRenderer mr)
    {
        if (mr == null)
            return;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
#if UNITY_2022_1_OR_NEWER
        mr.allowOcclusionWhenDynamic = false;
#endif
    }

    private void Awake()
    {
        _gizmoLayer = LayerMask.NameToLayer(GizmoLayerName);
        if (_gizmoLayer < 0)
        {
            Debug.LogWarning(
                $"Layer '{GizmoLayerName}' not found by name; using layer index {FallbackGizmoLayerIndex}. " +
                "Add the named layer under Edit > Project Settings > Tags and Layers to avoid conflicts.");
            _gizmoLayer = FallbackGizmoLayerIndex;
        }

        _visualRoot = new GameObject("RuntimeTransformGizmo");
        _visualRoot.transform.SetParent(transform, false);
        _visualRoot.SetActive(false);

        BuildHandles();
    }

    private void OnEnable()
    {
        RegisterSelectionListener();
        RefreshFromSelection();
    }

    private void Start()
    {
        RegisterSelectionListener();
        RefreshFromSelection();
    }

    private void OnDisable()
    {
        if (AssetSelectionManager.Instance != null)
            AssetSelectionManager.Instance.OnSelectionChanged -= OnSelectionChanged;
        EndDragInternal();
        if (_visualRoot != null)
            _visualRoot.SetActive(false);
    }

    private void RegisterSelectionListener()
    {
        if (AssetSelectionManager.Instance == null)
            return;
        AssetSelectionManager.Instance.OnSelectionChanged -= OnSelectionChanged;
        AssetSelectionManager.Instance.OnSelectionChanged += OnSelectionChanged;
    }

    private void RefreshFromSelection()
    {
        OnSelectionChanged(AssetSelectionManager.Instance != null ? AssetSelectionManager.Instance.SelectedAsset : null);
    }

    private void OnSelectionChanged(PlaceableAsset asset)
    {
        _placeableAsset = asset;
        _target = asset != null ? asset.transform : null;
        _targetRb = asset != null ? asset.Rigidbody : null;
        var show = asset != null;
        if (_visualRoot != null)
            _visualRoot.SetActive(show);

        if (!show)
            _lastSmoothingSelection = null;
    }

    private void LateUpdate()
    {
        if (_target == null || _visualRoot == null || !_visualRoot.activeSelf)
            return;

        _visualRoot.transform.position = _target.position;
        _visualRoot.transform.rotation = _target.rotation;

        var cam = PickScaleCamera();
        float distanceScale = 0.75f;
        if (cam != null)
        {
            float d = Vector3.Distance(cam.transform.position, _target.position);
            distanceScale = Mathf.Clamp(d * 0.12f, 0.5f, 4f);
        }

        float rawExtent = GetSelectionMaxWorldExtent(_placeableAsset);
        var selectionChanged = _placeableAsset != _lastSmoothingSelection;
        if (selectionChanged)
        {
            _lastSmoothingSelection = _placeableAsset;
            _smoothedMaxExtent = rawExtent;
            _maxExtentVel = 0f;
        }
        else
        {
            float extSmooth = Mathf.Max(0.0001f, boundsExtentSmoothTime);
            _smoothedMaxExtent = Mathf.SmoothDamp(
                _smoothedMaxExtent, rawExtent, ref _maxExtentVel, extSmooth,
                Mathf.Infinity, Time.deltaTime);
        }

        float reach = Mathf.Max(moveArrowLocalLength, 1e-4f);
        float boundsScale = (_smoothedMaxExtent + boundsSurfacePadding) / reach;
        float targetUniform = Mathf.Max(distanceScale, boundsScale);
        targetUniform = Mathf.Clamp(targetUniform, 0.35f, 120f);

        if (selectionChanged)
        {
            _smoothedUniformScale = targetUniform;
            _uniformScaleVel = 0f;
        }
        else
        {
            float scaleSmooth = Mathf.Max(0.0001f, gizmoScaleSmoothTime);
            _smoothedUniformScale = Mathf.SmoothDamp(
                _smoothedUniformScale, targetUniform, ref _uniformScaleVel, scaleSmooth,
                Mathf.Infinity, Time.deltaTime);
        }

        _visualRoot.transform.localScale = Vector3.one * _smoothedUniformScale;
    }

    private static float GetSelectionMaxWorldExtent(PlaceableAsset p)
    {
        if (p == null)
            return 0.25f;

        var rends = p.GetRenderers();
        if (rends == null || rends.Length == 0)
            return 0.25f;

        var has = false;
        var b = new Bounds(p.transform.position, Vector3.zero);
        foreach (var r in rends)
        {
            if (r == null)
                continue;
            if (!has)
            {
                b = r.bounds;
                has = true;
            }
            else
                b.Encapsulate(r.bounds);
        }

        if (!has)
            return 0.25f;

        return Mathf.Max(Mathf.Max(b.extents.x, b.extents.y), b.extents.z);
    }

    private Camera PickScaleCamera()
    {
        if (scaleReferenceCamera != null && scaleReferenceCamera.isActiveAndEnabled)
            return scaleReferenceCamera;
        if (Camera.main != null && Camera.main.isActiveAndEnabled)
            return Camera.main;
        var cameras = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (var c in cameras)
        {
            if (c != null && c.isActiveAndEnabled)
                return c;
        }

        return null;
    }

    private void BuildHandles()
    {
        var red = CreateHandleMaterial(new Color(0.95f, 0.25f, 0.2f));
        var green = CreateHandleMaterial(new Color(0.35f, 0.85f, 0.35f));
        var blue = CreateHandleMaterial(new Color(0.35f, 0.5f, 0.95f));
        var yellow = CreateHandleMaterial(new Color(0.95f, 0.85f, 0.2f));
        var cyan = CreateHandleMaterial(new Color(0.25f, 0.75f, 0.85f), 0.32f);
        var magenta = CreateHandleMaterial(new Color(0.85f, 0.35f, 0.75f), 0.32f);
        var lime = CreateHandleMaterial(new Color(0.55f, 0.85f, 0.35f), 0.32f);

        AddMoveArrow("MoveX", Vector3.right, red, GizmoHandleKind.MoveX);
        AddMoveArrow("MoveY", Vector3.up, green, GizmoHandleKind.MoveY);
        AddMoveArrow("MoveZ", Vector3.forward, blue, GizmoHandleKind.MoveZ);

        AddRotateDisc("RotX", Vector3.right, cyan, GizmoHandleKind.RotateX);
        AddRotateDisc("RotY", Vector3.up, magenta, GizmoHandleKind.RotateY);
        AddRotateDisc("RotZ", Vector3.forward, lime, GizmoHandleKind.RotateZ);

        AddScaleCube("ScaleX", Vector3.right * 0.62f, yellow, GizmoHandleKind.ScaleX);
        AddScaleCube("ScaleY", Vector3.up * 0.62f, yellow, GizmoHandleKind.ScaleY);
        AddScaleCube("ScaleZ", Vector3.forward * 0.62f, yellow, GizmoHandleKind.ScaleZ);
    }

    private void AddMoveArrow(string name, Vector3 axis, Material mat, GizmoHandleKind kind)
    {
        float len = 0.52f;
        float thick = 0.035f;
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.layer = _gizmoLayer;
        go.transform.SetParent(_visualRoot.transform, false);
        go.transform.localScale = new Vector3(len, thick, thick);
        go.transform.localRotation = Quaternion.FromToRotation(Vector3.right, axis);
        go.transform.localPosition = axis.normalized * (len * 0.5f);
        Destroy(go.GetComponent<Collider>());
        var box = go.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = Vector3.one * 1.1f;
        box.center = Vector3.zero;
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        ConfigureGizmoRenderer(mr);
        go.AddComponent<GizmoHandlePart>().Kind = kind;
    }

    private void AddRotateDisc(string name, Vector3 axisNormal, Material mat, GizmoHandleKind kind)
    {
        float span = 0.72f;
        float thin = 0.012f;
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.layer = _gizmoLayer;
        go.transform.SetParent(_visualRoot.transform, false);
        if (Mathf.Abs(Vector3.Dot(axisNormal, Vector3.right)) > 0.9f)
            go.transform.localScale = new Vector3(thin, span, span);
        else if (Mathf.Abs(Vector3.Dot(axisNormal, Vector3.up)) > 0.9f)
            go.transform.localScale = new Vector3(span, thin, span);
        else
            go.transform.localScale = new Vector3(span, span, thin);
        go.transform.localPosition = Vector3.zero;
        Destroy(go.GetComponent<Collider>());
        var box = go.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = Vector3.one;
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        ConfigureGizmoRenderer(mr);
        go.AddComponent<GizmoHandlePart>().Kind = kind;
    }

    private void AddScaleCube(string name, Vector3 localPos, Material mat, GizmoHandleKind kind)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.layer = _gizmoLayer;
        go.transform.SetParent(_visualRoot.transform, false);
        go.transform.localScale = Vector3.one * 0.11f;
        go.transform.localPosition = localPos;
        Destroy(go.GetComponent<Collider>());
        var box = go.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = Vector3.one * 1.05f;
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        ConfigureGizmoRenderer(mr);
        go.AddComponent<GizmoHandlePart>().Kind = kind;
    }

    public bool IsDragging => _activeKind.HasValue;

    public bool TryBeginDrag(Ray worldRay, float maxDistance, Camera cam)
    {
        if (_target == null || cam == null)
            return false;

        var mask = 1 << _gizmoLayer;
        if (!Physics.Raycast(worldRay, out var hit, maxDistance, mask, QueryTriggerInteraction.Collide))
            return false;

        var part = hit.collider.GetComponent<GizmoHandlePart>();
        if (part == null)
            return false;

        _activeKind = part.Kind;
        SyncRigidbodyFromTransform();

        var pivot = _target.position;
        if (!TryDragPlanePoint(worldRay, cam, pivot, part.Kind, out var worldOnPlane))
            worldOnPlane = hit.point;

        _dragLastWorld = worldOnPlane;

        if (IsRotateKind(part.Kind))
        {
            var axis = AxisWorld(part.Kind);
            _rotatePrevDir = DirOnRotatePlane(worldOnPlane - pivot, axis);
        }

        return true;
    }

    public void Drag(Ray worldRay, Camera cam)
    {
        if (!_activeKind.HasValue || _target == null || cam == null)
            return;

        var kind = _activeKind.Value;
        var pivot = _target.position;

        switch (kind)
        {
            case GizmoHandleKind.MoveX:
            case GizmoHandleKind.MoveY:
            case GizmoHandleKind.MoveZ:
                DragMove(worldRay, cam, pivot, kind);
                break;
            case GizmoHandleKind.RotateX:
            case GizmoHandleKind.RotateY:
            case GizmoHandleKind.RotateZ:
                DragRotate(worldRay, cam, pivot, kind);
                break;
            case GizmoHandleKind.ScaleX:
            case GizmoHandleKind.ScaleY:
            case GizmoHandleKind.ScaleZ:
                DragScale(worldRay, cam, pivot, kind);
                break;
        }

        SyncRigidbodyFromTransform();
    }

    public void EndDrag()
    {
        EndDragInternal();
    }

    private void EndDragInternal()
    {
        _activeKind = null;
    }

    private void DragMove(Ray worldRay, Camera cam, Vector3 pivot, GizmoHandleKind kind)
    {
        var axis = AxisWorld(kind);
        if (!TryDragPlanePoint(worldRay, cam, pivot, kind, out var worldHit))
            return;

        var delta = worldHit - _dragLastWorld;
        var move = Vector3.Project(delta, axis) * moveSensitivity;
        _target.position += move;
        _dragLastWorld = worldHit;
    }

    private void DragRotate(Ray worldRay, Camera cam, Vector3 pivot, GizmoHandleKind kind)
    {
        var axis = AxisWorld(kind);
        if (!TryIntersectRotatePlane(worldRay, pivot, axis, out var hit))
            return;

        var dir = DirOnRotatePlane(hit - pivot, axis);
        if (dir.sqrMagnitude < 1e-8f)
            return;

        var signed = Vector3.SignedAngle(_rotatePrevDir, dir, axis) * rotateSensitivity;
        if (Mathf.Abs(signed) < 1e-5f)
            return;

        _target.RotateAround(pivot, axis, signed);
        _rotatePrevDir = dir;
    }

    private void DragScale(Ray worldRay, Camera cam, Vector3 pivot, GizmoHandleKind kind)
    {
        var axis = AxisWorld(kind);
        if (!TryDragPlanePoint(worldRay, cam, pivot, kind, out var worldHit))
            return;

        var delta = worldHit - _dragLastWorld;
        var along = Vector3.Dot(delta, axis) * scaleSensitivity;
        _dragLastWorld = worldHit;

        var ls = _target.localScale;
        int i = AxisIndex(kind);
        if (i == 0) ls.x = Mathf.Max(minScaleAxis, ls.x + along);
        else if (i == 1) ls.y = Mathf.Max(minScaleAxis, ls.y + along);
        else ls.z = Mathf.Max(minScaleAxis, ls.z + along);
        _target.localScale = ls;
    }

    private static int AxisIndex(GizmoHandleKind k)
    {
        return k switch
        {
            GizmoHandleKind.MoveX or GizmoHandleKind.RotateX or GizmoHandleKind.ScaleX => 0,
            GizmoHandleKind.MoveY or GizmoHandleKind.RotateY or GizmoHandleKind.ScaleY => 1,
            _ => 2,
        };
    }

    private Vector3 AxisWorld(GizmoHandleKind kind)
    {
        int i = AxisIndex(kind);
        var local = i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward;
        return _target.TransformDirection(local);
    }

    private static bool IsRotateKind(GizmoHandleKind k) =>
        k is GizmoHandleKind.RotateX or GizmoHandleKind.RotateY or GizmoHandleKind.RotateZ;

    private bool TryDragPlanePoint(Ray ray, Camera cam, Vector3 pivot, GizmoHandleKind kind, out Vector3 worldPoint)
    {
        worldPoint = default;
        var axis = AxisWorld(kind);
        var forward = cam.transform.forward;
        var planeNormal = Vector3.Cross(axis, forward);
        if (planeNormal.sqrMagnitude < 1e-6f)
            planeNormal = Vector3.Cross(axis, cam.transform.up);
        if (planeNormal.sqrMagnitude < 1e-6f)
            return false;
        planeNormal.Normalize();

        var plane = new Plane(planeNormal, pivot);
        if (!plane.Raycast(ray, out var dist))
            return false;
        worldPoint = ray.GetPoint(dist);
        return true;
    }

    private bool TryIntersectRotatePlane(Ray ray, Vector3 pivot, Vector3 axisNormal, out Vector3 hit)
    {
        hit = default;
        var plane = new Plane(axisNormal.normalized, pivot);
        if (!plane.Raycast(ray, out var dist))
            return false;
        hit = ray.GetPoint(dist);
        return true;
    }

    private static Vector3 DirOnRotatePlane(Vector3 fromPivot, Vector3 axisNormal)
    {
        var v = Vector3.ProjectOnPlane(fromPivot, axisNormal);
        return v.sqrMagnitude > 1e-8f ? v.normalized : Vector3.zero;
    }

    private void SyncRigidbodyFromTransform()
    {
        if (_targetRb == null)
            return;
        _targetRb.position = _target.position;
        _targetRb.rotation = _target.rotation;
        _targetRb.linearVelocity = Vector3.zero;
        _targetRb.angularVelocity = Vector3.zero;
    }
}
