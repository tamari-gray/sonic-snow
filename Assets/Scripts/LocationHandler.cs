using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Android;

/// <summary>
/// Owns Input.location. Starts the GPS service, waits for a fix,
/// and broadcasts updates via OnLocationUpdated. Other scripts
/// (WorldOrigin, debug HUDs, etc.) should subscribe to this rather
/// than touching Input.location directly.
/// </summary>
public class LocationHandler : MonoBehaviour
{
    public static LocationHandler Instance;

    [Header("Location Service Settings")]
    [Tooltip("Desired accuracy in meters. Smaller = more battery drain.")]
    [SerializeField] private float desiredAccuracyMeters = 1f;

    [Tooltip("Minimum distance (meters) the device must move before an update fires.")]
    [SerializeField] private float updateDistanceMeters = 1f;

    [Tooltip("How long to wait for a fix before retrying. A cold start with no cached almanac " +
             "can take 30-60s; a warm one is usually a few seconds.")]
    [SerializeField] private float initTimeoutSeconds = 45f;

    [Tooltip("Pause between attempts, in seconds.")]
    [SerializeField] private float retryDelaySeconds = 3f;

    [Tooltip("Attempts before giving up. 0 = keep trying indefinitely.")]
    [SerializeField] private int maxInitAttempts = 0;

    [Header("Debug")]
    [SerializeField] private bool logUpdates = false;

    // --- Public state ---
    public bool IsReady { get; private set; } = false;
    public double CurrentLatitude { get; private set; }
    public double CurrentLongitude { get; private set; }
    public double CurrentAltitude { get; private set; }
    public float HorizontalAccuracy { get; private set; }
    public double LastTimestamp { get; private set; }

    /// <summary>Why the last attempt failed, or null. Surfaced on the calibration screen.</summary>
    public string LastFailureReason { get; private set; }

    /// <summary>True while retrying. Lets UI distinguish "still trying" from "gave up".</summary>
    public bool IsRetrying { get; private set; }

    // --- Events ---
    /// <summary>Fires every time a new GPS reading comes in (lat, lng).</summary>
    public event Action<double, double> OnLocationUpdated;

    /// <summary>Fires once, the first time the service produces a valid fix.</summary>
    public event Action OnLocationReady;

    /// <summary>Fires if the service fails to start or times out. Passes a reason string.</summary>
    public event Action<string> OnLocationFailed;

    private double lastPolledTimestamp = -1;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        StartCoroutine(InitializeLocationService());
    }

    private IEnumerator InitializeLocationService()
    {
#if UNITY_ANDROID
        if (!Permission.HasUserAuthorizedPermission(Permission.FineLocation))
        {
            Permission.RequestUserPermission(Permission.FineLocation);

            // Give the user a moment to respond to the OS prompt.
            float permissionWait = 0f;
            while (!Permission.HasUserAuthorizedPermission(Permission.FineLocation) && permissionWait < 10f)
            {
                permissionWait += Time.deltaTime;
                yield return null;
            }

            if (!Permission.HasUserAuthorizedPermission(Permission.FineLocation))
            {
                // Terminal — no amount of retrying helps until the user grants it.
                Fail("Location permission denied. Grant it in Android settings, then relaunch.");
                yield break;
            }
        }
#endif

        // Keep retrying rather than dying on the first miss. A cold GPS start can
        // easily exceed the timeout, and with a relaunch-per-run workflow a single
        // permanent failure costs a whole run.
        int attempt = 0;

        while (maxInitAttempts <= 0 || attempt < maxInitAttempts)
        {
            attempt++;
            IsRetrying = attempt > 1;

            yield return StartCoroutine(TryStartLocationService());

            if (IsReady)
            {
                IsRetrying = false;
                LastFailureReason = null;
                Debug.Log($"[LocationHandler] GPS fix acquired on attempt {attempt}.");
                yield break;
            }

            Debug.LogWarning($"[LocationHandler] Attempt {attempt} failed ({LastFailureReason}). " +
                             $"Retrying in {retryDelaySeconds:F0}s.");

            yield return new WaitForSeconds(retryDelaySeconds);
        }

        IsRetrying = false;
        Fail($"GPS gave up after {attempt} attempts. Last reason: {LastFailureReason}");
    }

    private IEnumerator TryStartLocationService()
    {
        if (!Input.location.isEnabledByUser)
        {
            // Not terminal — the user can flip it on in settings and the next attempt picks it up.
            Note("Location services switched off in device settings.");
            yield break;
        }

        Input.location.Start(desiredAccuracyMeters, updateDistanceMeters);

        float elapsed = 0f;
        while (Input.location.status == LocationServiceStatus.Initializing && elapsed < initTimeoutSeconds)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (Input.location.status == LocationServiceStatus.Running)
        {
            ReadLocation(firstFix: true);
            yield break;
        }

        Note(Input.location.status == LocationServiceStatus.Failed
            ? "Location service failed to initialize."
            : $"No fix within {initTimeoutSeconds:F0}s (status: {Input.location.status}).");

        // Stop before retrying, otherwise the next Start() call is a no-op on a
        // service that's already wedged in Initializing.
        Input.location.Stop();
    }

    private void Update()
    {
        if (!IsReady && Input.location.status != LocationServiceStatus.Running) return;
        if (Input.location.status != LocationServiceStatus.Running) return;

        // Input.location doesn't push events, so we poll and diff on timestamp
        // to avoid firing duplicate updates every frame.
        double ts = Input.location.lastData.timestamp;
        if (ts > lastPolledTimestamp)
        {
            ReadLocation(firstFix: false);
        }
    }

    private void ReadLocation(bool firstFix)
    {
        LocationInfo data = Input.location.lastData;

        CurrentLatitude = data.latitude;
        CurrentLongitude = data.longitude;
        CurrentAltitude = data.altitude;
        HorizontalAccuracy = data.horizontalAccuracy;
        LastTimestamp = data.timestamp;
        lastPolledTimestamp = data.timestamp;

        if (logUpdates)
        {
            //Debug.Log($"[LocationHandler] lat={CurrentLatitude:F6}, lng={CurrentLongitude:F6}, " +
            //          $"acc={HorizontalAccuracy:F1}m, ts={LastTimestamp}");
        }

        OnLocationUpdated?.Invoke(CurrentLatitude, CurrentLongitude);

        if (firstFix)
        {
            IsReady = true;
            OnLocationReady?.Invoke();
        }
    }

    /// <summary>Transient failure — recorded, will be retried.</summary>
    private void Note(string reason)
    {
        LastFailureReason = reason;
    }

    /// <summary>Terminal failure — we've stopped trying.</summary>
    private void Fail(string reason)
    {
        IsReady = false;
        LastFailureReason = reason;
        Debug.LogError($"[LocationHandler] {reason}");
        OnLocationFailed?.Invoke(reason);
    }

    /// <summary>Stops the GPS service. Call on scene teardown if needed.</summary>
    public void StopLocationService()
    {
        if (Input.location.status == LocationServiceStatus.Running)
        {
            Input.location.Stop();
        }
        IsReady = false;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            StopLocationService();
        }
    }
}