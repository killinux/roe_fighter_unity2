using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace RoeFighter.EditorTools
{
    /// <summary>
    /// How do the weapon nodes of each outfit move in its own clips, and which hand are they rigid to?
    /// For every node outside the Bip001 body (and under the Biped prop bones): the range of its
    /// world position over all battle clips, and how constant its transform is relative to each hand.
    /// Batch mode: -executeMethod RoeFighter.EditorTools.RoeWeaponReport.Run
    /// </summary>
    public static class RoeWeaponReport
    {
        [MenuItem("ROE Fighter/Check/Weapon report")]
        public static void Run()
        {
            var manifest = RoeManifest.Load();
            foreach (var c in manifest.WithModels)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RoeFighterBuilder.PrefabPath(c.id));
                if (prefab == null)
                    continue;
                var go = Object.Instantiate(prefab);
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var map = RoeHumanoid.MapBones(go);
                var nodes = new List<Transform>();
                foreach (var att in RoeHumanoid.Attachments(go, map))
                    nodes.AddRange(att.GetComponentsInChildren<Transform>(true));
                var skinned = new HashSet<Transform>(go.GetComponentsInChildren<SkinnedMeshRenderer>(true).SelectMany(r => r.bones).Where(b => b != null));
                var hands = new[] { map["LeftHand"], map["RightHand"] };

                var lo = nodes.ToDictionary(n => n, n => Vector3.positiveInfinity);
                var hi = nodes.ToDictionary(n => n, n => Vector3.negativeInfinity);
                var rel = nodes.ToDictionary(n => n, n => new[] { new List<Vector3>(), new List<Vector3>() });
                foreach (var name in new[] { "idle_01", "skill_01", "skill_02", "skill_03", "hurt" })
                {
                    var clip = c.LoadClip(name);
                    if (clip == null)
                        continue;
                    int frames = Mathf.RoundToInt(clip.length * 15f);
                    for (int f = 0; f <= frames; f++)
                    {
                        RoeCapture.Pose(go, clip, clip.length * f / Mathf.Max(1, frames));
                        foreach (var n in nodes)
                        {
                            lo[n] = Vector3.Min(lo[n], n.position);
                            hi[n] = Vector3.Max(hi[n], n.position);
                            for (int h = 0; h < 2; h++)
                                rel[n][h].Add(hands[h].InverseTransformPoint(n.position));
                        }
                    }
                }
                RoeCapture.EndPosing();

                var sb = new StringBuilder();
                sb.AppendLine($"[ROE] {c.id} weapon nodes (range of world position over idle, skills, hurt; spread relative to each hand):");
                foreach (var n in nodes)
                {
                    float range = (hi[n] - lo[n]).magnitude;
                    string depth = new string(' ', 2 * Depth(n, go.transform));
                    float[] spread = new float[2];
                    for (int h = 0; h < 2; h++)
                    {
                        var mean = rel[n][h].Aggregate(Vector3.zero, (a, b) => a + b) / rel[n][h].Count;
                        spread[h] = Mathf.Sqrt(rel[n][h].Average(p => (p - mean).sqrMagnitude));
                    }
                    sb.AppendLine($"[ROE]   {depth}{n.name}{(skinned.Contains(n) ? " *skin" : "")}: moves {range:F2} m, spread vs L hand {spread[0]:F2} m, R hand {spread[1]:F2} m, scale {n.lossyScale.x:F2}");
                }
                Debug.Log(sb.ToString());
                Object.DestroyImmediate(go);
            }
        }

        static int Depth(Transform t, Transform root)
        {
            int d = 0;
            while (t != null && t != root) { d++; t = t.parent; }
            return d;
        }
    }
}
