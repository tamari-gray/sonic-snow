// Vertical alpha ramp for the finish-line beam: solid where it meets the snow,
// fading out by the time it reaches the sky.
//
// Unlit on purpose. The beam is meant to read as light, so shading it with the scene's
// directional light made it darker on the shadowed side, which is exactly wrong for
// something that is supposed to be emitting.
//
// The ramp is driven by object-space Y rather than world Y, so it survives the beam
// being moved, re-anchored, or yawed to face back down the run.
Shader "SonicSnow/LightPillarGradient"
{
    Properties
    {
        [HDR] _BaseColor ("Colour", Color) = (1, 0.5490196, 0, 1)

        _AlphaBase   ("Alpha at base",   Range(0, 1)) = 1
        _AlphaMarker ("Alpha at marker", Range(0, 1)) = 0.1
        _AlphaTop    ("Alpha at top",    Range(0, 1)) = 0

        // Where the FINISH LINE text sits, as a fraction of the beam's height.
        // The beam spans 0.17m to 50.17m and the text sits at 15.83m, so (15.83-0.17)/50.
        _MarkerHeight ("Marker height up beam", Range(0.001, 0.999)) = 0.313134

        // Soft dissolve at the very bottom, as a fraction of the beam's height. Without
        // this the beam is fully opaque right where it meets the snow, so the hard
        // intersection reads as clipping through the ground — especially on optical
        // see-through glasses, which can't occlude the real world behind it. 0.06 of a
        // 50m beam is roughly a 3m mist zone.
        _GroundFade ("Ground fade height", Range(0, 0.5)) = 0.06

        // Shapes the fade curve. 1 is linear; higher values hold the beam transparent
        // for longer near the ground and ramp up faster, which reads more like haze
        // than like a dimmer switch.
        _GroundFadeSoftness ("Ground fade softness", Range(0.25, 4)) = 1.6
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
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float  heightT    : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // Must match the Properties block exactly, or the SRP Batcher silently
            // drops this material out of batching.
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float  _AlphaBase;
                float  _AlphaMarker;
                float  _AlphaTop;
                float  _MarkerHeight;
                float  _GroundFade;
                float  _GroundFadeSoftness;
            CBUFFER_END

            Varyings Vertex(Attributes input)
            {
                Varyings output;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);

                // Unity's built-in cylinder spans -1..1 in object space, so this maps
                // the beam to 0 at the base and 1 at the top.
                output.heightT = saturate(input.positionOS.y * 0.5 + 0.5);

                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // Clamped away from the ends so neither segment can divide by zero.
                float marker = clamp(_MarkerHeight, 0.001, 0.999);
                float t = input.heightT;

                // Two straight segments: base -> marker, then marker -> top.
                float alpha = (t < marker)
                    ? lerp(_AlphaBase,   _AlphaMarker, t / marker)
                    : lerp(_AlphaMarker, _AlphaTop,    (t - marker) / (1.0 - marker));

                // Dissolve the base into the ground. Applied on top of the ramp rather
                // than folded into it, so tuning the mist can't disturb the marker/top
                // gradient the beam's read depends on.
                float groundFade = saturate(t / max(_GroundFade, 1e-4));
                alpha *= pow(groundFade, _GroundFadeSoftness);

                return half4(_BaseColor.rgb, _BaseColor.a * alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
