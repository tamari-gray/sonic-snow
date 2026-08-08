using TMPro;
using UnityEngine;

/// <summary>
/// Shows checkpoint progress in the corner of the screen, e.g. "1/3".
///
/// Reads <see cref="CheckpointDomeSpawner"/> rather than being pushed to, so it can't
/// drift out of sync with the domes and needs no wiring at the spawn site. Hidden until
/// a route with checkpoints is actually loaded, so a route with none shows nothing
/// instead of a permanent "0/0".
/// </summary>
public class CheckpointCounterUI : MonoBehaviour
{
    [SerializeField] private TMP_Text label;

    [Tooltip("Hide the counter entirely on a route that has no checkpoints.")]
    [SerializeField] private bool hideWhenRouteHasNone = true;

    private int shownCollected = -1;
    private int shownTotal = -1;

    private void Update()
    {
        if (label == null) return;

        CheckpointDomeSpawner spawner = CheckpointDomeSpawner.Instance;

        int total = spawner != null ? spawner.Total : 0;
        int collected = spawner != null ? spawner.CollectedCount : 0;

        // Assigning TMP_Text.text forces a mesh rebuild, so only touch it on a change
        // rather than every frame of the run.
        if (collected == shownCollected && total == shownTotal) return;

        shownCollected = collected;
        shownTotal = total;

        label.text = $"{collected}/{total}";

        // Disable the renderer, not the GameObject — this component lives on the same
        // object, and a disabled GameObject would stop running Update and never come back.
        label.enabled = !hideWhenRouteHasNone || total > 0;
    }
}
