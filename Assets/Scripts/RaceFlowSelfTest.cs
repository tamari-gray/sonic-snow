using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Walks a virtual rider down a synthetic route and checks the whole flow lands in the
/// right order: calibration, start gate, countdown, checkpoints, finish.
///
/// Exists because the real thing can only be tested by walking a hill with a GPS fix,
/// which is a slow way to find out that a screen fired at the wrong moment. Everything
/// downstream of a fix — proximity, state machine, countdown, spawning, scoring — is
/// plain logic, so feeding it mock fixes exercises all of it without leaving the desk.
///
/// What it does NOT cover: AR tracking, GeoAnchor's motion fit (there is no real camera
/// motion), rendering, and anything device-specific. A pass means the flow is right, not
/// that placement is accurate.
///
/// Inert unless the SONIC_SNOW_FLOW_TEST environment variable is "1", so it never runs
/// in a normal Editor session and never does anything in a device build. Drive it with
/// Assets/Editor/RaceFlowTestBatch.cs.
/// </summary>
public class RaceFlowSelfTest : MonoBehaviour
{
    public const string EnableVariable = "SONIC_SNOW_FLOW_TEST";

    /// <summary>Set when the run ends, however it ends. Polled by the batch runner.</summary>
    public static bool Finished { get; private set; }

    /// <summary>Whether every check passed. Only meaningful once <see cref="Finished"/>.</summary>
    public static bool Passed { get; private set; }

    // A quiet stretch of nowhere. Absolute position is irrelevant — everything downstream
    // works in metres relative to the origin — but a real latitude keeps the metres-per-
    // degree conversion honest rather than testing it at the equator.
    private const double OriginLat = 46.5000;
    private const double OriginLng = 11.3500;
    private const double OriginAlt = 2400d;

    private const float CheckpointSpacing = 80f;   // metres down the fall line
    private const int CheckpointCount = 3;
    private const float FinishDistance = 320f;

    private static readonly List<string> failures = new List<string>();
    private static int checks;

    // Wall-clock timing for each step of the flow, reported alongside pass/fail so a
    // slow step (or a step that got faster/slower after a change) is visible without
    // digging through the raw log. Real time, not frame count — Time.realtimeSinceStartup
    // is unaffected by Time.timeScale, so this reads the same as a stopwatch would.
    private static readonly List<(string label, float sinceStart, float delta)> timings =
        new List<(string, float, float)>();

    private static float flowStartTime;
    private static float lastMarkTime;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install()
    {
        if (Environment.GetEnvironmentVariable(EnableVariable) != "1") return;

        // Before the scene loads, so MapDataFetcher never reaches the network and
        // LocationHandler never starts the device service.
        LocationHandler.MockMode = true;
        MapDataFetcher.MockRoute = BuildRoute();

        GameObject host = new GameObject(nameof(RaceFlowSelfTest));
        DontDestroyOnLoad(host);
        host.AddComponent<RaceFlowSelfTest>();

        Debug.Log("[FlowTest] Installed — running the end-to-end race flow on mock GPS.");
    }

    private static MapData BuildRoute()
    {
        MapData route = new MapData
        {
            originLat = OriginLat,
            originLng = OriginLng,
            originAlt = OriginAlt,
            finishLat = LatAfter(FinishDistance),
            finishLng = OriginLng,
            finishAlt = OriginAlt - 90d,
            checkpoints = new CheckpointData[CheckpointCount]
        };

        for (int i = 0; i < CheckpointCount; i++)
        {
            float down = CheckpointSpacing * (i + 1);

            route.checkpoints[i] = new CheckpointData
            {
                lat = LatAfter(down),
                lng = OriginLng,
                alt = OriginAlt - down * 0.28d,
            };
        }

        return route;
    }

    /// <summary>Latitude this many metres north of the origin. The route runs due north.</summary>
    private static double LatAfter(double metres)
    {
        // Same series GpsUtils uses, so the distances the game measures back out to the
        // ones asked for here rather than being a few tenths of a percent off.
        double latRad = OriginLat * Math.PI / 180.0;
        double mPerDegLat = 111132.92 - 559.82 * Math.Cos(2.0 * latRad) + 1.175 * Math.Cos(4.0 * latRad);

        return OriginLat + metres / mPerDegLat;
    }

    private void Start()
    {
        StartCoroutine(RunFlow());
    }

    private IEnumerator RunFlow()
    {
        flowStartTime = Time.realtimeSinceStartup;
        lastMarkTime = flowStartTime;

        yield return new WaitUntil(() => GameLogic.Instance != null && LocationHandler.Instance != null);
        Mark("scene ready (GameLogic + LocationHandler alive)");

        MapData route = MapDataFetcher.MockRoute;

        // The rider is standing on the start line from the first frame, with a fix good
        // enough to trigger. This is the exact condition that used to start a race behind
        // the calibration screen.
        StandAt(0f);
        Mark("rider placed at start gate, calibration screen up");

        // --- calibration holds everything ------------------------------------------
        // This 2s isn't a real wait the test is measuring — it's a deliberate pause so
        // the "nothing happens yet" checks below have time to fail if they're going to.
        // Real calibration on-device takes as long as AR tracking + a GPS fix take, which
        // this mock skips entirely via ForceReady below — see the timing caveat in Report().
        yield return new WaitForSeconds(2f);

        Check("nothing starts while calibration is up",
              GameLogic.Instance.CurrentState == GameLogic.GameState.SearchingForStart
              && !GameLogic.Instance.IsArmed,
              $"state was {GameLogic.Instance.CurrentState}, armed={GameLogic.Instance.IsArmed}");

        Check("no countdown during calibration",
              CountdownTimer.instance == null || !CountdownTimer.instance.IsRunning);

        Check("race clock not running during calibration",
              RaceTimer.instance == null || !RaceTimer.instance.IsRunning);

        Check("no checkpoints spawned during calibration",
              CheckpointDomeSpawner.Instance == null || CheckpointDomeSpawner.Instance.Total == 0);

        // --- calibration clears, search begins --------------------------------------
        if (CalibrationScreen.Instance == null)
        {
            Fail("no CalibrationScreen in the scene");
        }
        else
        {
            CalibrationScreen.Instance.ForceReady("flow test — no AR session in the Editor");
        }
        Mark("calibration force-completed (bypasses real AR/GPS wait — see caveat)");

        yield return WaitFor(() => GameLogic.Instance.IsArmed, 10f, "startup to finish");
        Mark("GameLogic armed — search for start begins");

        Check("search for start begins after calibration", GameLogic.Instance.IsArmed);

        // --- start gate -------------------------------------------------------------
        yield return WaitFor(() => GameLogic.Instance.CurrentState != GameLogic.GameState.SearchingForStart,
                             10f, "start line to trigger");
        Mark("start line triggered (rider was pre-placed at 0m, so this is near-instant)");

        Check("start line triggers at the gate",
              GameLogic.Instance.CurrentState == GameLogic.GameState.PlayerInit,
              $"state was {GameLogic.Instance.CurrentState}");

        Check("checkpoints spawned at the gate",
              CheckpointDomeSpawner.Instance != null && CheckpointDomeSpawner.Instance.Total == CheckpointCount,
              $"spawned {(CheckpointDomeSpawner.Instance != null ? CheckpointDomeSpawner.Instance.Total : -1)} " +
              $"of {CheckpointCount}");

        Check("countdown runs after the gate, not before",
              CountdownTimer.instance != null && CountdownTimer.instance.IsRunning);

        Check("race clock still stopped during the countdown",
              RaceTimer.instance == null || !RaceTimer.instance.IsRunning);

        // --- countdown completes ----------------------------------------------------
        yield return WaitFor(() => GameLogic.Instance.CurrentState == GameLogic.GameState.Racing,
                             15f, "countdown to finish");
        Mark("countdown finished — racing begins (real timing: 6 beats x CountdownTimer.stepDuration)");

        Check("racing once the countdown ends",
              GameLogic.Instance.CurrentState == GameLogic.GameState.Racing);

        Check("countdown clears itself off screen",
              CountdownTimer.instance == null || !CountdownTimer.instance.IsRunning);

        Check("race clock starts on GO", RaceTimer.instance != null && RaceTimer.instance.IsRunning);

        // --- ride the route ---------------------------------------------------------
        for (int i = 0; i < CheckpointCount; i++)
        {
            float target = CheckpointSpacing * (i + 1);
            float from = i == 0 ? 0f : CheckpointSpacing * i;

            yield return Ride(from, target);
            Mark($"checkpoint {i + 1}/{CheckpointCount} collected " +
                 "(mock rider teleports between fixes — not real travel time)");

            Check($"checkpoint {i + 1} collected on the way past",
                  CheckpointDomeSpawner.Instance.CollectedCount == i + 1,
                  $"collected {CheckpointDomeSpawner.Instance.CollectedCount}");
        }

        Check("all checkpoints collected", CheckpointDomeSpawner.Instance.AllCollected);

        // --- finish -----------------------------------------------------------------
        float elapsedBeforeFinish = RaceTimer.instance != null ? RaceTimer.instance.ElapsedTime : 0f;

        yield return Ride(CheckpointSpacing * CheckpointCount, FinishDistance);
        yield return WaitFor(() => GameLogic.Instance.CurrentState != GameLogic.GameState.Racing,
                             10f, "finish line to trigger");
        Mark("finish line reached (also compressed travel time — see above)");

        Check("race clock stopped at the finish",
              RaceTimer.instance == null || !RaceTimer.instance.IsRunning);

        Check("race was timed", elapsedBeforeFinish > 0f, $"elapsed {elapsedBeforeFinish:F2}s");

        Check("finish score screen shows",
              FinishScoreUI.Instance != null && FinishScoreUI.Instance.IsShowing);

        Check("score is positive and reflects checkpoints collected",
              FinishScoreUI.Instance != null && FinishScoreUI.Instance.LastScore > 0,
              $"score {(FinishScoreUI.Instance != null ? FinishScoreUI.Instance.LastScore : -1)}");

        // With the username screen skipped there is no submission, so the game drops
        // straight back to searching — which is the loop the rider sees on a relaunch.
        yield return WaitFor(() => GameLogic.Instance.CurrentState == GameLogic.GameState.SearchingForStart,
                             10f, "return to searching");
        Mark("returned to searching for the next run");

        Check("returns to searching for the next run",
              GameLogic.Instance.CurrentState == GameLogic.GameState.SearchingForStart,
              $"state was {GameLogic.Instance.CurrentState}");

        Report();
    }

    /// <summary>Records how long this step took, both since the last mark and since the flow began.</summary>
    private static void Mark(string label)
    {
        float now = Time.realtimeSinceStartup;
        float delta = now - lastMarkTime;
        float sinceStart = now - flowStartTime;

        timings.Add((label, sinceStart, delta));
        Debug.Log($"[FlowTest] TIMING  +{delta:F2}s  (t={sinceStart:F2}s)  {label}");

        lastMarkTime = now;
    }

    /// <summary>Moves the rider between two points down the route, a fix at a time.</summary>
    private IEnumerator Ride(float fromMetres, float toMetres)
    {
        const int steps = 8;

        for (int i = 1; i <= steps; i++)
        {
            StandAt(Mathf.Lerp(fromMetres, toMetres, i / (float)steps));

            // A fix per frame or so — faster than a real receiver, but the proximity
            // checks run per frame anyway and nothing downstream is rate-sensitive.
            yield return null;
            yield return null;
        }

        // Let the proximity checks see the arrival for a beat before asserting on it.
        yield return new WaitForSeconds(0.3f);
    }

    /// <summary>Puts the virtual rider this many metres down the route from the start.</summary>
    private void StandAt(float metresFromStart)
    {
        LocationHandler.Instance.PushMockFix(LatAfter(metresFromStart), OriginLng, 3f,
                                             OriginAlt - metresFromStart * 0.28d);
    }

    /// <summary>
    /// Waits for a condition but gives up rather than hanging the run. A stall is itself
    /// a result — the report says which step never happened.
    /// </summary>
    private IEnumerator WaitFor(Func<bool> condition, float timeoutSeconds, string what)
    {
        float waited = 0f;

        while (!condition() && waited < timeoutSeconds)
        {
            waited += Time.deltaTime;
            yield return null;
        }

        if (!condition()) Fail($"timed out after {timeoutSeconds:F0}s waiting for {what}");
    }

    private static void Check(string what, bool ok, string detail = null)
    {
        checks++;

        if (ok)
        {
            Debug.Log($"[FlowTest] PASS  {what}");
            return;
        }

        Fail(string.IsNullOrEmpty(detail) ? what : $"{what} — {detail}");
    }

    private static void Fail(string what)
    {
        failures.Add(what);
        Debug.LogWarning($"[FlowTest] FAIL  {what}");
    }

    private static void Report()
    {
        Passed = failures.Count == 0;

        if (Passed)
        {
            Debug.Log($"[FlowTest] RESULT: PASS — {checks} checks, flow ran start to finish in order.");
        }
        else
        {
            Debug.Log($"[FlowTest] RESULT: FAIL — {failures.Count} of {checks} checks failed:\n  " +
                      string.Join("\n  ", failures));
        }

        System.Text.StringBuilder table = new System.Text.StringBuilder();
        table.AppendLine("[FlowTest] STEP TIMINGS:");
        foreach (var (label, sinceStart, delta) in timings)
        {
            table.AppendLine($"  +{delta,6:F2}s  (t={sinceStart,6:F2}s)  {label}");
        }
        Debug.Log(table.ToString());

        Finished = true;
    }
}
