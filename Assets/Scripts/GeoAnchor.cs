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
/// The seed: the fit needs ~20m of travel before it's well conditioned, so something
/// has to cover the gap. This build assumes a launch ritual — the player stands at
/// the start line with the phone pointed at the finish while the app boots. That
/// pins both unknowns immediately: Unity's origin is the start, and Unity's forward
/// is the route bearing. It beats the magnetometer (no interference from cars, lift
/// towers or rebar) but it is an assumption, not a measurement, so the motion fit
/// still takes over as soon as it has a baseline and quietly corrects a sloppy ritual.
///
/// IMPORTANT: the AR session origin is set when ARCore first establishes tracking,
/// a second or two after launch — not the instant the icon is tapped. The phone has
/// to be held steady on the finish through app startup, not just at launch.
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

    [Tooltip("Assumed height of the device above the snow when the vertical anchor is set, in metres. " +
             "Getting this wrong offsets the whole geo ground plane by the difference, so tune it if " +
             "placement sits consistently high or low. 2.1 is a 2026-08-16 field-test correction on " +
             "Beam Pro + One Pro: checkpoints and the finish beam both sat ~0.5m above the snow at the " +
             "old 1.6 (which was tuned for eye height alone) — whatever this rig actually reports for " +
             "camera height, it reads higher than a plain eye-level assumption by about that much. If a " +
             "future test still floats or now sits low, adjust this by the newly measured error.")]
    [SerializeField] private float deviceHoldHeight = 2.1f;

    [Header("Debug")]
    [SerializeField] private bool logCalibration = true;

    /// <summary>Parent transform for geo-placed content. Position children in ENU metres.</summary>
    public Transform Root => geoRoot;

    /// <summary>True once Root holds any usable alignment, launch-seeded or better.</summary>
    public bool IsAligned { get; private set; }

    /// <summary>True once the alignment comes from the motion fit rather than the launch seed.</summary>
    public bool HasMotionFit { get; private set; }

    /// <summary>0-1. Below ~0.3 the placement is still the launch seed and is only as good as the ritual was.</summary>
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
            if (!HasMotionFit) return $"launch seed ({geoSamples.Count} samples, {Baseline:F0}m/{minBaseline:F0}m baseline)";
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

    // Camera pose at the earliest frame we could read it — the launch ritual says the
    // player was standing at the start line facing the finish at that moment. The yaw
    // stays valid for the whole AR session (Unity's frame never moves), so a reset
    // between races keeps it and only re-derives position.
    private float launchYaw;
    private Vector3 launchPosition;
    private bool hasLaunchPose;

    /// <summary>
    /// True once the rider has actually raced away from the gate, which is what makes the
    /// launch position stale — not merely the fact that we've seeded before.
    ///
    /// These are different questions and conflating them cost real accuracy: the seed
    /// runs several times before the first race (once when calibration latches the pose,
    /// again after GameLogic resets the alignment), and treating the second of those as
    /// "between races" threw away the exact ritual position in favour of a single GPS fix,
    /// sliding the whole world sideways by that fix's error.
    /// </summary>
    private bool launchPoseSpent;

    private void Awake()
    {
        Instance = this;

        if (geoRoot == null)
        {
            geoRoot = new GameObject("GeoAnchorRoot").transform;
        }
    }

    private void Update()
    {
        if (arCamera == null) arCamera = Camera.main;
        if (arCamera == null) return;

        CaptureLaunchPose();
        RecordPose();
        PollGps();
        if (!IsAligned) TrySeed();
        EaseRoot();
    }

    /// <summary>
    /// Grabs the camera pose on the first frame it exists, so the anchor works even
    /// with no calibration screen present. Under the launch ritual this is the player
    /// standing at the start line facing the finish.
    /// </summary>
    private void CaptureLaunchPose()
    {
        if (hasLaunchPose) return;

        launchYaw = arCamera.transform.eulerAngles.y;
        launchPosition = arCamera.transform.position;
        hasLaunchPose = true;
    }

    /// <summary>
    /// Re-latches the launch pose on the current frame and forces a re-seed. Call once
    /// AR tracking is established and the phone has been held still — see
    /// CalibrationScreen. The frame-1 capture is taken before the session origin has
    /// settled, so this is the more trustworthy of the two.
    ///
    /// Deliberately a method the platform layer calls rather than an SDK query in
    /// here: GeoAnchor only ever touches Camera.main, so it works unchanged on any
    /// tracker that can report a camera pose.
    /// </summary>
    public void RecaptureLaunchPose()
    {
        if (arCamera == null) arCamera = Camera.main;
        if (arCamera == null) return;

        launchYaw = arCamera.transform.eulerAngles.y;
        launchPosition = arCamera.transform.position;
        hasLaunchPose = true;

        // Drop back to unseeded so TrySeed re-applies with the settled pose next frame.
        // A fresh ritual also re-arms the launch position: the rider is back at the gate.
        launchPoseSpent = false;
        IsAligned = false;

        if (logCalibration)
            Debug.Log($"[GeoAnchor] Launch pose re-latched at yaw {launchYaw:F1}deg.");
    }

    /// <summary>
    /// Tells the anchor the rider has left the gate, so the launch position no longer
    /// describes where they are. Later seeds derive the origin from GPS instead.
    ///
    /// Called when the race actually starts rather than on the first seed: several seeds
    /// happen while the rider is still standing at the gate, and all of them should keep
    /// using the ritual position.
    /// </summary>
    public void MarkLaunchPoseSpent()
    {
        if (launchPoseSpent) return;

        launchPoseSpent = true;

        if (logCalibration)
            Debug.Log("[GeoAnchor] Race started — launch position retired, later seeds use GPS.");
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
    /// Initial alignment from the launch ritual: the player stood at the start line
    /// facing the finish while AR tracking established, so the camera's launch yaw
    /// corresponds to the start-to-finish bearing, and the launch position is the
    /// route origin. Applies immediately — no GPS fix and no movement required, so
    /// the beam is up before the race starts.
    /// </summary>
    private void TrySeed()
    {
        if (!hasLaunchPose) return;

        MapDataFetcher fetcher = MapDataFetcher.Instance;
        if (fetcher == null || !fetcher.IsLoaded) return;

        MapData config = fetcher.LoadedConfig;

        double bearing = GpsUtils.Bearing(config.originLat, config.originLng,
                                          config.finishLat, config.finishLng);

        // The launch yaw represents the route bearing. Bearings and Unity yaw both
        // increase clockwise, so a true bearing t sits at Unity yaw (launchYaw - B).
        // North is bearing 0, so north sits at (launchYaw - B) — and Root's local +Z
        // is north, which makes that Root's rotation.
        float northYaw = launchYaw - (float)bearing;

        targetRotation = Quaternion.Euler(0f, northYaw, 0f);
        targetPosition = SeedPosition(targetRotation);

        geoRoot.SetPositionAndRotation(targetPosition, targetRotation);

        IsAligned = true;
        Confidence = 0.2f;

        if (logCalibration)
        {
            Debug.Log($"[GeoAnchor] Launch seed. Route bearing {bearing:F1}deg, launch yaw " +
                      $"{launchYaw:F1}deg, north sits at Unity yaw {northYaw:F1}deg. " +
                      $"Only as good as the launch ritual until the motion fit engages.");
        }
    }

    /// <summary>
    /// Where the route origin sits in Unity space. Straight after launch that's the
    /// launch position, per the ritual. After a reset between races the player is at
    /// the finish, not the start, so it's re-derived from their current fix instead.
    /// </summary>
    private Vector3 SeedPosition(Quaternion rotation)
    {
        // Before the first race: trust the ritual. The player is standing on the origin,
        // which is a far better position estimate than a single GPS fix carrying several
        // metres of noise. This holds for every seed until they actually ride away.
        if (!launchPoseSpent) return WithGroundY(launchPosition, launchPosition);

        // Re-seed between races: the player is at the finish, not the start, so the
        // launch position tells us nothing useful. Derive the origin from where they
        // are now instead.
        LocationHandler location = LocationHandler.Instance;
        if (location == null || !location.IsReady) return WithGroundY(launchPosition, launchPosition);

        MapData config = MapDataFetcher.Instance.LoadedConfig;

        Vector3 enu = GpsUtils.GpsToEnu(location.CurrentLatitude, location.CurrentLongitude,
                                        config.originLat, config.originLng);

        Vector3 camera = arCamera.transform.position;
        return WithGroundY(camera - rotation * Flat(enu), camera);
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
        // and the yaw estimate is meaningless. Better to keep the launch seed.
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
    ///
    /// Deliberately keeps the launch pose: Unity's frame doesn't move for the life of
    /// the AR session, so the yaw the launch ritual gave us is still valid for run two.
    /// Only the position needs re-deriving, which TrySeed does from the current fix.
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

    /// <summary>
    /// The player's head position in Unity world space. Falls back to Camera.main if the
    /// scene reference was left empty, matching how the rest of this class resolves it.
    /// </summary>
    public bool TryGetPlayerWorldPosition(out Vector3 position)
    {
        if (arCamera == null) arCamera = Camera.main;

        if (arCamera == null)
        {
            position = Vector3.zero;
            return false;
        }

        position = arCamera.transform.position;
        return true;
    }

    /// <summary>
    /// True if the player is within <paramref name="radius"/> metres of a rendered world
    /// position, measured on the ground plane so the rider's eye height (and any altitude
    /// error in the placement) doesn't eat into the radius.
    ///
    /// This is the "close enough to touch it" test, deliberately separate from the GPS
    /// distance checks. Fixes arrive at ~1Hz and trail the real position, so at riding
    /// speed the rider is visibly through a dome several metres before GPS agrees — which
    /// reads as the dome refusing to pop until they're inside it. Gameplay that should
    /// feel immediate can trigger off this; anything that has to be defensible after the
    /// run (timing, scoring) stays on GPS.
    ///
    /// Requires an alignment: with no anchor, rendered positions are meaningless.
    /// </summary>
    public bool IsPlayerWithinReach(Vector3 worldPoint, float radius)
    {
        if (radius <= 0f) return false;
        if (!IsAligned) return false;
        if (!TryGetPlayerWorldPosition(out Vector3 player)) return false;

        float dx = player.x - worldPoint.x;
        float dz = player.z - worldPoint.z;

        return dx * dx + dz * dz <= radius * radius;
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
