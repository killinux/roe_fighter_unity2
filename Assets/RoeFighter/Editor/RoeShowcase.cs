using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RoeFighter.EditorTools
{
    /// <summary>
    /// Look-development renders of the built characters in the studio scene.
    ///   Stills: four fixed shots per character (full body, face, upper body, back) as PNG.
    /// Batch mode: -executeMethod RoeFighter.EditorTools.RoeShowcase.Stills [-roeChars a08,g04] [-roeOut dir] [-roeClip idle_01] [-roeTime 0]
    /// </summary>
    public static class RoeShowcase
    {
        static string ProjectDir => Path.GetDirectoryName(Application.dataPath);

        public static Transform FindBone(GameObject root, string name)
        {
            return root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);
        }

        /// <summary>Which way the character looks, from the feet: toes are in front of the ankles.</summary>
        public static Vector3 Forward(GameObject character)
        {
            var sum = Vector3.zero;
            // Biped feet, or a Dead or Alive 6 rig's (bone_7 / bone_23 left foot and toes, bone_8 / bone_24 right)
            foreach (var (footName, toeName) in new[] { ("Bip001 L Foot", "Bip001 L Toe0"), ("Bip001 R Foot", "Bip001 R Toe0"), ("bone_7", "bone_23"), ("bone_8", "bone_24") })
            {
                var foot = FindBone(character, footName);
                var toe = FindBone(character, toeName);
                if (foot != null && toe != null)
                    sum += toe.position - foot.position;
            }
            sum = Vector3.ProjectOnPlane(sum, Vector3.up);
            return sum.sqrMagnitude > 1e-6f ? sum.normalized : character.transform.forward;
        }

        public static Bounds WorldBounds(GameObject character)
        {
            var renderers = character.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var b = new Bounds();
            bool first = true;
            foreach (var r in renderers)
            {
                var mesh = new Mesh();
                r.BakeMesh(mesh, true);
                var verts = mesh.vertices;
                var m = r.transform.localToWorldMatrix;
                foreach (var v in verts)
                {
                    var w = m.MultiplyPoint3x4(v);
                    if (first) { b = new Bounds(w, Vector3.zero); first = false; }
                    else b.Encapsulate(w);
                }
                Object.DestroyImmediate(mesh);
            }
            return b;
        }

        [MenuItem("ROE Fighter/Showcase/Stills")]
        public static void Stills()
        {
            var manifest = RoeManifest.Load();
            var ids = RoeCapture.Arg("-roeChars", string.Join(",", manifest.WithModels.Select(c => c.id))).Split(',');
            string outDir = RoeCapture.Arg("-roeOut", Path.Combine(ProjectDir, "_work", "shots"));
            string clipName = RoeCapture.Arg("-roeClip", "idle_01");
            float time = float.Parse(RoeCapture.Arg("-roeTime", "0"), System.Globalization.CultureInfo.InvariantCulture);

            // wait for the real shader instead of drawing the cyan placeholder
            ShaderUtil.allowAsyncCompilation = false;
            var studio = RoeStudio.Build();
            foreach (var id in ids)
            {
                var c = manifest.Find(id);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RoeFighterBuilder.PrefabPath(id));
                if (prefab == null)
                    prefab = RoeFighterBuilder.Build(c);
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);

                var clip = c.LoadClip(clipName);
                if (clip != null)
                    RoeCapture.Pose(go, clip, time);

                var forward = Forward(go);
                var bounds = WorldBounds(go);
                var head = FindBone(go, "Bip001 Head");
                var chest = FindBone(go, "Bip001 Spine2") ?? FindBone(go, "Bip001 Spine1");
                Vector3 headPos = head != null ? head.position : bounds.center + Vector3.up * bounds.extents.y * 0.8f;
                Vector3 chestPos = chest != null ? chest.position : bounds.center;
                float height = bounds.max.y;
                Debug.Log($"[ROE] {id}: clip {clipName}@{time}, forward {forward}, bounds {bounds.min}..{bounds.max}, head {headPos}");

                studio.LightFrom(forward);
                var body = new Vector3(bounds.center.x, height * 0.52f, bounds.center.z);
                var face = headPos + Vector3.up * 0.06f + forward * 0.02f;

                studio.SetFocus(0f, 0f, 0f);
                studio.Aim(body, forward, 18f, 6f, height * 2.75f, 26f);
                // the very first frame with new materials comes out with placeholder shading: throw two away
                string warm = Path.Combine(outDir, "_warmup.png");
                RoeCapture.Render(studio.camera, 320, 320, warm);
                RoeCapture.Render(studio.camera, 320, 320, warm);
                RoeCapture.Render(studio.camera, 1400, 1800, Path.Combine(outDir, $"{id}_1_full.png"));

                studio.Aim(face, forward, 14f, 2f, 0.95f, 17f);
                studio.SetFocus(0.95f, 2.8f, 85f);
                RoeCapture.Render(studio.camera, 1400, 1400, Path.Combine(outDir, $"{id}_2_face.png"));

                studio.Aim(Vector3.Lerp(chestPos, headPos, 0.45f), forward, -28f, 4f, 1.9f, 24f);
                studio.SetFocus(1.9f, 4f, 70f);
                RoeCapture.Render(studio.camera, 1400, 1800, Path.Combine(outDir, $"{id}_3_upper.png"));

                studio.SetFocus(0f, 0f, 0f);
                studio.Aim(body, forward, 160f, 8f, height * 2.75f, 26f);
                RoeCapture.Render(studio.camera, 1400, 1800, Path.Combine(outDir, $"{id}_4_back.png"));

                RoeCapture.EndPosing();
                Object.DestroyImmediate(go);
            }
            Debug.Log($"[ROE] stills written to {outDir}");
        }

        /// <summary>
        /// Retarget test: every fighter plays humanoid clips made from ANOTHER character's clips
        /// (pure humanoid retargeting, none of the donor's helper or hair curves apply).
        /// Writes one strip of evenly spaced frames per pairing.
        /// Batch mode: -executeMethod RoeFighter.EditorTools.RoeShowcase.RetargetStills [-roeOut dir] [-roeClip skill_01]
        /// </summary>
        [MenuItem("ROE Fighter/Showcase/Retarget stills")]
        public static void RetargetStills()
        {
            var manifest = RoeManifest.Load();
            string outDir = RoeCapture.Arg("-roeOut", Path.Combine(ProjectDir, "_work", "retarget"));
            string clipName = RoeCapture.Arg("-roeClip", "skill_01");
            int shots = int.Parse(RoeCapture.Arg("-roeShots", "8"));
            var ids = manifest.WithModels.Select(c => c.id).ToArray();

            ShaderUtil.allowAsyncCompilation = false;
            var studio = RoeStudio.Build();
            foreach (var actor in ids)
            {
                foreach (var donor in ids)
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RoeHumanoid.FighterPath(actor));
                    var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(RoeHumanoidClips.ClipPath(donor, clipName));
                    if (prefab == null || clip == null)
                    {
                        Debug.LogWarning($"[ROE] retarget {actor} <- {donor}: prefab or clip missing");
                        continue;
                    }
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    RoeCapture.Pose(go, clip, 0f);
                    var forward = Forward(go);
                    float top = 1.7f;
                    studio.LightFrom(forward);
                    studio.SetFocus(0f, 0f, 0f);
                    var bones = go.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                        .SelectMany(r => r.bones).Where(b => b != null).Distinct().ToArray();
                    string warm = Path.Combine(outDir, "_warmup.jpg");
                    for (int i = 0; i < shots; i++)
                    {
                        float time = clip.length * (i + 0.5f) / shots;
                        RoeCapture.Pose(go, clip, time);
                        var lo = bones[0].position;
                        var hi = lo;
                        foreach (var bone in bones)
                        {
                            lo = Vector3.Min(lo, bone.position);
                            hi = Vector3.Max(hi, bone.position);
                        }
                        lo.y = Mathf.Min(lo.y, 0f);
                        float half = Mathf.Max(top * 0.62f, (hi.y - lo.y) * 0.5f + 0.25f, Mathf.Max(hi.x - lo.x, hi.z - lo.z) * 0.5f + 0.3f);
                        studio.Aim((lo + hi) * 0.5f, forward, 25f, 6f, half / Mathf.Tan(13.5f * Mathf.Deg2Rad), 27f);
                        if (i == 0)
                        {
                            RoeCapture.Render(studio.camera, 320, 320, warm, 80);
                            RoeCapture.Render(studio.camera, 320, 320, warm, 80);
                        }
                        RoeCapture.Render(studio.camera, 900, 900, Path.Combine(outDir, $"{actor}_plays_{donor}_{clipName}_{i}.jpg"), 92);
                    }
                    RoeCapture.EndPosing();
                    Object.DestroyImmediate(go);
                    Debug.Log($"[ROE] retarget stills: {actor} plays {donor}'s {clipName}");
                }
            }
        }

        public static Vector3[] Smooth(Vector3[] values, int radius)
        {
            var result = new Vector3[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                var sum = Vector3.zero;
                int count = 0;
                for (int k = Mathf.Max(0, i - radius); k <= Mathf.Min(values.Length - 1, i + radius); k++)
                {
                    sum += values[k];
                    count++;
                }
                result[i] = sum / count;
            }
            return result;
        }

        public static float[] Smooth(float[] values, int radius)
        {
            var result = new float[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                float sum = 0f;
                int count = 0;
                for (int k = Mathf.Max(0, i - radius); k <= Mathf.Min(values.Length - 1, i + radius); k++)
                {
                    sum += values[k];
                    count++;
                }
                result[i] = sum / count;
            }
            return result;
        }

        public static float[] SlidingMax(float[] values, int radius)
        {
            var result = new float[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                float best = values[i];
                for (int k = Mathf.Max(0, i - radius); k <= Mathf.Min(values.Length - 1, i + radius); k++)
                    best = Mathf.Max(best, values[k]);
                result[i] = best;
            }
            return result;
        }

        /// <summary>
        /// Renders each character playing its own battle clips (idle, three skills, hurt) as a JPG
        /// frame sequence plus a timeline.json; tools/make_video.py turns that into an mp4 with the
        /// game's skill sounds.
        /// Batch mode: -executeMethod RoeFighter.EditorTools.RoeShowcase.Video [-roeChars a08,g04] [-roeOut dir]
        ///             [-roeSize 1920x1080] [-roeFps 60] [-roeClips idle_01,skill_01,...]
        /// </summary>
        [MenuItem("ROE Fighter/Showcase/Video frames")]
        public static void Video()
        {
            var manifest = RoeManifest.Load();
            var ids = RoeCapture.Arg("-roeChars", string.Join(",", manifest.WithModels.Select(c => c.id))).Split(',');
            string outDir = RoeCapture.Arg("-roeOut", Path.Combine(ProjectDir, "_work", "video"));
            var size = RoeCapture.Arg("-roeSize", "1920x1080").Split('x');
            int width = int.Parse(size[0]), height = int.Parse(size[1]);
            int fps = int.Parse(RoeCapture.Arg("-roeFps", "60"));
            var clipNames = RoeCapture.Arg("-roeClips", "idle_01,skill_01,idle_01,skill_02,idle_01,skill_03,hurt,idle_01").Split(',');

            ShaderUtil.allowAsyncCompilation = false;
            var studio = RoeStudio.Build();
            var timeline = new List<string>();
            int frame = 0;

            foreach (var id in ids)
            {
                var c = manifest.Find(id);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RoeFighterBuilder.PrefabPath(id));
                if (prefab == null)
                    prefab = RoeFighterBuilder.Build(c);
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                var idle = c.LoadClip("idle_01");
                RoeCapture.Pose(go, idle, 0f);

                var forward = Forward(go);
                var pelvis = FindBone(go, "Bip001 Pelvis") ?? FindBone(go, "Bip001");
                float top = WorldBounds(go).max.y;
                studio.LightFrom(forward);
                studio.SetFocus(0f, 0f, 0f);

                // only transforms that deform a mesh (body and weapon bones), not helper nulls at the origin
                var bones = go.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .SelectMany(r => r.bones).Where(b => b != null).Distinct().ToArray();
                const float fov = 27f;
                float tanHalf = Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
                float aspect = width / (float)height;
                string warm = Path.Combine(outDir, "_warmup.jpg");
                studio.Aim(new Vector3(pelvis.position.x, top * 0.52f, pelvis.position.z), forward, 22f, 5f, top * 0.62f / tanHalf, fov);
                RoeCapture.Render(studio.camera, 320, 180, warm, 80);
                RoeCapture.Render(studio.camera, 320, 180, warm, 80);

                // ---- which clip and time each output frame shows
                var shotClips = new List<AnimationClip>();
                var shotTimes = new List<float>();
                foreach (var name in clipNames)
                {
                    var clip = c.LoadClip(name);
                    if (clip == null)
                    {
                        Debug.LogWarning($"[ROE] {id}: no clip '{name}'");
                        continue;
                    }
                    int frames = Mathf.Max(1, Mathf.RoundToInt(clip.length * fps));
                    timeline.Add($"{{\"id\":\"{id}\",\"clip\":\"{name}\",\"frame\":{frame + shotClips.Count},\"frames\":{frames},\"fps\":{fps}}}");
                    for (int f = 0; f < frames; f++)
                    {
                        shotClips.Add(clip);
                        shotTimes.Add(f / (float)fps);
                    }
                    Debug.Log($"[ROE] {id} {name}: {clip.length:F2} s, {frames} frames");
                }

                // ---- pass 1: where the skeleton (weapon bones included) is in every frame
                int n = shotClips.Count;
                var centres = new Vector3[n];
                var distances = new float[n];
                for (int i = 0; i < n; i++)
                {
                    RoeCapture.Pose(go, shotClips[i], shotTimes[i]);
                    var lo = bones[0].position;
                    var hi = lo;
                    foreach (var bone in bones)
                    {
                        lo = Vector3.Min(lo, bone.position);
                        hi = Vector3.Max(hi, bone.position);
                    }
                    lo.y = Mathf.Min(lo.y, 0f);
                    centres[i] = (lo + hi) * 0.5f;
                    float halfHeight = (hi.y - lo.y) * 0.5f + 0.22f;
                    float halfWidth = Mathf.Max(hi.x - lo.x, hi.z - lo.z) * 0.5f + 0.25f;
                    distances[i] = Mathf.Max(top * 0.62f, halfHeight, halfWidth / aspect) / tanHalf;
                }
                // The camera path is smoothed with knowledge of the future (we render offline): it
                // starts moving before a jump or a lunge, so the subject never leaves the frame.
                int r = Mathf.RoundToInt(fps * 0.4f);
                centres = Smooth(Smooth(centres, r), r);
                distances = Smooth(Smooth(SlidingMax(distances, r), r), r);

                // ---- pass 2: render
                for (int i = 0; i < n; i++)
                {
                    RoeCapture.Pose(go, shotClips[i], shotTimes[i]);
                    float yaw = 20f + 12f * Mathf.Sin(i / (float)fps * 0.35f);
                    studio.Aim(centres[i], forward, yaw, 6f, distances[i], fov);
                    RoeCapture.Render(studio.camera, width, height, Path.Combine(outDir, "frames", $"{frame:D5}.jpg"), 95);
                    frame++;
                }
                RoeCapture.EndPosing();
                Object.DestroyImmediate(go);
            }
            Directory.CreateDirectory(outDir);
            File.WriteAllText(Path.Combine(outDir, "timeline.json"), "[" + string.Join(", ", timeline) + "]");
            Debug.Log($"[ROE] {frame} frames written to {outDir}");
        }
    }
}
