using UnityEngine;

/// <summary>
/// Spawns drawer prefabs at a stable placement point (viewport ray vs sandbox floor), not mid-air in front of the camera.
/// </summary>
public class XRDrawerSpawner : MonoBehaviour
{
    [Header("References")]
    [Tooltip("If set, this world position is used and other placement logic is skipped.")]
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private Transform xrCamera;
    [Tooltip("Used for viewport-center ray; falls back to Camera on xrCamera or Camera.main.")]
    [SerializeField] private Camera placementCamera;

    [Header("Placement")]
    [SerializeField] private float maxRaycastDistance = 24f;
    [Tooltip("Raycast mask for the sandbox floor (or other valid placement surfaces).")]
    [SerializeField] private LayerMask placementMask;
    [SerializeField] private float fallbackGroundY = 0f;
    [Tooltip("Spawn Y offset so ~1m primitives (pivot at center) rest on the floor.")]
    [SerializeField] private float clearanceAboveFloor = 0.5f;
    [SerializeField] private float surfaceBias = 0.02f;
    [Tooltip("When the view ray does not hit the ground plane in front (e.g. looking at horizon), step this far on XZ from the camera.")]
    [SerializeField] private float horizontalFallbackDistance = 1.8f;

    private Camera _resolvedCamera;

    private void Awake()
    {
        _resolvedCamera = placementCamera != null
            ? placementCamera
            : xrCamera != null
                ? xrCamera.GetComponent<Camera>()
                : null;

        if (_resolvedCamera == null)
        {
            _resolvedCamera = Camera.main;
        }
    }

    public void SpawnFromDrawerItem(XRDrawerItem drawerItem)
    {
        if (drawerItem == null || drawerItem.SpawnPrefab == null)
        {
            return;
        }

        var spawnPos = ResolveSpawnPosition();
        var instance = Instantiate(drawerItem.SpawnPrefab, spawnPos, Quaternion.identity);

        foreach (var rb in instance.GetComponentsInChildren<Rigidbody>())
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        var placeable = instance.GetComponent<PlaceableAsset>()
                        ?? instance.GetComponentInChildren<PlaceableAsset>(true);
        if (placeable != null && AssetSelectionManager.Instance != null)
            AssetSelectionManager.Instance.SelectAsset(placeable);
    }

    private Vector3 ResolveSpawnPosition()
    {
        if (spawnPoint != null)
        {
            return spawnPoint.position;
        }

        var ray = BuildViewportCenterRay();
        var origin = ray.origin;
        var dir = ray.direction;

        if (placementMask.value != 0 &&
            Physics.Raycast(ray, out var hit, maxRaycastDistance, placementMask, QueryTriggerInteraction.Ignore))
        {
            var n = hit.normal.sqrMagnitude > 0.0001f ? hit.normal.normalized : Vector3.up;
            return hit.point + n * surfaceBias + Vector3.up * clearanceAboveFloor;
        }

        const float eps = 1e-4f;
        if (Mathf.Abs(dir.y) > eps)
        {
            var t = (fallbackGroundY - origin.y) / dir.y;
            if (t > 0.01f && t < maxRaycastDistance)
            {
                var p = origin + dir * t;
                return new Vector3(p.x, fallbackGroundY + clearanceAboveFloor + surfaceBias, p.z);
            }
        }

        var flat = dir;
        flat.y = 0f;
        if (flat.sqrMagnitude < eps)
        {
            flat = Vector3.forward;
        }

        flat.Normalize();
        var xz = origin + flat * horizontalFallbackDistance;
        return new Vector3(xz.x, fallbackGroundY + clearanceAboveFloor + surfaceBias, xz.z);
    }

    private Ray BuildViewportCenterRay()
    {
        if (_resolvedCamera != null)
        {
            return _resolvedCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        }

        if (xrCamera != null)
        {
            return new Ray(xrCamera.position, xrCamera.forward);
        }

        return new Ray(Vector3.zero, Vector3.forward);
    }
}
