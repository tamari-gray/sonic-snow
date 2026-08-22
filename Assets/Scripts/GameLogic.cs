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
    [Tooltip("How close to the finish coord ends the race, in metres.")]
    [SerializeField] private float finishProximityRadius = 10f;

    [Tooltip("Don't trigger the finish on a fix worse than this, in metres. Previously unset — " +
             "the finish had no accuracy gate at all, so a rough fix could end the race before " +
             "the rider physically reached the beam.")]
    [SerializeField] private float maxFinishTriggerAccuracy = 20f;

    [Tooltip("Seconds between proximity log lines. The on-screen log only keeps a handful of rows, " +
             "so logging this every frame buries the GeoAnchor and spawn messages you actually need.")]
    [SerializeField] private float proximityLogInterval = 1f;

    [Header("Debug / Scope")]
    [Tooltip("Skips the username entry screen: start line goes straight into the countdown " +
             "using the default \"Player\" name, and the finish line goes straight back to " +
             "searching for start with no Firebase submission (so repeated test runs don't " +
             "spam the leaderboard with placeholder entries). For focusing on core race " +
             "mechanics (checkpoints, finish) without that screen in the way.")]
    [SerializeField] private bool skipUsernameEntry = true;

    [Tooltip("Master switch for the race itself. Off leaves the app sitting on a blank screen once " +
             "calibration clears — nothing spawns and the countdown/checkpoints/finish never run. " +
             "The distance-to-start check that used to gate this lives on CalibrationScreen now (it " +
             "IS one of the five calibration conditions), so this fires once, right when calibration " +
             "completes, rather than every frame in a search loop. On by default now that the start " +
             "gate is folded into calibration — turn off only to park on calibration for debugging.")]
    [SerializeField] private bool raceMechanicsEnabled = true;

    private float lastProximityLogTime = float.NegativeInfinity;

    /// <summary>Set once startup has finished. See the gate in <see cref="Update"/>.</summary>
    private bool armed;

    /// <summary>True once startup is complete and the proximity checks are live.</summary>
    public bool IsArmed => armed;

    private const string LEADERBOARD_URL = "https://sonicar-7ea55-default-rtdb.asia-southeast1.firebasedatabase.app/leaderboard.json";

    void Awake()
    {
        Instance = this;
        Debug.Log("✔ GameLogic Awake ACTIVE");

        if (!raceMechanicsEnabled)
            Debug.LogWarning("[GameLogic] Race mechanics disabled — calibration will still run its " +
                             "distance-to-start check, but nothing will spawn once it clears.");
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

        armed = true;

        // The race clock's on-screen text sits on the head-locked HUD, not inside the
        // calibration panel, so it stays hidden until now rather than showing "0.000"
        // through the whole calibration hold.
        if (RaceTimer.instance != null) RaceTimer.instance.Show();

        // Calibration's "Distance to start" condition IS the start-gate trigger now — by
        // construction it can't clear without the player already standing at the gate
        // with a fix at least as good as the old per-frame check required. No separate
        // search loop needed afterwards.
        TriggerRaceStart();
    }

    void Update()
    {
        PollLocation();

        // Nothing runs until Start() has cleared the route load *and* the calibration
        // screen. Update doesn't wait on that coroutine, so without this gate the
        // default SearchingForStart state would be live from frame one — see the armed
        // flag's history for why that matters.
        if (!armed) return;

        if (CurrentState == GameState.Racing)
        {
            CheckCheckpointProximity();
            CheckFinishLineProximity();
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

    /// <summary>
    /// Fires once, right after calibration confirms the player is at the start line with a
    /// good fix — see CalibrationScreen's "Distance to start" condition, which now owns
    /// that check. Replaces the old per-frame CheckStartLineProximity() trigger: since
    /// calibration only runs once per app launch and the current workflow is a fresh
    /// launch per run, there's no need for a live re-check loop afterwards.
    /// </summary>
    private void TriggerRaceStart()
    {
        if (!raceMechanicsEnabled)
        {
            Debug.LogWarning("[GameLogic] Race mechanics disabled — calibration cleared but nothing will spawn.");
            return;
        }

        if (GeoAnchor.Instance == null)
        {
            Debug.LogError("GeoAnchor instance is null — can't start, content would be placed unaligned!");
            return;
        }

        // Shouldn't happen: the launch seed aligns as soon as the route config loads and
        // the first camera frame exists, both of which finish well before calibration's
        // AR-tracking/steadiness/GPS conditions do. Logged rather than retried, since
        // there's no more per-frame loop to retry from — this call only ever fires once.
        if (!GeoAnchor.Instance.IsAligned)
        {
            Debug.LogError("[GameLogic] Calibration finished but GeoAnchor isn't aligned yet — " +
                           "starting anyway, but placement will be wrong.");
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
                      $"GPS ±{LocationHandler.Instance.HorizontalAccuracy:F1}m (need <{maxFinishTriggerAccuracy:F0}m) | " +
                      $"{checkpoints}anchor {(GeoAnchor.Instance != null ? GeoAnchor.Instance.StatusLine : "missing")}");
        }

        if (distance > finishProximityRadius) return;

        if (LocationHandler.Instance.HorizontalAccuracy > maxFinishTriggerAccuracy)
        {
            if (ShouldLogProximity())
                Debug.Log($"At the finish line but the fix is only good to " +
                          $"{LocationHandler.Instance.HorizontalAccuracy:F1}m — waiting for a better one");
            return;
        }

        OnFinishLineReached();
    }

    /// <summary>
    /// Resets state after a race finishes. There's no live re-trigger afterwards — the
    /// start-gate check now lives on CalibrationScreen and only ever runs once per app
    /// launch — so this just leaves a clean slate for the next launch rather than
    /// searching for anything itself.
    /// </summary>
    void BeginSearchingForStart()
    {
        Debug.Log("Race finished and reset. Relaunch the app to run again.");
        CurrentState = GameState.SearchingForStart;

        // Tear down the previous run's world before resetting the alignment, so
        // nothing is left parented to a root that's about to jump.
        if (FinishLinePillar.Instance != null) FinishLinePillar.Instance.ClearPillar();
        if (CheckpointDomeSpawner.Instance != null) CheckpointDomeSpawner.Instance.ClearDomes();

        // Clear the previous run's alignment. VIO has drifted since then, and the
        // player may well have ridden the lift back up, so the old fit is stale.
        if (GeoAnchor.Instance != null)
            GeoAnchor.Instance.ResetAlignment();
    }


    // player inputs username and presses button to start race
    void InitPlayerAndWorld()
    {
        // Defensive: nothing currently re-enters this mid-launch (the start gate only ever
        // fires once, right after calibration), but if that ever changes, a leftover score
        // screen from the previous run shouldn't still be up for the next one.
        if (FinishScoreUI.Instance != null) FinishScoreUI.Instance.Hide();

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

        if (skipUsernameEntry)
        {
            playerUsername = DefaultUsername;
            EnterRacingState();
        }
        else
        {
            ShowUsernamePanel();
        }
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

        int checkpointsCollected = CheckpointDomeSpawner.Instance != null ? CheckpointDomeSpawner.Instance.CollectedCount : 0;
        int checkpointsTotal = CheckpointDomeSpawner.Instance != null ? CheckpointDomeSpawner.Instance.Total : 0;

        if (FinishLinePillar.Instance != null && GeoAnchor.Instance != null)
            FinishCollectEffect.Play(GeoAnchor.Instance.Root, FinishLinePillar.Instance.LocalPosition);

        if (FinishScoreUI.Instance != null)
            FinishScoreUI.Instance.Show(playerUsername, elapsedSeconds, checkpointsCollected, checkpointsTotal);
        else
            Debug.LogWarning("FinishScoreUI instance is null — run Sonic Snow > Set Up Finish Score.");

        // Finalizes the capture file and hands it to the device's gallery 5 seconds after this
        // moment, not immediately — see RGBCameraCapture.StopRecording's doc comment for why the
        // stop itself, not the recording-just-started flag, is what actually needs to run for
        // anything to be retrievable. The delay is deliberate: it gives the finish celebration
        // (FinishCollectEffect, the scoreboard shown just above) a few seconds of real footage
        // instead of the video cutting out on the exact frame the beam is crossed.
        // This is now the ONLY stop trigger -- RGBCameraCapture's old auto-stop fallback was
        // removed, so hardware that never reaches this method (no GPS fix to trigger the finish
        // line) never stops recording and saves nothing.
        if (RGBCameraCapture.Instance != null && RGBCameraCapture.Instance.IsRecording)
            StartCoroutine(StopRecordingAfterDelay(5f));

        if (skipUsernameEntry)
        {
            // No real username was ever collected in this mode, so submitting would only
            // ever write a placeholder "Player" row — skip straight back to searching.
            ReturnToSearching();
            return;
        }

        // Submit *then* return, in that order. Firing both at once races the PATCH
        // against the leaderboard's own GET, and the GET usually wins — so the player
        // finishes a run and their time isn't on the board they're looking at.
        StartCoroutine(SubmitThenReturn(playerUsername, elapsedSeconds));
    }

    /// <summary>Stops the capture a fixed delay after the finish line is reached, so the finish
    /// celebration and scoreboard land in the footage. Re-checks IsRecording rather than assuming:
    /// something else could in principle have stopped it in the meantime, and StopRecording's own
    /// "not recording" branch would just log a harmless warning either way, but checking first keeps
    /// that warning out of the normal-path logs.</summary>
    private IEnumerator StopRecordingAfterDelay(float delaySeconds)
    {
        yield return new WaitForSeconds(delaySeconds);

        if (RGBCameraCapture.Instance != null && RGBCameraCapture.Instance.IsRecording)
            RGBCameraCapture.Instance.StopRecording();
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