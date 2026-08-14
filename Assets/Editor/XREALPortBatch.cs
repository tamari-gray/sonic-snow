using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Batch-mode entry point for the mechanical parts of the XREAL canvas port
/// (<see cref="XREALCanvasConversion"/> and <see cref="ZzzLogSetup"/>), so they can be driven
/// from the command line instead of clicking each menu item in the GUI.
/// </summary>
public static class XREALPortBatch
{
    private const string ScenePath = "Assets/Scenes/Game.unity";

    public static void RunCanvasPort()
    {
        EditorSceneManager.OpenScene(ScenePath);

        XREALCanvasConversion.Convert();
        ZzzLogSetup.SetUp();

        bool saved = EditorSceneManager.SaveOpenScenes();
        Debug.Log($"[XREALPortBatch] Canvas port applied to {ScenePath}. Saved: {saved}");
    }

    public static void RunStartDistanceSetup()
    {
        EditorSceneManager.OpenScene(ScenePath);

        StartDistanceSetup.SetUp();

        bool saved = EditorSceneManager.SaveOpenScenes();
        Debug.Log($"[XREALPortBatch] Start distance readout applied to {ScenePath}. Saved: {saved}");
    }
}
