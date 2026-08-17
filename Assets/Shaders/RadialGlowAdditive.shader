// Soft additive radial gradient, shared by the finish line's ground puddle and its haze
// bands. Hot in the centre, transparent at the edge, with all motion driven from _Time so
// nothing here costs a MonoBehaviour update.
//
// ZTest defaults to Always: this is light lying on the floor, and the whole point is that
// AR's estimated ground depth must not be able to chop it. A depth-tested puddle would
// flicker in exactly the way the pillar's soft foot exists to avoid.
//
// The drift is perceptual cover rather than decoration. When the depth estimate wobbles,
// a slowly moving haze gives the eye something to read the change as — atmosphere instead
// of a glitch. Keep it slow and low-contrast; anything busy defeats the purpose.
Shader "SonicSnow/RadialGlowAdditive"
{
    Properties
    {
        // Deeper amber than the beam itself, per the palette: hot core, warm falloff.
        [HDR] _Color ("Colour", Color) = (1, 0.35, 0, 1)
        _Intensity ("Intensity", Range(0, 8)) = 1

        // Higher values pull the glow tighter to the centre.
        _Falloff ("Edge falloff", Range(0.25, 8)) = 2

        _PulsePeriod ("Pulse period (s, 0 = off)", Float) = 3.5
        _PulseAmount ("Pulse amount", Range(0, 0.5)) = 0.1

        // Lateral drift, for the haze bands. Left at 0 the quad sits still (the puddle).
        _DriftPeriod ("Drift period (s, 0 = off)", Float) = 0
        _DriftAmount ("Drift amount (UV)", Range(0, 0.5)) = 0

        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 8
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
            ZTest [_ZTest]
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

            // Must match the Properties block exactly or the SRP Batcher drops this material.
            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float  _Intensity;
                float  _Falloff;
                float  _PulsePeriod;
                float  _PulseAmount;
                float  _DriftPeriod;
                float  _DriftAmount;
                float  _ZTest;
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

                float2 centred = input.uv - 0.5;

                if (_DriftPeriod > 1e-4)
                    centred.x -= sin(_Time.y * TWO_PI / _DriftPeriod) * _DriftAmount;

                // 0 at the centre, 1 at the edge of the quad's inscribed circle.
                float dist = saturate(length(centred) * 2.0);
                float falloff = pow(saturate(1.0 - dist), _Falloff);

                float pulse = 1.0;
                if (_PulsePeriod > 1e-4)
                    pulse += sin(_Time.y * TWO_PI / _PulsePeriod) * _PulseAmount;

                float3 emissive = _Color.rgb * _Intensity * falloff * pulse;

                return half4(emissive, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
