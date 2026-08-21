using UnityEngine;
using TMPro;

public class RaceTimer : MonoBehaviour
{
    public static RaceTimer instance;

    public TMP_Text timerText;

    private float elapsedTime = 0f;
    private bool isRunning = false;

    public float ElapsedTime => elapsedTime;

    /// <summary>True while the clock is counting. Read by the automated flow test.</summary>
    public bool IsRunning => isRunning;

    private void Awake()
    {
        instance = this;

        // Sits on the head-locked HUD canvas as a sibling of the calibration panel, not a
        // child of it — so with no gating of its own it was showing "0.000" through the
        // whole calibration hold. Hidden until GameLogic calls Show(), once calibration
        // actually clears.
        if (timerText != null) timerText.gameObject.SetActive(false);
    }

    void Start()
    {
        UpdateTimerText();
    }

    /// <summary>Reveals the timer text. Call once calibration finishes — see GameLogic.Start().</summary>
    public void Show()
    {
        if (timerText != null) timerText.gameObject.SetActive(true);
    }

    void Update()
    {
        if (!isRunning) return;

        elapsedTime += Time.deltaTime;
        UpdateTimerText();
    }

    public void StartTimer()
    {
        isRunning = true;

        Debug.Log("Timer started");
    }

    public void StopTimer()
    {
        isRunning = false;

        Debug.Log("Timer stopped");
    }

    public void ResetTimer()
    {
        elapsedTime = 0f;
        UpdateTimerText();
    }

    private void UpdateTimerText()
    {
        // SetText with a format, not .text = ToString("F3"): this runs every frame of every race,
        // and the ToString allocates a fresh string each time for the GC to collect later. TMP's
        // SetText formats into its own char buffer instead, so the per-frame allocation is zero.
        // "{0:0.000}" is the same three-decimal output "F3" produced.
        timerText.SetText("{0:0.000}", elapsedTime);
    }
}