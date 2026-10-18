// Rebuild of the game's "Pinkcore/Particles/Dissolve".
//   base uv     = uv0.xy * _BaseMap_ST.xy     + uv0.zw * _MainSpeed.xy       (uv0.zw, uv1.xy = custom data)
//   dissolve uv = uv0.xy * _DissolveMap_ST.xy + uv1.xy * _DissolveSpeed.xy
//   the vertex alpha is the dissolve amount: a pixel is cut when dissolveMap.r * 0.999 < 1 - vertex.a
//   rgb = texture.rgb * vertex.rgb + _Emissive.rgb, alpha = texture.a (NOT times the vertex alpha)
Shader "ROE/Particles/Dissolve"
{
    Properties
    {
        [HDR] _Emissive ("Emissive", Color) = (0, 0, 0, 0)
        _BaseMap ("Base Map", 2D) = "white" {}
        _MainSpeed ("Main Speed", Vector) = (0, 0, 0, 0)
        _DissolveMap ("Dissolve Map", 2D) = "white" {}
        _DissolveSpeed ("Dissolve Speed", Vector) = (0, 0, 0, 0)
        [Toggle(_ALPHATEST_ON)] _AlphaTest ("Alpha Test", Float) = 1
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
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull [_Cull]
            ColorMask RGB

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #include "RoeParticles.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_DissolveMap); SAMPLER(sampler_DissolveMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _Emissive;
                float4 _DissolveMap_ST;
                float2 _MainSpeed;
                float2 _DissolveSpeed;
                float _Cull;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float4 uv : TEXCOORD0;
                float2 custom : TEXCOORD1;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 dissolveUV : TEXCOORD1;
                half4 color : TEXCOORD2;        // a = 1 - vertex alpha = the dissolve threshold
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.uv = input.uv.xy * _BaseMap_ST.xy + input.uv.zw * _MainSpeed.xy;
                output.dissolveUV = input.uv.xy * _DissolveMap_ST.xy + input.custom.xy * _DissolveSpeed.xy;
                output.color = half4(input.color.rgb, 1.0 - input.color.a);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                #if defined(_ALPHATEST_ON)
                    half dissolve = SAMPLE_TEXTURE2D(_DissolveMap, sampler_DissolveMap, input.dissolveUV).r;
                    clip(dissolve * 0.999 - input.color.a);
                #endif
                half4 c = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                return half4(c.rgb * input.color.rgb + _Emissive.rgb, c.a);
            }
            ENDHLSL
        }
    }
}
