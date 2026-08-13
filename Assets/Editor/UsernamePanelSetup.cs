using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builds the retro "ENTER NAME" panel and rewires the existing game logic to it.
///
/// The point of the rewiring is that nothing about the game's behaviour changes: the
/// panel exposes a real TMP_InputField and Button, and those get assigned to the same
/// GameLogic and UsernameInputValidator fields the old UI used. Username sanitising,
/// the play-button gating, and OnPlayButtonPressed all carry on untouched.
/// </summary>
public static class UsernamePanelSetup
{
    private const string CanvasName = "UsernameCanvas";
    private const string FontGuid = "380f1a141427a294684671969d6342cb";

    [MenuItem("Sonic Snow/Set Up Username Panel")]
    public static void SetUp()
    {
        GameObject canvasObject = GameObject.Find(CanvasName);

        if (canvasObject == null)
        {
            canvasObject = new GameObject(CanvasName);
            Undo.RegisterCreatedObjectUndo(canvasObject, "Create Username Canvas");
        }

        Canvas canvas = Ensure<Canvas>(canvasObject);
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        // Above the game HUD, below the leaderboard — the two are never up together.
        canvas.sortingOrder = 9;

        CanvasScaler scaler = Ensure<CanvasScaler>(canvasObject);
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        Ensure<GraphicRaycaster>(canvasObject);

        RetroUsernamePanel panel = Ensure<RetroUsernamePanel>(canvasObject);

        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
            AssetDatabase.GUIDToAssetPath(FontGuid));

        if (font != null)
        {
            SerializedObject so = new SerializedObject(panel);
            so.FindProperty("displayFont").objectReferenceValue = font;
            so.ApplyModifiedProperties();
        }

        EnsureEventSystem();

        // The panel builds its widgets in Awake, which hasn't run in edit mode — so the
        // rewiring below happens on entering play mode instead, via the panel itself.
        Debug.Log("[UsernamePanelSetup] Panel created. GameLogic is rewired to the new input " +
                  "field and button automatically at runtime — see RetroUsernamePanel.Start().");

        RetireOldUsernameUI();

        EditorSceneManager.MarkSceneDirty(canvasObject.scene);
        Selection.activeGameObject = canvasObject;

        Debug.Log("[UsernamePanelSetup] Done. Save the scene (Ctrl+S).");
    }

    /// <summary>An input field needs an EventSystem to receive focus and typing at all.</summary>
    private static void EnsureEventSystem()
    {
        if (Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>(FindObjectsInactive.Include) != null) return;

        GameObject go = new GameObject("EventSystem",
            typeof(UnityEngine.EventSystems.EventSystem),
            typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));

        Undo.RegisterCreatedObjectUndo(go, "Create EventSystem");
        Debug.Log("[UsernamePanelSetup] Added an EventSystem — the input field needs one to accept typing.");
    }

    /// <summary>
    /// Disables the old username UI rather than deleting it. Looked up by name so this
    /// script has no compile-time dependency on objects you may want to remove later.
    /// </summary>
    private static void RetireOldUsernameUI()
    {
        foreach (string name in new[] { "TextFeild", "PlayButton" })
        {
            GameObject old = GameObject.Find(name);
            if (old == null) continue;

            Undo.RecordObject(old, "Retire old username UI");
            old.SetActive(false);

            Debug.Log($"[UsernamePanelSetup] Disabled '{name}'. Safe to delete once the new panel looks right.");
        }
    }

    private static T Ensure<T>(GameObject go) where T : Component
    {
        T component = go.GetComponent<T>();
        return component != null ? component : Undo.AddComponent<T>(go);
    }
}
