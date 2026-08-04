using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.Text;

public class GameLogic : MonoBehaviour
{
    public enum GameState
    {
        SearchingForStart,
        PlayerInit,
        Racing,
        FinishedRace
    }

    [Header("Username UI")]
    public TMP_InputField usernameInputField;
    public GameObject playButton;

    public GameState CurrentState { get; private set; } = GameState.SearchingForStart;

    private double currentLat;
    private double currentLng;

    private string playerUsername = "Player";

    [Header("Proximity")]
    [Tooltip("How close to the origin counts as being at the start gate, in metres.")]
    [SerializeField] private float startProximityRadius = 10f;

    [Tooltip("How close to the finish coord ends the race, in metres.")]
    [SerializeField] private float finishProximityRadius = 10f;

    [Tooltip("Don't trigger the start on a fix worse than this, in metres. Must stay comfortably " +
             "below the distance from start to finish, or a sloppy fix can read as 'at the gate' " +
             "from down the route. 20m suits street testing, where accuracy rarely beats 10m; " +
             "tighten it on an open slope.")]
    [SerializeField] private float maxTriggerAccuracy = 20f;

    [Tooltip("Seconds between proximity log lines. The on-screen log only keeps a handful of rows, " +
             "so logging this every frame buries the GeoAnchor and spawn messages you actually need.")]
    [SerializeField] private float proximityLogInterval = 1f;

    private float lastProximityLogTime = float.NegativeInfinity;

    private const string LEADERBOARD_URL = "https://sonicar-7ea55-default-rtdb.asia-southeast1.firebasedatabase.app/leaderboard.json";

    void Awake()
    {
        Debug.Log("✔ GameLogic Awake ACTIVE");
    }

    IEnumerator Start()
    {
        Debug.Log("✔ GameLogic Start ACTIVE");

        if (MapDataFetcher.Instance == null)
        {
            Debug.LogError("No MapDataFetcher in the scene — nothing can be placed without a route.");
            yield break;
        }

        yield return StartCoroutine(MapDataFetcher.Instance.LoadRouteConfig());

        if (!MapDataFetcher.Instance.IsLoaded)
        {
            Debug.LogError("No route config — aborting game start");
            yield break;
        }

        // Hold everything behind the calibration screen. Starting the search early
        // would let the player trigger a race off an unsettled launch pose, which is
        // the one thing the ritual exists to prevent.
        if (CalibrationScreen.Instance != null)
        {
            yield return new WaitUntil(() => CalibrationScreen.Instance.IsReady);
        }

        BeginSearchingForStart();
    }

    void Update()
    {
        PollLocation();

        switch (CurrentState)
        {
            case GameState.SearchingForStart:
                CheckStartLineProximity();
                break;

            case GameState.Racing:
                CheckFinishLineProximity();
                break;
        }
    }

    private void PollLocation()
    {
        if (LocationHandler.Instance == null || !LocationHandler.Instance.IsReady) return;

        currentLat = LocationHandler.Instance.CurrentLatitude;
        currentLng = LocationHandler.Instance.CurrentLongitude;
    }

    /// <summary>Rate-limits the proximity lines so they don't flush everything else off the HUD.</summary>
    private bool ShouldLogProximity()
    {
        if (Time.unscaledTime - lastProximityLogTime < proximityLogInterval) return false;

        lastProximityLogTime = Time.unscaledTime;
        return true;
    }

    private void CheckStartLineProximity()
    {
        if (LocationHandler.Instance == null || !LocationHandler.Instance.IsReady) return;

        if (MapDataFetcher.Instance == null || !MapDataFetcher.Instance.IsLoaded)
        {
            if (ShouldLogProximity()) Debug.Log("Route config not loaded yet — can't check the start line");
            return;
        }

        MapData config = MapDataFetcher.Instance.LoadedConfig;

        float distance = GpsUtils.HaversineDistance(currentLat, currentLng, config.originLat, config.originLng);

        // Everything you need to diagnose a failed start, on one line in the on-screen log.
        if (ShouldLogProximity())
        {
            Debug.Log($"Start line {distance:F1}m away (need <{startProximityRadius:F0}m) | " +
                      $"GPS ±{LocationHandler.Instance.HorizontalAccuracy:F1}m (need <{maxTriggerAccuracy:F0}m) | " +
                      $"anchor {(GeoAnchor.Instance != null ? GeoAnchor.Instance.StatusLine : "missing")}");
        }

        if (distance > startProximityRadius) return;

        if (LocationHandler.Instance.HorizontalAccuracy > maxTriggerAccuracy)
        {
            if (ShouldLogProximity())
                Debug.Log($"At the start line but the fix is only good to " +
                          $"{LocationHandler.Instance.HorizontalAccuracy:F1}m — waiting for a better one");
            return;
        }

        if (GeoAnchor.Instance == null)
        {
            Debug.LogError("GeoAnchor instance is null — can't start, content would be placed unaligned!");
            return;
        }

        // Spawning before any alignment exists drops the beam at a meaningless spot,
        // which reads as the whole system being broken. Hold the start instead. With
        // the launch seed this should only ever trip while the route config is still
        // downloading, since the seed needs no GPS fix and no movement.
        if (!GeoAnchor.Instance.IsAligned)
        {
            Debug.Log("At the start line but the world isn't aligned yet — waiting on the route config");
            return;
        }

        // The player is standing at the gate, so their real altitude is the route's
        // originAlt. That's the only moment we can tie surveyed altitudes to Unity's
        // vertical axis, so pin the ground plane here before anything is spawned.
        GeoAnchor.Instance.AnchorVertical();

        CurrentState = GameState.PlayerInit;
        InitPlayerAndWorld();
    }

    private void CheckFinishLineProximity()
    {
        if (LocationHandler.Instance == null || !LocationHandler.Instance.IsReady) return;

        if (MapDataFetcher.Instance == null || !MapDataFetcher.Instance.IsLoaded) return;

        MapData config = MapDataFetcher.Instance.LoadedConfig;

        // Race timing is decided by GPS, never by the visual beam. The beam is an
        // estimate that converges during the run; GPS is the source of truth.
        float distance = GpsUtils.HaversineDistance(currentLat, currentLng, config.finishLat, config.finishLng);

        if (ShouldLogProximity())
        {
            Debug.Log($"Finish line {distance:F1}m away (need <{finishProximityRadius:F0}m) | " +
                      $"GPS ±{LocationHandler.Instance.HorizontalAccuracy:F1}m | " +
                      $"anchor {(GeoAnchor.Instance != null ? GeoAnchor.Instance.StatusLine : "missing")}");
        }

        if (distance <= finishProximityRadius)
        {
            OnFinishLineReached();
        }
    }

    void BeginSearchingForStart()
    {
        Debug.Log("Searching for start line...");
        CurrentState = GameState.SearchingForStart;

        // Tear down the previous run's world before resetting the alignment, so
        // nothing is left parented to a root that's about to jump.
        if (FinishLinePillar.Instance != null) FinishLinePillar.Instance.ClearPillar();
        if (CheckpointDomeSpawner.Instance != null) CheckpointDomeSpawner.Instance.ClearDomes();

        // Clear the previous run's alignment. VIO has drifted since then, and the
        // player may well have ridden the lift back up, so the old fit is stale.
        if (GeoAnchor.Instance != null)
            GeoAnchor.Instance.ResetAlignment();

        if (LeaderboardUI.Instance != null)
            LeaderboardUI.Instance.Show();
        else
            Debug.LogWarning("LeaderboardUI instance is null!");
    }


    // player inputs username and presses button to start race
    void InitPlayerAndWorld()
    {
        if (LeaderboardUI.Instance != null)
            LeaderboardUI.Instance.Hide();

        if (RaceTimer.instance != null)
        {
            RaceTimer.instance.ResetTimer();
        }

        if (CheckpointDomeSpawner.Instance != null)
            CheckpointDomeSpawner.Instance.SpawnDomes();
        else
            Debug.LogWarning("CheckpointDomeSpawner instance is null!");

        if (FinishLinePillar.Instance != null)
            FinishLinePillar.Instance.SpawnPillar();
        else
            Debug.LogWarning("FinishLinePillar instance is null!");

        ShowUsernamePanel();
    }

    void ShowUsernamePanel()
    {
        if (usernameInputField != null)
        {
            usernameInputField.gameObject.SetActive(true);
            usernameInputField.text = ""; // clearing this also re-triggers UsernameInputValidator to hide the Play button
        }
        else
        {
            Debug.LogWarning("usernameInputField is not assigned!");
        }
    }

    void EnterRacingState()
    {
        Debug.Log("Start line reached — starting countdown!");

        if (CountdownTimer.instance != null)
        {
            CountdownTimer.instance.StartCountdown(OnCountdownComplete);
        }
        else
        {
            Debug.LogWarning("CountdownTimer instance is null — skipping countdown");
            OnCountdownComplete();
        }
    }

    private void OnCountdownComplete()
    {
        Debug.Log("Countdown complete — racing!");

        CurrentState = GameState.Racing;

        if (RaceTimer.instance != null)
        {
            RaceTimer.instance.StartTimer();
        }
    }

    // Hook this up to the Play button's OnClick in the Inspector.
    public void OnPlayButtonPressed()
    {
        if (CurrentState != GameState.PlayerInit) return;

        string enteredName = usernameInputField != null ? usernameInputField.text.Trim() : "";
        playerUsername = string.IsNullOrEmpty(enteredName) ? "Player" : enteredName;

        Debug.Log("Player username set to: " + playerUsername);

        if (playButton != null) playButton.SetActive(false);
        if (usernameInputField != null) usernameInputField.gameObject.SetActive(false);

        EnterRacingState();
    }

    public void OnFinishLineReached()
    {
        if (CurrentState != GameState.Racing) return;

        Debug.Log("Finish line reached!");

        CurrentState = GameState.FinishedRace;

        float elapsedSeconds = 0f;

        if (RaceTimer.instance != null)
        {
            RaceTimer.instance.StopTimer();
            elapsedSeconds = RaceTimer.instance.ElapsedTime;
        }

        // Submit *then* return, in that order. Firing both at once races the PATCH
        // against the leaderboard's own GET, and the GET usually wins — so the player
        // finishes a run and their time isn't on the board they're looking at.
        StartCoroutine(SubmitThenReturn(playerUsername, elapsedSeconds));
    }

    private IEnumerator SubmitThenReturn(string username, float elapsedSeconds)
    {
        yield return StartCoroutine(SubmitLeaderboardEntry(username, elapsedSeconds));

        ReturnToSearching();
    }

    private IEnumerator SubmitLeaderboardEntry(string username, float elapsedSeconds)
    {
        string json = "{\"" + username + "\":" + elapsedSeconds.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + "}";

        UnityWebRequest request = new UnityWebRequest(LEADERBOARD_URL, "PATCH");
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");

        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            Debug.Log("Leaderboard entry submitted: " + json);
        }
        else
        {
            Debug.LogError("Leaderboard submit failed: " + request.error + " | " + request.downloadHandler.text);
        }
    }

    public void ReturnToSearching()
    {
        if (CurrentState != GameState.FinishedRace) return;

        BeginSearchingForStart();
    }

}