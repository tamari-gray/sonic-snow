// Checkpoint dome: a translucent body with a solid outline around its silhouette.
//
// The outline is a fresnel rim rather than a second inverted-hull mesh. On a sphere the
// surface turns away from you exactly at the silhouette, so shading by the angle between
// the normal and the view direction outlines whatever you can currently see — from any
// angle, with no extra geometry and no per-frame work on the CPU.
//
// Unlit on purpose, same reasoning as the finish beam: this reads as a glowing marker,
// and letting the scene's directional light darken one side of it works against that.
Shader "SonicSnow/DomeGlow"
{
    Properties
    {
        _BaseColor    ("Body colour",    Color) = (0, 0.5071269, 1, 0.5411765)
        [HDR] _OutlineColor ("Outline colour", Color) = (0, 0.5071269, 1, 1)

        // Fraction of the silhouette taken up by the outline. Larger = thicker band.
        _OutlineWidth ("Outline width", Range(0.01, 1)) = 0.35

        // Bias toward the rim. Higher values keep the outline tight to the edge instead
        // of bleeding across the face of the dome.
        _OutlineFalloff ("Outline falloff", Range(0.5, 8)) = 2
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
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

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
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // Must match the Properties block exactly or the SRP Batcher drops this material.
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _OutlineColor;
                float  _OutlineWidth;
                float  _OutlineFalloff;
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

                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float3 normal = normalize(input.normalWS);
                float3 view   = normalize(GetWorldSpaceViewDir(input.positionWS));

                // 0 facing the camera, 1 at the silhouette.
                float rim = 1.0 - saturate(dot(normal, view));
                rim = pow(rim, _OutlineFalloff);

                // smoothstep rather than a hard cut, so the edge stays clean under the
                // heavy aliasing you get from a thin band on a phone screen.
                float outline = smoothstep(1.0 - _OutlineWidth, 1.0, rim);

                half3 colour = lerp(_BaseColor.rgb, _OutlineColor.rgb, outline);
                half  alpha  = lerp(_BaseColor.a,   _OutlineColor.a,   outline);

                return half4(colour, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
