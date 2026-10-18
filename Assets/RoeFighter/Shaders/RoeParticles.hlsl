// Shared by the ROE/Particles/* shaders.  These are line-by-line rebuilds of the game's own
// "Pinkcore/Particles/*" shaders, read from the disassembly of their DirectX 11 programs
// (tools/shader_asm.py writes it to _work/shader_asm).  Property names are the game's, so the
// game's materials work unchanged.
#ifndef ROE_PARTICLES_INCLUDED
#define ROE_PARTICLES_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

// The game packs its distortion normals the DXT5nm way: x = r * a, y = g.
half2 RoeDistortionNormal(half4 packed)
{
    return half2(packed.r * packed.a, packed.g) * 2.0 - 1.0;
}

// Fades a particle out where it comes close to the scene behind it.
// params.x = near distance, params.y = 1 / (far - near); eyeDepth = the fragment's view depth.
half RoeSoftParticle(float4 screenPos, float eyeDepth, float4 params)
{
    float scene = LinearEyeDepth(SampleSceneDepth(screenPos.xy / screenPos.w), _ZBufferParams);
    return saturate((scene - params.x - eyeDepth) * params.y);
}

// Fades a particle out close to the camera.  params.x = near distance, params.y = 1 / (far - near).
half RoeCameraFade(float eyeDepth, float4 params)
{
    return saturate((eyeDepth - params.x) * params.y);
}

// The game draws its refraction effects in a pass of their own, after a copy of the frame
// (including earlier transparent effects) has been made.  Here the copy is URP's opaque
// texture: what is behind the effect, without other effects.
half3 RoeSceneColor(float2 screenUV)
{
    return SampleSceneColor(screenUV);
}

#endif
