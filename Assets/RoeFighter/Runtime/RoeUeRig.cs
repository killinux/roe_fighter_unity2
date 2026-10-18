using System.Collections.Generic;
using UnityEngine;

namespace RoeFighter
{
    /// <summary>
    /// What a Unity humanoid leaves out of an Unreal Engine 5 / MetaHuman body (Vindictus' Fiona), put back on every pose
    /// whatever clip made it (FighterRig: Rest before the animation is evaluated, Distribute after it, DriveTwists on the
    /// final pose after the legs' IK):
    ///   - spine and neck: a humanoid has three spine bones and one neck, MetaHuman five and two.  The humanoid maps
    ///     spine_01 / 03 / 05 and neck_01, so the bend of spine_02, spine_04 and neck_02 lands in the mapped bone above
    ///     each: her chest and head were 3.2 cm from the game's pose on average (12 cm at worst) in her own clips.  Each
    ///     unmapped joint takes back a share of the pair's bend (spine02, spine04, neck02), the upper bone keeping its turn;
    ///     the defaults are fitted on her 90 game clips by where they put her chest and head (1.3 cm then;
    ///     tools/vdf_research/spine_share_sim.py).
    ///   - twist bones: a humanoid does not move them.  As in the game, each turns by where it sits along its limb: the
    ///     forearm's and shin's with that share of the hand's / foot's roll about the limb (1/3 and 2/3 of the way from
    ///     the elbow: 1/3 and 2/3 of it - the game's clips have 0.35 and 0.72 in a sword swing), the upper arm's and
    ///     thigh's undoing the limb's own roll towards the shoulder / hip (1/3 of the way: -2/3 of it; the game's -0.71).
    ///     Left alone, her wrists twisted like a sweet wrapper when the sword hand rolled (160 degrees in her first attack).
    ///   - since 10-05 (user "继续": the elbow and knee correctives were next) the twist bones, the joints' corrective roots
    ///     and the fingers' half joints follow the game's own procedural rig, BaseBody_PCF/Model/Rig_proc_ControlRig, read
    ///     out of the cooked package (tools/vdf_research/controlrig_decode.py: its constants and its RigVM byte code):
    ///       per limb (start bone S, end bone E): S's turn from its bind pose split into swing and twist about the bone
    ///       (its X axis); the corrective root under S gets the inverse twist times slerp(inverse swing, identity,
    ///       swing blend) - it follows that share of S's swing and none of its twist, so the correctives under it
    ///       (lowerarm_in/out/fwd/bck ...) sit at the half-bent joint; each twist bone gets slerp(q, identity, its twist
    ///       blend), q being E's twist (forearm, shin) or the inverse of S's own (upper arm, thigh);
    ///       per finger half joint: half of the inverse of its finger joint's turn, the yaw going over to the whole of it
    ///       as the joint's own yaw goes from 0 to 20 degrees.
    ///     The first version (each twist bone by where it sits along its limb) stays as TwistRig.ByPosition.
    /// Bind rotations are taken when the component wakes (the prefab's bind pose).
    /// </summary>
    public class RoeUeRig : MonoBehaviour
    {
        [Tooltip("Share of the bend between spine_01 and spine_03 that spine_02 takes back (0: as the humanoid leaves it)")]
        [Range(0f, 1f)] public float spine02 = 0.8f;
        [Tooltip("Share of the bend between spine_03 and spine_05 that spine_04 takes back")]
        [Range(0f, 1f)] public float spine04 = 0.3f;
        [Tooltip("Share of the bend between neck_01 and the head that neck_02 takes back")]
        [Range(0f, 1f)] public float neck02 = 0f;
        [Tooltip("How much of what the rig gives them the twist bones (and corrective roots, half joints) take (0: they stay at rest)")]
        [Range(0f, 1f)] public float twist = 1f;

        public enum TwistRig { Off, ByPosition, Game }
        [Tooltip("Game: the game's own procedural rig (twist bones, corrective roots, finger half joints); ByPosition: the first " +
                 "version (twist bones by where they sit along the limb, the rest left with their parents); Off: all at rest")]
        public TwistRig twistRig = TwistRig.Game;

        /// <summary>One limb of the game's rig (Compute Twist): the defaults are its constants.</summary>
        [System.Serializable]
        public class Limb
        {
            public string start, end, root;
            public string[] twistBones;
            [Tooltip("Per twist bone: the share of the twist it does NOT take (the game's Twist Blend)")]
            public float[] twistBlend;
            [Tooltip("Share of the start bone's swing the corrective root follows")]
            public float swingBlend = 0.5f;
            [Tooltip("The twist bones take the end bone's twist (forearm, shin); else they undo the start bone's own (upper arm, thigh)")]
            public bool fromEnd;
        }

        public Limb[] limbs = GameLimbs();
        [Tooltip("Share of the inverse of its finger joint's turn a half joint takes")]
        [Range(0f, 1f)] public float halfJoint = 0.5f;
        [Tooltip("Degrees of the finger joint's own yaw over which the half joint's yaw goes from that share to the whole")]
        public float halfJointYawRange = 20f;

        /// <summary>Rig_proc_ControlRig's eight Compute Twist calls, as decoded (10-05).</summary>
        public static Limb[] GameLimbs()
        {
            var list = new List<Limb>();
            foreach (var s in new[] { "l", "r" })
            {
                list.Add(new Limb { start = $"upperarm_{s}", end = $"lowerarm_{s}", root = $"upperarm_correctiveRoot_{s}",
                                    twistBones = new[] { $"upperarm_twist_01_{s}", $"upperarm_twist_02_{s}" }, twistBlend = new[] { 0.2f, 0.8f }, swingBlend = 0.5f });
                list.Add(new Limb { start = $"thigh_{s}", end = $"calf_{s}", root = $"thigh_correctiveRoot_{s}",
                                    twistBones = new[] { $"thigh_twist_01_{s}", $"thigh_twist_02_{s}" }, twistBlend = new[] { 0f, 0.6f }, swingBlend = 0.6f });
                list.Add(new Limb { start = $"lowerarm_{s}", end = $"hand_{s}", root = $"lowerarm_correctiveRoot_{s}",
                                    twistBones = new[] { $"lowerarm_twist_01_{s}", $"lowerarm_twist_02_{s}" }, twistBlend = new[] { 0.4f, 0.8f }, swingBlend = 0.5f, fromEnd = true });
                list.Add(new Limb { start = $"calf_{s}", end = $"foot_{s}", root = $"calf_correctiveRoot_{s}",
                                    twistBones = new[] { $"calf_twist_01_{s}", $"calf_twist_02_{s}" }, twistBlend = new[] { 0.5f, 0.5f }, swingBlend = 0.5f, fromEnd = true });
            }
            return list.ToArray();
        }

        /// <summary>The finger half joints the game's rig turns (Compute Half Fingers).</summary>
        static IEnumerable<string> HalfJoints()
        {
            foreach (var s in new[] { "l", "r" })
            {
                foreach (var finger in new[] { "index", "middle", "pinky", "ring" })
                    for (int k = 1; k <= 3; k++)
                        yield return $"{finger}_0{k}_half_{s}";
                yield return $"thumb_02_half_{s}";
                yield return $"thumb_03_half_{s}";
            }
        }

        class LimbRig
        {
            public Limb data;
            public Transform start, end, root;
            public Quaternion startRest, endRest, rootRest;
            public Transform[] twist;
            public Quaternion[] twistRest;
        }

        class Half
        {
            public Transform bone, joint;
            public Quaternion boneRest, jointRest;
        }

        readonly List<LimbRig> limbRigs = new List<LimbRig>();
        readonly List<Half> halves = new List<Half>();

        class Pair
        {
            public Transform low, high;
            public Quaternion lowRest, highRest;
            public System.Func<float> share;
        }

        class Twist
        {
            public Transform bone, limb, driver;   // the twist bone, the limb bone it hangs on, the bone whose roll it shares
            public Quaternion boneRest, driverRest;
            public Vector3 axis;                    // along the limb, in the limb bone's frame
            public float share;                     // of the driver's roll (negative: undoes the limb's own roll)
            public bool own;                        // the driver is the limb itself (upper arm, thigh)
        }

        readonly List<Pair> pairs = new List<Pair>();
        readonly List<Twist> twists = new List<Twist>();
        bool built;

        public int TwistBones => twists.Count;
        public int GameLimbCount => limbRigs.Count;
        public int HalfJointCount => halves.Count;

        void Awake() => Build();

        /// <summary>An Unreal / MetaHuman body: pelvis, spine_01..05, neck_01/02.</summary>
        public static bool IsUe(Transform root)
        {
            bool pelvis = false, spine = false;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                pelvis |= t.name == "pelvis";
                spine |= t.name == "spine_02";
            }
            return pelvis && spine;
        }

        public void Build()
        {
            if (built)
                return;
            built = true;
            var byName = new Dictionary<string, Transform>();
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (!byName.ContainsKey(t.name))
                    byName[t.name] = t;
            Transform Get(string n) => byName.TryGetValue(n, out var t) ? t : null;

            void AddPair(string low, string high, System.Func<float> share)
            {
                var l = Get(low);
                var h = Get(high);
                if (l != null && h != null && h.parent == l)
                    pairs.Add(new Pair { low = l, high = h, lowRest = l.localRotation, highRest = h.localRotation, share = share });
            }
            AddPair("spine_02", "spine_03", () => spine02);
            AddPair("spine_04", "spine_05", () => spine04);
            AddPair("neck_02", "head", () => neck02);

            // limb bone, the joint below it, the bone whose roll the twist bones share, own roll or the next joint's
            foreach (var s in new[] { "l", "r" })
            {
                AddLimb(Get($"upperarm_{s}"), Get($"lowerarm_{s}"), Get($"upperarm_{s}"), "upperarm_twist_", s, byName);
                AddLimb(Get($"lowerarm_{s}"), Get($"hand_{s}"), Get($"hand_{s}"), "lowerarm_twist_", s, byName);
                AddLimb(Get($"thigh_{s}"), Get($"calf_{s}"), Get($"thigh_{s}"), "thigh_twist_", s, byName);
                AddLimb(Get($"calf_{s}"), Get($"foot_{s}"), Get($"foot_{s}"), "calf_twist_", s, byName);
            }

            // the game's rig
            foreach (var l in limbs ?? new Limb[0])
            {
                var rig = new LimbRig { data = l, start = Get(l.start), end = Get(l.end), root = Get(l.root) };
                if (rig.start == null)
                    continue;
                rig.startRest = rig.start.localRotation;
                rig.endRest = rig.end != null ? rig.end.localRotation : Quaternion.identity;
                rig.rootRest = rig.root != null ? rig.root.localRotation : Quaternion.identity;
                var bones = l.twistBones ?? new string[0];
                rig.twist = new Transform[bones.Length];
                rig.twistRest = new Quaternion[bones.Length];
                for (int i = 0; i < bones.Length; i++)
                {
                    rig.twist[i] = Get(bones[i]);
                    rig.twistRest[i] = rig.twist[i] != null ? rig.twist[i].localRotation : Quaternion.identity;
                }
                limbRigs.Add(rig);
            }
            foreach (var name in HalfJoints())
            {
                var bone = Get(name);
                if (bone != null && bone.parent != null)
                    halves.Add(new Half { bone = bone, joint = bone.parent, boneRest = bone.localRotation, jointRest = bone.parent.localRotation });
            }
        }

        void AddLimb(Transform limb, Transform next, Transform driver, string prefix, string side, Dictionary<string, Transform> byName)
        {
            if (limb == null || next == null || driver == null)
                return;
            var along = next.localPosition;
            float length = along.magnitude;
            if (length < 1e-4f)
                return;
            var axis = along / length;
            bool own = driver == limb;
            foreach (Transform child in limb)
            {
                if (!child.name.StartsWith(prefix) || !child.name.EndsWith("_" + side))
                    continue;
                // where it sits along the limb: 0 at the limb bone, 1 at the next joint
                float f = Mathf.Clamp01(Vector3.Dot(child.localPosition, axis) / length);
                twists.Add(new Twist
                {
                    bone = child, limb = limb, driver = driver, own = own, axis = axis,
                    boneRest = child.localRotation, driverRest = driver.localRotation,
                    share = own ? f - 1f : f,
                });
            }
        }

        /// <summary>Before the animation is evaluated: the unmapped spine and neck joints at rest (the humanoid expects them there).</summary>
        public void Rest()
        {
            foreach (var p in pairs)
                p.low.localRotation = p.lowRest;
        }

        /// <summary>After it: each pair's bend shared between its two joints, the upper bone's turn kept.</summary>
        public void Distribute()
        {
            foreach (var p in pairs)
            {
                float s = p.share();
                if (s <= 0f)
                    continue;
                var turn = p.low.localRotation * p.high.localRotation;
                var bend = Quaternion.Inverse(p.lowRest * p.highRest) * turn;            // in the upper bone's rest frame
                var lowBend = p.highRest * bend * Quaternion.Inverse(p.highRest);         // the same, in the lower one's
                p.low.localRotation = p.lowRest * Quaternion.Slerp(Quaternion.identity, lowBend, s);
                p.high.localRotation = p.highRest * Quaternion.Slerp(Quaternion.identity, bend, 1f - s);
            }
        }

        /// <summary>On the final pose: the twist bones (and with the game's rig the corrective roots and finger half joints).</summary>
        public void DriveTwists()
        {
            if (twist <= 0f || twistRig == TwistRig.Off)
            {
                foreach (var t in twists)
                    t.bone.localRotation = t.boneRest;
                RestGameRig();
                return;
            }
            if (twistRig == TwistRig.Game)
            {
                DriveGameRig();
                return;
            }
            RestGameRig();
            foreach (var t in twists)
            {
                // the limb's own roll (in its own frame), or the next joint's (in the limb's frame)
                var delta = t.own ? Quaternion.Inverse(t.driverRest) * t.driver.localRotation
                                  : t.driver.localRotation * Quaternion.Inverse(t.driverRest);
                float roll = Roll(delta, t.axis);
                t.bone.localRotation = Quaternion.AngleAxis(roll * t.share * twist, t.axis) * t.boneRest;
            }
        }

        void RestGameRig()
        {
            foreach (var l in limbRigs)
            {
                if (l.root != null)
                    l.root.localRotation = l.rootRest;
                for (int i = 0; i < l.twist.Length; i++)
                    if (l.twist[i] != null)
                        l.twist[i].localRotation = l.twistRest[i];
            }
            foreach (var h in halves)
                h.bone.localRotation = h.boneRest;
        }

        Quaternion Blend(Quaternion rest, Quaternion value) => twist >= 1f ? value : Quaternion.Slerp(rest, value, twist);

        /// <summary>
        /// Rig_proc_ControlRig as the game runs it after the animation, in her bones' own frames (Unreal's turned half a
        /// turn about z: the bone still lies along x, so the twist axis is the same line and the algebra carries over).
        /// </summary>
        void DriveGameRig()
        {
            foreach (var l in limbRigs)
            {
                // the start bone's turn from its bind pose, in its own frame: swing and twist about the bone
                SwingTwist(Quaternion.Inverse(l.startRest) * l.start.localRotation, Vector3.right, out var swing, out var startTwist);
                var undo = Quaternion.Inverse(startTwist);
                if (l.root != null)
                    l.root.localRotation = Blend(l.rootRest, undo * Quaternion.Slerp(Quaternion.Inverse(swing), Quaternion.identity, l.data.swingBlend));
                var q = undo;
                if (l.data.fromEnd && l.end != null)
                    SwingTwist(Quaternion.Inverse(l.endRest) * l.end.localRotation, Vector3.right, out _, out q);
                var blend = l.data.twistBlend ?? new float[0];
                for (int i = 0; i < l.twist.Length; i++)
                    if (l.twist[i] != null)
                        l.twist[i].localRotation = Blend(l.twistRest[i], Quaternion.Slerp(q, Quaternion.identity, i < blend.Length ? blend[i] : 0.5f));
            }
            foreach (var h in halves)
            {
                var turn = Quaternion.Inverse(h.jointRest) * h.joint.localRotation;
                var undo = Quaternion.Inverse(turn);
                var part = Quaternion.Slerp(Quaternion.identity, undo, halfJoint);     // the game: slerp(undo, identity, 0.5)
                // in Unreal's frame and Unreal's rotators (pitch about y, yaw about z, roll about x)
                var r1 = UeRotator(ToUe(part));
                float ownYaw = UeRotator(ToUe(turn)).y;
                float wholeYaw = UeRotator(ToUe(undo)).y;
                float t = halfJointYawRange > 1e-4f ? Mathf.Clamp01(ownYaw / halfJointYawRange) : 1f;
                var value = new Vector3(r1.x, Mathf.Lerp(r1.y, wholeYaw, t), r1.z);
                h.bone.localRotation = Blend(h.boneRest, ToUe(UeQuat(value)));
            }
        }

        /// <summary>Her bone frames and Unreal's: half a turn about z apart (the same map both ways).</summary>
        static Quaternion ToUe(Quaternion q) => new Quaternion(-q.x, -q.y, q.z, q.w);

        /// <summary>FQuat::ToSwingTwist: q = swing * twist, twist about the axis.</summary>
        public static void SwingTwist(Quaternion q, Vector3 axis, out Quaternion swing, out Quaternion twist)
        {
            var p = Vector3.Dot(axis, new Vector3(q.x, q.y, q.z)) * axis;
            twist = new Quaternion(p.x, p.y, p.z, q.w);
            float n = Mathf.Sqrt(twist.x * twist.x + twist.y * twist.y + twist.z * twist.z + twist.w * twist.w);
            twist = n < 1e-8f ? Quaternion.identity : new Quaternion(twist.x / n, twist.y / n, twist.z / n, twist.w / n);
            swing = q * Quaternion.Inverse(twist);
        }

        /// <summary>FQuat::Rotator: (pitch, yaw, roll) in degrees.</summary>
        public static Vector3 UeRotator(Quaternion q)
        {
            float x = q.x, y = q.y, z = q.z, w = q.w;
            float test = z * x - w * y;
            float yawY = 2f * (w * z + x * y), yawX = 1f - 2f * (y * y + z * z);
            float yaw = Mathf.Atan2(yawY, yawX) * Mathf.Rad2Deg;
            if (test < -0.4999995f)
                return new Vector3(-90f, yaw, NormalizeAxis(-yaw - 2f * Mathf.Atan2(x, w) * Mathf.Rad2Deg));
            if (test > 0.4999995f)
                return new Vector3(90f, yaw, NormalizeAxis(yaw - 2f * Mathf.Atan2(x, w) * Mathf.Rad2Deg));
            float pitch = Mathf.Asin(Mathf.Clamp(2f * test, -1f, 1f)) * Mathf.Rad2Deg;
            float roll = Mathf.Atan2(-2f * (w * x + y * z), 1f - 2f * (x * x + y * y)) * Mathf.Rad2Deg;
            return new Vector3(pitch, yaw, roll);
        }

        /// <summary>FRotator::Quaternion from (pitch, yaw, roll) in degrees.</summary>
        public static Quaternion UeQuat(Vector3 r)
        {
            float half = Mathf.Deg2Rad * 0.5f;
            float sp = Mathf.Sin((r.x % 360f) * half), cp = Mathf.Cos((r.x % 360f) * half);
            float sy = Mathf.Sin((r.y % 360f) * half), cy = Mathf.Cos((r.y % 360f) * half);
            float sr = Mathf.Sin((r.z % 360f) * half), cr = Mathf.Cos((r.z % 360f) * half);
            return new Quaternion(cr * sp * sy - sr * cp * cy, -cr * sp * cy - sr * cp * sy, cr * cp * sy - sr * sp * cy, cr * cp * cy + sr * sp * sy);
        }

        static float NormalizeAxis(float a)
        {
            a %= 360f;
            if (a > 180f)
                a -= 360f;
            else if (a < -180f)
                a += 360f;
            return a;
        }

        /// <summary>The twist of a rotation about an axis (swing-twist), degrees in -180..180.</summary>
        public static float Roll(Quaternion q, Vector3 axis)
        {
            var v = new Vector3(q.x, q.y, q.z);
            float along = Vector3.Dot(v, axis);
            float angle = 2f * Mathf.Atan2(along, q.w) * Mathf.Rad2Deg;
            if (angle > 180f)
                angle -= 360f;
            else if (angle < -180f)
                angle += 360f;
            return angle;
        }
    }
}
