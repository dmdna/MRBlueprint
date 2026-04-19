using System;
using UnityEngine;

/// <summary>Populates the content drawer with extra procedural shapes at runtime (spawn templates + preview tiles).</summary>
[DefaultExecutionOrder(-120)]
public sealed class SandboxDrawerAdditionalShapesBootstrap : MonoBehaviour
{
    private bool _didBuild;

    private void Awake()
    {
        if (_didBuild)
            return;

        var previewMaterial = ResolveDrawerPreviewMaterial();
        if (previewMaterial == null)
        {
            Debug.LogWarning(
                "SandboxDrawerAdditionalShapesBootstrap: could not resolve a preview material from existing drawer tiles.");
            previewMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        }

        var holder = new GameObject("MrBlueprint_ShapeSpawnTemplates");
        holder.transform.SetParent(transform.root, false);
        holder.SetActive(false);

        foreach (var kind in (MrBlueprintPlaceableShapeKind[])Enum.GetValues(typeof(MrBlueprintPlaceableShapeKind)))
        {
            var template = PlaceableRuntimeFactory.CreateShapeSpawnTemplate(kind, previewMaterial, holder.transform);
            var tile = CreateDrawerTile(kind, previewMaterial, template);
            tile.transform.SetParent(transform, false);
        }

        GetComponent<DrawerGridLayout3D>()?.LayoutChildren();

        _didBuild = true;
    }

    private Material ResolveDrawerPreviewMaterial()
    {
        for (var i = 0; i < transform.childCount; i++)
        {
            var mr = transform.GetChild(i).GetComponentInChildren<MeshRenderer>(true);
            if (mr != null && mr.sharedMaterial != null)
                return mr.sharedMaterial;
        }

        return null;
    }

    private static GameObject CreateDrawerTile(
        MrBlueprintPlaceableShapeKind kind,
        Material previewMaterial,
        GameObject spawnTemplate)
    {
        var root = new GameObject($"DrawerItem_{kind}");
        var drawerItem = root.AddComponent<XRDrawerItem>();
        drawerItem.SetSpawnPrefab(spawnTemplate);

        var pivot = new GameObject("PreviewPivot");
        pivot.transform.SetParent(root.transform, false);

        var model = new GameObject("PreviewModel");
        model.transform.SetParent(pivot.transform, false);
        model.transform.localScale = new Vector3(0.15f, 0.15f, 0.15f);

        var mf = model.AddComponent<MeshFilter>();
        mf.sharedMesh = MrBlueprintPrimitiveMeshes.GetOrCreate(kind);

        var mr = model.AddComponent<MeshRenderer>();
        mr.sharedMaterial = previewMaterial;

        var sc = model.AddComponent<SphereCollider>();
        sc.radius = 0.5f;

        var pick = model.AddComponent<DrawerTilePickTarget>();
        pick.SetCaptionForRuntime(PlaceableRuntimeFactory.GetShapeDisplayName(kind));

        return root;
    }
}
