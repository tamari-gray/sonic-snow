// Finish-line pillar. Matches the reference "Finish Line Trigger Sequence" design's
// PILLAR_FRAG exactly: flat unlit orange body, no fresnel/rim term, a vertical fade top and
// bottom in absolute world-space metres, and a checker break-up confined to the base —
// sampled in SCREEN space, not object space, so it drifts very slightly as the camera
// moves. That drift is intentional; it is not a bug to "fix" into a stable object-space
// pattern.
//
// The fade constants (0.1 / 0.9 / 2.6 / 1.2) are absolute metres, matching the reference
// literally — they are only correct paired with the reference's own geometry: a 0.55-radius,
// 3.6-tall cylinder with its BASE at local Y=0 (see LightPillar's transform in
// FinishLine.prefab, fixed to match via XREALPortBatch.FixPillarGeometry). Rescaling the
// mesh instead of the shader is deliberate: the shader is meant to be a literal port of the
// reference, not a version tuned to whatever the mesh happens to be.
//
// Single-sided (Cull Back): rendering both cylinder walls with additive blending doubles
// the emissive colour and pushes the result toward yellow/white, which is not the
// reference look.
//
// Stereo macros are load-bearing — the XREAL build renders single-pass instanced.
Shader "SonicSnow/PillarRetroChecker"
{
    Properties
    {
        // The reference design's "(0.72, 0.34, 0.02)" turned out to be a value for a
        // shader stage that no longer matches this one 1:1 — the reference's own displayed
        // pixel, sampled from the live artifact, is #ff930a. That's an sRGB screen colour;
        // decoding it through the standard sRGB->linear curve gives the actual linear value
        // this shader should emit: ~(1.0, 0.292, 0.003). Its green/red ratio (0.29) is
        // markedly lower than the old value's (0.34/0.72=0.47) — that's the whole difference
        // between "blood orange" and "light orange". Intensity folded to 1 so this raw value
        // is exactly what lands on screen, with no multiplier to re-drift the ratio.
        [HDR] _CoreColor ("Core colour", Color) = (1.0, 0.292, 0.003, 1)
        _Intensity ("Intensity multiplier", Float) = 1

        _Body ("Body alpha", Range(0, 1)) = 0.88

        // Absolute world-space metres. Scaled up from the reference's literal 0.1/0.9/2.6/1.2
        // (tuned for a 3.6m pillar) by the same 5x the mesh itself was stretched by, to clear
        // FinishLineText (local Y=15.88) — see FixPillarGeometry's TargetHeight. Ratios to
        // pillar height are preserved exactly, so the silhouette (where the fade starts/ends
        // as a fraction of the pillar) is unchanged from the reference; only the absolute
        // scale grew. Only correct for a pillar whose base sits at local Y=0 and is 18m tall.
        _BaseFadeOffset ("Base fade offset (m)", Float) = 0.5
        _BaseFadeRange  ("Base fade range (m)", Float) = 4.5
        _TopFadeStart   ("Top fade start (m)", Float) = 13.0
        _TopFadeRange   ("Top fade range (m)", Float) = 6.0

        // World-space Y the pillar's own base actually spawned at. The fade band above is
        // tuned in local terms (base at 0, top at 18), but this shader reads world-space Y —
        // so on a route where the finish sits at a meaningfully different altitude than the
        // origin, the whole prefab spawns far from world Y=0 and the fade band (and thus the
        // whole pillar) silently discards everywhere. Set once from FinishLinePillar.SpawnPillar
        // right after placement; defaults to 0 so an unset material behaves exactly as before.
        _BaseWorldY ("Base world Y (m)", Float) = 0

        // Screen-space checker cell size in pixels. The reference's own canvas rendered at
        // a small, downscaled resolution, so its "~1px" reads as a chunky, clearly visible
        // square there — at full device resolution that would be nearly invisible, so this
        // defaults larger. Tune on device so the checker stays clearly visible.
        _CellPx ("Checker cell size (screen px)", Range(1, 64)) = 10
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "Queue"           = "Transparent"
            "RenderPipeline"  = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode" = "UniversalForward" }

            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // Must match the Properties block exactly or the SRP Batcher drops this material.
            CBUFFER_START(UnityPerMaterial)
                float4 _CoreColor;
                float  _Intensity;
                float  _Body;
                float  _BaseFadeOffset;
                float  _BaseFadeRange;
                float  _TopFadeStart;
                float  _TopFadeRange;
                float  _CellPx;
                float  _BaseWorldY;
            CBUFFER_END

            Varyings Vertex(Attributes input)
            {
                Varyings output;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;

                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // Flat multiply, no fresnel/rim term — the body is a single uniform colour
                // from every viewing angle, never brighter at the silhouette edge.
                float3 col = _CoreColor.rgb * _Intensity;

                float localY = input.positionWS.y - _BaseWorldY;
                float baseFade = saturate((localY + _BaseFadeOffset) / max(_BaseFadeRange, 1e-4));
                float topFade  = 1.0 - saturate((localY - _TopFadeStart) / max(_TopFadeRange, 1e-4));
                float fade = baseFade * topFade;
                float a = _Body * fade;

                // Screen-space checker — deliberately not object/world-space. See the header
                // comment: the slight camera-relative drift this produces is intentional.
                float2 q = floor(input.positionCS.xy / max(_CellPx, 1e-4));
                float checker = fmod(q.x + q.y, 2.0);

                // 1 right at the very bottom, 0 by the time baseFade reaches 0.85 — the
                // checker only ever shows up near the base, not spread across the pillar.
                float lowness = 1.0 - smoothstep(0.05, 0.85, baseFade);
                a *= lerp(1.0, checker * 0.85 + 0.08, lowness);

                // Quantize to 5 discrete steps — no smooth gradient anywhere on this shader.
                a = floor(a * 4.0 + 0.5) / 4.0;

                if (a <= 0.01) discard;

                return half4(col * a, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
