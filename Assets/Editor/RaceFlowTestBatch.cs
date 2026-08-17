using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Drives <see cref="RaceFlowSelfTest"/> from the command line: opens the game scene,
/// enters Play mode, and quits with an exit code once the flow test reports.
///
/// Run it with the enable variable set, or the test installs nothing and this hangs
/// until the timeout:
///
///   SONIC_SNOW_FLOW_TEST=1 Unity.exe -batchmode -projectPath . \
///       -executeMethod RaceFlowTestBatch.Run -logFile flowtest.log
///
/// Note there is no -quit: Play mode has to keep running after Run() returns, so the
/// quitting is done here instead, via EditorApplication.Exit.
/// Exit codes: 0 pass, 1 one or more checks failed, 2 timed out.
/// </summary>
public static class RaceFlowTestBatch
{
    private const string ScenePath = "Assets/Scenes/Game.unity";

    // Entering Play mode reloads the domain, which drops the update subscription and
    // every static in this class — so the fact that a run is in progress lives in
    // SessionState, which survives it.
    private const string ActiveKey = "SonicSnow.FlowTest.Active";
    private const string DeadlineKey = "SonicSnow.FlowTest.Deadline";

    private const double TimeoutSeconds = 180d;

    public static void Run()
    {
        Debug.Log("[FlowTestBatch] Opening the scene and entering Play mode.");

        EditorSceneManager.OpenScene(ScenePath);

        SessionState.SetBool(ActiveKey, true);
        SessionState.SetFloat(DeadlineKey, (float)(EditorApplication.timeSinceStartup + TimeoutSeconds));

        Subscribe();

        EditorApplication.EnterPlaymode();
    }

    [InitializeOnLoadMethod]
    private static void ReattachAfterDomainReload()
    {
        if (SessionState.GetBool(ActiveKey, false)) Subscribe();
    }

    private static void Subscribe()
    {
        EditorApplication.update -= Poll;
        EditorApplication.update += Poll;
    }

    private static void Poll()
    {
        if (!SessionState.GetBool(ActiveKey, false)) return;

        if (RaceFlowSelfTest.Finished)
        {
            Quit(RaceFlowSelfTest.Passed ? 0 : 1,
                 RaceFlowSelfTest.Passed ? "flow test passed" : "flow test reported failures");
            return;
        }

        if (EditorApplication.timeSinceStartup > SessionState.GetFloat(DeadlineKey, 0f))
        {
            Quit(2, $"flow test did not report within {TimeoutSeconds:F0}s");
        }
    }

    private static bool quitting;

    private static void Quit(int code, string why)
    {
        // Exit doesn't take effect until the editor unwinds, and Poll keeps firing in
        // the meantime — without this the log fills with repeat quit messages.
        if (quitting) return;
        quitting = true;

        SessionState.SetBool(ActiveKey, false);
        EditorApplication.update -= Poll;

        Debug.Log($"[FlowTestBatch] Exiting with code {code} — {why}.");

        EditorApplication.Exit(code);
    }
}
