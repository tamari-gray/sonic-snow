// Checkpoint dome, early-90s console styling.
//
// Two jobs at once. The first is the same seam fix as before: the sphere crosses the AR
// floor, AROcclusionManager carves that intersection against a jittering depth estimate,
// and the waterline saws. The second is the look — nothing here ramps smoothly. The rim is
// quantized into discrete bands and the waist breaks up into coarse checker cells, so the
// dome dissolves into chunky squares rather than fading out.
//
// The checker is sampled in OBJECT space (azimuth + height), not screen space. The brief
// allows either and prefers object space, and on this project it's the only workable
// choice: the XREAL build renders stereo, and a screen-space pattern would give each eye a
// different mask on the same surface, which fights stereo fusion and reads as shimmer
// rather than as a pattern painted on the dome. Object space also rotates with the mesh for
// free and needs no screen-centre uniform.
//
// Stereo macros are load-bearing — single-pass instanced.
Shader "SonicSnow/DomeRetroChecker"
{
    Properties
    {
        // Deep blue centre. Additive, so a dark core reads as see-through — the checkpoint
        // sits on the racing line and the rider has to see the track through it.
        [HDR] _CoreColor ("Core colour", Color) = (0, 0.12, 0.38, 1)
        [HDR] _RimColor ("Rim colour", Color) = (0.55, 0.92, 1, 1)

        _Intensity ("Intensity", Range(0, 8)) = 1.8
        _RimPower ("Rim power", Range(0, 8)) = 2.5

        // Height (normalised object space, equator = 0) where the checker break-up starts.
        // Solid above, fully dissolved by _CheckerStart - _CheckerBand.
        //
        // Defaults to 0.60 rather than the brief's 0.15 because of this prefab's geometry:
        // the spawner overwrites the root transform, putting the ground plane at root-local
        // y = 0 with the sphere centre 0.76 below it and a radius of 4 — so the real
        // waterline is at normalised height +0.19, ABOVE the equator. Starting the break-up
        // at 0.15 would leave the dome solid across the entire waterline, which is the
        // artefact this shader exists to remove. 0.60 - 0.40 lands the full dissolve at
        // 0.20, right on the waterline. Retune both if the dome is sunk deeper or prouder.
        _CheckerStart ("Checker start height", Range(-1, 1)) = 0.6
        _CheckerBand ("Checker dissolve band", Range(0.05, 1)) = 0.4

        // Checker cell density. Named _CellPx to match the brief; in object-space sampling
        // it sets cells per half-turn (so 6 = 12 around the dome) rather than literal pixels.
        _CellPx ("Checker cell density", Range(2, 32)) = 6

        _SpinSpeed ("Spin speed (rad/s)", Float) = 0.3
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

            #define TWO_PI 6.2831853

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 positionOS : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _CoreColor;
                float4 _RimColor;
                float  _Intensity;
                float  _RimPower;
                float  _CheckerStart;
                float  _CheckerBand;
                float  _CellPx;
                float  _SpinSpeed;
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
                output.positionOS = input.positionOS.xyz;

                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // Unity's sphere is radius 0.5 in object space: -1 south pole, 0 equator, 1 north.
                float height = input.positionOS.y * 2.0;

                // --- stepped rim -------------------------------------------------------
                float3 viewDir = normalize(GetWorldSpaceViewDir(input.positionWS));
                float fresnel = pow(1.0 - saturate(dot(viewDir, normalize(input.normalWS))), _RimPower);

                // The core retro move: 5 discrete bands, no gradient.
                float banded = floor(saturate(fresnel) * 5.0) / 5.0;
                float3 colour = lerp(_CoreColor.rgb, _RimColor.rgb, banded);

                // --- rotating checker --------------------------------------------------
                float azimuth = atan2(input.positionOS.z, input.positionOS.x) + _Time.y * _SpinSpeed;

                float cellsAround = max(_CellPx, 1.0) * 2.0;
                float2 cell;
                cell.x = (azimuth / TWO_PI) * cellsAround;
                cell.y = height * cellsAround * 0.5;

                float checker = fmod(floor(cell.x) + floor(cell.y), 2.0);

                // How much the checker applies: 0 above _CheckerStart (solid dome), 1 once
                // fully into the band. Stepped, so even this blend arrives in tiers.
                float into = saturate((_CheckerStart - height) / max(_CheckerBand, 1e-4));
                into = floor(into * 4.0) / 4.0;

                // Below the band the dome is gone entirely — the checker thins it out on
                // the way down, so there is no solid silhouette left at the waterline.
                float dissolve = step(height, _CheckerStart - _CheckerBand) ;
                float mask = lerp(1.0, checker, into) * (1.0 - dissolve);

                float3 emissive = colour * _Intensity * mask;

                // Additive (One One) ignores alpha; the mask is already in the colour.
                return half4(emissive, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
