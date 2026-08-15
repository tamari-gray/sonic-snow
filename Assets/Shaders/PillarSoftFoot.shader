// Additive beam for the finish-line pillar, with a soft foot.
//
// The problem this solves: the pillar is a solid cylinder, and AR Foundation's
// AROcclusionManager occludes it against an *estimated* environment depth. That estimate
// jitters frame to frame, so the cutoff where the beam meets the floor crawls and flickers.
// The fix isn't to fight the occlusion — it's to make sure there are no opaque pixels down
// there to clip in the first place. Alpha ramps to zero across the bottom _FadeHeight
// metres, so the geometry has already dissolved before it reaches any plausible ground
// estimate, and a wobbling depth solve has nothing left to chop.
//
// Additive and depth-write-off throughout: this is light, not an object.
//
// Stereo macros are load-bearing — the XREAL build renders single-pass instanced, and a
// shader without them draws to one eye or misprojects.
Shader "SonicSnow/PillarSoftFoot"
{
    Properties
    {
        // Seeded from LightPillarMat's existing orange so the beam keeps its palette.
        [HDR] _Color ("Colour", Color) = (1, 0.5490196, 0, 1)
        _Intensity ("Intensity", Range(0, 8)) = 1.4

        _FadeHeight ("Bottom fade height (m)", Float) = 2
        _TopFade ("Top fade height (m)", Float) = 4

        // Fresnel-ish rim, so the cylinder reads as a volume rather than a flat billboard.
        // 0 disables it (pow(x, 0) == 1), which is the escape hatch if the beam reads hollow.
        _RimPower ("Rim power (0 = off)", Range(0, 8)) = 1.5

        _NoiseTex ("Noise (grayscale)", 2D) = "white" {}
        _ScrollSpeed ("Noise scroll speed", Float) = 0.08
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
                // x = metres above the beam's base, y = metres below its top.
                float2 heights    : TEXCOORD3;
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
                float  _TopFade;
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

                // Unity's cylinder spans -1..1 in object space: 0 at the base, 1 at the top.
                // Scaling by the object's world height turns the fade distances into real
                // metres, so they stay honest whatever the beam is scaled to.
                float t = saturate(input.positionOS.y * 0.5 + 0.5);
                float worldHeight = length(unity_ObjectToWorld._m01_m11_m21) * 2.0;
                output.heights = float2(t * worldHeight, (1.0 - t) * worldHeight);

                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float bottom = smoothstep(0.0, max(_FadeHeight, 1e-4), input.heights.x);
                float top    = smoothstep(0.0, max(_TopFade, 1e-4), input.heights.y);

                float3 viewDir = normalize(GetWorldSpaceViewDir(input.positionWS));
                float  rim = pow(1.0 - saturate(dot(viewDir, normalize(input.normalWS))), _RimPower);

                // Texture, not a light show: the noise only ever removes up to 15%.
                float2 noiseUV = input.uv;
                noiseUV.y -= _Time.y * _ScrollSpeed;
                float noise = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, noiseUV).r;
                float grain = lerp(0.85, 1.0, noise);

                float3 emissive = _Color.rgb * _Intensity * rim * grain * bottom * top;

                // Additive (One One) ignores alpha; the falloffs are already in the colour.
                return half4(emissive, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
