using System.Collections;
using UnityEngine;

/// <summary>
/// Guarantees the game's window stays capturable, so XREAL Eye mixed reality recording
/// gets the virtual layer instead of a black rectangle.
///
/// XREAL's docs say "screen capture of the virtual space depends on the application's
/// permission to be recorded. If the app does not allow recording, recording may fail or
/// result in a black screen." On Android that permission is not a manifest entry — it is
/// the absence of WindowManager.LayoutParams.FLAG_SECURE on the activity window. A window
/// carrying that flag is skipped by MediaProjection, which is what the Beam Pro recorder
/// uses to grab the AR layer.
///
/// Sonic Snow never sets FLAG_SECURE itself, and neither does anything in the merged
/// manifest, so recording should already work. This clears it anyway: the flag can be set
/// by any library sharing the activity, it costs nothing to clear, and the log line it
/// leaves turns "the recording came out black" from a guess into something logcat answers.
///
/// Runs twice — once at startup and once after the XR session has had time to come up —
/// because a flag set later by the SDK would otherwise survive a single early clear.
/// </summary>
public static class AllowScreenRecording
{
    /// <summary>WindowManager.LayoutParams.FLAG_SECURE. Not exposed by UnityEngine, so it
    /// is spelled out here rather than read off the Java class.</summary>
    private const int FlagSecure = 0x2000;

    /// <summary>How long to wait before the second clear. Long enough for the XREAL
    /// session to have started, short enough to be well before anyone reaches for the
    /// record button.</summary>
    private const float RecheckDelay = 5f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        Clear();

        GameObject host = new GameObject(nameof(AllowScreenRecording));
        Object.DontDestroyOnLoad(host);
        host.AddComponent<Recheck>();
#endif
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private class Recheck : MonoBehaviour
    {
        private IEnumerator Start()
        {
            yield return new WaitForSeconds(RecheckDelay);
            Clear();
            Destroy(gameObject);
        }
    }

    private static void Clear()
    {
        try
        {
            AndroidJavaObject activity;
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                activity = player.GetStatic<AndroidJavaObject>("currentActivity");

            // Window flags may only be touched from the UI thread. runOnUiThread posts the
            // runnable and returns immediately, so `activity` must NOT be disposed by a using
            // block out here — the lambda runs later, and an already-disposed handle throws an
            // NullReferenceException on the UI thread where nothing around this can catch it.
            // It gets disposed inside the lambda instead, once it has actually been used.
            activity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
            {
                try
                {
                    using (AndroidJavaObject window = activity.Call<AndroidJavaObject>("getWindow"))
                    using (AndroidJavaObject attributes = window.Call<AndroidJavaObject>("getAttributes"))
                    {
                        int flags = attributes.Get<int>("flags");
                        bool wasSecure = (flags & FlagSecure) != 0;

                        window.Call("clearFlags", FlagSecure);

                        Debug.Log(wasSecure
                            ? "[Recording] FLAG_SECURE was set and has been cleared — the window is now capturable."
                            : "[Recording] FLAG_SECURE not set; the window is capturable.");
                    }
                }
                catch (System.Exception e)
                {
                    // Same reasoning as the outer catch, but this one is load-bearing: an
                    // exception escaping a runnable surfaces as an unhandled UI-thread error.
                    Debug.LogWarning($"[Recording] Could not check the window's capture flags: {e.Message}");
                }
                finally
                {
                    activity.Dispose();
                }
            }));
        }
        catch (System.Exception e)
        {
            // Never worth taking the game down over — the app is capturable by default,
            // so a failure here means we could not confirm it, not that recording broke.
            Debug.LogWarning($"[Recording] Could not reach the activity to check capture flags: {e.Message}");
        }
    }
#endif
}
