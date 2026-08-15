using UnityEngine;

/// <summary>
/// Snaps a marker's label between two vertical positions on a fixed timer — a 2-frame
/// sprite bob, not a smooth bounce. No easing anywhere: the position jumps once per
/// half-period, matching the stepped, low-framerate feel used elsewhere on this project's
/// retro-styled markers (see CheckpointDomeRoot's checker dome/pad and DistanceLabelScaler's
/// own quantized motion).
///
/// Position-only: this only ever writes localPosition, so it coexists cleanly with a
/// Billboard component on the same object, which only ever writes rotation. Order between
/// them doesn't matter since neither reads what the other writes.
/// </summary>
[DisallowMultipleComponent]
public class StepBob : MonoBehaviour
{
    [Tooltip("World units between the two frames.")]
    [SerializeField] private float offsetY = 0.05f;

    [Tooltip("Frames per second. 2 frames per cycle, so 1.67 gives a ~1.2s full period " +
             "(~0.6s held per frame).")]
    [SerializeField] private float frameRate = 1.67f;

    private Vector3 basePos;

    private void Awake()
    {
        basePos = transform.localPosition;
    }

    private void Update()
    {
        int frame = Mathf.FloorToInt(Time.time * frameRate) % 2;
        transform.localPosition = basePos + Vector3.up * (frame == 1 ? offsetY : 0f);
    }
}
