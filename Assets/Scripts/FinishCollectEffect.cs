using UnityEngine;

/// <summary>
/// The finish-line trigger burst: orange/gold palette, "FINISH!" label, roughly double the
/// checkpoint burst's scale — matching the "Finish Line Trigger Sequence" design's own
/// larger pixel sizes and travel distances (Claude Design project
/// a496b48d-6afc-420a-bd9b-ea77cf691c95). See RetroBurstEffect for the actual particle/
/// label construction — this is just the finish-line-specific palette and scale.
/// </summary>
public static class FinishCollectEffect
{
    private static readonly RetroBurstEffect.Config Config = new RetroBurstEffect.Config
    {
        BurstColours = new[] { Hex("ff8a1e"), Hex("ffd400"), Hex("ffb15e"), Color.white },
        RingColour = Hex("ff8a1e"),
        LabelText = "FINISH!",
        LabelColour = Hex("ffd400"),
        LabelShadowColour = Hex("8a3d0c"),
        Scale = 2f,
        EffectName = "FinishCollectEffect",
    };

    /// <summary>See RetroBurstEffect.Play — pass GeoAnchor.Root and the finish pillar's own
    /// localPosition, so the effect tracks alignment corrections the way the pillar does.</summary>
    public static void Play(Transform parent, Vector3 localPosition) =>
        RetroBurstEffect.Play(parent, localPosition, Config);

    private static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString("#" + hex, out Color c);
        return c;
    }
}
