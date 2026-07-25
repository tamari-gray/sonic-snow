using System.Collections;
using UnityEngine;
using TMPro;

public class CountdownTimer : MonoBehaviour
{
    public static CountdownTimer instance;

    [Header("Countdown Text")]
    public TMP_Text countdownText;

    [Header("Timing")]
    [SerializeField] private float holdDuration = 0.6f;  // how long each number is fully visible
    [SerializeField] private float fadeDuration = 0.4f;  // how long the fade out takes

    private void Awake()
    {
        instance = this;
    }

    void Start()
    {
        countdownText.gameObject.SetActive(false);
    }

    public void StartCountdown(System.Action onComplete)
    {
        StartCoroutine(RunCountdown(onComplete));
    }

    private IEnumerator RunCountdown(System.Action onComplete)
    {

        countdownText.gameObject.SetActive(true);

        string[] steps = { "3", "2", "1", "GO!" };

        foreach (string step in steps)
        {
            countdownText.text = step;

            // Bigger font for numbers, slightly smaller for GO!
            countdownText.fontSize = step == "GO!" ? 120 : 160;

            // Fade in instantly
            SetAlpha(1f);

            // Hold
            yield return new WaitForSeconds(holdDuration);

            // Fade out
            float elapsed = 0f;
            while (elapsed < fadeDuration)
            {
                elapsed += Time.deltaTime;
                SetAlpha(1f - (elapsed / fadeDuration));
                yield return null;
            }

            SetAlpha(0f);
        }

        countdownText.gameObject.SetActive(false);

        // Fire the callback
        onComplete?.Invoke();
    }

    private void SetAlpha(float alpha)
    {
        Color c = countdownText.color;
        c.a = alpha;
        countdownText.color = c;
    }
}
