using UnityEngine;

/// <summary>
/// The checkpoint-collect burst: blue/gold/cyan palette, "+1 CHECKPOINT" label. See
/// RetroBurstEffect for the actual particle/label construction — this is just its
/// checkpoint-specific palette.
/// </summary>
public static class CheckpointCollectEffect
{
    private static readonly RetroBurstEffect.Config Config = new RetroBurstEffect.Config
    {
        BurstColours = new[] { Hex("2f6fe0"), Hex("ffd400"), Hex("7cf0ff"), Color.white },
        RingColour = Hex("2f6fe0"),
        LabelText = "+1 CHECKPOINT",
        LabelColour = Hex("ffd400"),
        LabelShadowColour = Hex("0c2f8a"),
        Scale = 1f,
        EffectName = "CheckpointCollectEffect",
    };

    /// <summary>See RetroBurstEffect.Play — pass GeoAnchor.Root and the dome's own
    /// localPosition, so the effect tracks alignment corrections the way the dome does.</summary>
    public static void Play(Transform parent, Vector3 localPosition) =>
        RetroBurstEffect.Play(parent, localPosition, Config);

    private static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString("#" + hex, out Color c);
        return c;
    }
}
