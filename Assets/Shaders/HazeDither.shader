// Floor haze at the pillar's waist, dithered rather than blended. Matches the reference
// "Finish Line Trigger Sequence" design's HAZE_FRAG precisely: a soft rectangular band
// (independent height/width falloffs) thresholded against a 4x4 ordered dither mask that
// itself scrolls sideways over time, so it reads as the chunky stipple an early-90s console
// used to fake transparency it couldn't actually blend — not a smooth soft edge. Rim colour
// only, at low intensity — no core colour, per the reference.
//
// ZTest Always: this is haze lying on the ground, and AR's estimated depth must not be able
// to chop it — same reasoning as GroundCheckerPad.
Shader "SonicSnow/HazeDither"
{
    Properties
    {
        [HDR] _RimColor ("Colour (rim only — no core)", Color) = (1.0, 0.75, 0.28, 1)

        // UV-space shape of the soft rectangular band.
        _HeightFalloff ("Height falloff", Range(0.01, 1)) = 0.55
        _HeightCentre  ("Height centre (UV)", Range(0, 1)) = 0.42
        _WidthInner    ("Width inner edge (UV)", Range(0, 0.5)) = 0.25
        _WidthOuter    ("Width outer edge (UV)", Range(0, 0.5)) = 0.5

        // The reference's own canvas rendered at a small, downscaled resolution, so its
        // "1-2px" dither cell reads as chunky there — at full device resolution that would
        // vanish, so this defaults larger. Tune on device.
        _DitherCellPx ("Dither cell size (screen px)", Range(1, 16)) = 2
        _ScrollSpeed ("Dither scroll speed (px/s)", Float) = 7

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
                float4 _RimColor;
                float  _HeightFalloff;
                float  _HeightCentre;
                float  _WidthInner;
                float  _WidthOuter;
                float  _DitherCellPx;
                float  _ScrollSpeed;
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

            // 4x4 ordered dither, matching the reference's own chunky()/b2() functions
            // exactly rather than a generic Bayer matrix — the two produce visibly
            // different patterns even though both are "ordered dither."
            float B2(float2 v)
            {
                return fmod(3.0 * v.x + 2.0 * v.y, 4.0);
            }

            float Chunky(float2 fragCoord, float cellPx)
            {
                float2 p = fmod(floor(fragCoord / max(cellPx, 1e-4)), 4.0);
                return (4.0 * B2(floor(p * 0.5)) + B2(fmod(p, 2.0)) + 0.5) / 16.0;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float heightFalloff = 1.0 - smoothstep(0.0, max(_HeightFalloff, 1e-4), abs(input.uv.y - _HeightCentre));
                float widthFalloff  = 1.0 - smoothstep(_WidthInner, max(_WidthOuter, _WidthInner + 1e-4), abs(input.uv.x - 0.5));
                float band = heightFalloff * widthFalloff;

                float a = band * 0.34;

                float2 fc = input.positionCS.xy + float2(_Time.y * _ScrollSpeed, 0.0);
                if (a < Chunky(fc, _DitherCellPx)) discard;

                a *= 0.4;
                if (a <= 0.01) discard;

                return half4(_RimColor.rgb * a, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
