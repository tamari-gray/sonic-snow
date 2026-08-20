using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using Unity.XR.XREAL;

#if UNITY_ANDROID && !UNITY_EDITOR
using GalleryDataProvider = Unity.XR.XREAL.NativeGalleryDataProvider;
#else
using GalleryDataProvider = Unity.XR.XREAL.MockGalleryDataProvider;
#endif

/// <summary>
/// Records the race as real world (XREAL Eye RGB camera) + AR content blended into one video,
/// driven entirely by the race flow: recording starts when calibration completes and stops when
/// the finish line is reached. No UI, no buttons — there is still no hand or controller input
/// wired up for this headset, so anything the player would have to press is unreachable.
///
/// This is the RGB camera capture path XREAL support pointed at
/// (docs.xreal.com/Camera/Access%20RGB%20Camera) after reviewing our logcat: rebuilt from their
/// RGBCameraAndCapture sample's CaptureExample with the sample UI stripped out. It replaces
/// FirstPersonStreammingCast as the wired-up capture. That component is deliberately left in the
/// project and in the scene, dormant, as the comparison path — telling the two apart is the whole
/// point of the exercise.
///
/// What is actually different from FirstPersonStreammingCast: not much, and that is deliberate.
/// Both end up in XREALVideoCapture then FrameCaptureContext then FrameBlender. The old component
/// additionally drags in LitJson and a whole Network stack for its streaming half, and only ever
/// reached StartVideoCapture through a coroutine shared with the RTP path. This is the same
/// producer with none of that, which makes it the clean A/B XREAL asked for rather than a rewrite
/// pretending to be a fix.
///
/// KNOWN ISSUE this does NOT fix on its own: FrameBlender drives the capture camera with
/// Camera.Render(), a built-in-render-pipeline immediate-mode call, while this project runs URP.
/// See RGBCameraCaptureSetup's header for the evidence. If the recording still shows a handful of
/// distinct images with identical frames between them, that is the cause, and it lives in the SDK
/// rather than here.
/// </summary>
public class RGBCameraCapture : MonoBehaviour
{
    /// <summary>Same singleton convention as GameLogic.Instance, CalibrationScreen.Instance, etc.</summary>
    public static RGBCameraCapture Instance;

    /// <summary>
    /// Mirrors FirstPersonStreammingCast.ResolutionLevel and XREAL's own sample. Note the supported
    /// list is sorted DESCENDING by pixel count, so Middle is the second-LARGEST supported
    /// resolution rather than a midpoint.
    /// </summary>
    public enum ResolutionLevel
    {
        High,
        Middle,
        Low,
    }

    [Header("Capture")]
    [Tooltip("Blend = real world (XREAL Eye) + AR content combined, which is the only reason this " +
             "path exists at all. Note the SDK silently downgrades this to VirtualOnly when the RGB " +
             "camera is unsupported or already busy — EffectiveBlendMode reports what actually ran.")]
    [SerializeField] private BlendMode blendMode = BlendMode.Blend;

    [Tooltip("High is the largest supported resolution. Resolution is NOT what drives the capture " +
             "stutter — dropping it to Low previously made things worse, not better — so this is set " +
             "for footage quality, not as a performance knob.")]
    [SerializeField] private ResolutionLevel resolutionLevel = ResolutionLevel.High;

    [Tooltip("ApplicationAndMicAudio captures game SFX and narration together, matching XREAL's own " +
             "sample default. Requires RECORD_AUDIO, which XREALSettings already grants.")]
    [SerializeField] private AudioState audioState = AudioState.ApplicationAndMicAudio;

    [SerializeField] private CaptureSide captureSide = CaptureSide.Single;
    [SerializeField] private LayerMask cullingMask = -1;
    [SerializeField] private bool useGreenBackGround = false;

    [Header("Gallery")]
    [Tooltip("Copies the finished file into the device gallery so it can be pulled off without adb. " +
             "The file is written to persistentDataPath either way.")]
    [SerializeField] private bool insertIntoGallery = true;

    private XREALVideoCapture m_VideoCapture;
    private GalleryDataProvider m_GalleryDataTool;
    private string m_VideoPath;

    /// <summary>
    /// True from the moment StartRecording is called until the file is finalized. This is the
    /// "asked to record" flag, NOT proof anything is being written — StartVideoModeAsync and
    /// StartRecordingAsync are both async and both sit behind the Android MediaProjection consent
    /// dialog. Use RecordingConfirmed when you need to know recording is actually running.
    /// </summary>
    public bool IsRecording { get; private set; }

    /// <summary>True only once OnStartedRecordingVideo has fired successfully — frames are landing.</summary>
    public bool RecordingConfirmed { get; private set; }

    /// <summary>True if either async step failed, e.g. the player denied the screen-capture prompt.</summary>
    public bool RecordingFailed { get; private set; }

    /// <summary>Human-readable reason behind RecordingFailed, for the logs.</summary>
    public string RecordingFailureReason { get; private set; }

    /// <summary>
    /// What the SDK actually recorded with, which is not necessarily the requested blendMode:
    /// FrameCaptureContext.AutoAdaptBlendMode falls back to VirtualOnly when the RGB camera is
    /// unsupported or busy. Worth reading before concluding the real world "didn't show up".
    /// </summary>
    public BlendMode EffectiveBlendMode { get; private set; }

    /// <summary>Where the current or last recording was written.</summary>
    public string VideoPath => m_VideoPath;

    private void Awake()
    {
        Instance = this;
    }

    private string BuildVideoSavePath()
    {
        string timeStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        return Path.Combine(Application.persistentDataPath, "SonicSnow_Race_" + timeStamp + ".mp4");
    }

    /// <summary>
    /// Starts recording. Called once, from CalibrationScreen.Finish() — i.e. after calibration has
    /// cleared, which by construction is the moment the player is standing at the start line with a
    /// good fix. Safe to call twice; the second call is ignored.
    /// </summary>
    public void StartRecording()
    {
        if (IsRecording)
        {
            Debug.LogWarning("[RGBCameraCapture] StartRecording ignored — already recording.");
            return;
        }

        IsRecording = true;
        RecordingConfirmed = false;
        RecordingFailed = false;
        RecordingFailureReason = null;

        if (m_VideoCapture == null)
        {
            Debug.Log("[RGBCameraCapture] Creating video capture instance...");
            XREALVideoCaptureUtility.CreateAsync(false, videoCapture =>
            {
                if (videoCapture == null)
                {
                    Fail("could not create a VideoCapture instance");
                    return;
                }

                Debug.Log("[RGBCameraCapture] Created VideoCapture Instance!");
                m_VideoCapture = videoCapture;
                StartVideoCapture();
            });
        }
        else
        {
            StartVideoCapture();
        }
    }

    /// <summary>
    /// Stops recording and finalizes the file. Called from GameLogic.OnFinishLineReached().
    ///
    /// This, not the start flag, is what has to run for anything to be retrievable: a recording
    /// killed mid-flight leaves an mp4 with no moov atom, which nothing will play. There is
    /// deliberately no OnApplicationPause/OnApplicationQuit safety net and no auto-segment timer —
    /// both existed once and were removed by explicit request. The accepted consequence is that if
    /// the finish line never fires, nothing is saved.
    /// </summary>
    public void StopRecording()
    {
        if (!IsRecording)
        {
            Debug.LogWarning("[RGBCameraCapture] StopRecording ignored — not recording.");
            return;
        }

        if (m_VideoCapture == null || !m_VideoCapture.IsRecording)
        {
            // Asked to stop before the async start chain ever completed — most likely the player
            // never answered the consent dialog. Nothing was written, so just reset.
            Debug.LogWarning("[RGBCameraCapture] Stop requested but recording never started — nothing to save.");
            IsRecording = false;
            RecordingConfirmed = false;
            return;
        }

        Debug.Log("[RGBCameraCapture] Stop Video Capture!");
        m_VideoCapture.StopRecordingAsync(OnStoppedRecordingVideo);
    }

    private void StartVideoCapture()
    {
        if (m_VideoCapture == null || m_VideoCapture.IsRecording)
        {
            Fail("cannot start video capture — no instance, or already recording");
            return;
        }

        Resolution cameraResolution = GetResolutionByLevel(resolutionLevel);

        CameraParameters cameraParameters = new CameraParameters
        {
            // Fully qualified: UnityEngine.CameraType exists too, and unlike FirstPersonStreammingCast
            // this class sits outside the Unity.XR.XREAL namespace, so the bare name is ambiguous.
            cameraType = Unity.XR.XREAL.CameraType.RGB,
            hologramOpacity = 0.0f,
            frameRate = NativeConstants.RECORD_FPS_DEFAULT,
            cameraResolutionWidth = cameraResolution.width,
            cameraResolutionHeight = cameraResolution.height,
            pixelFormat = CapturePixelFormat.PNG,
            blendMode = blendMode,
            audioState = audioState,
            captureSide = captureSide,
            backgroundColor = useGreenBackGround ? Color.green : Color.black,
        };

        Debug.Log("[RGBCameraCapture] Starting video mode: " +
                  cameraResolution.width + "x" + cameraResolution.height +
                  " (" + resolutionLevel + "), blend=" + blendMode + ", audio=" + audioState);

        // autoAdaptBlendMode left on: it degrades Blend to VirtualOnly rather than failing outright
        // when the RGB camera is busy, which is the difference between footage with no real world in
        // it and no footage at all. EffectiveBlendMode records which one we actually got.
        m_VideoCapture.StartVideoModeAsync(cameraParameters, OnStartedVideoCaptureMode, true);
    }

    private void OnStartedVideoCaptureMode(XREALVideoCapture.VideoCaptureResult result)
    {
        if (!result.success)
        {
            Fail("capture mode failed to start (" + result.resultType + ") — screen-capture prompt denied?");
            return;
        }

        Debug.Log("[RGBCameraCapture] Started Video Capture Mode!");

        FrameCaptureContext context = m_VideoCapture.GetContext();
        EffectiveBlendMode = context.GetBlender().BlendMode;

        if (EffectiveBlendMode != blendMode)
        {
            Debug.LogWarning("[RGBCameraCapture] Requested " + blendMode + " but the SDK adapted to " +
                             EffectiveBlendMode + " — the RGB camera is unsupported or already in use, " +
                             "so the real world will NOT be in this recording.");
        }

        CaptureBehaviourBase behaviour = context.GetBehaviour();
        if (behaviour != null && behaviour.CaptureCamera != null)
        {
            behaviour.CaptureCamera.cullingMask = cullingMask.value;
            behaviour.CaptureCamera.backgroundColor = useGreenBackGround ? Color.green : Color.black;
        }

        m_VideoPath = BuildVideoSavePath();
        m_VideoCapture.StartRecordingAsync(m_VideoPath, OnStartedRecordingVideo);
    }

    private void OnStartedRecordingVideo(XREALVideoCapture.VideoCaptureResult result)
    {
        if (!result.success)
        {
            Fail("recording failed to start (" + result.resultType + ")");
            return;
        }

        RecordingConfirmed = true;
        Debug.Log("[RGBCameraCapture] Started Recording Video! -> " + m_VideoPath);
    }

    private void OnStoppedRecordingVideo(XREALVideoCapture.VideoCaptureResult result)
    {
        if (!result.success)
        {
            Debug.LogError("[RGBCameraCapture] Stopped Recording Video Failed (" + result.resultType +
                           ") — the file may be unplayable (no moov atom).");
        }
        else
        {
            Debug.Log("[RGBCameraCapture] Stopped Recording Video!");
        }

        // Run this either way. StopVideoModeAsync is what tears the encoder down, and skipping it on
        // failure would leave the RGB camera and the MediaProjection session held open.
        m_VideoCapture.StopVideoModeAsync(OnStoppedVideoCaptureMode);
    }

    private void OnStoppedVideoCaptureMode(XREALVideoCapture.VideoCaptureResult result)
    {
        Debug.Log("[RGBCameraCapture] Stopped Video Capture Mode!");

        IsRecording = false;
        RecordingConfirmed = false;

        if (insertIntoGallery && !string.IsNullOrEmpty(m_VideoPath))
        {
            string displayName = "SonicSnow_Race_" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + ".mp4";
            StartCoroutine(DelayInsertVideoToGallery(m_VideoPath, displayName, "SonicSnow"));
        }

        if (m_VideoCapture != null)
        {
            m_VideoCapture.Dispose();
            m_VideoCapture = null;
        }
    }

    /// <summary>
    /// Waits a beat before handing the file to the gallery. The muxer finishes writing the moov
    /// atom slightly after StopVideoModeAsync reports back, and inserting too early registers a
    /// truncated file — same 0.1s delay XREAL's own sample uses.
    /// </summary>
    private IEnumerator DelayInsertVideoToGallery(string originFilePath, string displayName, string folderName)
    {
        yield return new WaitForSeconds(0.1f);

        Debug.Log("[RGBCameraCapture] InsertVideoToGallery: " + displayName + ", " +
                  originFilePath + " => " + folderName);

        if (m_GalleryDataTool == null) m_GalleryDataTool = new GalleryDataProvider();

        m_GalleryDataTool.InsertVideo(originFilePath, displayName, folderName);
    }

    /// <summary>
    /// Picks a resolution off the supported list, which is sorted largest-first. Clamped rather than
    /// indexed blindly: the sample assumes at least three supported resolutions and would throw on
    /// hardware reporting fewer.
    /// </summary>
    private Resolution GetResolutionByLevel(ResolutionLevel level)
    {
        Resolution[] resolutions = XREALVideoCaptureUtility.SupportedResolutions
            .OrderByDescending(res => res.width * res.height)
            .ToArray();

        if (resolutions.Length == 0)
        {
            Debug.LogError("[RGBCameraCapture] No supported capture resolutions reported — using 1280x720.");
            return new Resolution { width = 1280, height = 720 };
        }

        int index = Mathf.Clamp((int)level, 0, resolutions.Length - 1);
        return resolutions[index];
    }

    private void Fail(string reason)
    {
        IsRecording = false;
        RecordingConfirmed = false;
        RecordingFailed = true;
        RecordingFailureReason = reason;
        Debug.LogError("[RGBCameraCapture] " + reason);
    }

    private void OnDestroy()
    {
        if (m_VideoCapture != null)
        {
            m_VideoCapture.Dispose();
            m_VideoCapture = null;
        }
    }
}
