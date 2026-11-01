using UnityEditor;

namespace Before.Production.Editor
{
    /// <summary>Keeps UnityEditor out of the Production runtime assembly.</summary>
    [InitializeOnLoad]
    internal static class ProductionEditorExitAdapter
    {
        static ProductionEditorExitAdapter()
        {
            ProductionShellFlow.EditorExitRequested -= StopPlayMode;
            ProductionShellFlow.EditorExitRequested += StopPlayMode;
        }

        private static void StopPlayMode()
        {
            if (EditorApplication.isPlaying)
                EditorApplication.isPlaying = false;
        }
    }
}
