using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Converts the game's Screen Space Overlay canvases to World Space, parented to the XREAL
/// centre camera. Screen Space Overlay does not render at all in an HMD — every HUD/menu
/// canvas needs a world position, a world camera, and a scale that reads at HMD distance.
///
/// Placement follows the XREAL port's FOV math (see the xreal-port memory): at 2m in front
/// of the camera, the One Pro's ~57-degree diagonal FOV gives about 1.9m of usable width, so
/// a canvas is placed at 2m with a scale that renders roughly 1.2m wide — comfortably inside
/// that budget. This is a first-pass default, not a final layout: it hasn't been seen on the
/// glasses yet and individual canvases (their font sizes, spinner size, etc.) will likely need
/// per-canvas tuning once field-tested.
///
/// Safe to re-run: re-parents and re-applies transform/camera settings rather than duplicating.
/// </summary>
public static class XREALCanvasConversion
{
    private static readonly string[] CanvasNames =
    {
        "CalibrationPanel",
        "CountdownCanvas",
        "Canvas",
        "UsernameCanvas",
        "CalibrationScreen",
        // Missed in the original port pass — FinishScoreUI builds this canvas at runtime
        // rather than it being a static pre-existing object, so it wasn't caught by whatever
        // swept the scene for canvases at the time. Left at ScreenSpaceOverlay (the Unity
        // default), which renders nothing at all in an HMD — confirmed 2026-08-17 as why the
        // finish score screen never appeared on the glasses despite Show() running correctly.
        "FinishScoreCanvas",
    };

    private const float DistanceFromCamera = 2f;
    private const float WorldScale = 0.00111f;

    [MenuItem("Sonic Snow/XREAL/Convert Canvases To World Space")]
    public static void Convert()
    {
        Camera mainCamera = Camera.main;
        if (mainCamera == null)
        {
            EditorUtility.DisplayDialog("Convert Canvases To World Space",
                "No camera tagged MainCamera in the open scene. Open Assets/Scenes/Game.unity " +
                "and make sure the XREAL/XR Origin camera is tagged MainCamera, then run again.",
                "OK");
            return;
        }

        int converted = 0;

        foreach (string name in CanvasNames)
        {
            GameObject go = GameObject.Find(name);
            if (go == null)
            {
                Debug.LogWarning($"[XREALCanvasConversion] No GameObject named '{name}' in the open scene — skipped.");
                continue;
            }

            Canvas canvas = go.GetComponent<Canvas>();
            if (canvas == null)
            {
                Debug.LogWarning($"[XREALCanvasConversion] '{name}' has no Canvas component — skipped.");
                continue;
            }

            Undo.RecordObject(canvas, "Convert Canvas To World Space");
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = mainCamera;

            Transform t = canvas.transform;
            Undo.SetTransformParent(t, mainCamera.transform, "Parent Canvas To Camera");
            t.localPosition = new Vector3(0f, 0f, DistanceFromCamera);
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one * WorldScale;

            EditorUtility.SetDirty(canvas);
            converted++;
        }

        if (converted > 0)
            EditorSceneManager.MarkSceneDirty(mainCamera.gameObject.scene);

        Debug.Log($"[XREALCanvasConversion] Converted {converted}/{CanvasNames.Length} canvases to World Space. " +
                  "Save the scene (Ctrl+S), then field-test on the One Pro — placement/scale is a first-pass default.");
    }
}
