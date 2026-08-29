using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using Unity.XR.XREAL;

/// <summary>
/// Drops the RGBCameraCapture component into the scene, wired for Blend mode. Same menu-command
/// pattern every other screen in this project is built with (see CalibrationScreenSetup,
/// FinishScoreSetup, FirstPersonCaptureSetup) — nothing here is expected to be hand-placed in the
/// Unity editor, and re-running is idempotent.
///
/// Unlike FirstPersonCaptureSetup this builds no Canvas and no buttons. Recording is driven purely
/// by the race flow — CalibrationScreen.RequestRecordingPermissionAndStart() starts it as soon as
/// the player accepts the recording permission/consent prompts (its own calibration condition),
/// and GameLogic.OnFinishLineReached() stops it 5 seconds later. There is no other stop trigger —
/// RGBCameraCapture.stopAfterSeconds (a fallback auto-stop) was removed by request, so a run that
/// never reaches the finish line saves nothing (see RGBCameraCapture.StopRecording's doc comment).
/// Nothing for a player to press either way, and the old setup's hidden-canvas dance (build two
/// buttons, then disable the canvas because no input can reach them) was pure ceremony.
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
        // AudioState.None, not ApplicationAndMicAudio. The mic path failed continuously on device
        // (AudioFlinger logging "PatchRecord getNextBuffer failed" / "dropping 192 frames" thousands
        // of lines a second), which both flooded logcat badly enough to evict every [CaptureDiag]
        // line within seconds — making the capture unmeasurable — and put a failing audio thread
        // next to the render loop. Footage from this recorder is meant to be laid over
        // separately-shot headcam video, which carries its own audio, so nothing needs the mic.
        serialized.FindProperty("audioState").enumValueIndex = (int)AudioState.None;
        serialized.FindProperty("captureSide").enumValueIndex = (int)CaptureSide.Single;
        serialized.FindProperty("cullingMask").intValue = -1;
        serialized.FindProperty("useGreenBackGround").boolValue = false;
        serialized.FindProperty("insertIntoGallery").boolValue = true;
        serialized.FindProperty("renderMode").enumValueIndex = (int)FrameBlender.CaptureRenderMode.Auto;
        serialized.FindProperty("captureRenderFps").floatValue = 15f;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(root);

        AddDiagnostics(root);
        DisableConflictingARCameraManager();

        Debug.Log("[RGBCameraCaptureSetup] RGBCameraCapture wired for Blend mode. Starts as soon as " +
                  "the player accepts the recording permission calibration condition, stops only at " +
                  "GameLogic.OnFinishLineReached() (5s after) -- no auto-stop fallback anymore, so if " +
                  "the finish line is never reached recording runs until the app is killed and nothing " +
                  "is saved (see RGBCameraCapture.StopRecording's doc comment). Requires CAMERA + RECORD_AUDIO + " +
                  "FOREGROUND_SERVICE + FOREGROUND_SERVICE_MEDIA_PROJECTION, all already present in " +
                  "XREALSettings.");
    }

    /// <summary>
    /// Puts CaptureFrameDiagnostics on the same object. It sits idle until a capture is actually
    /// running, then logs app fps, capture-camera renders/s, encoder commits/s and — the number that
    /// settles the argument — how many of those renders produced pixels different from the previous
    /// frame. Every theory about this stutter so far has been inferred from source or from
    /// after-the-fact mp4 box scans; this measures it on device in one run.
    /// </summary>
    private static void AddDiagnostics(GameObject root)
    {
        if (root.GetComponent<CaptureFrameDiagnostics>() == null)
        {
            root.AddComponent<CaptureFrameDiagnostics>();
            Debug.Log("[RGBCameraCaptureSetup] Added CaptureFrameDiagnostics — watch logcat for " +
                      "[CaptureDiag] lines during a race.");
        }
    }

    [MenuItem("Sonic Snow/XREAL/Recording Mode/AR Only (for overlay)")]
    public static void SetModeArOnly() => SetBlendMode(BlendMode.VirtualOnly);

    [MenuItem("Sonic Snow/XREAL/Recording Mode/Real World + AR (Blend)")]
    public static void SetModeBlend() => SetBlendMode(BlendMode.Blend);

    /// <summary>
    /// Switches what the recorder actually writes.
    ///
    /// VirtualOnly records the AR layer alone against a black background — see FrameBlender's
    /// BlendMode switch, where it is the one case that calls CameraRenderToTarget(false) and so
    /// never composites the RGB frame in. That is the mode to use for footage meant to be laid
    /// over separately-shot headcam video: the content in this game is emissive (glowing domes,
    /// beam, retro text) on black, so it composites with a Screen or Add blend in any editor
    /// with no keying at all. Chroma key is the worse option here and useGreenBackGround is
    /// deliberately left false — green fringes every glow edge and the soft alpha falls apart.
    ///
    /// Note this does NOT by itself stop the RGB camera: FrameBlender.OnFrame is still driven by
    /// camera frames (it takes frame.timeStamp from them) even when it ignores their pixels, so
    /// treat any frame-rate benefit as unproven until measured on device with CaptureFrameDiagnostics.
    /// </summary>
    private static void SetBlendMode(BlendMode mode)
    {
        RGBCameraCapture capture = Object.FindAnyObjectByType<RGBCameraCapture>(FindObjectsInactive.Include);

        if (capture == null)
        {
            Debug.LogWarning("[RGBCameraCaptureSetup] No RGBCameraCapture in the scene — run " +
                             "\"Set Up RGB Camera Capture\" first.");
            return;
        }

        SerializedObject serialized = new SerializedObject(capture);
        serialized.FindProperty("blendMode").enumValueIndex = (int)mode;
        serialized.FindProperty("useGreenBackGround").boolValue = false;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(capture);
        Debug.Log($"[RGBCameraCaptureSetup] Capture blendMode set to {mode} in the scene. " +
                  "Save the scene for it to reach a build. Note RGBCameraCapture.EffectiveBlendMode " +
                  "reports what the SDK actually ran with — it silently downgrades Blend to " +
                  "VirtualOnly when the RGB camera is busy.");
    }

    [MenuItem("Sonic Snow/XREAL/Enable Race Recording")]
    public static void EnableRecording() => SetRecording(true);

    [MenuItem("Sonic Snow/XREAL/Disable Race Recording")]
    public static void DisableRecording() => SetRecording(false);

    /// <summary>
    /// Flips CalibrationScreen.recordingEnabled in the scene. The serialized scene value beats the
    /// C# field initialiser every time, so this — not the field default — is what a build ships.
    ///
    /// Deliberately NOT called from SetUp() any more. It used to be, on the reasoning that a scene
    /// carrying recordingEnabled: 0 would leave the whole capture dormant with nothing in the logs
    /// to explain why (which is how this feature "mysteriously died" once before). But recording is
    /// now a thing we turn off on purpose — it costs a second full scene render per RGB frame, and
    /// A/B-ing it against the live jitter is a routine test. Having SetUp() silently turn it back on
    /// makes that test unrepeatable, which is the worse failure of the two. Use the two menu items
    /// above, and check the "Recording permission" line on the calibration screen if you're unsure
    /// which way the scene is currently set — it reads "recording off" when this is false.
    /// </summary>
    private static void SetRecording(bool on)
    {
        CalibrationScreen calibration = Object.FindAnyObjectByType<CalibrationScreen>(FindObjectsInactive.Include);

        if (calibration == null)
        {
            Debug.LogWarning("[RGBCameraCaptureSetup] No CalibrationScreen in the scene — recording " +
                             "has no trigger and will never start.");
            return;
        }

        SerializedObject serialized = new SerializedObject(calibration);
        serialized.FindProperty("recordingEnabled").boolValue = on;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(calibration);
        Debug.Log($"[RGBCameraCaptureSetup] CalibrationScreen.recordingEnabled set to {on} in the scene. " +
                  "Save the scene for it to reach a build.");
    }

    /// <summary>
    /// Disables Main Camera's ARCameraManager. ROOT CAUSE, confirmed on-device via a bisection on the
    /// "test" branch (see xreal-first-person-capture memory): ARCameraManager's AR Foundation
    /// lifecycle (SubsystemLifecycleManager.OnEnable) calls
    /// Unity.XR.XREAL.XREALCameraProvider.Start(), which opens the SAME native RGB camera resource
    /// RGBCameraCapture's own XREALVideoCapture path needs for recording. Two consumers pulling from
    /// one camera stream -- the recorder loses that contention almost every frame, landing a fresh
    /// image roughly once every few seconds instead of every frame (measured ~0-1 renders/s). With
    /// just ARCameraManager disabled and nothing else changed, a clean on-device test recovered
    /// ~27-30 renders/s (matching xreal-hello-world's own working Blend-mode capture, which never had
    /// an ARCameraManager in its scene at all).
    ///
    /// ONLY ARCameraManager, not ARCameraBackground/AROcclusionManager too: disabling all three
    /// together hung the whole app on launch in testing (killed by Android's watchdog after ~90s,
    /// never crashed, never recorded) -- almost certainly a teardown-ordering issue between them.
    /// Leaving ARCameraBackground/AROcclusionManager enabled-but-inert (they subscribe to
    /// ARCameraManager's frameReceived event, which simply never fires while it's disabled) avoided
    /// the hang and is sufficient: nothing in this project's own code (CalibrationScreen, GeoAnchor)
    /// calls into either of them directly, only ARSession.state, which ARCameraManager does not own.
    /// </summary>
    private static void DisableConflictingARCameraManager()
    {
        Camera mainCamera = Camera.main;
        if (mainCamera == null)
        {
            Debug.LogWarning("[RGBCameraCaptureSetup] No MainCamera in the scene — cannot check for a " +
                             "conflicting ARCameraManager.");
            return;
        }

        ARCameraManager arCameraManager = mainCamera.GetComponent<ARCameraManager>();
        if (arCameraManager == null)
        {
            Debug.Log("[RGBCameraCaptureSetup] No ARCameraManager on Main Camera — nothing to disable.");
            return;
        }

        if (!arCameraManager.enabled)
        {
            Debug.Log("[RGBCameraCaptureSetup] ARCameraManager already disabled.");
            return;
        }

        arCameraManager.enabled = false;
        EditorUtility.SetDirty(arCameraManager);
        Debug.Log("[RGBCameraCaptureSetup] Disabled ARCameraManager on Main Camera — it was starting " +
                  "AR Foundation's own RGB camera stream, contending with RGBCameraCapture's recording " +
                  "for the same native camera resource.");
    }
}
