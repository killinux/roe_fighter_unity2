// Rebuild of the game's "Pinkcore/Particles/MutateDistortion": a textured effect laid over a
// distorted copy of the picture behind it.
//   base uv    = uv0.xy * _BaseMap_ST.xy + _BaseMap_ST.zw + uv0.zw * _FlowSpeed.xy      (uv0.zw, uv1.xy = custom data)
//                [BASEMAP_DISTORTION: + _DistortionMap.rg * _DistortStrength, that map scrolling with time]
//   scene      = picture behind, at screenUV + normal.xy * _ScreenDistortStrength
//                (normal map uv scrolls in x by uv1.x * _ScreenDistortFlowSpeed)
//   cover      = saturate(base.a)  [COLOR_DISSOLVE: 0 where _DissolveMap.r < uv1.y]
//   rgb        = lerp(scene, (base.rgb + _Emissive.rgb) * vertex.rgb, cover)
//   alpha      = vertex.a  [* _MaskMap.r with ALPHA_MASK]
Shader "ROE/Particles/MutateDistortion"
{
    Properties
    {
        [HDR] _Emissive ("Emissive", Color) = (0, 0, 0, 1)
        _BaseMap ("Base Map", 2D) = "white" {}
        _FlowSpeed ("FlowSpeed", Vector) = (0, 0, 0, 0)
        [Toggle(BASEMAP_DISTORTION)] _EnableBaseMapDistortion ("BaseMap Distortion (RG = distorted uv)?", Float) = 0
        _DistortionMap ("Distortion Map", 2D) = "black" {}
        _DistortStrength ("Distort Strength", Range(0, 1)) = 0.5
        _DistortFlowSpeed ("Distort Flow Speed", Vector) = (0, 0, 0, 0)
        [Toggle(COLOR_DISSOLVE)] _EnableColorDissolve ("Color dissolve ?", Float) = 0
        _DissolveMap ("Dissolve Map", 2D) = "white" {}
        _DistortNormalMap ("Distortion Normal Map", 2D) = "bump" {}
        _ScreenDistortStrength ("Screen Distort Strength", Range(0, 1)) = 0.5
        _ScreenDistortFlowSpeed ("Screen Distort Flow Speed", Float) = 0
        [Toggle(ALPHA_MASK)] _EnableMask ("Alpha Mask ?", Float) = 0
        _MaskMap ("Mask Map", 2D) = "white" {}
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Pass
        {
            Name "SurfaceColor"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull [_Cull]
            ColorMask RGB

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local ALPHA_MASK
            #pragma shader_feature_local COLOR_DISSOLVE
            #pragma shader_feature_local BASEMAP_DISTORTION
            #include "RoeParticles.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_DistortionMap); SAMPLER(sampler_DistortionMap);
            TEXTURE2D(_DissolveMap); SAMPLER(sampler_DissolveMap);
            TEXTURE2D(_DistortNormalMap); SAMPLER(sampler_DistortNormalMap);
            TEXTURE2D(_MaskMap); SAMPLER(sampler_MaskMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _DistortionMap_ST;
                float4 _DistortNormalMap_ST;
                float4 _DissolveMap_ST;
                float4 _MaskMap_ST;
                float4 _FlowSpeed;
                float4 _DistortFlowSpeed;
                float _ScreenDistortFlowSpeed;
                float _DistortStrength;
                float _ScreenDistortStrength;
                half4 _Emissive;
                float _Cull;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float4 uv : TEXCOORD0;
                half4 color : COLOR;
                float2 custom : TEXCOORD1;      // x: screen distortion scroll, y: dissolve amount
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 uv : TEXCOORD0;          // xy: base, zw: dissolve
                float4 maskScreen : TEXCOORD1;  // xy: mask uv
                float4 screenPos : TEXCOORD2;
                float4 distort : TEXCOORD3;     // xy: distortion map, zw: screen normal map
                float dissolve : TEXCOORD4;
                half4 color : TEXCOORD5;
            };

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.uv.xy = input.uv.xy * _BaseMap_ST.xy + _BaseMap_ST.zw + input.uv.zw * _FlowSpeed.xy;
                #if defined(COLOR_DISSOLVE)
                    output.uv.zw = input.uv.xy * _DissolveMap_ST.xy + _DissolveMap_ST.zw;
                    output.dissolve = input.custom.y;
                #endif
                output.maskScreen.xy = input.uv.xy * _MaskMap_ST.xy + _MaskMap_ST.zw;
                output.screenPos = ComputeScreenPos(output.positionCS);
                #if defined(BASEMAP_DISTORTION)
                    output.distort.xy = frac(_Time.y * _DistortFlowSpeed.xy) + input.uv.xy * _DistortionMap_ST.xy + _DistortionMap_ST.zw;
                #endif
                output.distort.zw = input.uv.xy * _DistortNormalMap_ST.xy + _DistortNormalMap_ST.zw
                                    + float2(input.custom.x * _ScreenDistortFlowSpeed, 0.0);
                output.color = input.color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 baseUV = input.uv.xy;
                #if defined(BASEMAP_DISTORTION)
                    baseUV += SAMPLE_TEXTURE2D(_DistortionMap, sampler_DistortionMap, input.distort.xy).rg * _DistortStrength;
                #endif
                half4 base = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, baseUV);
                #if defined(COLOR_DISSOLVE)
                    half gone = SAMPLE_TEXTURE2D(_DissolveMap, sampler_DissolveMap, input.uv.zw).r - input.dissolve < 0.0 ? -1.0 : 0.0;
                #else
                    half gone = 0.0;
                #endif
                half cover = saturate(gone + base.a);

                half2 n = RoeDistortionNormal(SAMPLE_TEXTURE2D(_DistortNormalMap, sampler_DistortNormalMap, input.distort.zw));
                half3 scene = RoeSceneColor(input.screenPos.xy / input.screenPos.w + n * _ScreenDistortStrength);
                half3 rgb = lerp(scene, (base.rgb + _Emissive.rgb) * input.color.rgb, cover);

                half alpha = input.color.a;
                #if defined(ALPHA_MASK)
                    alpha *= SAMPLE_TEXTURE2D(_MaskMap, sampler_MaskMap, input.maskScreen.xy).r;
                #endif
                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }
}
