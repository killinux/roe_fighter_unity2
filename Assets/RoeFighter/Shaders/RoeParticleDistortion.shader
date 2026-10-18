// Rebuild of the game's "Pinkcore/Particles/Distortion": a patch of the picture behind it,
// pushed around by a normal map (heat haze, shock waves).
//   normal uv = uv0.xy * _DistortNormalMap_ST.xy + uv0.zw * _Speed.xy      (uv0.zw = custom data)
//   rgb   = scene(screenUV + normal.xy * _DistortStrength) * vertex.rgb
//   alpha = vertex.a  [* _ColorMask.r with ALPHA_MASK]
// The game draws it in a "Refraction" pass after copying the frame; here it is an ordinary
// transparent pass that reads URP's opaque texture (see RoeParticles.hlsl).
Shader "ROE/Particles/Distortion"
{
    Properties
    {
        _DistortNormalMap ("Distort Normal Map", 2D) = "bump" {}
        _DistortStrength ("Distort Strength", Range(0, 1)) = 0.5
        _Speed ("Speed", Vector) = (0, 0, 0, 0)
        [Toggle(ALPHA_MASK)] _EnableAlphaMask ("Alpha Mask?", Float) = 0
        [NoScaleOffset] _ColorMask ("Color Mask", 2D) = "white" {}
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        LOD 100
        Pass
        {
            Name "Refraction"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull [_Cull]
            ColorMask RGB

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local_fragment ALPHA_MASK
            #include "RoeParticles.hlsl"

            TEXTURE2D(_DistortNormalMap); SAMPLER(sampler_DistortNormalMap);
            TEXTURE2D(_ColorMask); SAMPLER(sampler_ColorMask);
            CBUFFER_START(UnityPerMaterial)
                float4 _DistortNormalMap_ST;
                float4 _Speed;
                float _DistortStrength;
                float _Cull;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float4 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 uv : TEXCOORD0;          // xy: mask, zw: normal map
                float4 screenPos : TEXCOORD1;
                half4 color : TEXCOORD2;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.uv.xy = input.uv.xy;
                output.uv.zw = input.uv.xy * _DistortNormalMap_ST.xy + input.uv.zw * _Speed.xy;
                output.screenPos = ComputeScreenPos(output.positionCS);
                output.color = input.color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half2 n = RoeDistortionNormal(SAMPLE_TEXTURE2D(_DistortNormalMap, sampler_DistortNormalMap, input.uv.zw));
                float2 screenUV = input.screenPos.xy / input.screenPos.w + n * _DistortStrength;
                half3 rgb = RoeSceneColor(screenUV) * input.color.rgb;
                half alpha = input.color.a;
                #if defined(ALPHA_MASK)
                    alpha *= SAMPLE_TEXTURE2D(_ColorMask, sampler_ColorMask, input.uv.xy).r;
                #endif
                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }
}
