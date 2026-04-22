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

    [Header("Audio")]
    [Tooltip("One-shot when a drawer item is spawned into the scene.")]
    [SerializeField] private AudioClip spawnSoundClip;
    [SerializeField, Range(0f, 1f)] private float spawnSoundVolume = 1f;

    [Header("Placement")]
    [SerializeField] private float maxRaycastDistance = 24f;
    [Tooltip("Selection ray reach used to keep drawer spawns reachable after placement is resolved.")]
    [SerializeField] private float reachableRayDistance = 8f;
    [Tooltip("Extra distance kept between the estimated object bounds and the end of the selection ray.")]
    [SerializeField] private float reachableSpawnPadding = 0.25f;
    [Tooltip("Raycast mask for the sandbox floor (or other valid placement surfaces).")]
    [SerializeField] private LayerMask placementMask;
    [SerializeField] private float fallbackGroundY = 0f;
    [Tooltip("Spawn Y offset so ~1m primitives (pivot at center) rest on the floor.")]
    [SerializeField] private float clearanceAboveFloor = 0.5f;
    [SerializeField] private float surfaceBias = 0.02f;
    [Tooltip("When the view ray does not hit the ground plane in front (e.g. looking at horizon), step this far on XZ from the camera.")]
    [SerializeField] private float horizontalFallbackDistance = 1.8f;

    private Camera _resolvedCamera;

    public float FallbackGroundY
    {
        get => fallbackGroundY;
        set => fallbackGroundY = value;
    }

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

        var spawnPos = ResolveSpawnPosition(drawerItem.SpawnPrefab);
        var instance = Instantiate(drawerItem.SpawnPrefab, spawnPos, Quaternion.identity);
        instance.SetActive(true);
        EnsureTelemetryFeatures(instance);

        if (spawnSoundClip != null && !UiMenuSelectSoundHub.SoundEffectsMuted)
            AudioSource.PlayClipAtPoint(spawnSoundClip, spawnPos, spawnSoundVolume);

        var templateMarker = instance.GetComponent<SpawnTemplateMarker>();
        if (templateMarker != null)
            Destroy(templateMarker);

        foreach (var rb in instance.GetComponentsInChildren<Rigidbody>())
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        if (AssetSelectionManager.Instance != null)
        {
            AssetSelectionManager.Instance.ClearSelection();
        }
    }

    private static void EnsureTelemetryFeatures(GameObject instance)
    {
        if (instance == null)
        {
            return;
        }

        PhysicsLensManager.EnsureRuntimeManager();
        SimulationVisualizationInstaller.EnsureRuntimeManager();

        var rigidbodies = instance.GetComponentsInChildren<Rigidbody>(true);
        for (var i = 0; i < rigidbodies.Length; i++)
        {
            var rb = rigidbodies[i];
            if (rb == null)
            {
                continue;
            }

            CollisionEventCache.GetOrAdd(rb);
            if (rb.GetComponent<PhysicsLensForceEventCache>() == null)
            {
                rb.gameObject.AddComponent<PhysicsLensForceEventCache>();
            }
        }
    }

    private Vector3 ResolveSpawnPosition(GameObject spawnPrefab)
    {
        if (spawnPoint != null)
        {
            return spawnPoint.position;
        }

        var ray = BuildViewportCenterRay();
        var origin = ray.origin;
        var dir = ray.direction;
        var maxCenterDistance = ResolveReachableCenterDistance(spawnPrefab);

        if (placementMask.value != 0 &&
            Physics.Raycast(ray, out var hit, maxRaycastDistance, placementMask, QueryTriggerInteraction.Ignore))
        {
            var n = hit.normal.sqrMagnitude > 0.0001f ? hit.normal.normalized : Vector3.up;
            return ClampSpawnPositionToReach(
                hit.point + n * surfaceBias + Vector3.up * clearanceAboveFloor,
                origin,
                maxCenterDistance);
        }

        const float eps = 1e-4f;
        if (Mathf.Abs(dir.y) > eps)
        {
            var t = (fallbackGroundY - origin.y) / dir.y;
            if (t > 0.01f && t < maxRaycastDistance)
            {
                var p = origin + dir * t;
                return ClampSpawnPositionToReach(
                    new Vector3(p.x, fallbackGroundY + clearanceAboveFloor + surfaceBias, p.z),
                    origin,
                    maxCenterDistance);
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
        return ClampSpawnPositionToReach(
            new Vector3(xz.x, fallbackGroundY + clearanceAboveFloor + surfaceBias, xz.z),
            origin,
            maxCenterDistance);
    }

    private float ResolveReachableCenterDistance(GameObject spawnPrefab)
    {
        var reach = Mathf.Max(0.5f, reachableRayDistance);
        var boundsRadius = EstimatePrefabReachRadius(spawnPrefab);
        return Mathf.Max(0.5f, reach - boundsRadius - Mathf.Max(0f, reachableSpawnPadding));
    }

    private Vector3 ClampSpawnPositionToReach(Vector3 position, Vector3 rayOrigin, float maxDistance)
    {
        maxDistance = Mathf.Max(0.5f, maxDistance);
        var offset = position - rayOrigin;
        if (offset.sqrMagnitude <= maxDistance * maxDistance)
        {
            return position;
        }

        var vertical = position.y - rayOrigin.y;
        var horizontal = new Vector2(offset.x, offset.z);
        if (horizontal.sqrMagnitude <= 0.0001f)
        {
            return rayOrigin + offset.normalized * maxDistance;
        }

        var maxHorizontal = Mathf.Sqrt(Mathf.Max(0.01f, maxDistance * maxDistance - vertical * vertical));
        if (horizontal.sqrMagnitude <= maxHorizontal * maxHorizontal)
        {
            return position;
        }

        var clampedHorizontal = horizontal.normalized * maxHorizontal;
        return new Vector3(rayOrigin.x + clampedHorizontal.x, position.y, rayOrigin.z + clampedHorizontal.y);
    }

    private static float EstimatePrefabReachRadius(GameObject spawnPrefab)
    {
        if (spawnPrefab == null)
        {
            return 0.5f;
        }

        var root = spawnPrefab.transform;
        var maxSq = 0f;
        var filters = spawnPrefab.GetComponentsInChildren<MeshFilter>(true);
        for (var i = 0; i < filters.Length; i++)
        {
            var filter = filters[i];
            if (filter == null || filter.sharedMesh == null)
            {
                continue;
            }

            EncapsulateMeshBounds(filter.sharedMesh.bounds, filter.transform, root, ref maxSq);
        }

        return maxSq > 0.0001f ? Mathf.Sqrt(maxSq) : 0.5f;
    }

    private static void EncapsulateMeshBounds(Bounds bounds, Transform meshTransform, Transform root, ref float maxSq)
    {
        if (meshTransform == null || root == null)
        {
            return;
        }

        var min = bounds.min;
        var max = bounds.max;
        for (var x = 0; x < 2; x++)
        {
            for (var y = 0; y < 2; y++)
            {
                for (var z = 0; z < 2; z++)
                {
                    var corner = new Vector3(
                        x == 0 ? min.x : max.x,
                        y == 0 ? min.y : max.y,
                        z == 0 ? min.z : max.z);
                    var offset = meshTransform.TransformPoint(corner) - root.position;
                    maxSq = Mathf.Max(maxSq, offset.sqrMagnitude);
                }
            }
        }
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
