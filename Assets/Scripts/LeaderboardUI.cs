using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;
using TMPro;

public class LeaderboardUI : MonoBehaviour
{
    public static LeaderboardUI Instance;

    [SerializeField] private GameObject rowPrefab;
    [SerializeField] private Transform contentParent;
    [SerializeField] private GameObject panelRoot; // the whole LeaderboardPanel, for show/hide

    private const string LEADERBOARD_URL = "https://sonicar-7ea55-default-rtdb.asia-southeast1.firebasedatabase.app/leaderboard.json";

    private void Awake()
    {
        Instance = this;
    }

    public void Show()
    {
        panelRoot.SetActive(true);
        StartCoroutine(FetchAndDisplay());
    }

    public void Hide()
    {
        panelRoot.SetActive(false);
    }

    private IEnumerator FetchAndDisplay()
    {
        UnityWebRequest request = UnityWebRequest.Get(LEADERBOARD_URL);
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("Leaderboard fetch failed: " + request.error);
            yield break;
        }

        string raw = request.downloadHandler.text;
        Debug.Log("Leaderboard raw: " + raw); // keep this — Firebase shape errors fail silently otherwise

        Dictionary<string, float> entries = ParseFlatJsonObject(raw);

        // Clear old rows
        foreach (Transform child in contentParent)
        {
            Destroy(child.gameObject);
        }

        // Sort by seconds, fastest first
        var sorted = entries
            .Select(kv => new { Name = kv.Key, Seconds = kv.Value })
            .OrderBy(e => e.Seconds)
            .ToList();

        int rank = 1;
        foreach (var entry in sorted)
        {
            GameObject row = Instantiate(rowPrefab, contentParent);
            TMP_Text rowText = row.GetComponent<TMP_Text>();
            if (rowText != null)
            {
                rowText.text = $"{rank}. {entry.Name} — {FormatSeconds(entry.Seconds)}";
            }
            rank++;
        }
    }

    // Manual parse for Firebase's flat { "key": 123.45 } shape — JsonUtility can't handle dictionaries.
    // Values are now raw numbers (total seconds), not quoted strings.
    private Dictionary<string, float> ParseFlatJsonObject(string json)
    {
        var result = new Dictionary<string, float>();

        json = json.Trim();
        if (json == "null" || json.Length < 2) return result; // empty leaderboard

        json = json.Substring(1, json.Length - 2); // strip outer { }

        string[] pairs = json.Split(',');

        foreach (string rawPair in pairs)
        {
            string pair = rawPair.Trim();
            if (string.IsNullOrEmpty(pair)) continue;

            int colonIndex = pair.IndexOf(':');
            if (colonIndex < 0) continue;

            string key = pair.Substring(0, colonIndex).Trim().Trim('"');
            string valueStr = pair.Substring(colonIndex + 1).Trim();

            if (float.TryParse(valueStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float seconds))
            {
                result[key] = seconds;
            }
            else
            {
                Debug.LogWarning($"Leaderboard entry for '{key}' wasn't a parseable number: '{valueStr}'");
            }
        }

        return result;
    }

    // Converts total seconds into "2 minutes 30 seconds" / "10 minutes" / "45 seconds" for display.
    private string FormatSeconds(float totalSeconds)
    {
        int minutes = Mathf.FloorToInt(totalSeconds / 60f);
        int seconds = Mathf.FloorToInt(totalSeconds % 60f);

        string minutePart = minutes == 1 ? "1 minute" : $"{minutes} minutes";
        string secondPart = seconds == 1 ? "1 second" : $"{seconds} seconds";

        if (minutes <= 0) return secondPart;
        if (seconds <= 0) return minutePart;

        return $"{minutePart} {secondPart}";
    }
}