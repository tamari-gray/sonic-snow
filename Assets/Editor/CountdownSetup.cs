using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Puts the countdown on its own transparent Canvas over the AR view.
///
/// Its own Canvas for the same reason as the other retro screens: the game's original
/// Canvas is Constant Pixel Size at 800x600, and this design needs proportional scaling.
/// Safe to re-run.
/// </summary>
public static class CountdownSetup
{
    private const string CanvasName = "CountdownCanvas";
    private const string FontGuid = "380f1a141427a294684671969d6342cb";

    [MenuItem("Sonic Snow/Set Up Countdown")]
    public static void SetUp()
    {
        GameObject canvasObject = GameObject.Find(CanvasName);

        if (canvasObject == null)
        {
            canvasObject = new GameObject(CanvasName);
            Undo.RegisterCreatedObjectUndo(canvasObject, "Create Countdown Canvas");
        }

        Canvas canvas = Ensure<Canvas>(canvasObject);
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        // Above the HUD, below the username panel and leaderboard — none of them are
        // ever up at the same time, so the exact order only matters for tidiness.
        canvas.sortingOrder = 8;

        CanvasScaler scaler = Ensure<CanvasScaler>(canvasObject);
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        // Deliberately no GraphicRaycaster: the countdown is pure overlay and must never
        // swallow a tap meant for something underneath.

        CountdownTimer timer = Ensure<CountdownTimer>(canvasObject);

        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
            AssetDatabase.GUIDToAssetPath(FontGuid));

        if (font != null)
        {
            SerializedObject so = new SerializedObject(timer);
            so.FindProperty("displayFont").objectReferenceValue = font;
            so.FindProperty("numeralFont").objectReferenceValue = font;
            so.ApplyModifiedProperties();
        }

        CleanUpDuplicates(canvasObject);

        EditorSceneManager.MarkSceneDirty(canvasObject.scene);
        Selection.activeGameObject = canvasObject;

        Debug.Log("[CountdownSetup] Countdown ready on its own Canvas. Save the scene (Ctrl+S).");
    }

    /// <summary>
    /// Runs the countdown on demand. A menu item rather than only the component's
    /// context menu, because that one hangs off the component *header* — right-clicking
    /// the greyed-out Script field below it does nothing, which is an easy miss.
    /// </summary>
    [MenuItem("Sonic Snow/Preview Countdown")]
    public static void Preview()
    {
        if (!Application.isPlaying)
        {
            EditorUtility.DisplayDialog("Preview Countdown",
                "Enter Play mode first.\n\nThe countdown is a coroutine, and coroutines " +
                "don't run in edit mode.", "OK");
            return;
        }

        CountdownTimer timer = CountdownTimer.instance;

        if (timer == null)
        {
            EditorUtility.DisplayDialog("Preview Countdown",
                "No CountdownTimer is live.\n\nRun Sonic Snow > Set Up Countdown, save the " +
                "scene, then enter Play mode.", "OK");
            return;
        }

        timer.StopAllCoroutines();
        timer.StartCountdown(() => Debug.Log("[CountdownSetup] Preview finished."));
    }

    /// <summary>
    /// Leaves exactly one live CountdownTimer in the scene.
    ///
    /// Every copy runs Awake, builds its own full-screen countdown, and overwrites the
    /// static instance — so duplicates give you several countdowns stacked on top of each
    /// other and whichever woke last wins. Worth being thorough about.
    ///
    /// Extra copies appear easily: run this tool while the project has a compile error and
    /// GetComponent&lt;CountdownTimer&gt;() returns null because the type won't resolve, so
    /// the Ensure call below adds a second one.
    /// </summary>
    private static void CleanUpDuplicates(GameObject keepOn)
    {
        // Extra components on the canvas itself.
        CountdownTimer[] onCanvas = keepOn.GetComponents<CountdownTimer>();
        for (int i = 1; i < onCanvas.Length; i++)
        {
            Undo.DestroyObjectImmediate(onCanvas[i]);
            Debug.Log("[CountdownSetup] Removed a duplicate CountdownTimer from the canvas.");
        }

        // Copies living on other objects — the originals this replaces.
        foreach (CountdownTimer timer in Object.FindObjectsByType<CountdownTimer>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (timer == null || timer.gameObject == keepOn) continue;

            Undo.RecordObject(timer.gameObject, "Retire old countdown");
            timer.gameObject.SetActive(false);

            Debug.Log($"[CountdownSetup] Disabled '{timer.gameObject.name}', which also had a " +
                      "CountdownTimer. Safe to delete once the new countdown looks right.");
        }

        // The old text object isn't a CountdownTimer, so it needs naming directly.
        GameObject text = GameObject.Find("CountDownText");
        if (text != null)
        {
            Undo.RecordObject(text, "Retire old countdown");
            text.SetActive(false);
            Debug.Log("[CountdownSetup] Disabled 'CountDownText'.");
        }
    }

    private static T Ensure<T>(GameObject go) where T : Component
    {
        T component = go.GetComponent<T>();
        return component != null ? component : Undo.AddComponent<T>(go);
    }
}
