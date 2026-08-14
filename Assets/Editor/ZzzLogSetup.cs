using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Gives the existing "DebugLog" GameObject (holding <see cref="ZzzLog"/>) a world-space
/// canvas and TMP text to write into, replacing the OnGUI overlay that doesn't render
/// through the XR pipeline. Parented to the XREAL centre camera off to the right, same
/// convention as <see cref="XREALCanvasConversion"/>, so it stays in view as a persistent
/// side panel rather than sitting in the middle of gameplay.
///
/// Safe to re-run: finds by name and re-wires rather than duplicating.
/// </summary>
public static class ZzzLogSetup
{
    private const string DebugLogName = "DebugLog";
    private const string CanvasName = "DebugLogCanvas";
    private const string TextName = "LogText";

    private const float DistanceFromCamera = 2f;
    private const float WorldScale = 0.00111f;

    // The project's TMP font, matching the other HUD text.
    private const string FontGuid = "380f1a141427a294684671969d6342cb";

    [MenuItem("Sonic Snow/XREAL/Set Up Debug Log Canvas")]
    public static void SetUp()
    {
        Camera mainCamera = Camera.main;
        if (mainCamera == null)
        {
            EditorUtility.DisplayDialog("Debug Log Canvas",
                "No camera tagged MainCamera in the open scene. Open Assets/Scenes/Game.unity " +
                "and make sure the XREAL/XR Origin camera is tagged MainCamera, then run again.",
                "OK");
            return;
        }

        GameObject debugLog = GameObject.Find(DebugLogName);
        if (debugLog == null || debugLog.GetComponent<ZzzLog>() == null)
        {
            EditorUtility.DisplayDialog("Debug Log Canvas",
                $"No '{DebugLogName}' GameObject with a ZzzLog component found in the open scene.",
                "OK");
            return;
        }

        GameObject canvasGo = FindOrCreateChild(mainCamera.transform, CanvasName);

        Canvas canvas = canvasGo.GetComponent<Canvas>();
        if (canvas == null) canvas = Undo.AddComponent<Canvas>(canvasGo);
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = mainCamera;

        RectTransform canvasRect = canvasGo.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(400f, 1080f);

        // Off to the right and slightly back, so it reads as a side panel rather than
        // blocking the centre of the view where gameplay/other HUD sits.
        canvasRect.localPosition = new Vector3(300f, 0f, DistanceFromCamera);
        canvasRect.localRotation = Quaternion.identity;
        canvasRect.localScale = Vector3.one * WorldScale;

        GameObject textGo = FindOrCreateChild(canvasRect, TextName);

        RectTransform textRect = textGo.GetComponent<RectTransform>();
        if (textRect == null) textRect = textGo.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        textRect.localScale = Vector3.one;

        TextMeshProUGUI text = textGo.GetComponent<TextMeshProUGUI>();
        if (text == null) text = Undo.AddComponent<TextMeshProUGUI>(textGo);
        text.fontSize = 28f;
        text.alignment = TextAlignmentOptions.TopLeft;
        text.color = Color.white;
        text.raycastTarget = false;
        text.enableWordWrapping = true;
        text.text = "";

        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
            AssetDatabase.GUIDToAssetPath(FontGuid));
        if (font != null) text.font = font;

        SerializedObject so = new SerializedObject(debugLog.GetComponent<ZzzLog>());
        so.FindProperty("display").objectReferenceValue = text;
        so.ApplyModifiedProperties();

        EditorSceneManager.MarkSceneDirty(debugLog.scene);
        Selection.activeGameObject = canvasGo;

        Debug.Log("[ZzzLogSetup] Debug log canvas placed and wired to ZzzLog. Save the scene (Ctrl+S).");
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
