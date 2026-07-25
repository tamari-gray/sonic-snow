using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static UnityEngine.Rendering.STP;

public class CheckpointDomeSpawner : MonoBehaviour
{
    public static CheckpointDomeSpawner Instance;

    [Header("Dome Settings")]
    [SerializeField] private GameObject checkpointDomePrefab;

    private List<GameObject> spawnedDomes = new List<GameObject>();

    private void Awake()
    {
        Instance = this;
    }

    // Call this once during calibration, same timing as FinishLinePillar.SpawnPillar()
    public void SpawnDomes()
    {
        ClearDomes();

        var config = MapDataFetcher.Instance.LoadedConfig;

        for (int i = 0; i < config.checkpoints.Length; i++)
        {
            var checkpoint = config.checkpoints[i];

            Vector3 worldPos = GpsUtils.GpsToWorld(
                checkpoint.lat,
                checkpoint.lng,
                config.originLat,
                config.originLng
            );

            GameObject dome = Instantiate(checkpointDomePrefab, worldPos, Quaternion.identity);
            dome.name = "CheckpointDome_" + i;

            spawnedDomes.Add(dome);

            Debug.Log("Checkpoint dome " + i + " spawned at world pos: " + worldPos);
        }
    }

    public void ClearDomes()
    {
        foreach (var dome in spawnedDomes)
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