using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RoeFighter.EditorTools
{
    /// <summary>
    /// Eve's own animations from Stellar Blade (user 10-17: "用 eve里自己的招式") as humanoid clips, as VdfAnims makes
    /// Fiona's.  tools/sb_anims.py export writes the CUE4Parse CLI's ActorX .psa of her clips (the 3267 bones of
    /// CH_P_EVE_01_Skeleton, 30 or 60 keys a second) under _work/sb/anim; this
    ///   - poses an instance of her fighter with a clip, frame by frame (Animator off): her Biped bones turn as in the game
    ///     (the reading of the keys that puts the bones a clip leaves alone on her bind pose, VdfAnims' four), Bip001 (the
    ///     Biped's centre of mass) and the prop bones also move; Root, where the game moves her (the _Root and P_Eve_ clips
    ///     travel 1-6 m on it, and it never turns in them), carries her whole rig along; the helpers are left to her rig,
    ///     as in every other clip she plays;
    ///   - reads the human pose off it with her fighter's avatar (her bind pose already stands in her heels), and the sword:
    ///     where the game has Bip001 Prop1 against her right hand, which keys the holder her sword hangs on under that hand
    ///     (SB_Sword, VdfFighter.AttachWeapons with tools/sb_weapons.py's list) - so the sword follows her hand in her
    ///     strikes and leaves it where a skill throws it;
    ///   - writes Assets/SB/&lt;id&gt;/anims/&lt;clip&gt;.anim (the root's travel kept: a motion pack takes it out of walks and
    ///     strikes, RoeMotionPacks.Copy) and, for the clips her fight definition names under anims/inplace/, a copy with Root
    ///     held where it starts;
    ///   - checks itself: per clip, how far her hands, feet, head and the sword's tip are from the game's pose when the clip
    ///     plays back on her fighter (cm, mean / worst).
    /// Axes: her bones' frames as Fiona's (VdfAnims.Local: the same PSK import and FBX export); the component space: Stellar
    /// Blade's characters face +x, which tools/sb_fbx.py turned to -y in Blender, so Unreal's (x, y, z) is her model's
    /// (y, z, x) - forward z.  CUE4Parse writes a .psa's rate as keys / length (91 keys over 3 s: 30.33): the keys are
    /// length / (keys - 1) apart.
    ///   -executeMethod RoeFighter.EditorTools.SbAnims.Import [-roeSb eve_nikke06] [-roeOnly regex]
    ///       [-roeSrc SB/Content/Art/Character/PC] [-roeCalib Proto_Battle_Idle]
    /// -roeSrc: the game folder under _work/sb/anim whose clips are hers (10-18: Raven's are
    /// SB/Content/Art/Character/Monster/CH_M_NA_53/Animation - her Result_* clips have Eve's names); -roeCalib: a clip she
    /// stands in (Raven: M_Raven_BattleIdle01).
    /// </summary>
    public static class SbAnims
    {
        public static string ClipDir(string id) => $"{SbFighter.Dir(id)}/anims";
        public static string InPlaceDir(string id) => $"{ClipDir(id)}/inplace";

        static string SourceDir => Path.Combine(Path.GetDirectoryName(Application.dataPath), "_work", "sb", "anim");
        const string Loops = "Idle|_Walk_|_Run_|_Loop$|KnockDown_L$|_Caution_|Groggy_L$";
        public const string Holder = "SB_Sword", Prop = "Bip001 Prop1", Hand = "Bip001 R Hand";

        static string Mine(string game) => game.StartsWith("Bip001-") ? game.Replace('-', ' ') : game;
        static Vector3 World(Vector3 p) => new Vector3(p.y, p.z, p.x) * 0.01f;

        class Rig
        {
            public GameObject go;
            public string[] names;
            public Transform[] target;
            public int root = -1;
            public readonly HashSet<int> moving = new HashSet<int>();
            public readonly Dictionary<Transform, (Vector3 p, Quaternion q)> bind = new Dictionary<Transform, (Vector3, Quaternion)>();
            public Transform hand, prop;
            public Vector3 rootBindWorld;
        }

        static Rig Bind(GameObject go, VdfAnims.Psa psa)
        {
            var top = go.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Root");
            var byName = new Dictionary<string, Transform>();
            foreach (var t in (top != null ? top : go.transform).GetComponentsInChildren<Transform>(true))
                if (!byName.ContainsKey(t.name))
                    byName[t.name] = t;
            var rig = new Rig { go = go, names = psa.bones, target = new Transform[psa.bones.Length] };
            for (int b = 0; b < psa.bones.Length; b++)
            {
                string n = psa.bones[b];
                if (n != "Root" && !n.StartsWith("Bip001"))
                    continue;
                if (!byName.TryGetValue(Mine(n), out var t))
                    continue;
                rig.target[b] = t;
                rig.bind[t] = (t.localPosition, t.localRotation);
                if (n == "Root")
                    rig.root = b;
                else if (n == "Bip001" || n.StartsWith("Bip001-Prop"))
                    rig.moving.Add(b);
            }
            byName.TryGetValue(Hand, out rig.hand);
            byName.TryGetValue(Prop, out rig.prop);
            if (rig.root >= 0)
                rig.rootBindWorld = rig.target[rig.root].position;
            return rig;
        }

        static void Pose(Rig rig, VdfAnims.Psa psa, int f, int reading, bool holdRoot)
        {
            var character = rig.go.transform;
            for (int b = 0; b < rig.target.Length; b++)
            {
                var t = rig.target[b];
                if (t == null)
                    continue;
                if (b == rig.root)
                {
                    var p = VdfAnims.Ue(psa.P(f, b), reading);
                    if (holdRoot)
                    {
                        var start = VdfAnims.Ue(psa.P(0, b), reading);
                        p = new Vector3(start.x, start.y, p.z);
                    }
                    t.position = rig.rootBindWorld + character.TransformVector(World(p));
                    continue;
                }
                t.localRotation = VdfAnims.Local(VdfAnims.Ue(psa.Q(f, b), reading));
                if (rig.moving.Contains(b))
                    t.localPosition = VdfAnims.Local(VdfAnims.Ue(psa.P(f, b), reading));
            }
        }

        static void Restore(Rig rig)
        {
            foreach (var kv in rig.bind)
            {
                kv.Key.localPosition = kv.Value.p;
                kv.Key.localRotation = kv.Value.q;
            }
        }

        static float Median(List<float> v)
        {
            if (v.Count == 0)
                return float.NaN;
            v.Sort();
            return v[v.Count / 2];
        }

        /// <summary>
        /// Which reading of the keys is Unreal's own (VdfAnims' four: as written, conjugated, mirrored in Y, both).  Fiona's
        /// test (the bones a clip leaves alone are in the reference pose) does not hold here: her fingers hold the sword the
        /// whole clip.  Mirrored or not: her bones' offsets from their parents never change, so the keys' places must be her
        /// bind pose's; conjugated or not: the reading that stands her up (head well above her feet, as a forward kinematics
        /// of the clip's keys in tools/sb_anims.py's files does with the children's rotations conjugated).
        /// </summary>
        static int Calibrate(Rig rig, VdfAnims.Psa psa, out string note)
        {
            var fixedBones = Enumerable.Range(0, psa.bones.Length).Where(b => rig.target[b] != null && b != rig.root && !rig.moving.Contains(b)).ToList();
            float Off(int r) => Median(fixedBones.Select(b => Vector3.Distance(VdfAnims.Local(VdfAnims.Ue(psa.P(0, b), r)), rig.bind[rig.target[b]].p)).ToList());
            int mirror = Off(2) < Off(0) ? 2 : 0;
            Transform Find(string n) => rig.target.FirstOrDefault(t => t != null && t.name == n);
            Transform head = Find("Bip001 Head"), left = Find("Bip001 L Foot"), right = Find("Bip001 R Foot");
            var up = new float[2];
            for (int c = 0; c < 2; c++)
            {
                Pose(rig, psa, 0, mirror + c, false);
                up[c] = head.position.y - 0.5f * (left.position.y + right.position.y);
                Restore(rig);
            }
            int best = mirror + (up[1] > up[0] ? 1 : 0);
            note = $"{psa.name}, key 0: her {fixedBones.Count} bones' offsets {1000f * Off(0):F1} mm off as written, {1000f * Off(2):F1} mm mirrored; " +
                   $"head above her feet {100f * up[0]:F0} cm as {(mirror == 2 ? "mirrored" : "written")}, {100f * up[1]:F0} cm conjugated -> keys read {VdfAnims.Readings[best]}";
            return best;
        }

        static readonly string[] RootNames = { "RootT.x", "RootT.y", "RootT.z", "RootQ.x", "RootQ.y", "RootQ.z", "RootQ.w" };
        static readonly string[] HolderCurves = { "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w",
                                                  "m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z" };

        static float Step(VdfAnims.Psa psa) => psa.frames / (Mathf.Max(1f, psa.rate) * Mathf.Max(1, psa.frames - 1));

        static AnimationClip Convert(Rig rig, VdfAnims.Psa psa, int reading, HumanPoseHandler handler, bool holdRoot, bool loop,
                                     string holderPath, out string limits)
        {
            int muscles = HumanTrait.MuscleCount;
            var keys = new List<Keyframe>[muscles + 7];
            for (int i = 0; i < keys.Length; i++)
                keys[i] = new List<Keyframe>(psa.frames);
            var holder = Enumerable.Range(0, 7).Select(_ => new List<Keyframe>(psa.frames)).ToArray();
            bool sword = holderPath != null && rig.hand != null && rig.prop != null;
            var pose = new HumanPose();
            var last = Quaternion.identity;
            var lastHolder = Quaternion.identity;
            float step = Step(psa);
            for (int f = 0; f < psa.frames; f++)
            {
                Pose(rig, psa, f, reading, holdRoot);
                handler.GetHumanPose(ref pose);
                float t = f * step;
                for (int i = 0; i < muscles; i++)
                    keys[i].Add(new Keyframe(t, pose.muscles[i]));
                var q = pose.bodyRotation;
                if (f > 0 && Quaternion.Dot(q, last) < 0f)
                    q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                last = q;
                float[] r = { pose.bodyPosition.x, pose.bodyPosition.y, pose.bodyPosition.z, q.x, q.y, q.z, q.w };
                for (int i = 0; i < 7; i++)
                    keys[muscles + i].Add(new Keyframe(t, r[i]));
                if (sword)
                {
                    // the prop bone against her hand, as the holder under the hand takes it
                    var hq = Quaternion.Inverse(rig.hand.rotation) * rig.prop.rotation;
                    if (f > 0 && Quaternion.Dot(hq, lastHolder) < 0f)
                        hq = new Quaternion(-hq.x, -hq.y, -hq.z, -hq.w);
                    lastHolder = hq;
                    var hp = rig.hand.InverseTransformPoint(rig.prop.position);
                    float[] v = { hq.x, hq.y, hq.z, hq.w, hp.x, hp.y, hp.z };
                    for (int c = 0; c < 7; c++)
                        holder[c].Add(new Keyframe(t, v[c]));
                }
            }
            Restore(rig);
            var over = Enumerable.Range(0, muscles).Select(i => (name: HumanTrait.MuscleName[i], max: keys[i].Max(k => Mathf.Abs(k.value)),
                                                                   share: keys[i].Count(k => Mathf.Abs(k.value) > 0.995f) / (float)keys[i].Count))
                .Where(m => m.max > 0.995f && !Regex.IsMatch(m.name, "Thumb|Index|Middle|Ring|Little")).OrderByDescending(m => m.share).ToList();
            limits = over.Count == 0 ? "none" : string.Join(", ", over.Take(8).Select(m => $"{m.name} {m.max:F2} ({100f * m.share:F0}%)"));
            var clip = new AnimationClip { name = psa.name, frameRate = Mathf.Round(1f / step) };
            var bindings = new List<EditorCurveBinding>();
            var curves = new List<AnimationCurve>();
            AnimationCurve Smooth(List<Keyframe> list)
            {
                var curve = new AnimationCurve(list.ToArray());
                for (int k = 0; k < curve.length; k++)
                    curve.SmoothTangents(k, 0f);
                return curve;
            }
            for (int i = 0; i < keys.Length; i++)
            {
                string attribute = i < muscles ? RoeHumanoidClips.MuscleAttribute(HumanTrait.MuscleName[i]) : RootNames[i - muscles];
                bindings.Add(EditorCurveBinding.FloatCurve("", typeof(Animator), attribute));
                curves.Add(Smooth(keys[i]));
            }
            if (sword)
                for (int c = 0; c < 7; c++)
                {
                    bindings.Add(EditorCurveBinding.FloatCurve(holderPath, typeof(Transform), HolderCurves[c]));
                    curves.Add(Smooth(holder[c]));
                }
            AnimationUtility.SetEditorCurves(clip, bindings.ToArray(), curves.ToArray());
            clip.EnsureQuaternionContinuity();
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loop;
            settings.loopBlend = false;
            settings.loopBlendOrientation = true;
            settings.keepOriginalOrientation = true;
            settings.loopBlendPositionY = true;
            settings.keepOriginalPositionY = true;
            settings.loopBlendPositionXZ = true;
            settings.keepOriginalPositionXZ = true;
            settings.heightFromFeet = false;
            settings.mirror = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            return clip;
        }

        static readonly string[] Checked = { "Bip001 L Hand", "Bip001 R Hand", "Bip001 L Foot", "Bip001 R Foot", "Bip001 Head" };

        /// <summary>The humanoid clip played on her fighter against the game's pose, every 6th key: her hands, feet, head and
        /// the sword's tip, mean / worst apart in cm.</summary>
        static string RoundTrip(Rig rig, VdfAnims.Psa psa, int reading, bool holdRoot, AnimationClip clip, GameObject player, Vector3 tip)
        {
            var mine = player.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
            var game = rig.go.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
            var names = Checked.Concat(new[] { "sword tip" }).ToArray();
            var sum = new double[names.Length];
            var worst = new float[names.Length];
            int count = 0;
            float step = Step(psa);
            mine.TryGetValue(Holder, out var holder);
            for (int f = 0; f < psa.frames; f += 6)
            {
                Pose(rig, psa, f, reading, holdRoot);
                var want = Checked.Select(n => game[n].position).ToList();
                want.Add(rig.prop != null ? rig.prop.TransformPoint(tip) : Vector3.zero);
                RoeCapture.Pose(player, clip, Mathf.Min(clip.length, f * step));
                var got = Checked.Select(n => mine[n].position).ToList();
                got.Add(holder != null ? holder.TransformPoint(tip) : Vector3.zero);
                for (int i = 0; i < names.Length; i++)
                {
                    float d = Vector3.Distance(want[i], got[i]);
                    sum[i] += d;
                    worst[i] = Mathf.Max(worst[i], d);
                }
                count++;
            }
            Restore(rig);
            return string.Join(", ", Enumerable.Range(0, names.Length).Select(i =>
                $"{names[i].Replace("Bip001 ", "")} {100.0 * sum[i] / Mathf.Max(1, count):F1}/{100f * worst[i]:F1}"));
        }

        [MenuItem("ROE Fighter/Motions/Stellar Blade: import Eve's own animations")]
        public static void Import()
        {
            foreach (var id in RoeCapture.Arg("-roeSb", "eve_nikke06").Split(','))
                Import(id.Trim(), RoeCapture.Arg("-roeOnly", ""), RoeCapture.Arg("-roeSrc", EveSource), RoeCapture.Arg("-roeCalib", "Proto_Battle_Idle"));
            AssetDatabase.SaveAssets();
        }

        const string EveSource = "SB/Content/Art/Character/PC";

        public static void Import(string id, string only) => Import(id, only, EveSource, "Proto_Battle_Idle");

        public static void Import(string id, string only, string src, string calibName)
        {
            string from = Path.Combine(SourceDir, src.Replace('/', Path.DirectorySeparatorChar));
            var files = Directory.Exists(from)
                ? Directory.GetFiles(from, "*.psa", SearchOption.AllDirectories).OrderBy(p => Path.GetFileName(p)).ToList()
                : new List<string>();
            if (!string.IsNullOrEmpty(only))
                files = files.Where(p => Regex.IsMatch(Path.GetFileNameWithoutExtension(p), only, RegexOptions.IgnoreCase)).ToList();
            if (files.Count == 0)
            {
                Debug.LogError($"[ROE] {id}: no .psa under {from} - run tools/sb_anims.py export first");
                return;
            }
            var definition = File.Exists(DoaFighter.DefinitionPath(id)) ? File.ReadAllText(DoaFighter.DefinitionPath(id)) : "";
            var inPlace = new HashSet<string>(Regex.Matches(definition, @"anims/inplace/([A-Za-z0-9_]+)\.anim").Cast<Match>().Select(m => m.Groups[1].Value));
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RoeHumanoid.FighterPath(id));
            if (prefab == null)
            {
                Debug.LogError($"[ROE] {id}: no fighter {RoeHumanoid.FighterPath(id)} - SbFighter.Build first");
                return;
            }
            var log = new StringBuilder();
            var source = (GameObject)Object.Instantiate(prefab);
            source.name = prefab.name;
            source.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            foreach (var a in source.GetComponentsInChildren<Animator>(true))
                a.enabled = false;
            var avatar = prefab.GetComponent<Animator>().avatar;
            var handler = new HumanPoseHandler(avatar, source.transform);
            var player = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            player.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            // the sword: the holder's path (the clips key it) and its tip (the mesh's farthest point from the holder)
            var holderT = player.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == Holder);
            string holderPath = holderT != null ? AnimationUtility.CalculateTransformPath(holderT, player.transform) : null;
            var swordMesh = holderT != null ? holderT.GetComponentInChildren<MeshFilter>()?.sharedMesh : null;
            var tip = swordMesh != null ? swordMesh.vertices.OrderByDescending(v => v.sqrMagnitude).First() : Vector3.zero;
            log.AppendLine(holderPath != null
                ? $"{id}: the sword's holder {holderPath}, its tip {100f * tip.magnitude:F0} cm from it"
                : $"{id}: NO sword holder ({Holder}: tools/sb_weapons.py, then SbFighter.Build) - the clips get no sword keys");

            Directory.CreateDirectory(ClipDir(id));
            Directory.CreateDirectory(InPlaceDir(id));
            int reading = -1, done = 0;
            Rig rig = null;
            // the reading of the keys, on a clip she stands in
            {
                string standing = Directory.GetFiles(from, "*.psa", SearchOption.AllDirectories)
                    .FirstOrDefault(p => Path.GetFileNameWithoutExtension(p) == calibName) ?? files[0];
                var calib = VdfAnims.Read(standing);
                var calibRig = Bind(source, calib);
                reading = Calibrate(calibRig, calib, out string note);
                log.AppendLine($"{id}: {calib.bones.Length} bones in the clips, {calibRig.target.Count(t => t != null)} of them her Biped's; {note}");
            }
            foreach (var path in files)
            {
                var psa = VdfAnims.Read(path);
                if (rig == null || !rig.names.SequenceEqual(psa.bones))
                {
                    rig = Bind(source, psa);
                    if (rig.root < 0)
                    {
                        log.AppendLine($"{psa.name}: no Root of hers - left out");
                        rig = null;
                        continue;
                    }
                }
                bool loop = Regex.IsMatch(psa.name, Loops);
                var clip = Convert(rig, psa, reading, handler, holdRoot: false, loop, holderPath, out string limits);
                string clipPath = $"{ClipDir(id)}/{psa.name}.anim";
                AssetDatabase.DeleteAsset(clipPath);
                AssetDatabase.CreateAsset(clip, clipPath);
                var travel = World(VdfAnims.Ue(psa.P(psa.frames - 1, rig.root), reading)) - World(VdfAnims.Ue(psa.P(0, rig.root), reading));
                string check = RoundTrip(rig, psa, reading, false, clip, player, tip);
                log.AppendLine($"{psa.name}: {psa.frames} keys {clip.length:F2} s{(loop ? " loop" : "")}, Root travels forward {travel.z:+0.00;-0.00} " +
                               $"right {travel.x:+0.00;-0.00} m; played back on her, cm mean/worst: {check}; muscles at their limits: {limits}");
                if (inPlace.Contains(psa.name))
                {
                    var still = Convert(rig, psa, reading, handler, holdRoot: true, loop, holderPath, out _);
                    string stillPath = $"{InPlaceDir(id)}/{psa.name}.anim";
                    AssetDatabase.DeleteAsset(stillPath);
                    AssetDatabase.CreateAsset(still, stillPath);
                    log.AppendLine($"  + in place: {stillPath}");
                }
                done++;
            }
            RoeCapture.EndPosing();
            handler.Dispose();
            Object.DestroyImmediate(source);
            Object.DestroyImmediate(player);
            AssetDatabase.SaveAssets();
            string report = Path.Combine(Path.GetDirectoryName(Application.dataPath), "_work", "sb", $"{id}_anims_import.txt");
            File.WriteAllText(report, log.ToString());
            Debug.Log($"[ROE] {id}: {done} of her Stellar Blade animations -> {ClipDir(id)} ({inPlace.Count} in place); report {report}\n{log}");
        }
    }
}
