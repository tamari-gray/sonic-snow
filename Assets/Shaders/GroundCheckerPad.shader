// Rotating checker glow pad on the floor, standing in for a soft light puddle.
//
// ZTest Always: this is light lying on the ground, and AR's estimated depth must not be
// able to chop it. A depth-tested pad would flicker in exactly the way the dome's checker
// break-up exists to hide.
//
// Everything steps. The radial falloff is quantized into rings, the disc is cut into
// alternating wedges, and the breathing pulse jumps between discrete levels instead of
// easing — the animation itself should read as low-framerate, not smooth.
Shader "SonicSnow/GroundCheckerPad"
{
    Properties
    {
        [HDR] _Color ("Colour", Color) = (0, 0.5071269, 1, 1)
        _Intensity ("Intensity", Range(0, 8)) = 1.5

        // Fraction of the quad's inscribed circle the glow covers.
        _Radius ("Falloff radius", Range(0.1, 1)) = 0.85

        // Number of quantized rings in the radial falloff.
        _Rings ("Radial steps", Range(2, 12)) = 5

        _WedgeCount ("Wedge count", Range(2, 32)) = 8
        _WedgeDim ("Wedge dim level", Range(0, 1)) = 0.25

        // Matches the dome's default so pad and dome spin together.
        _SpinSpeed ("Spin speed (rad/s)", Float) = 0.3

        _PulseAmount ("Pulse amount", Range(0, 0.5)) = 0.12

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
                float  _Rings;
                float  _WedgeCount;
                float  _WedgeDim;
                float  _SpinSpeed;
                float  _PulseAmount;
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
                float dist = length(centred) * 2.0;

                // Stepped rings rather than a gradient, matching the dome's banded rim.
                float radial = saturate(1.0 - dist / max(_Radius, 1e-4));
                radial = floor(radial * _Rings) / _Rings;

                // Rotating wedges: alternating full and dimmed slices.
                float angle = atan2(centred.y, centred.x) + _Time.y * _SpinSpeed;
                float wedge = fmod(floor((angle / TWO_PI + 0.5) * _WedgeCount), 2.0);
                float wedgeLevel = lerp(_WedgeDim, 1.0, wedge);

                // Breathing in 3 discrete levels — it should jump, not ease.
                float pulseStep = floor(sin(_Time.y * 1.6) * 3.0) / 3.0;
                float pulse = 1.0 + pulseStep * _PulseAmount;

                float3 emissive = _Color.rgb * _Intensity * radial * wedgeLevel * pulse;

                return half4(emissive, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
