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

    public void Init(ParticleSystem targetSystem, float durationSeconds, int stepCount)
    {
        system = targetSystem;
        duration = Mathf.Max(durationSeconds, 0.01f);
        steps = Mathf.Max(stepCount, 1);
    }

    private void Update()
    {
        elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(elapsed / duration);

        // steps(n, end): hold each step's starting value until its boundary, then jump —
        // quantize DOWN to the current step, not round to the nearest one.
        float steppedT = t >= 1f ? 1f : Mathf.Floor(t * steps) / steps;

        system.Simulate(steppedT * duration);
    }
}
