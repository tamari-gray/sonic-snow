using UnityEngine;
using UnityEditor;
using TMPro;

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

        FlattenLabelMaterial();
        ApplyLabelDropShadow();
        ApplyLabelBob();
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

    // Reuses the colour the material's old shader-underlay shadow used, so switching
    // technique doesn't also change how the shadow looks — same "keep the same colours"
    // rule the finish-line text follows (its own shadow is a tint on the SAME face
    // material, not a separate colour scheme).
    static readonly Color LabelShadowColour = new Color(0f, 0.08f, 0.30f, 1f);

    /// <summary>
    /// Strips the built-in outline/glow/underlay shader features off the checkpoint label's
    /// material — was previously carrying all three at once (outline width 0.21, glow, AND a
    /// shader underlay), which reads as "outline text", not the clean-face-plus-shadow look
    /// FinishLineText has. Underlay is redundant now anyway: <see cref="ApplyLabelDropShadow"/>
    /// replaces it with a real duplicate GameObject, matching how FinishLineText's own shadow
    /// works (FinishLineTextShadow is a child of FinishLineText, not a shader effect) and how
    /// RetroBurstEffect.BuildLabel already builds every other shadow label in this project.
    /// </summary>
    static void FlattenLabelMaterial()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(LabelMaterialPath);
        if (mat == null)
        {
            Debug.LogWarning($"[RetroCheckpointSetup] Label material not found at {LabelMaterialPath} — skipping.");
            return;
        }

        mat.SetFloat("_OutlineWidth", 0f);
        mat.DisableKeyword("GLOW_ON");
        mat.DisableKeyword("UNDERLAY_ON");
        EditorUtility.SetDirty(mat);

        Debug.Log("[RetroCheckpointSetup] Outline/glow/underlay stripped off the checkpoint label material — clean face only now.");
    }

    /// <summary>
    /// Builds "CheckPointTextShadow" as a child of CheckPointText — same technique as
    /// FinishLineText/FinishLineTextShadow and RetroBurstEffect.BuildLabel's shadow labels:
    /// a duplicate text object, same font/material, offset and tinted, rather than a shader
    /// effect. Offset uses the same fontSize * 0.15 convention RetroBurstEffect already
    /// established, so this reads as the same "kind" of shadow as the rest of the project's
    /// retro text, not a one-off.
    /// </summary>
    static void ApplyLabelDropShadow()
    {
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        if (root == null)
        {
            Debug.LogError($"[RetroCheckpointSetup] Could not load {PrefabPath}");
            return;
        }

        try
        {
            Transform label = FindDeep(root.transform, "CheckPointText");
            if (label == null)
            {
                Debug.LogError("[RetroCheckpointSetup] CheckPointText not found — skipping shadow.");
                return;
            }

            TMP_Text mainText = label.GetComponent<TMP_Text>();
            if (mainText == null)
            {
                Debug.LogError("[RetroCheckpointSetup] CheckPointText has no TMP_Text — skipping shadow.");
                return;
            }

            Transform existingShadow = label.Find("CheckPointTextShadow");
            GameObject shadowGo = existingShadow != null ? existingShadow.gameObject : new GameObject("CheckPointTextShadow");
            shadowGo.transform.SetParent(label, false);

            // Z needs to be big enough to unambiguously win transparent depth-sorting, not
            // just nudge the mesh — 0.02 here (borrowed from RetroBurstEffect's much smaller,
            // close-up VFX context) turned out to be noise-level once shrunk by this label's
            // own 0.26 localScale, on a marker viewed from up to 20m away. The sort then came
            // down to floating-point noise, which is why it read correctly in a static Editor
            // preview but flipped (shadow drawing in FRONT) on the actual glasses — confirmed
            // 2026-08-17. A Z offset on the same order as the X/Y offset removes the ambiguity.
            float offset = mainText.fontSize * 0.15f;
            shadowGo.transform.localPosition = new Vector3(offset, -offset, offset * 0.3f);

            TextMeshPro shadowText = shadowGo.GetComponent<TextMeshPro>();
            if (shadowText == null) shadowText = shadowGo.AddComponent<TextMeshPro>();

            shadowText.text = mainText.text;
            shadowText.font = mainText.font;
            shadowText.fontSharedMaterial = mainText.fontSharedMaterial;
            shadowText.fontSize = mainText.fontSize;
            shadowText.alignment = mainText.alignment;
            shadowText.color = LabelShadowColour;

            // Behind the main text in draw order, same as every other shadow-duplicate build
            // site in this project.
            shadowGo.transform.SetAsFirstSibling();

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log("[RetroCheckpointSetup] CheckPointTextShadow built/updated.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name) return root;
        foreach (Transform child in root)
        {
            var found = FindDeep(child, name);
            if (found != null) return found;
        }
        return null;
    }

    /// <summary>
    /// CheckPointText's animation is now the same StepBob 2-frame vertical bob
    /// FinishLineText uses (same offsetY/frameRate), not DistanceLabelScaler's
    /// distance-driven scale/lift/fade/bob system.
    ///
    /// History, in order: DistanceLabelScaler's farScale had drifted to 0.2 while the label
    /// itself is authored larger, making the runtime size never match the static Inspector
    /// preview; then the whole distance-scaling approach was found "too intense" and removed
    /// by request 2026-08-17; then explicitly replaced with StepBob "to function just like
    /// the finish line text animation" the same day. Don't reintroduce DistanceLabelScaler
    /// here without checking whether that request still stands.
    ///
    /// Only touches the StepBob component — never localPosition/localScale, both of which
    /// have been hand-tuned in the Editor since and must survive a re-run untouched.
    /// </summary>
    static void ApplyLabelBob()
    {
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        if (root == null)
        {
            Debug.LogError($"[RetroCheckpointSetup] Could not load {PrefabPath}");
            return;
        }

        try
        {
            Transform label = FindDeep(root.transform, "CheckPointText");
            if (label == null)
            {
                Debug.LogError("[RetroCheckpointSetup] CheckPointText not found — skipping bob setup.");
                return;
            }

            DistanceLabelScaler stale = label.GetComponent<DistanceLabelScaler>();
            if (stale != null) Object.DestroyImmediate(stale);

            StepBob bob = label.GetComponent<StepBob>();
            if (bob == null) bob = label.gameObject.AddComponent<StepBob>();

            SerializedObject so = new SerializedObject(bob);
            so.FindProperty("offsetY").floatValue = 0.05f;
            so.FindProperty("frameRate").floatValue = 1.67f;
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log("[RetroCheckpointSetup] CheckPointText now uses StepBob, matching FinishLineText.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
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
