using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns the checkpoint domes down the run and retires them as the rider passes.
/// Domes are parented to GeoAnchor.Root and positioned in ENU metres, so they keep
/// tracking the alignment as GeoAnchor refines it during the run rather than being
/// frozen at their spawn position.
/// </summary>
public class CheckpointDomeSpawner : MonoBehaviour
{
    public static CheckpointDomeSpawner Instance;

    [Header("Dome Settings")]
    [SerializeField] private GameObject checkpointDomePrefab;

    [Tooltip("Vertical nudge in metres, for tuning where the domes sit relative to the snow.")]
    [SerializeField] private float verticalOffset = 0f;

    [Tooltip("Radius of the ground contact shadow under each dome, in metres. See " +
             "GroundContactShadow — a grounding cue standing in for real occlusion, which " +
             "XREAL's glasses can't do. Without it, a correctly-placed dome on sloped terrain " +
             "can look like it's floating from a distance (weak depth cues at range), even " +
             "though it settles into place visually once the rider is close enough for " +
             "parallax/relative-size cues to take over. Matches FinishLinePillar's use of the " +
             "same helper; sized a bit larger here since the dome's own footprint is bigger.")]
    [SerializeField] private float contactShadowRadius = 2.5f;

    [Header("Collection")]
    [Tooltip("How close the rider has to get for a checkpoint to count, in metres, measured " +
             "by GPS against the checkpoint's real coordinate.")]
    [SerializeField] private float collectRadius = 10f;

    [Tooltip("How close the rider has to get to the dome they can actually see, in metres, " +
             "before it counts regardless of what GPS says. The dome's sphere is ~4m across " +
             "and its glow footprint ~6m, so this is set to retire it as the rider arrives at " +
             "that footprint rather than after they've ridden through the middle of it. " +
             "Exists because GPS fixes arrive at ~1Hz and trail the real position: at riding " +
             "speed the dome is already behind the rider by the time the fix falls inside " +
             "collectRadius. Set to 0 to disable and go back to GPS-only collection.")]
    [SerializeField] private float reachRadius = 6f;

    [Header("Collect Effect")]
    [Tooltip("How long the dome instance takes to shrink away once collected, in seconds. " +
             "Matches the design's own dome-collapse timing (~420ms). The particle burst " +
             "plays on its own separate timing — see CheckpointCollectEffect. This only " +
             "scales the spawned instance's transform; the prefab itself is never touched.")]
    [SerializeField] private float shrinkDuration = 0.42f;

    /// <summary>A spawned dome together with the coordinate it actually answers to.</summary>
    private class Checkpoint
    {
        public GameObject Instance;
        public double Lat;
        public double Lng;
        public bool Collected;
    }

    private readonly List<Checkpoint> checkpoints = new List<Checkpoint>();

    /// <summary>How many checkpoints this route has.</summary>
    public int Total => checkpoints.Count;

    /// <summary>How many the rider has ridden through so far this run.</summary>
    public int CollectedCount { get; private set; }

    /// <summary>True once every checkpoint has been reached. Vacuously true on a route with none.</summary>
    public bool AllCollected => CollectedCount >= checkpoints.Count;

    private void Awake()
    {
        Instance = this;
    }

    // Called once the player is confirmed at the start gate, same timing as
    // FinishLinePillar.SpawnPillar().
    public void SpawnDomes()
    {
        ClearDomes();

        if (GeoAnchor.Instance == null)
        {
            Debug.LogError("[CheckpointDomeSpawner] No GeoAnchor in the scene — can't place domes.");
            return;
        }

        MapData config = MapDataFetcher.Instance.LoadedConfig;

        if (config.checkpoints == null)
        {
            Debug.LogWarning("[CheckpointDomeSpawner] Route config has no checkpoints array.");
            return;
        }

        bool hasAltitudes = config.HasSurveyedAltitudes;

        for (int i = 0; i < config.checkpoints.Length; i++)
        {
            CheckpointData checkpoint = config.checkpoints[i];

            Vector3 enu = hasAltitudes
                ? GpsUtils.GpsToEnu(checkpoint.lat, checkpoint.lng, checkpoint.alt,
                                    config.originLat, config.originLng, config.originAlt)
                : GpsUtils.GpsToEnu(checkpoint.lat, checkpoint.lng,
                                    config.originLat, config.originLng);

            enu.y += verticalOffset;

            GameObject dome = Instantiate(checkpointDomePrefab, GeoAnchor.Instance.Root);
            dome.transform.localPosition = enu;
            dome.transform.localRotation = GpsUtils.ReadableFromOrigin(enu);
            dome.name = "CheckpointDome_" + i;

            GroundContactShadow.Create(dome.transform, contactShadowRadius);

            checkpoints.Add(new Checkpoint
            {
                Instance = dome,
                Lat = checkpoint.lat,
                Lng = checkpoint.lng,
            });

            Debug.Log($"[CheckpointDomeSpawner] Dome {i} placed at ENU {enu}");
        }

        if (!hasAltitudes)
        {
            Debug.LogWarning("[CheckpointDomeSpawner] Route has no surveyed altitudes — domes are " +
                             "flat at start-gate height. On a run with real vertical drop they will " +
                             "float well above the snow. Add originAlt/finishAlt/alt to the Firebase config.");
        }
    }

    /// <summary>
    /// Retires any checkpoint the rider has reached. Call each frame while racing.
    ///
    /// Two ways to reach one, whichever lands first:
    ///
    /// - GPS distance to the checkpoint's real coordinate, within collectRadius. This is
    ///   the authority: the dome is an estimate that shifts as GeoAnchor sharpens its fit,
    ///   so a rider whose alignment is poor still collects on the coordinate they rode over.
    /// - Physical distance to the dome as rendered, within reachRadius. GPS fixes arrive at
    ///   ~1Hz and trail the real position, so on GPS alone a dome doesn't pop until the
    ///   rider is already through it. This closes that gap without letting a bad alignment
    ///   hand out checkpoints the rider never went near — reachRadius is small and the test
    ///   needs an aligned anchor (see GeoAnchor.IsPlayerWithinReach).
    ///
    /// Deliberately unordered for now: passing checkpoint 2 first collects checkpoint 2.
    /// Enforcing sequence is a rule change, not a detection change.
    /// </summary>
    public void CheckProximity(double lat, double lng)
    {
        for (int i = 0; i < checkpoints.Count; i++)
        {
            Checkpoint checkpoint = checkpoints[i];

            if (checkpoint.Collected) continue;

            float distance = GpsUtils.HaversineDistance(lat, lng, checkpoint.Lat, checkpoint.Lng);

            if (distance <= collectRadius)
            {
                Collect(i, distance, "GPS");
                continue;
            }

            // GPS hasn't caught up, but the rider is standing at the dome they can see.
            // Retiring it here is what makes collection feel like contact instead of like
            // a delayed radio check. Scoring is unchanged — this decides *when* the dome
            // retires, not whether an unvisited checkpoint can count.
            if (checkpoint.Instance != null &&
                GeoAnchor.Instance != null &&
                GeoAnchor.Instance.IsPlayerWithinReach(checkpoint.Instance.transform.position, reachRadius))
            {
                Collect(i, distance, "reach");
            }
        }
    }

    private void Collect(int index, float distance, string trigger)
    {
        Checkpoint checkpoint = checkpoints[index];

        checkpoint.Collected = true;
        CollectedCount++;

        if (checkpoint.Instance != null)
        {
            if (GeoAnchor.Instance != null)
                CheckpointCollectEffect.Play(GeoAnchor.Instance.Root, checkpoint.Instance.transform.localPosition);

            // Shrunk then hidden rather than hidden outright: ClearDomes still tears the
            // instance down between races, and keeping it around (just invisible, scaled
            // to zero) means indices stay valid for anything holding one, same as before.
            StartCoroutine(ShrinkAndHide(checkpoint.Instance, shrinkDuration));
        }

        Debug.Log($"Checkpoint {index + 1} reached at {distance:F1}m by {trigger} — " +
                  $"{CollectedCount}/{checkpoints.Count} collected");
    }

    /// <summary>
    /// Scales the dome instance down to nothing, then deactivates it. Runs on
    /// CheckpointDomeSpawner rather than the dome itself so it isn't cut short by the
    /// dome's own SetActive(false) at the end. Only ever touches this one instance's
    /// transform — never the prefab or its materials.
    /// </summary>
    private IEnumerator ShrinkAndHide(GameObject dome, float duration)
    {
        Vector3 startScale = dome.transform.localScale;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            // Bails cleanly if a new race's ClearDomes() destroys this instance mid-shrink.
            if (dome == null) yield break;

            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = t * t; // ease-in: starts slow, collapses fast — a pop, not a fade

            dome.transform.localScale = Vector3.Lerp(startScale, Vector3.zero, eased);
            yield return null;
        }

        if (dome == null) yield break;

        dome.transform.localScale = Vector3.zero;
        dome.SetActive(false);
    }

    public void ClearDomes()
    {
        foreach (Checkpoint checkpoint in checkpoints)
        {
            if (checkpoint.Instance != null)
            {
                Destroy(checkpoint.Instance);
            }
        }

        checkpoints.Clear();
        CollectedCount = 0;
    }

    public GameObject GetDome(int index)
    {
        if (index < 0 || index >= checkpoints.Count) return null;
        return checkpoints[index].Instance;
    }
}
