using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Drops the checkpoint counter into the bottom-left of the scene's Canvas and wires it to
/// a <see cref="CheckpointCounterUI"/>.
///
/// An editor tool rather than a hand-placed object for the same reason as the
/// calibration screen: it's a serialized reference into an existing Canvas, which is
/// exactly the kind of step that silently ends up half-wired.
///
/// Safe to re-run: it finds the object by name and re-wires rather than duplicating.
/// </summary>
public static class CheckpointCounterSetup
{
    private const string CounterName = "CheckpointCounter";
    private const string CalibrationPanelName = "CalibrationPanel";

    // The project's TMP font, so the counter matches the rest of the HUD.
    private const string FontGuid = "380f1a141427a294684671969d6342cb";

    private const string ScenePath = "Assets/Scenes/Game.unity";

    /// <summary>Batch-mode entry point, same pattern as RGBCameraCaptureSetup.SetUpBatch.</summary>
    public static void SetUpBatch()
    {
        EditorSceneManager.OpenScene(ScenePath);
        SetUp();
        bool saved = EditorSceneManager.SaveOpenScenes();
        Debug.Log("[CheckpointCounterSetup] Applied to " + ScenePath + ". Saved: " + saved);
    }

    [MenuItem("Sonic Snow/Set Up Checkpoint Counter")]
    public static void SetUp()
    {
        Canvas canvas = Object.FindAnyObjectByType<Canvas>(FindObjectsInactive.Include);

        if (canvas == null)
        {
            EditorUtility.DisplayDialog("Checkpoint Counter",
                "No Canvas in the open scene. Open Assets/Scenes/Game.unity and run this again.",
                "OK");
            return;
        }

        GameObject counter = FindOrCreateChild(canvas.transform, CounterName);

        RectTransform rect = counter.GetComponent<RectTransform>();
        if (rect == null) rect = counter.AddComponent<RectTransform>();

        // Anchor and pivot both bottom-left, so the inset stays put on any screen size.
        // Moved from top-left to the bottom by request: HUD info at the bottom of the
        // view is safer to read while moving at speed (less eye travel from the terrain
        // ahead), same reasoning as TimerText's move in Game.unity.
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(0f, 0f);
        rect.pivot = new Vector2(0f, 0f);
        rect.sizeDelta = new Vector2(260f, 70f);
        rect.anchoredPosition = new Vector2(24f, 24f);
        rect.localScale = Vector3.one;

        TextMeshProUGUI text = counter.GetComponent<TextMeshProUGUI>();
        if (text == null) text = Undo.AddComponent<TextMeshProUGUI>(counter);

        text.fontSize = 48f;
        text.alignment = TextAlignmentOptions.BottomLeft;
        text.color = Color.white;
        text.raycastTarget = false;
        text.text = "0/0";

        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
            AssetDatabase.GUIDToAssetPath(FontGuid));
        if (font != null) text.font = font;

        CheckpointCounterUI ui = counter.GetComponent<CheckpointCounterUI>();
        if (ui == null) ui = Undo.AddComponent<CheckpointCounterUI>(counter);

        SerializedObject so = new SerializedObject(ui);
        so.FindProperty("label").objectReferenceValue = text;
        so.ApplyModifiedProperties();

        // The calibration screen is a full-screen blackout and must stay on top of the
        // HUD, so put it back at the end after inserting the counter.
        Transform calibrationPanel = canvas.transform.Find(CalibrationPanelName);
        if (calibrationPanel != null) calibrationPanel.SetAsLastSibling();

        EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
        Selection.activeGameObject = counter;

        Debug.Log("[CheckpointCounterSetup] Counter placed bottom-left and wired. Save the scene (Ctrl+S).");
    }

    private static GameObject FindOrCreateChild(Transform parent, string name)
    {
        Transform existing = parent.Find(name);
        if (existing != null) return existing.gameObject;

        GameObject go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Create " + name);
        go.transform.SetParent(parent, false);
        go.layer = parent.gameObject.layer;

        return go;
    }
}
