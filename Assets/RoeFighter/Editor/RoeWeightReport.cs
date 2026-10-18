using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace RoeFighter.EditorTools
{
    /// <summary>
    /// Skin weights of the fighters' meshes.  Per mesh: influences per vertex, weight sums,
    /// vertices without weight, missing bones.  Per bone: how much skin it carries and what kind
    /// of bone it is - a human bone the humanoid animation drives, a helper hanging under one
    /// (twist and joint helpers: the game's clips animate them, motion capture does not), a node
    /// above the hips, or a chain (hair, skirt, breasts).
    ///   -executeMethod RoeFighter.EditorTools.RoeWeightReport.Run [-roeChars a08,g04]
    /// </summary>
    public static class RoeWeightReport
    {
        [MenuItem("ROE Fighter/Checks/Skin weight report")]
        public static void Run()
        {
            foreach (var id in RoeCapture.Arg("-roeChars", "a08,g04").Split(','))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RoeHumanoid.FighterPath(id));
                if (prefab == null)
                {
                    Debug.LogError($"[ROE] weights: no fighter prefab for {id}");
                    continue;
                }
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                Debug.Log(Report(id, go));
                Object.DestroyImmediate(go);
            }
        }

        static string Report(string id, GameObject go)
        {
            var animator = go.GetComponent<Animator>();
            var human = new Dictionary<Transform, string>();
            foreach (HumanBodyBones hb in System.Enum.GetValues(typeof(HumanBodyBones)))
            {
                if (hb == HumanBodyBones.LastBone)
                    continue;
                var t = animator.GetBoneTransform(hb);
                if (t != null)
                    human[t] = hb.ToString();
            }
            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);

            string Kind(Transform b)
            {
                if (human.TryGetValue(b, out var h))
                    return "human " + h;
                if (!b.IsChildOf(hips))
                    return "above/outside the hips";
                for (var t = b.parent; t != null; t = t.parent)
                    if (human.TryGetValue(t, out var owner))
                        return "under " + owner;
                return "?";
            }

            var sb = new StringBuilder($"[ROE] skin weights of {id}:");
            var carried = new Dictionary<Transform, (int verts, float weight)>();
            foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = r.sharedMesh;
                if (mesh == null)
                    continue;
                var counts = mesh.GetBonesPerVertex();
                var weights = mesh.GetAllBoneWeights();
                var bones = r.bones;
                int maxPer = 0, over4 = 0, unweighted = 0, offSum = 0, badIndex = 0, nullBones = bones.Count(b => b == null);
                float lowestSum = float.MaxValue;
                int at = 0;
                for (int v = 0; v < counts.Length; v++)
                {
                    int n = counts[v];
                    maxPer = Mathf.Max(maxPer, n);
                    if (n > 4)
                        over4++;
                    float sum = 0f;
                    for (int k = 0; k < n; k++)
                    {
                        var w = weights[at + k];
                        sum += w.weight;
                        if (w.boneIndex < 0 || w.boneIndex >= bones.Length || bones[w.boneIndex] == null)
                        {
                            badIndex++;
                            continue;
                        }
                        var b = bones[w.boneIndex];
                        carried.TryGetValue(b, out var c);
                        carried[b] = (c.verts + (w.weight > 0.05f ? 1 : 0), c.weight + w.weight);
                    }
                    at += n;
                    if (n == 0 || sum < 1e-4f)
                        unweighted++;
                    else if (Mathf.Abs(sum - 1f) > 0.01f)
                        offSum++;
                    lowestSum = Mathf.Min(lowestSum, sum);
                }
                sb.Append($"\n[ROE]   mesh {r.name}: {mesh.vertexCount} verts, {bones.Length} bones ({nullBones} missing), " +
                          $"up to {maxPer} influences per vertex ({over4} verts over 4), {unweighted} verts without weight, " +
                          $"{offSum} verts whose weights do not sum to 1 (lowest sum {lowestSum:F3}), {badIndex} weights on missing bones; " +
                          $"root bone {(r.rootBone ? r.rootBone.name : "none")}, quality {r.quality}, update when offscreen {r.updateWhenOffscreen}");
            }

            // the bones that carry skin, grouped by kind; the limb helpers in full
            var groups = carried.Where(kv => kv.Value.verts > 0).GroupBy(kv => Kind(kv.Key)).OrderBy(g => g.Key);
            string[] limbs = { "LeftUpperArm", "LeftLowerArm", "LeftHand", "RightUpperArm", "RightLowerArm", "RightHand",
                               "LeftUpperLeg", "LeftLowerLeg", "LeftFoot", "LeftToes", "RightUpperLeg", "RightLowerLeg", "RightFoot", "RightToes",
                               "LeftShoulder", "RightShoulder" };
            foreach (var g in groups)
            {
                int verts = g.Sum(kv => kv.Value.verts);
                bool detail = g.Key.StartsWith("above") || limbs.Any(l => g.Key == "under " + l || g.Key == "human " + l);
                sb.Append($"\n[ROE]   {g.Key}: {g.Count()} bones, {verts} verts" + (detail || g.Count() <= 3
                    ? ": " + string.Join(", ", g.OrderByDescending(kv => kv.Value.verts).Select(kv =>
                        $"{kv.Key.name} {kv.Value.verts}{(human.ContainsKey(kv.Key) ? "" : $" (parent {kv.Key.parent.name}, {Vector3.Distance(kv.Key.position, kv.Key.parent.position):F2} m from it)")}"))
                    : ""));
            }
            return sb.ToString();
        }
    }
}
