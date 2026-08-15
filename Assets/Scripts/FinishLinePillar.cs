using UnityEngine;

/// <summary>
/// Spawns the finish-line beam. The pillar is parented to GeoAnchor.Root and
/// positioned in ENU metres, so it keeps tracking the alignment as GeoAnchor
/// refines it during the run rather than being frozen at its spawn position.
/// </summary>
public class FinishLinePillar : MonoBehaviour
{
    public static FinishLinePillar Instance;

    [Header("Pillar Settings")]
    [SerializeField] private GameObject pillarPrefab;

    [Tooltip("Vertical nudge in metres, for tuning where the beam meets the snow.")]
    [SerializeField] private float verticalOffset = 0f;

    [Tooltip("Radius of the ground contact shadow under the beam, in metres. See " +
             "GroundContactShadow — a grounding cue standing in for real occlusion, which " +
             "XREAL's glasses can't do.")]
    [SerializeField] private float contactShadowRadius = 0.8f;

    private GameObject spawnedPillar;

    private void Awake()
    {
        Instance = this;
    }

    public void SpawnPillar()
    {
        ClearPillar(); // otherwise a second race leaks a duplicate pillar

        if (GeoAnchor.Instance == null)
        {
            Debug.LogError("[FinishLinePillar] No GeoAnchor in the scene — can't place the beam.");
            return;
        }

        MapData config = MapDataFetcher.Instance.LoadedConfig;

        Vector3 enu = config.HasSurveyedAltitudes
            ? GpsUtils.GpsToEnu(config.finishLat, config.finishLng, config.finishAlt,
                                config.originLat, config.originLng, config.originAlt)
            : GpsUtils.GpsToEnu(config.finishLat, config.finishLng,
                                config.originLat, config.originLng);

        enu.y += verticalOffset;

        spawnedPillar = Instantiate(pillarPrefab, GeoAnchor.Instance.Root);
        spawnedPillar.transform.localPosition = enu;
        spawnedPillar.transform.localRotation = GpsUtils.ReadableFromOrigin(enu);

        GroundContactShadow.Create(spawnedPillar.transform, contactShadowRadius);

        Debug.Log($"[FinishLinePillar] Beam placed at ENU {enu} " +
                  $"({enu.magnitude:F0}m from start, {(config.HasSurveyedAltitudes ? "surveyed altitude" : "no altitude data")})");
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
