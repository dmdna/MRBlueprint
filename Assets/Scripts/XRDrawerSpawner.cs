using UnityEngine;

public class XRDrawerSpawner : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private Transform xrCamera;

    [Header("Spawn Settings")]
    [SerializeField] private float spawnDistanceFromCamera = 1.2f;

    public void SpawnFromDrawerItem(XRDrawerItem drawerItem)
    {
        if (drawerItem == null || drawerItem.SpawnPrefab == null) return;

        Vector3 spawnPos;

        if (spawnPoint != null)
        {
            spawnPos = spawnPoint.position;
        }
        else if (xrCamera != null)
        {
            spawnPos = xrCamera.position + xrCamera.forward * spawnDistanceFromCamera;
        }
        else
        {
            spawnPos = Vector3.zero;
        }

        Instantiate(drawerItem.SpawnPrefab, spawnPos, Quaternion.identity);
    }
}