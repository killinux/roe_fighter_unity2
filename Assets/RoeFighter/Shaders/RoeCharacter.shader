// Outfit, weapon and accessory surfaces of Rise of Eros characters.
// Stands in for the game's "Pinkcore/Heros/ErosLit/Character" (and SimpleLit/Character):
// same property names, so the game's materials bind without changes.  The maths follows the
// game's compiled shader (tools/shader_asm.py): URP's standard PBR with an RNM detail normal,
// the grazing reflection scaled by _FresnelStrength and the final colour pulled towards
// _RimColor at grazing angles.  UVs: _BaseMap_ST for the albedo, _UVScaleOffset for the normal,
// MGA and emission maps, _DetailNormalMap_ST on the mesh UV for the detail normal.
Shader "ROE/Character"
{
    Properties
    {
        [Enum(Opaque, 0, Transparent, 1)] _Surface ("Surface Type", Float) = 0
        [Toggle] _IsDecal ("Is Decal (transparent only)", Float) = 0

        [Header(Base)] [Space(5)]
        _BaseMap ("Albedo", 2D) = "white" {}
        _BaseColor ("Color", Color) = (1,1,1,1)
        [Toggle(_ALPHATEST_ON)] _EnableAlphaTest ("Alpha Test", Float) = 0
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5

        [Header(Cast Shadow)] [Space(5)]
        _CastShadowDepthBias ("Depth Bias (x light bias)", Range(0, 30)) = 1
        _CastShadowNormalBias ("Normal Bias (x light bias)", Range(0, 30)) = 0.5

        [Header(Metallic Gloss Occlusion)] [Space(5)]
        [Toggle(PINKCORE_MGA)] _EnableMGA ("MGA map (R metallic, G gloss, B occlusion)", Float) = 0
        _Smoothness ("Smoothness", Range(0, 1)) = 0.5
        _Metallic ("Metallic", Range(0, 1)) = 0
        [NoScaleOffset] _MetallicGlossMap ("MGA Map", 2D) = "white" {}
        _OcclusionStrength ("Occlusion Strength", Range(0, 1)) = 1
        _FresnelStrength ("Fresnel Strength (grazing reflection)", Range(0, 1)) = 0

        [Header(Normal)] [Space(5)]
        [Toggle(_NORMALMAP)] _EnableNormalMap ("Normal Map", Float) = 0
        _BumpScale ("Scale", Range(0, 1)) = 1
        [NoScaleOffset] _BumpMap ("Normal Map", 2D) = "bump" {}
        _DetailNormalMap ("Detail Normal Map", 2D) = "bump" {}
        _DetailBumpScale ("Detail Bump Scale", Range(0, 1)) = 1

        [Header(Emission)] [Space(5)]
        [Toggle(_EMISSION)] _EnableEmission ("Emission", Float) = 0
        [HDR] _EmissionColor ("Color", Color) = (0,0,0,1)
        [NoScaleOffset] _EmissionMap ("Emission", 2D) = "white" {}

        _UVScaleOffset ("Tiling / Offset", Vector) = (1,1,0,0)

        [Header(Other)] [Space(5)]
        [Toggle(_ALPHAPREMULTIPLY_ON)] _EnablePremultiplyAlpha ("Premultiply Alpha", Float) = 0
        [ToggleOff(_RECEIVE_SHADOWS_OFF)] _ReceiveShadows ("Receive Shadows", Float) = 1
        [Toggle(DIRECT_SPECULAR)] _EnableDirectSpecular ("Direct Specular", Float) = 1
        [Toggle(INDIRECT_SPECULAR)] _EnableIndirectSpecular ("Indirect Specular", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Culling", Float) = 2
        [Enum(Off, 0, On, 1)] _ZWrite ("Z Write", Float) = 1
        _IGNOpacity ("Dither Opacity", Range(0, 1)) = 1
        [HDR] _RimColor ("Rim Color (alpha = strength)", Color) = (1,1,1,0)
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

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            float4 _DetailNormalMap_ST;
            float4 _UVScaleOffset;
            half4 _BaseColor;
            half4 _EmissionColor;
            half4 _RimColor;
            half _Surface;
            half _IsDecal;
            half _Cutoff;
            half _CastShadowDepthBias;
            half _CastShadowNormalBias;
            half _Smoothness;
            half _Metallic;
            half _OcclusionStrength;
            half _FresnelStrength;
            half _BumpScale;
            half _DetailBumpScale;
            half _IGNOpacity;
        CBUFFER_END

        // UV of the normal, MGA and emission maps
        float2 RoeUV(float2 uv)
        {
            return uv * _UVScaleOffset.xy + _UVScaleOffset.zw;
        }

        // UV of the albedo map
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
            ZWrite [_ZWrite]
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex RoeVert
            #pragma fragment Frag

            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local _RECEIVE_SHADOWS_OFF
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local_fragment _ALPHAPREMULTIPLY_ON
            #pragma shader_feature_local_fragment _EMISSION
            #pragma shader_feature_local_fragment PINKCORE_MGA
            #pragma shader_feature_local_fragment DIRECT_SPECULAR
            #pragma shader_feature_local_fragment INDIRECT_SPECULAR

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
                half3 normalWS;
            };

            half3 RoeShadeLight(RoeSurface s, InputData inputData, Light light)
            {
                half NdotL = saturate(dot(s.normalWS, light.direction));
                half3 radiance = light.color * (light.distanceAttenuation * light.shadowAttenuation * NdotL);
                half3 brdf = s.brdf.diffuse;
            #if defined(DIRECT_SPECULAR)
                brdf += RoeSpecularGGX(s.brdf, s.normalWS, light.direction, inputData.viewDirectionWS);
            #endif
                return brdf * radiance;
            }

            #include "RoeLightLoop.hlsl"

            half4 Frag(RoeVaryings input, FRONT_FACE_TYPE frontFace : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = RoeUV(input.uv);
                half4 albedoAlpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, RoeBaseUV(input.uv)) * _BaseColor;
                half alpha = albedoAlpha.a;
            #if defined(_ALPHATEST_ON)
                clip(alpha - _Cutoff);
            #endif
                RoeDitherClip(input.positionCS, _IGNOpacity);

                half metallic = _Metallic;
                half smoothness = _Smoothness;
                half occlusion = 1.0;
            #if defined(PINKCORE_MGA)
                half4 mga = SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_MetallicGlossMap, uv);
                metallic *= mga.r;
                smoothness *= mga.g;
                occlusion = LerpWhiteTo(mga.b, _OcclusionStrength);
            #endif

                half3 normalTS = half3(0, 0, 1);
            #if defined(_NORMALMAP)
                normalTS = RoeUnpackNormalLerp(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uv), _BumpScale);
                float2 detailUV = input.uv * _DetailNormalMap_ST.xy + _DetailNormalMap_ST.zw;
                half3 detailTS = RoeUnpackNormalLerp(SAMPLE_TEXTURE2D(_DetailNormalMap, sampler_DetailNormalMap, detailUV), _DetailBumpScale);
                normalTS = BlendNormalRNM(normalTS, detailTS);
            #endif

                half3x3 tangentToWorld = RoeTangentToWorld(input, IS_FRONT_VFACE(frontFace, true, false));
                InputData inputData;
                RoeInitInputData(input, tangentToWorld, normalTS, inputData);

                RoeSurface s;
                InitializeBRDFData(albedoAlpha.rgb, metallic, half3(0, 0, 0), smoothness, alpha, s.brdf);
                s.normalWS = inputData.normalWS;

                AmbientOcclusionFactor aoFactor = CreateAmbientOcclusionFactor(inputData.normalizedScreenSpaceUV, occlusion);
            #if defined(INDIRECT_SPECULAR)
                bool reflections = true;
            #else
                bool reflections = false;
            #endif
                half3 color = RoeEnvironment(s.brdf, inputData, inputData.bakedGI,
                                             aoFactor.indirectAmbientOcclusion, _FresnelStrength, reflections);
                color += RoeDirectLighting(s, inputData, aoFactor);

            #if defined(_EMISSION)
                color += SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, uv).rgb * _EmissionColor.rgb;
            #endif

                // Rim used by the game for hit flashes and highlights (alpha 0 = off): the colour
                // is blended towards _RimColor at grazing angles, not added.
                half NoV = saturate(dot(inputData.normalWS, inputData.viewDirectionWS));
                color = lerp(color, _RimColor.rgb, _RimColor.a * Pow4(1.0 - NoV));

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
