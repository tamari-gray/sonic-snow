using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns the checkpoint domes down the run. Domes are parented to GeoAnchor.Root
/// and positioned in ENU metres, so they keep tracking the alignment as GeoAnchor
/// refines it during the run rather than being frozen at their spawn position.
/// </summary>
public class CheckpointDomeSpawner : MonoBehaviour
{
    public static CheckpointDomeSpawner Instance;

    [Header("Dome Settings")]
    [SerializeField] private GameObject checkpointDomePrefab;

    [Tooltip("Vertical nudge in metres, for tuning where the domes sit relative to the snow.")]
    [SerializeField] private float verticalOffset = 0f;

    private List<GameObject> spawnedDomes = new List<GameObject>();

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
            dome.transform.localRotation = Quaternion.identity;
            dome.name = "CheckpointDome_" + i;

            spawnedDomes.Add(dome);

            Debug.Log($"[CheckpointDomeSpawner] Dome {i} placed at ENU {enu}");
        }

        if (!hasAltitudes)
        {
            Debug.LogWarning("[CheckpointDomeSpawner] Route has no surveyed altitudes — domes are " +
                             "flat at start-gate height. On a run with real vertical drop they will " +
                             "float well above the snow. Add originAlt/finishAlt/alt to the Firebase config.");
        }
    }

    public void ClearDomes()
    {
        foreach (GameObject dome in spawnedDomes)
        {
            if (dome != null)
            {
                Destroy(dome);
            }
        }

        spawnedDomes.Clear();
    }

    public GameObject GetDome(int index)
    {
        if (index < 0 || index >= spawnedDomes.Count) return null;
        return spawnedDomes[index];
    }
}
