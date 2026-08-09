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

    public static GameLogic Instance;

    public GameState CurrentState { get; private set; } = GameState.SearchingForStart;

    /// <summary>The sanitised name this run will be filed under. Lets the leaderboard
    /// pick out the local player's row without being told.</summary>
    public string PlayerUsername => playerUsername;

    private double currentLat;
    private double currentLng;

    private const string DefaultUsername = "Player";

    // Firebase caps keys at 768 bytes. A leaderboard row anywhere near that is a
    // mistake or an attack, and it would wreck the layout either way.
    private const int MaxUsernameLength = 32;

    private string playerUsername = DefaultUsername;

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
        Instance = this;
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
                CheckCheckpointProximity();
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

    /// <summary>
    /// Retires checkpoints as the rider rides through them. Racing-only, so walking past
    /// one while still entering a username doesn't quietly consume it.
    /// </summary>
    private void CheckCheckpointProximity()
    {
        if (LocationHandler.Instance == null || !LocationHandler.Instance.IsReady) return;
        if (CheckpointDomeSpawner.Instance == null) return;

        CheckpointDomeSpawner.Instance.CheckProximity(currentLat, currentLng);
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
            CheckpointDomeSpawner domes = CheckpointDomeSpawner.Instance;
            string checkpoints = domes != null && domes.Total > 0
                ? $"CP {domes.CollectedCount}/{domes.Total} | " : "";

            Debug.Log($"Finish line {distance:F1}m away (need <{finishProximityRadius:F0}m) | " +
                      $"GPS ±{LocationHandler.Instance.HorizontalAccuracy:F1}m | {checkpoints}" +
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

        if (RetroLeaderboardUI.Instance != null)
            RetroLeaderboardUI.Instance.Show();
        else
            Debug.LogWarning("RetroLeaderboardUI instance is null — run Sonic Snow > Set Up Leaderboard.");
    }


    // player inputs username and presses button to start race
    void InitPlayerAndWorld()
    {
        if (RetroLeaderboardUI.Instance != null)
            RetroLeaderboardUI.Instance.Hide();

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

    /// <summary>
    /// Hands GameLogic the widgets a runtime-built username panel created, so the rest of
    /// this class carries on working against the same two references it always had.
    /// Called by <see cref="RetroUsernamePanel"/> before the first race.
    /// </summary>
    public void BindUsernameUI(TMP_InputField field, GameObject play)
    {
        usernameInputField = field;
        playButton = play;
    }

    void ShowUsernamePanel()
    {
        // The retro panel owns the backdrop and framing; the input field below is one of
        // its children, so showing the field alone would leave the chrome hidden.
        if (RetroUsernamePanel.Instance != null) RetroUsernamePanel.Instance.Show();

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

        // From here the rider leaves the gate, so the launch position stops describing
        // where they are. Until this moment every seed should keep using it.
        if (GeoAnchor.Instance != null) GeoAnchor.Instance.MarkLaunchPoseSpent();

        if (RaceTimer.instance != null)
        {
            RaceTimer.instance.StartTimer();
        }
    }

    // Hook this up to the Play button's OnClick in the Inspector.
    public void OnPlayButtonPressed()
    {
        if (CurrentState != GameState.PlayerInit) return;

        // Sanitise here rather than at submit time, so playerUsername is the exact
        // string that ends up on the board and the log below doesn't lie about it.
        playerUsername = SanitiseForFirebaseKey(usernameInputField != null ? usernameInputField.text : "");

        Debug.Log("Player username set to: " + playerUsername);

        if (playButton != null) playButton.SetActive(false);
        if (usernameInputField != null) usernameInputField.gameObject.SetActive(false);
        if (RetroUsernamePanel.Instance != null) RetroUsernamePanel.Instance.Hide();

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

    /// <summary>
    /// Reduces a typed-in name to something Firebase will accept as a key.
    ///
    /// Firebase rejects keys containing . $ # [ ] / or ASCII control characters. The
    /// slash is the nasty one: in a PATCH body Firebase reads keys as *paths*, so
    /// "tam/1" would quietly write leaderboard/tam/1 and nest the entry instead of
    /// listing it, rather than failing loudly.
    ///
    /// Illegal characters are dropped rather than replaced, because mapping them all
    /// to one substitute lets two different players collide on the same key and
    /// overwrite each other's times.
    /// </summary>
    private static string SanitiseForFirebaseKey(string name)
    {
        if (string.IsNullOrEmpty(name)) return DefaultUsername;

        StringBuilder clean = new StringBuilder(name.Length);

        foreach (char c in name)
        {
            if (c == '.' || c == '$' || c == '#' || c == '[' || c == ']' || c == '/') continue;
            if (char.IsControl(c)) continue;

            clean.Append(c);
        }

        // Trim last: stripping control characters can expose whitespace at the ends.
        string result = clean.ToString().Trim();

        if (result.Length > MaxUsernameLength)
        {
            int cut = MaxUsernameLength;

            // An emoji is two chars, so a blind cut can leave a lone surrogate behind —
            // which isn't valid UTF-8 and gets rejected on the way out.
            if (char.IsHighSurrogate(result[cut - 1])) cut--;

            result = result.Substring(0, cut).Trim();
        }

        return result.Length == 0 ? DefaultUsername : result;
    }

    /// <summary>
    /// Escapes a string for use as a JSON string literal.
    ///
    /// Quote and backslash are both legal Firebase keys, so sanitising for Firebase
    /// isn't enough on its own — a name like O"Brien would still produce malformed
    /// JSON and a 400 that costs the player their run.
    /// </summary>
    private static string EscapeJsonString(string value)
    {
        StringBuilder escaped = new StringBuilder(value.Length + 8);

        foreach (char c in value)
        {
            if (c == '"' || c == '\\') escaped.Append('\\').Append(c);
            else if (c < 0x20) escaped.Append("\\u").Append(((int)c).ToString("x4"));
            else escaped.Append(c);
        }

        return escaped.ToString();
    }

    private IEnumerator SubmitLeaderboardEntry(string username, float elapsedSeconds)
    {
        string json = "{\"" + EscapeJsonString(username) + "\":" +
                      elapsedSeconds.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + "}";

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