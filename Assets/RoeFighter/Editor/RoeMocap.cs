using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RoeFighter.EditorTools
{
    /// <summary>
    /// Motion capture (BVH) -> Unity humanoid clips for the fighters' basic moves (walking, punches,
    /// kicks, the guard).  The game has none of these; its characters only have their battle stance,
    /// three skills, a hit reaction and a fall.
    ///
    /// Built in: Bandai Namco Research Motion Dataset 1 (Bandai Namco Research Inc., CC BY-NC 4.0),
    /// BVH at 30 fps, downloaded by tools/fetch_mocap.py into _work/mocap/bandai1 (ImportAll).
    /// Other BVH data comes in through a motion pack description (RoeMotionPacks, its "bvh" block),
    /// which lists the same things as the Bandai Source below.
    ///
    /// The BVH skeleton is rebuilt as GameObjects (BVH is right-handed: x is mirrored; units -> m).
    /// Which joint is which humanoid bone is worked out from the names and the hierarchy, so the usual
    /// conventions all work (Bandai "Hand_L", CMU / LAFAN1 / Mixamo "LeftHand", 3ds Max "Bip01 L Hand",
    /// ASF "lwrist"...); a description can still name joints itself.
    /// The avatar's T-pose is made from a calm standing frame (or the BVH's zero pose): the hips are
    /// turned to face +Z, then every bone is swung (no twist) to the T-pose direction - spine up, arms
    /// out to the side, legs down, toes forward.  Each frame of a segment is then read back as a
    /// HumanPose (muscles + body position) and written as a humanoid clip, which any humanoid fighter
    /// can play.
    /// Loops (walks, run) are cut where the pose repeats best and play in place; the strikes keep their
    /// own small travel.
    /// Batch mode: -executeMethod RoeFighter.EditorTools.RoeMocap.ImportAll
    /// </summary>
    public static class RoeMocap
    {
        public const string OutDir = "Assets/RoeFighter/Generated/mocap";

        public static string ClipPath(string name) => $"{OutDir}/mc_{name}.anim";
        public static string MovesPath => $"{OutDir}/mocap_strikes.json";

        /// <summary>Which BVH files to read and how (also the "bvh" block of a motion pack description).</summary>
        [Serializable]
        public class Source
        {
            public string folder;           // relative to the project folder, or absolute
            public string reference;        // file whose rest pose builds the avatar for all; empty: each file its own
            public int restFrame;           // a calm standing frame; -1: the BVH's zero pose (when that is a T-pose)
            public float scale;             // metres per BVH unit; 0: from the leg length
            public float fps;               // resample to this rate; 0: as recorded
            public List<BoneName> bones = new List<BoneName>();     // overrides: { "human": "LeftHand", "joint": "lwrist" }
            public List<Segment> segments = new List<Segment>();
        }

        [Serializable]
        public class BoneName
        {
            public string human, joint;
        }

        /// <summary>A piece of a BVH file that becomes one clip.  Frame numbers as tools/bvh_preview.py prints them.</summary>
        [Serializable]
        public class Segment
        {
            public string name, file;
            public int from, to;            // frame range (to 0: the end); for loops the range the cycle is searched in
            public bool loop;
            public float minCycle, maxCycle;    // loop length limits, seconds
            public string limb;             // strikes: the humanoid bone that hits (LeftHand, RightFoot, ...) or "auto"
            public string alignWith;        // use the turn of another segment
            public string face;             // what faces the opponent: "travel", "back" (walking backwards), or a yaw in degrees;
                                            // default: a strike's reach, else the hips (wrong for a side-on fighting stance)
            public float recoverSpeed;      // strikes: after the farthest reach the clip runs this much faster (the way back to guard)
        }

        public static readonly Source Bandai = new Source
        {
            folder = "_work/mocap/bandai1", reference = "dataset-1_walk_normal_001.bvh", restFrame = 0, scale = 0.01f,
            segments = new List<Segment>
            {
                new Segment { name = "guard", file = "dataset-1_punch_normal_001.bvh", from = 0, to = 24, loop = true, minCycle = 12 / 30f, maxCycle = 22 / 30f, alignWith = "jab" },
                new Segment { name = "jab", file = "dataset-1_punch_normal_001.bvh", from = 18, to = 44, limb = "LeftHand" },
                new Segment { name = "cross", file = "dataset-1_punch_normal_002.bvh", from = 40, to = 74, limb = "RightHand" },
                new Segment { name = "kick", file = "dataset-1_kick_normal_001.bvh", from = 38, to = 96, limb = "RightFoot" },
                new Segment { name = "slash", file = "dataset-1_slash_normal_001.bvh", from = 56, to = 100, limb = "RightHand" },
                new Segment { name = "walk", file = "dataset-1_walk_feminine_001.bvh", from = 40, to = 240, loop = true, minCycle = 26 / 30f, maxCycle = 46 / 30f },
                new Segment { name = "walk_back", file = "dataset-1_walk-back_feminine_001.bvh", from = 40, to = 280, loop = true, minCycle = 26 / 30f, maxCycle = 50 / 30f },
                new Segment { name = "run", file = "dataset-1_run_feminine_001.bvh", from = 20, to = 110, loop = true, minCycle = 14 / 30f, maxCycle = 30 / 30f },
            },
        };

        // Unity humanoid bones in the order the avatar lists them
        static readonly string[] HumanBones =
        {
            "Hips", "Spine", "Chest", "UpperChest", "Neck", "Head",
            "LeftShoulder", "LeftUpperArm", "LeftLowerArm", "LeftHand",
            "RightShoulder", "RightUpperArm", "RightLowerArm", "RightHand",
            "LeftUpperLeg", "LeftLowerLeg", "LeftFoot", "LeftToes",
            "RightUpperLeg", "RightLowerLeg", "RightFoot", "RightToes",
        };

        static readonly string[] Required =
        {
            "Hips", "Spine", "Head", "LeftUpperArm", "LeftLowerArm", "LeftHand", "RightUpperArm", "RightLowerArm", "RightHand",
            "LeftUpperLeg", "LeftLowerLeg", "LeftFoot", "RightUpperLeg", "RightLowerLeg", "RightFoot",
        };

        class Joint
        {
            public string name;
            public int parent = -1, depth;
            public bool end;
            public Vector3 offset;
            public string[] channels = new string[0];
            public int firstChannel;
            public Transform transform;
        }

        class Bvh
        {
            public string file;
            public readonly List<Joint> joints = new List<Joint>();
            public float[][] frames;
            public float frameTime, scale = 0.01f;
            public Dictionary<string, Joint> human = new Dictionary<string, Joint>();
            public bool Has(string bone) => human.ContainsKey(bone);
            public Transform this[string bone] => human[bone].transform;
        }

        static Bvh Parse(string path)
        {
            var bvh = new Bvh { file = Path.GetFileName(path) };
            var stack = new Stack<int>();
            var lines = File.ReadAllLines(path);
            int channelCount = 0, i = 0;
            for (; i < lines.Length; i++)
            {
                var tok = lines[i].Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (tok.Length == 0)
                    continue;
                switch (tok[0])
                {
                    case "ROOT":
                    case "JOINT":
                        bvh.joints.Add(new Joint { name = string.Join(" ", tok.Skip(1)), parent = stack.Count > 0 ? stack.Peek() : -1, depth = stack.Count });
                        stack.Push(bvh.joints.Count - 1);
                        break;
                    case "End":
                        bvh.joints.Add(new Joint { name = bvh.joints[stack.Peek()].name + "_end", parent = stack.Peek(), depth = stack.Count, end = true });
                        stack.Push(bvh.joints.Count - 1);
                        break;
                    case "OFFSET":
                        bvh.joints[stack.Peek()].offset = new Vector3(F(tok[1]), F(tok[2]), F(tok[3]));
                        break;
                    case "CHANNELS":
                        var j = bvh.joints[stack.Peek()];
                        j.channels = tok.Skip(2).ToArray();
                        j.firstChannel = channelCount;
                        channelCount += j.channels.Length;
                        break;
                    case "}":
                        stack.Pop();
                        break;
                }
                if (tok[0] == "MOTION")
                    break;
            }
            int frames = int.Parse(lines[i + 1].Split(':')[1].Trim());
            bvh.frameTime = F(lines[i + 2].Split(':')[1].Trim());
            var rows = new List<float[]>();
            for (int f = 0; f < frames && i + 3 + f < lines.Length; f++)
            {
                var row = lines[i + 3 + f].Split((char[])null, StringSplitOptions.RemoveEmptyEntries).Select(F).ToArray();
                if (row.Length >= channelCount)
                    rows.Add(row);
            }
            bvh.frames = rows.ToArray();
            return bvh;
        }

        static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);

        // ---- which joint is which humanoid bone

        // joint names that can carry a one-letter side in front without a separator ("lfemur", "rhand")
        static readonly HashSet<string> SidedCores = new HashSet<string>
        {
            "hipjoint", "femur", "tibia", "foot", "toes", "toe", "clavicle", "humerus", "radius", "wrist", "hand", "fingers", "thumb",
            "shoulder", "collar", "arm", "forearm", "upleg", "leg", "hip", "thigh", "calf", "knee", "ankle", "elbow",
        };

        /// <summary>
        /// A joint name as (side, core): "LeftUpLeg" -> (L, upleg), "Shoulder_R" -> (R, shoulder),
        /// "Bip01 L Thigh" -> (L, thigh), "mixamorig:Spine1" -> (-, spine1), "lfemur" -> (L, femur).
        /// </summary>
        static (char side, string core) Split(string name)
        {
            string n = name.Contains(":") ? name.Substring(name.LastIndexOf(':') + 1) : name;
            n = Regex.Replace(n, @"^(bip0*1|mixamorig|def)[ _\-.]+", "", RegexOptions.IgnoreCase);
            var parts = Regex.Split(n, @"[ _\-.]+|(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])")
                .Where(p => p.Length > 0).Select(p => p.ToLowerInvariant()).ToList();
            char side = '\0';
            if (parts.Count > 1)
            {
                string first = parts[0], last = parts[parts.Count - 1];
                if (first == "left" || first == "l" || first == "right" || first == "r")
                {
                    side = first[0] == 'l' ? 'L' : 'R';
                    parts.RemoveAt(0);
                }
                else if (last == "left" || last == "l" || last == "right" || last == "r")
                {
                    side = last[0] == 'l' ? 'L' : 'R';
                    parts.RemoveAt(parts.Count - 1);
                }
            }
            string core = string.Concat(parts);
            if (side == '\0' && core.Length > 1 && (core[0] == 'l' || core[0] == 'r') && SidedCores.Contains(core.Substring(1)))
            {
                side = core[0] == 'l' ? 'L' : 'R';
                core = core.Substring(1);
            }
            return (side, core);
        }

        /// <summary>
        /// Unity humanoid bone -> BVH joint.  Hands and feet are found by name; the forearm and upper
        /// arm (shin, thigh) are the joints above them, the shoulder the one above that when it has the
        /// same side.  The hips join the legs and the head; the spine is the path between them, split
        /// where the arms branch off: below it Spine / Chest / UpperChest, above it the neck.  Joints
        /// that sit on their parent (zero offset) are passed over there.  The description's names win.
        /// </summary>
        static Dictionary<string, Joint> Map(Bvh bvh, List<BoneName> overrides)
        {
            var joints = bvh.joints.Where(j => !j.end).ToList();
            var parts = joints.ToDictionary(j => j, j => Split(j.name));
            Joint Parent(Joint j) => j != null && j.parent >= 0 ? bvh.joints[j.parent] : null;
            Joint Up(Joint j)
            {
                var p = Parent(j);
                while (p != null && Regex.IsMatch(parts[p].core, "twist|roll"))
                    p = Parent(p);
                return p;
            }
            Joint Find(char side, params string[] cores)
            {
                foreach (var c in cores)
                {
                    var hit = joints.Where(j => parts[j].side == side && parts[j].core == c).OrderBy(j => j.depth).FirstOrDefault();
                    if (hit != null)
                        return hit;
                }
                return null;
            }
            List<Joint> Chain(Joint j)
            {
                var list = new List<Joint>();
                for (var p = j; p != null; p = Parent(p))
                    list.Add(p);
                list.Reverse();
                return list;
            }
            Joint Common(params Joint[] list)
            {
                if (list.Any(j => j == null))
                    return null;
                var chains = list.Select(Chain).ToList();
                Joint last = null;
                for (int i = 0; chains.All(c => i < c.Count && c[i] == chains[0][i]); i++)
                    last = chains[0][i];
                return last;
            }

            var map = new Dictionary<string, Joint>();
            void Set(string human, Joint j)
            {
                if (j != null)
                    map[human] = j;
            }
            Joint Get(string human) => map.TryGetValue(human, out var j) ? j : null;
            foreach (var (side, s) in new[] { ('L', "Left"), ('R', "Right") })
            {
                var hand = Find(side, "wrist", "hand");
                var lowerArm = hand == null ? null : Up(hand);
                var upperArm = lowerArm == null ? null : Up(lowerArm);
                var shoulder = upperArm == null ? null : Up(upperArm);
                Set(s + "Hand", hand);
                Set(s + "LowerArm", lowerArm);
                Set(s + "UpperArm", upperArm);
                if (shoulder != null && parts[shoulder].side == side)
                    Set(s + "Shoulder", shoulder);
                var foot = Find(side, "foot", "ankle");
                var lowerLeg = foot == null ? null : Up(foot);
                Set(s + "Foot", foot);
                Set(s + "LowerLeg", lowerLeg);
                Set(s + "UpperLeg", lowerLeg == null ? null : Up(lowerLeg));
                if (foot != null)
                    Set(s + "Toes", joints.FirstOrDefault(j => Parent(j) == foot && j.offset.sqrMagnitude > 1e-8f));
            }
            var head = Find('\0', "head");
            Set("Head", head);
            var hips = Common(Get("LeftUpperLeg"), Get("RightUpperLeg"), head);
            Set("Hips", hips);
            if (hips != null)
            {
                var path = Chain(head);
                int h = path.IndexOf(hips), top = path.Count - 2;
                var chest = Common(Get("LeftUpperArm"), Get("RightUpperArm"));
                int c = chest != null && path.IndexOf(chest) > h ? path.IndexOf(chest) : top;
                bool Apart(Joint j) => j.offset.sqrMagnitude > 1e-8f;
                var spine = path.Skip(h + 1).Take(c - h).Where(Apart).ToList();
                var neck = path.Skip(c + 1).Take(top - c).Where(Apart).ToList();
                if (spine.Count > 0)
                    Set("Spine", spine[0]);
                if (spine.Count > 1)
                    Set("Chest", spine[1]);
                if (spine.Count > 2)
                    Set("UpperChest", spine[spine.Count - 1]);
                if (neck.Count > 0)
                    Set("Neck", neck[0]);
            }
            foreach (var o in overrides ?? new List<BoneName>())
            {
                if (string.IsNullOrEmpty(o.human))
                    continue;
                var j = joints.FirstOrDefault(x => x.name == o.joint);
                if (j != null)
                    map[o.human] = j;
                else
                    map.Remove(o.human);
            }
            var missing = Required.Where(b => !map.ContainsKey(b)).ToList();
            if (missing.Count > 0)
                throw new Exception($"{bvh.file}: no joint found for {string.Join(", ", missing)} - name them in the description's \"bones\" " +
                                    $"(joints: {string.Join(", ", joints.Select(j => j.name))})");
            return map;
        }

        /// <summary>Metres per BVH unit from the leg (thigh + shin): centimetres, metres or inches when it fits, else a 0.84 m leg.</summary>
        static float AutoScale(Bvh bvh)
        {
            float leg = bvh.human["LeftLowerLeg"].offset.magnitude + bvh.human["LeftFoot"].offset.magnitude;
            if (leg > 55f && leg < 130f)
                return 0.01f;
            if (leg > 0.55f && leg < 1.3f)
                return 1f;
            if (leg > 22f && leg < 51f)
                return 0.0254f;
            return 0.84f / Mathf.Max(1e-6f, leg);
        }

        // ---- the skeleton as GameObjects

        // BVH is right-handed: mirror x.  A rotation about x keeps its angle, about y and z it flips.
        static Vector3 Position(Bvh bvh, Vector3 p) => new Vector3(-p.x, p.y, p.z) * bvh.scale;

        static Quaternion Rotation(char axis, float degrees)
        {
            switch (axis)
            {
                case 'X': return Quaternion.AngleAxis(degrees, Vector3.right);
                case 'Y': return Quaternion.AngleAxis(-degrees, Vector3.up);
                default: return Quaternion.AngleAxis(-degrees, Vector3.forward);
            }
        }

        /// <summary>The skeleton as GameObjects under root/align; end sites without length are left out.</summary>
        static (GameObject root, Transform align) Build(Bvh bvh)
        {
            var root = new GameObject("mocap_actor");
            var align = new GameObject("align").transform;
            align.SetParent(root.transform, false);
            foreach (var j in bvh.joints)
            {
                j.transform = null;
                if (j.end && j.offset.sqrMagnitude < 1e-6f)
                    continue;
                var t = new GameObject(j.name).transform;
                t.SetParent(j.parent >= 0 ? bvh.joints[j.parent].transform : align, false);
                t.localPosition = Position(bvh, j.offset);
                j.transform = t;
            }
            return (root, align);
        }

        /// <summary>Poses the skeleton at a frame; frame -1 is the zero pose (offsets only).</summary>
        static void Apply(Bvh bvh, int frame)
        {
            if (frame < 0)
            {
                foreach (var j in bvh.joints.Where(x => x.transform != null))
                {
                    j.transform.localPosition = Position(bvh, j.offset);
                    j.transform.localRotation = Quaternion.identity;
                }
                return;
            }
            var values = bvh.frames[Mathf.Clamp(frame, 0, bvh.frames.Length - 1)];
            foreach (var j in bvh.joints)
            {
                if (j.transform == null || j.channels.Length == 0)
                    continue;
                var p = j.offset;
                var q = Quaternion.identity;
                for (int c = 0; c < j.channels.Length; c++)
                {
                    float v = values[j.firstChannel + c];
                    string ch = j.channels[c];
                    if (ch.EndsWith("position"))
                        p["XYZ".IndexOf(ch[0])] = v;
                    else
                        q *= Rotation(ch[0], v);
                }
                j.transform.localPosition = Position(bvh, p);
                j.transform.localRotation = q;
            }
        }

        static Vector3 Facing(Bvh bvh)
        {
            var right = bvh["RightUpperLeg"].position - bvh["LeftUpperLeg"].position;
            var forward = Vector3.Cross(right, Vector3.up);
            forward.y = 0f;
            return forward.sqrMagnitude > 1e-8f ? forward.normalized : Vector3.forward;
        }

        static void Swing(Transform bone, Transform child, Vector3 direction)
        {
            var current = child.position - bone.position;
            if (current.sqrMagnitude < 1e-10f)
                return;
            bone.rotation = Quaternion.FromToRotation(current.normalized, direction.normalized) * bone.rotation;
        }

        /// <summary>T-pose from a calm standing frame (or the zero pose), and the avatar built on it.</summary>
        static Avatar BuildAvatar(Bvh bvh, GameObject root, int restFrame)
        {
            Apply(bvh, restFrame);
            var hips = bvh["Hips"];
            // hips upright, facing +Z, above the origin.  Up is the line to the first spine bone, unless
            // that bone is not above the hips - ACCAD's lower-back joint sits 8 cm behind the pelvis, level
            // with it, and standing it up tipped the pelvis 90 degrees (the skirts, hung on the pelvis,
            // flew out level) - then the line to the neck.
            var up = (bvh["Spine"].position - hips.position).normalized;
            var trunk = (bvh[bvh.Has("Neck") ? "Neck" : "Head"].position - hips.position).normalized;
            if (Vector3.Angle(up, trunk) > 30f)
                up = trunk;
            var right = (bvh["RightUpperLeg"].position - bvh["LeftUpperLeg"].position).normalized;
            var current = Quaternion.LookRotation(Vector3.Cross(right, up), up);
            hips.rotation = Quaternion.Inverse(current) * hips.rotation;
            hips.position = new Vector3(0f, hips.position.y, 0f);
            void Straighten(Vector3 direction, params string[] chain)
            {
                var bones = chain.Where(bvh.Has).ToList();
                for (int i = 0; i + 1 < bones.Count; i++)
                    Swing(bvh[bones[i]], bvh[bones[i + 1]], direction);
            }
            Straighten(Vector3.up, "Spine", "Chest", "UpperChest", "Neck", "Head");
            Straighten(Vector3.left, "LeftShoulder", "LeftUpperArm", "LeftLowerArm", "LeftHand");
            Straighten(Vector3.right, "RightShoulder", "RightUpperArm", "RightLowerArm", "RightHand");
            foreach (var s in new[] { "Left", "Right" })
            {
                Straighten(Vector3.down, s + "UpperLeg", s + "LowerLeg", s + "Foot");
                Straighten(Vector3.forward, s + "Foot", s + "Toes");
            }
            // feet on the floor
            var soles = bvh.Has("LeftToes") && bvh.Has("RightToes") ? new[] { "LeftToes", "RightToes" } : new[] { "LeftFoot", "RightFoot" };
            float lowest = Mathf.Min(bvh[soles[0]].position.y, bvh[soles[1]].position.y);
            hips.position -= new Vector3(0f, lowest, 0f);

            var human = HumanBones.Where(bvh.Has).Select(b =>
            {
                var hb = new HumanBone { humanName = b, boneName = bvh.human[b].name };
                hb.limit.useDefaultValues = true;
                return hb;
            }).ToArray();
            var skeleton = root.GetComponentsInChildren<Transform>(true).Select(t => new SkeletonBone
            {
                name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale,
            }).ToArray();
            var description = new HumanDescription
            {
                human = human,
                skeleton = skeleton,
                upperArmTwist = 0.5f, lowerArmTwist = 0.5f, upperLegTwist = 0.5f, lowerLegTwist = 0.5f,
                armStretch = 0.05f, legStretch = 0.05f, feetSpacing = 0f, hasTranslationDoF = false,
            };
            var avatar = AvatarBuilder.BuildHumanAvatar(root, description);
            avatar.name = "mocap_actor_avatar";
            return avatar;
        }

        static string MuscleAttribute(string traitName)
        {
            var m = Regex.Match(traitName, @"^(Left|Right) (Thumb|Index|Middle|Ring|Little) (.+)$");
            return m.Success ? $"{m.Groups[1].Value}Hand.{m.Groups[2].Value}.{m.Groups[3].Value}" : traitName;
        }

        // ---- cutting and aligning a segment

        /// <summary>Pose features for finding where a cycle repeats: limbs relative to the hips, heading removed.</summary>
        static float[] Features(Bvh bvh)
        {
            var hips = bvh["Hips"];
            var heading = Quaternion.Inverse(Quaternion.LookRotation(Facing(bvh)));
            var list = new List<float> { hips.position.y };
            foreach (var name in new[] { "LeftHand", "RightHand", "LeftFoot", "RightFoot", "Head", "LeftLowerLeg", "RightLowerLeg", "LeftLowerArm", "RightLowerArm" })
            {
                var p = heading * (bvh[name].position - hips.position);
                list.Add(p.x);
                list.Add(p.y);
                list.Add(p.z);
            }
            return list.ToArray();
        }

        static int Frames(Bvh bvh, float seconds) => Mathf.Max(1, Mathf.RoundToInt(seconds / bvh.frameTime));

        static (int a, int b, float cost) FindCycle(Bvh bvh, Segment s, int lo, int hi)
        {
            int minCycle = Frames(bvh, s.minCycle > 0f ? s.minCycle : 0.4f), maxCycle = Frames(bvh, s.maxCycle > 0f ? s.maxCycle : 1.6f);
            var feats = new float[hi - lo + 2][];
            var hips = new Vector3[hi - lo + 2];
            for (int f = lo; f <= hi + 1; f++)
            {
                Apply(bvh, f);
                feats[f - lo] = Features(bvh);
                hips[f - lo] = Flat(bvh["Hips"].position);
            }
            // a walk must get somewhere: standing still also repeats itself
            bool moving = s.face == "travel" || s.face == "back";
            float Dist(int a, int b)
            {
                float d = 0f;
                var fa = feats[a - lo];
                var fb = feats[b - lo];
                var na = feats[Mathf.Min(a + 1, hi + 1) - lo];
                var nb = feats[Mathf.Min(b + 1, hi + 1) - lo];
                for (int k = 0; k < fa.Length; k++)
                {
                    float p = fa[k] - fb[k];
                    float v = (na[k] - fa[k]) - (nb[k] - fb[k]);
                    d += p * p + 4f * v * v;
                }
                return d;
            }
            var best = (a: lo, b: Mathf.Min(hi, lo + maxCycle), cost: float.MaxValue);
            for (int a = lo; a <= hi - minCycle; a++)
                for (int b = a + minCycle; b <= Mathf.Min(hi, a + maxCycle); b++)
                {
                    if (moving && (hips[b - lo] - hips[a - lo]).magnitude < 0.15f * (b - a) * bvh.frameTime)
                        continue;
                    float c = Dist(a, b);
                    if (c < best.cost)
                        best = (a, b, c);
                }
            return best;
        }

        static float Yaw(Vector3 v) => Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;

        static readonly string[] Ends = { "LeftHand", "RightHand", "LeftFoot", "RightFoot" };

        /// <summary>The hand or foot that gets farthest out from the hips (beyond where it started) in a segment.</summary>
        static string Striking(Bvh bvh, int a, int b)
        {
            Apply(bvh, a);
            var start = Ends.Select(e => Flat(bvh[e].position - bvh["Hips"].position).magnitude).ToArray();
            var gain = new float[Ends.Length];
            for (int f = a; f <= b; f++)
            {
                Apply(bvh, f);
                for (int k = 0; k < Ends.Length; k++)
                    gain[k] = Mathf.Max(gain[k], Flat(bvh[Ends[k]].position - bvh["Hips"].position).magnitude - start[k]);
            }
            return Ends[Array.IndexOf(gain, gain.Max())];
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        /// <summary>
        /// Which way is "towards the opponent" in a segment (yaw, degrees): for a strike the
        /// direction of its farthest reach, otherwise the mean facing of the hips.
        /// </summary>
        static float Heading(Bvh bvh, string limb, int a, int b) => Heading(bvh, limb, a, b, out _);

        static float Heading(Bvh bvh, string limb, int a, int b, out int frame)
        {
            frame = a;
            if (!string.IsNullOrEmpty(limb))
            {
                var best = Vector3.zero;
                for (int f = a; f <= b; f++)
                {
                    Apply(bvh, f);
                    var d = bvh[limb].position - bvh["Hips"].position;
                    d.y = 0f;
                    if (d.sqrMagnitude > best.sqrMagnitude)
                    {
                        best = d;
                        frame = f;
                    }
                }
                return Yaw(best);
            }
            var sum = Vector3.zero;
            for (int f = a; f <= b; f++)
            {
                Apply(bvh, f);
                sum += Facing(bvh);
            }
            return Yaw(sum);
        }

        /// <summary>The segment a segment takes its turn from (alignWith, or face "stance:other"), or null.</summary>
        static string Reference(Segment s) =>
            !string.IsNullOrEmpty(s.alignWith) ? s.alignWith : s.face != null && s.face.StartsWith("stance:") ? s.face.Substring(7) : null;

        /// <summary>A strike as the fight sees it (times in seconds of the clip at its own speed).</summary>
        [Serializable]
        public class StrikeInfo
        {
            public string name, bone;           // humanoid bone that hits
            public float length, reach, height; // reach: farthest the bone gets towards the opponent, from where the hips started
            public float hitStart, hitPeak, hitEnd;
            public float travel, travelSide;    // how far the hips get forward / to the right from the first frame to the last
        }

        [Serializable]
        class StrikeTable
        {
            public List<StrikeInfo> moves = new List<StrikeInfo>();
        }

        static AnimationClip Convert(Bvh bvh, Segment s, Avatar avatar, GameObject root, Transform align, int step, Dictionary<string, float> headings,
                                     Dictionary<string, float> stances, out StrikeInfo strike, out string report)
        {
            int a = Mathf.Clamp(s.from, 0, bvh.frames.Length - 1);
            int b = s.to > s.from ? Mathf.Min(s.to, bvh.frames.Length - 1) : bvh.frames.Length - 1;
            float cost = 0f;
            if (s.loop)
                (a, b, cost) = FindCycle(bvh, s, a, b);

            // start of the segment: hips above the origin, the opponent towards +Z
            align.localPosition = Vector3.zero;
            align.localRotation = Quaternion.identity;
            string limb = s.limb == "auto" ? Striking(bvh, a, b) : s.limb;
            Apply(bvh, a);
            float hipsYaw = Yaw(Facing(bvh));
            float heading, aim = 0f;
            int aimFrame = a;
            if (!string.IsNullOrEmpty(s.alignWith) && headings.TryGetValue(s.alignWith, out float other))
                heading = other;
            else if (s.face != null && s.face.StartsWith("stance:") && stances.TryGetValue(s.face.Substring(7), out float stance))
            {
                // A side-on fighting stance: the hips start turned to the opponent the way they are in
                // the reference segment, so changing between the two does not twist the body.  A strike
                // then turns smoothly to where it reaches farthest and back.
                heading = hipsYaw - stance;
                if (!string.IsNullOrEmpty(limb))
                    aim = Mathf.DeltaAngle(heading, Heading(bvh, limb, a, b, out aimFrame));
            }
            else if (s.face == "travel" || s.face == "back")
            {
                var from = bvh["Hips"].position;
                Apply(bvh, b);
                heading = Yaw(bvh["Hips"].position - from) + (s.face == "back" ? 180f : 0f);
            }
            else if (float.TryParse(s.face, NumberStyles.Float, CultureInfo.InvariantCulture, out float yaw))
                heading = yaw;
            else
                heading = Heading(bvh, limb, a, b);
            headings[s.name] = heading;
            stances[s.name] = Mathf.DeltaAngle(heading, hipsYaw);
            Apply(bvh, a);
            var start = bvh["Hips"].position;
            // the turn at a frame: hips above the origin at the start, the opponent towards +Z
            void Align(int f)
            {
                float w = aim == 0f ? 0f : f <= aimFrame ? Mathf.SmoothStep(0f, 1f, (f - a) / (float)Mathf.Max(1, aimFrame - a))
                                                         : Mathf.SmoothStep(0f, 1f, (b - f) / (float)Mathf.Max(1, b - aimFrame));
                var turn = Quaternion.Euler(0f, -(heading + aim * w), 0f);
                align.localRotation = turn;
                align.localPosition = -(turn * new Vector3(start.x, 0f, start.z));
            }
            Align(a);
            strike = null;
            int peakFrame = int.MaxValue;
            float warp = 1f;
            // clip time of a frame; with recoverSpeed the way back after the farthest reach runs faster
            float ClipTime(int f) => f <= peakFrame ? (f - a) * bvh.frameTime : ((peakFrame - a) + (f - peakFrame) / warp) * bvh.frameTime;
            if (!string.IsNullOrEmpty(limb))
            {
                var reach = new List<(int frame, float dist, float height)>();
                for (int f = a; f <= b; f++)
                {
                    Align(f);
                    Apply(bvh, f);
                    var p = bvh[limb].position;
                    reach.Add((f, p.z, p.y));   // forward reach towards the opponent
                }
                var peak = reach.OrderByDescending(r => r.dist).First();
                if (s.recoverSpeed > 1f)
                {
                    peakFrame = peak.frame;
                    warp = s.recoverSpeed;
                }
                // active while the bone is within 15 % of its farthest reach around the peak
                float limit = peak.dist * 0.85f;
                int ip = reach.IndexOf(peak), i0 = ip, i1 = ip;
                while (i0 > 0 && reach[i0 - 1].dist >= limit)
                    i0--;
                while (i1 < reach.Count - 1 && reach[i1 + 1].dist >= limit)
                    i1++;
                strike = new StrikeInfo
                {
                    name = s.name, bone = limb, length = ClipTime(b), reach = peak.dist, height = peak.height,
                    hitStart = ClipTime(reach[i0].frame), hitPeak = ClipTime(peak.frame), hitEnd = ClipTime(reach[i1].frame),
                };
                Align(a);
                Apply(bvh, a);
            }

            var handler = new HumanPoseHandler(avatar, root.transform);
            var pose = new HumanPose();
            int muscles = HumanTrait.MuscleCount;
            var keys = new List<Keyframe>[muscles + 7];
            for (int k = 0; k < keys.Length; k++)
                keys[k] = new List<Keyframe>();
            var lastQ = Quaternion.identity;
            var hips0 = Vector3.zero;
            var hips1 = Vector3.zero;
            var sampled = new List<int>();
            for (int f = a; f <= b; f += step)
                sampled.Add(f);
            if (sampled[sampled.Count - 1] != b)
                sampled.Add(b);
            foreach (int f in sampled)
            {
                Align(f);
                Apply(bvh, f);
                if (f == a)
                    hips0 = bvh["Hips"].position;
                if (f == b)
                    hips1 = bvh["Hips"].position;
                handler.GetHumanPose(ref pose);
                float time = ClipTime(f);
                for (int m = 0; m < muscles; m++)
                    keys[m].Add(new Keyframe(time, pose.muscles[m]));
                var q = pose.bodyRotation;
                if (f > a && Quaternion.Dot(q, lastQ) < 0f)
                    q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                lastQ = q;
                float[] rootValues = { pose.bodyPosition.x, pose.bodyPosition.y, pose.bodyPosition.z, q.x, q.y, q.z, q.w };
                for (int k = 0; k < 7; k++)
                    keys[muscles + k].Add(new Keyframe(time, rootValues[k]));
            }
            handler.Dispose();

            // Loops walk on the spot: the steady travel is taken out of the body position (the fight
            // moves the fighter), only the sway stays.  Strikes keep their small lunge.
            var travelXZ = Vector2.zero;
            if (s.loop)
            {
                var tx = keys[muscles + 0];
                var tz = keys[muscles + 2];
                float x0 = tx[0].value, z0 = tz[0].value;
                float duration = Mathf.Max(1e-4f, tx[tx.Count - 1].time);
                travelXZ = new Vector2(tx[tx.Count - 1].value - x0, tz[tz.Count - 1].value - z0);
                for (int i = 0; i < tx.Count; i++)
                {
                    float u = tx[i].time / duration;
                    tx[i] = new Keyframe(tx[i].time, tx[i].value - x0 - travelXZ.x * u);
                    tz[i] = new Keyframe(tz[i].time, tz[i].value - z0 - travelXZ.y * u);
                }
            }

            var clip = new AnimationClip { name = s.name, frameRate = Mathf.Round(1f / (bvh.frameTime * step)) };
            var bindings = new List<EditorCurveBinding>();
            var curves = new List<AnimationCurve>();
            string[] rootNames = { "RootT.x", "RootT.y", "RootT.z", "RootQ.x", "RootQ.y", "RootQ.z", "RootQ.w" };
            for (int k = 0; k < keys.Length; k++)
            {
                string attribute = k < muscles ? MuscleAttribute(HumanTrait.MuscleName[k]) : rootNames[k - muscles];
                var curve = new AnimationCurve(keys[k].ToArray());
                for (int i = 0; i < curve.length; i++)
                    curve.SmoothTangents(i, 0f);
                bindings.Add(EditorCurveBinding.FloatCurve("", typeof(Animator), attribute));
                curves.Add(curve);
            }
            AnimationUtility.SetEditorCurves(clip, bindings.ToArray(), curves.ToArray());
            clip.EnsureQuaternionContinuity();

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = s.loop;
            settings.loopBlend = false;
            settings.loopBlendOrientation = true;      // the body keeps the clip's heading
            settings.keepOriginalOrientation = true;
            settings.loopBlendPositionY = true;        // and its height
            settings.keepOriginalPositionY = true;
            settings.loopBlendPositionXZ = true;       // body position as in the clip (loops: on the spot)
            settings.keepOriginalPositionXZ = true;
            settings.heightFromFeet = false;
            settings.mirror = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            var travel = hips1 - hips0;
            float seconds = (b - a) * bvh.frameTime;
            report = $"{s.name}: {s.file} frames {a}..{b} ({seconds:F2} s{(step > 1 ? $", every {step}th frame" : "")}), heading {heading:F0} deg" +
                     (aim != 0f ? $" turning {aim:+0;-0} deg towards the reach" : "") +
                     (s.loop ? $", cycle match {cost:F4}" : "") +
                     $", hips travel ({travel.x:F2}, {travel.y:F2}, {travel.z:F2}) m = {(seconds > 0 ? new Vector2(travel.x, travel.z).magnitude / seconds : 0):F2} m/s" +
                     (s.loop ? $", taken out of the clip: ({travelXZ.x:F2}, {travelXZ.y:F2}) m" : "") +
                     (strike != null ? $"; strike {strike.bone} reach {strike.reach:F2} m at height {strike.height:F2} m, " +
                                       $"active {strike.hitStart:F2}-{strike.hitEnd:F2} s (peak {strike.hitPeak:F2})" : "");
            return clip;
        }

        static string Resolve(string folder) =>
            Path.IsPathRooted(folder) ? folder : Path.Combine(Path.GetDirectoryName(Application.dataPath), folder);

        /// <summary>
        /// Every segment of a source as a humanoid clip at clipPath(name).  Returns the strikes
        /// (segments with a limb) as measured on the capture itself.
        /// </summary>
        public static List<StrikeInfo> ConvertAll(Source src, Func<string, string> clipPath, string avatarPath = null)
        {
            string dir = Resolve(src.folder);
            var parsed = new Dictionary<string, Bvh>();
            Bvh Load(string file)
            {
                if (parsed.TryGetValue(file, out var b))
                    return b;
                string path = Path.Combine(dir, file);
                if (!File.Exists(path))
                    throw new FileNotFoundException($"mocap file {path} missing");
                b = Parse(path);
                b.human = Map(b, src.bones);
                b.scale = src.scale > 0f ? src.scale : AutoScale(b);
                parsed[file] = b;
                return b;
            }
            var avatars = new Dictionary<string, Avatar>();
            Avatar AvatarFor(string file)
            {
                string key = string.IsNullOrEmpty(src.reference) ? file : src.reference;
                if (avatars.TryGetValue(key, out var avatar))
                    return avatar;
                var bvh = Load(key);
                var (root, _) = Build(bvh);
                avatar = BuildAvatar(bvh, root, src.restFrame);
                Debug.Log($"[ROE] mocap avatar from {key} ({(src.restFrame < 0 ? "zero pose" : $"frame {src.restFrame}")}): valid={avatar.isValid} " +
                          $"human={avatar.isHuman}, scale {bvh.scale:G4} m/unit, head {bvh["Head"].position.y:F2} m; " +
                          string.Join(" ", HumanBones.Where(bvh.Has).Select(h => $"{h}={bvh.human[h].name}")));
                if (avatarPath != null)
                {
                    AssetDatabase.DeleteAsset(avatarPath);
                    AssetDatabase.CreateAsset(avatar, avatarPath);
                }
                Object.DestroyImmediate(root);
                if (!avatar.isValid || !avatar.isHuman)
                    throw new Exception($"mocap avatar from {key} is not a valid humanoid");
                avatars[key] = avatar;
                return avatar;
            }

            var headings = new Dictionary<string, float>();
            var stances = new Dictionary<string, float>();
            var strikes = new List<StrikeInfo>();
            foreach (var s in src.segments.OrderBy(x => Reference(x) == null ? 0 : 1))
            {
                var bvh = Load(s.file);
                var avatar = AvatarFor(s.file);
                var (root, align) = Build(bvh);
                // An avatar built on another file's skeleton binds by joint name; files of one dataset
                // share their joints and offsets, so the T-pose (bind) values carry over.
                var animator = root.AddComponent<Animator>();
                animator.avatar = avatar;
                int step = src.fps > 0f ? Mathf.Max(1, Mathf.RoundToInt(1f / (bvh.frameTime * src.fps))) : 1;
                var clip = Convert(bvh, s, avatar, root, align, step, headings, stances, out var strike, out string report);
                Object.DestroyImmediate(root);
                string assetPath = clipPath(s.name);
                Directory.CreateDirectory(Path.GetDirectoryName(assetPath));
                clip.name = Path.GetFileNameWithoutExtension(assetPath);
                AssetDatabase.DeleteAsset(assetPath);
                AssetDatabase.CreateAsset(clip, assetPath);
                Debug.Log("[ROE] " + Path.GetFileName(assetPath) + " <- " + report);
                if (strike != null)
                {
                    strikes.RemoveAll(x => x.name == s.name);
                    strikes.Add(strike);
                }
            }
            AssetDatabase.SaveAssets();
            return strikes;
        }

        /// <summary>
        /// Check pictures: every fighter in every mocap clip, a few evenly spaced frames each, seen
        /// from the side the strikes go to.
        /// Batch mode: -executeMethod RoeFighter.EditorTools.RoeMocap.Preview [-roeOut dir] [-roeClips jab,kick] [-roeShots 6]
        ///   [-roeClipDir Assets/.../motionpacks/my_pack]  (clips named &lt;name&gt;.anim there instead of the Bandai ones)
        /// </summary>
        [MenuItem("ROE Fighter/Mocap/Preview stills")]
        public static void Preview()
        {
            string outDir = RoeCapture.Arg("-roeOut", Path.Combine(Path.GetDirectoryName(Application.dataPath), "_work", "mocap", "preview"));
            int shots = int.Parse(RoeCapture.Arg("-roeShots", "6"));
            string clipDir = RoeCapture.Arg("-roeClipDir", null);
            var names = RoeCapture.Arg("-roeClips", clipDir != null
                ? string.Join(",", Directory.GetFiles(clipDir, "*.anim").Select(Path.GetFileNameWithoutExtension))
                : string.Join(",", Bandai.segments.Select(s => s.name))).Split(',');
            ShaderUtil.allowAsyncCompilation = false;
            var studio = RoeStudio.Build();
            foreach (var c in RoeManifest.Load().WithModels)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RoeHumanoid.FighterPath(c.id));
                if (prefab == null)
                    continue;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                bool warmed = false;
                foreach (var name in names)
                {
                    var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipDir != null ? $"{clipDir}/{name}.anim" : ClipPath(name));
                    if (clip == null)
                        continue;
                    // seen from the fighter's left, a little in front: punches and kicks go to the right of the picture
                    var view = Quaternion.Euler(0f, -70f, 0f) * Vector3.forward;
                    studio.LightFrom(view);
                    for (int i = 0; i < shots; i++)
                    {
                        float time = clip.length * i / Mathf.Max(1, shots - 1);
                        RoeCapture.Pose(go, clip, time);
                        studio.Aim(new Vector3(0f, 0.9f, 0.3f), view, 8f, 0f, 1.25f / Mathf.Tan(13.5f * Mathf.Deg2Rad), 27f);
                        if (!warmed)
                        {
                            RoeCapture.Render(studio.camera, 256, 256, Path.Combine(outDir, "_warmup.jpg"), 80);
                            RoeCapture.Render(studio.camera, 256, 256, Path.Combine(outDir, "_warmup.jpg"), 80);
                            warmed = true;
                        }
                        RoeCapture.Render(studio.camera, 480, 600, Path.Combine(outDir, $"{c.id}_{name}_{i}.jpg"), 88);
                    }
                }
                RoeCapture.EndPosing();
                Object.DestroyImmediate(go);
                Debug.Log($"[ROE] mocap preview: {c.id} done");
            }
        }

        /// <summary>
        /// Side steps, made here: the mocap has no step to the side while facing forward (its
        /// "walk-left" turns and walks).  The guard pose stays; the legs shuffle: the leading leg
        /// lifts and steps out, then the trailing leg lifts and closes in, while the fight moves the
        /// fighter sideways.  Angles in degrees, turned into muscle values with Unity's default ranges.
        /// (No longer played: the fight steps the legs itself, see FighterRig.SideStep.)
        /// </summary>
        static void BuildSideSteps()
        {
            var guard = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath("guard"));
            if (guard == null)
                return;
            var bindings = AnimationUtility.GetCurveBindings(guard);
            var baseValues = bindings.ToDictionary(b => b.propertyName, b => AnimationUtility.GetEditorCurve(guard, b).Evaluate(0f));
            int Muscle(string name) => Array.IndexOf(HumanTrait.MuscleName, name);
            float Delta(string muscle, float degrees)
            {
                int i = Muscle(muscle);
                float range = degrees >= 0f ? HumanTrait.GetMuscleDefaultMax(i) : -HumanTrait.GetMuscleDefaultMin(i);
                return degrees / Mathf.Max(1f, range);
            }
            const int frames = 16;              // one shuffle: 0.53 s at 30 fps
            const float fps = 30f;
            foreach (var (name, lead, trail, side) in new[] { ("side_right", "Right", "Left", 1f), ("side_left", "Left", "Right", -1f) })
            {
                var keys = bindings.ToDictionary(b => b.propertyName, b => new List<Keyframe>());
                for (int k = 0; k <= frames; k++)
                {
                    float u = k / (float)frames;
                    float first = u < 0.5f ? u / 0.5f : 1f;
                    float second = u < 0.5f ? 0f : (u - 0.5f) / 0.5f;
                    float spread = u < 0.5f ? Mathf.SmoothStep(0f, 1f, first) : 1f - Mathf.SmoothStep(0f, 1f, second);
                    float liftLead = u < 0.5f ? Mathf.Sin(Mathf.PI * first) : 0f;
                    float liftTrail = u < 0.5f ? 0f : Mathf.Sin(Mathf.PI * second);
                    var v = new Dictionary<string, float>(baseValues);
                    void Add(string attribute, float delta)
                    {
                        if (v.ContainsKey(attribute))
                            v[attribute] += delta;
                    }
                    // both legs open while the leading foot travels, close while the trailing one follows
                    Add($"{lead} Upper Leg In-Out", Delta($"{lead} Upper Leg In-Out", 13f * spread));
                    Add($"{trail} Upper Leg In-Out", Delta($"{trail} Upper Leg In-Out", 13f * spread));
                    Add($"{lead} Upper Leg Front-Back", Delta($"{lead} Upper Leg Front-Back", -14f * liftLead));
                    Add($"{lead} Lower Leg Stretch", Delta($"{lead} Lower Leg Stretch", -22f * liftLead));
                    Add($"{trail} Upper Leg Front-Back", Delta($"{trail} Upper Leg Front-Back", -12f * liftTrail));
                    Add($"{trail} Lower Leg Stretch", Delta($"{trail} Lower Leg Stretch", -20f * liftTrail));
                    Add("Spine Left-Right", Delta("Spine Left-Right", 3f * side));
                    Add("RootT.y", -0.012f * (liftLead + liftTrail));
                    foreach (var pair in v)
                        keys[pair.Key].Add(new Keyframe(k / fps, pair.Value));
                }
                var clip = new AnimationClip { name = "mc_" + name, frameRate = fps };
                var curves = bindings.Select(b =>
                {
                    var c = new AnimationCurve(keys[b.propertyName].ToArray());
                    for (int i = 0; i < c.length; i++)
                        c.SmoothTangents(i, 0f);
                    return c;
                }).ToArray();
                AnimationUtility.SetEditorCurves(clip, bindings, curves);
                var settings = AnimationUtility.GetAnimationClipSettings(guard);
                settings.loopTime = true;
                AnimationUtility.SetAnimationClipSettings(clip, settings);
                AssetDatabase.DeleteAsset(ClipPath(name));
                AssetDatabase.CreateAsset(clip, ClipPath(name));
                Debug.Log($"[ROE] mc_{name}: made from the guard pose, {frames / fps:F2} s per shuffle");
            }
        }

        [MenuItem("ROE Fighter/Mocap/Import basic moves")]
        public static void ImportAll()
        {
            Directory.CreateDirectory(OutDir);
            var strikes = ConvertAll(Bandai, ClipPath, $"{OutDir}/mocap_actor_avatar.asset");
            BuildSideSteps();
            var table = new StrikeTable();
            table.moves.AddRange(strikes.OrderBy(x => x.name));
            File.WriteAllText(MovesPath, JsonUtility.ToJson(table, true));
            AssetDatabase.ImportAsset(MovesPath);
            Debug.Log($"[ROE] mocap strikes: {MovesPath} ({table.moves.Count} moves)");
            AssetDatabase.SaveAssets();
        }
    }
}
