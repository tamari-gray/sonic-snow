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

    [Tooltip("How long to wait for GPS to initialize before giving up.")]
    [SerializeField] private float initTimeoutSeconds = 20f;

    [Header("Debug")]
    [SerializeField] private bool logUpdates = false;

    // --- Public state ---
    public bool IsReady { get; private set; } = false;
    public double CurrentLatitude { get; private set; }
    public double CurrentLongitude { get; private set; }
    public double CurrentAltitude { get; private set; }
    public float HorizontalAccuracy { get; private set; }
    public double LastTimestamp { get; private set; }

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
                Fail("Location permission denied by user.");
                yield break;
            }
        }
#endif

        if (!Input.location.isEnabledByUser)
        {
            Fail("Location services disabled by user (check device settings).");
            yield break;
        }

        Input.location.Start(desiredAccuracyMeters, updateDistanceMeters);

        float elapsed = 0f;
        while (Input.location.status == LocationServiceStatus.Initializing && elapsed < initTimeoutSeconds)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (Input.location.status == LocationServiceStatus.Failed)
        {
            Fail("Location service failed to initialize.");
            yield break;
        }

        if (Input.location.status != LocationServiceStatus.Running)
        {
            Fail($"Location service did not start in time (status: {Input.location.status}).");
            yield break;
        }

        // We have a running service — pull the first reading immediately.
        ReadLocation(firstFix: true);
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

    private void Fail(string reason)
    {
        IsReady = false;
        Debug.LogWarning($"[LocationHandler] {reason}");
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