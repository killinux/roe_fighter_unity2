// Stage surfaces of Rise of Eros battle scenes.
// Stands in for the game's "Pinkcore/Heros/ErosLit/Environment" (and SimpleLit/Environment):
// the same surface model as ROE/Character, plus baked lightmaps (the stages are lightmapped).
Shader "ROE/Environment"
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
        _CastShadowDepthBias ("Depth Bias (unused)", Range(0, 30)) = 0
        _CastShadowNormalBias ("Normal Bias (unused)", Range(0, 30)) = 0

        [Header(Metallic Gloss Occlusion)] [Space(5)]
        _Smoothness ("Smoothness", Range(0, 1)) = 0.5
        _Metallic ("Metallic", Range(0, 1)) = 0
        [NoScaleOffset] _MetallicGlossMap ("MGA (R metallic, G gloss, B occlusion)", 2D) = "white" {}
        _OcclusionStrength ("Occlusion Strength", Range(0, 1)) = 1
        _FresnelStrength ("Fresnel Strength (grazing reflection)", Range(0, 1)) = 0

        [Header(Normal)] [Space(5)]
        _BumpScale ("Scale", Range(0, 1)) = 1
        [NoScaleOffset] _BumpMap ("Normal Map", 2D) = "bump" {}
        _DetailNormalMap ("Detail Normal Map", 2D) = "bump" {}
        _DetailBumpScale ("Detail Bump Scale", Range(0, 1)) = 1

        [Header(Emission)] [Space(5)]
        [HDR] _EmissionColor ("Color", Color) = (0,0,0,1)
        [NoScaleOffset] _EmissionMap ("Emission", 2D) = "white" {}

        _UVScaleOffset ("Tiling / Offset", Vector) = (1,1,0,0)

        [Header(Other)] [Space(5)]
        [Toggle(_ALPHAPREMULTIPLY_ON)] _EnablePremultiplyAlpha ("Premultiply Alpha", Float) = 0
        [ToggleOff(_RECEIVE_SHADOWS_OFF)] _ReceiveShadows ("Receive Shadows", Float) = 1
        [KeywordEnum(None, DirectSpecular, IndirectSpecular)] _SpecularMode ("Specular Mode", Float) = 2
        [Toggle(_PLANAR_REFLECTION_ON)] _EnablePlanarReflection ("Planar Reflection (unused)", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Culling", Float) = 2
        [Enum(Off, 0, On, 1)] _ZWrite ("Z Write", Float) = 1
        _Opacity ("Dither Opacity", Range(0, 1)) = 1
        _AlphaMultiplier ("Alpha Multiplier", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "RoeCore.hlsl"

        TEXTURE2D(_BaseMap);            SAMPLER(sampler_BaseMap);
        TEXTURE2D(_MetallicGlossMap);   SAMPLER(sampler_MetallicGlossMap);
        TEXTURE2D(_BumpMap);            SAMPLER(sampler_BumpMap);
        TEXTURE2D(_EmissionMap);        SAMPLER(sampler_EmissionMap);

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            float4 _DetailNormalMap_ST;
            float4 _UVScaleOffset;
            half4 _BaseColor;
            half4 _EmissionColor;
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
            half _Opacity;
            half _AlphaMultiplier;
        CBUFFER_END

        float2 RoeUV(float2 uv)
        {
            return (uv * _BaseMap_ST.xy + _BaseMap_ST.zw) * _UVScaleOffset.xy + _UVScaleOffset.zw;
        }

        void RoeClip(float2 uv, float4 positionCS)
        {
        #if defined(_ALPHATEST_ON)
            clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, RoeUV(uv)).a * _BaseColor.a - _Cutoff);
        #endif
            RoeDitherClip(positionCS, _Opacity);
        }

        void RoeShadowClip(float2 uv)
        {
        #if defined(_ALPHATEST_ON)
            clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, RoeUV(uv)).a * _BaseColor.a - _Cutoff);
        #endif
        }

        #define ROE_SHADOW_DEPTH_BIAS 1.0
        #define ROE_SHADOW_NORMAL_BIAS 1.0
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
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma shader_feature_local _RECEIVE_SHADOWS_OFF
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local_fragment _ALPHAPREMULTIPLY_ON
            #pragma shader_feature_local_fragment _ DIRECT_SPECULAR INDIRECT_SPECULAR

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
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_ON
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #pragma multi_compile_instancing

            struct EnvAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 texcoord   : TEXCOORD0;
                float2 staticLightmapUV : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct EnvVaryings
            {
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                half4  tangentWS  : TEXCOORD3;
                half   fogFactor  : TEXCOORD4;
            #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                float4 shadowCoord : TEXCOORD5;
            #endif
                DECLARE_LIGHTMAP_OR_SH(staticLightmapUV, vertexSH, 6);
                half3  vertexLight : TEXCOORD7;
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            EnvVaryings Vert(EnvAttributes input)
            {
                EnvVaryings output = (EnvVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                output.uv = input.texcoord;
                output.positionWS = vertexInput.positionWS;
                output.normalWS = normalInput.normalWS;
                real sign = input.tangentOS.w * GetOddNegativeScale();
                output.tangentWS = half4(normalInput.tangentWS.xyz, sign);
                half fogFactor = 0;
            #if !defined(_FOG_FRAGMENT)
                fogFactor = ComputeFogFactor(vertexInput.positionCS.z);
            #endif
                output.fogFactor = fogFactor;
            #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                output.shadowCoord = GetShadowCoord(vertexInput);
            #endif
                OUTPUT_LIGHTMAP_UV(input.staticLightmapUV, unity_LightmapST, output.staticLightmapUV);
                OUTPUT_SH(output.normalWS.xyz, output.vertexSH);
                output.vertexLight = RoePunctualVertexLights(vertexInput.positionWS, normalInput.normalWS);
                output.positionCS = vertexInput.positionCS;
                return output;
            }

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
            #if defined(DIRECT_SPECULAR) || defined(INDIRECT_SPECULAR)
                brdf += RoeSpecularGGX(s.brdf, s.normalWS, light.direction, inputData.viewDirectionWS);
            #endif
                return brdf * radiance;
            }

            #include "RoeLightLoop.hlsl"

            half4 Frag(EnvVaryings input, FRONT_FACE_TYPE frontFace : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = RoeUV(input.uv);
                half4 albedoAlpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv) * _BaseColor;
                half alpha = albedoAlpha.a * _AlphaMultiplier;
            #if defined(_ALPHATEST_ON)
                clip(albedoAlpha.a - _Cutoff);
            #endif
                RoeDitherClip(input.positionCS, _Opacity);

                half4 mga = SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_MetallicGlossMap, uv);
                half metallic = _Metallic * mga.r;
                half smoothness = _Smoothness * mga.g;
                half occlusion = LerpWhiteTo(mga.b, _OcclusionStrength);
                half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uv), _BumpScale);

                // the forward helpers of RoeCore work on RoeVaryings: fill one from this pass's varyings
                RoeVaryings core = (RoeVaryings)0;
                core.uv = input.uv;
                core.positionWS = input.positionWS;
                core.normalWS = input.normalWS;
                core.tangentWS = input.tangentWS;
                core.fogFactor = input.fogFactor;
                core.vertexLight = input.vertexLight;
            #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                core.shadowCoord = input.shadowCoord;
            #endif
                core.positionCS = input.positionCS;

                half3x3 tangentToWorld = RoeTangentToWorld(core, IS_FRONT_VFACE(frontFace, true, false));
                InputData inputData;
                RoeInitInputData(core, tangentToWorld, normalTS, inputData);
                inputData.bakedGI = SAMPLE_GI(input.staticLightmapUV, input.vertexSH, inputData.normalWS);
                inputData.shadowMask = SAMPLE_SHADOWMASK(input.staticLightmapUV);

                RoeSurface s;
                InitializeBRDFData(albedoAlpha.rgb, metallic, half3(0, 0, 0), smoothness, alpha, s.brdf);
                s.normalWS = inputData.normalWS;

                AmbientOcclusionFactor aoFactor = CreateAmbientOcclusionFactor(inputData.normalizedScreenSpaceUV, occlusion);
                half4 shadowMask = CalculateShadowMask(inputData);
                Light mainLight = GetMainLight(inputData, shadowMask, aoFactor);
                MixRealtimeAndBakedGI(mainLight, inputData.normalWS, inputData.bakedGI);

            #if defined(INDIRECT_SPECULAR)
                bool reflections = true;
            #else
                bool reflections = false;
            #endif
                half3 color = RoeEnvironment(s.brdf, inputData, inputData.bakedGI,
                                             aoFactor.indirectAmbientOcclusion, _FresnelStrength, reflections);
                color += RoeDirectLighting(s, inputData, aoFactor);
                color += SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, uv).rgb * _EmissionColor.rgb;

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
