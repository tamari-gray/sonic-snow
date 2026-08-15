using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Android;
using Unity.XR.XREAL;

#if UNITY_ANDROID && !UNITY_EDITOR
using GalleryDataProvider = Unity.XR.XREAL.NativeGalleryDataProvider;
#else
using GalleryDataProvider = Unity.XR.XREAL.MockGalleryDataProvider;
#endif

/// <summary>
/// Records the run as mixed-reality video through XREAL's capture API.
///
/// Android's built-in screen recorder can't capture this game at all: XR content renders to
/// the glasses' secondary display (displayId 4 — the SDK logs "start GlassesDisplay in
/// second screen"), while the screen recorder only sees the primary display. XREAL's own
/// capture pipeline is the supported path, and it's a better result anyway —
/// <see cref="BlendMode.Blend"/> composites the virtual content over the RGB camera feed, so
/// the recording shows the real slope and the finish beam together rather than a flat UI grab.
///
/// Requires the XREAL Eye (RGB camera) attached; without it there's no camera feed to blend
/// against. StartVideoModeAsync is called with autoAdaptBlendMode, so an unsupported mode
/// degrades rather than failing hard.
///
/// Lifecycle is driven by GameLogic: recording starts once calibration completes, and stops
/// at the finish line. OnApplicationPause/Quit also stop it, so backgrounding or killing the
/// app still flushes a playable file instead of leaving a truncated one.
/// </summary>
public class RaceRecorder : MonoBehaviour
{
    public static RaceRecorder Instance;

    public enum ResolutionLevel { High, Middle, Low }

    [Header("Capture")]
    [Tooltip("Blend composites virtual content over the camera feed — the mixed-reality view. " +
             "CameraOnly is the bare real world, VirtualOnly the game render alone.")]
    [SerializeField] private BlendMode blendMode = BlendMode.Blend;

    [Tooltip("Highest resolution the device reports. Drop this if recording costs too much " +
             "framerate during a run.")]
    [SerializeField] private ResolutionLevel resolutionLevel = ResolutionLevel.High;

    [Tooltip("ApplicationAudio records the game's own sound only and needs no extra permission. " +
             "Anything including MicAudio requires RECORD_AUDIO, which prompts the rider mid-run " +
             "if it hasn't already been granted.")]
    [SerializeField] private AudioState audioState = AudioState.ApplicationAudio;

    [Tooltip("Copy finished recordings into the device gallery, so they can be pulled off " +
             "without adb.")]
    [SerializeField] private bool saveToGallery = true;

    [Tooltip("Set false to disable recording entirely without unwiring anything.")]
    [SerializeField] private bool recordingEnabled = true;

    private XREALVideoCapture videoCapture;
    private GalleryDataProvider gallery;

    /// <summary>Guards against double-starts and against stopping a session that never began.</summary>
    public bool IsRecording => videoCapture != null && videoCapture.IsRecording;

    private string VideoSavePath
    {
        get
        {
            string stamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
            return Path.Combine(Application.persistentDataPath, $"SonicSnow_Run_{stamp}.mp4");
        }
    }

    private bool permissionsRequested;

    private void Awake()
    {
        Instance = this;
    }

    /// <summary>
    /// Fires the camera/mic permission dialogs without starting capture. Call this as early
    /// as possible — ideally while the calibration screen is still up — so the OS dialog is
    /// already resolved by the time <see cref="StartRecording"/> actually runs.
    ///
    /// Without this, StartRecording() kicks off the same request as a fire-and-forget
    /// coroutine and returns immediately; whatever GameLogic does next (showing the
    /// leaderboard) renders synchronously, while the real OS dialog takes a moment longer to
    /// appear — so the dialog visibly lands over the leaderboard instead of over calibration,
    /// even though nothing about the leaderboard actually triggered it.
    /// </summary>
    public void RequestPermissionsEarly()
    {
        if (!recordingEnabled || permissionsRequested) return;

        permissionsRequested = true;
        StartCoroutine(RequestPermissions());
    }

    /// <summary>Called by GameLogic once the calibration screen clears.</summary>
    public void StartRecording()
    {
        if (!recordingEnabled)
        {
            Debug.Log("[RaceRecorder] Recording disabled — skipping.");
            return;
        }

        if (IsRecording)
        {
            Debug.LogWarning("[RaceRecorder] Already recording — ignoring start request.");
            return;
        }

        StartCoroutine(RequestPermissionsThenRecord());
    }

    /// <summary>
    /// Asks for camera (and mic, if audio is being captured) before touching the capture API,
    /// which fails silently without them. If RequestPermissionsEarly() already ran, both
    /// permission checks below resolve instantly with no dialog — this is what makes
    /// calling it early actually remove the delay rather than just moving it.
    /// </summary>
    private IEnumerator RequestPermissionsThenRecord()
    {
        yield return RequestPermissions();

        if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
        {
            Debug.LogError("[RaceRecorder] Camera permission denied — can't record. " +
                           "Grant it in Android settings and relaunch.");
            yield break;
        }

        if (videoCapture != null)
        {
            BeginVideoMode();
            yield break;
        }

        XREALVideoCaptureUtility.CreateAsync(false, capture =>
        {
            if (capture == null)
            {
                Debug.LogError("[RaceRecorder] Failed to create the video capture instance — " +
                               "is the XREAL Eye attached? Continuing without recording.");
                return;
            }

            videoCapture = capture;
            BeginVideoMode();
        });
    }

    private IEnumerator RequestPermissions()
    {
        yield return RequestPermission(Permission.Camera, "camera");

        if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
        {
            Debug.LogError("[RaceRecorder] Camera permission denied — recording won't be available.");
            yield break;
        }

        if (audioState != AudioState.None)
        {
            yield return RequestPermission(Permission.Microphone, "microphone");

            if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
            {
                // Not fatal — drop to a silent recording rather than losing the footage.
                Debug.LogWarning("[RaceRecorder] Microphone permission denied — recording without audio.");
                audioState = AudioState.None;
            }
        }
    }

    /// <summary>
    /// Prompts for a permission and waits for the user to answer, capped so a dismissed or
    /// never-shown dialog can't hang the coroutine forever. Same shape as LocationHandler's
    /// permission wait.
    /// </summary>
    private static IEnumerator RequestPermission(string permission, string label)
    {
        if (Permission.HasUserAuthorizedPermission(permission)) yield break;

        Debug.Log($"[RaceRecorder] Requesting {label} permission.");
        Permission.RequestUserPermission(permission);

        float waited = 0f;
        while (!Permission.HasUserAuthorizedPermission(permission) && waited < 15f)
        {
            waited += Time.deltaTime;
            yield return null;
        }
    }

    private void BeginVideoMode()
    {
        Resolution resolution = ResolutionFor(resolutionLevel);

        var parameters = new CameraParameters
        {
            // Fully qualified: UnityEngine.CameraType also exists and collides here.
            cameraType = Unity.XR.XREAL.CameraType.RGB,
            hologramOpacity = 0f,
            frameRate = NativeConstants.RECORD_FPS_DEFAULT,
            cameraResolutionWidth = resolution.width,
            cameraResolutionHeight = resolution.height,
            pixelFormat = CapturePixelFormat.PNG,
            blendMode = blendMode,
            audioState = audioState,
            captureSide = CaptureSide.Single,
            backgroundColor = Color.black,
        };

        videoCapture.StartVideoModeAsync(parameters, OnVideoModeStarted, true);
    }

    private void OnVideoModeStarted(XREALVideoCapture.VideoCaptureResult result)
    {
        if (!result.success)
        {
            Debug.LogError($"[RaceRecorder] Couldn't start video mode ({result.resultType}). " +
                           "Continuing without recording.");
            return;
        }

        videoCapture.StartRecordingAsync(VideoSavePath, r =>
        {
            if (r.success)
                Debug.Log("[RaceRecorder] Recording started.");
            else
                Debug.LogError($"[RaceRecorder] Couldn't start recording ({r.resultType}).");
        });
    }

    /// <summary>Called by GameLogic at the finish line, and on pause/quit.</summary>
    public void StopRecording()
    {
        if (!IsRecording) return;

        Debug.Log("[RaceRecorder] Stopping recording.");
        videoCapture.StopRecordingAsync(OnStoppedRecording);
    }

    private void OnStoppedRecording(XREALVideoCapture.VideoCaptureResult result)
    {
        if (!result.success)
            Debug.LogError($"[RaceRecorder] Stop-recording reported failure ({result.resultType}) — " +
                           "the file may be incomplete.");

        videoCapture.StopVideoModeAsync(OnStoppedVideoMode);
    }

    private void OnStoppedVideoMode(XREALVideoCapture.VideoCaptureResult result)
    {
        string path = null;

        if (videoCapture?.GetContext()?.GetEncoder() is VideoEncoder encoder)
            path = encoder.EncodeConfig.outPutPath;

        Debug.Log($"[RaceRecorder] Recording saved to {path ?? "(unknown path)"}");

        if (saveToGallery && !string.IsNullOrEmpty(path))
            InsertToGallery(path);

        videoCapture?.Dispose();
        videoCapture = null;
    }

    private void InsertToGallery(string path)
    {
        try
        {
            gallery ??= new GalleryDataProvider();
            gallery.InsertVideo(path, Path.GetFileName(path), "SonicSnow");
        }
        catch (Exception e)
        {
            // A gallery failure shouldn't look like a lost recording — the file is on disk
            // regardless, and can still be pulled with adb.
            Debug.LogError($"[RaceRecorder] Saved the recording but couldn't add it to the gallery: {e.Message}");
        }
    }

    private static Resolution ResolutionFor(ResolutionLevel level)
    {
        var resolutions = XREALVideoCaptureUtility.SupportedResolutions
            .OrderByDescending(r => r.width * r.height)
            .ToList();

        if (resolutions.Count == 0) return default;

        int index = level switch
        {
            ResolutionLevel.High => 0,
            ResolutionLevel.Middle => 1,
            _ => 2,
        };

        // Not every device reports three tiers — clamp rather than throwing.
        return resolutions[Mathf.Min(index, resolutions.Count - 1)];
    }

    // Backgrounding the app tears the camera down underneath us, so flush the file first
    // rather than leaving an unplayable fragment.
    private void OnApplicationPause(bool paused)
    {
        if (paused) StopRecording();
    }

    private void OnApplicationQuit()
    {
        StopRecording();
    }

    private void OnDestroy()
    {
        videoCapture?.Dispose();
        videoCapture = null;
    }
}
