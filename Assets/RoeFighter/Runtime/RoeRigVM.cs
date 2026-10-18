using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RoeFighter
{
    /// <summary>
    /// An Unreal Control Rig run from its own byte code: the RigVM of UE 4.26 (tools/sb_controlrig.py reads it out of the
    /// cooked rig - Stellar Blade's pleated skirt, CH_P_EVE_37_Skirt_CtlRig - into a program).  Every register is a run
    /// of floats in one memory (a transform: rotation x y z w, translation x y z, scale x y z; a quaternion 4; a rotator
    /// pitch yaw roll; a vector 3; a float or bool 1), set at the start to what the game's package holds (the work memory as
    /// the editor last left it, the literals); each step is a Copy (part of a register into part of another) or a rig unit
    /// on its pins, in the rig's order.  The units do what UE 4.26's do (FRigUnit_*::Execute, FTransform, FQuat::Rotator,
    /// FRotator::Quaternion); a unit this class does not know stops the program from loading.
    /// The bones are seen in Unreal's terms (IBones: its bone frames, its component space, centimetres): UnityBones turns
    /// a fighter's bones into them and back.
    /// Replay checks the units: the work memory holds the editor's last run, every register what its unit made of the
    /// others, so running the units again on those values (the bones' reads and writes left out) has to give them back.
    /// </summary>
    public class RoeRigVM
    {
        [Serializable]
        public class Register
        {
            public string name, kind, bone;
            public float[] value;
        }

        [Serializable]
        public class Op
        {
            public string unit;
            public int[] args;          // per pin: register, first float, count (-1: the whole register)
        }

        [Serializable]
        public class Reference
        {
            public string bone;
            public float[] local, global;       // the rig's own skeleton at its reference pose (transforms, Unreal's terms)
        }

        [Serializable]
        public class Program
        {
            public string name, abp, node, kind;
            public float alpha = 1f;
            public List<string> skip = new List<string>(), skipWhy = new List<string>();
            public List<Register> registers = new List<Register>();
            public List<Op> code = new List<Op>();
            public List<Reference> reference = new List<Reference>();
        }

        /// <summary>A transform as Unreal has one: rotation, translation, scale (FTransform).</summary>
        public struct UeTransform
        {
            public Quaternion q;
            public Vector3 t, s;

            public UeTransform(Quaternion q, Vector3 t, Vector3 s)
            {
                this.q = q;
                this.t = t;
                this.s = s;
            }

            /// <summary>FTransform A * B: A, then B (B's rotation * A's; B's rotation of B's scale x A's translation, plus B's).</summary>
            public static UeTransform operator *(UeTransform a, UeTransform b) =>
                new UeTransform(b.q * a.q, b.q * Vector3.Scale(b.s, a.t) + b.t, Vector3.Scale(a.s, b.s));

            /// <summary>FTransform::Inverse.</summary>
            public UeTransform Inverse()
            {
                var inv = Quaternion.Inverse(q);
                var scale = new Vector3(Recip(s.x), Recip(s.y), Recip(s.z));
                return new UeTransform(inv, inv * Vector3.Scale(scale, -t), scale);
            }

            static float Recip(float x) => Mathf.Abs(x) <= 1e-8f ? 0f : 1f / x;
        }

        /// <summary>The skeleton as a rig sees it: Unreal's bone frames and component space, centimetres.</summary>
        public interface IBones
        {
            UeTransform Local(int bone);
            UeTransform Global(int bone);
            UeTransform InitialLocal(int bone);
            void SetLocal(int bone, UeTransform x, bool propagate, float weight);
            void SetGlobal(int bone, UeTransform x, bool propagate, float weight);
        }

        enum Unit
        {
            Copy, BeginExecution, GetTransform, SetTransform, MathQuaternionToRotator, MathQuaternionFromRotator,
            MathTransformInverse, MathTransformMul, MathFloatAdd, MathFloatSub, MathFloatMul, MathFloatDiv, MathFloatMax,
            MathFloatMin, MathFloatNegate, MathFloatAbs, MathFloatRemap, MathFloatGreater, MathFloatGreaterEqual,
            MathFloatLess, MathFloatLessEqual, MathBoolAnd, MathBoolOr, MathBoolNot,
        }

        struct Step
        {
            public Unit unit;
            public int[] reg, at, n;    // per pin: register, where its floats start in the memory, how many
        }

        public readonly Program program;
        /// <summary>The bones the rig reads or writes (its bone keys), in the order of their first key.</summary>
        public readonly string[] bones;
        /// <summary>The bones it writes; the bones it reads or writes in their parent's space.</summary>
        public readonly int[] written, local;
        readonly float[] initial;
        readonly float[] memory;
        readonly int[] start, size;
        readonly int[] boneOf;          // per register: its bone (a key), else -1
        readonly Step[] steps;

        static readonly Dictionary<string, int> Sizes = new Dictionary<string, int>
        {
            ["transform"] = 10, ["quat"] = 4, ["rotator"] = 3, ["vector"] = 3, ["float"] = 1, ["bool"] = 1,
            ["key"] = 0, ["cache"] = 0, ["context"] = 0,
        };

        public RoeRigVM(Program p)
        {
            program = p;
            int count = p.registers.Count;
            start = new int[count];
            size = new int[count];
            boneOf = new int[count];
            var boneList = new List<string>();
            int total = 0;
            for (int i = 0; i < count; i++)
            {
                var r = p.registers[i];
                if (!Sizes.TryGetValue(r.kind ?? "", out int n))
                    throw new NotSupportedException($"{p.name}: register {r.name} is a {r.kind}");
                start[i] = total;
                size[i] = n;
                total += n;
                boneOf[i] = -1;
                if (r.kind == "key")
                {
                    int b = boneList.IndexOf(r.bone);
                    if (b < 0)
                    {
                        b = boneList.Count;
                        boneList.Add(r.bone);
                    }
                    boneOf[i] = b;
                }
            }
            bones = boneList.ToArray();
            initial = new float[total];
            for (int i = 0; i < count; i++)
            {
                var v = p.registers[i].value;
                if (size[i] > 0 && (v == null || v.Length != size[i]))
                    throw new NotSupportedException($"{p.name}: register {p.registers[i].name} has {v?.Length ?? 0} values, not {size[i]}");
                if (size[i] > 0)
                    Array.Copy(v, 0, initial, start[i], size[i]);
            }
            memory = (float[])initial.Clone();
            steps = new Step[p.code.Count];
            var writes = new HashSet<int>();
            var locals = new HashSet<int>();
            for (int k = 0; k < p.code.Count; k++)
            {
                var op = p.code[k];
                if (!Enum.TryParse(op.unit, out Unit unit))
                    throw new NotSupportedException($"{p.name}: step {k} runs {op.unit}, a unit RoeRigVM does not have");
                int pins = op.args.Length / 3;
                var s = new Step { unit = unit, reg = new int[pins], at = new int[pins], n = new int[pins] };
                for (int j = 0; j < pins; j++)
                {
                    int r = op.args[3 * j], first = op.args[3 * j + 1], n = op.args[3 * j + 2];
                    s.reg[j] = r;
                    s.at[j] = start[r] + first;
                    s.n[j] = n < 0 ? size[r] : n;
                }
                if (unit == Unit.Copy && s.n[0] != s.n[1])
                    throw new NotSupportedException($"{p.name}: step {k} copies {s.n[0]} floats into {s.n[1]}");
                if (unit == Unit.SetTransform)
                    writes.Add(boneOf[s.reg[0]]);
                if ((unit == Unit.GetTransform || unit == Unit.SetTransform) && initial[s.at[1]] == 0f)
                    locals.Add(boneOf[s.reg[0]]);
                steps[k] = s;
            }
            written = writes.OrderBy(x => x).ToArray();
            local = locals.OrderBy(x => x).ToArray();
        }

        /// <summary>One run of the rig on the bones; weight blends what it sets with what was there (the anim node's alpha).</summary>
        public void Run(IBones b, float weight = 1f)
        {
            foreach (var s in steps)
                Exec(s, memory, b, weight);
        }

        void Exec(Step s, float[] m, IBones b, float weight)
        {
            var at = s.at;
            switch (s.unit)
            {
                case Unit.Copy:
                    Array.Copy(m, at[0], m, at[1], s.n[0]);
                    break;
                case Unit.BeginExecution:
                    break;
                case Unit.GetTransform:
                {
                    // Item, Space (0 local, 1 global), bInitial, Transform, CachedIndex
                    if (b == null)
                        break;
                    int bone = boneOf[s.reg[0]];
                    bool global = m[at[1]] != 0f, initialPose = m[at[2]] != 0f;
                    if (initialPose && global)
                        throw new NotSupportedException($"{program.name}: the initial global transform is not kept");
                    Put(m, at[3], initialPose ? b.InitialLocal(bone) : global ? b.Global(bone) : b.Local(bone));
                    break;
                }
                case Unit.SetTransform:
                {
                    // Item, Space, bInitial, Transform, Weight, bPropagateToChildren, CachedIndex, ExecuteContext
                    if (b == null)
                        break;
                    if (m[at[2]] != 0f)
                        throw new NotSupportedException($"{program.name}: setting the initial pose");
                    int bone = boneOf[s.reg[0]];
                    var x = Get(m, at[3]);
                    float w = Mathf.Clamp01(m[at[4]]) * weight;
                    bool propagate = m[at[5]] != 0f;
                    if (m[at[1]] != 0f)
                        b.SetGlobal(bone, x, propagate, w);
                    else
                        b.SetLocal(bone, x, propagate, w);
                    break;
                }
                case Unit.MathQuaternionToRotator:
                {
                    var r = RoeUeRig.UeRotator(Quat(m, at[0]));
                    m[at[1]] = r.x;
                    m[at[1] + 1] = r.y;
                    m[at[1] + 2] = r.z;
                    break;
                }
                case Unit.MathQuaternionFromRotator:
                    PutQuat(m, at[1], RoeUeRig.UeQuat(new Vector3(m[at[0]], m[at[0] + 1], m[at[0] + 2])));
                    break;
                case Unit.MathTransformInverse:
                    Put(m, at[1], Get(m, at[0]).Inverse());
                    break;
                case Unit.MathTransformMul:
                    Put(m, at[2], Get(m, at[0]) * Get(m, at[1]));
                    break;
                case Unit.MathFloatAdd: m[at[2]] = m[at[0]] + m[at[1]]; break;
                case Unit.MathFloatSub: m[at[2]] = m[at[0]] - m[at[1]]; break;
                case Unit.MathFloatMul: m[at[2]] = m[at[0]] * m[at[1]]; break;
                case Unit.MathFloatDiv: m[at[2]] = Mathf.Abs(m[at[1]]) > 1e-8f ? m[at[0]] / m[at[1]] : 0f; break;
                case Unit.MathFloatMax: m[at[2]] = Mathf.Max(m[at[0]], m[at[1]]); break;
                case Unit.MathFloatMin: m[at[2]] = Mathf.Min(m[at[0]], m[at[1]]); break;
                case Unit.MathFloatNegate: m[at[1]] = -m[at[0]]; break;
                case Unit.MathFloatAbs: m[at[1]] = Mathf.Abs(m[at[0]]); break;
                case Unit.MathFloatRemap:
                {
                    // Value, SourceMinimum, SourceMaximum, TargetMinimum, TargetMaximum, bClamp, Result
                    float value = m[at[0]], s0 = m[at[1]], s1 = m[at[2]], t0 = m[at[3]], t1 = m[at[4]];
                    float ratio = Mathf.Abs(s1 - s0) <= 1e-8f ? 0f : (value - s0) / (s1 - s0);
                    if (m[at[5]] != 0f)
                        ratio = Mathf.Clamp01(ratio);
                    m[at[6]] = t0 + (t1 - t0) * ratio;
                    break;
                }
                case Unit.MathFloatGreater: m[at[2]] = m[at[0]] > m[at[1]] ? 1f : 0f; break;
                case Unit.MathFloatGreaterEqual: m[at[2]] = m[at[0]] >= m[at[1]] ? 1f : 0f; break;
                case Unit.MathFloatLess: m[at[2]] = m[at[0]] < m[at[1]] ? 1f : 0f; break;
                case Unit.MathFloatLessEqual: m[at[2]] = m[at[0]] <= m[at[1]] ? 1f : 0f; break;
                case Unit.MathBoolAnd: m[at[2]] = m[at[0]] != 0f && m[at[1]] != 0f ? 1f : 0f; break;
                case Unit.MathBoolOr: m[at[2]] = m[at[0]] != 0f || m[at[1]] != 0f ? 1f : 0f; break;
                case Unit.MathBoolNot: m[at[1]] = m[at[0]] != 0f ? 0f : 1f; break;
            }
        }

        static Quaternion Quat(float[] m, int i) => new Quaternion(m[i], m[i + 1], m[i + 2], m[i + 3]);

        static void PutQuat(float[] m, int i, Quaternion q)
        {
            m[i] = q.x;
            m[i + 1] = q.y;
            m[i + 2] = q.z;
            m[i + 3] = q.w;
        }

        static UeTransform Get(float[] m, int i) =>
            new UeTransform(Quat(m, i), new Vector3(m[i + 4], m[i + 5], m[i + 6]), new Vector3(m[i + 7], m[i + 8], m[i + 9]));

        static void Put(float[] m, int i, UeTransform x)
        {
            PutQuat(m, i, x.q);
            m[i + 4] = x.t.x;
            m[i + 5] = x.t.y;
            m[i + 6] = x.t.z;
            m[i + 7] = x.s.x;
            m[i + 8] = x.s.y;
            m[i + 9] = x.s.z;
        }

        /// <summary>
        /// The units run again on the editor's last run (the work memory as the package has it), the bones' reads and writes
        /// left out: the worst difference between a register and what the package holds, and where.  A quaternion and its
        /// negative are the same turn.
        /// </summary>
        public (float worst, string where) Replay()
        {
            var m = (float[])initial.Clone();
            foreach (var s in steps)
                if (s.unit != Unit.GetTransform && s.unit != Unit.SetTransform)
                    Exec(s, m, null, 1f);
            float worst = 0f;
            string where = "-";
            for (int i = 0; i < size.Length; i++)
            {
                if (size[i] == 0)
                    continue;
                var kind = program.registers[i].kind;
                float d = 0f;
                for (int j = 0; j < size[i]; j++)
                    d = Mathf.Max(d, Mathf.Abs(m[start[i] + j] - initial[start[i] + j]));
                if (kind == "quat" || kind == "transform")
                {
                    // the same turn either sign
                    float flip = 0f;
                    for (int j = 0; j < 4; j++)
                        flip = Mathf.Max(flip, Mathf.Abs(m[start[i] + j] + initial[start[i] + j]));
                    float rest = 0f;
                    for (int j = 4; j < size[i]; j++)
                        rest = Mathf.Max(rest, Mathf.Abs(m[start[i] + j] - initial[start[i] + j]));
                    d = Mathf.Min(d, Mathf.Max(flip, rest));
                }
                if (kind == "rotator")
                {
                    // angles a whole turn apart are the same
                    d = 0f;
                    for (int j = 0; j < 3; j++)
                        d = Mathf.Max(d, Mathf.Abs(Mathf.DeltaAngle(initial[start[i] + j], m[start[i] + j])));
                }
                if (d > worst)
                {
                    worst = d;
                    where = program.registers[i].name;
                }
            }
            return (worst, where);
        }

        /// <summary>
        /// A fighter's bones in Unreal's terms.  Her bone frames are Unreal's turned half a turn about z ((x, y, z) is
        /// (-x, -y, z), a rotation's x and y negated, as RoeKawaiiPhysics and RoeSbPhysics have them); her component space
        /// is the fighter's own (tools/sb_fbx.py turned her to face Unity's +z: Unreal's x, y, z are our z, x, y); metres
        /// become centimetres.  The bones a rig may not move here (Program.skip) are left alone; what it asked of them is
        /// kept (Skipped).  Rest puts the bones the rig sets back to the bind pose: nothing animates them, and a rig adds
        /// to what it reads.
        /// </summary>
        public class UnityBones : IBones
        {
            static readonly Quaternion ToComponent = new Quaternion(0.5f, 0.5f, 0.5f, 0.5f);      // our axes into Unreal's component axes
            static readonly Quaternion HalfZ = new Quaternion(0f, 0f, 1f, 0f);

            readonly Transform space;
            readonly Transform[] t;
            readonly bool[] skip;
            readonly (Vector3 position, Quaternion rotation)[] rest;
            readonly int[] writes;
            /// <summary>The farthest a skipped bone was asked to move from where it was (cm) and to turn (degrees).</summary>
            public float skippedMove, skippedTurn;
            /// <summary>The most the rig has turned a bone it sets away from its rest (degrees), since the last Reset.</summary>
            public float turned;

            public UnityBones(RoeRigVM vm, Transform space, IDictionary<string, Transform> byName, Func<Transform, (Vector3, Quaternion)> restOf)
            {
                this.space = space;
                t = vm.bones.Select(n => byName.TryGetValue(n, out var x) ? x : null).ToArray();
                var missing = vm.bones.Where((n, i) => t[i] == null).ToList();
                if (missing.Count > 0)
                    throw new ArgumentException($"{vm.program.name}: bones not on her rig: {string.Join(", ", missing)}");
                skip = vm.bones.Select(n => vm.program.skip.Contains(n)).ToArray();
                writes = vm.written.Where(i => !skip[i]).ToArray();
                rest = t.Select(x => restOf(x)).ToArray();
            }

            public IEnumerable<Transform> Moved => writes.Select(i => t[i]);

            public static Quaternion Flip(Quaternion q) => new Quaternion(-q.x, -q.y, q.z, q.w);
            public static Vector3 Flip(Vector3 v) => new Vector3(-v.x, -v.y, v.z);

            public UeTransform Local(int i)
            {
                var b = t[i];
                return new UeTransform(Flip(b.localRotation), Flip(b.localPosition) * 100f, b.localScale);
            }

            public UeTransform InitialLocal(int i) => new UeTransform(Flip(rest[i].rotation), Flip(rest[i].position) * 100f, Vector3.one);

            public UeTransform Global(int i)
            {
                var b = t[i];
                var u = space.InverseTransformPoint(b.position);
                var q = Quaternion.Inverse(space.rotation) * b.rotation;
                return new UeTransform(ToComponent * q * HalfZ, new Vector3(u.z, u.x, u.y) * 100f, Vector3.one);
            }

            public void SetLocal(int i, UeTransform x, bool propagate, float weight)
            {
                var b = t[i];
                var position = Flip(x.t) * 0.01f;
                var rotation = Flip(x.q);
                if (skip[i])
                {
                    skippedMove = Mathf.Max(skippedMove, 100f * Vector3.Distance(position, b.localPosition));
                    skippedTurn = Mathf.Max(skippedTurn, Quaternion.Angle(rotation, b.localRotation));
                    return;
                }
                if (weight <= 0f)
                    return;
                if (weight < 1f)
                {
                    position = Vector3.Lerp(b.localPosition, position, weight);
                    rotation = Quaternion.Slerp(b.localRotation, rotation, weight);
                }
                // a bone set without propagating keeps its children where they were in Unreal only for the rig's later
                // reads (its output is the bones' local transforms): Unity moves them along at once - the same pose
                b.SetLocalPositionAndRotation(position, rotation);
                turned = Mathf.Max(turned, Quaternion.Angle(rotation, rest[i].rotation));
            }

            public void SetGlobal(int i, UeTransform x, bool propagate, float weight)
            {
                // Unreal's component space back into hers
                var q = Quaternion.Inverse(ToComponent) * x.q * Quaternion.Inverse(HalfZ);
                var u = new Vector3(x.t.y, x.t.z, x.t.x) * 0.01f;
                var parent = t[i].parent;
                var local = Quaternion.Inverse(Quaternion.Inverse(space.rotation) * parent.rotation) * q;
                var position = parent.InverseTransformPoint(space.TransformPoint(u));
                SetLocal(i, new UeTransform(Flip(local), Flip(position) * 100f, x.s), propagate, weight);
            }

            public void Rest()
            {
                foreach (int i in writes)
                    t[i].SetLocalPositionAndRotation(rest[i].position, rest[i].rotation);
            }

            /// <summary>
            /// Her bind pose against the rig's own skeleton (Program.reference): the worst global difference and the worst
            /// local one of the bones the rig reads or sets in their parent's space (cm, degrees) - zero when the axes here
            /// are Unreal's (a bone given another parent here, as the humanoid build does the thighs, differs locally only).
            /// Run at the bind pose.
            /// </summary>
            public string Check(RoeRigVM vm)
            {
                float lt = 0f, la = 0f, gt = 0f, ga = 0f;
                string lw = "-", gw = "-";
                foreach (var r in vm.program.reference)
                {
                    int i = Array.IndexOf(vm.bones, r.bone);
                    if (i < 0 || r.local == null || r.local.Length != 10)
                        continue;
                    var want = Get(r.local, 0);
                    var have = InitialLocal(i);
                    float dt = Vector3.Distance(want.t, have.t), da = Quaternion.Angle(want.q, have.q);
                    if (Array.IndexOf(vm.local, i) >= 0 && dt + da > lt + la)
                    {
                        lt = dt;
                        la = da;
                        lw = r.bone;
                    }
                    if (r.global == null || r.global.Length != 10)
                        continue;
                    var wantG = Get(r.global, 0);
                    var haveG = Global(i);
                    dt = Vector3.Distance(wantG.t, haveG.t);
                    da = Quaternion.Angle(wantG.q, haveG.q);
                    if (dt + da > gt + ga)
                    {
                        gt = dt;
                        ga = da;
                        gw = r.bone;
                    }
                }
                return $"bind pose vs the rig's skeleton: local {lt:F2} cm {la:F2} deg ({lw}), global {gt:F2} cm {ga:F2} deg ({gw})";
            }
        }
    }
}
