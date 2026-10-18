using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace RoeFighter.EditorTools
{
    /// <summary>
    /// Measures every clip of every built character: length, how far the hips travel, how low the
    /// feet go (below 0 = through the floor), where the body ends relative to where it started.
    /// Batch mode: -executeMethod RoeFighter.EditorTools.RoeClipReport.Run
    /// </summary>
    public static class RoeClipReport
    {
        [MenuItem("ROE Fighter/Check/Clip report")]
        public static void Run()
        {
            var manifest = RoeManifest.Load();
            foreach (var c in manifest.WithModels)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RoeFighterBuilder.PrefabPath(c.id));
                if (prefab == null)
                    continue;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                var hips = RoeShowcase.FindBone(go, "Bip001");
                var feet = new[] { "Bip001 L Foot", "Bip001 R Foot", "Bip001 L Toe0", "Bip001 R Toe0" }
                    .Select(n => RoeShowcase.FindBone(go, n)).Where(t => t != null).ToArray();
                var sb = new StringBuilder();
                foreach (var entry in c.clips)
                {
                    var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(entry.path);
                    if (clip == null)
                        continue;
                    int frames = Mathf.Max(2, Mathf.RoundToInt(clip.length * 60f));
                    float minFoot = float.MaxValue, maxHip = float.MinValue, minHip = float.MaxValue, travel = 0f;
                    Vector3 first = Vector3.zero, last = Vector3.zero;
                    float firstFoot = 0f;
                    for (int f = 0; f <= frames; f++)
                    {
                        RoeCapture.Pose(go, clip, clip.length * f / frames);
                        float foot = feet.Min(t => t.position.y);
                        if (f == 0) { first = hips.position; firstFoot = foot; }
                        last = hips.position;
                        minFoot = Mathf.Min(minFoot, foot);
                        maxHip = Mathf.Max(maxHip, hips.position.y);
                        minHip = Mathf.Min(minHip, hips.position.y);
                        travel = Mathf.Max(travel, Vector3.ProjectOnPlane(hips.position - first, Vector3.up).magnitude);
                    }
                    sb.AppendLine($"[ROE] {c.id} {entry.name,-10} {clip.length,5:F2} s  loop={clip.isLooping}  hips y {minHip:F2}..{maxHip:F2}  " +
                                  $"lowest foot {minFoot:F2} (first frame {firstFoot:F2})  travel {travel:F2} m  " +
                                  $"end-start ({last.x - first.x:F2}, {last.y - first.y:F2}, {last.z - first.z:F2})  events={clip.events.Length}");
                }
                RoeCapture.EndPosing();
                Object.DestroyImmediate(go);
                Debug.Log(sb.ToString());
            }
        }
    }
}
