// Eyebrow and eyelash cards of Rise of Eros characters (alpha blended over the face).
// Stands in for the game's "Pinkcore/Heros/Eyebrow"; the maths follows the game's compiled
// shader (tools/shader_asm.py): plain Lambert with the full shadow, light probes and the
// per-vertex point lights, on albedo x 0.96 (a dielectric's diffuse share), no highlight.
// The game's materials premultiply AND blend with SrcAlpha: colour x alpha^2 over the face x
// (1 - alpha), which keeps soft edges dark - brows and lashes read dense.
Shader "ROE/Eyebrow"
{
    Properties
    {
        [Header(Base)] [Space(5)]
        [NoScaleOffset] _BaseMap ("Albedo", 2D) = "white" {}
        _BaseColor ("Color", Color) = (1,1,1,1)

        [Header(Cast Shadow)] [Space(5)]
        _CastShadowDepthBias ("Depth Bias", Range(0, 30)) = 1
        _CastShadowNormalBias ("Normal Bias", Range(0, 30)) = 0.5

        [Toggle(_OCCLUSIONMAP)] _EnableOcclusion ("Occlusion", Float) = 0
        _OcclusionMap ("Occlusion Map", 2D) = "white" {}
        _OcclusionStrength ("Occlusion Strength", Range(0, 1)) = 1

        [Header(Other)] [Space(5)]
        [Toggle(_ALPHAPREMULTIPLY_ON)] _EnablePremultiplyAlpha ("Premultiply Alpha", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 10
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Culling", Float) = 2
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend [_SrcBlend] [_DstBlend], One OneMinusSrcAlpha
            ZWrite Off
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex RoeVert
            #pragma fragment Frag

            #pragma shader_feature_local_fragment _OCCLUSIONMAP
            #pragma shader_feature_local_fragment _ALPHAPREMULTIPLY_ON

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #pragma multi_compile_instancing

            #include "RoeCore.hlsl"

            TEXTURE2D(_BaseMap);        SAMPLER(sampler_BaseMap);
            TEXTURE2D(_OcclusionMap);   SAMPLER(sampler_OcclusionMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _OcclusionMap_ST;
                half4 _BaseColor;
                half _CastShadowDepthBias;
                half _CastShadowNormalBias;
                half _OcclusionStrength;
            CBUFFER_END

            struct RoeSurface
            {
                half3 diffuse;      // albedo x 0.96
                half3 normalWS;
            };

            half3 RoeShadeLight(RoeSurface s, InputData inputData, Light light)
            {
                half NdotL = saturate(dot(s.normalWS, light.direction));
                return s.diffuse * light.color * (light.distanceAttenuation * light.shadowAttenuation * NdotL);
            }

            #define ROE_SURFACE_DIFFUSE(s) ((s).diffuse)
            #include "RoeLightLoop.hlsl"

            half4 Frag(RoeVaryings input, FRONT_FACE_TYPE frontFace : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half occlusion = 1.0;
            #if defined(_OCCLUSIONMAP)
                occlusion = LerpWhiteTo(SAMPLE_TEXTURE2D(_OcclusionMap, sampler_OcclusionMap, input.uv).g, _OcclusionStrength);
            #endif

                // the card's own normal, turned round on its back face
                half3x3 tangentToWorld = RoeTangentToWorld(input, IS_FRONT_VFACE(frontFace, true, false));
                InputData inputData;
                RoeInitInputData(input, tangentToWorld, half3(0, 0, 1), inputData);

                RoeSurface s;
                s.diffuse = tex.rgb * _BaseColor.rgb * 0.96;
                s.normalWS = inputData.normalWS;

                AmbientOcclusionFactor aoFactor = CreateAmbientOcclusionFactor(inputData.normalizedScreenSpaceUV, occlusion);
                half3 color = inputData.bakedGI * s.diffuse * aoFactor.indirectAmbientOcclusion;
                color += RoeDirectLighting(s, inputData, aoFactor);
            #if defined(_ALPHAPREMULTIPLY_ON)
                color *= tex.a;
            #endif
                color = MixFog(color, inputData.fogCoord);
                return half4(color, tex.a);
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
