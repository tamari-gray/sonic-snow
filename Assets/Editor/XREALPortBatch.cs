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

    /// <summary>
    /// One-off cleanup: removes the floating "StartDistanceText" object an earlier pass
    /// added directly under LeaderboardCanvas. Superseded by RetroLeaderboardUI's own
    /// distance row, which is built in code and only exists while the board is populated.
    /// </summary>
    public static void RemoveLegacyStartDistanceText()
    {
        EditorSceneManager.OpenScene(ScenePath);

        GameObject canvasGo = GameObject.Find("LeaderboardCanvas");
        Transform legacy = canvasGo != null ? canvasGo.transform.Find("StartDistanceText") : null;
        if (legacy != null)
        {
            // DestroyImmediate alone doesn't mark the scene dirty in batch mode — without
            // this, SaveOpenScenes() reports success but silently skips rewriting the file,
            // since Unity doesn't think anything changed. Confirmed the hard way: an earlier
            // run of this method logged "Saved: True" and the object was still on disk after.
            Object.DestroyImmediate(legacy.gameObject);
            EditorSceneManager.MarkSceneDirty(canvasGo.scene);
            Debug.Log("[XREALPortBatch] Removed legacy StartDistanceText.");
        }
        else
        {
            Debug.Log("[XREALPortBatch] No legacy StartDistanceText found — nothing to remove.");
        }

        bool saved = EditorSceneManager.SaveOpenScenes();
        Debug.Log($"[XREALPortBatch] Saved: {saved}");
    }
}
