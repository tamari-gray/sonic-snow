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

    private void Awake()
    {
        Instance = this;
    }

    public IEnumerator LoadRouteConfig()
    {
        UnityWebRequest request = UnityWebRequest.Get(DATABASE_URL);
        yield return request.SendWebRequest();

        Debug.LogError("sent web request");


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