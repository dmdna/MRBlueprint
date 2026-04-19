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

        if (rb != null)
            gravityWhenSimulating = rb.useGravity;
    }

    private void Start()
    {
        ApplySandboxGravityPolicy();
    }

    private void OnDestroy()
    {
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
        transform.localScale = newScale;
    }

    public Vector3 GetPosition()
    {
        return transform.position;
    }

    public void SetPosition(Vector3 newPosition)
    {
        transform.position = newPosition;
    }

    public Vector3 GetRotationEuler()
    {
        return transform.eulerAngles;
    }

    public void SetRotationEuler(Vector3 newEuler)
    {
        transform.rotation = Quaternion.Euler(newEuler);
    }

    /// <summary>
    /// Spins the object around world +Y (good for floor-placed props). Keeps the rigidbody in sync when present.
    /// </summary>
    public void RotateWorldY(float degrees)
    {
        var q = Quaternion.AngleAxis(degrees, Vector3.up) * transform.rotation;
        if (rb != null)
        {
            rb.MoveRotation(q);
            rb.angularVelocity = Vector3.zero;
        }
        else
            transform.rotation = q;
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