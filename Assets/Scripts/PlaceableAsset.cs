using UnityEngine;

public class PlaceableAsset : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Renderer[] targetRenderers;
    [SerializeField] private Rigidbody rb;

    [Header("Optional")]
    [SerializeField] private string assetDisplayName = "Asset";

    [Tooltip("Inspector gravity toggle — applied to rigidbody while simulating; in edit mode gravity may be forced off.")]
    [SerializeField] private bool gravityWhenSimulating = true;
    [SerializeField, Range(0f, 1f)] private float friction = 0.5f;
    [SerializeField, Range(0f, 1f)] private float restitution;

    private Collider[] _colliders;
    private PhysicsMaterial _runtimePhysicsMaterial;

    public string AssetDisplayName => assetDisplayName;
    public Rigidbody Rigidbody => rb;

    /// <summary>Used when building placeables from code (e.g. drawer catalog) before user edits the inspector.</summary>
    public void SetAssetDisplayNameForRuntime(string displayName) => assetDisplayName = displayName;

    private void Reset()
    {
        rb = GetComponent<Rigidbody>();

        if (targetRenderers == null || targetRenderers.Length == 0)
        {
            targetRenderers = GetComponentsInChildren<Renderer>();
        }
    }

    private void Awake()
    {
        if (rb == null)
        {
            rb = GetComponent<Rigidbody>();
        }

        if (targetRenderers == null || targetRenderers.Length == 0)
        {
            targetRenderers = GetComponentsInChildren<Renderer>();
        }

        EnsureScaleAwareColliders();
        CacheColliders();

        if (rb != null)
            gravityWhenSimulating = rb.useGravity;

        ApplyPhysicsMaterialToColliders();
        RefreshRigidbodyMassProperties();
    }

    private void Start()
    {
        ApplySandboxGravityPolicy();
    }

    private void OnDestroy()
    {
        if (_runtimePhysicsMaterial != null)
        {
            Destroy(_runtimePhysicsMaterial);
        }

        if (AssetSelectionManager.Instance != null && AssetSelectionManager.Instance.SelectedAsset == this)
        {
            AssetSelectionManager.Instance.ClearSelection();
        }
    }

    public Renderer[] GetRenderers()
    {
        return targetRenderers;
    }

    public Color GetColor()
    {
        if (targetRenderers.Length == 0 || targetRenderers[0] == null)
        {
            return Color.white;
        }

        return targetRenderers[0].material.color;
    }

    public void SetColor(Color color)
    {
        foreach (Renderer rend in targetRenderers)
        {
            if (rend != null)
            {
                rend.material.color = color;
            }
        }
    }

    public float GetAlpha()
    {
        return GetColor().a;
    }

    public void SetAlpha(float alpha)
    {
        alpha = Mathf.Clamp01(alpha);

        foreach (Renderer rend in targetRenderers)
        {
            if (rend != null)
            {
                Color c = rend.material.color;
                c.a = alpha;
                rend.material.color = c;
            }
        }
    }

    public Vector3 GetScale()
    {
        return transform.localScale;
    }

    public void SetScale(Vector3 newScale)
    {
        EnsureScaleAwareColliders();
        transform.localScale = newScale;
        RefreshPhysicsShape();
    }

    public Vector3 GetPosition()
    {
        return transform.position;
    }

    public void SetPosition(Vector3 newPosition)
    {
        transform.position = newPosition;
        Physics.SyncTransforms();
    }

    public Quaternion GetRotation()
    {
        return transform.rotation;
    }

    public Vector3 GetRotationEuler()
    {
        return transform.eulerAngles;
    }

    public void SetRotationEuler(Vector3 newEuler)
    {
        transform.rotation = Quaternion.Euler(newEuler);
        Physics.SyncTransforms();
    }

    public void SetPose(Vector3 newPosition, Quaternion newRotation)
    {
        if (rb != null)
        {
            rb.position = newPosition;
            rb.rotation = newRotation;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        transform.SetPositionAndRotation(newPosition, newRotation);
        Physics.SyncTransforms();
    }

    public void SnapUprightPreserveYaw()
    {
        var yaw = transform.eulerAngles.y;
        SetPose(transform.position, Quaternion.Euler(0f, yaw, 0f));
    }

    /// <summary>
    /// Spins the object around world +Y (good for floor-placed props). Keeps the rigidbody in sync when present.
    /// </summary>
    public void RotateWorldY(float degrees)
    {
        var q = Quaternion.AngleAxis(degrees, Vector3.up) * transform.rotation;
        SetPose(transform.position, q);
    }

    /// <summary>User intent for gravity during simulation (inspector toggle).</summary>
    public bool GetUseGravity() => gravityWhenSimulating;

    public void SetUseGravity(bool useGravity)
    {
        gravityWhenSimulating = useGravity;
        ApplySandboxGravityPolicy();
    }

    /// <summary>Re-apply edit vs simulate gravity rules (called when sim state changes or object spawns).</summary>
    public void ApplySandboxGravityPolicy()
    {
        if (rb == null)
            return;

        var sim = SandboxSimulationController.Instance;
        if (sim == null)
        {
            rb.useGravity = gravityWhenSimulating;
            return;
        }

        if (sim.IsSimulating && !sim.IsPaused)
        {
            rb.useGravity = gravityWhenSimulating;
        }
        else if (sim.IsSimulating && sim.IsPaused)
        {
            rb.useGravity = gravityWhenSimulating;
        }
        else if (!sim.IsSimulating && sim.ZeroGravityInEdit)
        {
            rb.useGravity = false;
        }
        else
        {
            rb.useGravity = gravityWhenSimulating;
        }
    }

    public float GetMass()
    {
        return rb != null ? rb.mass : 1f;
    }

    public void SetMass(float mass)
    {
        if (rb != null)
        {
            rb.mass = Mathf.Max(0.01f, mass);
        }
    }

    public float GetFriction()
    {
        return Mathf.Clamp01(friction);
    }

    public float GetDynamicFrictionCoefficient()
    {
        return Mathf.Lerp(0f, 1.25f, GetFriction());
    }

    public float GetStaticFrictionCoefficient()
    {
        return Mathf.Lerp(0f, 1.5f, GetFriction());
    }

    public void SetFriction(float value)
    {
        friction = Mathf.Clamp01(value);
        ApplyPhysicsMaterialToColliders();
    }

    public float GetRestitution()
    {
        return Mathf.Clamp01(restitution);
    }

    public float GetRestitutionCoefficient()
    {
        return GetRestitution();
    }

    public void SetRestitution(float value)
    {
        restitution = Mathf.Clamp01(value);
        ApplyPhysicsMaterialToColliders();
    }

    private void CacheColliders()
    {
        if (_colliders == null || _colliders.Length == 0)
        {
            _colliders = GetComponentsInChildren<Collider>();
        }
    }

    private void EnsureScaleAwareColliders()
    {
        var sphereColliders = GetComponentsInChildren<SphereCollider>(true);
        for (var i = 0; i < sphereColliders.Length; i++)
        {
            var sphere = sphereColliders[i];
            if (sphere == null || sphere.isTrigger || !sphere.enabled)
            {
                continue;
            }

            if (sphere.GetComponent<MeshCollider>() != null)
            {
                continue;
            }

            var meshFilter = sphere.GetComponent<MeshFilter>();
            if (meshFilter == null || meshFilter.sharedMesh == null)
            {
                continue;
            }

            var meshCollider = sphere.gameObject.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = meshFilter.sharedMesh;
            meshCollider.convex = true;
            meshCollider.sharedMaterial = sphere.sharedMaterial;

            sphere.enabled = false;
            if (Application.isPlaying)
            {
                Destroy(sphere);
            }
            else
            {
                DestroyImmediate(sphere);
            }

            _colliders = null;
        }
    }

    private void ApplyPhysicsMaterialToColliders()
    {
        CacheColliders();
        if (_colliders == null || _colliders.Length == 0)
        {
            return;
        }

        if (_runtimePhysicsMaterial == null)
        {
            _runtimePhysicsMaterial = new PhysicsMaterial(name + "_RuntimePhysics");
        }

        _runtimePhysicsMaterial.dynamicFriction = GetDynamicFrictionCoefficient();
        _runtimePhysicsMaterial.staticFriction = GetStaticFrictionCoefficient();
        _runtimePhysicsMaterial.bounciness = GetRestitutionCoefficient();
        _runtimePhysicsMaterial.frictionCombine = PhysicsMaterialCombine.Minimum;
        _runtimePhysicsMaterial.bounceCombine = PhysicsMaterialCombine.Maximum;

        for (var i = 0; i < _colliders.Length; i++)
        {
            var collider = _colliders[i];
            if (collider == null || !collider.enabled || collider.isTrigger)
            {
                continue;
            }

            collider.sharedMaterial = _runtimePhysicsMaterial;
        }
    }

    private void RefreshPhysicsShape()
    {
        Physics.SyncTransforms();
        RefreshRigidbodyMassProperties();
    }

    private void RefreshRigidbodyMassProperties()
    {
        if (rb == null)
        {
            return;
        }

        rb.ResetCenterOfMass();
        rb.ResetInertiaTensor();
        rb.WakeUp();
    }

    public PlaceableAsset Duplicate()
    {
        GameObject clone = Instantiate(gameObject, transform.position + new Vector3(0.2f, 0f, 0.2f), transform.rotation);
        clone.name = gameObject.name.Replace("(Clone)", "").Trim() + "_Copy";

        PlaceableAsset cloneAsset = clone.GetComponent<PlaceableAsset>();
        return cloneAsset;
    }

    public void Delete()
    {
        Destroy(gameObject);
    }
}
