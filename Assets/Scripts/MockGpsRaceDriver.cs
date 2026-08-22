using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// On-device stand-in for GPS on hardware with no GNSS (XREAL Beam Pro — see xreal-port memory).
/// Feeds a synthetic route via LocationHandler.PushMockFix so the whole race flow — calibration's
/// GPS-fix/distance-to-start conditions, checkpoints, the finish line — can be exercised end to
/// end without a real fix, while everything else (AR tracking, the steadiness hold, the real
/// recording permission flow, the real countdown/timers) runs for real on the glasses.
///
/// This is the on-device sibling of RaceFlowSelfTest, which does the equivalent thing in the
/// Editor. The difference is pacing: RaceFlowSelfTest teleports the rider between fixes as fast
/// as the checks can run because it's only proving ordering. This one waits real seconds between
/// each fix so a recording actually shows the race progressing rather than an instant finish.
///
/// Off by default (driveWithMockGps). Scene is the authority, same pattern as
/// CalibrationScreen.recordingEnabled — flip it on via an idempotent *Setup.cs, not by hand.
/// A phone build with real GPS should leave this off.
/// </summary>
public class MockGpsRaceDriver : MonoBehaviour
{
    [SerializeField] private bool driveWithMockGps = false;

    [Tooltip("Real seconds to wait between feeding each checkpoint/finish fix once racing starts, " +
             "so there's watchable footage of the race progressing instead of an instant finish.")]
    [SerializeField] private float secondsBetweenSteps = 4f;

    // Same synthetic route shape as RaceFlowSelfTest, duplicated rather than shared — that class
    // is Editor-flow-only (its own report/checks don't apply here) and deliberately never touches
    // device builds.
    private const double OriginLat = 46.5000;
    private const double OriginLng = 11.3500;
    private const double OriginAlt = 2400d;
    private const float CheckpointSpacing = 80f;
    private const int CheckpointCount = 3;
    private const float FinishDistance = 320f;

    private void Awake()
    {
        if (!driveWithMockGps) return;

        // Set now, in Awake: Unity runs every object's Awake before any object's Start, so this
        // is guaranteed to land before LocationHandler.Start() and GameLogic.Start() check it,
        // regardless of GameObject order in the scene.
        LocationHandler.MockMode = true;
        MapDataFetcher.MockRoute = BuildRoute();

        Debug.Log("[MockGpsRaceDriver] Mock GPS route installed — standing in for GNSS the Beam " +
                  "Pro doesn't have. AR tracking, steadiness, recording permission and countdown " +
                  "all still run for real.");
    }

    private IEnumerator Start()
    {
        if (!driveWithMockGps) yield break;

        yield return new WaitUntil(() => LocationHandler.Instance != null);

        // Rider starts on the start line with a good fix — this is what lets calibration's real
        // "GPS fix" and "Distance to start" conditions clear once AR tracking + steadiness
        // genuinely finish. Nothing about calibration itself is skipped.
        StandAt(0f);
        Debug.Log("[MockGpsRaceDriver] Fed start-line fix — calibration proceeds for real from here.");

        yield return new WaitUntil(() => GameLogic.Instance != null &&
                                          GameLogic.Instance.CurrentState == GameLogic.GameState.Racing);
        Debug.Log("[MockGpsRaceDriver] Racing — walking the mock rider down the route.");

        for (int i = 1; i <= CheckpointCount; i++)
        {
            yield return new WaitForSeconds(secondsBetweenSteps);

            float metres = CheckpointSpacing * i;
            StandAt(metres);
            Debug.Log($"[MockGpsRaceDriver] Fed fix at checkpoint {i}/{CheckpointCount} ({metres:F0}m).");
        }

        yield return new WaitForSeconds(secondsBetweenSteps);
        StandAt(FinishDistance);
        Debug.Log("[MockGpsRaceDriver] Fed fix at the finish line.");
    }

    private void StandAt(float metresFromStart)
    {
        LocationHandler.Instance.PushMockFix(LatAfter(metresFromStart), OriginLng, 3f,
                                             OriginAlt - metresFromStart * 0.28d);
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

    /// <summary>Latitude this many metres north of the origin, using the same series GpsUtils
    /// uses so the distances the game measures back out to the ones asked for here.</summary>
    private static double LatAfter(double metres)
    {
        double latRad = OriginLat * Math.PI / 180.0;
        double mPerDegLat = 111132.92 - 559.82 * Math.Cos(2.0 * latRad) + 1.175 * Math.Cos(4.0 * latRad);

        return OriginLat + metres / mPerDegLat;
    }
}
