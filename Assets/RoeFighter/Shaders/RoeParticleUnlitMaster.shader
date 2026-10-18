// Rebuild of the game's "Pinkcore/Particles/UnlitMaster", its newest all-in-one effect shader.
// Vertex streams: uv0.xy, custom1 (TEXCOORD1: xy = UV offset, z = dissolve amount, w = distortion
// strength), custom2 (TEXCOORD2.rgb = emission colour), colour, normal.
//   the UV offset goes to ONE of base / dissolve / distortion map (_OffsetOption = one-hot of _UVMode);
//   tiling of the four maps is used, their offset is not
//   [DISTORTION]  base uv += _DistortionMap normal.xy * custom1.w
//   [DISSOLVE]    v = _DissolveMap.r (HybridBaseMapAlpha: * base.a);  t = custom1.z * (softness + width + 1.001)
//                 alpha *= smoothstep(t - softness - width, t - width, v)
//                 a band of _DissolveWidth before the cut glows with the emission colour (edge softness)
//   colour mode:  multiply = saturate(base * vertex),  subtractive = saturate(base - vertex)
//   alpha         = base.a * [dissolve] * [soft particle] * [camera fade] * _AlphaMask.r * vertex.a
//   emission:     self = + custom2.rgb,  fresnel = + custom2.rgb * smoothstep(middle -/+ softness, 1 - N.V)
// Blend, ZWrite and Cull come from the material.
Shader "ROE/Particles/UnlitMaster"
{
    Properties
    {
        [Enum(Opaque, 0, Transparent, 1)] _Surface ("Surface Type", Float) = 0
        [KeywordEnum(Multiply, Subtractive)] _ColorMode ("Color Mode", Float) = 0
        [KeywordEnum(None, Self, Fresnel)] _EmissionMode ("Emission Mode", Float) = 0
        _FresnelMiddle ("Fresnel Middle", Range(0, 1)) = 0.5
        _FresnelSoftness ("Fresnel Softness", Range(0.0001, 1)) = 0.1
        [Enum(BaseMap, 0, DissolveMap, 1, DistortionMap, 2)] _UVMode ("UV Offset Target", Float) = 0
        [HideInInspector] _OffsetOption ("_OffsetOption", Vector) = (1, 0, 0, 0)
        _BaseMap ("Base Map", 2D) = "white" {}
        [Toggle(_ALPHATEST_ON)] _EnableAlphaTest ("Alpha Test?", Float) = 0
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
        [Toggle(DISTORTION)] _EnableBaseMapDistortion ("BaseMap Distortion (RG = distort direction)?", Float) = 0
        _DistortionMap ("Distortion Map", 2D) = "bump" {}
        [Toggle(DISSOLVE)] _EnableDissolve ("Dissolve ?", Float) = 0
        [Enum(DissolveMapOnly, 0, HybridBaseMapAlpha, 1)] _DissolveMode ("Dissolve Mode", Float) = 0
        _DissolveMap ("Dissolve Map", 2D) = "white" {}
        _DissolveSoftness ("Dissolve Softness", Range(0.001, 1)) = 0.001
        _DissolveEdgeSoftness ("Dissolve Edge Softness", Range(0.001, 1)) = 0.001
        _DissolveWidth ("Dissolve Width", Range(0, 1)) = 0
        _AlphaMask ("AlphaMask", 2D) = "white" {}
        [Toggle(_SOFTPARTICLES_ON)] _EnableSoftParticle ("Soft Particle?", Float) = 0
        _SoftParticlesNearFadeDistance ("Soft Particles Near Fade", Float) = 0
        _SoftParticlesFarFadeDistance ("Soft Particles Far Fade", Float) = 1
        [HideInInspector] _SoftParticleFadeParams ("__softparticlefadeparams", Vector) = (0, 0, 0, 0)
        [Toggle(_FADING_ON)] _EnableCameraFade ("Camera Fade?", Float) = 0
        _CameraNearFadeDistance ("Camera Near Fade", Float) = 1
        _CameraFarFadeDistance ("Camera Far Fade", Float) = 2
        [HideInInspector] _CameraFadeParams ("__camerafadeparams", Vector) = (0, 0, 0, 0)
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 0
        [Enum(Off, 0, On, 1)] _ZWrite ("Z Write", Float) = 1
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        LOD 100
        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull [_Cull]
            ColorMask RGB

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local_fragment DISSOLVE
            #pragma shader_feature_local_fragment DISTORTION
            #pragma shader_feature_local_fragment _SOFTPARTICLES_ON
            #pragma shader_feature_local_fragment _FADING_ON
            #pragma shader_feature_local_fragment _COLORMODE_MULTIPLY _COLORMODE_SUBTRACTIVE
            #pragma shader_feature_local_fragment _EMISSIONMODE_NONE _EMISSIONMODE_SELF _EMISSIONMODE_FRESNEL
            #pragma multi_compile_fog
            #include "RoeParticles.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_DissolveMap); SAMPLER(sampler_DissolveMap);
            TEXTURE2D(_DistortionMap); SAMPLER(sampler_DistortionMap);
            TEXTURE2D(_AlphaMask); SAMPLER(sampler_AlphaMask);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _DissolveMap_ST;
                float4 _DistortionMap_ST;
                float4 _AlphaMask_ST;
                float _DissolveMode;
                float _DissolveSoftness;
                float _DissolveEdgeSoftness;
                float _DissolveWidth;
                float _Cutoff;
                float4 _OffsetOption;
                float4 _SoftParticleFadeParams;
                float4 _CameraFadeParams;
                float _FresnelMiddle;
                float _FresnelSoftness;
                float _SrcBlend;
                float _DstBlend;
                float _ZWrite;
                float _Cull;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 custom1 : TEXCOORD1;
                float3 custom2 : TEXCOORD2;
                half4 color : COLOR;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 uv : TEXCOORD0;              // xy: base, zw: dissolve
                float4 uv2 : TEXCOORD1;             // xy: distortion, zw: alpha mask
                float2 amounts : TEXCOORD2;         // x: dissolve amount, y: distortion strength
                float4 screenPos : TEXCOORD3;
                half4 color : TEXCOORD4;
                float4 emissionFog : TEXCOORD5;     // rgb: emission colour, a: fog factor
                float3 normalWS : TEXCOORD6;
                float3 viewDirWS : TEXCOORD7;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS);
                output.positionCS = TransformWorldToHClip(positionWS);
                float2 offset = input.custom1.xy;
                output.uv.xy = input.uv * _BaseMap_ST.xy + offset * _OffsetOption.x;
                output.uv.zw = input.uv * _DissolveMap_ST.xy + offset * _OffsetOption.y;
                output.uv2.xy = input.uv * _DistortionMap_ST.xy + offset * _OffsetOption.z;
                output.uv2.zw = input.uv * _AlphaMask_ST.xy;
                output.amounts = saturate(input.custom1.zw);
                output.screenPos = ComputeScreenPos(output.positionCS);
                output.color = input.color;
                output.emissionFog = float4(input.custom2, ComputeFogFactor(output.positionCS.z));
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.viewDirWS = GetWorldSpaceViewDir(positionWS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 baseUV = input.uv.xy;
                #if defined(DISTORTION)
                    baseUV += input.amounts.y * RoeDistortionNormal(SAMPLE_TEXTURE2D(_DistortionMap, sampler_DistortionMap, input.uv2.xy));
                #endif
                half4 base = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, baseUV);
                half alpha = base.a;
                half3 edge = 0.0;
                #if defined(DISSOLVE)
                    float band = _DissolveSoftness + _DissolveWidth;
                    float front = (band + 1.001) * input.amounts.x;
                    half v = SAMPLE_TEXTURE2D(_DissolveMap, sampler_DissolveMap, input.uv.zw).r;
                    v = lerp(v, v * base.a, _DissolveMode);
                    alpha *= smoothstep(front - band, front - _DissolveWidth, v);
                    if (_DissolveWidth > 0.0)
                        edge = smoothstep(0.0, 1.0, saturate((front - v) / _DissolveEdgeSoftness)) * input.emissionFog.rgb;
                #endif
                #if defined(_SOFTPARTICLES_ON)
                    alpha *= RoeSoftParticle(input.screenPos, input.positionCS.w, _SoftParticleFadeParams);
                #endif
                #if defined(_FADING_ON)
                    alpha *= RoeCameraFade(input.positionCS.w, _CameraFadeParams);
                #endif
                alpha *= SAMPLE_TEXTURE2D(_AlphaMask, sampler_AlphaMask, input.uv2.zw).r * input.color.a;
                #if defined(_ALPHATEST_ON)
                    clip(alpha - _Cutoff - 1e-6);
                #elif defined(DISSOLVE)
                    clip(alpha - 1e-6);
                #endif

                #if defined(_COLORMODE_SUBTRACTIVE)
                    half3 rgb = saturate(base.rgb - input.color.rgb);
                #else
                    half3 rgb = saturate(base.rgb * input.color.rgb);
                #endif
                rgb += edge;
                #if defined(_EMISSIONMODE_SELF)
                    rgb += input.emissionFog.rgb;
                #elif defined(_EMISSIONMODE_FRESNEL)
                    half rim = 1.0 - saturate(dot(normalize(input.normalWS), normalize(input.viewDirWS)));
                    rgb += input.emissionFog.rgb * smoothstep(_FresnelMiddle - _FresnelSoftness, _FresnelMiddle + _FresnelSoftness, rim);
                #endif
                rgb = MixFog(rgb, input.emissionFog.a);
                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }
}
