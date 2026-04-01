using UnityEngine;

public class PlaceableAsset : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Renderer[] targetRenderers;
    [SerializeField] private Rigidbody rb;

    [Header("Optional")]
    [SerializeField] private string assetDisplayName = "Asset";

    public string AssetDisplayName => assetDisplayName;
    public Rigidbody Rigidbody => rb;

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

    public bool GetUseGravity()
    {
        return rb != null && rb.useGravity;
    }

    public void SetUseGravity(bool useGravity)
    {
        if (rb != null)
        {
            rb.useGravity = useGravity;
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