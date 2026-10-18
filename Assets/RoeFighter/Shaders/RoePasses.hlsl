// Shadow caster, depth and depth-normals passes shared by the ROE character shaders.
// Before including, the shader defines:
//     ROE_SHADOW_DEPTH_BIAS, ROE_SHADOW_NORMAL_BIAS   multipliers of the light's shadow bias
//     void RoeClip(float2 uv, float4 positionCS)      cut-out / dither for depth passes
//     void RoeShadowClip(float2 uv)                    cut-out for the shadow pass
#ifndef ROE_PASSES_INCLUDED
#define ROE_PASSES_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

// Set by URP while it renders a shadow map (see ShadowUtils.SetupShadowCasterConstantBuffer).
float3 _LightDirection;
float3 _LightPosition;

struct RoePassAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS   : NORMAL;
    float2 texcoord   : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct RoePassVaryings
{
    float2 uv         : TEXCOORD0;
    half3  normalWS   : TEXCOORD1;
    float4 positionCS : SV_POSITION;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

// ---------------------------------------------------------------- shadow caster
RoePassVaryings RoeShadowVert(RoePassAttributes input)
{
    RoePassVaryings output = (RoePassVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);

    float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
    float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
#if _CASTING_PUNCTUAL_LIGHT_SHADOW
    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
#else
    float3 lightDirectionWS = _LightDirection;
#endif
    // URP's ApplyShadowBias with the material's own multipliers (the game has both per material).
    float invNdotL = 1.0 - saturate(dot(lightDirectionWS, normalWS));
    float scale = invNdotL * _ShadowBias.y * ROE_SHADOW_NORMAL_BIAS;
    positionWS = lightDirectionWS * (_ShadowBias.x * ROE_SHADOW_DEPTH_BIAS) + positionWS;
    positionWS = normalWS * scale + positionWS;

    output.uv = input.texcoord;
    output.normalWS = normalWS;
    output.positionCS = ApplyShadowClamping(TransformWorldToHClip(positionWS));
    return output;
}

half4 RoeShadowFrag(RoePassVaryings input) : SV_TARGET
{
    UNITY_SETUP_INSTANCE_ID(input);
    RoeShadowClip(input.uv);
    return 0;
}

// ---------------------------------------------------------------- depth only
RoePassVaryings RoeDepthVert(RoePassAttributes input)
{
    RoePassVaryings output = (RoePassVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
    output.uv = input.texcoord;
    output.normalWS = TransformObjectToWorldNormal(input.normalOS);
    output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
    return output;
}

half RoeDepthFrag(RoePassVaryings input) : SV_TARGET
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    RoeClip(input.uv, input.positionCS);
    return input.positionCS.z;
}

// ---------------------------------------------------------------- depth + normals (SSAO)
void RoeDepthNormalsFrag(RoePassVaryings input, FRONT_FACE_TYPE frontFace : FRONT_FACE_SEMANTIC,
                         out half4 outNormalWS : SV_Target0
#ifdef _WRITE_RENDERING_LAYERS
                         , out uint outRenderingLayers : SV_Target1
#endif
)
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    RoeClip(input.uv, input.positionCS);
    half3 normalWS = NormalizeNormalPerPixel(input.normalWS);
    normalWS = IS_FRONT_VFACE(frontFace, normalWS, -normalWS);
    outNormalWS = half4(normalWS, 0.0);
#ifdef _WRITE_RENDERING_LAYERS
    outRenderingLayers = EncodeMeshRenderingLayer();
#endif
}

#endif
