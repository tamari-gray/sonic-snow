using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Puts the finish-line score screen on its own transparent Canvas over the AR view.
///
/// Its own Canvas for the same reason as the other retro screens: the game's original
/// Canvas is Constant Pixel Size at 800x600, and this design needs proportional scaling.
/// Safe to re-run.
/// </summary>
public static class FinishScoreSetup
{
    private const string CanvasName = "FinishScoreCanvas";
    private const string FontGuid = "380f1a141427a294684671969d6342cb";

    [MenuItem("Sonic Snow/Set Up Finish Score")]
    public static void SetUp()
    {
        GameObject canvasObject = GameObject.Find(CanvasName);

        if (canvasObject == null)
        {
            canvasObject = new GameObject(CanvasName);
            Undo.RegisterCreatedObjectUndo(canvasObject, "Create Finish Score Canvas");
        }

        Canvas canvas = Ensure<Canvas>(canvasObject);
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        // Above the HUD and the countdown — the two only ever meet for a frame in
        // transition, and this should win that frame rather than the countdown.
        canvas.sortingOrder = 9;

        CanvasScaler scaler = Ensure<CanvasScaler>(canvasObject);
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        // Deliberately no GraphicRaycaster: pure overlay, must never swallow a tap meant
        // for something underneath.

        FinishScoreUI score = Ensure<FinishScoreUI>(canvasObject);

        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
            AssetDatabase.GUIDToAssetPath(FontGuid));

        if (font != null)
        {
            SerializedObject so = new SerializedObject(score);
            so.FindProperty("font").objectReferenceValue = font;
            so.ApplyModifiedProperties();
        }

        CleanUpDuplicates(canvasObject);

        EditorSceneManager.MarkSceneDirty(canvasObject.scene);
        Selection.activeGameObject = canvasObject;

        // Saved here rather than left to a manual Ctrl+S like the other retro screens'
        // setup tools — this one also needs to run headlessly from batch mode, where
        // there's no human left to press it before the process exits on -quit.
        bool saved = EditorSceneManager.SaveOpenScenes();

        Debug.Log($"[FinishScoreSetup] Finish score screen ready on its own Canvas. Saved: {saved}");
    }

    /// <summary>
    /// Shows the screen on demand with sample numbers. A menu item rather than only the
    /// component's context menu, matching CountdownSetup's precedent — that one hangs off
    /// the header and is easy to miss.
    /// </summary>
    [MenuItem("Sonic Snow/Preview Finish Score")]
    public static void Preview()
    {
        if (!Application.isPlaying)
        {
            EditorUtility.DisplayDialog("Preview Finish Score",
                "Enter Play mode first.\n\nThe score pulse is a coroutine, and coroutines " +
                "don't run in edit mode.", "OK");
            return;
        }

        FinishScoreUI score = FinishScoreUI.Instance;

        if (score == null)
        {
            EditorUtility.DisplayDialog("Preview Finish Score",
                "No FinishScoreUI is live.\n\nRun Sonic Snow > Set Up Finish Score, save the " +
                "scene, then enter Play mode.", "OK");
            return;
        }

        score.Show("RINGRUNNER", 105.77f, 4, 5);
    }

    /// <summary>Leaves exactly one live FinishScoreUI in the scene, same reasoning as
    /// CountdownSetup's duplicate cleanup.</summary>
    private static void CleanUpDuplicates(GameObject keepOn)
    {
        FinishScoreUI[] onCanvas = keepOn.GetComponents<FinishScoreUI>();
        for (int i = 1; i < onCanvas.Length; i++)
        {
            Undo.DestroyObjectImmediate(onCanvas[i]);
            Debug.Log("[FinishScoreSetup] Removed a duplicate FinishScoreUI from the canvas.");
        }

        foreach (FinishScoreUI score in Object.FindObjectsByType<FinishScoreUI>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (score == null || score.gameObject == keepOn) continue;

            Undo.RecordObject(score.gameObject, "Retire old finish score screen");
            score.gameObject.SetActive(false);

            Debug.Log($"[FinishScoreSetup] Disabled '{score.gameObject.name}', which also had a " +
                      "FinishScoreUI. Safe to delete once the new screen looks right.");
        }
    }

    private static T Ensure<T>(GameObject go) where T : Component
    {
        T component = go.GetComponent<T>();
        return component != null ? component : Undo.AddComponent<T>(go);
    }
}
