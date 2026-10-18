// Rebuild of the game's "Pinkcore/Particles/Default".
//   uv    = uv0.xy * _BaseMap_ST.xy + uv0.zw * _Speed.xy     (uv0.zw = the particle system's custom data: a UV scroll)
//   alpha = texture.a * vertex.a   [clip against _Cutoff]
//   rgb   = texture.rgb * vertex.rgb + _Emissive.rgb
Shader "ROE/Particles/Default"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        [HDR] _Emissive ("Emissive", Color) = (0, 0, 0, 1)
        _Speed ("Speed", Vector) = (0, 0, 0, 0)
        [Toggle(_ALPHATEST_ON)] _AlphaTest ("Alpha Test", Float) = 0
        _Cutoff ("Cutoff", Range(0, 1)) = 0
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
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float _Cutoff;
                float2 _Speed;
                half4 _Emissive;
                float _Cull;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float4 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : TEXCOORD1;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.uv = input.uv.xy * _BaseMap_ST.xy + input.uv.zw * _Speed.xy;
                output.color = input.color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 c = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half alpha = c.a * input.color.a;
                #if defined(_ALPHATEST_ON)
                    clip(alpha - _Cutoff);
                #endif
                return half4(c.rgb * input.color.rgb + _Emissive.rgb, alpha);
            }
            ENDHLSL
        }
    }
}
