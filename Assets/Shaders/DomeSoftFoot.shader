// Additive shell for the checkpoint dome, with a dissolving waist.
//
// Same problem as the finish beam: the sphere is solid where it crosses the floor, and AR
// Foundation's AROcclusionManager carves that intersection against an *estimated* depth
// that jitters frame to frame, so the waterline saws. The fix is to leave no opaque pixels
// there for depth to clip — the shell fades out through the waist before it reaches the
// ground line.
//
// The rim term deliberately drives most of the emissive, so the dome is bright at its
// silhouette and near-transparent through the middle. The checkpoint sits on the racing
// line: the rider has to be able to see the track through it.
//
// Stereo macros are load-bearing — the XREAL build renders single-pass instanced.
Shader "SonicSnow/DomeSoftFoot"
{
    Properties
    {
        // Seeded from Dome.mat's existing blue. HDR intensity takes the rim toward
        // near-white without shifting the hue.
        [HDR] _Color ("Colour", Color) = (0, 0.7355238, 1, 1)
        _Intensity ("Intensity", Range(0, 8)) = 1.8

        // Length of the dissolve, in normalised object units (sphere spans -1..1).
        _FadeHeight ("Waist fade length", Range(0.01, 1)) = 0.35

        // Normalised object height at which the shell is fully gone. Defaults to 0.2
        // because this prefab's sphere sits 0.76 below the ground plane with a radius of
        // 4, putting the real waterline at +0.19 — slightly ABOVE the equator. Anchoring
        // the dissolve at the equator (height 0) would leave the shell at full alpha right
        // where it crosses the floor, which is the artefact this shader exists to remove.
        // Raise it if the dome is sunk deeper, lower it if it sits prouder.
        _WaistHeight ("Waist height (fully faded)", Range(-1, 1)) = 0.2

        _RimPower ("Rim power", Range(0, 8)) = 2.5

        _NoiseTex ("Noise (grayscale)", 2D) = "white" {}
        _ScrollSpeed ("Noise scroll speed", Float) = 0.05
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
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                float  height     : TEXCOORD3;  // normalised object height, -1..1
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_NoiseTex);
            SAMPLER(sampler_NoiseTex);

            // Must match the Properties block exactly or the SRP Batcher drops this material.
            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _NoiseTex_ST;
                float  _Intensity;
                float  _FadeHeight;
                float  _WaistHeight;
                float  _RimPower;
                float  _ScrollSpeed;
            CBUFFER_END

            Varyings Vertex(Attributes input)
            {
                Varyings output;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs   normals   = GetVertexNormalInputs(input.normalOS);

                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.normalWS   = normals.normalWS;
                output.uv         = TRANSFORM_TEX(input.uv, _NoiseTex);

                // Unity's sphere is radius 0.5 in object space, so this maps to -1 at the
                // south pole, 0 at the equator, 1 at the north pole.
                output.height = input.positionOS.y * 2.0;

                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // Gone at _WaistHeight, full by _FadeHeight above it.
                float waist = smoothstep(_WaistHeight, _WaistHeight + _FadeHeight, input.height);

                // Mild, deliberately not exposed: just enough to stop the pole reading as a
                // hard-shaded ball, without punching a hole in the top of the dome.
                float pole = lerp(1.0, 0.55, smoothstep(0.75, 1.0, input.height));

                float3 viewDir = normalize(GetWorldSpaceViewDir(input.positionWS));
                float  rim = pow(1.0 - saturate(dot(viewDir, normalize(input.normalWS))), _RimPower);

                // Texture, not a light show: never removes more than 12%.
                float2 noiseUV = input.uv;
                noiseUV.y -= _Time.y * _ScrollSpeed;
                float noise = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, noiseUV).r;
                float grain = lerp(0.88, 1.0, noise);

                float3 emissive = _Color.rgb * _Intensity * rim * grain * waist * pole;

                // Additive (One One) ignores alpha; the falloffs are already in the colour.
                return half4(emissive, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
