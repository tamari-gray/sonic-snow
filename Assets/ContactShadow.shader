// Soft radial "blob" shadow for objects placed via XREAL's optical-see-through glasses,
// which can't do true environment occlusion — there's no way to block real-world light
// where a virtual object should occlude something in front of it, unlike phone AR's
// camera-composited depth occlusion. A dark, fading disc at an object's base does most of
// the perceptual work of "this touches the ground" as a placement/grounding cue instead.
//
// Unlit, alpha-blended, no lighting — this only ever needs to read as a shadow, not react
// to the scene's directional light.
Shader "SonicSnow/ContactShadow"
{
    Properties
    {
        _ShadowColor ("Shadow colour", Color) = (0, 0, 0, 0.45)

        // Fraction of the disc that's fully dark before it starts fading to the edge.
        // Lower = softer, more spread-out falloff.
        _Softness ("Edge softness", Range(0.01, 1)) = 0.6
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
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _ShadowColor;
                float  _Softness;
            CBUFFER_END

            Varyings Vertex(Attributes input)
            {
                Varyings output;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;

                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // 0 at centre, 1 at the quad's edge.
                float dist = distance(input.uv, float2(0.5, 0.5)) * 2.0;
                float falloff = 1.0 - smoothstep(1.0 - _Softness, 1.0, dist);

                return half4(_ShadowColor.rgb, _ShadowColor.a * falloff);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
