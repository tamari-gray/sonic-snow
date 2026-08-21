using UnityEngine;
using TMPro;
using UnityEngine.XR.ARFoundation;

/// <summary>
/// Blocking startup screen that holds the player on the launch ritual until the app
/// is genuinely ready to place content.
///
/// This exists because the ritual depends on a moment the player can't see. The AR
/// session origin latches when tracking first establishes, a second or two after
/// launch — and whatever direction the phone is pointing at that instant becomes the
/// reference for the whole session. Without a screen telling them to hold still,
/// they lower the phone during startup and every placement is off by however far
/// they moved.
///
/// Readiness is five separate conditions, reported individually so a stall on the
/// hill is diagnosable rather than a mystery spinner. The fifth — distance to start —
/// used to live in GameLogic as a per-frame check that ran only after this screen
/// closed. It moved here instead: since this screen only runs once per app launch and
/// the current workflow is a fresh launch per run anyway, there's no need for a
/// separate live "searching for start" loop afterwards. Once all five clear, the race
/// is triggered immediately — checkpoints and the finish beam spawn and the countdown
/// starts the moment this screen closes.
///
/// AR capture used to be a sixth condition here: it auto-started XREAL's First Person
/// View capture the moment this screen appeared and blocked on it the same way the
/// other five block. That coupling is gone. Recording now starts in Finish(), once
/// calibration has already cleared, which is both what was asked for and strictly
/// better: the capture never was a real precondition (nothing about it being
/// incomplete blocks placement the way an unsettled AR pose does), yet gating on it
/// meant the Android screen-capture consent dialog sat between the player and the
/// start of their race. Starting after the fact costs nothing — clearing "distance to
/// start" means the player is standing at the gate, so the recording still begins
/// before they move.
/// </summary>
public class CalibrationScreen : MonoBehaviour
{
    public static CalibrationScreen Instance;

    [Header("UI")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private TMP_Text instructionText;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private RectTransform spinner;

    [Header("Copy")]
    [SerializeField, TextArea]
    private string instruction = "Please hold steady and stay facing the finish point.";

    [SerializeField, TextArea]
    private string readyInstruction = "Calibrated. You're good to go.";

    [Header("Spinner")]
    [SerializeField] private float spinnerDegreesPerSecond = 180f;

    [Header("Readiness")]
    [Tooltip("GPS accuracy needed before calibration completes, in metres.")]
    [SerializeField] private float requiredAccuracy = 15f;

    [Tooltip("How long the phone must be held steady after AR tracking establishes, in seconds.")]
    [SerializeField] private float steadyHoldSeconds = 1.5f;

    [Tooltip("Degrees of yaw drift per second still counted as 'steady'.")]
    [SerializeField] private float steadyYawTolerance = 12f;

    [Tooltip("Proceed anyway after this long, so a demo can never hang on the steadiness check.")]
    [SerializeField] private float steadyTimeoutSeconds = 12f;

    [Tooltip("Proceed regardless after this long, in seconds. The steadiness check has its own " +
             "timeout but AR tracking and GPS don't, so without this a denied camera permission " +
             "or a bad fix leaves the player stuck on this screen with no way past it. 0 disables.")]
    [SerializeField] private float overallTimeoutSeconds = 30f;

    [Tooltip("How long to leave the 'calibrated' confirmation up before hiding, in seconds.")]
    [SerializeField] private float confirmationSeconds = 1.2f;

    [Tooltip("How close to the start line counts as ready, in metres. This condition IS the " +
             "start-gate trigger now — clearing it is what spawns checkpoints/finish and starts " +
             "the countdown, same radius GameLogic used to check every frame after this screen.")]
    [SerializeField] private float startDistanceThreshold = 10f;

    [Tooltip("GPS accuracy required specifically for the distance-to-start condition, in metres. " +
             "Tighter than the general GPS-fix requirement above (15m) — that one only has to be " +
             "good enough to let calibration proceed, this one gates actually spawning content.")]
    [SerializeField] private float startAccuracyThreshold = 10f;

    [Tooltip("Master switch for AR race capture. True starts RGBCameraCapture recording the moment " +
             "calibration completes; GameLogic.OnFinishLineReached() stops it. False means recording " +
             "never starts at all. This no longer gates calibration — the screen-capture consent " +
             "dialog is no longer in the way of starting a race. Note OS-level AR Screen Recording " +
             "is NOT an alternative to leave this off for: nebula deliberately kills it when a " +
             "third-party app launches, confirmed by the device's own popup, so in-app capture is " +
             "the only route to AR footage.")]
    [SerializeField] private bool recordingEnabled = true;

    /// <summary>True once calibration is complete and the game may start.</summary>
    public bool IsReady { get; private set; }

    private float steadyTimer;
    private float elapsedOnScreen;
    private float elapsedSinceTracking;
    private float lastYaw;
    private bool hasLastYaw;
    private bool trackingSeen;
    private float confirmationTimer;
    private bool confirming;
    private string shownStatus;


    private void Awake()
    {
        Instance = this;

        if (panelRoot != null) panelRoot.SetActive(true);
        if (instructionText != null) instructionText.text = instruction;
    }

    /// <summary>
    /// Kicks off the race recording, once, as calibration completes. Everything after this point
    /// is asynchronous and none of it is waited on: StartRecording() returns immediately, the
    /// Android screen-capture consent dialog is answered while the countdown runs, and if the
    /// player denies it the race is entirely unaffected. That is the deliberate trade — a missing
    /// recording is a far better failure than a race that won't start.
    /// </summary>
    private void StartRecording()
    {
        RGBCameraCapture capture = RGBCameraCapture.Instance;

        if (capture == null)
        {
            Debug.LogWarning("[CalibrationScreen] recordingEnabled is on but no RGBCameraCapture is " +
                             "in the scene — run Sonic Snow > XREAL > Set Up RGB Camera Capture. " +
                             "Racing anyway, with no footage.");
            return;
        }

        Debug.Log("[CalibrationScreen] Calibration cleared — starting AR race capture.");
        capture.StartRecording();
    }

    private void Update()
    {
        if (IsReady) return;

        SpinSpinner();

        if (confirming)
        {
            confirmationTimer += Time.deltaTime;
            if (confirmationTimer >= confirmationSeconds) Finish();
            return;
        }

        bool tracking = ARSession.state == ARSessionState.SessionTracking;
        bool routeLoaded = MapDataFetcher.Instance != null && MapDataFetcher.Instance.IsLoaded;

        LocationHandler location = LocationHandler.Instance;
        bool gpsReady = location != null && location.IsReady && location.HorizontalAccuracy <= requiredAccuracy;

        bool steady = UpdateSteadiness(tracking);

        // Computed directly from LocationHandler/MapDataFetcher rather than reading
        // GameLogic.DistanceToStart — GameLogic doesn't arm its own Update() until this
        // screen reports ready, so that value wouldn't exist yet. Same reasoning as the
        // "armed" gate: this screen can't depend on state that only exists after it closes.
        float distance = -1f;
        bool atStartLine = false;

        if (routeLoaded && location != null && location.IsReady)
        {
            MapData config = MapDataFetcher.Instance.LoadedConfig;
            distance = GpsUtils.HaversineDistance(location.CurrentLatitude, location.CurrentLongitude,
                                                  config.originLat, config.originLng);
            atStartLine = distance <= startDistanceThreshold && location.HorizontalAccuracy <= startAccuracyThreshold;
        }

        if (statusText != null)
        {
            string status =
                Line("AR tracking", tracking) + "\n" +
                Line("Holding steady", steady) + "\n" +
                Line("Route data", routeLoaded) + "\n" +
                Line("GPS fix", gpsReady, GpsDetail(location)) + "\n" +
                Line("Distance to start", atStartLine, StartDistanceDetail(distance, location, routeLoaded));

            // Only push it through when it actually reads differently. Assigning TMP_Text.text
            // forces a full mesh rebuild of five lines of text every frame, and this panel is on
            // screen for the whole of calibration — but the lines only change when a tick flips or
            // the GPS accuracy figure moves, a few times a second at most. Same reasoning as
            // CheckpointCounterUI. The string is still built each frame, which is the cheap half.
            if (status != shownStatus)
            {
                shownStatus = status;
                statusText.text = status;
            }
        }

        elapsedOnScreen += Time.deltaTime;

        bool timedOut = overallTimeoutSeconds > 0f && elapsedOnScreen >= overallTimeoutSeconds;

        if (!(tracking && steady && routeLoaded && gpsReady && atStartLine))
        {
            if (!timedOut) return;

            Debug.LogWarning($"[CalibrationScreen] Timed out after {overallTimeoutSeconds:F0}s and " +
                             $"proceeding anyway. tracking={tracking} steady={steady} " +
                             $"route={routeLoaded} gps={gpsReady} atStartLine={atStartLine}. " +
                             $"Placement will be rough, and if GPS never arrives the race will " +
                             $"start wherever the player happens to be standing.");
        }

        // Everything is up and the phone has been still for a moment. Latch the
        // session origin now, on a settled pose, rather than on whatever frame 1 was.
        if (GeoAnchor.Instance != null) GeoAnchor.Instance.RecaptureLaunchPose();

        confirming = true;
        confirmationTimer = 0f;

        if (instructionText != null) instructionText.text = readyInstruction;
        if (spinner != null) spinner.gameObject.SetActive(false);
    }

    /// <summary>
    /// Tracks whether the phone has been held still long enough since tracking began.
    /// Times out rather than blocking forever — a hung calibration screen would kill a
    /// demo far more effectively than a slightly sloppy launch pose.
    /// </summary>
    private bool UpdateSteadiness(bool tracking)
    {
        if (!tracking)
        {
            steadyTimer = 0f;
            hasLastYaw = false;
            return false;
        }

        if (!trackingSeen)
        {
            trackingSeen = true;
            elapsedSinceTracking = 0f;
        }

        elapsedSinceTracking += Time.deltaTime;

        Camera camera = Camera.main;
        if (camera == null) return false;

        float yaw = camera.transform.eulerAngles.y;

        if (hasLastYaw)
        {
            float driftPerSecond = Mathf.Abs(Mathf.DeltaAngle(lastYaw, yaw)) / Mathf.Max(Time.deltaTime, 1e-4f);
            steadyTimer = driftPerSecond <= steadyYawTolerance ? steadyTimer + Time.deltaTime : 0f;
        }

        lastYaw = yaw;
        hasLastYaw = true;

        if (elapsedSinceTracking >= steadyTimeoutSeconds)
        {
            Debug.LogWarning("[CalibrationScreen] Steadiness check timed out — proceeding on an " +
                             "unsettled launch pose. Placement may be off until the motion fit engages.");
            return true;
        }

        return steadyTimer >= steadyHoldSeconds;
    }

    /// <summary>
    /// Completes calibration without waiting on the readiness conditions. For the
    /// automated flow test, which runs in the Editor where there is no AR session to
    /// establish tracking and so would otherwise sit here until the overall timeout.
    /// </summary>
    public void ForceReady(string reason)
    {
        if (IsReady) return;

        Debug.Log($"[CalibrationScreen] Forced ready — {reason}");

        if (GeoAnchor.Instance != null) GeoAnchor.Instance.RecaptureLaunchPose();

        Finish();
    }

    private void Finish()
    {
        IsReady = true;

        if (panelRoot != null) panelRoot.SetActive(false);

        Debug.Log("[CalibrationScreen] Calibration complete — game ready.");

        // Deliberately after IsReady and after the panel is hidden. GameLogic polls IsReady to
        // trigger the race, so starting the capture first would put the screen-capture consent
        // dialog in front of a player who is already standing at the gate waiting to go.
        if (recordingEnabled) StartRecording();
    }

    private void SpinSpinner()
    {
        if (spinner == null) return;

        spinner.Rotate(0f, 0f, -spinnerDegreesPerSecond * Time.deltaTime);
    }

    /// <summary>
    /// Says why GPS isn't ready yet. A bare spinner on a cold start looks identical to
    /// a spinner on "location is switched off in settings", and only one of those is
    /// something you can fix while standing on the hill.
    /// </summary>
    private string GpsDetail(LocationHandler location)
    {
        if (location == null) return "starting";

        if (location.IsReady)
            return $"±{location.HorizontalAccuracy:F0}m, need ±{requiredAccuracy:F0}m";

        if (!string.IsNullOrEmpty(location.LastFailureReason))
            return location.IsRetrying ? $"retrying — {location.LastFailureReason}" : location.LastFailureReason;

        return "searching for satellites";
    }

    /// <summary>Live distance/accuracy readout against both thresholds, so the last few
    /// metres of walking to the gate are visible instead of a flat "not yet".</summary>
    private string StartDistanceDetail(float distance, LocationHandler location, bool routeLoaded)
    {
        if (!routeLoaded) return "waiting on route data";
        if (location == null || !location.IsReady) return "waiting on GPS";

        return $"{distance:F0}m away, ±{location.HorizontalAccuracy:F0}m " +
               $"(need <{startDistanceThreshold:F0}m, ±{startAccuracyThreshold:F0}m)";
    }

    private static string Line(string label, bool done, string detail = null)
    {
        string mark = done ? "<color=#6EE7A0>OK</color>" : "<color=#888888>...</color>";
        string suffix = string.IsNullOrEmpty(detail) ? "" : $"  <color=#888888>({detail})</color>";

        return $"{mark}  {label}{suffix}";
    }
}
