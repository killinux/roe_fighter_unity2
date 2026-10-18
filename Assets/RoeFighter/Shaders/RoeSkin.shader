// Skin, face and teeth of Rise of Eros characters.
// Stands in for the game's "Pinkcore/Heros/Skin"; the maths follows the game's compiled shader
// (tools/shader_asm.py):
//   diffuse   the game's skin LUT at (half-Lambert of a softened normal, MGAC alpha x curvature
//             strength) times (albedo + a red "translucent" term that grows towards the
//             silhouette: (1 - N.V)^power x colour x MGAC alpha x strength); the softened normal
//             is the normal map read _BumpBias mips blurrier, without detail and scale
//   specular  URP's GGX on the detailed normal, times N.L of the softened normal
//   ambient   light probes x occlusion x albedo, reflections without the grazing boost
Shader "ROE/Skin"
{
    Properties
    {
        [Enum(Opaque, 0, Transparent, 1)] _Surface ("Surface Type", Float) = 0

        [Header(Base)] [Space(5)]
        _BaseMap ("Albedo", 2D) = "white" {}
        _BaseColor ("Color", Color) = (1,1,1,1)
        [Toggle(_ALPHATEST_ON)] _EnableAlphaTest ("Alpha Test", Float) = 0
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
        _IGNOpacity ("Dither Opacity", Range(0, 1)) = 1

        [Header(Cast Shadow)] [Space(5)]
        _CastShadowDepthBias ("Depth Bias (x light bias)", Range(0, 30)) = 1
        _CastShadowNormalBias ("Normal Bias (x light bias)", Range(0, 30)) = 0.5

        [Header(Skin)] [Space(5)]
        [NoScaleOffset] _SkinLutMap ("Skin LUT (u: N.L, v: curvature)", 2D) = "white" {}
        _LutCurveStrength ("Curvature Strength", Range(0, 1)) = 0.5
        _TranslucentCurveStrength ("Translucent Curvature Strength", Range(0, 1)) = 0.5
        _TranslucentColor ("Translucent Color", Color) = (1,1,1,1)
        _TranslucentPower ("Translucent Power", Range(0, 5)) = 1

        [Header(Metallic Gloss Occlusion Curvature)] [Space(5)]
        [NoScaleOffset] _MetallicGlossMap ("MGAC (R metallic, G gloss, B occlusion, A curvature)", 2D) = "white" {}
        _Metallic ("Metallic", Range(0, 1)) = 0
        _Smoothness ("Smoothness", Range(0, 1)) = 0.5
        _OcclusionStrength ("Occlusion Strength", Range(0, 1)) = 1

        [Header(Normal)] [Space(5)]
        [NoScaleOffset] _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Scale", Range(0, 1)) = 1
        _BumpBias ("Diffuse Normal Mip Bias", Range(0, 20)) = 0
        _DetailNormalMap ("Detail Normal Map", 2D) = "bump" {}
        _DetailBumpScale ("Detail Bump Scale", Range(0, 1)) = 1

        [Header(Emission)] [Space(5)]
        [Toggle(_EMISSION)] _EnableEmission ("Emission", Float) = 0
        [HDR] _EmissionColor ("Color", Color) = (0,0,0,1)
        _EmissionMap ("Emission", 2D) = "white" {}

        [Header(Other)] [Space(5)]
        [Toggle(_ALPHAPREMULTIPLY_ON)] _EnablePremultiplyAlpha ("Premultiply Alpha", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Culling", Float) = 2
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "RoeCore.hlsl"

        TEXTURE2D(_BaseMap);            SAMPLER(sampler_BaseMap);
        TEXTURE2D(_MetallicGlossMap);   SAMPLER(sampler_MetallicGlossMap);
        TEXTURE2D(_BumpMap);            SAMPLER(sampler_BumpMap);
        TEXTURE2D(_DetailNormalMap);    SAMPLER(sampler_DetailNormalMap);
        TEXTURE2D(_EmissionMap);        SAMPLER(sampler_EmissionMap);
        TEXTURE2D(_SkinLutMap);

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            float4 _DetailNormalMap_ST;
            half4 _BaseColor;
            half4 _EmissionColor;
            half4 _TranslucentColor;
            half _Surface;
            half _Cutoff;
            half _CastShadowDepthBias;
            half _CastShadowNormalBias;
            half _LutCurveStrength;
            half _TranslucentCurveStrength;
            half _TranslucentPower;
            half _Metallic;
            half _Smoothness;
            half _OcclusionStrength;
            half _BumpScale;
            half _BumpBias;
            half _DetailBumpScale;
            half _IGNOpacity;
        CBUFFER_END

        float2 RoeBaseUV(float2 uv)
        {
            return uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
        }

        void RoeClip(float2 uv, float4 positionCS)
        {
        #if defined(_ALPHATEST_ON)
            clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, RoeBaseUV(uv)).a * _BaseColor.a - _Cutoff);
        #endif
            RoeDitherClip(positionCS, _IGNOpacity);
        }

        void RoeShadowClip(float2 uv)
        {
        #if defined(_ALPHATEST_ON)
            clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, RoeBaseUV(uv)).a * _BaseColor.a - _Cutoff);
        #endif
            clip(_IGNOpacity - 0.5);
        }

        #define ROE_SHADOW_DEPTH_BIAS _CastShadowDepthBias
        #define ROE_SHADOW_NORMAL_BIAS _CastShadowNormalBias
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend [_SrcBlend] [_DstBlend]
            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex RoeVert
            #pragma fragment Frag

            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local_fragment _ALPHAPREMULTIPLY_ON
            #pragma shader_feature_local_fragment _EMISSION

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ REFLECTION_PROBE_ROTATION
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #pragma multi_compile_instancing

            struct RoeSurface
            {
                BRDFData brdf;
                half3 normalWS;          // detailed normal (with pores): highlights
                half3 diffuseNormalWS;   // softened normal: the LUT lookup and the highlight's N.L
                half curvature;          // LUT row: 0 flat .. 1 thin / tightly curved
                half3 translucent;       // red added to the diffuse colour towards the silhouette
            };

            half3 RoeShadeLight(RoeSurface s, InputData inputData, Light light)
            {
                half3 L = light.direction;
                half3 radiance = light.color * (light.distanceAttenuation * light.shadowAttenuation);

                // Pre-integrated scattering: the LUT replaces max(N.L, 0).
                half NdotLd = dot(s.diffuseNormalWS, L);
                half3 lut = SAMPLE_TEXTURE2D_LOD(_SkinLutMap, sampler_LinearClamp,
                                                 float2(NdotLd * 0.5 + 0.5, s.curvature), 0).rgb;
                half3 color = lut * (s.brdf.diffuse + s.translucent);
                color += RoeSpecularGGX(s.brdf, s.normalWS, L, inputData.viewDirectionWS) * saturate(NdotLd);
                return color * radiance;
            }

            #include "RoeLightLoop.hlsl"

            half4 Frag(RoeVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = RoeBaseUV(input.uv);
                half4 albedoAlpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv) * _BaseColor;
                half alpha = albedoAlpha.a;
            #if defined(_ALPHATEST_ON)
                clip(alpha - _Cutoff);
            #endif
                // paint on the skin fading out (RoeClothesBurst Piece.fade: m02's vines)
                RoeDitherClip(input.positionCS, _IGNOpacity);

                half4 mgac = SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_MetallicGlossMap, uv);
                half metallic = mgac.r * _Metallic;
                half smoothness = mgac.g * _Smoothness;
                half occlusion = LerpWhiteTo(mgac.b, _OcclusionStrength);

                half3 normalTS = RoeUnpackNormalLerp(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uv), _BumpScale);
                half3 softTS = UnpackNormal(SAMPLE_TEXTURE2D_BIAS(_BumpMap, sampler_BumpMap, uv, _BumpBias));
                float2 detailUV = input.uv * _DetailNormalMap_ST.xy + _DetailNormalMap_ST.zw;
                half3 detailTS = RoeUnpackNormalLerp(SAMPLE_TEXTURE2D(_DetailNormalMap, sampler_DetailNormalMap, detailUV), _DetailBumpScale);
                normalTS = BlendNormalRNM(normalTS, detailTS);

                half3x3 tangentToWorld = RoeTangentToWorld(input, true);
                InputData inputData;
                RoeInitInputData(input, tangentToWorld, normalTS, inputData);

                RoeSurface s;
                InitializeBRDFData(albedoAlpha.rgb, metallic, half3(0, 0, 0), smoothness, alpha, s.brdf);
                s.normalWS = inputData.normalWS;
                s.diffuseNormalWS = NormalizeNormalPerPixel(TransformTangentToWorld(softTS, tangentToWorld));
                s.curvature = mgac.a * _LutCurveStrength;
                half oneMinusNoV = 1.0 - saturate(dot(s.normalWS, inputData.viewDirectionWS));
                half rim = oneMinusNoV > 0.0 ? 0.04 + 0.96 * pow(oneMinusNoV, _TranslucentPower) : 0.04;
                s.translucent = _TranslucentColor.rgb * (rim * mgac.a * _TranslucentCurveStrength);

                AmbientOcclusionFactor aoFactor = CreateAmbientOcclusionFactor(inputData.normalizedScreenSpaceUV, occlusion);
                // ambient: light probes at the mesh normal; reflections with no grazing boost
                half3 ambient = SampleSH(normalize(input.normalWS));
                half3 color = RoeEnvironment(s.brdf, inputData, ambient, aoFactor.indirectAmbientOcclusion, 0.0, true);
                color += RoeDirectLighting(s, inputData, aoFactor);

            #if defined(_EMISSION)
                color += SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, uv).rgb * _EmissionColor.rgb;
            #endif

                color = MixFog(color, inputData.fogCoord);
                return half4(color, OutputAlpha(alpha, IsSurfaceTypeTransparent(_Surface)));
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex RoeShadowVert
            #pragma fragment RoeShadowFrag
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing
            #include "RoePasses.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex RoeDepthVert
            #pragma fragment RoeDepthFrag
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma multi_compile_instancing
            #include "RoePasses.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex RoeDepthVert
            #pragma fragment RoeDepthNormalsFrag
            #pragma shader_feature_local _ALPHATEST_ON
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #pragma multi_compile_instancing
            #include "RoePasses.hlsl"
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
