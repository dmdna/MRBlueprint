using UnityEngine;

public static class SimulationVisualizationInstaller
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void BootstrapInSandboxScene()
    {
        EnsureRuntimeManager();
    }

    public static SimulationVisualizationManager EnsureRuntimeManager()
    {
        var existing = Object.FindFirstObjectByType<SimulationVisualizationManager>();
        if (existing != null)
            return existing;

        if (Object.FindFirstObjectByType<SandboxSimulationController>() == null
            && Object.FindFirstObjectByType<SandboxEditorToolbarFrame>() == null
            && Object.FindFirstObjectByType<AssetSelectionManager>() == null)
        {
            return null;
        }

        var go = new GameObject("SimulationVisualizationManager");
        return go.AddComponent<SimulationVisualizationManager>();
    }
}
