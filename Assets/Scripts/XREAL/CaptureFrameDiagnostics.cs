using UnityEngine;
using UnityEngine.Rendering;
using Unity.XR.XREAL;

/// <summary>
/// Measures, on device, the four rates that have been argued about in circles: how fast the app's
/// main loop runs, how often the capture pipeline is asked to render, how often it commits a frame
/// to the encoder, and — the one nobody has actually measured — how often those renders produce
/// pixels that are different from the previous frame's.
///
/// WHY THIS EXISTS
/// XREAL support's reading of our logcat was "Unity main thread not blocked, camera data not
/// lagging, no dropped frames in the recorded video, but the image only updated about 6 times, with
/// identical frames in between", and pointed at how frames are updated/rendered in the app. Every
/// theory since has been inferred from source or from mp4 box-scans after the fact. These four
/// numbers separate the candidate causes in a single run:
///
///   fps low, commits track fps        -> the app itself is slow; the glasses only look smooth
///                                        because the XREAL compositor reprojects the last frame.
///   fps ~60, commits ~30, new ~30     -> render and blend are healthy; the stutter is downstream,
///                                        in NativeEncoder/HWEncoderUpdateSurface.
///   fps ~60, commits ~30, new ~4      -> the render is being called and is not producing new
///                                        pixels: the URP/Camera.Render() problem FrameBlender's
///                                        local patch addresses.
///   renders != commits                -> frames are being committed without a matching render.
///
/// HOW "new" IS MEASURED
/// The blend RenderTexture is what VideoEncoder hands the native encoder — it caches
/// rt.GetNativeTexturePtr() once and re-reads that same surface forever, so the encoder's pixels
/// are literally this texture's contents. Each frame it is blitted down to a tiny probe texture and
/// read back asynchronously; a readback whose bytes differ from the previous one counts as a new
/// image. Async, so it never stalls the main thread, and 8x8 so the readback is 256 bytes. It is a
/// sampled measure, not a checksum of the full frame: two genuinely different frames that match at
/// all 64 probe points would be miscounted as one. In practice a moving timer, particles, or any
/// head motion at all changes them.
///
/// This is diagnostics, not gameplay. It costs one small blit and one small readback per frame, and
/// it only runs while a capture is actually in progress.
/// </summary>
public class CaptureFrameDiagnostics : MonoBehaviour
{
    [Tooltip("Seconds between summary lines. One a second is enough to see structure without " +
             "burying the rest of logcat.")]
    [SerializeField] private float logIntervalSeconds = 1f;

    [Tooltip("Also log the app's frame rate before recording starts, so there is a baseline to " +
             "compare the during-capture rate against. Capture doubles the scene render, so a drop " +
             "here is expected and worth quantifying.")]
    [SerializeField] private bool logBeforeRecording = true;

    [Tooltip("Edge length of the square the blend texture is sampled down to. Larger catches " +
             "smaller changes and costs more; 8 (64 pixels) has been plenty.")]
    [SerializeField] private int probeSize = 8;

    private RenderTexture probe;
    private byte[] lastProbeBytes;
    private int pendingReadbacks;
    private const int MaxPendingReadbacks = 3;

    private float windowStartTime;
    private int windowStartFrameCount;
    private int windowStartCommits;
    private int windowStartRenders;
    private int windowNewImages;

    private bool wasRecording;
    private bool readbackUnsupportedLogged;

    private void Awake()
    {
        ResetWindow();
    }

    private void OnDestroy()
    {
        if (probe != null)
        {
            probe.Release();
            probe = null;
        }
    }

    // LateUpdate, not Update: XREALRGBCameraTexture.Update() is what drives the whole capture
    // pipeline — TryAcquireLatestImage -> OnRGBCameraUpdate -> FrameBlender.OnFrame -> render +
    // commit — and Unity gives no ordering guarantee between two Updates. By LateUpdate this
    // frame's render and commit have both happened, so the counters and the texture agree.
    private void LateUpdate()
    {
        BlenderBase blender = GetBlender();
        bool recording = blender != null;

        if (recording && !wasRecording) OnRecordingStarted(blender);
        if (!recording && wasRecording) ResetWindow();
        wasRecording = recording;

        if (recording) SampleBlendTexture(blender.BlendTexture);

        if (!recording && !logBeforeRecording) return;

        float elapsed = Time.realtimeSinceStartup - windowStartTime;
        if (elapsed < logIntervalSeconds) return;

        int frames = Time.frameCount - windowStartFrameCount;
        float fps = frames / elapsed;

        if (recording)
        {
            int commits = blender.FrameCount - windowStartCommits;
            int renders = FrameBlender.RenderCount - windowStartRenders;

            Debug.Log($"[CaptureDiag] fps={fps:F1} renders={renders / elapsed:F1}/s " +
                      $"commits={commits / elapsed:F1}/s newImages={windowNewImages / elapsed:F1}/s " +
                      $"(over {elapsed:F1}s)");
        }
        else
        {
            Debug.Log($"[CaptureDiag] fps={fps:F1} (not recording, over {elapsed:F1}s)");
        }

        ResetWindow();
    }

    /// <summary>
    /// The live blender, or null when no capture is running. Reached through the capture component
    /// rather than cached: the SDK builds a new FrameCaptureContext, blender and encoder on every
    /// StartVideoModeAsync and destroys them on stop, so anything held across a start/stop is stale.
    /// </summary>
    private BlenderBase GetBlender()
    {
        RGBCameraCapture capture = RGBCameraCapture.Instance;
        if (capture == null || !capture.RecordingConfirmed) return null;

        FrameCaptureContext context = capture.CaptureContext;
        return context?.GetBlender();
    }

    private void OnRecordingStarted(BlenderBase blender)
    {
        lastProbeBytes = null;

        Debug.Log($"[CaptureDiag] Recording started. blendMode={blender.BlendMode} " +
                  $"blendTexture={blender.Width}x{blender.Height} " +
                  $"renderMode={FrameBlender.RenderMode} " +
                  $"srp={(GraphicsSettings.currentRenderPipeline != null ? GraphicsSettings.currentRenderPipeline.GetType().Name : "Built-in")} " +
                  $"asyncReadback={SystemInfo.supportsAsyncGPUReadback}");

        ResetWindow();
    }

    private void ResetWindow()
    {
        windowStartTime = Time.realtimeSinceStartup;
        windowStartFrameCount = Time.frameCount;
        windowStartCommits = 0;
        windowStartRenders = FrameBlender.RenderCount;
        windowNewImages = 0;

        BlenderBase blender = GetBlender();
        if (blender != null) windowStartCommits = blender.FrameCount;
    }

    private void SampleBlendTexture(RenderTexture blendTexture)
    {
        if (blendTexture == null) return;

        if (!SystemInfo.supportsAsyncGPUReadback)
        {
            // Deliberately not falling back to ReadPixels: that stalls the GPU every frame, which
            // would change the very timing this is here to measure.
            if (!readbackUnsupportedLogged)
            {
                readbackUnsupportedLogged = true;
                Debug.LogWarning("[CaptureDiag] No async GPU readback on this device — fps, renders " +
                                 "and commits are still measured, newImages will read 0.");
            }
            return;
        }

        if (pendingReadbacks >= MaxPendingReadbacks) return;

        if (probe == null)
        {
            int size = Mathf.Clamp(probeSize, 2, 64);
            probe = new RenderTexture(size, size, 0, RenderTextureFormat.ARGB32)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            probe.Create();
        }

        Graphics.Blit(blendTexture, probe);

        pendingReadbacks++;
        AsyncGPUReadback.Request(probe, 0, TextureFormat.RGBA32, OnProbeReadback);
    }

    private void OnProbeReadback(AsyncGPUReadbackRequest request)
    {
        pendingReadbacks--;

        if (request.hasError) return;

        var data = request.GetData<byte>();
        int length = data.Length;

        if (lastProbeBytes == null || lastProbeBytes.Length != length)
        {
            lastProbeBytes = new byte[length];
            data.CopyTo(lastProbeBytes);
            windowNewImages++;
            return;
        }

        bool changed = false;
        for (int i = 0; i < length; i++)
        {
            if (data[i] != lastProbeBytes[i])
            {
                changed = true;
                break;
            }
        }

        if (!changed) return;

        data.CopyTo(lastProbeBytes);
        windowNewImages++;
    }
}
