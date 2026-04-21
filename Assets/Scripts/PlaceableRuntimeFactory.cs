using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>Builds runtime placeable roots for procedural drawer shapes (mirrors core prefab components).</summary>
public static class PlaceableRuntimeFactory
{
    public static string GetShapeDisplayName(MrBlueprintPlaceableShapeKind kind) => kind switch
    {
        MrBlueprintPlaceableShapeKind.TriangularPrism => "Triangular Prism",
        MrBlueprintPlaceableShapeKind.HexagonalPrism => "Hexagonal Prism",
        MrBlueprintPlaceableShapeKind.Buckyball => "Buckyball",
        _ => kind.ToString()
    };

    /// <summary>Inactive template parented under <paramref name="templatesParent"/>; used as XR drawer spawn prefab.</summary>
    public static GameObject CreateShapeSpawnTemplate(
        MrBlueprintPlaceableShapeKind kind,
        Material sharedMaterial,
        Transform templatesParent)
    {
        var mesh = MrBlueprintPrimitiveMeshes.GetOrCreate(kind);
        var root = new GameObject($"ShapeSpawn_{kind}");
        root.transform.SetParent(templatesParent, false);

        var mf = root.AddComponent<MeshFilter>();
        mf.sharedMesh = mesh;

        var mr = root.AddComponent<MeshRenderer>();
        mr.sharedMaterial = sharedMaterial;

        var bounds = mesh.bounds;
        var box = root.AddComponent<BoxCollider>();
        box.center = bounds.center;
        box.size = Vector3.Max(bounds.size, new Vector3(0.04f, 0.04f, 0.04f));

        var rb = root.AddComponent<Rigidbody>();
        rb.mass = 1f;
        rb.useGravity = true;

        var placeable = root.AddComponent<PlaceableAsset>();
        placeable.SetAssetDisplayNameForRuntime(GetShapeDisplayName(kind));

        root.AddComponent<SelectableAsset>();
        root.AddComponent<XRGrabInteractable>();
        root.AddComponent<PlaceableXRGrabBridge>();
        root.AddComponent<CollisionEventCache>();
        root.AddComponent<PhysicsLensForceEventCache>();
        root.AddComponent<SpawnTemplateMarker>();

        root.SetActive(false);
        return root;
    }
}
