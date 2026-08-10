using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Deletes the UI objects the retro screens replaced.
///
/// Done through the editor rather than by hand-editing the scene file: removing a
/// GameObject means removing its components, its whole child subtree, its entry in the
/// parent's child list, and nulling every reference to it. Unity does all of that
/// correctly; a hand edit that gets one of them wrong corrupts the scene quietly.
///
/// This is a one-shot tidy-up — delete this script once you've run it.
/// </summary>
public static class RetiredUICleanup
{
    /// <summary>
    /// Retired objects, by name. All replaced by code-built equivalents:
    /// TextFeild/PlayButton by RetroUsernamePanel, UserNameHandler by that panel's own
    /// gating, CountDownText by CountdownTimer, the leaderboard pair by RetroLeaderboardUI.
    /// </summary>
    private static readonly string[] Retired =
    {
        "TextFeild",
        "PlayButton",
        "UserNameHandler",
        "CountDownText",
        "Leaderboard",
        "LeaderboardPanel",
        "CountdownTimer",
        "CountdownTimer (1)",
    };

    [MenuItem("Sonic Snow/Delete Retired UI Objects")]
    public static void DeleteRetired()
    {
        List<GameObject> found = new List<GameObject>();

        // Walks the scene rather than using GameObject.Find, which skips inactive objects
        // — and every one of these was deactivated by its replacement's setup tool.
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            Collect(root.transform, found);
        }

        if (found.Count == 0)
        {
            EditorUtility.DisplayDialog("Delete Retired UI",
                "Nothing to delete — the retired objects are already gone.", "OK");
            return;
        }

        string list = string.Join("\n", found.ConvertAll(go => "   • " + go.name));

        bool confirmed = EditorUtility.DisplayDialog("Delete Retired UI",
            $"Permanently delete {found.Count} retired object(s)?\n\n{list}\n\n" +
            "Their replacements are already in the scene. Undo works if this is wrong.",
            "Delete", "Cancel");

        if (!confirmed) return;

        foreach (GameObject go in found)
        {
            Debug.Log($"[RetiredUICleanup] Deleted '{go.name}'.");
            Undo.DestroyObjectImmediate(go);
        }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

        Debug.Log($"[RetiredUICleanup] Removed {found.Count} object(s). Save the scene (Ctrl+S).");
    }

    private static void Collect(Transform current, List<GameObject> found)
    {
        if (System.Array.IndexOf(Retired, current.name) >= 0)
        {
            // Whole subtree goes with it, so don't descend into a match.
            found.Add(current.gameObject);
            return;
        }

        foreach (Transform child in current) Collect(child, found);
    }
}
