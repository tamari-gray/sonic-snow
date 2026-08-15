using UnityEngine;

/// <summary>
/// Builds a soft radial "blob" shadow decal — a flat quad with a fading dark centre,
/// dropped at an object's base. See ContactShadow.shader for why this exists: XREAL's
/// glasses can't do true environment occlusion, so this is a placement/grounding cue
/// standing in for it, not a real depth effect.
/// </summary>
public static class GroundContactShadow
{
    private const string ShaderName = "SonicSnow/ContactShadow";

    private static Material sharedMaterial;

    /// <summary>Creates the shadow as a child of <paramref name="parent"/>, sitting at its
    /// local origin with a tiny lift to avoid z-fighting with the ground underneath it.</summary>
    public static GameObject Create(Transform parent, float radius)
    {
        Material material = GetMaterial();
        if (material == null) return null;

        GameObject go = new GameObject("ContactShadow");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(0f, 0.01f, 0f);
        go.transform.localRotation = Quaternion.identity;

        go.AddComponent<MeshFilter>().sharedMesh = BuildQuad(radius);

        MeshRenderer renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        return go;
    }

    private static Material GetMaterial()
    {
        if (sharedMaterial != null) return sharedMaterial;

        Shader shader = Shader.Find(ShaderName);
        if (shader == null)
        {
            Debug.LogError($"[GroundContactShadow] Shader '{ShaderName}' not found — is it in " +
                            "Always Included Shaders (Project Settings > Graphics)?");
            return null;
        }

        sharedMaterial = new Material(shader) { name = "ContactShadow (runtime)" };
        return sharedMaterial;
    }

    /// <summary>Flat quad in the object's local XZ plane, centred on the origin.</summary>
    private static Mesh BuildQuad(float radius)
    {
        Mesh mesh = new Mesh { name = "ContactShadowQuad" };

        mesh.vertices = new[]
        {
            new Vector3(-radius, 0f, -radius),
            new Vector3(radius, 0f, -radius),
            new Vector3(radius, 0f, radius),
            new Vector3(-radius, 0f, radius),
        };
        mesh.uv = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(1f, 1f),
            new Vector2(0f, 1f),
        };
        // Cull is off in the shader, so winding doesn't matter for visibility.
        mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };

        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        return mesh;
    }
}
