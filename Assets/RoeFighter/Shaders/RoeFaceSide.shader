// Check pictures (RoeBurstBuilder.Check -roeFaces 1): which side of each triangle the camera sees.
//   grey     the front (the side its winding faces), its normal towards the camera - lit as modelled
//   yellow   the front, but its normal turned away from the camera (URP Lit shades it from behind: dark)
//   magenta  the back of the triangle (a two-sided material's inside; URP Lit shades it with the front's normal: dark,
//            where Unreal and RoeCharacter turn the normal round)
// Shaded by a fixed light from above and in front, so the shape reads.
Shader "Hidden/ROE/FaceSide"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Pass
        {
            Name "FaceSide"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite On
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 positionWS : TEXCOORD1; };

            Varyings Vert(Attributes a)
            {
                Varyings v;
                v.positionWS = TransformObjectToWorld(a.positionOS.xyz);
                v.positionCS = TransformWorldToHClip(v.positionWS);
                v.normalWS = TransformObjectToWorldNormal(a.normalOS);
                return v;
            }

            half4 Frag(Varyings v, bool front : SV_IsFrontFace) : SV_Target
            {
                float3 n = normalize(v.normalWS);
                float3 toCamera = normalize(GetCameraPositionWS() - v.positionWS);
                float3 shadeNormal = dot(n, toCamera) < 0 ? -n : n;
                float shade = 0.35 + 0.65 * saturate(dot(shadeNormal, normalize(toCamera + float3(0, 0.6, 0))));
                if (!front)
                    return half4(shade * 0.95, shade * 0.1, shade * 0.85, 1);
                if (dot(n, toCamera) < 0)
                    return half4(shade * 0.95, shade * 0.85, shade * 0.1, 1);
                return half4(shade.xxx * 0.85, 1);
            }
            ENDHLSL
        }
    }
}
