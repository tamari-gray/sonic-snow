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

    void Update()
    {
        if (label == null || GameLogic.Instance == null) return;

        float distance = GameLogic.Instance.DistanceToStart;
        label.text = distance >= 0f ? $"{distance:F0}m to start" : "-- m to start";
    }
}
