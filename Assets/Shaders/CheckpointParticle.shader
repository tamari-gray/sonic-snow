// Additive, vertex-colour-driven particle quad. Used for the checkpoint-collect burst
// (see CheckpointCollectEffect.cs) — Unity's ParticleSystem feeds each particle's colour
// and fade-over-lifetime through vertex colour, so this only has to sample and output it.
//
// Additive rather than alpha blended to match the design's own glow-heavy palette (see the
// "Checkpoint Trigger Sequence" handoff) — a flat alpha-blended square reads as a sticker,
// additive reads as a spark.
Shader "SonicSnow/CheckpointParticle"
{
    Properties
    {
        [HDR] _Tint ("Tint", Color) = (1, 1, 1, 1)
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
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // Must match the Properties block exactly or the SRP Batcher drops this material.
            CBUFFER_START(UnityPerMaterial)
                float4 _Tint;
            CBUFFER_END

            Varyings Vertex(Attributes input)
            {
                Varyings output;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;

                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 colour = input.color * _Tint;

                // Alpha scales the emissive contribution rather than blending against the
                // background, so a particle fades to nothing as its lifetime alpha drops
                // instead of fading to a visible flat-tinted square.
                return half4(colour.rgb * colour.a, colour.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
