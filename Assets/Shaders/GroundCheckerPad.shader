// Rotating checker glow pad on the floor, standing in for a soft light puddle. Matches the
// reference "Finish Line Trigger Sequence" design's GROUND_FRAG precisely: radial falloff
// blended from _RimColor at the edge to _CoreColor at the centre, quantized into rings, cut
// into 8 alternating full/dimmed wedges that rotate, and a breathing pulse that jumps
// between discrete levels rather than easing.
//
// ZTest Always: this is light lying on the ground, and AR's estimated depth must not be
// able to chop it. A depth-tested pad would flicker in exactly the way the pillar's own
// checker break-up exists to hide.
Shader "SonicSnow/GroundCheckerPad"
{
    Properties
    {
        [HDR] _CoreColor ("Core colour", Color) = (0.72, 0.34, 0.02, 1)
        [HDR] _RimColor ("Rim colour", Color) = (1.0, 0.75, 0.28, 1)
        _Intensity ("Intensity", Range(0, 8)) = 1.5

        // Fraction of the quad's inscribed circle the glow covers.
        _Radius ("Falloff radius", Range(0.1, 1)) = 1.0

        // Number of quantized rings in the radial falloff.
        _Rings ("Radial steps", Float) = 4

        _WedgeCount ("Wedge count", Float) = 8
        _WedgeDim ("Wedge dim level", Range(0, 1)) = 0.25

        _SpinSpeed ("Spin speed (rad/s)", Float) = 0.5
        _PulseSpeed ("Pulse speed (rad/s)", Float) = 1.6

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
                float4 _CoreColor;
                float4 _RimColor;
                float  _Intensity;
                float  _Radius;
                float  _Rings;
                float  _WedgeCount;
                float  _WedgeDim;
                float  _SpinSpeed;
                float  _PulseSpeed;
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

                float2 p = (input.uv - 0.5) * 2.0;
                float r = length(p) / max(_Radius, 1e-4);

                // Breathing pulse: jumps between discrete levels rather than easing.
                float pulse = 0.9 + 0.1 * floor(sin(_Time.y * _PulseSpeed) * 3.0) / 3.0;

                float a = (1.0 - smoothstep(0.05, 1.0, r)) * pulse;

                // Rotating alternating wedges.
                float angle = atan2(p.y, p.x) + _Time.y * _SpinSpeed;
                float wedge = step(0.5, frac(angle / (TWO_PI / _WedgeCount) * 0.5));
                a *= lerp(_WedgeDim, 1.0, wedge);

                // Quantize the radial falloff into rings, then damp the whole pad — reads as
                // a floor glow, not a second light source.
                a = floor(a * _Rings) / _Rings * 0.6;

                float3 col = lerp(_RimColor.rgb, _CoreColor.rgb, saturate(r * 1.3));

                if (a <= 0.01) discard;

                return half4(col * _Intensity * a, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
