using UnityEngine;

public static class SimulationVisualizationInstaller
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void BootstrapInSandboxScene()
    {
        if (Object.FindFirstObjectByType<SimulationVisualizationManager>() != null)
            return;

        if (Object.FindFirstObjectByType<SandboxSimulationController>() == null
            && Object.FindFirstObjectByType<SandboxEditorToolbarFrame>() == null
            && Object.FindFirstObjectByType<AssetSelectionManager>() == null)
        {
            return;
        }

        var go = new GameObject("SimulationVisualizationManager");
        go.AddComponent<SimulationVisualizationManager>();
    }
}
