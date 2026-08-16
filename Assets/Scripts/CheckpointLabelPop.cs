using UnityEngine;
using TMPro;

/// <summary>
/// Drives the "+1 CHECKPOINT" label's pop-in/float-up/fade-out, then destroys its own
/// GameObject. Spawned by CheckpointCollectEffect, which owns the timing constant.
///
/// Translates the design's textPop CSS keyframe directly: pop in with a slight overshoot
/// through the first 30% of the lifetime, hold at full size/opacity through 70%, then fade
/// out over the last 30% while still drifting upward.
/// </summary>
public class CheckpointLabelPop : MonoBehaviour
{
    private TMP_Text label;
    private float duration;
    private float elapsed;
    private Vector3 startLocalPosition;

    private const float RiseDistance = 0.35f;

    public void Init(TMP_Text target, float lifetimeSeconds)
    {
        label = target;
        duration = Mathf.Max(lifetimeSeconds, 0.01f);
        startLocalPosition = transform.localPosition;
    }

    private void Update()
    {
        elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(elapsed / duration);

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

        transform.localPosition = startLocalPosition + Vector3.up * (riseT * RiseDistance);
        transform.localScale = Vector3.one * scale;

        if (label != null)
        {
            Color c = label.color;
            c.a = alpha;
            label.color = c;
        }

        if (t >= 1f) Destroy(gameObject);
    }
}
