using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace RoeFighter.EditorTools
{
    /// <summary>
    /// One-time project setup: linear colour, a URP asset tuned for close-up characters
    /// (4K soft shadows, MSAA for the hair cut-outs, SSAO), assigned to Graphics and Quality.
    /// Menu: ROE Fighter > Setup.  Batch mode: -executeMethod RoeFighter.EditorTools.RoeProjectSetup.Run
    /// </summary>
    public static class RoeProjectSetup
    {
        public const string SettingsDir = "Assets/RoeFighter/Settings";
        public const string PipelinePath = SettingsDir + "/RoeURP.asset";
        public const string RendererPath = SettingsDir + "/RoeURP_Renderer.asset";

        [MenuItem("ROE Fighter/Setup/Render pipeline and quality")]
        public static void Run()
        {
            Directory.CreateDirectory(SettingsDir);
            PlayerSettings.colorSpace = ColorSpace.Linear;

            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (renderer == null)
                renderer = CreateRenderer(RendererPath);
            EnsureSsao(renderer);

            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (pipeline == null)
            {
                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(pipeline, PipelinePath);
            }
            Configure(pipeline);

            GraphicsSettings.defaultRenderPipeline = pipeline;
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = pipeline;
                QualitySettings.skinWeights = SkinWeights.FourBones;
                QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
                QualitySettings.vSyncCount = 0;
            }
            QualitySettings.SetQualityLevel(current, false);

            EditorUtility.SetDirty(pipeline);
            EditorUtility.SetDirty(renderer);
            AssetDatabase.SaveAssets();
            Debug.Log($"[ROE] render pipeline ready: {PipelinePath}, colour space {PlayerSettings.colorSpace}");
        }

        static UniversalRendererData CreateRenderer(string path)
        {
            // URP keeps this helper internal; it fills in the post-process data and shader references.
            var method = typeof(UniversalRenderPipelineAsset).GetMethod("CreateRendererAsset",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (method != null)
            {
                var data = method.Invoke(null, new object[] { path, RendererType.UniversalRenderer, false, "Renderer" });
                if (data is UniversalRendererData made)
                    return made;
            }
            throw new InvalidOperationException(
                "UniversalRenderPipelineAsset.CreateRendererAsset is gone in this URP version; create the renderer by hand: " +
                "Assets > Create > Rendering > URP Universal Renderer, save it as " + path);
        }

        static void EnsureSsao(UniversalRendererData renderer)
        {
            foreach (var f in renderer.rendererFeatures)
                if (f is ScreenSpaceAmbientOcclusion)
                    return;
            var ssao = ScriptableObject.CreateInstance<ScreenSpaceAmbientOcclusion>();
            ssao.name = "ScreenSpaceAmbientOcclusion";
            AssetDatabase.AddObjectToAsset(ssao, renderer);
            renderer.rendererFeatures.Add(ssao);

            var so = new SerializedObject(ssao);
            Set(so, "m_Settings.Intensity", 1.6f);
            Set(so, "m_Settings.Radius", 0.12f);
            Set(so, "m_Settings.Falloff", 100f);
            Set(so, "m_Settings.DirectLightingStrength", 0.35f);
            Set(so, "m_Settings.Samples", 0);          // High
            Set(so, "m_Settings.Source", 1);           // DepthNormals
            Set(so, "m_Settings.AfterOpaque", false);
            so.ApplyModifiedPropertiesWithoutUndo();

            // keep the feature map in sync (URP does this in OnValidate)
            var validate = typeof(ScriptableRendererData).GetMethod("ValidateRendererFeatures",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            AssetDatabase.SaveAssets();
            validate?.Invoke(renderer, null);
            renderer.SetDirty();
        }

        static void Configure(UniversalRenderPipelineAsset pipeline)
        {
            var so = new SerializedObject(pipeline);
            Set(so, "m_SupportsHDR", true);
            Set(so, "m_MSAA", 4);
            Set(so, "m_RenderScale", 1f);
            Set(so, "m_RequireDepthTexture", true);
            Set(so, "m_RequireOpaqueTexture", true);             // the picture behind an effect: refraction particles read it
            Set(so, "m_OpaqueDownsampling", 0);                  // at full size
            Set(so, "m_MainLightRenderingMode", 1);              // per pixel
            Set(so, "m_MainLightShadowsSupported", true);
            Set(so, "m_MainLightShadowmapResolution", 4096);
            Set(so, "m_AdditionalLightsRenderingMode", 1);       // per pixel
            Set(so, "m_AdditionalLightsPerObjectLimit", 8);
            Set(so, "m_AdditionalLightShadowsSupported", true);
            Set(so, "m_AdditionalLightsShadowmapResolution", 2048);
            Set(so, "m_ShadowDistance", 30f);
            Set(so, "m_ShadowCascadeCount", 4);
            Set(so, "m_SoftShadowsSupported", true);
            Set(so, "m_SoftShadowQuality", 3);                   // high
            Set(so, "m_ShadowDepthBias", 0.6f);
            Set(so, "m_ShadowNormalBias", 0.6f);
            Set(so, "m_ReflectionProbeBlending", true);
            Set(so, "m_ReflectionProbeBoxProjection", true);
            Set(so, "m_ColorGradingMode", 1);                    // HDR grading
            Set(so, "m_ColorGradingLutSize", 32);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void Set(SerializedObject so, string name, object value)
        {
            var p = so.FindProperty(name);
            if (p == null)
            {
                Debug.LogWarning($"[ROE] {so.targetObject.GetType().Name} has no serialized field '{name}'");
                return;
            }
            switch (value)
            {
                case bool b: p.boolValue = b; break;
                case int i:
                    if (p.propertyType == SerializedPropertyType.Enum) p.intValue = i;
                    else p.intValue = i;
                    break;
                case float f: p.floatValue = f; break;
                default: throw new ArgumentException(name);
            }
        }
    }
}
