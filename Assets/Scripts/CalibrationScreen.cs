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
/// Readiness is four separate conditions, reported individually so a stall on the
/// hill is diagnosable rather than a mystery spinner.
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
    private string instruction = "Stand on the start line and point your phone at the finish.\n\nHold still while we calibrate.";

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

    [Tooltip("How long to leave the 'calibrated' confirmation up before hiding, in seconds.")]
    [SerializeField] private float confirmationSeconds = 1.2f;

    /// <summary>True once calibration is complete and the game may start.</summary>
    public bool IsReady { get; private set; }

    private float steadyTimer;
    private float elapsedSinceTracking;
    private float lastYaw;
    private bool hasLastYaw;
    private bool trackingSeen;
    private float confirmationTimer;
    private bool confirming;

    private void Awake()
    {
        Instance = this;

        if (panelRoot != null) panelRoot.SetActive(true);
        if (instructionText != null) instructionText.text = instruction;
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

        if (statusText != null)
        {
            statusText.text =
                Line("AR tracking", tracking) + "\n" +
                Line("Holding steady", steady) + "\n" +
                Line("Route data", routeLoaded) + "\n" +
                Line("GPS fix", gpsReady, GpsDetail(location));
        }

        if (!(tracking && steady && routeLoaded && gpsReady)) return;

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

    private void Finish()
    {
        IsReady = true;

        if (panelRoot != null) panelRoot.SetActive(false);

        Debug.Log("[CalibrationScreen] Calibration complete — game ready.");
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

    private static string Line(string label, bool done, string detail = null)
    {
        string mark = done ? "<color=#6EE7A0>OK</color>" : "<color=#888888>...</color>";
        string suffix = string.IsNullOrEmpty(detail) ? "" : $"  <color=#888888>({detail})</color>";

        return $"{mark}  {label}{suffix}";
    }
}
