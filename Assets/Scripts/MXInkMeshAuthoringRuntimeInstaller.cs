using UnityEngine;
using UnityEngine.SceneManagement;

public static class MXInkMeshAuthoringRuntimeInstaller
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InstallAfterSceneLoad()
    {
        TryInstall();
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        TryInstall();
    }

    private static void TryInstall()
    {
        var drawer = Object.FindFirstObjectByType<XRContentDrawerController>(FindObjectsInactive.Include);
        var stylus = Object.FindFirstObjectByType<StylusHandler>(FindObjectsInactive.Include);
        if (drawer == null)
        {
            return;
        }

        MXInkMeshDrawerButton.EnsureForDrawer(drawer);

        if (stylus == null
            || Object.FindFirstObjectByType<MeshSketchController>(FindObjectsInactive.Include) != null)
        {
            return;
        }

        var go = new GameObject("MXInkMeshSketchAuthoringRuntime");
        var adapter = go.AddComponent<MXInkInputAdapter>();
        adapter.Configure(stylus);
        go.AddComponent<MeshSketchFeedback>();
        var controller = go.AddComponent<MeshSketchController>();
        controller.Configure(adapter, drawer);
    }
}
