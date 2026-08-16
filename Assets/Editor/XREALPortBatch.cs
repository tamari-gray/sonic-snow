using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Batch-mode entry point for the mechanical parts of the XREAL canvas port
/// (<see cref="XREALCanvasConversion"/> and <see cref="ZzzLogSetup"/>), so they can be driven
/// from the command line instead of clicking each menu item in the GUI.
/// </summary>
public static class XREALPortBatch
{
    private const string ScenePath = "Assets/Scenes/Game.unity";

    public static void RunCanvasPort()
    {
        EditorSceneManager.OpenScene(ScenePath);

        XREALCanvasConversion.Convert();
        ZzzLogSetup.SetUp();

        bool saved = EditorSceneManager.SaveOpenScenes();
        Debug.Log($"[XREALPortBatch] Canvas port applied to {ScenePath}. Saved: {saved}");
    }

    /// <summary>
    /// One-off: applies the "Finish Line Trigger Sequence" design to FinishLine.prefab —
    /// swaps the pillar's material to the new checker-dissolve shader, and replaces the
    /// text's outline/glow with a duplicate-label hard shadow. Doesn't touch
    /// LightPillar.prefab (a nested prefab, overridden here rather than edited) or the
    /// shared font asset's own material (used by text all over the project — cloned into a
    /// dedicated asset instead of edited in place).
    /// </summary>
    public static void UpdateFinishLinePrefab()
    {
        const string PrefabPath = "Assets/Prefabs/FinishLine.prefab";
        const string CleanFaceMaterialPath = "Assets/Materials/RetroTextFace.mat";
        const string PillarMaterialPath = "Assets/Materials/PillarRetroChecker.mat";
        const string ShadowHex = "FF9309";

        Material cleanFace = AssetDatabase.LoadAssetAtPath<Material>(CleanFaceMaterialPath);
        if (cleanFace == null)
        {
            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                AssetDatabase.GUIDToAssetPath("380f1a141427a294684671969d6342cb"));

            if (font == null)
            {
                Debug.LogError("[XREALPortBatch] Shared font asset not found — aborting.");
                return;
            }

            // Cloned from the shared material rather than hand-authored: TMP SDF materials
            // carry many interdependent properties, and copying the real one guarantees
            // everything except outline/glow stays exactly as designed.
            cleanFace = new Material(font.material) { name = "RetroTextFace" };
            cleanFace.SetFloat("_OutlineWidth", 0f);
            cleanFace.SetFloat("_GlowOuter", 0f);
            AssetDatabase.CreateAsset(cleanFace, CleanFaceMaterialPath);
        }

        Material pillarMat = AssetDatabase.LoadAssetAtPath<Material>(PillarMaterialPath);
        if (pillarMat == null)
        {
            Debug.LogError($"[XREALPortBatch] {PillarMaterialPath} not found — aborting.");
            return;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);

        int pillarsUpdated = 0;
        foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (renderer.gameObject.name != "LightPillar") continue;
            renderer.sharedMaterial = pillarMat;
            pillarsUpdated++;
        }

        TextMeshPro face = null;
        foreach (TextMeshPro candidate in root.GetComponentsInChildren<TextMeshPro>(true))
        {
            if (candidate.gameObject.name != "FinishLineText") continue;
            // Skips a squashed (localScale.y ~0.04) legacy duplicate nested inside the
            // LightPillar sub-prefab — already invisible, not the one actually rendering.
            if (candidate.transform.localScale.y < 0.1f) continue;
            face = candidate;
            break;
        }

        if (face == null)
        {
            Debug.LogError("[XREALPortBatch] No active FinishLineText found.");
        }
        else
        {
            face.fontSharedMaterial = cleanFace;

            Transform existingShadow = face.transform.Find("FinishLineTextShadow");
            if (existingShadow != null) Object.DestroyImmediate(existingShadow.gameObject);

            GameObject shadowGo = Object.Instantiate(face.gameObject, face.transform);
            shadowGo.name = "FinishLineTextShadow";

            // The clone carries Billboard/StepBob too — remove both so the shadow doesn't
            // fight its own parent's rotation, and simply inherits it instead. FinishLineText
            // already bobs (StepBob) and billboards; a child with no components of its own
            // just rides along for free.
            Billboard billboard = shadowGo.GetComponent<Billboard>();
            if (billboard != null) Object.DestroyImmediate(billboard);
            StepBob bob = shadowGo.GetComponent<StepBob>();
            if (bob != null) Object.DestroyImmediate(bob);

            // A hard offset in the parent's own local X/Y, not a literal CSS pixel value —
            // FinishLineText is billboarded, so its local X/Y already track screen-right/
            // screen-down each frame, and this child inherits that rotation for free. The
            // small +Z pushes the shadow behind the face rather than z-fighting at an
            // identical depth (Billboard aims local +Z away from the viewer).
            shadowGo.transform.localPosition = new Vector3(0.18f, -0.18f, 0.02f);
            shadowGo.transform.localRotation = Quaternion.identity;
            shadowGo.transform.localScale = Vector3.one;

            TextMeshPro shadowText = shadowGo.GetComponent<TextMeshPro>();
            shadowText.fontSharedMaterial = cleanFace;
            shadowText.color = Hex(ShadowHex);
        }

        // Captured before UnloadPrefabContents destroys root (and face's GameObject with
        // it) — Unity's Object equality treats a destroyed reference as == null, so
        // checking face itself after this point always reads as "failed" even on success.
        bool faceUpdated = face != null;

        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        PrefabUtility.UnloadPrefabContents(root);

        Debug.Log($"[XREALPortBatch] Pillar renderers updated: {pillarsUpdated}. " +
                  $"Text face/shadow: {(faceUpdated ? "done" : "FAILED — see above")}.");
    }

    /// <summary>
    /// One-off: resizes the "LightPillar" mesh (Unity's built-in cylinder primitive — 2m
    /// tall, 1m diameter, centre-pivoted) to 0.55m radius x 18m height, with its BASE
    /// re-pivoted to local Y=0 rather than its centre. Height is 5x the reference's literal
    /// 3.6m: FinishLineText sits at local Y=15.88 in this prefab (a value the reference
    /// design doesn't address at all, since it has no equivalent text element at that
    /// height), and the pillar needs to read as taller than the text without moving the
    /// text itself. PillarRetroChecker's fade constants are scaled by the same 5x in lockstep
    /// (see the shader's own Properties comment) so the silhouette proportions match the
    /// reference exactly, just at 5x the absolute scale. Also resizes BaseGlow's quad to the
    /// reference's ~5.8m diameter (pillar radius x ~10) — unaffected by the height change.
    /// HazeBandA/B are left alone — already reasonably scaled, not flagged as wrong.
    /// </summary>
    public static void FixPillarGeometry()
    {
        const string PrefabPath = "Assets/Prefabs/FinishLine.prefab";
        const float TargetRadius = 0.55f;
        const float TargetHeight = 18f;
        const float GlowDiameter = 5.8f;

        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);

        Transform pillar = null;
        MeshRenderer pillarRenderer = null;
        foreach (MeshRenderer mr in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (mr.gameObject.name != "LightPillar") continue;
            pillar = mr.transform;
            pillarRenderer = mr;
            break;
        }

        if (pillar == null)
        {
            Debug.LogError("[XREALPortBatch] LightPillar mesh renderer not found — aborting.");
            PrefabUtility.UnloadPrefabContents(root);
            return;
        }

        Bounds before = pillarRenderer.bounds;

        // Unity's cylinder primitive: 1m diameter (0.5 radius), 2m tall, centred on its own
        // pivot. Scale to the target, then move the pivot up by half the new height so the
        // BASE — not the centre — sits at local Y=0, matching where the shader assumes
        // "ground" is (see PillarRetroChecker.shader's own header comment).
        float scaleXZ = TargetRadius / 0.5f;
        float scaleY = TargetHeight / 2f;

        pillar.localScale = new Vector3(scaleXZ, scaleY, scaleXZ);
        pillar.localPosition = new Vector3(pillar.localPosition.x, TargetHeight * 0.5f, pillar.localPosition.z);

        Bounds after = pillarRenderer.bounds;

        Transform glow = null;
        foreach (MeshRenderer mr in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (mr.gameObject.name != "BaseGlow") continue;
            glow = mr.transform;
            break;
        }

        if (glow != null)
        {
            glow.localScale = new Vector3(GlowDiameter, GlowDiameter, glow.localScale.z);
        }

        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        PrefabUtility.UnloadPrefabContents(root);

        Debug.Log($"[XREALPortBatch] LightPillar bounds before={before.size} (base y={before.min.y:F2}), " +
                  $"after={after.size} (base y={after.min.y:F2}). BaseGlow resized: {glow != null}.");
    }

    private static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString("#" + hex, out Color c);
        return c;
    }
}
