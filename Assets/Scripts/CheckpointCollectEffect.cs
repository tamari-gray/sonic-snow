using UnityEngine;
using TMPro;

/// <summary>
/// The checkpoint-collect burst: a radial pixel-particle scatter, an expanding ring of
/// pixels, a quick white flash, and a floating "+1 CHECKPOINT" label. Built to the
/// "Checkpoint Trigger Sequence" design (Claude Design project
/// a496b48d-6afc-420a-bd9b-ea77cf691c95).
///
/// Deliberately doesn't touch CheckpointDomeRoot's prefab or materials — this only adds a
/// short-lived effect alongside the existing dome and leaves the dome instance's own shrink
/// to the caller (see CheckpointDomeSpawner.Collect). Everything here is spawned fresh at
/// runtime and self-destructs, matching this project's other code-built VFX (see
/// GroundContactShadow) rather than being a hand-authored prefab.
///
/// The design's ground-pad shader pulse isn't reproduced — there's no equivalent ground
/// decal anywhere else in this project, and adding one would mean a second new shader for
/// a background accent rather than the effect itself. The burst, ring, flash and label are
/// the parts that read at a glance as "you collected something."
/// </summary>
public static class CheckpointCollectEffect
{
    // Palette straight from the design's own burst colours.
    private static readonly Color Blue  = Hex("2f6fe0");
    private static readonly Color Gold  = Hex("ffd400");
    private static readonly Color Cyan  = Hex("7cf0ff");
    private static readonly Color White = Color.white;
    private static readonly Color[] BurstColours = { Blue, Gold, Cyan, White };

    private const float FlashLifetime = 0.35f;  // matches the design's flashPop
    private const float RingLifetime  = 0.55f;  // matches ringExpand
    private const float BurstLifetime = 0.6f;   // matches flyOut
    private const float TextLifetime  = 0.9f;   // matches textPop

    private const string ShaderName = "SonicSnow/CheckpointParticle";

    private static Material particleMaterial;

    /// <summary>
    /// Plays the burst at <paramref name="localPosition"/> in <paramref name="parent"/>'s
    /// local space — pass GeoAnchor.Root as parent and the dome's own localPosition, so the
    /// effect tracks alignment corrections exactly the way the dome it's replacing did.
    /// Self-cleaning: everything it spawns destroys itself once its own animation ends.
    /// </summary>
    public static void Play(Transform parent, Vector3 localPosition)
    {
        Material material = GetMaterial();
        if (material == null) return; // logged in GetMaterial(); nothing sensible to spawn without it

        GameObject root = new GameObject("CheckpointCollectEffect");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = localPosition;

        BuildFlash(root.transform, material);
        BuildRing(root.transform, material);
        BuildBurst(root.transform, material);
        BuildLabel(root.transform);

        float maxLifetime = Mathf.Max(FlashLifetime, RingLifetime, BurstLifetime, TextLifetime);
        Object.Destroy(root, maxLifetime + 0.2f);
    }

    // --- flash: one bright particle that pops and fades -------------------------------

    private static void BuildFlash(Transform parent, Material material)
    {
        ParticleSystem ps = CreateSystem("Flash", parent, material, 1);

        var main = ps.main;
        main.duration = FlashLifetime;
        main.startLifetime = FlashLifetime;
        main.startSpeed = 0f;
        main.startSize = 0.4f;
        main.startColor = White;

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });

        var sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.8f));

        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        colorOverLifetime.color = FadeAlphaOnly();

        ps.gameObject.SetActive(true);
        ps.Play();
    }

    // --- ring: pixels emitted evenly around a circle, all moving straight outward ------

    private static void BuildRing(Transform parent, Material material)
    {
        const int count = 16;
        ParticleSystem ps = CreateSystem("Ring", parent, material, count);

        var main = ps.main;
        main.duration = RingLifetime;
        main.startLifetime = RingLifetime;
        main.startSpeed = 3.2f;
        main.startSize = 0.05f;
        main.startColor = Blue;
        main.gravityModifier = 0f;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.05f;
        shape.radiusThickness = 0f; // emit from the rim only, so it reads as a ring, not a disc
        shape.arc = 360f;

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });

        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        colorOverLifetime.color = FadeAlphaOnly();

        ps.gameObject.SetActive(true);
        ps.Play();
    }

    // --- burst: the confetti scatter, upward-biased with the design's four colours -----

    private static void BuildBurst(Transform parent, Material material)
    {
        const int count = 28;
        ParticleSystem ps = CreateSystem("Burst", parent, material, count);

        var main = ps.main;
        main.duration = BurstLifetime;
        main.startLifetime = BurstLifetime;
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.6f, 3.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.07f);
        main.gravityModifier = 0.4f; // arcs up then settles, rather than a flat scatter
        main.startColor = new ParticleSystem.MinMaxGradient(SteppedGradient(BurstColours))
        {
            mode = ParticleSystemGradientMode.RandomColor
        };

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Hemisphere; // biased upward, like the mockup's flyOut
        shape.radius = 0.05f;

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });

        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        colorOverLifetime.color = FadeAlphaOnly(); // multiplies against the random start colour above

        ps.gameObject.SetActive(true);
        ps.Play();
    }

    // --- label: "+1 CHECKPOINT", pops in, floats up, fades out -------------------------

    private static void BuildLabel(Transform parent)
    {
        GameObject go = new GameObject("CheckpointLabel");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(0f, 0.35f, 0f);

        TextMeshPro text = go.AddComponent<TextMeshPro>();
        text.text = "+1 CHECKPOINT";
        text.color = Gold;
        text.fontSize = 3.2f;
        text.alignment = TextAlignmentOptions.Center;
        text.outlineWidth = 0.15f;
        text.outlineColor = Hex("0c2f8a");

        // Reads correctly from whatever angle the rider actually approached the checkpoint
        // from — same reasoning as the checkpoint dome's own text, see Billboard.cs.
        go.AddComponent<Billboard>();

        go.AddComponent<CheckpointLabelPop>().Init(text, TextLifetime);
    }

    // --- shared setup ------------------------------------------------------------------

    private static ParticleSystem CreateSystem(string name, Transform parent, Material material, int maxParticles)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);

        // Inactive until fully configured: AddComponent on an already-active GameObject in
        // Play mode fires Awake immediately, and a fresh ParticleSystem defaults to
        // playOnAwake=true — without this, it starts playing (with defaults) before the
        // main/emission/shape/colour setup below ever runs, and every later attempt to
        // change its config logs "Setting X while system is still playing is not
        // supported." Each Build* method re-activates once its own setup is done.
        go.SetActive(false);

        ParticleSystem ps = go.AddComponent<ParticleSystem>();

        var main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.maxParticles = maxParticles;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;

        var emission = ps.emission;
        emission.enabled = true;

        ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        return ps;
    }

    private static Material GetMaterial()
    {
        if (particleMaterial != null) return particleMaterial;

        Shader shader = Shader.Find(ShaderName);
        if (shader == null)
        {
            Debug.LogError($"[CheckpointCollectEffect] Shader '{ShaderName}' not found — is it in " +
                            "Always Included Shaders (Project Settings > Graphics)?");
            return null;
        }

        particleMaterial = new Material(shader) { name = "CheckpointParticle (runtime)" };
        return particleMaterial;
    }

    /// <summary>White RGB with alpha ramping 1 to 0. Used as colorOverLifetime, which
    /// multiplies against a particle's own start colour — this fades it out without
    /// touching whatever hue it started with.</summary>
    private static Gradient FadeAlphaOnly()
    {
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.4f), new GradientAlphaKey(0f, 1f) });
        return gradient;
    }

    /// <summary>Hard-stepped colour bands, one per entry in <paramref name="colours"/>, each
    /// covering an equal share of the gradient. RandomColor mode samples a uniform random
    /// point along the gradient per particle, so this is how "pick one of N colours" gets
    /// built out of a gradient rather than a true discrete random choice.</summary>
    private static Gradient SteppedGradient(Color[] colours)
    {
        int n = colours.Length;
        GradientColorKey[] keys = new GradientColorKey[n * 2];

        for (int i = 0; i < n; i++)
        {
            float start = (float)i / n;
            float end = (float)(i + 1) / n - 0.001f;
            keys[i * 2] = new GradientColorKey(colours[i], start);
            keys[i * 2 + 1] = new GradientColorKey(colours[i], end);
        }

        Gradient gradient = new Gradient();
        gradient.SetKeys(keys, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        return gradient;
    }

    private static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString("#" + hex, out Color c);
        return c;
    }
}
