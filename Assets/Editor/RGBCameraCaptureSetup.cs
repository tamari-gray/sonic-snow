using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unity.XR.XREAL;

/// <summary>
/// Drops the RGBCameraCapture component into the scene, wired for Blend mode. Same menu-command
/// pattern every other screen in this project is built with (see CalibrationScreenSetup,
/// FinishScoreSetup, FirstPersonCaptureSetup) — nothing here is expected to be hand-placed in the
/// Unity editor, and re-running is idempotent.
///
/// Unlike FirstPersonCaptureSetup this builds no Canvas and no buttons. Recording is driven purely
/// by the race flow — CalibrationScreen.Finish() starts it, GameLogic.OnFinishLineReached() stops
/// it — so there is nothing for a player to press, and the old setup's hidden-canvas dance
/// (build two buttons, then disable the canvas because no input can reach them) was pure ceremony.
/// The component therefore sits on a bare GameObject at scene root. The SDK parents its own
/// capture rig under Camera.main by itself, from
/// FrameCaptureContext.GetCaptureBehaviourByMode, so this object's position is irrelevant.
///
/// WHY THIS PATH EXISTS
/// XREAL support reviewed our logcat and reported: Unity main thread not blocked, camera data not
/// lagging, no dropped frames in the recorded video — but the image only updated about 6 times,
/// with identical frames in between. They suggested trying the RGBCamera sample with Blend enabled
/// instead of FirstPersonStreammingCast. This is that sample's capture half, wired into the race.
///
/// WHAT THE SOURCE ACTUALLY SAYS, because this may well reproduce the same stutter:
///   1. VideoEncoder.Commit caches rt.GetNativeTexturePtr() ONCE and hands the native encoder a raw
///      pointer to a single RenderTexture. The mp4's frame count and timing come from how often
///      UpdateSurface is called; the pixels come from whatever is in that texture at the time. That
///      is exactly how you get "no dropped frames" and "the image only updated 6 times" at once.
///   2. FrameBlender fills that texture by disabling the capture camera and calling Camera.Render()
///      by hand — a built-in-render-pipeline immediate-mode API that assumes the render has
///      completed before the next line runs.
///   3. sonic-snow runs URP (GraphicsSettings m_CustomRenderPipeline is assigned). xreal-hello-world,
///      where XREAL's samples are known to work, has m_CustomRenderPipeline: {fileID: 0} and neither
///      project sets a per-quality override — so the samples are exercised on Built-in RP and this
///      project is not. Under URP, camera rendering is scheduled by the pipeline rather than run
///      inline, so that assumption does not hold.
///   4. The whole SDK contains no URP-aware rendering in this path: no SubmitRenderRequest, no
///      RenderSingleCamera, no RenderPipelineManager. The single URP reference anywhere in the
///      package is a UI-camera check in XREALVirtualController.
/// Ruled out while checking: MSAA (QualitySettings antiAliasing is 0 at every level, so the blend
/// RenderTexture is non-MSAA and there is no unresolved-surface problem) and Vulkan (Android
/// m_APIs is 0b = OpenGLES3, so no image-layout or synchronisation problem).
///
/// So if footage from this component still stutters, the cause is upstream of it and the fix is to
/// make FrameBlender render through URP. Setting blendMode to VirtualOnly on the component is the
/// cheapest confirmation: it takes the same Camera.Render() path with the RGB camera and background
/// quad removed entirely, so if that still updates ~6 times, nothing about the camera feed is
/// involved.
/// </summary>
public static class RGBCameraCaptureSetup
{
    private const string RootName = "RGBCameraCapture";
    private const string ScenePath = "Assets/Scenes/Game.unity";

    /// <summary>
    /// Batch-mode entry point, so this can be driven from the command line rather than by clicking
    /// the menu item — same pattern as XREALPortBatch.RunCanvasPort.
    /// </summary>
    public static void SetUpBatch()
    {
        EditorSceneManager.OpenScene(ScenePath);
        SetUp();
        bool saved = EditorSceneManager.SaveOpenScenes();
        Debug.Log("[RGBCameraCaptureSetup] Applied to " + ScenePath + ". Saved: " + saved);
    }

    [MenuItem("Sonic Snow/XREAL/Set Up RGB Camera Capture")]
    public static void SetUp()
    {
        GameObject root = GameObject.Find(RootName);
        if (root == null) root = new GameObject(RootName);

        RGBCameraCapture capture = root.GetComponent<RGBCameraCapture>();
        if (capture == null) capture = root.AddComponent<RGBCameraCapture>();

        // Written through SerializedObject rather than left to the field initialisers: the scene is
        // the authority, not the code default. Game.unity was saved before these fields existed, so
        // without this the component would deserialize with whatever the scene happens to hold —
        // exactly the trap recordingEnabled fell into when this feature was re-enabled.
        SerializedObject serialized = new SerializedObject(capture);
        serialized.FindProperty("blendMode").enumValueIndex = (int)BlendMode.Blend;
        serialized.FindProperty("resolutionLevel").enumValueIndex = (int)RGBCameraCapture.ResolutionLevel.High;
        serialized.FindProperty("audioState").enumValueIndex = (int)AudioState.ApplicationAndMicAudio;
        serialized.FindProperty("captureSide").enumValueIndex = (int)CaptureSide.Single;
        serialized.FindProperty("cullingMask").intValue = -1;
        serialized.FindProperty("useGreenBackGround").boolValue = false;
        serialized.FindProperty("insertIntoGallery").boolValue = true;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(root);

        EnableRecordingOnCalibrationScreen();

        Debug.Log("[RGBCameraCaptureSetup] RGBCameraCapture wired for Blend mode. Starts at " +
                  "CalibrationScreen.Finish(), stops at GameLogic.OnFinishLineReached(). Requires " +
                  "CAMERA + RECORD_AUDIO + FOREGROUND_SERVICE + FOREGROUND_SERVICE_MEDIA_PROJECTION, " +
                  "all already present in XREALSettings.");
    }

    /// <summary>
    /// Flips CalibrationScreen.recordingEnabled on in the scene. This is not optional housekeeping:
    /// Game.unity carries recordingEnabled: 0 from when the feature was switched off, and the
    /// serialized scene value beats the C# field initialiser every time. Skip this and the whole
    /// capture sits dormant with nothing in the logs to explain why — which is exactly how this
    /// feature "mysteriously died" once before.
    /// </summary>
    private static void EnableRecordingOnCalibrationScreen()
    {
        CalibrationScreen calibration = Object.FindAnyObjectByType<CalibrationScreen>(FindObjectsInactive.Include);

        if (calibration == null)
        {
            Debug.LogWarning("[RGBCameraCaptureSetup] No CalibrationScreen in the scene — recording " +
                             "has no trigger and will never start.");
            return;
        }

        SerializedObject serialized = new SerializedObject(calibration);
        SerializedProperty enabled = serialized.FindProperty("recordingEnabled");
        enabled.boolValue = true;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(calibration);
        Debug.Log("[RGBCameraCaptureSetup] CalibrationScreen.recordingEnabled set to true in the scene.");
    }
}
