using TMPro;
using UnityEngine;

/// <summary>
/// Shows live distance to the start line on the leaderboard screen, reading the same
/// value GameLogic already computes for its on-screen proximity log — just without the
/// log's throttle, so this updates every frame instead of once a second.
/// </summary>
public class StartDistanceUI : MonoBehaviour
{
    [SerializeField] private TMP_Text label;

    /// <summary>For binding at runtime, e.g. when the label is built in code (see
    /// RetroLeaderboardUI's distance row) rather than wired in the editor.</summary>
    public void Bind(TMP_Text text) => label = text;

    void Update()
    {
        if (label == null || GameLogic.Instance == null) return;

        float distance = GameLogic.Instance.DistanceToStart;
        if (distance < 0f)
        {
            label.text = "Distance to start: -- m";
            return;
        }

        string accuracy = LocationHandler.Instance != null
            ? $" ({LocationHandler.Instance.HorizontalAccuracy:F0}m±)"
            : "";

        label.text = $"Distance to start: {distance:F0}m{accuracy}";
    }
}
