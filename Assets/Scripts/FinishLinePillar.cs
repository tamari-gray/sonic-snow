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
        spawnedPillar.transform.localRotation = FacingBackDownTheRun(enu);

        Debug.Log($"[FinishLinePillar] Beam placed at ENU {enu} " +
                  $"({enu.magnitude:F0}m from start, {(config.HasSurveyedAltitudes ? "surveyed altitude" : "no altitude data")})");
    }

    /// <summary>
    /// Rotation that turns the beam so its FINISH LINE text reads the right way round
    /// to a rider coming down from the start.
    ///
    /// Unity text is readable when its own forward points the *same* way the viewer is
    /// looking, not back at them — a billboard script sets `transform.forward =
    /// camera.forward`, it doesn't LookAt the camera. A rider at the start is looking
    /// along start-to-finish, and the start is the route origin, so in the geo frame
    /// that direction is simply the finish's own ENU vector.
    ///
    /// Flattened to yaw only: the pillar is a vertical beam, and tilting it to point at
    /// a start line further down the hill would read as broken. The beam itself is a
    /// cylinder, so the yaw is invisible on it and only orients the text.
    /// </summary>
    private static Quaternion FacingBackDownTheRun(Vector3 enu)
    {
        Vector3 startToFinish = new Vector3(enu.x, 0f, enu.z);

        // Only degenerate if the finish sits on top of the start. The route editor
        // rejects that, but LookRotation logs an error on a zero vector regardless.
        if (startToFinish.sqrMagnitude < 1e-4f) return Quaternion.identity;

        return Quaternion.LookRotation(startToFinish, Vector3.up);
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
