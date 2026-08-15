// Floor haze, dithered rather than blended.
//
// Where the smooth version faded alpha toward the edges, this thresholds a 2x2 ordered
// (Bayer) dither against the falloff shape, so every pixel is either fully on or fully off.
// The result is the chunky stipple an early-90s console used to fake transparency it
// couldn't actually blend.
//
// The lateral drift is quantized too — it steps between discrete offsets instead of sliding,
// so the motion reads as low-framerate rather than smooth. That still does the job the haze
// exists for: when AR's depth estimate wobbles, drifting mist gives the eye something to
// read the change as.
Shader "SonicSnow/HazeDither"
{
    Properties
    {
        [HDR] _Color ("Colour", Color) = (0, 0.42, 0.95, 1)
        _Intensity ("Intensity", Range(0, 8)) = 0.5

        _Radius ("Falloff radius", Range(0.1, 1)) = 0.9

        _DriftPeriod ("Drift period (s, 0 = off)", Float) = 7.5
        _DriftAmount ("Drift amount (UV)", Range(0, 0.5)) = 0.06

        // How many discrete offsets the drift steps through. Higher is smoother; the point
        // is that it should visibly step.
        _DriftSteps ("Drift steps", Range(2, 64)) = 16

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

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float  _Intensity;
                float  _Radius;
                float  _DriftPeriod;
                float  _DriftAmount;
                float  _DriftSteps;
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

            // Classic 2x2 ordered dither:  0 2 / 3 1, over 4.
            float Bayer2x2(float2 pixel)
            {
                float2 m = fmod(abs(pixel), 2.0);
                float value = (m.x < 1.0)
                    ? ((m.y < 1.0) ? 0.0 : 3.0)
                    : ((m.y < 1.0) ? 2.0 : 1.0);
                return value / 4.0;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 centred = input.uv - 0.5;

                if (_DriftPeriod > 1e-4)
                {
                    float raw = sin(_Time.y * TWO_PI / _DriftPeriod) * _DriftAmount;
                    // Whole steps only — the drift should stutter, not slide.
                    float steps = max(_DriftSteps, 1.0);
                    centred.x -= floor(raw * steps) / steps;
                }

                float dist = length(centred) * 2.0;
                float shape = saturate(1.0 - dist / max(_Radius, 1e-4));

                // Hard on/off against the ordered mask — no partial alpha anywhere.
                float threshold = Bayer2x2(input.positionCS.xy);
                float on = step(threshold, shape);

                float3 emissive = _Color.rgb * _Intensity * on;

                return half4(emissive, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
