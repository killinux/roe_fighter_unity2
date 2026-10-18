// Rebuild of the game's "Pinkcore/Particles/Unlit" (a cut-down URP particle shader).
//   albedo = _BaseMap (two flipbook frames blended with _FLIPBOOKBLENDING_ON) * _BaseColor
//   mixed with the vertex colour by _ColorMode: multiply, overlay, colour (hue), difference, additive, subtractive
//   [_ALPHATEST_ON] clip(alpha - _Cutoff)   [_SOFTPARTICLES_ON] [_FADING_ON] alpha fades
//   [_EMISSION] + _EmissionMap.rgb * _EmissionColor.rgb          fog like every URP shader
// The texture's tiling / offset is not used (as in the game).
Shader "ROE/Particles/Unlit"
{
    Properties
    {
        [KeywordEnum(Multiply, Overlay, Color, Difference, Additive, Subtractive)] _ColorMode ("ColorMode", Float) = 0
        [NoScaleOffset] _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        [Toggle(_ALPHATEST_ON)] _EnableAlphaTest ("Alpha Test?", Float) = 0
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
        [Toggle(_EMISSION)] _EnableEmission ("Emission?", Float) = 0
        [HDR] _EmissionColor ("Emission Color", Color) = (0, 0, 0, 1)
        [NoScaleOffset] _EmissionMap ("Emission", 2D) = "white" {}
        [Toggle(_SOFTPARTICLES_ON)] _EnableSoftParticle ("Soft Particle?", Float) = 0
        _SoftParticlesNearFadeDistance ("Soft Particles Near Fade", Float) = 0
        _SoftParticlesFarFadeDistance ("Soft Particles Far Fade", Float) = 1
        [HideInInspector] _SoftParticleFadeParams ("__softparticlefadeparams", Vector) = (0, 0, 0, 0)
        [Toggle(_FADING_ON)] _EnableCameraFade ("Camera Fade?", Float) = 0
        _CameraNearFadeDistance ("Camera Near Fade", Float) = 1
        _CameraFarFadeDistance ("Camera Far Fade", Float) = 2
        [HideInInspector] _CameraFadeParams ("__camerafadeparams", Vector) = (0, 0, 0, 0)
        [Toggle(_FLIPBOOKBLENDING_ON)] _EnableFlipbookBlending ("Flipbook Blend?", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull [_Cull]
            ColorMask RGB

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local _FLIPBOOKBLENDING_ON
            #pragma shader_feature_local _SOFTPARTICLES_ON
            #pragma shader_feature_local_fragment _FADING_ON
            #pragma shader_feature_local_fragment _COLORMODE_MULTIPLY _COLORMODE_OVERLAY _COLORMODE_COLOR _COLORMODE_DIFFERENCE _COLORMODE_ADDITIVE _COLORMODE_SUBTRACTIVE
            #pragma shader_feature_local_fragment _EMISSION
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma multi_compile_fog
            #include "RoeParticles.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_EmissionMap); SAMPLER(sampler_EmissionMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _SoftParticleFadeParams;
                float4 _CameraFadeParams;
                half4 _BaseColor;
                half4 _EmissionColor;
                float _Cutoff;
                float _Cull;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                half4 color : COLOR;
                #if defined(_FLIPBOOKBLENDING_ON)
                    float4 uv : TEXCOORD0;
                    float blend : TEXCOORD1;
                #else
                    float2 uv : TEXCOORD0;
                #endif
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 uvFog : TEXCOORD0;       // xy: uv, z: fog factor
                half4 color : COLOR;
                #if defined(_FLIPBOOKBLENDING_ON)
                    float3 uv2Blend : TEXCOORD1;
                #endif
                #if defined(_SOFTPARTICLES_ON)
                    float4 screenPos : TEXCOORD2;
                #endif
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.uvFog = float3(input.uv.xy, ComputeFogFactor(output.positionCS.z));
                output.color = input.color;
                #if defined(_FLIPBOOKBLENDING_ON)
                    output.uv2Blend = float3(input.uv.zw, input.blend);
                #endif
                #if defined(_SOFTPARTICLES_ON)
                    output.screenPos = ComputeScreenPos(output.positionCS);
                #endif
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uvFog.xy);
                #if defined(_FLIPBOOKBLENDING_ON)
                    albedo = lerp(albedo, SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv2Blend.xy), input.uv2Blend.z);
                #endif
                albedo *= _BaseColor;

                half4 color = input.color;
                half4 result;
                result.a = albedo.a * color.a;
                #if defined(_COLORMODE_OVERLAY)
                    result.rgb = lerp(1.0 - 2.0 * (1.0 - albedo.rgb) * (1.0 - color.rgb), 2.0 * albedo.rgb * color.rgb, step(albedo.rgb, 0.5));
                #elif defined(_COLORMODE_COLOR)
                    half3 aHSL = RgbToHsv(albedo.rgb);
                    half3 bHSL = RgbToHsv(color.rgb);
                    result.rgb = HsvToRgb(half3(bHSL.x, bHSL.y, aHSL.z));
                #elif defined(_COLORMODE_DIFFERENCE)
                    result.rgb = abs(albedo.rgb - color.rgb);
                #elif defined(_COLORMODE_ADDITIVE)
                    result.rgb = albedo.rgb + color.rgb;
                #elif defined(_COLORMODE_SUBTRACTIVE)
                    result.rgb = albedo.rgb - color.rgb;
                #else
                    result.rgb = albedo.rgb * color.rgb;
                #endif

                #if defined(_ALPHATEST_ON)
                    clip(result.a - _Cutoff);
                #endif
                #if defined(_SOFTPARTICLES_ON)
                    result.a *= RoeSoftParticle(input.screenPos, input.positionCS.w, _SoftParticleFadeParams);
                #endif
                #if defined(_FADING_ON)
                    result.a *= RoeCameraFade(input.positionCS.w, _CameraFadeParams);
                #endif
                #if defined(_EMISSION)
                    result.rgb += SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, input.uvFog.xy).rgb * _EmissionColor.rgb;
                #endif
                result.rgb = MixFog(result.rgb, input.uvFog.z);
                return result;
            }
            ENDHLSL
        }
    }
}
