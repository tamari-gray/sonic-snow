using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Unity.XR.XREAL;
using Unity.XR.XREAL.Samples;

/// <summary>
/// Wires up XREAL's "First Person View" capture tool (docs.xreal.com/Tools/First Person View) —
/// the only capture path on this hardware that records the real world (via XREAL Eye) and the
/// AR content blended into one video, rather than virtual content alone. XREAL ships it as a
/// demo scene, not an actual .prefab asset, so this rebuilds the same GameObject/component
/// wiring the demo scene uses (FirstPersonStreammingCast + Record/Stream buttons) as a menu
/// command, matching how every other screen in this project is built — see
/// RetroUI/FinishScoreUI/ZzzLogSetup for the same pattern.
///
/// Parented under Main Camera at the same (0,0,2) / 0.00111 placement the port already uses for
/// its five other World Space canvases (see XREALCanvasConversion.cs), so this reads as another
/// head-locked HUD panel rather than a one-off.
/// </summary>
public static class FirstPersonCaptureSetup
{
    private const string RootName = "FirstPersonCapture";

    [MenuItem("Sonic Snow/XREAL/Set Up First Person Capture")]
    public static void SetUp()
    {
        Camera mainCamera = Camera.main;
        if (mainCamera == null)
        {
            Debug.LogError("[FirstPersonCaptureSetup] No Camera.main in the scene — run this after the XR rig exists.");
            return;
        }

        Transform existing = mainCamera.transform.Find(RootName);
        GameObject root = existing != null ? existing.gameObject : new GameObject(RootName);
        root.transform.SetParent(mainCamera.transform, false);
        root.transform.localPosition = new Vector3(0f, 0f, 2f);
        root.transform.localRotation = Quaternion.identity;
        root.transform.localScale = Vector3.one * 0.00111f;

        // Clear out anything from a previous run so re-invoking this is idempotent rather than
        // stacking duplicate children (same rule every other "Set Up ..." tool in this project follows).
        for (int i = root.transform.childCount - 1; i >= 0; i--)
            Object.DestroyImmediate(root.transform.GetChild(i).gameObject);

        Canvas canvas = root.GetComponent<Canvas>();
        if (canvas == null) canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = mainCamera;

        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        if (scaler == null) scaler = root.AddComponent<CanvasScaler>();

        if (root.GetComponent<GraphicRaycaster>() == null)
            root.AddComponent<GraphicRaycaster>();

        RectTransform rootRect = (RectTransform)root.transform;
        rootRect.sizeDelta = new Vector2(300f, 150f);

        Button recordButton = BuildButton(rootRect, "Record", new Vector2(-70f, 0f), out Text recordText);
        Button streamButton = BuildButton(rootRect, "Stream", new Vector2(70f, 0f), out Text streamText);

        FirstPersonStreammingCast capture = root.GetComponent<FirstPersonStreammingCast>();
        if (capture == null) capture = root.AddComponent<FirstPersonStreammingCast>();

        SerializedObject serialized = new SerializedObject(capture);
        serialized.FindProperty("m_RecordBtn").objectReferenceValue = recordButton;
        serialized.FindProperty("m_RecordText").objectReferenceValue = recordText;
        serialized.FindProperty("m_StreamBtn").objectReferenceValue = streamButton;
        serialized.FindProperty("m_StreamText").objectReferenceValue = streamText;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        capture.m_BlendMode = BlendMode.Blend; // real world (XREAL Eye) + AR content, combined
        // Middle, set 2026-08-17 for the logcat reproduction XREAL support asked for. History:
        // Middle stuttered, Low was tried and got WORSE not better, so it went back to High —
        // evidence against "cost scales with resolution" and for "the stutter isn't
        // resolution-bound at all". Middle is the middle setting of a knob we've now shown
        // doesn't drive the problem, which makes it the honest one to reproduce on: it rules
        // out "you were just asking the device for too many pixels" without pretending the
        // resolution is what matters. Note GetResolutionByLevel sorts DESCENDING, so Middle is
        // the second-LARGEST supported resolution, not a midpoint in pixel count.
        capture.m_ResolutionLevel = FirstPersonStreammingCast.ResolutionLevel.Middle;
        capture.m_CullingMask = -1;
        capture.m_AudioState = AudioState.ApplicationAndMicAudio; // game SFX + narration, matches XREAL's own demo default
        capture.useGreenBackGround = false;

        // No hand/controller input is wired up for this headset yet, so there's no way to
        // actually reach these buttons — recording is triggered automatically instead (see
        // CalibrationScreen's "AR recording setup" condition). Disabling the canvas hides both
        // buttons and stops it from eating raycasts, without touching the component wiring
        // above — TriggerRecord() calls straight into the script, not through a UI click.
        canvas.enabled = false;

        EditorUtility.SetDirty(root);
        Debug.Log("[FirstPersonCaptureSetup] First Person Capture panel wired under Main Camera. " +
                  "Requires CAMERA + RECORD_AUDIO Android permissions — see FirstPersonCaptureSetup's " +
                  "manifest step.");
    }

    private static Button BuildButton(RectTransform parent, string label, Vector2 anchoredPosition, out Text text)
    {
        GameObject buttonGo = new GameObject(label, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        RectTransform buttonRect = (RectTransform)buttonGo.transform;
        buttonRect.SetParent(parent, false);
        buttonRect.anchorMin = new Vector2(0.5f, 0.5f);
        buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
        buttonRect.pivot = new Vector2(0.5f, 0.5f);
        buttonRect.anchoredPosition = anchoredPosition;
        buttonRect.sizeDelta = new Vector2(100f, 45f);

        Image image = buttonGo.GetComponent<Image>();
        image.color = new Color(0.16f, 0.16f, 0.16f, 0.9f);

        GameObject textGo = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        RectTransform textRect = (RectTransform)textGo.transform;
        textRect.SetParent(buttonRect, false);
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        text = textGo.GetComponent<Text>();
        text.text = label;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 14;

        return buttonGo.GetComponent<Button>();
    }
}
