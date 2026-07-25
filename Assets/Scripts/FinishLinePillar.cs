using UnityEngine;

public class FinishLinePillar : MonoBehaviour
{
    public static FinishLinePillar Instance;

    [Header("Pillar Settings")]
    [SerializeField] private GameObject pillarPrefab;

    private GameObject spawnedPillar;

    private void Awake()
    {
        Instance = this;
    }

    public void SpawnPillar()
    {
        MapData mapData = MapDataFetcher.Instance.LoadedConfig;
        Vector3 worldPos = GpsUtils.GpsToWorld(mapData.finishLat, mapData.finishLng, mapData.originLat, mapData.originLng);

        spawnedPillar = Instantiate(pillarPrefab, worldPos, Quaternion.identity);

        Debug.Log("Finish line pillar spawned at world pos: " + worldPos);
    }

    public void ClearPillar()
    {
        if (spawnedPillar != null)
        {
            Destroy(spawnedPillar);
            spawnedPillar = null;
        }
    }
}