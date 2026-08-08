using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Creates the retro leaderboard on its own Canvas and retires the old one.
///
/// A separate Canvas on purpose: the design is authored at a 1040px board width against
/// a 1920x1080 reference, but the game's existing Canvas is Constant Pixel Size at
/// 800x600. Switching that scaler would resize every other piece of UI in the scene, so
/// the board gets its own correctly-configured Canvas instead.
///
/// Safe to re-run: finds by name and re-wires rather than duplicating.
/// </summary>
public static class LeaderboardSetup
{
    private const string CanvasName = "LeaderboardCanvas";
    private const string FontGuid = "380f1a141427a294684671969d6342cb";

    [MenuItem("Sonic Snow/Set Up Leaderboard")]
    public static void SetUp()
    {
        GameObject canvasObject = GameObject.Find(CanvasName);

        if (canvasObject == null)
        {
            canvasObject = new GameObject(CanvasName);
            Undo.RegisterCreatedObjectUndo(canvasObject, "Create Leaderboard Canvas");
        }

        Canvas canvas = Ensure<Canvas>(canvasObject);
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        // Above the game HUD, below nothing else — the board is a full-screen takeover.
        canvas.sortingOrder = 10;

        CanvasScaler scaler = Ensure<CanvasScaler>(canvasObject);
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        // Match height: the board is a tall centred column, so vertical fit is what matters.
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 1f;

        Ensure<GraphicRaycaster>(canvasObject);

        RetroLeaderboardUI board = Ensure<RetroLeaderboardUI>(canvasObject);

        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
            AssetDatabase.GUIDToAssetPath(FontGuid));

        if (font != null)
        {
            SerializedObject so = new SerializedObject(board);

            // The handoff asks for Press Start 2P and VT323. Until those are imported and
            // TMP font assets generated, the project's own arcade face stands in for both.
            so.FindProperty("displayFont").objectReferenceValue = font;
            so.FindProperty("numeralFont").objectReferenceValue = font;
            so.ApplyModifiedProperties();
        }

        RetireOldLeaderboard();

        EditorSceneManager.MarkSceneDirty(canvasObject.scene);
        Selection.activeGameObject = canvasObject;

        Debug.Log("[LeaderboardSetup] Retro leaderboard ready on its own Canvas. Save the scene (Ctrl+S).");
    }

    /// <summary>
    /// Disables the previous leaderboard rather than deleting it, so a scene that still
    /// has references to it doesn't come up with missing objects. Delete by hand once
    /// you're happy with the new board.
    /// </summary>
    private static void RetireOldLeaderboard()
    {
        LeaderboardUI old = Object.FindAnyObjectByType<LeaderboardUI>(FindObjectsInactive.Include);
        if (old == null) return;

        Undo.RecordObject(old.gameObject, "Retire old leaderboard");
        old.gameObject.SetActive(false);

        Debug.Log($"[LeaderboardSetup] Disabled the old '{old.gameObject.name}' object. " +
                  "Delete it once the new board looks right.");
    }

    private static T Ensure<T>(GameObject go) where T : Component
    {
        T component = go.GetComponent<T>();
        return component != null ? component : Undo.AddComponent<T>(go);
    }
}
