using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RoeFighter.Fight;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RoeFighter.EditorTools
{
    /// <summary>
    /// Magica Cloth 2 in this project (user 10-04: "magic cloth2 下载到E:\Downloads\布料模拟插件 Magica Cloth 2 了，看看能不能用"):
    /// the package goes to Assets/MagicaCloth2 (gitignored, never committed), and on load it adds the define MAGICACLOTH2 to
    /// the player settings itself, which switches RoeMagicaCloth (the "magica" solver and setup on F4) on.
    ///   -executeMethod RoeFighter.EditorTools.RoeMagicaSetup.Check     what is installed, and whether the define is there
    /// </summary>
    public static class RoeMagicaSetup
    {
        public static void Check()
        {
            var cloth = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("MagicaCloth2.MagicaCloth", false)).FirstOrDefault(t => t != null);
            var target = NamedBuildTarget.FromBuildTargetGroup(EditorUserBuildSettings.selectedBuildTargetGroup);
            PlayerSettings.GetScriptingDefineSymbols(target, out string[] defines);
            bool solver = RoeClothSolvers.Find("magica") != null;
            string readme = System.IO.File.Exists("Assets/MagicaCloth2/Readme.txt") ? "Assets/MagicaCloth2 present" : "Assets/MagicaCloth2 missing";
            Debug.Log($"[ROE] Magica Cloth 2: {readme}; assembly {(cloth != null ? cloth.Assembly.GetName().Name : "not loaded")}; " +
                      $"defines ({target.TargetName}) {string.Join(";", defines)}; solver \"magica\" {(solver ? "on" : "off")}; " +
                      $"setups {string.Join(" ", RoeClothBackends.All.Select(b => b.name))}");
        }

        /// <summary>
        /// The outfit's cloth pieces as meshes of their own (the 爆衣 split, RoeClothesBurst): per piece its renderer, vertices,
        /// whether the mesh is readable (Magica's MeshCloth needs it), and how its vertices hang on the bones - per bone the
        /// vertices it carries most of, their height range in the stance, the bone's parent.
        ///   -executeMethod RoeFighter.EditorTools.RoeMagicaSetup.Pieces [-roeChar a08]
        /// </summary>
        public static void Pieces()
        {
            string id = RoeCapture.Arg("-roeChar", "a08");
            EditorSceneManager.OpenScene(RoeFightScene.ScenePath(RoeCapture.Arg("-roeStage", "e23_steel_s02")), OpenSceneMode.Single);
            var game = Object.FindFirstObjectByType<FightGame>();
            if (game.roster.Length > 0 && game.roster[game.pick[0]].id != id && game.roster[game.pick[1]].id != id)
                game.PickIds(null, id);
            game.Setup();
            var rig = game.rigs.First(r => r != null && r.id == id);
            var sb = new StringBuilder($"[ROE] {id}: cloth pieces of the outfit");
            if (rig.burst == null)
                sb.Append(": none (no 爆衣 split)");
            var baked = new Mesh();
            foreach (var p in rig.burst != null ? rig.burst.pieces : new List<RoeClothesBurst.Piece>())
            {
                var smr = p.renderer;
                var mesh = smr != null ? smr.sharedMesh : null;
                if (mesh == null)
                    continue;
                sb.Append($"\n[ROE]   {(p.cloth ? "CLOTH" : "armour")} {p.name} stage {p.stage}: {smr.name}, {mesh.vertexCount} vertices, " +
                          $"readable {mesh.isReadable}, {mesh.subMeshCount} submeshes, {smr.bones.Length} bones");
                if (!p.cloth || !mesh.isReadable)
                    continue;
                smr.BakeMesh(baked, true);
                var v = baked.vertices;
                var toWorld = smr.transform.localToWorldMatrix;
                var w = mesh.boneWeights;
                var bones = smr.bones;
                var per = new Dictionary<int, (int n, float lo, float hi, float wsum)>();
                for (int i = 0; i < v.Length; i++)
                {
                    float y = toWorld.MultiplyPoint3x4(v[i]).y;
                    int b = w[i].boneIndex0;
                    var e = per.TryGetValue(b, out var x) ? x : (0, float.MaxValue, float.MinValue, 0f);
                    per[b] = (e.Item1 + 1, Mathf.Min(e.Item2, y), Mathf.Max(e.Item3, y), e.Item4 + w[i].weight0);
                }
                foreach (var kv in per.OrderByDescending(kv => kv.Value.hi))
                {
                    var bone = kv.Key < bones.Length ? bones[kv.Key] : null;
                    sb.Append($"\n[ROE]     {(bone != null ? bone.name : "?")} (on {(bone != null && bone.parent != null ? bone.parent.name : "-")}): " +
                              $"{kv.Value.n} vertices, y {kv.Value.lo:F2}..{kv.Value.hi:F2}, mean top weight {kv.Value.wsum / kv.Value.n:F2}");
                }
            }
            Object.DestroyImmediate(baked);
            // the skin the checks measure cloth against (RoeBodySurface), and why vertices are left out of it
            var animator = rig.animator;
            var thighs = new[] { animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg), animator.GetBoneTransform(HumanBodyBones.RightUpperLeg) };
            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            sb.Append($"\n[ROE]   body surface: {new RoeBodySurface(animator).Count} points; hips {hips?.name} y {hips?.position.y:F2}, " +
                      $"thighs {thighs[0]?.name}/{thighs[1]?.name}, model y {animator.transform.position.y:F2}");
            var mesh2 = new Mesh();
            foreach (var smr in animator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.sharedMesh == null)
                    continue;
                smr.BakeMesh(mesh2, true);
                var w = smr.sharedMesh.boneWeights;
                var bones = smr.bones;
                int legs = 0, inRange = 0;
                var toWorld = smr.transform.localToWorldMatrix;
                var v = mesh2.vertices;
                for (int i = 0; i < v.Length && i < w.Length; i++)
                {
                    var b = w[i].boneIndex0 < bones.Length ? bones[w[i].boneIndex0] : null;
                    if (b != null && thighs.Any(t => t != null && b.IsChildOf(t)))
                        legs++;
                    float y = toWorld.MultiplyPoint3x4(v[i]).y;
                    if (hips != null && y < hips.position.y + 0.15f && y > animator.transform.position.y + 0.01f)
                        inRange++;
                }
                float lo = v.Length > 0 ? v.Min(p => toWorld.MultiplyPoint3x4(p).y) : 0f, hi = v.Length > 0 ? v.Max(p => toWorld.MultiplyPoint3x4(p).y) : 0f;
                float lo2 = v.Length > 0 ? v.Min(p => smr.transform.TransformPoint(p).y) : 0f, hi2 = v.Length > 0 ? v.Max(p => smr.transform.TransformPoint(p).y) : 0f;
                sb.Append($"\n[ROE]     {smr.name}: {smr.sharedMesh.vertexCount} vertices (baked {v.Length}, weights {w.Length}, normals {mesh2.normals.Length}), " +
                          $"readable {smr.sharedMesh.isReadable}, on leg bones {legs}, between floor and hips {inRange}; scale {smr.transform.lossyScale.x:F3}, " +
                          $"y {lo:F2}..{hi:F2} (TRS) / {lo2:F2}..{hi2:F2} (TransformPoint)");
            }
            Object.DestroyImmediate(mesh2);
            Debug.Log(sb.ToString());
        }
    }
}
