using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builds the calibration screen UI in the open scene and wires it to a
/// <see cref="CalibrationScreen"/> component.
///
/// This exists as an editor tool rather than a prefab because the screen is four
/// serialized references into a Canvas that already exists in the scene — hooking
/// those up by hand is the kind of step that silently gets half-done and then
/// presents as "the calibration screen doesn't appear".
///
/// Safe to run more than once: it finds the existing objects by name and re-wires
/// them rather than stacking up duplicates.
/// </summary>
public static class CalibrationScreenSetup
{
    private const string HostName = "CalibrationScreen";
    private const string PanelName = "CalibrationPanel";
    private const string InstructionName = "InstructionText";
    private const string StatusName = "StatusText";
    private const string SpinnerName = "Spinner";

    [MenuItem("Sonic Snow/Set Up Calibration Screen")]
    public static void SetUp()
    {
        Canvas canvas = Object.FindAnyObjectByType<Canvas>(FindObjectsInactive.Include);

        if (canvas == null)
        {
            EditorUtility.DisplayDialog("Calibration Screen",
                "No Canvas in the open scene. Open Assets/Scenes/Game.unity and run this again.",
                "OK");
            return;
        }

        GameObject panel = FindOrCreateChild(canvas.transform, PanelName);
        RectTransform panelRect = EnsureRect(panel);
        Stretch(panelRect);

        Image backdrop = Ensure<Image>(panel);
        backdrop.color = new Color(0.03f, 0.05f, 0.09f, 0.92f);
        backdrop.raycastTarget = true; // swallow taps so nothing behind the screen is clickable

        TMP_Text instruction = BuildText(panelRect, InstructionName,
            new Vector2(700f, 260f), new Vector2(0f, 130f), 34f, TextAlignmentOptions.Center);

        TMP_Text status = BuildText(panelRect, StatusName,
            new Vector2(520f, 220f), new Vector2(0f, -190f), 26f, TextAlignmentOptions.Left);

        RectTransform spinner = BuildSpinner(panelRect);

        // The component lives on its own root object, not on the panel. Finish() disables
        // the panel, and a disabled GameObject stops running Update — which would freeze
        // the component the moment it succeeded.
        GameObject host = GameObject.Find(HostName);
        if (host == null)
        {
            host = new GameObject(HostName);
            Undo.RegisterCreatedObjectUndo(host, "Create Calibration Screen");
        }

        CalibrationScreen screen = Ensure<CalibrationScreen>(host);

        SerializedObject so = new SerializedObject(screen);
        so.FindProperty("panelRoot").objectReferenceValue = panel;
        so.FindProperty("instructionText").objectReferenceValue = instruction;
        so.FindProperty("statusText").objectReferenceValue = status;
        so.FindProperty("spinner").objectReferenceValue = spinner;
        so.ApplyModifiedProperties();

        // Last sibling = drawn last = on top of the leaderboard and username panels.
        panelRect.SetAsLastSibling();

        EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
        Selection.activeGameObject = host;

        Debug.Log("[CalibrationScreenSetup] Calibration screen built and wired. Save the scene (Ctrl+S).");
    }

    private static TMP_Text BuildText(RectTransform parent, string name, Vector2 size,
                                      Vector2 position, float fontSize, TextAlignmentOptions alignment)
    {
        GameObject go = FindOrCreateChild(parent, name);
        RectTransform rect = EnsureRect(go);

        Centre(rect, size, position);

        TextMeshProUGUI text = Ensure<TextMeshProUGUI>(go);
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = Color.white;
        text.richText = true;
        text.raycastTarget = false;

        return text;
    }

    private static RectTransform BuildSpinner(RectTransform parent)
    {
        GameObject go = FindOrCreateChild(parent, SpinnerName);
        RectTransform rect = EnsureRect(go);

        Centre(rect, new Vector2(72f, 72f), new Vector2(0f, -30f));

        Image image = Ensure<Image>(go);
        image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
        image.color = new Color(0.43f, 0.91f, 0.63f, 1f);
        image.raycastTarget = false;

        // A rotating disc reads as static. A rotating wedge reads as a spinner.
        image.type = Image.Type.Filled;
        image.fillMethod = Image.FillMethod.Radial360;
        image.fillAmount = 0.25f;

        return rect;
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

    private static RectTransform EnsureRect(GameObject go)
    {
        RectTransform rect = go.GetComponent<RectTransform>();
        return rect != null ? rect : go.AddComponent<RectTransform>();
    }

    private static T Ensure<T>(GameObject go) where T : Component
    {
        T component = go.GetComponent<T>();
        return component != null ? component : Undo.AddComponent<T>(go);
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
    }

    private static void Centre(RectTransform rect, Vector2 size, Vector2 position)
    {
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
    }
}
