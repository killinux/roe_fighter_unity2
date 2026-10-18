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
    /// Fiona's own animations from Vindictus: Defying Fate (her moves were the next step offered; user 10-04 "继续") as
    /// humanoid clips.  tools/vdf_anims.py export writes UE Viewer's ActorX .psa of every AnimSequence of hers (191, 60 fps,
    /// the 1359 bones of SK_PCF_BaseBody01_Skeleton) under _work/vdf/anim_umodel; this
    ///   - reads a .psa and poses an instance of her fighter with it, frame by frame (Animator off): every bone of the game's
    ///     skeleton she has turns as in the game, the pelvis and the root also move as in the game, the other bones keep the
    ///     lengths of her mesh;
    ///   - reads the human pose off that instance with a flat-footed copy of her avatar (her fighter's avatar stands in her
    ///     heels, RoeHumanoid.StandOnHeels: a flat game foot then comes out tipped onto her heels, as every clip she plays);
    ///   - writes Assets/VDF/&lt;id&gt;/anims/&lt;clip&gt;.anim, 60 keys a second, the root's travel kept (a motion pack takes it
    ///     out of walks and strikes and the fight moves her along, RoeMotionPacks.Copy), and for the clips her fight
    ///     definition plays as they are (tools/vdf/&lt;id&gt;.json names anims/inplace/&lt;clip&gt;.anim) a copy with the root held
    ///     where it starts (the game's root carries the travel: 2-4 m in a skill, 6 m when she is knocked flying);
    ///   - checks itself: how UE Viewer's keys read (the bones a clip leaves alone must come out in her bind pose), where the
    ///     root bone lands, and per clip how far her hands, feet and head are from the game's pose when the humanoid clip is
    ///     played back on her fighter.
    /// Axes: Unreal's bone frames become hers as (-x, -y, z), its component space as (-x, z, y) (RoeKawaiiPhysics.FromUe),
    /// centimetres become metres.  By default the traversal, ladder, slope, jump and long dance clips are left out
    /// (-roeOnly takes a regular expression of clip names instead).
    ///   -executeMethod RoeFighter.EditorTools.VdfAnims.Import [-roeVdf fio005] [-roeOnly regex]
    /// </summary>
    public static class VdfAnims
    {
        public static string ClipDir(string id) => $"Assets/VDF/{id}/anims";
        public static string InPlaceDir(string id) => $"{ClipDir(id)}/inplace";

        static string SourceDir => Path.Combine(Path.GetDirectoryName(Application.dataPath), "_work", "vdf", "anim_umodel");

        // left out by default: traversal, slopes, jumps, the long dances, the Biped versions, and Run_F / _L / _R (21-frame poses
        // for a blend, the shoulders far out of range)
        const string Skip = "Interactive|Ladder|Narrow|Campsite|Quick_|Slope|_Jump_|_BIP$|_AS_TEST|Emo_Dance|Emo_Followme|Emo_Rest|Drink_Potion|_Run_[FLR]$";
        const string Loops = "_Loop$|[Ii]dle$|_During$";

        internal class Psa
        {
            public string name;
            public string[] bones;
            public int frames;
            public float rate;
            public float[] keys;    // frame by frame, bone by bone: px py pz qx qy qz qw time

            public Quaternion Q(int f, int b)
            {
                int o = (f * bones.Length + b) * 8;
                return new Quaternion(keys[o + 3], keys[o + 4], keys[o + 5], keys[o + 6]);
            }

            public Vector3 P(int f, int b)
            {
                int o = (f * bones.Length + b) * 8;
                return new Vector3(keys[o], keys[o + 1], keys[o + 2]);
            }
        }

        /// <summary>An ActorX .psa as UE Viewer writes it: BONENAMES (120 bytes each), ANIMINFO (one, 168 bytes), ANIMKEYS (32 bytes).</summary>
        internal static Psa Read(string path)
        {
            var data = File.ReadAllBytes(path);
            var psa = new Psa { name = Path.GetFileNameWithoutExtension(path) };
            for (int off = 0; off + 32 <= data.Length;)
            {
                string id = Encoding.ASCII.GetString(data, off, 20).Split('\0')[0];
                int size = BitConverter.ToInt32(data, off + 24), count = BitConverter.ToInt32(data, off + 28), body = off + 32;
                if (id == "BONENAMES")
                    psa.bones = Enumerable.Range(0, count).Select(i => Encoding.ASCII.GetString(data, body + i * size, 64).Split('\0')[0]).ToArray();
                else if (id == "ANIMINFO")
                {
                    psa.rate = BitConverter.ToSingle(data, body + 152);
                    psa.frames = BitConverter.ToInt32(data, body + 164);
                }
                else if (id == "ANIMKEYS")
                {
                    psa.keys = new float[count * 8];
                    Buffer.BlockCopy(data, body, psa.keys, 0, count * 32);
                }
                off = body + size * count;
            }
            if (psa.bones == null || psa.keys == null || psa.frames <= 0 || psa.keys.Length < psa.frames * psa.bones.Length * 8)
                throw new Exception($"{path}: not a complete .psa");
            return psa;
        }

        // A key read as Unreal's own local rotation: 0 as written, 1 conjugated, 2 mirrored in Y, 3 mirrored and conjugated
        // (ActorX: 3ds Max is right-handed and stores child rotations inverted).  The root is never conjugated.
        internal static readonly string[] Readings = { "as written", "conjugated", "mirrored in Y", "mirrored in Y + conjugated" };

        internal static Quaternion Ue(Quaternion k, int reading) =>
            reading == 0 ? k :
            reading == 1 ? new Quaternion(-k.x, -k.y, -k.z, k.w) :
            reading == 2 ? new Quaternion(-k.x, k.y, -k.z, k.w) :
                           new Quaternion(k.x, -k.y, k.z, k.w);

        internal static Vector3 Ue(Vector3 p, int reading) => reading >= 2 ? new Vector3(p.x, -p.y, p.z) : p;

        // Unreal bone space -> the bone's frame in her rig: a half turn about z, (-x, -y, z)
        internal static Quaternion Local(Quaternion q) => new Quaternion(-q.x, -q.y, q.z, q.w);
        internal static Vector3 Local(Vector3 p) => new Vector3(-p.x, -p.y, p.z) * 0.01f;

        // Unreal component space -> her model's: (-x, z, y), a half turn about (0, 1, 1); the bone frame turned as above
        static readonly Quaternion WorldTurn = new Quaternion(0f, 0.70710678f, 0.70710678f, 0f);
        static readonly Quaternion BoneTurn = new Quaternion(0f, 0f, 1f, 0f);
        static Quaternion World(Quaternion q) => WorldTurn * q * Quaternion.Inverse(BoneTurn);
        static Vector3 World(Vector3 p) => new Vector3(-p.x, p.z, p.y) * 0.01f;

        /// <summary>
        /// The bones her weapons hang on (VdfFighter.AttachWeapons): a humanoid clip has no muscle for them, so each clip
        /// also gets their own local turn and place as plain transform curves (user 10-05 "继续": the shield's animation was
        /// next; the game holds shield_l 22.9 degrees off its bind pose in nearly every clip, up to 120 in some).
        /// -roeProps a,b names others ("-": none).
        /// </summary>
        public static readonly string[] PropBones = { "weapon_r", "weapon_l", "shield_l" };

        class Rig
        {
            public GameObject go;
            public string[] names;          // the .psa's bones
            public Transform[] target;      // per .psa bone (null: she has no such bone)
            public int root = -1, pelvis = -1;
            public Dictionary<Transform, (Vector3 p, Quaternion q)> bind = new Dictionary<Transform, (Vector3, Quaternion)>();
            public readonly List<(int bone, Transform t, string path)> props = new List<(int, Transform, string)>();
            public readonly HashSet<int> propBones = new HashSet<int>();
        }

        static string[] Props()
        {
            string arg = RoeCapture.Arg("-roeProps", null);
            return arg == null ? PropBones : arg == "-" ? new string[0] : arg.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
        }

        static Rig Bind(GameObject go, Psa psa)
        {
            var rig = new Rig { go = go, names = psa.bones, target = new Transform[psa.bones.Length] };
            var rootBone = go.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "root");
            var byName = new Dictionary<string, Transform>();
            foreach (var t in (rootBone != null ? rootBone : go.transform).GetComponentsInChildren<Transform>(true))
                if (!byName.ContainsKey(t.name))
                    byName[t.name] = t;
            for (int b = 0; b < psa.bones.Length; b++)
            {
                if (!byName.TryGetValue(psa.bones[b], out var t))
                    continue;
                rig.target[b] = t;
                rig.bind[t] = (t.localPosition, t.localRotation);
                if (psa.bones[b] == "root")
                    rig.root = b;
                else if (psa.bones[b] == "pelvis")
                    rig.pelvis = b;
            }
            foreach (var name in Props())
            {
                int b = Array.IndexOf(psa.bones, name);
                if (b < 0 || rig.target[b] == null)
                    continue;
                rig.props.Add((b, rig.target[b], AnimationUtility.CalculateTransformPath(rig.target[b], go.transform)));
                rig.propBones.Add(b);
            }
            return rig;
        }

        static void Pose(Rig rig, Psa psa, int f, int reading, bool holdRoot)
        {
            var character = rig.go.transform;
            for (int b = 0; b < rig.target.Length; b++)
            {
                var t = rig.target[b];
                if (t == null)
                    continue;
                if (b == rig.root)
                {
                    var p = Ue(psa.P(f, b), reading);
                    if (holdRoot)
                    {
                        var start = Ue(psa.P(0, b), reading);
                        p = new Vector3(start.x, start.y, p.z);
                    }
                    t.SetPositionAndRotation(character.TransformPoint(World(p)), character.rotation * World(Ue(psa.Q(f, b), reading & 2)));
                    continue;
                }
                t.localRotation = Local(Ue(psa.Q(f, b), reading));
                if (b == rig.pelvis || rig.propBones.Contains(b))      // a weapon also moves in its socket (the shield's bash)
                    t.localPosition = Local(Ue(psa.P(f, b), reading));
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
        /// Which reading of the keys is Unreal's own: the bones the clip leaves alone (first, middle and last frame alike) are
        /// in the skeleton's reference pose and must come out as her bind pose.  Returns the reading and a note.
        /// </summary>
        static int Calibrate(Rig rig, Psa psa, out string note)
        {
            var still = new List<int>();
            for (int b = 0; b < psa.bones.Length; b++)
            {
                if (rig.target[b] == null || b == rig.root)
                    continue;
                var q0 = psa.Q(0, b);
                if (Quaternion.Angle(q0, psa.Q(psa.frames / 2, b)) < 0.01f && Quaternion.Angle(q0, psa.Q(psa.frames - 1, b)) < 0.01f)
                    still.Add(b);
            }
            var angle = new float[4];
            var distance = new float[4];
            for (int r = 0; r < 4; r++)
            {
                angle[r] = Median(still.Select(b => Quaternion.Angle(Local(Ue(psa.Q(0, b), r)), rig.bind[rig.target[b]].q)).ToList());
                distance[r] = Median(still.Select(b => Vector3.Distance(Local(Ue(psa.P(0, b), r)), rig.bind[rig.target[b]].p)).ToList());
            }
            int best = Enumerable.Range(0, 4).OrderBy(r => angle[r] + distance[r] * 100f).First();
            note = $"{still.Count} bones left alone by {psa.name}: median off her bind pose " +
                   string.Join(", ", Enumerable.Range(0, 4).Select(r => $"{Readings[r]} {angle[r]:F2} deg / {1000f * distance[r]:F1} mm")) +
                   $" -> keys read {Readings[best]}";
            if (rig.root >= 0)
            {
                var t = rig.target[rig.root];
                var wantQ = World(Ue(psa.Q(0, rig.root), best & 2));
                var wantP = World(Ue(psa.P(0, rig.root), best));
                var parent = t.parent;
                var bindWorldQ = (parent != null ? parent.rotation : Quaternion.identity) * rig.bind[t].q;
                var bindWorldP = parent != null ? parent.TransformPoint(rig.bind[t].p) : rig.bind[t].p;
                note += $"; root bone: frame 0 {Quaternion.Angle(wantQ, bindWorldQ):F2} deg / {1000f * Vector3.Distance(wantP, bindWorldP):F1} mm from her bind pose";
            }
            return best;
        }

        /// <summary>
        /// Her avatar on a T-posed copy, without the heels (RoeHumanoid.StandOnHeels left out: the clips are read with it) or
        /// with them (as her fighter's); twist: the distribution to use (null: the fighter's).
        /// </summary>
        static Avatar MakeAvatar(GameObject fighter, bool heels, float[] twist)
        {
            var posed = Object.Instantiate(fighter);
            posed.name = fighter.name;
            posed.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var map = RoeHumanoid.MapBones(posed);
            RoeHumanoid.EnforceTPose(posed, map);
            if (heels)
                RoeHumanoid.StandOnHeels(posed, map);
            var avatar = RoeHumanoid.BuildAvatar(posed, map, twist);
            Object.DestroyImmediate(posed);
            if (!avatar.isValid || !avatar.isHuman)
                throw new Exception($"{fighter.name}: the avatar ({(heels ? "heels" : "flat")}) is not valid");
            return avatar;
        }

        static string LastLimits = "";

        static readonly string[] RootNames = { "RootT.x", "RootT.y", "RootT.z", "RootQ.x", "RootQ.y", "RootQ.z", "RootQ.w" };
        static readonly string[] PropCurves = { "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w",
                                                "m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z" };

        static AnimationClip Convert(Rig rig, Psa psa, int reading, HumanPoseHandler handler, bool holdRoot, bool loop)
        {
            int muscles = HumanTrait.MuscleCount;
            var keys = new List<Keyframe>[muscles + 7];
            for (int i = 0; i < keys.Length; i++)
                keys[i] = new List<Keyframe>(psa.frames);
            var pose = new HumanPose();
            var last = Quaternion.identity;
            // the weapon bones: local rotation (x y z w) and position (x y z) per frame
            var propKeys = rig.props.Select(_ => Enumerable.Range(0, 7).Select(__ => new List<Keyframe>(psa.frames)).ToArray()).ToArray();
            var propLast = rig.props.Select(p => p.t.localRotation).ToArray();
            float step = 1f / Mathf.Max(1f, psa.rate);
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
                for (int j = 0; j < rig.props.Count; j++)
                {
                    var bone = rig.props[j].t;
                    var pq = bone.localRotation;
                    if (Quaternion.Dot(pq, propLast[j]) < 0f)
                        pq = new Quaternion(-pq.x, -pq.y, -pq.z, -pq.w);
                    propLast[j] = pq;
                    var pp = bone.localPosition;
                    float[] v = { pq.x, pq.y, pq.z, pq.w, pp.x, pp.y, pp.z };
                    for (int c = 0; c < 7; c++)
                        propKeys[j][c].Add(new Keyframe(t, v[c]));
                }
            }
            Restore(rig);
            // muscles at or past their limits (the avatar's range: a pose beyond it does not come back)
            var over = Enumerable.Range(0, muscles).Select(i => (name: HumanTrait.MuscleName[i], max: keys[i].Max(k => Mathf.Abs(k.value)),
                                                                   share: keys[i].Count(k => Mathf.Abs(k.value) > 0.995f) / (float)keys[i].Count))
                .Where(m => m.max > 0.995f).OrderByDescending(m => m.share).ToList();
            // fingers apart (their spread is past its range in her rest pose already)
            over = over.Where(m => !Regex.IsMatch(m.name, "Thumb|Index|Middle|Ring|Little")).ToList();
            LastLimits = over.Count == 0 ? "none" : string.Join(", ", over.Take(10).Select(m => $"{m.name} {m.max:F2} ({100f * m.share:F0}%)"));
            var clip = new AnimationClip { name = psa.name, frameRate = Mathf.Round(psa.rate) };
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
            for (int j = 0; j < rig.props.Count; j++)
                for (int c = 0; c < 7; c++)
                {
                    bindings.Add(EditorCurveBinding.FloatCurve(rig.props[j].path, typeof(Transform), PropCurves[c]));
                    curves.Add(Smooth(propKeys[j][c]));
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

        static readonly string[] Checked = { "hand_l", "hand_r", "foot_l", "foot_r", "head", "spine_05", "upperarm_l", "upperarm_r" };
        // measured from another bone: what is left when the bend above it is not counted
        static readonly (string bone, string from)[] Relative =
        {
            ("hand_l", "spine_05"), ("hand_r", "spine_05"), ("head", "spine_05"), ("upperarm_r", "spine_05"), ("hand_l", "upperarm_l"), ("hand_r", "upperarm_r"),
        };

        static readonly string[] Rolled =
        {
            "upperarm_r", "lowerarm_r", "hand_r", "upperarm_l", "lowerarm_l", "hand_l", "thigh_r", "calf_r", "foot_r",
            "upperarm_twist_01_r", "upperarm_twist_02_r", "lowerarm_twist_01_r", "lowerarm_twist_02_r", "thigh_twist_01_r", "calf_twist_01_r",
            "spine_02", "spine_03", "spine_04", "spine_05", "neck_02", "head", "weapon_r", "shield_l",
            "upperarm_correctiveRoot_r", "lowerarm_correctiveRoot_r", "thigh_correctiveRoot_r", "calf_correctiveRoot_r", "index_02_half_r",
        };

        static readonly (string, string)[] Segments =
        {
            ("clavicle_r", "upperarm_r"), ("upperarm_r", "lowerarm_r"), ("lowerarm_r", "hand_r"), ("hand_r", "middle_01_r"),
            ("upperarm_l", "lowerarm_l"), ("lowerarm_l", "hand_l"), ("thigh_r", "calf_r"), ("calf_r", "foot_r"), ("spine_01", "spine_05"),
        };

        /// <summary>
        /// The humanoid clip played on her fighter against the game's pose, every 6th frame: how far her hands, feet (ankles),
        /// head, upper chest and shoulders are apart, mean and worst, in centimetres; then some measured from a bone above
        /// them (bone~from).
        /// </summary>
        static string RoundTrip(Rig rig, Psa psa, int reading, bool holdRoot, AnimationClip clip, GameObject player)
        {
            var byName = player.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
            var names = Checked.Concat(Relative.Select(r => $"{r.bone}~{r.from}")).ToArray();
            var srcIndex = Checked.Select(n => Array.IndexOf(psa.bones, n)).ToArray();
            var sum = new double[names.Length];
            var worst = new float[names.Length];
            var rollSum = new double[Rolled.Length];
            var rollWorst = new float[Rolled.Length];
            var segSum = new double[Segments.Length];
            var segWorst = new float[Segments.Length];
            int count = 0;
            // the game runs its procedural rig on every pose (twist bones, corrective roots, finger half joints:
            // RoeUeRig's game rig), whatever the clip baked into those bones - the game's pose is the clip plus that
            var gameRig = rig.go.GetComponent<RoeUeRig>();
            if (gameRig != null)
            {
                gameRig.Build();
                gameRig.twistRig = RoeUeRig.TwistRig.Game;
                gameRig.twist = 1f;
            }
            for (int f = 0; f < psa.frames; f += 6)
            {
                Pose(rig, psa, f, reading, holdRoot);
                gameRig?.DriveTwists();
                var game = srcIndex.Select(b => b >= 0 && rig.target[b] != null ? rig.target[b].position : Vector3.zero).ToArray();
                        RoeCapture.Pose(player, clip, Mathf.Min(clip.length, f / Mathf.Max(1f, psa.rate)));
                var mine = Checked.Select(c => byName.TryGetValue(c, out var t) ? t.position : Vector3.zero).ToArray();
                for (int i = 0; i < names.Length; i++)
                {
                    float d;
                    if (i < Checked.Length)
                        d = Vector3.Distance(mine[i], game[i]);
                    else
                    {
                        int k = Array.IndexOf(Checked, Relative[i - Checked.Length].bone), o = Array.IndexOf(Checked, Relative[i - Checked.Length].from);
                        d = Vector3.Distance(mine[k] - mine[o], game[k] - game[o]);
                    }
                    sum[i] += d;
                    worst[i] = Mathf.Max(worst[i], d);
                }
                // directions of the arm and leg bones: which joint is off (degrees)
                for (int k = 0; k < Segments.Length; k++)
                {
                    var (a, b) = Segments[k];
                    int ia = Array.IndexOf(psa.bones, a), ib = Array.IndexOf(psa.bones, b);
                    if (ia < 0 || ib < 0 || rig.target[ia] == null || rig.target[ib] == null || !byName.ContainsKey(a) || !byName.ContainsKey(b))
                        continue;
                    float angle = Vector3.Angle(rig.target[ib].position - rig.target[ia].position, byName[b].position - byName[a].position);
                    segSum[k] += angle;
                    segWorst[k] = Mathf.Max(segWorst[k], angle);
                }
                // world rotations of the limb bones (their roll: the skin twists with it)
                for (int k = 0; k < Rolled.Length; k++)
                {
                    int ib = Array.IndexOf(psa.bones, Rolled[k]);
                    if (ib < 0 || rig.target[ib] == null || !byName.ContainsKey(Rolled[k]))
                        continue;
                    float angle = Quaternion.Angle(rig.target[ib].rotation, byName[Rolled[k]].rotation);
                    rollSum[k] += angle;
                    rollWorst[k] = Mathf.Max(rollWorst[k], angle);
                }
                count++;
            }
            Restore(rig);
            return string.Join(", ", names.Select((c, i) => $"{c} {100.0 * sum[i] / Mathf.Max(1, count):F1}/{100f * worst[i]:F1}")) +
                   "; rotations, deg: " + string.Join(", ", Rolled.Select((g, k) => $"{g} {rollSum[k] / Mathf.Max(1, count):F1}/{rollWorst[k]:F0}")) +
                   "; directions, deg: " + string.Join(", ", Segments.Select((g, k) => $"{g.Item1}>{g.Item2} {segSum[k] / Mathf.Max(1, count):F1}/{segWorst[k]:F0}"));
        }

        /// <summary>
        /// Close-ups of her forearms in frames of one of her clips (default: her first attack, where the sword hand rolls
        /// 160 degrees), the twist bones left with their limb (RoeUeRig.twist 0) and turned by their share (1), side by side
        /// per frame: &lt;out&gt;/twist_&lt;time&gt;_&lt;0|1&gt;_&lt;arm&gt;.png.
        ///   -executeMethod RoeFighter.EditorTools.VdfAnims.TwistStills -Graphics [-roeVdf fio005] [-roeClip name] [-roeTimes 0.18,0.35] [-roeOut dir]
        /// </summary>
        public static void TwistStills()
        {
            string id = RoeCapture.Arg("-roeVdf", "fio005");
            string clipName = RoeCapture.Arg("-roeClip", "AS_PC_Fiona_Battle_Attack01");
            var times = RoeCapture.Arg("-roeTimes", "0.18,0.3,0.45,0.6").Split(',').Select(x => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            string outDir = RoeCapture.Arg("-roeOut", Path.Combine(Path.GetDirectoryName(Application.dataPath), "_work", "vdf", "twist"));
            Directory.CreateDirectory(outDir);
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{ClipDir(id)}/{clipName}.anim");
            if (clip == null)
                throw new Exception($"no clip {ClipDir(id)}/{clipName}.anim");
            ShaderUtil.allowAsyncCompilation = false;
            var studio = RoeStudio.Build();
            var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(RoeHumanoid.FighterPath(id)));
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                smr.forceMatrixRecalculationPerRender = true;
            var ue = go.GetComponent<RoeUeRig>();
            var animator = go.GetComponent<Animator>();
            studio.LightFrom(Vector3.forward);
            studio.SetFocus(0f, 0f, 0f);
            bool warmed = false;
            var notes = new List<string>();
            foreach (float time in times)
            {
                foreach (var (arm, lowerBone, handBone) in new[] { ("right", HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand), ("left", HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand) })
                {
                    for (int on = 0; on < 2; on++)
                    {
                        if (ue != null)
                            ue.twist = on;
                        RoeCapture.Pose(go, clip, Mathf.Min(clip.length, time));
                        var lower = animator.GetBoneTransform(lowerBone);
                        var hand = animator.GetBoneTransform(handBone);
                        var mid = Vector3.Lerp(lower.position, hand.position, 0.45f);
                        // from the side of the forearm, square to it
                        var along = (hand.position - lower.position).normalized;
                        var side = Vector3.Cross(along, Vector3.up);
                        if (side.sqrMagnitude < 1e-4f)
                            side = Vector3.right;
                        var look = Vector3.Cross(Vector3.up, side.normalized);
                        studio.Aim(mid, look.sqrMagnitude > 1e-4f ? look.normalized : Vector3.forward, 0f, 10f, 0.75f, 32f);
                        string file = Path.Combine(outDir, $"twist_{time:F2}_{on}_{arm}.png");
                        if (!warmed)
                        {
                            RoeCapture.Render(studio.camera, 320, 320, file);
                            RoeCapture.Render(studio.camera, 320, 320, file);
                            warmed = true;
                        }
                        RoeCapture.Render(studio.camera, 900, 700, file);
                    }
                    notes.Add($"{arm} {time:F2} s");
                }
            }
            RoeCapture.EndPosing();
            Object.DestroyImmediate(go);
            Debug.Log($"[ROE] {id}: twist stills of {clipName} to {outDir}: {string.Join(", ", notes)}");
        }

        /// <summary>
        /// Close-ups of her joints in moments of her clips with each twist rig (RoeUeRig.TwistRig: Off, ByPosition, Game):
        /// an elbow or knee seen square to the plane of its two bones (the bend's profile: the crease and the point), a
        /// forearm from the side.  Stills to &lt;out&gt;/rig_&lt;n&gt;_&lt;mode&gt;.png and shots.tsv for tools/vdf_rig_sheet.py.
        ///   -executeMethod RoeFighter.EditorTools.VdfAnims.RigStills -Graphics [-roeVdf fio005] [-roeModes Off,ByPosition,Game]
        ///   [-roeShots clip@seconds@joint,...]  (joint: elbow_l elbow_r knee_l knee_r forearm_l forearm_r shoulder_l shoulder_r)
        ///   [-roeHide shield,longsword]  (props not drawn; the clip may be a fight clip in clips/ too: skill_01, die, ...)
        /// </summary>
        public static void RigStills()
        {
            string id = RoeCapture.Arg("-roeVdf", "fio005");
            string outDir = RoeCapture.Arg("-roeOut", Path.Combine(Path.GetDirectoryName(Application.dataPath), "_work", "vdf", "rig"));
            var modes = RoeCapture.Arg("-roeModes", "Off,ByPosition,Game").Split(',').Select(m => (RoeUeRig.TwistRig)Enum.Parse(typeof(RoeUeRig.TwistRig), m.Trim(), true)).ToArray();
            // (her left elbow is behind the shield in every clip; the right arm's are in the open)
            var shots = RoeCapture.Arg("-roeShots", "AS_PC_Fiona_Battle_Attack_Strong03@0.25@elbow_r,AS_PC_Fiona_Battle_Attack01@0.3@elbow_r," +
                                                    "AS_PC_Fiona_Battle_Attack01@0.3@forearm_r,AS_PC_Fiona_Battle_HeavyStander_During@0.5@knee_l," +
                                                    "AS_PC_Fiona_Battle_HeavyStander_During@0.5@knee_r,AS_PC_Fiona_Test_Emo_Cheering_Evy@0.8@shoulder_r").Split(',');
            Directory.CreateDirectory(outDir);
            ShaderUtil.allowAsyncCompilation = false;
            var studio = RoeStudio.Build();
            var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(RoeHumanoid.FighterPath(id)));
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                smr.forceMatrixRecalculationPerRender = true;
            var ue = go.GetComponent<RoeUeRig>();
            ue?.Build();
            var bones = go.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
            // -roeHide shield,longsword: props left out of the stills (her left elbow is behind the shield in every clip)
            var hide = new HashSet<string>(RoeCapture.Arg("-roeHide", "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
                if (hide.Contains(r.gameObject.name))
                    r.enabled = false;
            studio.LightFrom(Vector3.forward);
            studio.SetFocus(0f, 0f, 0f);
            bool warmed = false;
            var table = new StringBuilder("n\tclip\ttime\tjoint\n");
            for (int n = 0; n < shots.Length; n++)
            {
                var parts = shots[n].Split('@');
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{ClipDir(id)}/{parts[0]}.anim")
                           ?? AssetDatabase.LoadAssetAtPath<AnimationClip>($"Assets/VDF/{id}/clips/{parts[0]}.anim");   // the fight's game actions
                if (clip == null || parts.Length < 3)
                    continue;
                float time = float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
                string joint = parts[2];
                string s = joint.EndsWith("_l") ? "l" : "r";
                // the three bones of the view: above the joint, the joint, below it
                var (a, b, c) = joint.StartsWith("elbow") ? ($"upperarm_{s}", $"lowerarm_{s}", $"hand_{s}")
                              : joint.StartsWith("knee") ? ($"thigh_{s}", $"calf_{s}", $"foot_{s}")
                              : joint.StartsWith("shoulder") ? ($"clavicle_{s}", $"upperarm_{s}", $"lowerarm_{s}")
                              : ($"lowerarm_{s}", $"lowerarm_{s}", $"hand_{s}");
                foreach (var mode in modes)
                {
                    if (ue != null)
                        ue.twistRig = mode;
                    RoeCapture.Pose(go, clip, Mathf.Min(clip.length, time));
                    Vector3 pa = bones[a].position, pb = bones[b].position, pc = bones[c].position;
                    Vector3 centre, look;
                    float distance;
                    if (joint.StartsWith("forearm"))
                    {
                        centre = Vector3.Lerp(pb, pc, 0.45f);
                        var side = Vector3.Cross((pc - pb).normalized, Vector3.up);
                        look = Vector3.Cross(Vector3.up, side.sqrMagnitude > 1e-4f ? side.normalized : Vector3.right);
                        distance = 0.75f;
                    }
                    else if (joint.StartsWith("shoulder"))
                    {
                        // from her front (the armpit and the top of the shoulder with the arm raised)
                        var animator = go.GetComponent<Animator>();
                        var right = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg).position - animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg).position;
                        centre = pb;
                        look = Vector3.Cross(right, Vector3.up);
                        distance = 0.8f;
                    }
                    else
                    {
                        centre = pb;
                        look = Vector3.Cross(pa - pb, pc - pb);
                        if (look.sqrMagnitude < 1e-6f)
                            look = Vector3.Cross(pa - pb, Vector3.up);
                        // from the side the body is not on (the outer side of the joint)
                        var hips = go.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Hips).position;
                        if (Vector3.Dot(look, pb - hips) < 0f)
                            look = -look;
                        distance = joint.StartsWith("knee") ? 0.9f : 0.7f;
                    }
                    studio.Aim(centre, look.normalized, 0f, 0f, distance, 32f);
                    string file = Path.Combine(outDir, $"rig_{n}_{mode}.png");
                    if (!warmed)
                    {
                        RoeCapture.Render(studio.camera, 320, 320, file);
                        RoeCapture.Render(studio.camera, 320, 320, file);
                        warmed = true;
                    }
                    RoeCapture.Render(studio.camera, 700, 700, file);
                }
                table.Append($"{n}\t{parts[0]}\t{time:F2}\t{joint}\n");
            }
            File.WriteAllText(Path.Combine(outDir, "shots.tsv"), table.ToString());
            RoeCapture.EndPosing();
            Object.DestroyImmediate(go);
            Debug.Log($"[ROE] {id}: rig stills ({string.Join(", ", modes)}) to {outDir}; game rig: {ue?.GameLimbCount} limbs, {ue?.HalfJointCount} finger half joints");
        }

        /// <summary>
        /// How her elbows bend in all her clips (the converted moves in ClipDir and the fight's game actions in clips/), every
        /// 1/30 s: the angle between the upper arm and the forearm (0 = straight), and whether the forearm bends the wrong way
        /// (against the bend of her battle idle, in the upper arm's frame).  Logs each clip's deepest bend per side and the deepest moments overall as RigStills shots
        /// (clip@seconds@elbow_x).  User 10-06: "fiona模型的肘部是否有问题".
        ///   -executeMethod RoeFighter.EditorTools.VdfAnims.ElbowSurvey [-roeVdf fio005] [-roeTop 12] [-roeIdle AS_pc_fiona_battle_idle]
        /// </summary>
        public static void ElbowSurvey()
        {
            string id = RoeCapture.Arg("-roeVdf", "fio005");
            int top = int.Parse(RoeCapture.Arg("-roeTop", "12"));
            var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(RoeHumanoid.FighterPath(id)));
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            go.GetComponent<RoeUeRig>()?.Build();
            var bones = go.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
            // her clips: the converted moves and the fight's game actions (hurt, die, skills, ...)
            var clips = new[] { ClipDir(id), $"Assets/VDF/{id}/clips" }.Where(AssetDatabase.IsValidFolder)
                .SelectMany(dir => AssetDatabase.FindAssets("t:AnimationClip", new[] { dir }))
                .Select(g => AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(c => c != null).Distinct().OrderBy(c => c.name).ToList();
            var sides = new[] { "l", "r" };
            // the way an elbow bends: its bend plane's normal in the upper arm's frame, in the battle idle
            var idle = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{ClipDir(id)}/{RoeCapture.Arg("-roeIdle", "AS_pc_fiona_battle_idle")}.anim");
            var hinge = new Dictionary<string, Vector3>();
            if (idle != null)
            {
                RoeCapture.Pose(go, idle, 0f);
                foreach (var s in sides)
                {
                    Transform a = bones[$"upperarm_{s}"], b = bones[$"lowerarm_{s}"], c = bones[$"hand_{s}"];
                    hinge[s] = Quaternion.Inverse(a.rotation) * Vector3.Cross(b.position - a.position, c.position - b.position).normalized;
                }
            }
            var deepest = new List<(string clip, float t, string side, float bend)>();
            var sb = new StringBuilder($"[ROE] {id}: elbow bend in her {clips.Count} clips, degrees (0 = straight; \"wrong way\" = bent against the battle idle's bend, deepest):");
            foreach (var clip in clips)
            {
                var best = sides.ToDictionary(s => s, s => (t: 0f, bend: 0f));
                var wrong = sides.ToDictionary(s => s, s => 0f);
                for (float t = 0f; t <= clip.length + 1e-4f; t += 1f / 30f)
                {
                    RoeCapture.Pose(go, clip, Mathf.Min(t, clip.length));
                    foreach (var s in sides)
                    {
                        Transform a = bones[$"upperarm_{s}"], b = bones[$"lowerarm_{s}"], c = bones[$"hand_{s}"];
                        Vector3 upper = b.position - a.position, fore = c.position - b.position;
                        float bend = Vector3.Angle(upper, fore);
                        if (bend > best[s].bend)
                            best[s] = (t, bend);
                        if (hinge.TryGetValue(s, out var h) && Vector3.Dot(Vector3.Cross(upper, fore), a.rotation * h) < 0f)
                            wrong[s] = Mathf.Max(wrong[s], bend);
                    }
                }
                foreach (var s in sides)
                    deepest.Add((clip.name, best[s].t, s, best[s].bend));
                sb.Append($"\n[ROE]   {clip.name} ({clip.length:F2} s): left {best["l"].bend:F0} at {best["l"].t:F2} s, right {best["r"].bend:F0} at {best["r"].t:F2} s" +
                          (wrong.Values.Any(w => w > 5f) ? $"; wrong way left {wrong["l"]:F0}, right {wrong["r"]:F0}" : ""));
            }
            sb.Append($"\n[ROE]   deepest: " + string.Join(",", deepest.OrderByDescending(d => d.bend).Take(top)
                .Select(d => FormattableString.Invariant($"{d.clip}@{d.t:F2}@elbow_{d.side}"))));
            RoeCapture.EndPosing();
            Object.DestroyImmediate(go);
            Debug.Log(sb.ToString());
        }

        /// <summary>
        /// Her weapons in moments of her clips, from her front-left (the shield's side) and from her front: stills to
        /// &lt;out&gt;/props_&lt;tag&gt;_&lt;n&gt;_&lt;view&gt;.png, run before and after an import to compare (tools/vdf_prop_sheet.py
        /// puts the two side by side).  Also logs, per moment, how far shield_l and weapon_r are turned from their bind pose.
        ///   -executeMethod RoeFighter.EditorTools.VdfAnims.PropStills -Graphics [-roeVdf fio005] [-roeTag after] [-roeOut dir]
        ///   [-roeShots clip@seconds,clip@seconds]
        /// </summary>
        public static void PropStills()
        {
            string id = RoeCapture.Arg("-roeVdf", "fio005");
            string tag = RoeCapture.Arg("-roeTag", "now");
            string outDir = RoeCapture.Arg("-roeOut", Path.Combine(Path.GetDirectoryName(Application.dataPath), "_work", "vdf", "props"));
            var shots = RoeCapture.Arg("-roeShots", "AS_pc_fiona_battle_idle@0.5,AS_PC_Fiona_Battle_Guard_Counter@0.3,AS_PC_Fiona_Battle_HeavyStander_During@0.5," +
                                                    "AS_PC_Fiona_Battle_Attack_Strong03@0.4,AS_PC_Fiona_Test_Emo_Cheering_Evy@0.8").Split(',');
            Directory.CreateDirectory(outDir);
            ShaderUtil.allowAsyncCompilation = false;
            var studio = RoeStudio.Build();
            var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(RoeHumanoid.FighterPath(id)));
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                smr.forceMatrixRecalculationPerRender = true;
            var all = go.GetComponentsInChildren<Transform>(true);
            var shield = all.FirstOrDefault(t => t.name == "shield_l");
            var sword = all.FirstOrDefault(t => t.name == "weapon_r");
            var bind = new[] { shield, sword }.Select(t => t != null ? t.localRotation : Quaternion.identity).ToArray();
            var animator = go.GetComponent<Animator>();
            studio.LightFrom(Vector3.forward);
            studio.SetFocus(0f, 0f, 0f);
            bool warmed = false;
            var notes = new List<string>();
            for (int n = 0; n < shots.Length; n++)
            {
                var parts = shots[n].Split('@');
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{ClipDir(id)}/{parts[0]}.anim");
                if (clip == null)
                {
                    notes.Add($"{parts[0]}: no clip");
                    continue;
                }
                float time = parts.Length > 1 ? float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) : 0f;
                RoeCapture.Pose(go, clip, Mathf.Min(clip.length, time));
                var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                var chest = animator.GetBoneTransform(HumanBodyBones.UpperChest) ?? animator.GetBoneTransform(HumanBodyBones.Chest);
                var mid = Vector3.Lerp(hips.position, chest.position, 0.5f);
                // where she faces: square to the line from her left thigh to her right (her bones' own axes are Unreal's)
                var right = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg).position - animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg).position;
                var forward = Vector3.Cross(right, Vector3.up);
                foreach (var (view, yaw) in new[] { ("left", -55f), ("front", 0f) })
                {
                    studio.Aim(mid, forward, yaw, 8f, 3.0f, 30f);
                    string file = Path.Combine(outDir, $"props_{tag}_{n}_{view}.png");
                    if (!warmed)
                    {
                        RoeCapture.Render(studio.camera, 320, 320, file);
                        RoeCapture.Render(studio.camera, 320, 320, file);
                        warmed = true;
                    }
                    RoeCapture.Render(studio.camera, 700, 800, file);
                }
                notes.Add($"{n} {parts[0]} @{time:F2}: shield_l {(shield != null ? Quaternion.Angle(shield.localRotation, bind[0]) : -1f):F1} deg, " +
                          $"weapon_r {(sword != null ? Quaternion.Angle(sword.localRotation, bind[1]) : -1f):F1} deg off their bind pose");
            }
            RoeCapture.EndPosing();
            Object.DestroyImmediate(go);
            Debug.Log($"[ROE] {id}: prop stills ({tag}) to {outDir}:\n[ROE]   {string.Join("\n[ROE]   ", notes)}");
        }

        [MenuItem("ROE Fighter/Motions/Vindictus: import Fiona's own animations")]
        public static void Import()
        {
            foreach (var id in RoeCapture.Arg("-roeVdf", "fio005").Split(','))
                Import(id.Trim(), RoeCapture.Arg("-roeOnly", ""));
            AssetDatabase.SaveAssets();
        }

        public static void Import(string id, string only)
        {
            var files = Directory.Exists(SourceDir)
                ? Directory.GetFiles(SourceDir, "*.psa", SearchOption.AllDirectories).OrderBy(p => Path.GetFileName(p)).ToList()
                : new List<string>();
            files = files.Where(p =>
            {
                string n = Path.GetFileNameWithoutExtension(p);
                return string.IsNullOrEmpty(only) ? !Regex.IsMatch(n, Skip) : Regex.IsMatch(n, only, RegexOptions.IgnoreCase);
            }).ToList();
            if (files.Count == 0)
            {
                Debug.LogError($"[ROE] {id}: no .psa under {SourceDir} - run tools/vdf_anims.py export first");
                return;
            }
            // the clips her fight definition plays as they are: in place
            var definition = File.Exists(DoaFighter.DefinitionPath(id)) ? File.ReadAllText(DoaFighter.DefinitionPath(id)) : "";
            var inPlace = new HashSet<string>(Regex.Matches(definition, @"anims/inplace/([A-Za-z0-9_]+)\.anim").Cast<Match>().Select(m => m.Groups[1].Value));

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RoeHumanoid.FighterPath(id));
            if (prefab == null)
            {
                Debug.LogError($"[ROE] {id}: no fighter {RoeHumanoid.FighterPath(id)} - VdfFighter.Build first");
                return;
            }
            var log = new StringBuilder();
            var source = (GameObject)Object.Instantiate(prefab);
            source.name = prefab.name;
            source.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            foreach (var a in source.GetComponentsInChildren<Animator>(true))
                a.enabled = false;
            // -roeTwist a,b,c,d: another twist distribution for reading and for the check (both avatars), to compare
            string twistArg = RoeCapture.Arg("-roeTwist", "");
            var twist = twistArg.Length > 0 ? twistArg.Split(',').Select(x => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray() : null;
            var avatar = MakeAvatar(prefab, heels: false, twist);
            var handler = new HumanPoseHandler(avatar, source.transform);
            var player = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            player.transform.SetPositionAndRotation(new Vector3(0f, 0f, 0f), Quaternion.identity);
            player.GetComponent<RoeUeRig>()?.Build();      // (no Awake in edit mode) bind rotations before any posing
            // -roeTwistRig Off|ByPosition|Game: which rig her fighter plays the clips back with, for the check (default: the prefab's)
            string twistRigArg = RoeCapture.Arg("-roeTwistRig", "");
            if (twistRigArg.Length > 0 && player.GetComponent<RoeUeRig>() != null)
            {
                player.GetComponent<RoeUeRig>().twistRig = (RoeUeRig.TwistRig)Enum.Parse(typeof(RoeUeRig.TwistRig), twistRigArg, true);
                log.AppendLine($"played back with the twist rig {twistRigArg}");
            }
            Avatar playerAvatar = null;
            if (twist != null)
            {
                playerAvatar = MakeAvatar(prefab, heels: true, twist);
                player.GetComponent<Animator>().avatar = playerAvatar;
                log.AppendLine($"twist distribution {twistArg} (upper arm, forearm, thigh, shin) for reading and playing back");
            }

            Directory.CreateDirectory(ClipDir(id));
            Directory.CreateDirectory(InPlaceDir(id));
            int reading = -1, done = 0;
            Rig rig = null;
            foreach (var path in files)
            {
                var psa = Read(path);
                // a clip on another skeleton (Gesture_01 has 386 bones) is bound anew, by name
                if (rig == null || !rig.names.SequenceEqual(psa.bones))
                {
                    rig = Bind(source, psa);
                    if (reading < 0)
                    {
                        reading = Calibrate(rig, psa, out string note);
                        log.AppendLine($"{id}: {psa.bones.Length} bones in the clips, {rig.target.Count(t => t != null)} of them hers; {note}");
                    }
                    if (rig.root < 0 || rig.pelvis < 0)
                    {
                        log.AppendLine($"{psa.name}: {psa.bones.Length} bones without a root and pelvis of hers - left out");
                        rig = null;
                        continue;
                    }
                }
                bool loop = Regex.IsMatch(psa.name, Loops);
                var clip = Convert(rig, psa, reading, handler, holdRoot: false, loop);
                string clipPath = $"{ClipDir(id)}/{psa.name}.anim";
                AssetDatabase.DeleteAsset(clipPath);
                AssetDatabase.CreateAsset(clip, clipPath);
                // the root's travel (Unreal: y is her back, x her left) and turn over the clip
                var p0 = Ue(psa.P(0, rig.root), reading);
                var p1 = Ue(psa.P(psa.frames - 1, rig.root), reading);
                var travel = World(p1) - World(p0);
                float turn = Mathf.DeltaAngle(0f, (World(Ue(psa.Q(psa.frames - 1, rig.root), reading & 2)) *
                                                   Quaternion.Inverse(World(Ue(psa.Q(0, rig.root), reading & 2)))).eulerAngles.y);
                string limits = LastLimits;
                string check = RoundTrip(rig, psa, reading, false, clip, player);
                log.AppendLine($"{psa.name}: {psa.frames} frames {clip.length:F2} s{(loop ? " loop" : "")}, root travel forward {travel.z:+0.00;-0.00} " +
                               $"right {travel.x:+0.00;-0.00} m, turn {turn:+0;-0} deg; played back on her, cm mean/worst: {check}; muscles at their limits: {limits}");
                if (inPlace.Contains(psa.name))
                {
                    var still = Convert(rig, psa, reading, handler, holdRoot: true, loop);
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
            Object.DestroyImmediate(avatar);
            if (playerAvatar != null)
                Object.DestroyImmediate(playerAvatar);
            AssetDatabase.SaveAssets();
            string report = Path.Combine(Path.GetDirectoryName(Application.dataPath), "_work", "vdf", $"{id}_anims_import{RoeCapture.Arg("-roeReport", "")}.txt");
            File.WriteAllText(report, log.ToString());
            Debug.Log($"[ROE] {id}: {done} of her Vindictus animations -> {ClipDir(id)} ({inPlace.Count} in place); report {report}\n{log}");
        }
    }
}
