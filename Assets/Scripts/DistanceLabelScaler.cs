using TMPro;
using UnityEngine;

/// <summary>
/// Scales, lifts and fades a marker's label based on how far the rider is from it, with
/// early-90s console styling: every value is quantized and the whole thing only updates a
/// handful of times a second, so the label steps between states like a low-framerate sprite
/// instead of easing between them. A small two-frame vertical bob runs on top.
///
/// The underlying behaviour is unchanged from the smooth version. A checkpoint label has two
/// competing jobs: at range it's the element that reads first, since the dome is only a faint
/// rim at 25 m. Up close it's in the way — the checkpoint sits on the racing line, and a label
/// that has ballooned to fill the view hides the track the rider is trying to see through. So
/// it's largest and brightest far out, then eases smaller, lifts clear of the growing dome,
/// and fades back as the rider closes.
///
/// Deliberately only touches local scale, local Y and TMP alpha — rotation belongs to
/// <see cref="Billboard"/>, which runs in its own LateUpdate and is left alone.
/// </summary>
[RequireComponent(typeof(TMP_Text))]
[DisallowMultipleComponent]
public class DistanceLabelScaler : MonoBehaviour
{
    [Header("Distance thresholds")]
    [Tooltip("At or beyond this, the label is at full opacity and its largest apparent size.")]
    [SerializeField] private float farDistance = 20f;

    [Tooltip("At or inside this, the label is faded and lifted clear of the dome.")]
    [SerializeField] private float nearDistance = 5f;

    [Header("Size")]
    [Tooltip("Local scale at or beyond farDistance. 0.2 is the prefab's authored size.")]
    [SerializeField] private float farScale = 0.2f;

    [Tooltip("Local scale at or inside nearDistance. Apparent size is scale/distance, so " +
             "0.042 at 5m reads slightly smaller than 0.2 at 20m — the label eases down as " +
             "the rider closes instead of ballooning.")]
    [SerializeField] private float nearScale = 0.042f;

    [Header("Placement and fade")]
    [Tooltip("Extra height above the label's authored position at nearDistance, so it clears " +
             "the dome silhouette as that grows in view.")]
    [SerializeField] private float nearLift = 2.5f;

    [Tooltip("Opacity at or inside nearDistance. Keeps the racing line visible through the " +
             "checkpoint rather than hiding it behind text.")]
    [Range(0f, 1f)]
    [SerializeField] private float nearAlpha = 0.4f;

    [Header("Retro stepping")]
    [Tooltip("Distance is quantized into this many levels, so size and fade jump between " +
             "discrete states instead of sliding.")]
    [Range(2, 32)]
    [SerializeField] private int quantizeSteps = 8;

    [Tooltip("How many times a second the label re-evaluates. Low on purpose — this is what " +
             "gives the movement its stepped, low-framerate feel.")]
    [Range(1f, 60f)]
    [SerializeField] private float updatesPerSecond = 10f;

    [Header("Two-frame bob")]
    [Tooltip("Seconds for a full bob cycle. The label alternates between two Y offsets on a " +
             "square wave, like a 2-frame sprite animation.")]
    [SerializeField] private float bobPeriod = 0.4f;

    [Tooltip("Height of the raised bob frame, in local units.")]
    [SerializeField] private float bobHeight = 0.05f;

    [Header("References")]
    [Tooltip("Camera to measure against. Defaults to Camera.main, which is the AR camera.")]
    [SerializeField] private Camera target;

    private TMP_Text label;
    private RectTransform rect;

    /// <summary>The label's authored height, which the lift and bob are added on top of.</summary>
    private float baseY;

    private float nextEvaluateTime;
    private float scale;
    private float lift;
    private float alpha;

    private void Awake()
    {
        label = GetComponent<TMP_Text>();
        rect = transform as RectTransform;

        baseY = rect != null ? rect.anchoredPosition.y : transform.localPosition.y;

        // Start settled at the far state rather than stepping in from zero on frame one.
        scale = farScale;
        lift = 0f;
        alpha = 1f;
    }

    private void LateUpdate()
    {
        if (target == null) target = Camera.main;
        if (target == null) return;

        // The distance-driven values only change a few times a second. The bob is applied
        // every frame below so its timing stays independent of this rate.
        if (Time.unscaledTime >= nextEvaluateTime)
        {
            nextEvaluateTime = Time.unscaledTime + 1f / Mathf.Max(updatesPerSecond, 1f);
            Evaluate();
        }

        // Two-frame square wave: down for the first half of the cycle, up for the second.
        float bob = Mathf.Repeat(Time.unscaledTime, Mathf.Max(bobPeriod, 1e-4f)) < bobPeriod * 0.5f
            ? 0f
            : bobHeight;

        transform.localScale = Vector3.one * scale;

        float y = baseY + lift + bob;
        if (rect != null)
            rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, y);
        else
            transform.localPosition = new Vector3(transform.localPosition.x, y, transform.localPosition.z);

        label.alpha = alpha;
    }

    private void Evaluate()
    {
        float distance = Vector3.Distance(transform.position, target.transform.position);

        // 0 at the near threshold, 1 at the far one — then quantized, so the label snaps
        // between a fixed set of states rather than tracking distance continuously.
        float t = Mathf.InverseLerp(nearDistance, farDistance, distance);
        int steps = Mathf.Max(quantizeSteps, 2);
        t = Mathf.Floor(t * steps) / steps;

        scale = Mathf.Lerp(nearScale, farScale, t);
        lift = Mathf.Lerp(nearLift, 0f, t);
        alpha = Mathf.Lerp(nearAlpha, 1f, t);
    }
}
