using System.Collections;
using UnityEngine;

/// <summary>
/// Test-branch harness for the XREAL RGB-capture stutter bisection (see xreal-first-person-capture
/// memory / commit history on "test"): drives RGBCameraCapture directly instead of going through
/// CalibrationScreen.Finish()/GameLogic.OnFinishLineReached(), so every version scene (V1-V4) starts
/// and stops recording on the same fixed timing with no on-device interaction required. Waits for
/// the XR session to settle, starts a recording, holds it for a fixed window once confirmed, stops
/// it, then quits the process so the PC side can pull the file + logcat and move to the next build.
///
/// Not used by V5 (Game.unity) or by production Sonic Snow -- that flow already has its own trigger
/// (CalibrationScreen/GameLogic) and its own stop cap (GameLogic.recordingStopAtRaceSeconds).
/// </summary>
public class AutoRecordAndQuit : MonoBehaviour
{
    [SerializeField] private float settleSeconds = 5f;
    [SerializeField] private float recordSeconds = 10f;
    [SerializeField] private float startTimeoutSeconds = 20f;
    [SerializeField] private float stopTimeoutSeconds = 15f;
    [SerializeField] private float quitDelaySeconds = 1f;

    IEnumerator Start()
    {
        Debug.Log("[AutoRecordAndQuit] Settling " + settleSeconds + "s before starting capture.");
        yield return new WaitForSeconds(settleSeconds);

        RGBCameraCapture capture = RGBCameraCapture.Instance;
        if (capture == null)
        {
            Debug.LogError("[AutoRecordAndQuit] No RGBCameraCapture in scene -- quitting.");
            yield return Quit();
            yield break;
        }

        Debug.Log("[AutoRecordAndQuit] Calling StartRecording().");
        capture.StartRecording();

        float elapsed = 0f;
        while (!capture.RecordingConfirmed && !capture.RecordingFailed && elapsed < startTimeoutSeconds)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (!capture.RecordingConfirmed)
        {
            Debug.LogError("[AutoRecordAndQuit] Recording never confirmed within " + startTimeoutSeconds +
                            "s (failed=" + capture.RecordingFailed + ", reason=" +
                            capture.RecordingFailureReason + "). Quitting anyway.");
            yield return Quit();
            yield break;
        }

        Debug.Log("[AutoRecordAndQuit] Recording confirmed -- recording for " + recordSeconds + "s.");
        while (Time.time - capture.RecordingConfirmedTime < recordSeconds)
            yield return null;

        Debug.Log("[AutoRecordAndQuit] Calling StopRecording().");
        capture.StopRecording();

        elapsed = 0f;
        while (capture.IsRecording && elapsed < stopTimeoutSeconds)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        Debug.Log("[AutoRecordAndQuit] Capture finished (IsRecording=" + capture.IsRecording + "). Quitting.");
        yield return Quit();
    }

    IEnumerator Quit()
    {
        yield return new WaitForSeconds(quitDelaySeconds);
        Debug.Log("[AutoRecordAndQuit] Application.Quit()");
        Application.Quit();
    }
}
