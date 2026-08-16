using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

public class MapDataFetcher : MonoBehaviour
{
    public static MapDataFetcher Instance;

    private const string DATABASE_URL =
        "https://sonicar-7ea55-default-rtdb.asia-southeast1.firebasedatabase.app/.json";

    public MapData LoadedConfig { get; private set; }
    public bool IsLoaded { get; private set; } = false;

    /// <summary>
    /// Stands in for the Firebase route when set before the scene loads (see
    /// RaceFlowSelfTest). Lets the whole flow be exercised offline and against a known
    /// route, rather than whatever happens to be uploaded at the time.
    /// </summary>
    public static MapData MockRoute;

    private void Awake()
    {
        Instance = this;
    }

    public IEnumerator LoadRouteConfig()
    {
        if (MockRoute != null)
        {
            LoadedConfig = MockRoute;
            IsLoaded = true;
            Debug.Log($"[MapDataFetcher] Mock route in use — {LoadedConfig.originLat}, {LoadedConfig.originLng} " +
                      $"with {(LoadedConfig.checkpoints != null ? LoadedConfig.checkpoints.Length : 0)} checkpoints.");
            yield break;
        }

        UnityWebRequest request = UnityWebRequest.Get(DATABASE_URL);
        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            Debug.Log("web request result: " + request.downloadHandler.text);

            LoadedConfig = JsonUtility.FromJson<MapData>(request.downloadHandler.text);
            IsLoaded = true;
            Debug.Log("Route config loaded: " + LoadedConfig.originLat + ", " + LoadedConfig.originLng);
        }
        else
        {
            Debug.LogError("Firebase fetch failed: " + request.error);
        }
    }
}