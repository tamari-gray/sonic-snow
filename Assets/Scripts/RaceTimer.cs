using UnityEngine;
using TMPro;

public class RaceTimer : MonoBehaviour
{
    public static RaceTimer instance;

    public TMP_Text timerText;

    private float elapsedTime = 0f;
    private bool isRunning = false;

    public float ElapsedTime => elapsedTime;

    private void Awake()
    {
        instance = this;
    }

    void Start()
    {
        UpdateTimerText();
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
        timerText.text = elapsedTime.ToString("F3");
    }
}