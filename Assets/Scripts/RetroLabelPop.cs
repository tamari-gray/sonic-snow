using UnityEngine;
using TMPro;

/// <summary>
/// Drives a floating label's pop-in/float-up/fade-out, then destroys its own GameObject.
/// Spawned by RetroBurstEffect, which owns the timing constant and per-effect scale.
///
/// Translates the design's textPop CSS keyframe directly: pop in with a slight overshoot
/// through the first 30% of the lifetime, hold at full size/opacity through 70%, then fade
/// out over the last 30% while still drifting upward. Progress is quantized into
/// <see cref="stepCount"/> discrete jumps before driving any of that — the reference
/// animates with steps(8, end), not a smooth per-frame ease, matching this project's other
/// pixel-art motion (StepBob, DistanceLabelScaler's quantizeSteps).
/// </summary>
public class RetroLabelPop : MonoBehaviour
{
    private TMP_Text label;
    private TMP_Text shadow;
    private float duration;
    private int stepCount;
    private float elapsed;
    private Vector3 startLocalPosition;
    private float riseDistance;

    public void Init(TMP_Text target, TMP_Text shadowTarget, float lifetimeSeconds, float rise = 0.35f, int steps = 8)
    {
        label = target;
        shadow = shadowTarget;
        duration = Mathf.Max(lifetimeSeconds, 0.01f);
        stepCount = Mathf.Max(steps, 1);
        riseDistance = rise;
        startLocalPosition = transform.localPosition;
    }

    private void Update()
    {
        elapsed += Time.deltaTime;
        float rawT = Mathf.Clamp01(elapsed / duration);

        // steps(n, end): hold each step's starting value until its boundary, then jump.
        float t = rawT >= 1f ? 1f : Mathf.Floor(rawT * stepCount) / stepCount;

        float scale;
        float alpha;
        float riseT;

        if (t < 0.3f)
        {
            float p = t / 0.3f;
            scale = Mathf.Lerp(0.5f, 1.1f, p);
            alpha = p;
            riseT = Mathf.Lerp(0f, 0.4f, p);
        }
        else if (t < 0.7f)
        {
            float p = (t - 0.3f) / 0.4f;
            scale = Mathf.Lerp(1.1f, 1f, p);
            alpha = 1f;
            riseT = Mathf.Lerp(0.4f, 0.7f, p);
        }
        else
        {
            float p = (t - 0.7f) / 0.3f;
            scale = 1f;
            alpha = 1f - p;
            riseT = Mathf.Lerp(0.7f, 1f, p);
        }

        transform.localPosition = startLocalPosition + Vector3.up * (riseT * riseDistance);
        transform.localScale = Vector3.one * scale;

        SetAlpha(label, alpha);
        SetAlpha(shadow, alpha);

        if (rawT >= 1f) Destroy(gameObject);
    }

    private static void SetAlpha(TMP_Text text, float alpha)
    {
        if (text == null) return;
        Color c = text.color;
        c.a = alpha;
        text.color = c;
    }
}
