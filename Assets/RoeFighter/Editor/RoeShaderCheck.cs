using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace RoeFighter.EditorTools
{
    /// <summary>
    /// Compiles every pass of the ROE shaders (vertex and fragment stage) for the keyword sets
    /// the game's materials really use, with and without the pipeline's lighting keywords, and
    /// prints the compiler messages.  Unity compiles shader variants lazily, so a broken
    /// variant would otherwise only show up as a pink character.
    /// Batch mode: -executeMethod RoeFighter.EditorTools.RoeShaderCheck.Run
    /// </summary>
    public static class RoeShaderCheck
    {
        static readonly string[][] PipelineKeywordSets =
        {
            new string[0],
            new[] { "_MAIN_LIGHT_SHADOWS_CASCADE", "_ADDITIONAL_LIGHTS", "_ADDITIONAL_LIGHT_SHADOWS", "_SHADOWS_SOFT",
                    "_SCREEN_SPACE_OCCLUSION", "_REFLECTION_PROBE_BLENDING", "_REFLECTION_PROBE_BOX_PROJECTION" },
            new[] { "_MAIN_LIGHT_SHADOWS", "_ADDITIONAL_LIGHTS", "_SHADOWS_SOFT_HIGH", "_CLUSTER_LIGHT_LOOP",
                    "_REFLECTION_PROBE_ATLAS", "_LIGHT_LAYERS", "_LIGHT_COOKIES", "FOG_EXP2" },
            new[] { "_MAIN_LIGHT_SHADOWS_SCREEN", "_CASTING_PUNCTUAL_LIGHT_SHADOW", "FOG_LINEAR" },
        };

        [MenuItem("ROE Fighter/Check/Compile ROE shaders")]
        public static void Run()
        {
            var shaders = AssetDatabase.FindAssets("t:Shader", new[] { "Assets/RoeFighter/Shaders" })
                .Select(g => AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(s => s != null).ToList();

            // distinct keyword sets per shader, taken from the game's materials
            var materialSets = new Dictionary<Shader, List<string[]>>();
            foreach (var shader in shaders)
                materialSets[shader] = new List<string[]> { new string[0] };
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/ROE" }))
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (mat == null || mat.shader == null || !materialSets.ContainsKey(mat.shader))
                    continue;
                var keys = mat.shaderKeywords.OrderBy(k => k).ToArray();
                if (!materialSets[mat.shader].Any(k => k.SequenceEqual(keys)))
                    materialSets[mat.shader].Add(keys);
            }

            int variants = 0, failed = 0;
            var seen = new HashSet<string>();
            foreach (var shader in shaders)
            {
                var data = ShaderUtil.GetShaderData(shader);
                Debug.Log($"[ROE] {shader.name}: supported={shader.isSupported} subshaders={data.SubshaderCount} " +
                          $"passes={data.GetSubshader(0).PassCount} material keyword sets={materialSets[shader].Count}");
                var sub = data.GetSubshader(0);
                for (int p = 0; p < sub.PassCount; p++)
                {
                    var pass = sub.GetPass(p);
                    foreach (var matKeys in materialSets[shader])
                    foreach (var pipeKeys in PipelineKeywordSets)
                    foreach (var stage in new[] { ShaderType.Vertex, ShaderType.Fragment })
                    {
                        var keywords = matKeys.Concat(pipeKeys).ToArray();
                        var info = pass.CompileVariant(stage, keywords, ShaderCompilerPlatform.D3D, BuildTarget.StandaloneWindows64);
                        variants++;
                        if (info.Success && info.Messages.Length == 0)
                            continue;
                        if (!info.Success) failed++;
                        foreach (var m in info.Messages)
                        {
                            string line = $"{shader.name} / {pass.Name} / {stage}: {m.severity} {m.message} ({m.file}:{m.line})";
                            if (seen.Add(line))
                                Debug.Log($"[ROE] {line}  keywords: {string.Join(" ", keywords)}");
                        }
                        if (!info.Success && info.Messages.Length == 0)
                            Debug.Log($"[ROE] {shader.name} / {pass.Name} / {stage}: FAILED without a message; keywords: {string.Join(" ", keywords)}");
                    }
                }
            }
            Debug.Log($"[ROE] shader check done: {shaders.Count} shaders, {variants} variants compiled, {failed} failed, {seen.Count} distinct messages");
        }
    }
}
