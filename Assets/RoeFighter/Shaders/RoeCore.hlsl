// Shared code of the ROE character shaders (URP 17, forward / forward+).
// The game's shader sources are not in the build, but its compiled DirectX 11 programs are:
// tools/shader_asm.py disassembles them, and the lighting maths of these shaders follows that
// disassembly.  They keep the game's property names, so the game's materials work as they are.
#ifndef ROE_CORE_INCLUDED
#define ROE_CORE_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Random.hlsl"

struct RoeAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS   : NORMAL;
    float4 tangentOS  : TANGENT;
    float2 texcoord   : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct RoeVaryings
{
    float2 uv         : TEXCOORD0;
    float3 positionWS : TEXCOORD1;
    float3 normalWS   : TEXCOORD2;
    half4  tangentWS  : TEXCOORD3;    // xyz: tangent, w: sign
    half   fogFactor  : TEXCOORD4;
#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
    float4 shadowCoord : TEXCOORD5;
#endif
    half3  vertexLight : TEXCOORD6;   // point and spot lights, see RoePunctualVertexLights
    float4 positionCS : SV_POSITION;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

// Is additional light number i of this object (the index of a URP light loop) a directional light?
bool RoeLightIsDirectional(uint i)
{
#if USE_CLUSTER_LIGHT_LOOP
    int index = i;
#else
    int index = GetPerObjectLightIndex(i);
#endif
#if USE_STRUCTURED_BUFFER_FOR_LIGHT_DATA
    return _AdditionalLightsBuffer[index].position.w == 0.0;
#else
    return _AdditionalLightsPosition[index].w == 0.0;
#endif
}

// Point and spot lights the way the game lights its characters and stages (read from its
// shaders: every variant in the build has _ADDITIONAL_LIGHTS_VERTEX): per vertex, plain Lambert,
// the attenuation capped at 1.  The lights in question are the flashes that skill effects carry
// (particle systems with a Lights module, intensity 8 and more); per pixel they would flood
// the floor, per vertex a sparse floor mesh hardly notices them while characters light up.
// Extra directional lights (our studio's fill and rim) stay per pixel, see RoeLightLoop.hlsl -
// unless the pipeline itself is set to per-vertex lights, then they are summed here as well.
half3 RoePunctualVertexLights(float3 positionWS, half3 normalWS)
{
    half3 sum = half3(0, 0, 0);
#if (defined(_ADDITIONAL_LIGHTS) || defined(_ADDITIONAL_LIGHTS_VERTEX)) && !USE_CLUSTER_LIGHT_LOOP
    uint count = GetAdditionalLightsCount();
    for (uint i = 0u; i < count; ++i)
    {
    #if !defined(_ADDITIONAL_LIGHTS_VERTEX)
        if (RoeLightIsDirectional(i))
            continue;
    #endif
        Light light = GetAdditionalLight(i, positionWS);
        sum += light.color * (min(light.distanceAttenuation, 1.0) * saturate(dot(normalWS, light.direction)));
    }
#endif
    return sum;
}

RoeVaryings RoeVert(RoeAttributes input)
{
    RoeVaryings output = (RoeVaryings)0;
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
    output.vertexLight = RoePunctualVertexLights(vertexInput.positionWS, normalInput.normalWS);

#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
    output.shadowCoord = GetShadowCoord(vertexInput);
#endif
    output.positionCS = vertexInput.positionCS;
    return output;
}

// Tangent frame of the pixel.  Back faces of two-sided meshes (hair cards, cloth) get the
// mirrored normal, the tangent and bitangent stay.
half3x3 RoeTangentToWorld(RoeVaryings input, bool frontFace)
{
    float3 n = normalize(input.normalWS);
    float3 t = input.tangentWS.xyz;
    if (dot(t, t) < 1e-6)   // mesh without usable tangents: any frame is better than NaN
        t = normalize(cross(n, abs(n.y) < 0.99 ? float3(0, 1, 0) : float3(1, 0, 0)));
    float3 b = input.tangentWS.w * cross(n, t);
    if (!frontFace)
        n = -n;
    return half3x3(t, b, n);
}

void RoeInitInputData(RoeVaryings input, half3x3 tangentToWorld, half3 normalTS, out InputData inputData)
{
    inputData = (InputData)0;
    inputData.positionWS = input.positionWS;
    inputData.positionCS = input.positionCS;
    inputData.tangentToWorld = tangentToWorld;
    inputData.normalWS = NormalizeNormalPerPixel(TransformTangentToWorld(normalTS, tangentToWorld));
    inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);

#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
    inputData.shadowCoord = input.shadowCoord;
#elif defined(MAIN_LIGHT_CALCULATE_SHADOWS)
    inputData.shadowCoord = TransformWorldToShadowCoord(inputData.positionWS);
#else
    inputData.shadowCoord = float4(0, 0, 0, 0);
#endif

    inputData.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor);
    inputData.vertexLighting = input.vertexLight;
    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
    inputData.bakedGI = SampleSH(inputData.normalWS);   // characters are dynamic: light probes / ambient only
    inputData.shadowMask = half4(1, 1, 1, 1);
}

// The GGX highlight of URP's Lit shader for one light, without the diffuse part.
half3 RoeSpecularGGX(BRDFData brdf, half3 normalWS, half3 lightDirWS, half3 viewDirWS)
{
    return brdf.specular * DirectBRDFSpecular(brdf, normalWS, lightDirWS, viewDirWS);
}

// Ambient diffuse + reflections, like URP's GlobalIllumination(), with two extra controls:
// fresnelScale scales the grazing-angle boost, reflections = false drops the reflection probe.
half3 RoeEnvironment(BRDFData brdf, InputData inputData, half3 diffuseNormalGI, half occlusion,
                     half fresnelScale, bool reflections)
{
    half3 reflectVector = reflect(-inputData.viewDirectionWS, inputData.normalWS);
    half NoV = saturate(dot(inputData.normalWS, inputData.viewDirectionWS));
    half fresnelTerm = Pow4(1.0 - NoV) * fresnelScale;

    half3 color = diffuseNormalGI * brdf.diffuse;
    if (reflections)
    {
        half3 indirectSpecular = GlossyEnvironmentReflection(reflectVector, inputData.positionWS,
            brdf.perceptualRoughness, half(1.0), inputData.normalizedScreenSpaceUV);
        color += indirectSpecular * EnvironmentBRDFSpecular(brdf, fresnelTerm);
    }
    return color * occlusion;
}

// Normal map strength the way the game's character and skin shaders apply it: the full normal
// is decoded first, then blended towards flat by the scale (URP's UnpackNormalScale only scales
// x and y, z stays as decoded - that is what the game's hair shader does).
half3 RoeUnpackNormalLerp(half4 packedNormal, half scale)
{
    half3 n = UnpackNormal(packedNormal);
    return half3(n.xy * scale, (n.z - 1.0) * scale + 1.0);
}

// Hermite ramp of x already in 0..1 (smoothstep without the remap).
half RoeSmooth01(half x)
{
    return x * x * (3.0 - 2.0 * x);
}

// Screen-door fade the game uses (_IGNOpacity): 1 = solid, 0 = gone.
void RoeDitherClip(float4 positionCS, half opacity)
{
    if (opacity < 0.999)
        clip(opacity - InterleavedGradientNoise(positionCS.xy, 0) - 0.001);
}

// Alpha-to-coverage friendly cut-out: sharpen alpha around the threshold so MSAA can
// anti-alias the edge; without MSAA it behaves like a plain alpha test.
half RoeCutoutAlpha(half alpha, half cutoff)
{
    half sharpened = (alpha - cutoff) / max(fwidth(alpha), 0.0001) + 0.5;
    clip(alpha - cutoff * 0.5);    // far below the threshold: always drop
    return saturate(sharpened);
}

#endif
