// Eyeballs of Rise of Eros characters.
// Stands in for the game's "Pinkcore/Heros/Eye"; the maths follows the game's compiled shader
// (tools/shader_asm.py).  The eye is composed in the eyeball's UV disc (centre = looking
// direction):
//   iris      where the iris mask's R says so; its albedo is read at a smaller scale than the
//             mask (lerp(_PupilMinSizeInv, _PupilMaxSizeInv, _PupilSizeScale)), which is what
//             enlarges the textures' tiny pupils, and shifted by the cornea's parallax (height
//             in the mask's G channel)
//   limbus    a ring around the pivot radius, its colour replaces the albedo
//   sclera    its own texture and bump map; the bump fades out over the iris
//   lighting  diffuse with the eyeball's own normal; a GGX highlight that the "Direct
//             Highlight" settings reshape into a crisp disc on the cornea, capped at
//             _SpecularTermMax; reflections at a dielectric's 4% rising at the silhouette;
//             the occlusion map marks the shaded rim in WHITE
Shader "ROE/Eye"
{
    Properties
    {
        [Header(Eye Albedo)] [Space(5)]
        _ScleraAlbedoTex ("Sclera Albedo Map", 2D) = "white" {}
        _ScleraColor ("Sclera Color", Color) = (0.95,0.95,0.95,1)
        _ScleraSizeInv ("Sclera Size (INV)", Range(0.5, 2)) = 1
        _IrisAlbedoTex ("Iris Albedo Map", 2D) = "white" {}
        _IrisMaskTex ("Iris Mask Map (R iris, G cornea height)", 2D) = "white" {}
        _IrisColor ("Iris Color", Color) = (1,1,1,1)
        _PupilMaxSizeInv ("Pupil Max Size (INV)", Range(0.35, 0.5)) = 0.4
        _PupilMinSizeInv ("Pupil Min Size (INV)", Range(0.75, 2)) = 1
        _PupilSizeScale ("Pupil Size Scale", Range(0, 1)) = 0
        _IrisLimbusScaleInv ("Iris Size (INV)", Range(0.5, 2)) = 1
        _LimbusPivot ("Limbus Pivot", Range(0, 1)) = 0.3
        _LimbusSizeSclera ("Limbal Ring Size Sclera", Range(0, 1)) = 1
        _LimbusSizeIris ("Limbal Ring Size Iris", Range(0, 1)) = 1
        _LimbusFadeRatio ("Limbal Fade Ratio", Range(1, 5)) = 1
        _LimbusPivotWidth ("Limbus Pivot Width", Range(0, 1)) = 0.5
        _LimbusColor ("Limbus Color", Color) = (0.2,0,0,1)

        [Header(Cornea Parallax)] [Space(5)]
        _IrisParallax ("Parallax Effect", Range(0, 0.1)) = 0.03

        [Header(Iris Illumination)] [Space(5)]
        [Toggle(_EMISSION)] _EnableEmission ("Iris Emission", Float) = 0
        [HDR] _IrisIllumColor ("Iris Emission Color", Color) = (0,0,0,1)

        [Header(Lighting Surface)] [Space(5)]
        _EyeBumpMap ("Sclera Bump Map", 2D) = "bump" {}
        _BumpStrength ("Sclera Bump Strength", Range(0, 1)) = 1
        _IrisClearCoatMetallic ("Iris ClearCoat Metallic", Range(0, 1)) = 1
        _ScleraMetallic ("Sclera Metallic", Range(0, 1)) = 0.1
        _IrisClearCoatSmoothness ("Iris ClearCoat Smoothness", Range(0, 1)) = 0.6
        _IrisClearCoatMask ("Iris ClearCoat Mask (unused by the game)", Range(0, 1)) = 0.3
        _ScleraSmoothness ("Sclera Smoothness", Range(0, 1)) = 0.1
        _EyeOcclusionMap ("Eye Occlusion Map (white = shaded)", 2D) = "white" {}
        _IrisOcclusionAmt ("Iris Occlusion Strength", Range(0, 1)) = 0
        _ScleraOcclusionAmt ("Sclera Occlusion Strength", Range(0, 1)) = 0
        _SpecularTermMax ("Specular Term Max", Range(0, 100)) = 10

        [Header(Specular Shape)] [Space(5)]
        _DirectHighlightSizeMultiplier ("Direct Highlight Size", Range(-1, 1)) = 0
        _DirectHighlightSharpen ("Direct Highlight Sharpness", Range(0, 1)) = 0
        _DirectHighlightThreshold ("Direct Highlight Specular Clipping Threshold", Range(0.01, 0.99)) = 0.9

        [Header(Other)] [Space(5)]
        [ToggleOff(_RECEIVE_SHADOWS_OFF)] _ReceiveShadows ("Receive Shadows", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "RoeCore.hlsl"

        TEXTURE2D(_ScleraAlbedoTex);    SAMPLER(sampler_ScleraAlbedoTex);
        TEXTURE2D(_IrisAlbedoTex);      SAMPLER(sampler_IrisAlbedoTex);
        TEXTURE2D(_IrisMaskTex);        SAMPLER(sampler_IrisMaskTex);
        TEXTURE2D(_EyeBumpMap);         SAMPLER(sampler_EyeBumpMap);
        TEXTURE2D(_EyeOcclusionMap);    SAMPLER(sampler_EyeOcclusionMap);

        CBUFFER_START(UnityPerMaterial)
            float4 _ScleraAlbedoTex_ST;
            float4 _IrisAlbedoTex_ST;
            float4 _IrisMaskTex_ST;
            float4 _EyeBumpMap_ST;
            float4 _EyeOcclusionMap_ST;
            half4 _ScleraColor;
            half4 _IrisColor;
            half4 _LimbusColor;
            half4 _IrisIllumColor;
            half _ScleraSizeInv;
            half _PupilMaxSizeInv;
            half _PupilMinSizeInv;
            half _PupilSizeScale;
            half _IrisLimbusScaleInv;
            half _LimbusPivot;
            half _LimbusSizeSclera;
            half _LimbusSizeIris;
            half _LimbusFadeRatio;
            half _LimbusPivotWidth;
            half _IrisParallax;
            half _BumpStrength;
            half _IrisClearCoatMetallic;
            half _ScleraMetallic;
            half _IrisClearCoatSmoothness;
            half _IrisClearCoatMask;
            half _ScleraSmoothness;
            half _IrisOcclusionAmt;
            half _ScleraOcclusionAmt;
            half _SpecularTermMax;
            half _DirectHighlightSizeMultiplier;
            half _DirectHighlightSharpen;
            half _DirectHighlightThreshold;
        CBUFFER_END

        void RoeClip(float2 uv, float4 positionCS) { }
        void RoeShadowClip(float2 uv) { }
        #define ROE_SHADOW_DEPTH_BIAS 1.0
        #define ROE_SHADOW_NORMAL_BIAS 1.0
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex RoeVert
            #pragma fragment Frag

            #pragma shader_feature_local _RECEIVE_SHADOWS_OFF
            #pragma shader_feature_local_fragment _EMISSION

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ REFLECTION_PROBE_ROTATION
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #pragma multi_compile_instancing

            struct RoeSurface
            {
                half3 diffuse;           // albedo x (1 - reflectivity)
                half3 specular;          // reflectance at normal incidence
                half3 normalWS;          // bumpy on the sclera, flat on the cornea
                half3 vertexNormalWS;    // the eyeball's own normal: diffuse N.L
                half irisMask;
                half roughness;          // perceptual roughness squared, at least 2^-7
            };

            half3 RoeShadeLight(RoeSurface s, InputData inputData, Light light)
            {
                half3 L = light.direction;
                half3 H = SafeNormalize(L + inputData.viewDirectionWS);
                half NoH = saturate(dot(s.normalWS, H));
                half LoH = saturate(dot(L, H));

                // On the cornea the highlight is reshaped into a crisp disc: N.H and L.H are
                // widened by the size setting, cut below the threshold and ramped back up.
                half size = _DirectHighlightSizeMultiplier + 1.0;
                half low = 0.5 - 0.5 * size;
                half cut = _DirectHighlightSharpen * (_DirectHighlightThreshold - 0.00001);
                half range = _DirectHighlightSharpen * (_DirectHighlightThreshold - 1.0) + 1.0 - cut;
                half NoHs = saturate((saturate(NoH * size + low) - cut) / range);
                half LoHs = saturate((saturate(LoH * size + low) - cut) / range);
                NoH = lerp(NoH, RoeSmooth01(NoHs), s.irisMask);
                LoH = lerp(LoH, RoeSmooth01(LoHs), s.irisMask);

                // URP's GGX term on the reshaped angles, capped
                half r2 = s.roughness * s.roughness;
                half d = NoH * NoH * (r2 - 1.0) + 1.00001;
                half specTerm = r2 / (d * d * max(0.1, LoH * LoH) * (s.roughness * 4.0 + 2.0));
                specTerm = min(specTerm, _SpecularTermMax);

                half3 radiance = light.color * (light.distanceAttenuation * light.shadowAttenuation);
                return (s.diffuse * saturate(dot(s.vertexNormalWS, L))
                        + s.specular * (specTerm * saturate(dot(s.normalWS, L)))) * radiance;
            }

            #define ROE_SURFACE_DIFFUSE(s) ((s).diffuse)
            #include "RoeLightLoop.hlsl"

            half4 Frag(RoeVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // Tangent frame from screen-space derivatives of position and UV: the eyeballs'
                // vertex tangents are not reliable, and a broken frame turns the whole eye black.
                half3 V = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float3 N = normalize(input.normalWS);
                float3 dp1 = ddx(input.positionWS);
                float3 dp2 = ddy(input.positionWS);
                float2 duv1 = ddx(input.uv);
                float2 duv2 = ddy(input.uv);
                float3 dp2perp = cross(dp2, N);
                float3 dp1perp = cross(N, dp1);
                float3 T = dp2perp * duv1.x + dp1perp * duv2.x;
                float3 B = dp2perp * duv1.y + dp1perp * duv2.y;
                float invmax = rsqrt(max(max(dot(T, T), dot(B, B)), 1e-20));
                half3x3 tangentToWorld = half3x3(T * invmax, B * invmax, N);

                float2 p = input.uv - 0.5;

                // limbal ring: the squared distance from the centre, stretched by the fade ratio
                // around the pivot radius, ramped in from the iris side and out to the sclera side
                float pivot = _LimbusPivot * 0.5;
                float pivot2 = pivot * pivot;
                float width = _LimbusPivotWidth * 0.01;
                float d2 = (dot(p, p) - pivot2) * _LimbusFadeRatio + pivot2;
                float inStart = (pivot2 - 0.0001 - width) * (1.0 - _LimbusSizeIris);
                float inEnd = pivot2 - width;
                float outEnd = lerp(pivot2 + width + 0.0001, 0.25, _LimbusSizeSclera);
                half ringIn = RoeSmooth01(saturate((d2 - inStart) / (inEnd - inStart)));
                half ringOut = RoeSmooth01(saturate((d2 - outEnd) / (pivot2 + width - outEnd)));
                half ring = ringIn + ringOut - 1.0;

                // iris, seen through the cornea
                half3 viewTS = normalize(half3(dot(tangentToWorld[0], V), dot(tangentToWorld[1], V), dot(N, V)));
                float2 offset = viewTS.xy / (viewTS.z + 0.42);
                half2 mask = SAMPLE_TEXTURE2D(_IrisMaskTex, sampler_IrisMaskTex, p * _IrisLimbusScaleInv + 0.5).rg;
                half irisW = mask.r;
                float height = mask.g * _IrisParallax;
                float2 pp = p - height * offset;
                float pupilScale = lerp(_PupilMinSizeInv, _PupilMaxSizeInv, _PupilSizeScale);
                float2 irisUV = pp * (pupilScale * _IrisLimbusScaleInv) + 0.5 - height * offset;
                half3 iris = SAMPLE_TEXTURE2D(_IrisAlbedoTex, sampler_IrisAlbedoTex, irisUV).rgb;
                iris = lerp(iris, iris * _IrisColor.rgb, _IrisColor.a);

                float2 scleraUV = p * _ScleraSizeInv + 0.5;
                half3 sclera = SAMPLE_TEXTURE2D(_ScleraAlbedoTex, sampler_ScleraAlbedoTex, scleraUV).rgb;
                sclera = lerp(sclera, sclera * _ScleraColor.rgb, _ScleraColor.a);

                half3 albedo = lerp(sclera, iris, irisW);
                albedo = lerp(albedo, _LimbusColor.rgb, ring);
                half metallic = lerp(_ScleraMetallic, _IrisClearCoatMetallic, irisW);
                half smoothness = lerp(_ScleraSmoothness, _IrisClearCoatSmoothness, irisW);

                // the sclera's bump, flat over the iris
                half3 bumpTS = RoeUnpackNormalLerp(SAMPLE_TEXTURE2D(_EyeBumpMap, sampler_EyeBumpMap, scleraUV), _BumpStrength);
                half3 normalTS = lerp(bumpTS, half3(0, 0, 1), irisW);

                InputData inputData;
                RoeInitInputData(input, tangentToWorld, normalTS, inputData);
                inputData.bakedGI = SampleSH(N);    // the game reads the probes per vertex

                RoeSurface s;
                s.diffuse = albedo * (kDielectricSpec.a - kDielectricSpec.a * metallic);
                s.specular = lerp(kDielectricSpec.rgb, albedo, metallic);
                s.normalWS = inputData.normalWS;
                s.vertexNormalWS = N;
                s.irisMask = irisW;
                half perceptualRoughness = 1.0 - smoothness;
                s.roughness = max(perceptualRoughness * perceptualRoughness, HALF_MIN_SQRT);

                // the occlusion map is white where the eyelids shade the eyeball
                half ao = SAMPLE_TEXTURE2D(_EyeOcclusionMap, sampler_EyeOcclusionMap, input.uv).r;
                half occlusion = 1.0 - ao * lerp(_ScleraOcclusionAmt, _IrisOcclusionAmt, irisW);

                // reflections: a dielectric's 4% rising to the grazing term at the silhouette,
                // whatever the metallic value
                half NoV = saturate(dot(s.normalWS, V));
                half grazing = saturate(smoothness + 0.04);
                half envBRDF = lerp(0.04, grazing, Pow4(1.0 - NoV)) / (s.roughness * s.roughness + 1.0);
                half3 env = GlossyEnvironmentReflection(reflect(-V, s.normalWS), inputData.positionWS,
                                                        perceptualRoughness, half(1.0), inputData.normalizedScreenSpaceUV);
                half3 color = (env * envBRDF + inputData.bakedGI * s.diffuse) * occlusion;

                AmbientOcclusionFactor aoFactor;
                aoFactor.directAmbientOcclusion = 1.0;
                aoFactor.indirectAmbientOcclusion = occlusion;
                color += RoeDirectLighting(s, inputData, aoFactor);

            #if defined(_EMISSION)
                color += iris * _IrisIllumColor.rgb * irisW;
            #endif

                color = MixFog(color, inputData.fogCoord);
                return half4(color, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex RoeDepthVert
            #pragma fragment RoeDepthFrag
            #pragma multi_compile_instancing
            #include "RoePasses.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex RoeDepthVert
            #pragma fragment RoeDepthNormalsFrag
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #pragma multi_compile_instancing
            #include "RoePasses.hlsl"
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
