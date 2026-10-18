using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RoeFighter.Fight;
using UnityEditor;
using UnityEngine;

namespace RoeFighter.EditorTools
{
    /// <summary>
    /// Builds motion packs (MotionPack): the swappable basic moves of the fight.
    ///
    ///   BuildBandai   the Bandai Namco motion capture converted by RoeMocap (pack "bandai1").
    ///   Import        any folder of humanoid clips - Asset Store packs imported as Humanoid FBX, .anim
    ///                 files, other converted datasets - described by a small JSON file:
    ///   {
    ///     "name": "my_pack", "title": "Shown in the game", "source": "...", "license": "...",
    ///     "folder": "Assets/ThirdParty/SomePack",          // searched for AnimationClips (FBX sub-assets, .anim)
    ///     "humanoid": true,                                 // set the FBX files there to Humanoid first
    ///     "roles": { "guard": "Fight_Idle", "walk": "Walk_Fwd", "walk_back": "Walk_Bwd", "run": "Run" },
    ///     "strikes": [ { "button": "A", "name": "jab", "clip": "Punch_Jab", "speed": 1.0, "damage": 40, "inPlace": false }, ... ]
    ///   }
    ///   A role written "-Clip" plays that clip backwards (UFE walks its fighters back on the forward walk at a negative
    ///   speed); "rig" names the model of clips that are not humanoid (Generic or Legacy: UFE's Mike) - they are converted
    ///   first (ConvertGeneric).
    ///   Also: "bvh" (motion capture to convert first, RoeMocap.Source), "strikesOnly" (a character's own strikes - no
    ///   stance or walks, not in the F3 list; FighterRig.strikePack), "measureOn" (the fighter the strikes are measured
    ///   on, default a08), and per strike "inPlace" (its step forward is taken out of the clip and the fight moves the
    ///   fighter along: Move.travel) and "hit" (clip seconds it can hit in, from the source's own frame data: UFE's active
    ///   frames).  Example: tools/motionpacks/doa6_mai.json (g04's strikes from DOA6's Mai).
    ///   Clips are found by name (case does not matter; "file.fbx:clip" picks one file).  Locomotion
    ///   clips are copied as loops with their travel taken out (the fight moves the body); every strike
    ///   is measured on a fighter model: which hand or foot hits, how far it reaches, when (the frames
    ///   it is within 85% of its farthest), how high.  Buttons not given in a strike get defaults
    ///   from light (A) to heavy (D).
    ///   -executeMethod RoeFighter.EditorTools.RoeMotionPacks.Import -roeSpec path\to\pack.json
    /// </summary>
    public static class RoeMotionPacks
    {
        public const string Dir = "Assets/RoeFighter/Generated/motionpacks";
        public static string PackPath(string name) => $"{Dir}/{name}.asset";

        /// <summary>All packs the fight can switch between (F3), the Bandai one first; strike-only packs are left out.</summary>
        public static List<MotionPack> All()
        {
            if (!Directory.Exists(Dir))
                return new List<MotionPack>();
            return AssetDatabase.FindAssets("t:MotionPack", new[] { Dir })
                .Select(g => AssetDatabase.LoadAssetAtPath<MotionPack>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(p => p != null && !p.strikesOnly)
                .OrderBy(p => p.name == "bandai1" ? 0 : 1).ThenBy(p => p.name).ToList();
        }

        /// <summary>A pack by name (null if it was not built).</summary>
        public static MotionPack Load(string name) => AssetDatabase.LoadAssetAtPath<MotionPack>(PackPath(name));

        // ---- the Bandai Namco motion capture (RoeMocap)

        [MenuItem("ROE Fighter/Motions/Build Bandai pack")]
        public static void BuildBandai()
        {
            var table = JsonUtility.FromJson<StrikeTable>(File.ReadAllText(RoeMocap.MovesPath));
            var pack = ScriptableObject.CreateInstance<MotionPack>();
            pack.title = "Bandai Namco motion dataset 1";
            pack.source = "Bandai Namco Research Motion Dataset 1 (https://github.com/BandaiNamcoResearchInc/Bandai-Namco-Research-Motiondataset), converted by RoeMocap";
            pack.license = "CC BY-NC 4.0, Bandai Namco Research Inc.";
            foreach (var role in MotionPack.Roles)
                Add(pack, role, AssetDatabase.LoadAssetAtPath<AnimationClip>(RoeMocap.ClipPath(role)), true);
            Move Make(string name, string button, float speed, int damage, float hitstun, float blockstun, float push, Level level, float radius, float meter)
            {
                var s = table.moves.First(x => x.name == name);
                Add(pack, name, AssetDatabase.LoadAssetAtPath<AnimationClip>(RoeMocap.ClipPath(name)), false);
                return FromStrike(s, name, button, speed, damage, hitstun, blockstun, push, level, radius, meter);
            }
            pack.strikes = new List<Move>
            {
                Make("jab", "A", 1.7f, 40, 0.32f, 0.18f, 0.25f, Level.High, 0.15f, 5f),
                Make("slash", "B", 1.6f, 60, 0.40f, 0.22f, 0.35f, Level.Mid, 0.20f, 6f),
                Make("cross", "C", 1.5f, 80, 0.45f, 0.25f, 0.45f, Level.High, 0.17f, 7f),
                Make("kick", "D", 1.7f, 95, 0.50f, 0.28f, 0.60f, Level.Mid, 0.22f, 8f),
            };
            Save(pack, "bandai1");
        }

        [Serializable]
        class StrikeTable
        {
            public List<RoeMocap.StrikeInfo> moves = new List<RoeMocap.StrikeInfo>();
        }

        static void Add(MotionPack pack, string role, AnimationClip clip, bool loop)
        {
            if (clip == null)
            {
                if (role == "guard")
                    throw new Exception($"motion pack {pack.title}: no guard clip");
                return;
            }
            pack.clips.Add(new MotionPack.Clip { role = role, clip = clip, loop = loop });
        }

        static Move FromStrike(RoeMocap.StrikeInfo s, string name, string button, float speed, int damage, float hitstun, float blockstun,
                               float push, Level level, float radius, float meter)
        {
            return new Move
            {
                name = name, button = button, clip = name, speed = speed,
                bone = (HumanBodyBones)Enum.Parse(typeof(HumanBodyBones), s.bone),
                radius = radius, hitStart = s.hitStart, hitEnd = s.hitEnd, length = s.length,
                damage = damage, hitstun = hitstun, blockstun = blockstun, push = push, level = level,
                meterGain = meter, reach = s.reach,
                recover = 0.82f, cancel = Mathf.Clamp01(s.hitEnd / Mathf.Max(0.01f, s.length) + 0.05f),
            };
        }

        static MotionPack Save(MotionPack pack, string name)
        {
            Directory.CreateDirectory(Dir);
            string path = PackPath(name);
            pack.name = name;
            // an existing pack is updated in place, so scenes that hold it keep it
            var existing = AssetDatabase.LoadAssetAtPath<MotionPack>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(pack, existing);
                EditorUtility.SetDirty(existing);
                pack = existing;
            }
            else
            {
                AssetDatabase.DeleteAsset(path);
                AssetDatabase.CreateAsset(pack, path);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[ROE] motion pack {path}: {pack.title}; roles {string.Join(" ", pack.clips.Select(c => c.role + (c.loop ? "(loop)" : "")))}; strikes " +
                      string.Join(", ", pack.strikes.Select(m => $"{m.button} {m.name} ({m.bone}, reach {m.reach:F2} m, hit {m.hitStart:F2}-{m.hitEnd:F2} of {m.length:F2} s)")));
            return pack;
        }

        // ---- any humanoid clips, from a JSON description

        [Serializable]
        public class Spec
        {
            public string name, title, source, license, folder;
            public bool humanoid = true;
            public bool strikesOnly;            // a character's own strikes only (no stance or walks; not in the F3 list)
            public bool grip;                   // strikes with the weapon in her hand: she holds it through the basic moves (a08's sword)
            public float bladeShare;            // a blade cuts while its tip moves at this share of its fastest or more (0: BladeSpeed
                                                // only); a08's 1.3 m greatsword passes 5 m/s with any move of the arm
            public string measureOn;            // the fighter the strikes are measured on (reach, when they hit); default a08
            public float walkSpeed, backSpeed;  // m/s; 0: the fight's own (MotionPack)
            public RoeMocap.Source bvh;         // motion capture to convert first (its segments become the clips)
            public string rig;                  // the model the folder's Generic / Legacy clips animate: converted to humanoid first
            public RoleMap roles = new RoleMap();
            public List<StrikeSpec> strikes = new List<StrikeSpec>();
        }

        [Serializable]
        public class RoleMap
        {
            public string guard, walk, walk_back, run;
        }

        [Serializable]
        public class StrikeSpec
        {
            public string button, name, clip, level;
            public string bone;                 // the humanoid bone that hits (LeftHand, RightFoot, ...); empty: measured. The hand
                                                // holding a blade (RoeBlade, Fiona's sword) is measured by the blade's tip
            public float from, to;              // seconds of the clip to keep (to 0: to its end) - a game's attack with its long
                                                // way back to idle (Vindictus: the swing is the first second of 2.6)
            public float[] hit;                 // seconds of the (kept) clip, at speed 1, in which it can hit - the source's own
                                                // frame data (UFE's active frames); the hand or foot is the one farthest out in
                                                // them.  Empty: measured (85% of the farthest reach - wrong for a kick that
                                                // goes up: Kyle's flip kick measured its wind-up)
            public float speed = 1f, hitstun, blockstun, push, radius, meter;
            public bool inPlace;                // the clip's travel is taken out and the fight moves the fighter along instead
                                                // (strikes that step in: DOA's lunge 0.6-0.7 m, which would jump back at the end)
            public int damage;
            public bool knockdown;
        }

        // light to heavy: A jab-like ... D kick-like (damage, hitstun, blockstun, push, radius, meter)
        static readonly Dictionary<string, (int, float, float, float, float, float)> ButtonDefaults = new Dictionary<string, (int, float, float, float, float, float)>
        {
            { "A", (40, 0.32f, 0.18f, 0.25f, 0.15f, 5f) },
            { "B", (60, 0.40f, 0.22f, 0.35f, 0.18f, 6f) },
            { "C", (80, 0.45f, 0.25f, 0.45f, 0.18f, 7f) },
            { "D", (95, 0.50f, 0.28f, 0.60f, 0.22f, 8f) },
        };

        [MenuItem("ROE Fighter/Motions/Import pack from JSON")]
        public static void Import()
        {
            string specPath = RoeCapture.Arg("-roeSpec", null);
            if (specPath == null)
                specPath = EditorUtility.OpenFilePanel("Motion pack description", Application.dataPath, "json");
            if (string.IsNullOrEmpty(specPath))
                return;
            // several packs: -roeSpec a.json,b.json
            foreach (var one in specPath.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0))
            {
                string path = Path.IsPathRooted(one) ? one : Path.Combine(Path.GetDirectoryName(Application.dataPath), one);
                ImportSpec(JsonUtility.FromJson<Spec>(File.ReadAllText(path)));
            }
        }

        public static MotionPack ImportSpec(Spec spec)
        {
            if (spec.bvh != null && spec.bvh.segments != null && spec.bvh.segments.Count > 0)
            {
                // BVH: each segment becomes a humanoid clip named after it, then on as for any clips
                spec.folder = $"{Dir}/{spec.name}/bvh";
                spec.humanoid = false;
                RoeMocap.ConvertAll(spec.bvh, n => $"{spec.folder}/{n}.anim");
            }
            if (spec.humanoid)
                MakeHumanoid(spec.folder);
            if (!string.IsNullOrEmpty(spec.rig))
            {
                // Generic / Legacy clips: humanoid copies on the rig's own avatar, then on as for any humanoid clips
                string converted = $"{Dir}/{spec.name}/humanoid";
                ConvertGeneric(spec.rig, spec.folder, converted);
                spec.folder = converted;
            }
            var found = FindClips(spec.folder);
            AnimationClip Find(string key)
            {
                if (string.IsNullOrEmpty(key))
                    return null;
                // a clip anywhere, by its asset path ("Assets/.../x.anim", "Assets/.../x.fbx:clip")
                if (key.StartsWith("Assets/"))
                {
                    var byPath = DoaFighter.PackClip(null, key);
                    if (byPath == null)
                        Debug.LogWarning($"[ROE] motion pack {spec.name}: no clip at {key}");
                    return byPath;
                }
                string file = null, clip = key;
                if (key.Contains(":"))
                {
                    file = key.Substring(0, key.IndexOf(':'));
                    clip = key.Substring(key.IndexOf(':') + 1);
                }
                var hit = found.FirstOrDefault(x => string.Equals(x.clip.name, clip, StringComparison.OrdinalIgnoreCase) &&
                                                    (file == null || Path.GetFileName(x.path).Equals(file, StringComparison.OrdinalIgnoreCase)));
                if (hit.clip == null)
                    Debug.LogWarning($"[ROE] motion pack {spec.name}: no clip '{key}' under {spec.folder}");
                return hit.clip;
            }

            string outDir = $"{Dir}/{spec.name}";
            Directory.CreateDirectory(outDir);
            var pack = ScriptableObject.CreateInstance<MotionPack>();
            pack.title = string.IsNullOrEmpty(spec.title) ? spec.name : spec.title;
            pack.source = spec.source;
            pack.license = spec.license;
            pack.walkSpeed = spec.walkSpeed;
            pack.backSpeed = spec.backSpeed;
            pack.strikesOnly = spec.strikesOnly;
            pack.grip = spec.grip;
            foreach (var (role, key) in new[] { ("guard", spec.roles.guard), ("walk", spec.roles.walk), ("walk_back", spec.roles.walk_back), ("run", spec.roles.run) })
            {
                bool backwards = key != null && key.StartsWith("-");
                var src = Find(backwards ? key.Substring(1) : key);
                if (src == null)
                    continue;
                Add(pack, role, Copy(src, $"{outDir}/{role}.anim", loop: true, inPlace: role != "guard", reverse: backwards), true);
            }
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(RoeHumanoid.FighterPath(string.IsNullOrEmpty(spec.measureOn) ? "a08" : spec.measureOn));
            foreach (var s in spec.strikes)
            {
                var src = Find(s.clip);
                if (src == null)
                    continue;
                string name = string.IsNullOrEmpty(s.name) ? s.clip : s.name;
                // the part asked for; measured as it is (reach and timing with its own step), then copied - in place if asked
                if (s.from > 0f || s.to > 0f)
                    src = Trim(src, s.from, s.to);
                var info = Measure(src, model, name, s.bone, s.hit != null && s.hit.Length == 2 ? new Vector2(s.hit[0], s.hit[1]) : (Vector2?)null, spec.grip,
                                   spec.bladeShare);
                var clip = Copy(src, $"{outDir}/{name}.anim", loop: false, inPlace: s.inPlace);
                Add(pack, name, clip, false);
                var d = ButtonDefaults.TryGetValue(s.button, out var v) ? v : ButtonDefaults["B"];
                var level = string.IsNullOrEmpty(s.level) ? (info.height > 1.2f ? Level.High : info.height < 0.5f ? Level.Low : Level.Mid)
                                                           : (Level)Enum.Parse(typeof(Level), s.level, true);
                var move = FromStrike(info, name, s.button, s.speed > 0f ? s.speed : 1f, s.damage > 0 ? s.damage : d.Item1,
                                      s.hitstun > 0f ? s.hitstun : d.Item2, s.blockstun > 0f ? s.blockstun : d.Item3,
                                      s.push > 0f ? s.push : d.Item4, level, s.radius > 0f ? s.radius : d.Item5, s.meter > 0f ? s.meter : d.Item6);
                move.knockdown = s.knockdown;
                if (s.inPlace)
                {
                    move.travel = info.travel;
                    move.travelSide = info.travelSide;
                }
                pack.strikes.Add(move);
            }
            return Save(pack, spec.name);
        }

        /// <summary>
        /// A contact sheet of a pack's strikes: a fighter plays each one, 8 frames across it, in the studio, with
        /// what was measured (which hand or foot, reach, when it can hit).  Frames to &lt;out&gt;/&lt;pack&gt;/, the table to
        /// strikes.tsv (tools/strike_sheet.py puts them on one picture).
        ///   -executeMethod RoeFighter.EditorTools.RoeMotionPacks.Sheet -roePack doa6_mai [-roeChars g04] [-roeOut dir] [-roeShots 8]
        /// </summary>
        [MenuItem("ROE Fighter/Motions/Strike contact sheet")]
        public static void Sheet()
        {
            string name = RoeCapture.Arg("-roePack", "bandai1");
            var pack = Load(name);
            if (pack == null)
                throw new Exception($"no motion pack {name} at {PackPath(name)}");
            string id = RoeCapture.Arg("-roeChars", "g04").Split(',')[0];
            string outDir = Path.Combine(RoeCapture.Arg("-roeOut", Path.Combine(Path.GetDirectoryName(Application.dataPath), "out", "motion_sheets")), name);
            int shots = int.Parse(RoeCapture.Arg("-roeShots", "8"));
            Directory.CreateDirectory(outDir);
            ShaderUtil.allowAsyncCompilation = false;
            var studio = RoeStudio.Build();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RoeHumanoid.FighterPath(id));
            var table = new System.Text.StringBuilder("clip\tbutton\tbone\treach\theight\thitStart\thitEnd\tlength\n");
            foreach (var m in pack.strikes)
            {
                var clip = pack.Get(m.clip);
                if (clip == null)
                    continue;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                // several renders in one editor update: skin again for each (else every frame shows the first pose)
                foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    smr.forceMatrixRecalculationPerRender = true;
                var animator = go.GetComponent<Animator>();
                var bones = go.GetComponentsInChildren<SkinnedMeshRenderer>(true).SelectMany(r => r.bones).Where(b => b != null).Distinct().ToArray();
                RoeCapture.Pose(go, clip, 0f);
                studio.LightFrom(Vector3.forward);
                studio.SetFocus(0f, 0f, 0f);
                var hits = animator.GetBoneTransform(m.bone);
                for (int i = 0; i < shots; i++)
                {
                    float time = clip.length * i / Mathf.Max(1, shots - 1);
                    RoeCapture.Pose(go, clip, time);
                    // the limb helpers as the fight drives them (b10's knees stayed behind on her thigh twists without)
                    go.GetComponentInChildren<RoeHelperRig>()?.Apply();
                    if (pack.grip)
                        go.GetComponent<RoeGrips>()?.Apply(animator);      // her weapon in her hand (a08's greatsword)
                    var hips = animator.GetBoneTransform(HumanBodyBones.Hips).position;
                    // from her front-right, far enough for a kick at full stretch
                    studio.Aim(new Vector3(hips.x, 0.95f, hips.z + 0.35f), Vector3.forward, 55f, 6f, 4.6f, 30f);
                    if (i == 0)
                    {
                        RoeCapture.Render(studio.camera, 240, 240, Path.Combine(outDir, "_warm.jpg"), 80);
                        RoeCapture.Render(studio.camera, 240, 240, Path.Combine(outDir, "_warm.jpg"), 80);
                    }
                    RoeCapture.Render(studio.camera, 420, 520, Path.Combine(outDir, $"{m.clip}_{i}.jpg"), 90);
                }
                RoeCapture.EndPosing();
                UnityEngine.Object.DestroyImmediate(go);
                table.Append($"{m.clip}\t{m.button}\t{m.bone}\t{m.reach:F2}\t{m.level}\t{m.hitStart:F2}\t{m.hitEnd:F2}\t{m.length:F2}\n");
            }
            File.WriteAllText(Path.Combine(outDir, "strikes.tsv"), table.ToString());
            Debug.Log($"[ROE] strike sheet of {name} on {id}: {pack.strikes.Count} strikes to {outDir}");
        }

        /// <summary>
        /// Clips without muscles (Generic or Legacy: UFE's Mike was made for the legacy Animation component) as humanoid clips:
        /// the rig's model is imported as Humanoid (an avatar from that model), every clip of the folder that is not a humanoid
        /// clip is played on an instance of it (AnimationClip.SampleAnimation writes the transforms, legacy or not) and the
        /// pose read back as muscles and body position (HumanPoseHandler), 60 keys a second, into outDir/&lt;clip&gt;.anim.
        /// Humanoid clips of the folder are copied as they are, so the pack finds every clip in outDir.
        /// </summary>
        public static void ConvertGeneric(string rigPath, string folder, string outDir)
        {
            if (AssetImporter.GetAtPath(rigPath) is ModelImporter mi &&
                (mi.animationType != ModelImporterAnimationType.Human || mi.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel))
            {
                mi.animationType = ModelImporterAnimationType.Human;
                mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                mi.SaveAndReimport();
            }
            var avatar = AssetDatabase.LoadAllAssetsAtPath(rigPath).OfType<Avatar>().FirstOrDefault();
            if (avatar == null || !avatar.isHuman || !avatar.isValid)
                throw new Exception($"{rigPath}: no valid humanoid avatar (the importer could not map its bones)");
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(rigPath);
            var go = (GameObject)UnityEngine.Object.Instantiate(model);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var animator = go.GetComponent<Animator>();
            if (animator != null)
                animator.enabled = false;
            var handler = new HumanPoseHandler(avatar, go.transform);
            var pose = new HumanPose();
            int muscles = HumanTrait.MuscleCount;
            string[] rootNames = { "RootT.x", "RootT.y", "RootT.z", "RootQ.x", "RootQ.y", "RootQ.z", "RootQ.w" };
            Directory.CreateDirectory(outDir);
            int converted = 0, copied = 0;
            foreach (var (src, _) in FindClips(folder))
            {
                string path = $"{outDir}/{src.name}.anim";
                if (src.humanMotion)
                {
                    Copy(src, path, loop: false, inPlace: false);
                    copied++;
                    continue;
                }
                int frames = Mathf.Max(1, Mathf.RoundToInt(src.length * 60f));
                var keys = new List<Keyframe>[muscles + 7];
                for (int i = 0; i < keys.Length; i++)
                    keys[i] = new List<Keyframe>(frames + 1);
                var last = Quaternion.identity;
                for (int f = 0; f <= frames; f++)
                {
                    float t = Mathf.Min(src.length, f / 60f);
                    src.SampleAnimation(go, t);
                    handler.GetHumanPose(ref pose);
                    for (int i = 0; i < muscles; i++)
                        keys[i].Add(new Keyframe(t, pose.muscles[i]));
                    var q = pose.bodyRotation;
                    if (f > 0 && Quaternion.Dot(q, last) < 0f)
                        q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                    last = q;
                    float[] r = { pose.bodyPosition.x, pose.bodyPosition.y, pose.bodyPosition.z, q.x, q.y, q.z, q.w };
                    for (int i = 0; i < 7; i++)
                        keys[muscles + i].Add(new Keyframe(t, r[i]));
                }
                var clip = new AnimationClip { name = src.name, frameRate = 60f };
                var bindings = new List<EditorCurveBinding>();
                var curves = new List<AnimationCurve>();
                for (int i = 0; i < muscles + 7; i++)
                {
                    string attribute = i < muscles ? RoeHumanoidClips.MuscleAttribute(HumanTrait.MuscleName[i]) : rootNames[i - muscles];
                    bindings.Add(EditorCurveBinding.FloatCurve("", typeof(Animator), attribute));
                    var curve = new AnimationCurve(keys[i].ToArray());
                    for (int k = 0; k < curve.length; k++)
                        curve.SmoothTangents(k, 0f);
                    curves.Add(curve);
                }
                AnimationUtility.SetEditorCurves(clip, bindings.ToArray(), curves.ToArray());
                clip.EnsureQuaternionContinuity();
                var settings = AnimationUtility.GetAnimationClipSettings(clip);
                settings.keepOriginalOrientation = true;
                settings.keepOriginalPositionY = true;
                settings.keepOriginalPositionXZ = true;
                AnimationUtility.SetAnimationClipSettings(clip, settings);
                AssetDatabase.DeleteAsset(path);
                AssetDatabase.CreateAsset(clip, path);
                converted++;
            }
            handler.Dispose();
            UnityEngine.Object.DestroyImmediate(go);
            AssetDatabase.SaveAssets();
            Debug.Log($"[ROE] {folder}: {converted} clips converted to humanoid on {rigPath} ({copied} were humanoid already) -> {outDir}");
        }

        /// <summary>The FBX files of a folder imported as Humanoid (an avatar made from each model).</summary>
        static void MakeHumanoid(string folder)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { folder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(path) is ModelImporter mi && mi.animationType != ModelImporterAnimationType.Human)
                {
                    mi.animationType = ModelImporterAnimationType.Human;
                    mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                    mi.SaveAndReimport();
                }
            }
        }

        static List<(AnimationClip clip, string path)> FindClips(string folder)
        {
            var list = new List<(AnimationClip, string)>();
            foreach (var guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { folder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (o is AnimationClip c && !c.name.StartsWith("__preview__"))
                        list.Add((c, path));
            }
            return list;
        }

        /// <summary>
        /// A writable copy of a humanoid clip; loops get loop time on, and with inPlace their travel on
        /// the floor (RootT x/z from first to last key) is taken out - the fight moves the body itself.
        /// reverse plays it backwards (the same length).
        /// </summary>
        /// <summary>The part of a clip between two times as a new clip (not saved), 60 keys a second, starting at 0.</summary>
        public static AnimationClip Trim(AnimationClip src, float from, float to)
        {
            float end = to > from ? Mathf.Min(to, src.length) : src.length;
            from = Mathf.Clamp(from, 0f, end);
            var clip = new AnimationClip { name = src.name, frameRate = src.frameRate };
            int n = Mathf.Max(1, Mathf.RoundToInt((end - from) * 60f));
            foreach (var b in AnimationUtility.GetCurveBindings(src))
            {
                var c = AnimationUtility.GetEditorCurve(src, b);
                var keys = new Keyframe[n + 1];
                for (int i = 0; i <= n; i++)
                {
                    float t = from + (end - from) * i / n;
                    keys[i] = new Keyframe(t - from, c.Evaluate(t));
                }
                var curve = new AnimationCurve(keys);
                for (int i = 0; i < curve.length; i++)
                    curve.SmoothTangents(i, 0f);
                AnimationUtility.SetEditorCurve(clip, b, curve);
            }
            var settings = AnimationUtility.GetAnimationClipSettings(src);
            settings.startTime = 0f;
            settings.stopTime = end - from;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            return clip;
        }

        public static AnimationClip Copy(AnimationClip src, string path, bool loop, bool inPlace, bool reverse = false)
        {
            var clip = new AnimationClip { frameRate = src.frameRate };
            foreach (var b in AnimationUtility.GetCurveBindings(src))
            {
                var curve = AnimationUtility.GetEditorCurve(src, b);
                if (reverse && curve.length > 0)
                {
                    var keys = curve.keys;
                    var back = new Keyframe[keys.Length];
                    for (int k = 0; k < keys.Length; k++)
                    {
                        var key = keys[keys.Length - 1 - k];
                        back[k] = new Keyframe(src.length - key.time, key.value, -key.outTangent, -key.inTangent, key.outWeight, key.inWeight)
                        {
                            weightedMode = key.weightedMode == WeightedMode.In ? WeightedMode.Out
                                         : key.weightedMode == WeightedMode.Out ? WeightedMode.In : key.weightedMode,
                        };
                    }
                    curve.keys = back;
                }
                if (inPlace && b.type == typeof(Animator) && (b.propertyName == "RootT.x" || b.propertyName == "RootT.z") && curve.length > 1)
                {
                    var keys = curve.keys;
                    float t0 = keys[0].time, t1 = keys[keys.Length - 1].time;
                    float v0 = keys[0].value, v1 = keys[keys.Length - 1].value;
                    float slope = (v1 - v0) / Mathf.Max(1e-4f, t1 - t0);
                    for (int k = 0; k < keys.Length; k++)
                    {
                        keys[k].value -= slope * (keys[k].time - t0);
                        keys[k].inTangent -= slope;
                        keys[k].outTangent -= slope;
                    }
                    curve.keys = keys;
                }
                AnimationUtility.SetEditorCurve(clip, b, curve);
            }
            var settings = AnimationUtility.GetAnimationClipSettings(src);
            settings.loopTime = loop;
            settings.keepOriginalPositionY = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(clip, path);
            return clip;
        }

        /// <summary>Metres per second at which a blade's tip cuts (Measure: a sword strike's active frames).</summary>
        public static float BladeSpeed = 5f;

        /// <summary>
        /// A strike measured on a fighter model, the way RoeMocap measures the motion capture: the hand or
        /// foot that gets farthest in front of where the hips started, how far, how high, and the frames
        /// it is within 85% of that.  window (clip seconds): the source's own active frames - the hand or foot
        /// farthest out within them, and they are when it can hit.
        /// </summary>
        public static RoeMocap.StrikeInfo Measure(AnimationClip clip, GameObject model, string name, string bone = null, Vector2? window = null, bool grip = false,
                                                  float bladeShare = 0f)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var animator = go.GetComponent<Animator>();
            // a weapon held by a grip (a08's greatsword): in her hand only for strikes made for it (MotionPack.grip)
            var held = go.GetComponent<RoeGrips>();
            var bones = string.IsNullOrEmpty(bone)
                ? new[] { HumanBodyBones.LeftHand, HumanBodyBones.RightHand, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot }
                : new[] { (HumanBodyBones)Enum.Parse(typeof(HumanBodyBones), bone, true) };
            // a blade in the striking hand reaches with its tip
            var blade = held != null && !grip ? null : go.GetComponentInChildren<RoeBlade>(true);
            var bladeHand = blade != null ? blade.hand : (HumanBodyBones?)null;     // (the instance is gone when the window is found)
            int frames = Mathf.Max(2, Mathf.CeilToInt(clip.length * 60f) + 1);
            var reach = new float[bones.Length, frames];
            var height = new float[bones.Length, frames];
            var tips = new Vector3[frames];         // a blade's tip, frame by frame
            Vector3 hips0 = Vector3.zero, hipsEnd = Vector3.zero;
            for (int f = 0; f < frames; f++)
            {
                RoeCapture.Pose(go, clip, Mathf.Min(clip.length, f / 60f));
                if (held != null && grip)
                    held.Apply(animator);
                if (f == 0)
                    hips0 = animator.GetBoneTransform(HumanBodyBones.Hips).position;
                if (f == frames - 1)
                    hipsEnd = animator.GetBoneTransform(HumanBodyBones.Hips).position;
                if (blade != null)
                    tips[f] = blade.Tip;
                for (int k = 0; k < bones.Length; k++)
                {
                    var p = blade != null && blade.hand == bones[k] ? blade.Tip : animator.GetBoneTransform(bones[k]).position;
                    reach[k, f] = p.z - hips0.z;
                    height[k, f] = p.y;
                }
            }
            RoeCapture.EndPosing();
            UnityEngine.Object.DestroyImmediate(go);
            // -roeTips dir: the blade's tip frame by frame (<dir>/<name>_tip.tsv: seconds, metres ahead of the hips' start, to the
            // side, up, m/s) - to pick a sword strike's "hit" seconds (tools/motionpacks/a08_sword.json)
            string tipsDir = RoeCapture.Arg("-roeTips", null);
            if (tipsDir != null && bladeHand.HasValue)
            {
                Directory.CreateDirectory(tipsDir);
                var tsv = new System.Text.StringBuilder("t\tahead\tside\tup\tspeed\n");
                for (int f = 0; f < frames; f++)
                    tsv.Append(FormattableString.Invariant(
                        $"{f / 60f:F3}\t{tips[f].z - hips0.z:F3}\t{tips[f].x - hips0.x:F3}\t{tips[f].y:F3}\t{(f > 0 ? (tips[f] - tips[f - 1]).magnitude * 60f : 0f):F2}\n"));
                File.WriteAllText(Path.Combine(tipsDir, $"{name}_tip.tsv"), tsv.ToString());
            }
            // the frames searched: all, or the source's active ones
            int lo = 0, hi = frames - 1;
            if (window.HasValue)
            {
                lo = Mathf.Clamp(Mathf.FloorToInt(window.Value.x * 60f), 0, frames - 1);
                hi = Mathf.Clamp(Mathf.CeilToInt(window.Value.y * 60f), lo, frames - 1);
            }
            int best = 0, peak = lo;
            for (int k = 0; k < bones.Length; k++)
                for (int f = lo; f <= hi; f++)
                    if (reach[k, f] > reach[best, peak])
                    {
                        best = k;
                        peak = f;
                    }
            float max = reach[best, peak];
            int a = peak, b = peak;
            if (window.HasValue)
            {
                a = lo;
                b = hi;
            }
            else
            {
                while (a > 0 && reach[best, a - 1] >= 0.85f * max)
                    a--;
                while (b < frames - 1 && reach[best, b + 1] >= 0.85f * max)
                    b++;
            }
            // a blade cuts while it swings: from the first to the last frame its tip moves at BladeSpeed or more (the fight
            // tests where the blade is, Fighter.ActiveHit) - a spinning cut is far out only for a moment
            if (bladeHand == bones[best] && !window.HasValue)
            {
                float fastest = 0f;
                for (int f = 1; f < frames; f++)
                    fastest = Mathf.Max(fastest, (tips[f] - tips[f - 1]).magnitude * 60f);
                float cutting = Mathf.Max(BladeSpeed, bladeShare * fastest);
                int first = -1, last = -1;
                for (int f = 1; f < frames; f++)
                    if ((tips[f] - tips[f - 1]).magnitude * 60f >= cutting)
                    {
                        if (first < 0)
                            first = f;
                        last = f;
                    }
                Debug.Log($"[ROE] {name}: blade tip at most {fastest:F1} m/s, cutting (from {cutting:F1} m/s) {first / 60f:F2}-{last / 60f:F2} s (farthest reach {a / 60f:F2}-{b / 60f:F2} s)");
                if (first >= 0)
                {
                    a = Mathf.Min(a, first);
                    b = Mathf.Max(b, last);
                }
            }
            return new RoeMocap.StrikeInfo
            {
                name = name, bone = bones[best].ToString(), length = clip.length, reach = max, height = height[best, peak],
                hitStart = a / 60f, hitPeak = peak / 60f, hitEnd = b / 60f,
                travel = hipsEnd.z - hips0.z, travelSide = hipsEnd.x - hips0.x,
            };
        }
    }
}
