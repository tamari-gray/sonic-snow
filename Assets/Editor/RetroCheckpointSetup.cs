using UnityEngine;
using UnityEditor;

/// <summary>
/// Restyles the checkpoint marker to the early-90s console look: stepped rim shading, a
/// spinning checker break-up at the dome's waist, a rotating checker glow pad on the floor,
/// and dithered haze. Creates the materials and rewires the prefab.
///
/// Batch-mode entry point so it can be driven headlessly — the Editor GUI holds the project
/// lock and doesn't reliably import externally-written files while unfocused.
///
/// Safe to re-run: materials are updated in place and the prefab children are matched by name.
/// </summary>
public static class RetroCheckpointSetup
{
    const string PrefabPath = "Assets/Prefabs/CheckpointDomeRoot.prefab";

    // The checkpoint label's own TMP preset. Verified used by nothing else in the project,
    // so adding a drop shadow here can't leak onto the other HUD screens the way editing the
    // font asset's default material would.
    const string LabelMaterialPath = "Assets/SonicHUD Text - Blue.mat";

    public static void Run()
    {
        AssetDatabase.Refresh();

        if (!VerifyShaders()) return;

        var dome = BuildDomeMaterial();
        var pad = BuildPadMaterial();
        var hazeA = BuildHazeMaterial("Assets/Materials/CheckpointHazeDitherA.mat",
                                      new Color(0f, 0.42f, 0.95f, 1f), 0.5f, 7.5f, 0.06f);
        var hazeB = BuildHazeMaterial("Assets/Materials/CheckpointHazeDitherB.mat",
                                      new Color(0f, 0.36f, 0.90f, 1f), 0.4f, 10f, 0.05f);

        ApplyLabelDropShadow();
        RewirePrefab(dome, pad, hazeA, hazeB);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[RetroCheckpointSetup] Done.");
    }

    static bool VerifyShaders()
    {
        string[] paths =
        {
            "Assets/Shaders/DomeRetroChecker.shader",
            "Assets/Shaders/GroundCheckerPad.shader",
            "Assets/Shaders/HazeDither.shader",
        };

        bool ok = true;
        foreach (var path in paths)
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
            if (shader == null)
            {
                Debug.LogError($"[RetroCheckpointSetup] Missing shader: {path}");
                ok = false;
            }
            else if (ShaderUtil.ShaderHasError(shader))
            {
                Debug.LogError($"[RetroCheckpointSetup] Shader has compile errors: {shader.name}");
                ok = false;
            }
            else
            {
                Debug.Log($"[RetroCheckpointSetup] Shader OK: {shader.name}");
            }
        }

        return ok;
    }

    static Material BuildDomeMaterial()
    {
        var mat = Make("SonicSnow/DomeRetroChecker", "Assets/Materials/DomeRetroChecker.mat");
        if (mat == null) return null;

        // Dark core reads as see-through under additive blending — the rider has to be able
        // to look through the checkpoint at the track.
        mat.SetColor("_CoreColor", new Color(0f, 0.12f, 0.38f, 1f));
        mat.SetColor("_RimColor", new Color(0.55f, 0.92f, 1f, 1f));
        mat.SetFloat("_Intensity", 1.8f);
        mat.SetFloat("_RimPower", 2.5f);
        mat.SetFloat("_CheckerStart", 0.6f);   // full dissolve lands at 0.20 = the waterline
        mat.SetFloat("_CheckerBand", 0.4f);
        mat.SetFloat("_CellPx", 6f);
        mat.SetFloat("_SpinSpeed", 0.3f);
        return mat;
    }

    static Material BuildPadMaterial()
    {
        var mat = Make("SonicSnow/GroundCheckerPad", "Assets/Materials/CheckpointCheckerPad.mat");
        if (mat == null) return null;

        mat.SetColor("_Color", new Color(0f, 0.5071269f, 1f, 1f));
        mat.SetFloat("_Intensity", 1.5f);
        mat.SetFloat("_Radius", 0.85f);
        mat.SetFloat("_Rings", 5f);
        mat.SetFloat("_WedgeCount", 8f);
        mat.SetFloat("_WedgeDim", 0.25f);
        mat.SetFloat("_SpinSpeed", 0.3f);      // matches the dome, so they spin together
        mat.SetFloat("_PulseAmount", 0.12f);
        mat.SetFloat("_ZTest", 8f);            // Always — AR depth must not chop the light
        return mat;
    }

    static Material BuildHazeMaterial(string path, Color colour, float intensity,
                                      float driftPeriod, float driftAmount)
    {
        var mat = Make("SonicSnow/HazeDither", path);
        if (mat == null) return null;

        mat.SetColor("_Color", colour);
        mat.SetFloat("_Intensity", intensity);
        mat.SetFloat("_Radius", 0.9f);
        mat.SetFloat("_DriftPeriod", driftPeriod);
        mat.SetFloat("_DriftAmount", driftAmount);
        mat.SetFloat("_DriftSteps", 16f);
        mat.SetFloat("_ZTest", 8f);
        return mat;
    }

    /// <summary>
    /// Hard TMP drop shadow: solid deep blue, offset down-right, zero softness so it reads
    /// as a stamped sprite shadow rather than a blur.
    /// </summary>
    static void ApplyLabelDropShadow()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(LabelMaterialPath);
        if (mat == null)
        {
            Debug.LogWarning($"[RetroCheckpointSetup] Label material not found at {LabelMaterialPath} — skipping drop shadow.");
            return;
        }

        mat.EnableKeyword("UNDERLAY_ON");
        mat.SetColor("_UnderlayColor", new Color(0f, 0.08f, 0.30f, 1f));
        mat.SetFloat("_UnderlayOffsetX", 0.06f);
        mat.SetFloat("_UnderlayOffsetY", -0.06f);
        mat.SetFloat("_UnderlaySoftness", 0f);
        mat.SetFloat("_UnderlayDilate", 0.1f);
        EditorUtility.SetDirty(mat);

        Debug.Log("[RetroCheckpointSetup] Hard drop shadow enabled on the checkpoint label preset.");
    }

    static void RewirePrefab(Material dome, Material pad, Material hazeA, Material hazeB)
    {
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        if (root == null)
        {
            Debug.LogError($"[RetroCheckpointSetup] Could not load {PrefabPath}");
            return;
        }

        try
        {
            Assign(root.transform, "CheckpointSphere", dome);
            Assign(root.transform, "BaseGlow", pad);
            Assign(root.transform, "HazeBandA", hazeA);
            Assign(root.transform, "HazeBandB", hazeB);

            // The pass-through trigger must survive untouched; log it so that's on record.
            var collider = root.transform.Find("CheckpointSphere")?.GetComponent<SphereCollider>();
            Debug.Log(collider != null
                ? $"[RetroCheckpointSetup] SphereCollider intact: radius={collider.radius} center={collider.center} isTrigger={collider.isTrigger}"
                : "[RetroCheckpointSetup] WARNING: no SphereCollider found.");

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log($"[RetroCheckpointSetup] Saved {PrefabPath}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static void Assign(Transform parent, string childName, Material mat)
    {
        var child = parent.Find(childName);
        if (child == null)
        {
            Debug.LogError($"[RetroCheckpointSetup] Child '{childName}' not found — leaving the prefab alone.");
            return;
        }

        var renderer = child.GetComponent<MeshRenderer>();
        if (renderer == null || mat == null) return;

        renderer.sharedMaterial = mat;

        // Re-assert: additive glow shouldn't cast shadows or sample probes.
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

        Debug.Log($"[RetroCheckpointSetup] {childName} -> {mat.name}");
    }

    static Material Make(string shaderName, string path)
    {
        var shader = Shader.Find(shaderName);
        if (shader == null)
        {
            Debug.LogError($"[RetroCheckpointSetup] Shader not found: {shaderName}");
            return null;
        }

        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null)
        {
            existing.shader = shader;
            return existing;
        }

        var mat = new Material(shader);
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }
}
