using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using RoeFighter.Fight;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RoeFighter.EditorTools
{
    /// <summary>
    /// Which of her vertices hang on both the skirt and the legs (user 10-11: "luf的腿部权重是否有问题，似乎和裙子有粘连"):
    /// for every skinned renderer of the fighter and each of its submeshes (by material), the vertices with weight on skirt
    /// bones (RoeBoneCloth's name rules), on leg bones (thighs, calves, feet, their twists and the knee / buttock helpers)
    /// and on both.  Every vertex also goes to _work/weights/&lt;id&gt;.tsv (bind place in the model root's frame, renderer,
    /// material, its bones and weights) for a look outside Unity.
    ///   -executeMethod RoeFighter.EditorTools.RoeWeightProbe.SkirtLegs -roeChar g04
    /// </summary>
    public static class RoeWeightProbe
    {
        static readonly Regex Leg = new Regex(@"thigh|calf|foot|toe|knee|butt", RegexOptions.IgnoreCase);

        public static void SkirtLegs()
        {
            var inv = CultureInfo.InvariantCulture;
            string id = RoeCapture.Arg("-roeChar", "g04");
            string stage = RoeCapture.Arg("-roeStage", "e23_steel_s02");
            string outDir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "_work", "weights");
            Directory.CreateDirectory(outDir);
            EditorSceneManager.OpenScene(RoeFightScene.ScenePath(stage), OpenSceneMode.Single);
            var rig = Object.FindObjectsByType<FighterRig>(FindObjectsInactive.Include, FindObjectsSortMode.None).First(r => r.id == id);
            var root = rig.animator.transform;
            var dump = new StringBuilder("renderer\tmaterial\tsub\tvertex\tx\ty\tz\tskirt\tleg\tpelvis\tbones\n");
            var log = new StringBuilder($"[ROE] skirt and leg weights of {id}: per renderer / material - vertices, on skirt bones, on leg bones, on both (each > 5 %), on both strongly (each >= 20 %)");
            foreach (var smr in rig.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r => r.sharedMesh != null))
            {
                var mesh = smr.sharedMesh;
                if (!mesh.isReadable)
                {
                    log.Append($"\n[ROE]   {smr.name}: mesh not readable");
                    continue;
                }
                var bones = smr.bones;
                var verts = mesh.vertices;
                var perVertex = mesh.GetBonesPerVertex();
                var weights = mesh.GetAllBoneWeights();
                var start = new int[mesh.vertexCount + 1];
                for (int i = 0; i < mesh.vertexCount; i++)
                    start[i + 1] = start[i] + perVertex[i];
                var toRoot = root.worldToLocalMatrix * smr.transform.localToWorldMatrix;
                var mats = smr.sharedMaterials;
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    var used = new HashSet<int>(mesh.GetTriangles(sub));
                    int n = 0, onSkirt = 0, onLeg = 0, both = 0, bothStrong = 0;
                    var mixedBones = new Dictionary<string, int>();
                    foreach (int v in used.OrderBy(x => x))
                    {
                        float skirt = 0f, leg = 0f, pelvis = 0f;
                        var names = new List<string>();
                        for (int k = start[v]; k < start[v + 1]; k++)
                        {
                            var w = weights[k];
                            var b = w.boneIndex < bones.Length ? bones[w.boneIndex] : null;
                            string name = b != null ? b.name : "?";
                            names.Add($"{name}:{w.weight.ToString("F3", inv)}");
                            if (RoeBoneCloth.RoeKindOf(name) == "skirt")
                                skirt += w.weight;
                            else if (Leg.IsMatch(name))
                                leg += w.weight;
                            else if (name.EndsWith("Pelvis"))
                                pelvis += w.weight;
                        }
                        n++;
                        if (skirt > 0.05f)
                            onSkirt++;
                        if (leg > 0.05f)
                            onLeg++;
                        if (skirt > 0.05f && leg > 0.05f)
                        {
                            both++;
                            for (int k = start[v]; k < start[v + 1]; k++)
                            {
                                var b = weights[k].boneIndex < bones.Length ? bones[weights[k].boneIndex] : null;
                                if (b != null && Leg.IsMatch(b.name) && weights[k].weight > 0.05f)
                                    mixedBones[b.name] = mixedBones.TryGetValue(b.name, out int c) ? c + 1 : 1;
                            }
                        }
                        if (skirt >= 0.2f && leg >= 0.2f)
                            bothStrong++;
                        var p = toRoot.MultiplyPoint3x4(verts[v]);
                        string mat = sub < mats.Length && mats[sub] != null ? mats[sub].name : "-";
                        dump.Append($"{smr.name}\t{mat}\t{sub}\t{v}\t{p.x.ToString("F4", inv)}\t{p.y.ToString("F4", inv)}\t{p.z.ToString("F4", inv)}\t" +
                                    $"{skirt.ToString("F3", inv)}\t{leg.ToString("F3", inv)}\t{pelvis.ToString("F3", inv)}\t{string.Join(" ", names)}\n");
                    }
                    string m = sub < mats.Length && mats[sub] != null ? mats[sub].name : "-";
                    log.Append($"\n[ROE]   {smr.name} / {m}: {n} vertices, skirt {onSkirt}, legs {onLeg}, both {both}, both strongly {bothStrong}" +
                               (mixedBones.Count > 0 ? "; leg bones in the mixed ones: " + string.Join(", ", mixedBones.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value}")) : ""));
                }
            }
            File.WriteAllText(Path.Combine(outDir, id + ".tsv"), dump.ToString());
            Debug.Log(log.ToString());
        }
    }
}
