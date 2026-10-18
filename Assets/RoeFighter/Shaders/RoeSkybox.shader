// Sky of Rise of Eros battle scenes: a cubemap with tint, exposure and rotation, plus a band
// of fog colour rising from the horizon.  Stands in for the game's "Pinkcore/Skybox/FogCubemap".
Shader "ROE/Skybox"
{
    Properties
    {
        _Tint ("Tint Color", Color) = (0.5,0.5,0.5,1)
        [Gamma] _Exposure ("Exposure", Range(0, 8)) = 1
        _Rotation ("Rotation", Range(0, 360)) = 0
        [NoScaleOffset] _Cubemap ("Cubemap (HDR)", Cube) = "grey" {}
        [Toggle(SKYBOX_FOG)] _EnableFog ("Fog", Float) = 0
        _FogColor ("Fog Color", Color) = (1,1,1,1)
        _FogHeight ("Fog Height", Range(-1, 1)) = 0.5
        _FogIntensity ("Fog Intensity", Range(0, 1)) = 1
        _FogSoftness ("Fog Softness", Range(0.002, 0.2)) = 0.05
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma shader_feature_local_fragment SKYBOX_FOG

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/EntityLighting.hlsl"

            TEXTURECUBE(_Cubemap);
            SAMPLER(sampler_Cubemap);
            half4 _Cubemap_HDR;
            half4 _Tint;
            half _Exposure;
            float _Rotation;
            half4 _FogColor;
            half _FogHeight;
            half _FogIntensity;
            half _FogSoftness;

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 direction : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float angle = _Rotation * PI / 180.0;
                float s = sin(angle);
                float c = cos(angle);
                float3 p = input.positionOS.xyz;
                float3 rotated = float3(c * p.x - s * p.z, p.y, s * p.x + c * p.z);
                output.positionCS = TransformObjectToHClip(rotated);
                output.direction = p;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 dir = normalize(input.direction);
                half4 tex = SAMPLE_TEXTURECUBE(_Cubemap, sampler_Cubemap, dir);
                half3 color = DecodeHDREnvironment(tex, _Cubemap_HDR);
                color *= _Tint.rgb * half3(4.59479380, 4.59479380, 4.59479380) * _Exposure;   // same scale as Unity's cubemap skybox
            #if defined(SKYBOX_FOG)
                half fog = 1.0 - smoothstep(_FogHeight - _FogSoftness, _FogHeight + _FogSoftness, dir.y);
                color = lerp(color, _FogColor.rgb, fog * _FogIntensity);
            #endif
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
}
