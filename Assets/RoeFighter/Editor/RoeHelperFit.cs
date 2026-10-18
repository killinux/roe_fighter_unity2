using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace RoeFighter.EditorTools
{
    /// <summary>
    /// Measures how the game moves the limb helper bones (twist, knee, elbow, shoulder helpers
    /// that carry skin) and stores it on the humanoid fighter prefab as a RoeHelperRig, so that
    /// animation driving only the human bones (motion capture) moves the skin the way the game does.
    ///
    /// The game's own clips are sampled on the original (Generic) character.  For each helper,
    /// every human bone is tried as the one it follows: the helper's rotation in that bone's frame
    /// should be fixed up to a roll about the bone, and the roll a share of the next bone's roll
    /// (least squares).  Blends of two neighbouring bones are tried too (knee and elbow helpers sit
    /// halfway).  The position is attached to the bone it stays at a fixed offset from, if any.
    ///   -executeMethod RoeFighter.EditorTools.RoeHelperFit.Run [-roeChars a08,g04]
    /// </summary>
    public static class RoeHelperFit
    {
        static readonly string[] LimbOwners =
        {
            "LeftShoulder", "RightShoulder", "LeftUpperArm", "RightUpperArm", "LeftLowerArm", "RightLowerArm",
            "LeftUpperLeg", "RightUpperLeg", "LeftLowerLeg", "RightLowerLeg",
        };

        static readonly string[] Clips = { "idle_01", "idle_02", "react_01", "react_02", "skill_01", "skill_02", "skill_03", "hurt", "die", "rip" };

        /// <summary>A leg's twist bone wherever it hangs (3ds Max biped: Bip001 LThighTwist, Bip001 RCalfTwist1).</summary>
        static readonly Regex LegTwist = new Regex(@"(Thigh|Calf)Twist", RegexOptions.IgnoreCase);

        // each human bone's next one along the limb: its axis, and the bone whose roll its helpers share
        static readonly Dictionary<string, string> Next = new Dictionary<string, string>
        {
            { "Hips", "Spine" }, { "Spine", "Chest" }, { "Chest", "UpperChest" }, { "UpperChest", "Neck" }, { "Neck", "Head" },
            { "LeftShoulder", "LeftUpperArm" }, { "LeftUpperArm", "LeftLowerArm" }, { "LeftLowerArm", "LeftHand" }, { "LeftHand", "Left Middle Proximal" },
            { "RightShoulder", "RightUpperArm" }, { "RightUpperArm", "RightLowerArm" }, { "RightLowerArm", "RightHand" }, { "RightHand", "Right Middle Proximal" },
            { "LeftUpperLeg", "LeftLowerLeg" }, { "LeftLowerLeg", "LeftFoot" }, { "LeftFoot", "LeftToes" },
            { "RightUpperLeg", "RightLowerLeg" }, { "RightLowerLeg", "RightFoot" }, { "RightFoot", "RightToes" },
        };

        static readonly Dictionary<string, string> Prev = new Dictionary<string, string>
        {
            { "Spine", "Hips" }, { "Chest", "Spine" }, { "UpperChest", "Chest" }, { "Neck", "UpperChest" }, { "Head", "Neck" },
            { "LeftShoulder", "UpperChest" }, { "LeftUpperArm", "LeftShoulder" }, { "LeftLowerArm", "LeftUpperArm" }, { "LeftHand", "LeftLowerArm" },
            { "RightShoulder", "UpperChest" }, { "RightUpperArm", "RightShoulder" }, { "RightLowerArm", "RightUpperArm" }, { "RightHand", "RightLowerArm" },
            { "LeftUpperLeg", "Hips" }, { "LeftLowerLeg", "LeftUpperLeg" }, { "LeftFoot", "LeftLowerLeg" }, { "LeftToes", "LeftFoot" },
            { "RightUpperLeg", "Hips" }, { "RightLowerLeg", "RightUpperLeg" }, { "RightFoot", "RightLowerLeg" }, { "RightToes", "RightFoot" },
        };

        static readonly (string, string)[] Pairs =
        {
            ("LeftShoulder", "LeftUpperArm"), ("LeftUpperArm", "LeftLowerArm"), ("LeftLowerArm", "LeftHand"),
            ("RightShoulder", "RightUpperArm"), ("RightUpperArm", "RightLowerArm"), ("RightLowerArm", "RightHand"),
            ("LeftUpperLeg", "LeftLowerLeg"), ("LeftLowerLeg", "LeftFoot"), ("RightUpperLeg", "RightLowerLeg"), ("RightLowerLeg", "RightFoot"),
            ("Hips", "LeftUpperLeg"), ("Hips", "RightUpperLeg"), ("UpperChest", "LeftShoulder"), ("UpperChest", "RightShoulder"),
            ("Chest", "LeftShoulder"), ("Chest", "RightShoulder"),
        };

        [MenuItem("ROE Fighter/Build/Fit limb helpers")]
        public static void Run()
        {
            var manifest = RoeManifest.Load();
            foreach (var id in RoeCapture.Arg("-roeChars", "a08,g04").Split(','))
            {
                var c = manifest.characters.FirstOrDefault(x => x.id == id);
                if (c == null)
                    continue;
                Debug.Log(Fit(c));
            }
            AssetDatabase.SaveAssets();
        }

        class Track
        {
            public readonly List<Quaternion> rot = new List<Quaternion>();
            public readonly List<Vector3> pos = new List<Vector3>();
        }

        class Result
        {
            public string model;
            public string driver, driver2, twistSource, twistSource2, positionBone;
            public float blend, error, share, share2, offsetDeg, positionSpread;
            public Quaternion align2 = Quaternion.identity, rotation = Quaternion.identity, twistRest = Quaternion.identity, twistRest2 = Quaternion.identity;
            public Vector3 axis = Vector3.right, offset;
        }

        static Quaternion Average(IList<Quaternion> qs)
        {
            var first = qs[0];
            Vector4 sum = Vector4.zero;
            foreach (var q in qs)
            {
                float s = Quaternion.Dot(q, first) < 0f ? -1f : 1f;
                sum += new Vector4(q.x, q.y, q.z, q.w) * s;
            }
            sum.Normalize();
            return new Quaternion(sum.x, sum.y, sum.z, sum.w);
        }

        static float Rms(IEnumerable<float> values)
        {
            var list = values.ToList();
            return list.Count == 0 ? 0f : Mathf.Sqrt(list.Sum(v => v * v) / list.Count);
        }

        /// <summary>RMS of the best 90%: how well a model fits, not swayed by a few wrapped-around frames.</summary>
        static float FitError(IEnumerable<float> values)
        {
            var list = values.OrderBy(v => v).ToList();
            return Rms(list.Take(Mathf.Max(1, (int)(list.Count * 0.9f))));
        }

        static string Fit(RoeManifest.Character c)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RoeFighterBuilder.PrefabPath(c.id));
            var go = Object.Instantiate(prefab);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var map = RoeHumanoid.MapBones(go);
            var human = new Dictionary<Transform, string>();
            foreach (var kv in map)
                human[kv.Value] = kv.Key;

            string Owner(Transform b)
            {
                for (var t = b.parent; t != null; t = t.parent)
                    if (human.TryGetValue(t, out var name))
                        return name;
                return null;
            }

            var skinned = new HashSet<Transform>(go.GetComponentsInChildren<SkinnedMeshRenderer>(true).SelectMany(r => r.bones).Where(b => b != null));
            // chains, ribbons and long sleeves swing (RoeBoneCloth), they follow no limb (g05's ribbons on her arms fitted 26-85
            // degrees off; b14's sleeve hangs from her right forearm).
            // A leg's twist bones may hang off the trunk: b10's Bip001 L/RThighTwist(1) are on Bip001 Spine, and with them left
            // where the stance put them her knees stayed behind in every lunge (user 10-06: "卡地亚的膝盖和小腿是不是有问题") - by name
            var helpers = skinned.Where(b => !human.ContainsKey(b) && (LimbOwners.Contains(Owner(b)) || LegTwist.IsMatch(b.name)) &&
                                             !Regex.IsMatch(b.name, "chain|riband|ribbon|^sleeve_", RegexOptions.IgnoreCase))
                .OrderBy(b => Depth(b)).ToList();
            var candidates = map.Where(kv => !kv.Key.Contains(" ")).Select(kv => kv.Key).ToList();     // no fingers
            var tracked = new HashSet<Transform>(helpers.Concat(map.Values));
            var tracks = tracked.ToDictionary(t => t, t => new Track());

            int frames = 0;
            var used = new List<string>();
            foreach (var name in Clips)
            {
                var clip = c.LoadClip(name);
                if (clip == null)
                    continue;
                used.Add(name);
                for (float t = 0f; t <= clip.length + 1e-4f; t += 1f / 15f)
                {
                    RoeCapture.Pose(go, clip, t);
                    foreach (var kv in tracks)
                    {
                        kv.Value.rot.Add(kv.Key.rotation);
                        kv.Value.pos.Add(kv.Key.position);
                    }
                    frames++;
                }
            }
            RoeCapture.EndPosing();

            var sb = new StringBuilder($"[ROE] limb helpers of {c.id}: {helpers.Count} helpers, {frames} frames from {string.Join(" ", used)}");
            var results = new Dictionary<string, Result>();
            foreach (var h in helpers)
            {
                var best = BestSingle(h, map, candidates, tracks);
                var blend = BestBlend(h, map, tracks);
                var r = blend != null && blend.error < best.error - 0.5f ? blend : best;
                // (10-06, "大腿根的权重还是不对呀": a blend between the pelvis and the thigh at the share the ROE PMX export
                // gives its skin, 0.86 for b10's upper ThighTwist, was tried here in place of the clips' fit and dropped: it
                // turns the twist bone with 86 % of the thigh's own roll, where the game's clips give the roll back - in her
                // skill_03 at 1.10 s the game's ThighTwist is 4 degrees from following the thigh without its roll, 55 from
                // following it with all of it - and with the roll her buttock bulged onto the thigh in the game's own pose.)
                Position(h, r, map, tracks);
                results[h.name] = r;
                sb.Append($"\n[ROE]   {h.name} (parent {h.parent.name}): {r.model} {r.driver}" +
                          (r.driver2 != null ? $"->{r.driver2} {r.blend:F2}" : "") +
                          $", error {r.error:F1} deg on 90% of frames (single best {best.error:F1}{(blend != null ? $", blend best {blend.error:F1}" : "")})" +
                          (r.twistSource != null && r.share != 0f ? $", roll {r.share:F2} x {r.twistSource}" : "") +
                          (r.twistSource2 != null && r.share2 != 0f ? $" {r.share2:+0.00;-0.00} x {r.twistSource2}" : "") +
                          (r.twistSource != null ? $" {r.offsetDeg:+0;-0} deg" : "") +
                          $", position {(r.positionBone != null ? $"on {r.positionBone} (spread {r.positionSpread * 1000f:F0} mm)" : $"from its parent (best spread {r.positionSpread * 1000f:F0} mm)")}");
            }
            Object.DestroyImmediate(go);

            // onto the humanoid fighter prefab, by bone name
            string path = RoeHumanoid.FighterPath(c.id);
            var root = PrefabUtility.LoadPrefabContents(path);
            var byName = new Dictionary<string, Transform>();
            var duplicates = new HashSet<string>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (byName.ContainsKey(t.name))
                    duplicates.Add(t.name);
                else
                    byName[t.name] = t;
            }
            var fighterMap = RoeHumanoid.MapBones(root);
            Transform Human(string humanName) => humanName != null && fighterMap.TryGetValue(humanName, out var t) ? t : null;
            var rig = root.GetComponent<RoeHelperRig>() ?? root.AddComponent<RoeHelperRig>();
            rig.drives.Clear();
            foreach (var kv in results)
            {
                if (duplicates.Contains(kv.Key) || !byName.TryGetValue(kv.Key, out var helper))
                {
                    sb.Append($"\n[ROE]   {kv.Key}: not unique / not found on the fighter - skipped");
                    continue;
                }
                var r = kv.Value;
                rig.drives.Add(new RoeHelperRig.Drive
                {
                    helper = helper,
                    driver = Human(r.driver),
                    driver2 = Human(r.driver2),
                    blend = r.blend,
                    align2 = r.align2,
                    rotation = r.rotation,
                    twistAxis = r.axis,
                    twistSource = Human(r.twistSource),
                    twistRest = r.twistRest,
                    twistShare = r.share,
                    twistOffset = r.offsetDeg,
                    twistSource2 = Human(r.twistSource2),
                    twistRest2 = r.twistRest2,
                    twistShare2 = r.share2,
                    positionBone = Human(r.positionBone),
                    offset = r.offset,
                    fitError = r.error,
                });
            }
            PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);
            sb.Append($"\n[ROE]   {rig.drives.Count} drives saved on {path}");
            return sb.ToString();
        }

        /// <summary>
        /// How the game moves what hangs on the hips (skirt panels): for each non-human child of the hips
        /// with skin under it, sampled over the game's own clips, the error of a few models - rigid on the
        /// pelvis; hanging from the hips' heading (yaw from the thigh line, gravity-like); and blends of the
        /// heading or the pelvis with either thigh.  Only a report.
        ///   -executeMethod RoeFighter.EditorTools.RoeHelperFit.SkirtReport [-roeChars a08,g04]
        /// </summary>
        public static void SkirtReport()
        {
            var manifest = RoeManifest.Load();
            foreach (var id in RoeCapture.Arg("-roeChars", "a08,g04").Split(','))
            {
                var c = manifest.characters.FirstOrDefault(x => x.id == id);
                if (c == null)
                    continue;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RoeFighterBuilder.PrefabPath(c.id));
                var go = Object.Instantiate(prefab);
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var map = RoeHumanoid.MapBones(go);
                var human = new HashSet<Transform>(map.Values);
                var skinned = new HashSet<Transform>(go.GetComponentsInChildren<SkinnedMeshRenderer>(true).SelectMany(r => r.bones).Where(b => b != null));
                var hips = map["Hips"];
                var roots = hips.Cast<Transform>().Where(t => !human.Contains(t) && t.GetComponentsInChildren<Transform>(true).Any(skinned.Contains)).ToList();
                var tracked = new List<Transform>(roots) { hips, map["LeftUpperLeg"], map["RightUpperLeg"], map["LeftLowerLeg"], map["RightLowerLeg"] };
                var tracks = tracked.ToDictionary(t => t, t => new Track());
                var tips = roots.ToDictionary(r => r, r => r.GetComponentsInChildren<Transform>(true).OrderByDescending(Depth).First());
                foreach (var tip in tips.Values)
                    if (!tracks.ContainsKey(tip))
                        tracks[tip] = new Track();
                int frames = 0;
                foreach (var name in Clips)
                {
                    var clip = c.LoadClip(name);
                    if (clip == null)
                        continue;
                    for (float t = 0f; t <= clip.length + 1e-4f; t += 1f / 15f)
                    {
                        RoeCapture.Pose(go, clip, t);
                        foreach (var kv in tracks)
                        {
                            kv.Value.rot.Add(kv.Key.rotation);
                            kv.Value.pos.Add(kv.Key.position);
                        }
                        frames++;
                    }
                }
                RoeCapture.EndPosing();
                var L = tracks[map["LeftUpperLeg"]];
                var R = tracks[map["RightUpperLeg"]];
                var heading = Enumerable.Range(0, frames).Select(f =>
                {
                    var fwd = Vector3.Cross(R.pos[f] - L.pos[f], Vector3.up);
                    fwd.y = 0f;
                    return Quaternion.LookRotation(fwd.normalized, Vector3.up);
                }).ToList();
                var H = tracks[hips];
                var sb = new StringBuilder($"[ROE] skirt roots of {c.id} over {frames} frames of the game's clips:");
                foreach (var root in roots)
                {
                    var S = tracks[root];
                    float Rigid(IList<Quaternion> frame)
                    {
                        var rel = Enumerable.Range(0, frames).Select(f => Quaternion.Inverse(frame[f]) * S.rot[f]).ToList();
                        var q = Average(rel);
                        return FitError(rel.Select(x => Quaternion.Angle(q, x)));
                    }
                    (float w, float error) Blend(IList<Quaternion> a, IList<Quaternion> b)
                    {
                        var align = Quaternion.Inverse(b[0]) * a[0];
                        var best = (w: 0f, error: float.MaxValue);
                        for (float w = 0f; w < 1.001f; w += 0.05f)
                        {
                            var rel = Enumerable.Range(0, frames).Select(f => Quaternion.Inverse(Quaternion.Slerp(a[f], b[f] * align, w)) * S.rot[f]).ToList();
                            var q = Average(rel);
                            float e = FitError(rel.Select(x => Quaternion.Angle(q, x)));
                            if (e < best.error)
                                best = (w, e);
                        }
                        return best;
                    }
                    var tip = tracks[tips[root]];
                    // how far the panel's tip swings around in the hips' heading frame: how much there is to explain
                    var tipDirs = Enumerable.Range(0, frames).Select(f => Quaternion.Inverse(heading[f]) * (tip.pos[f] - S.pos[f]).normalized).ToList();
                    var meanDir = tipDirs.Aggregate(Vector3.zero, (s, v) => s + v).normalized;
                    float swing = Rms(tipDirs.Select(d => Vector3.Angle(d, meanDir)));
                    var hl = Blend(heading, L.rot);
                    var hr = Blend(heading, R.rot);
                    var pl = Blend(H.rot, L.rot);
                    var pr = Blend(H.rot, R.rot);
                    sb.Append($"\n[ROE]   {root.name} (tip {tips[root].name}, swings {swing:F0} deg rms): rigid on pelvis {Rigid(H.rot):F1}, hanging {Rigid(heading):F1}, " +
                              $"heading->L thigh {hl.w:F2} {hl.error:F1}, heading->R thigh {hr.w:F2} {hr.error:F1}, pelvis->L thigh {pl.w:F2} {pl.error:F1}, pelvis->R thigh {pr.w:F2} {pr.error:F1}");
                }
                Object.DestroyImmediate(go);
                Debug.Log(sb.ToString());
            }
        }

        static int Depth(Transform t)
        {
            int d = 0;
            for (; t != null; t = t.parent)
                d++;
            return d;
        }

        // ---- the skirt's animation pose under motion capture (RoeSkirtRig)

        static readonly string[] SkirtFitClips = { "idle_01", "react_01", "skill_01", "skill_03", "hurt" };
        static readonly string[] SkirtTestClips = { "idle_02", "react_02", "skill_02" };      // held out to check the fit

        /// <summary>
        /// Fits the skirt to the legs the way the game's animators keyed it, and stores it on the humanoid
        /// fighter prefab as a RoeSkirtRig.  The game's own clips (standing ones; lying on the floor is
        /// left out) are sampled on the original character; each skirt bone's tail is a linear blend of
        /// where six drivers would carry it from the battle stance - the hips' heading, the pelvis, both
        /// thighs, both calves - with weights >= 0 that sum to 1, least squares over the frames.  The log
        /// compares, on clips the fit did not see, the bone directions of "pinned to the pelvis" (the
        /// skirt before), "skinned by distance" (Magica's Custom Skinning) and the fit.
        ///   -executeMethod RoeFighter.EditorTools.RoeHelperFit.FitSkirt [-roeChars a08,g04]
        /// </summary>
        [MenuItem("ROE Fighter/Build/Fit skirt to the legs")]
        public static void FitSkirt()
        {
            var manifest = RoeManifest.Load();
            foreach (var id in RoeCapture.Arg("-roeChars", "a08,g04").Split(','))
            {
                var c = manifest.characters.FirstOrDefault(x => x.id == id);
                if (c != null)
                    Debug.Log(FitSkirt(c));
            }
            AssetDatabase.SaveAssets();
        }

        static readonly string[] SkirtDrivers = { null, "Hips", "LeftUpperLeg", "RightUpperLeg", "LeftLowerLeg", "RightLowerLeg" };

        class SkirtFrame
        {
            public bool test;
            public Matrix4x4[] driver = new Matrix4x4[RoeSkirtRig.Drivers];
            public Vector3[] head, tail;
        }

        static string FitSkirt(RoeManifest.Character c)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RoeFighterBuilder.PrefabPath(c.id));
            var go = Object.Instantiate(prefab);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var map = RoeHumanoid.MapBones(go);
            var idle = c.LoadClip("idle_01");
            // the skirt bones the cloth simulates, and their tails, in the battle stance (as the fight finds them)
            RoeCapture.Pose(go, idle, 0f);
            var kinds = RoeBoneCloth.Classify(go.transform, new HashSet<Transform>(map.Values), null);
            var bones = kinds.Where(kv => kv.Value == "skirt").Select(kv => kv.Key).OrderBy(Depth).ThenBy(b => b.name).ToList();
            if (bones.Count == 0)
            {
                // no skirt (b10 wears shorts; g05's front panel is a ribbon): nothing to fit
                RoeCapture.EndPosing();
                Object.DestroyImmediate(go);
                return $"[ROE] skirt fit {c.id}: no skirt bones, nothing to fit";
            }
            var index = new Dictionary<Transform, int>();
            for (int i = 0; i < bones.Count; i++)
                index[bones[i]] = i;
            var skin = RoeBoneCloth.SkinPoints(go.transform);
            var parent = bones.Select(b => b.parent != null && index.TryGetValue(b.parent, out int p) ? p : -1).ToArray();
            var tailLocal = bones.Select((b, i) =>
            {
                Transform child = null;
                foreach (Transform k in b)
                    if (index.ContainsKey(k))
                        child = k;
                return b.InverseTransformPoint(RoeBoneCloth.Tail(b, child, parent[i] >= 0 ? bones[parent[i]] : null, skin));
            }).ToArray();
            Transform Driver(int k) => SkirtDrivers[k] != null && map.TryGetValue(SkirtDrivers[k], out var t) ? t : null;
            Matrix4x4 Heading()
            {
                var l = map["LeftUpperLeg"].position;
                var r = map["RightUpperLeg"].position;
                var fwd = Vector3.Cross(r - l, Vector3.up);
                fwd.y = 0f;
                return Matrix4x4.TRS(map["Hips"].position, Quaternion.LookRotation(fwd.normalized, Vector3.up), Vector3.one);
            }

            // the game's clips, 15 frames a second
            var frames = new List<SkirtFrame>();
            foreach (var (name, test) in SkirtFitClips.Select(n => (n, false)).Concat(SkirtTestClips.Select(n => (n, true))))
            {
                var clip = c.LoadClip(name);
                if (clip == null)
                    continue;
                for (float t = 0f; t <= clip.length + 1e-4f; t += 1f / 15f)
                {
                    RoeCapture.Pose(go, clip, t);
                    var f = new SkirtFrame { test = test, head = new Vector3[bones.Count], tail = new Vector3[bones.Count] };
                    f.driver[0] = Heading();
                    for (int k = 1; k < RoeSkirtRig.Drivers; k++)
                    {
                        var d = Driver(k);
                        f.driver[k] = d != null ? Matrix4x4.TRS(d.position, d.rotation, Vector3.one) : f.driver[0];
                    }
                    for (int i = 0; i < bones.Count; i++)
                    {
                        f.head[i] = bones[i].position;
                        f.tail[i] = bones[i].TransformPoint(tailLocal[i]);
                    }
                    frames.Add(f);
                }
            }
            RoeCapture.EndPosing();
            var reference = frames[0];      // idle_01 at 0: the battle stance

            // where each driver carries each tail from the stance, per frame
            Vector3 Carried(SkirtFrame f, int k, int i) => f.driver[k].MultiplyPoint3x4(reference.driver[k].inverse.MultiplyPoint3x4(reference.tail[i]));
            float[] FitWeights(int i, IEnumerable<SkirtFrame> set)
            {
                var ata = new double[RoeSkirtRig.Drivers, RoeSkirtRig.Drivers];
                var atb = new double[RoeSkirtRig.Drivers];
                foreach (var f in set)
                {
                    var cols = Enumerable.Range(0, RoeSkirtRig.Drivers).Select(k => Carried(f, k, i)).ToArray();
                    for (int a = 0; a < RoeSkirtRig.Drivers; a++)
                    {
                        atb[a] += Vector3.Dot(cols[a], f.tail[i]);
                        for (int b = 0; b < RoeSkirtRig.Drivers; b++)
                            ata[a, b] += Vector3.Dot(cols[a], cols[b]);
                    }
                }
                return SimplexLeastSquares(ata, atb);
            }
            // by distance, as Magica's Custom Skinning would (inverse square to the pelvis, thigh and calf segments)
            float[] DistanceWeights(int i)
            {
                var p = reference.tail[i];
                var w = new float[RoeSkirtRig.Drivers];
                float Seg(Vector3 a, Vector3 b)
                {
                    var ab = b - a;
                    float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-8f));
                    return Vector3.Distance(p, a + t * ab);
                }
                w[1] = 1f / Mathf.Pow(Mathf.Max(0.03f, Vector3.Distance(p, (map["LeftUpperLeg"].position + map["RightUpperLeg"].position) * 0.5f)), 2f);
                w[2] = 1f / Mathf.Pow(Mathf.Max(0.03f, Seg(map["LeftUpperLeg"].position, map["LeftLowerLeg"].position)), 2f);
                w[3] = 1f / Mathf.Pow(Mathf.Max(0.03f, Seg(map["RightUpperLeg"].position, map["RightLowerLeg"].position)), 2f);
                w[4] = 1f / Mathf.Pow(Mathf.Max(0.03f, Seg(map["LeftLowerLeg"].position, map["LeftFoot"].position)), 2f);
                w[5] = 1f / Mathf.Pow(Mathf.Max(0.03f, Seg(map["RightLowerLeg"].position, map["RightFoot"].position)), 2f);
                float sum = w.Sum();
                return w.Select(x => x / sum).ToArray();
            }
            // the reference distances are in the stance pose: pose it once more
            RoeCapture.Pose(go, idle, 0f);
            var distance = Enumerable.Range(0, bones.Count).Select(DistanceWeights).ToArray();
            RoeCapture.EndPosing();
            var pinned = Enumerable.Range(0, bones.Count).Select(_ => new float[] { 0f, 1f, 0f, 0f, 0f, 0f }).ToArray();
            var fitted = Enumerable.Range(0, bones.Count).Select(i => FitWeights(i, frames.Where(f => !f.test))).ToArray();

            // each bone's direction from the skinned head to the skinned tail, against the game's keys (RMS, degrees)
            float[] DirectionError(float[][] weights, bool test)
            {
                var sum = new double[bones.Count];
                int n = 0;
                foreach (var f in frames.Where(f => f.test == test))
                {
                    var p = new Vector3[bones.Count];
                    for (int i = 0; i < bones.Count; i++)
                        for (int k = 0; k < RoeSkirtRig.Drivers; k++)
                            if (weights[i][k] > 0f)
                                p[i] += weights[i][k] * Carried(f, k, i);
                    for (int i = 0; i < bones.Count; i++)
                    {
                        var h = parent[i] >= 0 ? p[parent[i]] : f.head[i];
                        float a = Vector3.Angle(f.tail[i] - f.head[i], p[i] - h);
                        sum[i] += a * a;
                    }
                    n++;
                }
                return sum.Select(x => n > 0 ? Mathf.Sqrt((float)(x / n)) : 0f).ToArray();
            }
            var errPinned = DirectionError(pinned, true);
            var errDistance = DirectionError(distance, true);
            var errFitted = DirectionError(fitted, true);
            var errFittedTrain = DirectionError(fitted, false);
            int testFrames = frames.Count(f => f.test), fitFrames = frames.Count - testFrames;
            var sb = new StringBuilder($"[ROE] skirt of {c.id} fitted to the legs: {bones.Count} bones, {fitFrames} frames ({string.Join(" ", SkirtFitClips)}), " +
                                       $"checked on {testFrames} others ({string.Join(" ", SkirtTestClips)}); bone direction off the game's keys, RMS degrees: " +
                                       $"pinned to the pelvis {errPinned.Average():F1}, by distance {errDistance.Average():F1}, fitted {errFitted.Average():F1} (on its own frames {errFittedTrain.Average():F1})");
            for (int i = 0; i < bones.Count; i++)
                sb.Append($"\n[ROE]   {bones[i].name}: pinned {errPinned[i]:F1}, distance {errDistance[i]:F1}, fitted {errFitted[i]:F1}; weights " +
                          string.Join(" ", Enumerable.Range(0, RoeSkirtRig.Drivers).Where(k => fitted[i][k] > 0.005f).Select(k => $"{RoeSkirtRig.DriverNames[k]} {fitted[i][k]:F2}")));
            var names = bones.Select(b => b.name).ToList();
            Object.DestroyImmediate(go);

            // the weights for the fight: fitted on all the clips
            var final = Enumerable.Range(0, names.Count).Select(i => FitWeights(i, frames)).ToArray();
            string path = RoeHumanoid.FighterPath(c.id);
            var root = PrefabUtility.LoadPrefabContents(path);
            var byName = new Dictionary<string, Transform>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (!byName.ContainsKey(t.name))
                    byName[t.name] = t;
            var fighterMap = RoeHumanoid.MapBones(root);
            var rig = root.GetComponent<RoeSkirtRig>() ?? root.AddComponent<RoeSkirtRig>();
            rig.joints.Clear();
            rig.hips = fighterMap["Hips"];
            rig.leftThigh = fighterMap["LeftUpperLeg"];
            rig.rightThigh = fighterMap["RightUpperLeg"];
            rig.leftCalf = fighterMap["LeftLowerLeg"];
            rig.rightCalf = fighterMap["RightLowerLeg"];
            var jointOf = new Dictionary<int, int>();
            for (int i = 0; i < names.Count; i++)
            {
                if (!byName.TryGetValue(names[i], out var bone))
                {
                    sb.Append($"\n[ROE]   {names[i]}: not on the fighter - skipped");
                    continue;
                }
                jointOf[i] = rig.joints.Count;
                rig.joints.Add(new RoeSkirtRig.Joint
                {
                    bone = bone,
                    parent = parent[i] >= 0 && jointOf.TryGetValue(parent[i], out int pj) ? pj : -1,
                    tailLocal = tailLocal[i],
                    weights = final[i],
                    fitError = errFitted[i],
                    rigidError = errPinned[i],
                });
            }
            rig.fitReport = sb.ToString().Replace("[ROE] ", "");
            PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);
            sb.Append($"\n[ROE]   {rig.joints.Count} skirt bones saved on {path}");
            return sb.ToString();
        }

        /// <summary>
        /// min |A w - b|^2 over w >= 0, sum w = 1, from A'A and A'b: every support set is tried (6 drivers:
        /// 63 small systems), the best one whose solution stays non-negative wins.
        /// </summary>
        static float[] SimplexLeastSquares(double[,] ata, double[] atb)
        {
            int k = atb.Length;
            double trace = 0;
            for (int a = 0; a < k; a++)
                trace += ata[a, a];
            double ridge = 1e-7 * trace / k;
            double best = double.MaxValue;
            float[] bestW = null;
            for (int mask = 1; mask < (1 << k); mask++)
            {
                var idx = Enumerable.Range(0, k).Where(a => (mask & (1 << a)) != 0).ToArray();
                int n = idx.Length;
                var m = new double[n + 1, n + 2];
                for (int a = 0; a < n; a++)
                {
                    for (int b = 0; b < n; b++)
                        m[a, b] = ata[idx[a], idx[b]] + (a == b ? ridge : 0);
                    m[a, n] = 1;
                    m[a, n + 1] = atb[idx[a]];
                }
                for (int b = 0; b < n; b++)
                    m[n, b] = 1;
                m[n, n + 1] = 1;
                var x = Gauss(m, n + 1);
                if (x == null || Enumerable.Range(0, n).Any(a => x[a] < -1e-9))
                    continue;
                var w = new double[k];
                for (int a = 0; a < n; a++)
                    w[idx[a]] = System.Math.Max(0, x[a]);
                double obj = 0;
                for (int a = 0; a < k; a++)
                {
                    obj -= 2 * atb[a] * w[a];
                    for (int b = 0; b < k; b++)
                        obj += w[a] * ata[a, b] * w[b];
                }
                if (obj < best)
                {
                    best = obj;
                    bestW = w.Select(v => (float)v).ToArray();
                }
            }
            return bestW ?? new float[] { 0f, 1f, 0f, 0f, 0f, 0f };
        }

        /// <summary>Gaussian elimination with partial pivoting on an n x (n+1) augmented matrix; null when singular.</summary>
        static double[] Gauss(double[,] m, int n)
        {
            for (int col = 0; col < n; col++)
            {
                int piv = col;
                for (int r = col + 1; r < n; r++)
                    if (System.Math.Abs(m[r, col]) > System.Math.Abs(m[piv, col]))
                        piv = r;
                if (System.Math.Abs(m[piv, col]) < 1e-12)
                    return null;
                if (piv != col)
                    for (int c2 = 0; c2 <= n; c2++)
                        (m[col, c2], m[piv, c2]) = (m[piv, c2], m[col, c2]);
                for (int r = 0; r < n; r++)
                {
                    if (r == col)
                        continue;
                    double f = m[r, col] / m[col, col];
                    if (f == 0)
                        continue;
                    for (int c2 = col; c2 <= n; c2++)
                        m[r, c2] -= f * m[col, c2];
                }
            }
            var x = new double[n];
            for (int r = 0; r < n; r++)
                x[r] = m[r, n] / m[r, r];
            return x;
        }

        /// <summary>The helper follows one bone: fixed rotation in its frame up to a roll that is a share of the next bone's roll.</summary>
        static Result BestSingle(Transform h, Dictionary<string, Transform> map, List<string> candidates, Dictionary<Transform, Track> tracks)
        {
            Result best = null;
            var H = tracks[h];
            int n = H.rot.Count;
            foreach (var name in candidates)
            {
                var D = tracks[map[name]];
                Track N = Next.TryGetValue(name, out var nextName) && map.TryGetValue(nextName, out var nt) ? tracks[nt] : null;
                var axis = N != null ? (Quaternion.Inverse(D.rot[0]) * (N.pos[0] - D.pos[0])).normalized : Vector3.right;
                if (axis.sqrMagnitude < 0.5f)
                    axis = Vector3.right;
                var rel = Enumerable.Range(0, n).Select(f => Quaternion.Inverse(D.rot[f]) * H.rot[f]).ToList();
                var q0 = Average(rel);
                var theta = new float[n];
                for (int it = 0; it < 4; it++)
                {
                    for (int f = 0; f < n; f++)
                        theta[f] = RoeHelperRig.TwistAngle(rel[f] * Quaternion.Inverse(q0), axis);
                    q0 = Average(Enumerable.Range(0, n).Select(f => Quaternion.AngleAxis(-theta[f], axis) * rel[f]).ToList());
                }
                // the roll sources: the next bone (the hand's roll spreads up the forearm) and the bone
                // before (an upper-arm twist bone takes back part of the arm's roll against the shoulder)
                var sources = new List<(string name, float[] tau, Quaternion rest)>();
                string prevName = Prev.TryGetValue(name, out var p) ? p : null;
                if (prevName == "UpperChest" && !map.ContainsKey("UpperChest"))
                    prevName = "Chest";
                // (not the knee's roll for a thigh twist bone: the knee is a hinge, its "roll" against the thigh is noise in the
                // game's clips - b10's LThighTwist took 1.5 times it - and the humanoid's leg twist under motion capture)
                bool hingeNext = LegTwist.IsMatch(h.name) && name.EndsWith("UpperLeg");
                foreach (var sName in new[] { N != null && !hingeNext ? nextName : null, prevName })
                {
                    if (sName == null || !map.TryGetValue(sName, out var st))
                        continue;
                    var S = tracks[st];
                    var r = Enumerable.Range(0, n).Select(f => Quaternion.Inverse(D.rot[f]) * S.rot[f]).ToList();
                    var rest = Average(r);
                    sources.Add((sName, r.Select(x => RoeHelperRig.TwistAngle(x * Quaternion.Inverse(rest), axis)).ToArray(), rest));
                }
                var subsets = new List<int[]>();
                for (int i = 0; i < sources.Count; i++)
                    subsets.Add(new[] { i });
                if (sources.Count == 2)
                    subsets.Add(new[] { 0, 1 });
                foreach (var subset in subsets)
                {
                    var (shares, offset) = Regress(theta, subset.Select(i => sources[i].tau).ToArray());
                    float error = FitError(Enumerable.Range(0, n).Select(f =>
                    {
                        float angle = offset;
                        for (int k = 0; k < subset.Length; k++)
                            angle += shares[k] * sources[subset[k]].tau[f];
                        return Quaternion.Angle(Quaternion.AngleAxis(angle, axis) * q0, rel[f]);
                    }));
                    var first = sources[subset[0]];
                    var result = new Result
                    {
                        model = "follows", driver = name, error = error, rotation = q0, axis = axis, offsetDeg = offset,
                        twistSource = first.name, twistRest = first.rest, share = shares[0],
                    };
                    if (subset.Length > 1)
                    {
                        var second = sources[subset[1]];
                        result.twistSource2 = second.name;
                        result.twistRest2 = second.rest;
                        result.share2 = shares[1];
                    }
                    Consider(result);
                }
                // without any roll
                var plain = Average(rel);
                Consider(new Result { model = "follows", driver = name, error = FitError(rel.Select(x => Quaternion.Angle(plain, x))), rotation = plain, axis = axis });
            }
            return best;

            void Consider(Result r)
            {
                if (best == null || r.error < best.error - 0.05f)
                    best = r;
            }
        }

        /// <summary>
        /// Least squares: theta = sum(share_k * tau_k) + offset, shares kept within +-1.5.  Twice
        /// more without the frames that miss by far: a hand turned past half a turn (g04 twirls
        /// her fan) wraps around and would drag the share towards zero.
        /// </summary>
        static (float[] shares, float offset) Regress(float[] theta, float[][] taus)
        {
            var keep = Enumerable.Range(0, theta.Length).ToArray();
            var fit = Solve(keep.Select(f => theta[f]).ToArray(), taus.Select(t => keep.Select(f => t[f]).ToArray()).ToArray());
            for (int it = 0; it < 2; it++)
            {
                var miss = Enumerable.Range(0, theta.Length).Select(f =>
                {
                    float p = fit.offset;
                    for (int k = 0; k < taus.Length; k++)
                        p += fit.shares[k] * taus[k][f];
                    return Mathf.Abs(Mathf.DeltaAngle(p, theta[f]));
                }).ToArray();
                float limit = Mathf.Max(8f, 2.5f * Rms(miss));
                keep = Enumerable.Range(0, theta.Length).Where(f => miss[f] <= limit).ToArray();
                if (keep.Length < theta.Length / 2)
                    break;
                fit = Solve(keep.Select(f => theta[f]).ToArray(), taus.Select(t => keep.Select(f => t[f]).ToArray()).ToArray());
            }
            return fit;
        }

        static (float[] shares, float offset) Solve(float[] theta, float[][] taus)
        {
            int n = theta.Length, m = taus.Length;
            float mh = theta.Average();
            var mt = taus.Select(t => t.Average()).ToArray();
            var shares = new float[m];
            if (m == 1)
            {
                float vt = taus[0].Sum(x => (x - mt[0]) * (x - mt[0]));
                float cov = Enumerable.Range(0, n).Sum(f => (taus[0][f] - mt[0]) * (theta[f] - mh));
                shares[0] = vt > n * 1f ? Mathf.Clamp(cov / vt, -1.5f, 1.5f) : 0f;
            }
            else
            {
                double a = 0, b = 0, c = 0, y1 = 0, y2 = 0;
                for (int f = 0; f < n; f++)
                {
                    double x1 = taus[0][f] - mt[0], x2 = taus[1][f] - mt[1], y = theta[f] - mh;
                    a += x1 * x1; b += x1 * x2; c += x2 * x2; y1 += x1 * y; y2 += x2 * y;
                }
                double det = a * c - b * b;
                if (System.Math.Abs(det) > 1e-6 * System.Math.Max(1.0, a * c))
                {
                    shares[0] = Mathf.Clamp((float)((c * y1 - b * y2) / det), -1.5f, 1.5f);
                    shares[1] = Mathf.Clamp((float)((a * y2 - b * y1) / det), -1.5f, 1.5f);
                }
            }
            float offset = mh;
            for (int k = 0; k < m; k++)
                offset -= shares[k] * mt[k];
            return (shares, offset);
        }

        /// <summary>The helper follows a blend of two neighbouring bones (no roll).</summary>
        static Result BestBlend(Transform h, Dictionary<string, Transform> map, Dictionary<Transform, Track> tracks)
        {
            Result best = null;
            var H = tracks[h];
            int n = H.rot.Count;
            foreach (var (a, b) in Pairs)
            {
                if (!map.TryGetValue(a, out var ta) || !map.TryGetValue(b, out var tb))
                    continue;
                var A = tracks[ta];
                var B = tracks[tb];
                var align = Quaternion.Inverse(B.rot[0]) * A.rot[0];
                for (float w = 0.05f; w < 0.951f; w += 0.05f)
                {
                    var rel = Enumerable.Range(0, n).Select(f => Quaternion.Inverse(Quaternion.Slerp(A.rot[f], B.rot[f] * align, w)) * H.rot[f]).ToList();
                    var q = Average(rel);
                    float error = FitError(rel.Select(x => Quaternion.Angle(q, x)));
                    if (best == null || error < best.error)
                        best = new Result { model = "blends", driver = a, driver2 = b, blend = w, align2 = align, rotation = q, error = error };
                }
            }
            return best;
        }

        /// <summary>The human bone the helper keeps a fixed offset from (the closest fit), if within 1 cm.</summary>
        static void Position(Transform h, Result r, Dictionary<string, Transform> map, Dictionary<Transform, Track> tracks)
        {
            var H = tracks[h];
            int n = H.pos.Count;
            float bestSpread = float.MaxValue;
            foreach (var kv in map.Where(kv => !kv.Key.Contains(" ")))
            {
                var B = tracks[kv.Value];
                var offsets = Enumerable.Range(0, n).Select(f => Quaternion.Inverse(B.rot[f]) * (H.pos[f] - B.pos[f])).ToList();
                var mean = offsets.Aggregate(Vector3.zero, (s, v) => s + v) / n;
                float spread = Rms(offsets.Select(o => (o - mean).magnitude));
                if (spread < bestSpread)
                {
                    bestSpread = spread;
                    r.positionSpread = spread;
                    r.positionBone = spread < 0.01f ? kv.Key : null;
                    r.offset = mean;
                }
            }
        }
    }
}
