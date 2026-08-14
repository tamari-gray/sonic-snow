using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Drops a "distance to start" readout onto the leaderboard screen, wired to
/// <see cref="StartDistanceUI"/>. Same convention as CheckpointCounterSetup: an editor tool
/// rather than a hand-placed object, since it's a serialized reference into an existing Canvas.
///
/// Safe to re-run: finds the object by name and re-wires rather than duplicating.
/// </summary>
public static class StartDistanceSetup
{
    private const string LabelName = "StartDistanceText";
    private const string LeaderboardCanvasName = "LeaderboardCanvas";

    // The project's TMP font, so this matches the rest of the HUD.
    private const string FontGuid = "380f1a141427a294684671969d6342cb";

    [MenuItem("Sonic Snow/Set Up Start Distance Readout")]
    public static void SetUp()
    {
        GameObject canvasGo = GameObject.Find(LeaderboardCanvasName);
        if (canvasGo == null)
        {
            EditorUtility.DisplayDialog("Start Distance Readout",
                $"No '{LeaderboardCanvasName}' GameObject in the open scene. Open Assets/Scenes/Game.unity and run this again.",
                "OK");
            return;
        }

        GameObject label = FindOrCreateChild(canvasGo.transform, LabelName);

        RectTransform rect = label.GetComponent<RectTransform>();
        if (rect == null) rect = label.AddComponent<RectTransform>();

        // Anchor and pivot both bottom-centre, below the leaderboard rows.
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.sizeDelta = new Vector2(600f, 70f);
        rect.anchoredPosition = new Vector2(0f, 24f);
        rect.localScale = Vector3.one;

        TextMeshProUGUI text = label.GetComponent<TextMeshProUGUI>();
        if (text == null) text = Undo.AddComponent<TextMeshProUGUI>(label);

        text.fontSize = 40f;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        text.raycastTarget = false;
        text.text = "-- m to start";

        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
            AssetDatabase.GUIDToAssetPath(FontGuid));
        if (font != null) text.font = font;

        StartDistanceUI ui = label.GetComponent<StartDistanceUI>();
        if (ui == null) ui = Undo.AddComponent<StartDistanceUI>(label);

        SerializedObject so = new SerializedObject(ui);
        so.FindProperty("label").objectReferenceValue = text;
        so.ApplyModifiedProperties();

        EditorSceneManager.MarkSceneDirty(canvasGo.scene);
        Selection.activeGameObject = label;

        Debug.Log("[StartDistanceSetup] Distance readout placed and wired. Save the scene (Ctrl+S).");
    }

    private static GameObject FindOrCreateChild(Transform parent, string name)
    {
        Transform existing = parent.Find(name);
        if (existing != null) return existing.gameObject;

        GameObject go = new GameObject(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(go, "Create " + name);
        go.transform.SetParent(parent, false);
        go.layer = parent.gameObject.layer;

        return go;
    }
}
