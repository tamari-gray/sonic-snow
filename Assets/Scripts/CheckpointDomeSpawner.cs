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

    [Header("Collection")]
    [Tooltip("How close the rider has to get for a checkpoint to count, in metres.")]
    [SerializeField] private float collectRadius = 10f;

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
    /// Distance is measured by GPS against the checkpoint's real coordinate, not by how
    /// close the rider is to the dome on screen. The dome is an estimate that shifts as
    /// GeoAnchor sharpens its fit, so scoring off it would make collection depend on how
    /// good the alignment happened to be at that moment. Same reasoning as the finish.
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
            if (distance > collectRadius) continue;

            Collect(i, distance);
        }
    }

    private void Collect(int index, float distance)
    {
        Checkpoint checkpoint = checkpoints[index];

        checkpoint.Collected = true;
        CollectedCount++;

        // Hidden rather than destroyed: ClearDomes tears them down between races anyway,
        // and keeping the instance means indices stay valid for anything holding one.
        if (checkpoint.Instance != null) checkpoint.Instance.SetActive(false);

        Debug.Log($"Checkpoint {index + 1} reached at {distance:F1}m — " +
                  $"{CollectedCount}/{checkpoints.Count} collected");
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
