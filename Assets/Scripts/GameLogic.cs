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

    public TMP_InputField inputField;

    [Header("Username UI")]
    public TMP_InputField usernameInputField;
    public GameObject playButton;

    public GameState CurrentState { get; private set; } = GameState.SearchingForStart;

    private double currentLat;
    private double currentLng;

    private string playerUsername = "Player";

    private const float START_PROXIMITY_RADIUS = 10f;  // meters
    private const float FINISH_PROXIMITY_RADIUS = 10f;  // meters

    private const string LEADERBOARD_URL = "https://sonicar-7ea55-default-rtdb.asia-southeast1.firebasedatabase.app/leaderboard.json";

    void Awake()
    {
        Debug.Log("✔ GameLogic Awake ACTIVE");
    }

    IEnumerator Start()
    {
        Debug.Log("✔ GameLogic Start ACTIVE");

        yield return StartCoroutine(MapDataFetcher.Instance.LoadRouteConfig());

        if (MapDataFetcher.Instance == null)
        {
            Debug.Log("Mapdata fetcher is null my dude");
            yield break;
        }

        if (!MapDataFetcher.Instance.IsLoaded)
        {
            Debug.LogError("No route config — aborting game start");
            yield break;
        }

        //inputField.onValueChanged.AddListener(CheckCommand);

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

    private void CheckStartLineProximity()
    {
        if (LocationHandler.Instance == null || !LocationHandler.Instance.IsReady) return;

        if (MapDataFetcher.Instance == null || !MapDataFetcher.Instance.IsLoaded)
        {
            Debug.Log("Mapdata fetcher is not loaded");
            return;
        }

        float distance = HaversineDistance(currentLat, currentLng, MapDataFetcher.Instance.LoadedConfig.originLat, MapDataFetcher.Instance.LoadedConfig.originLng);

        Debug.Log($"You are {distance:F1}m away from start line");

        if (distance <= START_PROXIMITY_RADIUS)
        {
            if (CalibrateWorld.Instance != null && !CalibrateWorld.Instance.IsCalibrated)
            {
                CalibrateWorld.Instance.AlignWorldToRoute(
                    MapDataFetcher.Instance.LoadedConfig.originLat,
                    MapDataFetcher.Instance.LoadedConfig.originLng,
                    MapDataFetcher.Instance.LoadedConfig.finishLat,
                    MapDataFetcher.Instance.LoadedConfig.finishLng
                );
            }

            CurrentState = GameState.PlayerInit;
            InitPlayerAndWorld();
        }
    }

    private void CheckFinishLineProximity()
    {
        if (LocationHandler.Instance == null || !LocationHandler.Instance.IsReady) return;

        if (MapDataFetcher.Instance == null || !MapDataFetcher.Instance.IsLoaded) return;

        float distance = HaversineDistance(currentLat, currentLng, MapDataFetcher.Instance.LoadedConfig.finishLat, MapDataFetcher.Instance.LoadedConfig.finishLng);

        Debug.Log($"You are {distance:F1}m away from finish line");

        if (distance <= FINISH_PROXIMITY_RADIUS)
        {
            OnFinishLineReached();
        }
    }

    private float HaversineDistance(double lat1, double lng1, double lat2, double lng2)
    {
        const double EARTH_RADIUS_M = 6371000d;

        double lat1Rad = lat1 * Mathf.Deg2Rad;
        double lat2Rad = lat2 * Mathf.Deg2Rad;
        double deltaLat = (lat2 - lat1) * Mathf.Deg2Rad;
        double deltaLng = (lng2 - lng1) * Mathf.Deg2Rad;

        double a = System.Math.Sin(deltaLat / 2) * System.Math.Sin(deltaLat / 2) +
                   System.Math.Cos(lat1Rad) * System.Math.Cos(lat2Rad) *
                   System.Math.Sin(deltaLng / 2) * System.Math.Sin(deltaLng / 2);

        double c = 2 * System.Math.Atan2(System.Math.Sqrt(a), System.Math.Sqrt(1 - a));

        return (float)(EARTH_RADIUS_M * c);
    }


    void BeginSearchingForStart()
    {
        Debug.Log("Searching for start line...");
        CurrentState = GameState.SearchingForStart;

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

        StartCoroutine(SubmitLeaderboardEntry(playerUsername, elapsedSeconds));

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