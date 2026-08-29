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

    /// <summary>The spawned beam's position in GeoAnchor.Root's local space, for anything
    /// that needs to place itself alongside it (see FinishCollectEffect). Zero if no beam
    /// is currently spawned.</summary>
    public Vector3 LocalPosition => spawnedPillar != null ? spawnedPillar.transform.localPosition : Vector3.zero;

    /// <summary>True while a beam is spawned.</summary>
    public bool IsSpawned => spawnedPillar != null;

    /// <summary>The spawned beam's position in Unity world space — where the rider actually
    /// sees it, alignment and all. Used by the finish's reach check; Vector3.zero if no beam
    /// is currently spawned, so callers should gate on <see cref="IsSpawned"/>.</summary>
    public Vector3 WorldPosition => spawnedPillar != null ? spawnedPillar.transform.position : Vector3.zero;

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

        // PillarRetroChecker's base/top fade band is tuned in local terms but reads
        // world-space Y — on a route where the finish altitude differs meaningfully from the
        // origin's, the whole prefab spawns far from world Y=0 and the fade band discards the
        // pillar everywhere (confirmed 2026-08-17: 18m altitude drop made it fully invisible
        // while siblings using other materials were unaffected). Tell the shader where its own
        // base actually landed so the fade re-anchors to that instead of assuming world Y=0.
        Transform lightPillar = spawnedPillar.transform.Find("LightPillar");
        if (lightPillar != null && lightPillar.TryGetComponent(out Renderer pillarRenderer))
            pillarRenderer.material.SetFloat("_BaseWorldY", spawnedPillar.transform.position.y);

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
