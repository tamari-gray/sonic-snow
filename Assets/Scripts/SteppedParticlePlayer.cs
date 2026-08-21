using UnityEngine;

/// <summary>
/// Drives a ParticleSystem by scrubbing to a quantized simulated time each frame, instead of
/// letting it advance continuously via Play(). Every module keyed off simulated time —
/// position, size-over-lifetime, colour-over-lifetime — gets re-evaluated at the same held
/// time value, so the whole system jumps between a fixed number of discrete frames. This is
/// what actually reproduces the reference designs' CSS `steps(n, end)` keyframes (see
/// RetroBurstEffect) — Unity's native Play() advances continuously, which is not the same
/// animation. Same idea as StepBob/DistanceLabelScaler's quantizeSteps, applied to a
/// ParticleSystem instead of a transform.
///
/// The system must not also be Play()ing — Simulate() and the normal per-frame Update both
/// advance the same internal state, and running both at once double-advances it.
/// </summary>
internal class SteppedParticlePlayer : MonoBehaviour
{
    private ParticleSystem system;
    private float duration;
    private int steps;
    private float elapsed;
    private float lastSteppedT = -1f;

    public void Init(ParticleSystem targetSystem, float durationSeconds, int stepCount)
    {
        system = targetSystem;
        duration = Mathf.Max(durationSeconds, 0.01f);
        steps = Mathf.Max(stepCount, 1);
        lastSteppedT = -1f;
    }

    private void Update()
    {
        elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(elapsed / duration);

        // steps(n, end): hold each step's starting value until its boundary, then jump —
        // quantize DOWN to the current step, not round to the nearest one.
        float steppedT = t >= 1f ? 1f : Mathf.Floor(t * steps) / steps;

        // Only re-scrub on a step boundary. Simulate() restarts the system and re-runs it from
        // zero in fixed 0.02s sub-steps, so calling it every frame re-simulates the same held
        // time over and over — at 60fps that is ~10x the work for a bit-identical result, and it
        // gets worse the further into the effect you are. Skipping is not an approximation: by
        // construction steppedT does not change between boundaries.
        // (Lifetime is not this component's business — RetroBurstEffect destroys the whole root
        // on a timer, so there is nothing to tear down here once the last step lands.)
        if (steppedT == lastSteppedT) return;

        lastSteppedT = steppedT;
        system.Simulate(steppedT * duration);
    }
}
