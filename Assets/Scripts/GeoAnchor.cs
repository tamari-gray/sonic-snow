using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Aligns the geographic frame (ENU metres from the route origin) with Unity's AR
/// world frame, and keeps them aligned as the player rides.
///
/// The problem this solves: ARCore/ARKit put world (0,0,0) wherever the phone was
/// when tracking started, with +Z along whatever direction it happened to be
/// facing. Neither has any relationship to the route origin or to true north. The
/// offset can't be assumed or hardcoded — it has to be measured.
///
/// The measurement: as the player moves, every GPS fix produces a pair of the same
/// point expressed in two frames — (ENU position, Unity camera position). Fitting a
/// rigid transform between two such point sets is closed-form (Kabsch/Umeyama, with
/// scale locked to 1 since both are already in metres). Movement gives far better
/// heading than a magnetometer does, and because the fit re-runs on every fix it
/// also absorbs VIO drift over a long descent instead of accumulating it.
///
/// The compass is used only to seed a rough alignment for the first few seconds,
/// before the player has covered enough ground for the fit to be well conditioned.
///
/// Vertical is handled separately: GPS altitude is far too noisy to fit, so the
/// ground plane is pinned once from the camera's tracked height at the start gate
/// (see <see cref="AnchorVertical"/>) and VIO carries it down the hill from there.
///
/// Everything geo-placed parents to <see cref="Root"/> with its ENU vector as
/// localPosition. Moving Root moves the whole world into place — the XR rig is
/// never touched, so the player never gets teleported by a calibration update.
/// </summary>
public class GeoAnchor : MonoBehaviour
{
    public static GeoAnchor Instance;

    [Header("Scene References")]
    [Tooltip("Parent for all geo-placed content. Created automatically if left empty.")]
    [SerializeField] private Transform geoRoot;

    [Tooltip("The AR camera. Defaults to Camera.main.")]
    [SerializeField] private Camera arCamera;

    [Header("Sample Quality")]
    [Tooltip("Reject GPS fixes worse than this, in metres. Consumer GPS on an open slope is usually 3-6m.")]
    [SerializeField] private float maxHorizontalAccuracy = 8f;

    [Tooltip("Minimum ground distance between stored samples, in metres. Stops a stationary player filling the buffer with noise.")]
    [SerializeField] private float minSampleSpacing = 4f;

    [Tooltip("Rolling buffer size. Older samples are dropped so the fit tracks recent drift.")]
    [SerializeField] private int maxSamples = 40;

    [Tooltip("Accuracy limit for the initial compass seed, in metres. Looser than the fit's limit so the player always sees something, even under trees or in bad conditions.")]
    [SerializeField] private float maxSeedAccuracy = 30f;

    [Tooltip("Largest GPS pipeline lag to compensate for, in seconds.")]
    [SerializeField] private float maxFixLag = 2f;

    [Header("Fit Conditioning")]
    [SerializeField] private int minSamples = 4;

    [Tooltip("Minimum spread across the samples before a heading fit is trusted, in metres.")]
    [SerializeField] private float minBaseline = 20f;

    [Tooltip("Spread at which the fit is treated as fully confident, in metres.")]
    [SerializeField] private float goodBaseline = 80f;

    [Tooltip("Reject a solution whose RMS residual exceeds this, in metres. Usually means a GPS outlier got in.")]
    [SerializeField] private float maxResidual = 15f;

    [Header("Application")]
    [Tooltip("How fast Root eases toward a new solution. 0 snaps instantly, which reads as the world jumping.")]
    [SerializeField] private float smoothing = 2f;

    [Tooltip("Assumed height of the device above the snow when the vertical anchor is set, in metres.")]
    [SerializeField] private float deviceHoldHeight = 1.4f;

    [Header("Debug")]
    [SerializeField] private bool logCalibration = true;

    /// <summary>Parent transform for geo-placed content. Position children in ENU metres.</summary>
    public Transform Root => geoRoot;

    /// <summary>True once Root holds any usable alignment, compass-seeded or better.</summary>
    public bool IsAligned { get; private set; }

    /// <summary>True once the alignment comes from the motion fit rather than the compass.</summary>
    public bool HasMotionFit { get; private set; }

    /// <summary>0-1. Below ~0.3 the placement is compass-grade and can be tens of degrees out.</summary>
    public float Confidence { get; private set; }

    /// <summary>Spread of the current sample set in metres. Drives Confidence.</summary>
    public float Baseline { get; private set; }

    /// <summary>RMS residual of the last accepted fit, in metres. A rough error bar on placement.</summary>
    public float LastResidual { get; private set; }

    /// <summary>One-line state summary for the on-screen debug log during field testing.</summary>
    public string StatusLine
    {
        get
        {
            if (!IsAligned) return "unaligned";
            if (!HasMotionFit) return $"compass only ({geoSamples.Count} samples, {Baseline:F0}m/{minBaseline:F0}m baseline)";
            return $"fitted conf {Confidence:F2}, {Baseline:F0}m baseline, RMS {LastResidual:F1}m";
        }
    }

    private readonly List<Vector3> geoSamples = new List<Vector3>();
    private readonly List<Vector3> worldSamples = new List<Vector3>();

    // Camera pose history, so a GPS fix can be paired with where the camera actually
    // was when that fix was measured rather than where it is now.
    private struct PoseSample
    {
        public float time;
        public Vector3 position;
    }

    private readonly List<PoseSample> poseHistory = new List<PoseSample>();

    private Vector3 lastSampleEnu;
    private bool hasLastSample;
    private double lastProcessedTimestamp = -1d;

    private Vector3 targetPosition;
    private Quaternion targetRotation = Quaternion.identity;

    private float groundY;
    private bool groundAnchored;

    private void Awake()
    {
        Instance = this;

        if (geoRoot == null)
        {
            geoRoot = new GameObject("GeoAnchorRoot").transform;
        }

        // Only used to seed the initial guess. LocationHandler starts the location
        // service, which the magnetometer depends on for true (vs magnetic) heading.
        Input.compass.enabled = true;
    }

    private void Update()
    {
        if (arCamera == null) arCamera = Camera.main;
        if (arCamera == null) return;

        RecordPose();
        PollGps();
        EaseRoot();
    }

    private void RecordPose()
    {
        poseHistory.Add(new PoseSample
        {
            time = Time.realtimeSinceStartup,
            position = arCamera.transform.position
        });

        float cutoff = Time.realtimeSinceStartup - (maxFixLag + 1f);
        while (poseHistory.Count > 0 && poseHistory[0].time < cutoff)
        {
            poseHistory.RemoveAt(0);
        }
    }

    /// <summary>Camera position at a past moment, linearly interpolated between recorded frames.</summary>
    private Vector3 PoseAt(float time)
    {
        if (poseHistory.Count == 0) return arCamera.transform.position;
        if (time >= poseHistory[poseHistory.Count - 1].time) return poseHistory[poseHistory.Count - 1].position;
        if (time <= poseHistory[0].time) return poseHistory[0].position;

        for (int i = poseHistory.Count - 1; i > 0; i--)
        {
            if (poseHistory[i - 1].time > time) continue;

            float span = poseHistory[i].time - poseHistory[i - 1].time;
            float t = span > 1e-5f ? (time - poseHistory[i - 1].time) / span : 0f;

            return Vector3.Lerp(poseHistory[i - 1].position, poseHistory[i].position, t);
        }

        return poseHistory[0].position;
    }

    private void PollGps()
    {
        LocationHandler location = LocationHandler.Instance;
        if (location == null || !location.IsReady) return;

        // LocationHandler polls Input.location itself; diff on its timestamp so we
        // process each fix once rather than every frame.
        if (location.LastTimestamp <= lastProcessedTimestamp) return;
        lastProcessedTimestamp = location.LastTimestamp;

        MapDataFetcher fetcher = MapDataFetcher.Instance;
        if (fetcher == null || !fetcher.IsLoaded) return;

        MapData config = fetcher.LoadedConfig;

        Vector3 enu = GpsUtils.GpsToEnu(
            location.CurrentLatitude, location.CurrentLongitude,
            config.originLat, config.originLng);

        // A fix is measured before it reaches us. At 15 m/s the camera has already
        // moved 15m by the time a one-second-old fix arrives, so pairing the fix with
        // the *current* camera position bakes a systematic downhill bias into the fit.
        // Rewind to where the camera was when the fix was actually taken.
        double unixNow = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
        float lag = Mathf.Clamp((float)(unixNow - location.LastTimestamp), 0f, maxFixLag);

        Vector3 world = PoseAt(Time.realtimeSinceStartup - lag);

        // Seed on a much looser accuracy bound than the fit uses. A rough beam beats
        // no beam, and under trees or heavy cloud we might never see a good fix.
        if (!IsAligned && location.HorizontalAccuracy <= maxSeedAccuracy)
        {
            SeedFromCompass(enu, world);
        }

        // A fix worse than this teaches the fit more noise than signal.
        if (location.HorizontalAccuracy > maxHorizontalAccuracy) return;

        if (hasLastSample && Vector3.Distance(Flat(enu), Flat(lastSampleEnu)) < minSampleSpacing) return;

        geoSamples.Add(enu);
        worldSamples.Add(world);
        lastSampleEnu = enu;
        hasLastSample = true;

        if (geoSamples.Count > maxSamples)
        {
            geoSamples.RemoveAt(0);
            worldSamples.RemoveAt(0);
        }

        TrySolve();
    }

    /// <summary>
    /// Rough initial alignment from the magnetometer, so the player sees something
    /// before they have moved. Expect 15-20 degrees of error, worse near chairlifts
    /// and anything else made of steel. The motion fit replaces this as soon as it
    /// has a baseline.
    /// </summary>
    private void SeedFromCompass(Vector3 enu, Vector3 cameraWorld)
    {
        if (!Input.compass.enabled || Input.compass.timestamp <= 0d) return;

        float cameraYaw = arCamera.transform.eulerAngles.y;

        // trueHeading is where the device points relative to north; cameraYaw is
        // where it points in Unity's world. The difference is the Unity yaw at
        // which north sits — which is exactly the rotation Root needs, since Root's
        // local +Z is geographic north.
        float northYaw = Mathf.DeltaAngle(Input.compass.trueHeading, cameraYaw);

        targetRotation = Quaternion.Euler(0f, northYaw, 0f);
        targetPosition = WithGroundY(cameraWorld - targetRotation * Flat(enu), cameraWorld);

        geoRoot.SetPositionAndRotation(targetPosition, targetRotation);

        IsAligned = true;
        Confidence = 0.15f;

        if (logCalibration)
        {
            Debug.Log($"[GeoAnchor] Compass seed. trueHeading={Input.compass.trueHeading:F1}deg, " +
                      $"north sits at Unity yaw {northYaw:F1}deg. Rough until the player moves.");
        }
    }

    /// <summary>
    /// Closed-form 2D rigid fit between the geo samples and the Unity samples.
    /// Solves yaw and horizontal translation only.
    /// </summary>
    private void TrySolve()
    {
        if (geoSamples.Count < minSamples) return;

        Vector3 geoCentroid = Flat(Centroid(geoSamples));
        Vector3 worldCentroid = Flat(Centroid(worldSamples));

        Baseline = Spread(geoSamples, geoCentroid);

        // Over a short baseline the sample noise dominates the actual displacement
        // and the yaw estimate is meaningless. Better to keep the compass seed.
        if (Baseline < minBaseline) return;

        double num = 0d, den = 0d;

        for (int i = 0; i < geoSamples.Count; i++)
        {
            Vector3 a = Flat(geoSamples[i]) - geoCentroid;
            Vector3 b = Flat(worldSamples[i]) - worldCentroid;

            // Unity yaw is clockwise about +Y, so R(t) * a = (a.x cos t + a.z sin t,
            // 0, -a.x sin t + a.z cos t). Maximising the dot product of R(t)*a with b
            // over all samples gives t = atan2(sum of cross terms, sum of dot terms).
            num += b.x * a.z - b.z * a.x;
            den += b.x * a.x + b.z * a.z;
        }

        // Degenerate: every sample landed on the centroid.
        if (System.Math.Sqrt(num * num + den * den) < 1e-4) return;

        float yaw = (float)(System.Math.Atan2(num, den) * Mathf.Rad2Deg);
        Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);
        Vector3 position = worldCentroid - rotation * geoCentroid;

        float residual = RmsResidual(rotation, position);

        // A good fit over a real baseline should land within a few metres. Much worse
        // than that means an outlier fix got into the buffer, so keep the old solution.
        if (residual > maxResidual)
        {
            if (logCalibration)
                Debug.LogWarning($"[GeoAnchor] Rejected fit, RMS residual {residual:F1}m over {maxResidual:F1}m limit.");
            return;
        }

        targetRotation = rotation;
        targetPosition = WithGroundY(position, arCamera.transform.position);

        LastResidual = residual;
        HasMotionFit = true;
        IsAligned = true;

        Confidence = Mathf.Clamp01(Mathf.InverseLerp(minBaseline, goodBaseline, Baseline))
                   * Mathf.Clamp01(1f - residual / maxResidual);

        if (logCalibration)
        {
            Debug.Log($"[GeoAnchor] Motion fit: north at Unity yaw {yaw:F1}deg, " +
                      $"{geoSamples.Count} samples over {Baseline:F0}m baseline, " +
                      $"RMS {residual:F1}m, confidence {Confidence:F2}");
        }
    }

    private float RmsResidual(Quaternion rotation, Vector3 position)
    {
        float sumSq = 0f;

        for (int i = 0; i < geoSamples.Count; i++)
        {
            Vector3 predicted = rotation * Flat(geoSamples[i]) + position;
            sumSq += (Flat(predicted) - Flat(worldSamples[i])).sqrMagnitude;
        }

        return Mathf.Sqrt(sumSq / geoSamples.Count);
    }

    /// <summary>
    /// Pins the geo ground plane to the camera's current tracked height. Call this
    /// while the player is standing at the start gate — that is the one moment we
    /// know their true altitude equals the route's originAlt, so it's the only
    /// reliable place to tie surveyed altitudes to Unity's vertical axis.
    /// </summary>
    public void AnchorVertical()
    {
        if (arCamera == null) arCamera = Camera.main;
        if (arCamera == null) return;

        groundY = arCamera.transform.position.y - deviceHoldHeight;
        groundAnchored = true;

        targetPosition = new Vector3(targetPosition.x, groundY, targetPosition.z);

        if (logCalibration)
            Debug.Log($"[GeoAnchor] Vertical anchored. Start-gate ground is Unity y = {groundY:F2}");
    }

    /// <summary>
    /// Clears the alignment and all samples. Call between races so a second run
    /// re-solves from scratch instead of inheriting the first run's drift.
    /// </summary>
    public void ResetAlignment()
    {
        geoSamples.Clear();
        worldSamples.Clear();
        poseHistory.Clear();

        hasLastSample = false;
        groundAnchored = false;
        IsAligned = false;
        HasMotionFit = false;
        Confidence = 0f;
        Baseline = 0f;
        LastResidual = 0f;

        targetRotation = Quaternion.identity;
        targetPosition = Vector3.zero;

        if (logCalibration) Debug.Log("[GeoAnchor] Alignment reset.");
    }

    /// <summary>
    /// World-space position of a coordinate, for one-off queries. Content that stays
    /// in the scene should be parented to <see cref="Root"/> in ENU instead, so it
    /// keeps tracking the alignment as it improves.
    /// </summary>
    public Vector3 GeoToWorld(double lat, double lng, double alt)
    {
        MapDataFetcher fetcher = MapDataFetcher.Instance;
        if (fetcher == null || !fetcher.IsLoaded) return Vector3.zero;

        MapData config = fetcher.LoadedConfig;

        Vector3 enu = config.HasSurveyedAltitudes
            ? GpsUtils.GpsToEnu(lat, lng, alt, config.originLat, config.originLng, config.originAlt)
            : GpsUtils.GpsToEnu(lat, lng, config.originLat, config.originLng);

        return geoRoot.TransformPoint(enu);
    }

    private void EaseRoot()
    {
        if (!IsAligned) return;

        if (smoothing <= 0f)
        {
            geoRoot.SetPositionAndRotation(targetPosition, targetRotation);
            return;
        }

        // Exponential ease, framerate independent. Corrections arrive at GPS rate
        // (~1Hz) and can be several metres; snapping them reads as the world twitching.
        float t = 1f - Mathf.Exp(-smoothing * Time.deltaTime);

        geoRoot.SetPositionAndRotation(
            Vector3.Lerp(geoRoot.position, targetPosition, t),
            Quaternion.Slerp(geoRoot.rotation, targetRotation, t));
    }

    private Vector3 WithGroundY(Vector3 position, Vector3 cameraWorld)
    {
        float y = groundAnchored ? groundY : cameraWorld.y - deviceHoldHeight;
        return new Vector3(position.x, y, position.z);
    }

    private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

    private static Vector3 Centroid(List<Vector3> points)
    {
        Vector3 sum = Vector3.zero;
        for (int i = 0; i < points.Count; i++) sum += points[i];
        return sum / points.Count;
    }

    /// <summary>Rough extent of a sample set: twice the furthest distance from its centroid.</summary>
    private static float Spread(List<Vector3> points, Vector3 centroid)
    {
        float max = 0f;
        for (int i = 0; i < points.Count; i++)
        {
            float d = Vector3.Distance(Flat(points[i]), centroid);
            if (d > max) max = d;
        }
        return max * 2f;
    }
}
