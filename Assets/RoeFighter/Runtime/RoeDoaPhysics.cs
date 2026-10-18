using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RoeFighter
{
    /// <summary>
    /// Dead or Alive 6's own physics for a character imported from that game (RoeClothSolvers "doa6"), from her data
    /// (RoeDoaRig, built by DoaPhysicsBuilder from the game's model files; docs/doa-physics.md):
    ///   - bone chains (NUNO4: ponytail, sash tails, cords): a particle per bone, the first one on its animated place,
    ///     velocity kept by the chain's damping word, gravity times its first word, pulled back towards the animated pose
    ///     by its restore word, its length held over its iteration count (always 10), pushed out of its own collider groups
    ///     with friction;
    ///   - swing bones (.swg: short hair locks): the same on two-bone strands, each bone kept within its angle limits;
    ///   - lattice soft bodies (SOFT: the breasts): a node per lattice point; each has an animated target skinned to body
    ///     bones and pinned nodes (the chest wall) stay on it; the others are damped springs to their targets (stiffness
    ///     word 270 = 2.6 Hz, gravity word -6 m/s2), the lattice's edges and face diagonals hold the shape, the hull holds
    ///     the volume, the arms, legs and hands of its collider group push in.  The breast meshes are skinned to the nodes.
    /// What the game's parameter words mean is partly guessed (ripper_tpose docs/doa-clothing-and-cloth.md); the guesses are
    /// marked below, with a scale to tune each by eye.  Her clips never key these bones, so they are simulated over every
    /// clip, the game-style ones included (Weight is ignored).
    /// </summary>
    public class RoeDoaPhysics : IRoeCloth
    {
        // tuning on top of the game's words (the guessed readings)
        public static float SoftSpring = 1f, SoftDampingRatio = 1f, SoftGravity = 1f, ChainRestore = 1f, ChainGravity = 1f;
        /// <summary>A pose that moves further than this in one step is a teleport: the simulation starts again from it (m).</summary>
        public static float Teleport = 0.5f;
        /// <summary>Whether the soft bodies collide with their limb colliders (for checks).</summary>
        public static bool SoftColliders = true;
        /// <summary>
        /// The soft lattice's rest shape (edge, diagonal lengths, hull volume): true, its targets as the pose has them; false,
        /// the bind pose's.  Her hips' lattice spans the hip joint and its targets bend with the thighs: held to the bind
        /// pose's lengths it pulled 2-5 cm off them standing in the guard, at the 1.5 cm per-axis limit every step.
        /// </summary>
        public static bool SoftRestFromPose = true;
        /// <summary>Grid cloth tuning on top of the game's words: gravity (x 9.8 m/s2), shear and bend stiffness, how far a column may
        /// stretch, the pull back to the rest shape (x word 6).</summary>
        public static float GridGravity = 1f, GridShear = 1f, GridBend = 1f, GridStretch = 1.05f, GridRestore = 1f;
        const float ParticleRadius = 0.01f, NodeRadius = 0.01f;

        readonly RoeDoaRig rig;
        readonly Transform root;
        readonly List<ChainSim> chains = new List<ChainSim>();
        readonly List<SoftSim> softs = new List<SoftSim>();
        readonly List<GridSim> grids = new List<GridSim>();
        // the bones placed from the soft bodies, as they rest: put back before each pose (their soft body's targets hang
        // partly on them - left where the last step put them, the breasts drifted off by up to 19 cm)
        readonly List<(Transform bone, Vector3 position, Quaternion rotation)> attachedRest = new List<(Transform, Vector3, Quaternion)>();
        float weight = 1f;

        string report;
        /// <summary>What it simulates, and since the last Reset how far the soft bodies and chain tips moved off the animated pose.</summary>
        public string Report => report + (softs.Any(s => s.Steps > 0) || chains.Any(c => c.Steps > 0) || grids.Any(g => g.Steps > 0)
            ? "; off the animated pose since the start: " + string.Join(", ", softs.Where(s => s.Steps > 0).Select(s => s.Stats)
                .Concat(chains.Where(c => c.Steps > 0).GroupBy(c => c.kind).Select(g => $"{g.Key} tips max {g.Max(c => c.MaxOff) * 100f:F1} cm"))
                .Concat(grids.Where(g => g.Steps > 0).Select(g => g.Stats)))
            : "");
        public float Weight { get => weight; set => weight = value; }

        public RoeDoaPhysics(RoeClothScope scope)
        {
            rig = scope.animator != null ? scope.animator.GetComponent<RoeDoaRig>() : null;
            root = scope.animator != null ? scope.animator.transform : null;
            if (rig == null)
            {
                report = "DOA6 physics: no data";
                return;
            }
            foreach (var c in rig.chains.Where(c => scope.Takes(c.kind)))
                chains.Add(new ChainSim(c.name, c.kind, c.bones, c.param, Colliders(c.groups), null));
            if (scope.Takes("hair"))
                foreach (var strand in rig.swings.GroupBy(s => s.group))
                {
                    var bones = strand.Select(s => s.bone).OrderBy(Depth).ToArray();
                    var p = strand.First().param;
                    float limit = p.Take(4).Max();
                    // [gravity 1, flags, damping f13, -, -, -, -, iterations 10, restore, friction 0.8]
                    var param = new float[] { 1f, 0f, p.Length > 13 && p[13] > 0f ? p[13] : 0.8f, 0.8f, 0.2f, 0.2f, 0.2f, 10f, 0.35f, 0.8f };
                    chains.Add(new ChainSim($"swing {strand.Key}", "hair", bones, param, new RoeDoaRig.Collider[0], limit > 0f ? limit : 30f));
                }
            foreach (var s in rig.softs.Where(s => scope.Takes(s.kind)))
                softs.Add(new SoftSim(s, Colliders(s.groups), root));
            if (softs.Count > 0)
                QualitySettings.skinWeights = SkinWeights.Unlimited;    // the breast meshes take up to 12 bones per vertex
            foreach (var a in rig.attachments)
                if (a.bone != null)
                    attachedRest.Add((a.bone, a.bone.localPosition, a.bone.localRotation));
            foreach (var g in rig.grids.Where(g => scope.Takes(g.kind)))
                grids.Add(new GridSim(g, Colliders(g.groups)));
            report = $"DOA6: {chains.Count} chains ({string.Join(", ", chains.Select(c => $"{c.name} {c.Count}"))}), " +
                     $"{softs.Count} soft bodies ({string.Join(", ", softs.Select(s => s.Name))}), " +
                     $"{grids.Count} cloth grids ({string.Join(", ", grids.Select(g => g.Name))})";
        }

        static int Depth(Transform t)
        {
            int d = 0;
            for (; t != null; t = t.parent)
                d++;
            return d;
        }

        RoeDoaRig.Collider[] Colliders(int[] groups) =>
            groups.SelectMany(g => g >= 0 && g < rig.groups.Count ? rig.groups[g].colliders : new int[0]).Distinct()
                .Select(i => rig.colliders[i]).Where(c => c != null && c.bone != null).ToArray();

        /// <summary>Whether this character's data has the kind (a chain kind, hair for swing bones, a soft body's kind).</summary>
        public static bool Covers(RoeClothScope scope, string kind)
        {
            var r = scope.animator != null ? scope.animator.GetComponent<RoeDoaRig>() : null;
            return r != null && (r.chains.Any(c => c.kind == kind) || (kind == "hair" && r.swings.Count > 0) || r.softs.Any(s => s.kind == kind)
                                 || r.grids.Any(g => g.kind == kind));
        }

        /// <summary>The loose bones of a DOA6 character by kind (null for the others: their bones are told apart by name).</summary>
        public static Dictionary<Transform, string> KindsOf(Animator animator) => animator != null ? animator.GetComponent<RoeDoaRig>()?.KindsOf() : null;

        public void Rest()
        {
            foreach (var c in chains)
                c.Rest();
            foreach (var s in softs)
                s.Rest();
            foreach (var (bone, position, rotation) in attachedRest)
                bone.SetLocalPositionAndRotation(position, rotation);
            foreach (var g in grids)
                g.Rest();
        }

        public void Step(float dt, float floor)
        {
            if (rig == null)
                return;
            int sub = dt > 0f ? Mathf.Max(1, Mathf.RoundToInt(dt * 120f)) : 0;
            foreach (var c in chains)
                c.Step(dt, sub, floor);
            foreach (var s in softs)
                s.Step(dt, sub);
            // bones that ride on the soft body (the cleavage)
            foreach (var a in rig.attachments)
            {
                var at = a.offset;
                for (int r = 0; r < a.body.Length; r++)
                {
                    var soft = rig.softs[a.body[r]];
                    for (int k = 0; k < 8; k++)
                        at += a.weight[r] * a.nodeWeight[8 * r + k] * soft.nodes[a.nodes[8 * r + k]].position;
                }
                if (a.bone != null && softs.Any(s => a.body.Any(b => rig.softs[b] == s.Data)))
                    a.bone.position = at;
            }
            // grid cloth last: a flap hangs from a bone the soft body just placed
            foreach (var g in grids)
                g.Step(dt, sub, floor);
        }

        public void Reset()
        {
            foreach (var c in chains)
                c.Reset();
            foreach (var s in softs)
                s.Reset();
            foreach (var g in grids)
                g.Reset();
        }

        /// <summary>For checks: per soft body, the nodes furthest off their targets right now.</summary>
        public string Probe(int top) => string.Join("\n", softs.Select(s => s.Probe(top)));

        // ---- shapes

        /// <summary>
        /// Push a point out of a collider (capsule along its local Y, or ellipsoid) - but only as far out as allow
        /// (a depth, m) inside it; true when it was pushed.
        /// </summary>
        static bool PushOut(ref Vector3 p, RoeDoaRig.Collider c, float radius, float allow = 0f)
        {
            var b = c.bone;
            var centre = b.TransformPoint(c.center);
            var rot = b.rotation * c.rotation;
            if (c.type == 6)
            {
                // ellipsoid: scaled to a unit sphere (radii grown by the point's radius)
                var local = Quaternion.Inverse(rot) * (p - centre);
                var r = new Vector3(c.a + radius, c.b + radius, c.c + radius);
                var u = new Vector3(local.x / r.x, local.y / r.y, local.z / r.z);
                float m = u.magnitude, edge = 1f - allow / Mathf.Max(1e-4f, Mathf.Min(r.x, Mathf.Min(r.y, r.z)));
                if (m >= edge || m < 1e-6f)
                    return false;
                u *= edge / m;
                p = centre + rot * new Vector3(u.x * r.x, u.y * r.y, u.z * r.z);
                return true;
            }
            // capsule (type 5; others treated alike): segment +-b along local Y, radius a
            var axis = rot * Vector3.up;
            float t = Mathf.Clamp(Vector3.Dot(p - centre, axis), -c.b, c.b);
            var closest = centre + axis * t;
            var d = p - closest;
            float dist = d.magnitude, min = c.a + radius - allow;
            if (dist >= min)
                return false;
            p = closest + (dist > 1e-6f ? d / dist : rot * Vector3.forward) * min;
            return true;
        }

        /// <summary>How deep a point is inside a collider (radius added; an ellipsoid's by its smallest radius), 0 outside.</summary>
        static float Depth(Vector3 p, RoeDoaRig.Collider c, float radius)
        {
            var b = c.bone;
            var centre = b.TransformPoint(c.center);
            var rot = b.rotation * c.rotation;
            if (c.type == 6)
            {
                var local = Quaternion.Inverse(rot) * (p - centre);
                var r = new Vector3(c.a + radius, c.b + radius, c.c + radius);
                float m = new Vector3(local.x / r.x, local.y / r.y, local.z / r.z).magnitude;
                return m >= 1f ? 0f : (1f - m) * Mathf.Min(r.x, Mathf.Min(r.y, r.z));
            }
            var axis = rot * Vector3.up;
            float t = Mathf.Clamp(Vector3.Dot(p - centre, axis), -c.b, c.b);
            return Mathf.Max(0f, c.a + radius - Vector3.Distance(p, centre + axis * t));
        }

        // ---- bone chains (and swing strands)

        class ChainSim
        {
            public readonly string name, kind;
            readonly Transform[] bones;
            readonly RoeDoaRig.Collider[] colliders;
            readonly int n;
            readonly Vector3[] x, xOld, anim;
            readonly float[] length;
            readonly Quaternion[] restLocal;
            readonly Vector3[] childLocal;      // each bone's local direction to the next point at rest (the last: its own)
            readonly float gravity, keep, restore, friction, limit;
            readonly int iterations;
            bool ready;

            public int Count => n;
            public int Steps;
            public float MaxOff;                // the tip's largest distance from its animated place (m)

            /// <summary>param: the game's words [gravity/mass, flags, damping a, damping b, stiffness a b c, iterations, restore, friction]; limit: swing angle limit (deg) or null.</summary>
            public ChainSim(string name, string kind, Transform[] bones, float[] param, RoeDoaRig.Collider[] colliders, float? limit)
            {
                this.name = name;
                this.kind = kind;
                this.bones = bones;
                this.colliders = colliders;
                n = bones.Length + 1;           // + the tip past the last bone
                x = new Vector3[n];
                xOld = new Vector3[n];
                anim = new Vector3[n];
                length = new float[n];
                restLocal = bones.Select(b => b.localRotation).ToArray();
                childLocal = new Vector3[bones.Length];
                for (int i = 0; i < bones.Length; i++)
                {
                    var next = i + 1 < bones.Length ? bones[i + 1].position
                             : bones[i].position + (i > 0 ? bones[i].position - bones[i - 1].position : bones[i].rotation * Vector3.right * 0.05f);
                    childLocal[i] = bones[i].InverseTransformPoint(next);
                    length[i + 1] = Vector3.Distance(bones[i].position, next);
                }
                float P(int i, float fallback) => param != null && param.Length > i ? param[i] : fallback;
                gravity = 9.8f * (P(0, 1f) > 0f ? P(0, 1f) : 1f);
                keep = Mathf.Clamp01(P(2, 0.9f));                 // damping a: the velocity kept per 1/60 s (guess)
                restore = Mathf.Clamp01(P(8, 0.2f));              // restore to rest: pulled back towards the animated pose per 1/60 s (guess)
                iterations = Mathf.Clamp(Mathf.RoundToInt(P(7, 10f)), 1, 20);
                friction = Mathf.Clamp01(P(9, 0.8f));
                this.limit = limit.HasValue ? limit.Value * Mathf.Deg2Rad : 0f;
            }

            public void Rest()
            {
                for (int i = 0; i < bones.Length; i++)
                    bones[i].localRotation = restLocal[i];
            }

            public void Reset()
            {
                ready = false;
                Steps = 0;
                MaxOff = 0f;
            }

            void ReadAnimated()
            {
                for (int i = 0; i < bones.Length; i++)
                    anim[i] = bones[i].position;
                var last = bones[bones.Length - 1];
                anim[n - 1] = last.TransformPoint(childLocal[bones.Length - 1]);
            }

            public void Step(float dt, int sub, float floor)
            {
                var before = anim[0];
                ReadAnimated();
                if (!ready || sub == 0 || (anim[0] - before).sqrMagnitude > Teleport * Teleport)
                {
                    System.Array.Copy(anim, x, n);
                    System.Array.Copy(anim, xOld, n);
                    ready = true;
                    if (sub == 0)
                        return;
                }
                float h = dt / sub;
                float k60 = h * 60f;
                float kv = Mathf.Pow(keep, k60);
                float kr = 1f - Mathf.Pow(1f - restore * ChainRestore, k60);
                var g = Vector3.down * gravity * ChainGravity * h * h;
                for (int s = 0; s < sub; s++)
                {
                    x[0] = anim[0];
                    xOld[0] = anim[0];
                    for (int i = 1; i < n; i++)
                    {
                        var v = (x[i] - xOld[i]) * kv;
                        xOld[i] = x[i];
                        x[i] += v + g;
                        x[i] += (anim[i] - x[i]) * kr;
                    }
                    for (int it = 0; it < iterations; it++)
                    {
                        for (int i = 1; i < n; i++)
                        {
                            var head = x[i - 1];
                            var dir = x[i] - head;
                            if (limit > 0f)
                            {
                                // a swing bone: within its angle of the animated direction
                                var animDir = anim[i] - anim[i - 1];
                                dir = Vector3.RotateTowards(animDir, dir, limit, 0f);
                            }
                            x[i] = head + (dir.sqrMagnitude > 1e-12f ? dir.normalized : Vector3.down) * length[i];
                            bool hit = false;
                            foreach (var c in colliders)
                                hit |= PushOut(ref x[i], c, ParticleRadius, Depth(anim[i], c, ParticleRadius));
                            if (x[i].y < floor + ParticleRadius)
                            {
                                x[i].y = floor + ParticleRadius;
                                hit = true;
                            }
                            if (hit)
                            {
                                x[i] = head + (x[i] - head).normalized * length[i];
                                xOld[i] += (x[i] - xOld[i]) * friction * 0.25f;
                            }
                        }
                    }
                }
                Steps++;
                MaxOff = Mathf.Max(MaxOff, Vector3.Distance(x[n - 1], anim[n - 1]));
                // the bones turn their next point onto the particles, root to tip
                for (int i = 0; i < bones.Length; i++)
                {
                    var b = bones[i];
                    var cur = b.TransformPoint(childLocal[i]) - b.position;
                    var want = x[i + 1] - b.position;
                    if (cur.sqrMagnitude > 1e-12f && want.sqrMagnitude > 1e-12f)
                        b.rotation = Quaternion.FromToRotation(cur, want) * b.rotation;
                }
            }
        }

        // ---- grid cloth (NUNO3)

        /// <summary>
        /// A grid cloth: the control points of its top rows follow the body (skinned), the others fall, swing and keep the
        /// grid's links, cell diagonals (shear) and every-other links (bend), no further down their column than its rest
        /// length (x GridStretch), out of the limbs of their collider groups (radius word 36) and above the floor.  Words read
        /// (guesses): 5 the share of speed lost per 1/60 s, 6 the share of the way back to the rest shape per 1/60 s, 7 / 8
        /// shear / bend stiffness, 12 friction, 37 iteration bytes.
        /// The visible cloth is rebuilt from the points afterwards (RoeDoaRig.RebuildSurfaces).
        /// </summary>
        class GridSim
        {
            public readonly RoeDoaRig.Grid Data;
            public string Name => $"{Data.cols}x{Data.rows} on {Data.parent.name}";
            readonly RoeDoaRig.Collider[] colliders;
            readonly int n, fixedCount, iterations;
            readonly Vector3[] x, xOld, anim, target, targetPrev;
            readonly Vector3[] restPos;
            readonly Quaternion[] restRot;
            readonly int[] column;          // the column's last skinned point
            readonly float[] tether;        // its rest distance down the column
            readonly bool[] hit;            // touched a collider or the floor this substep
            readonly float keep, shear, bend, friction, restore;
            bool ready;
            public int Steps;
            float maxOff, sumOff;
            int maxAt;
            public string Stats => $"cloth {Name} max {maxOff * 100f:F1} cm from its rest on the body at step {maxAt} " +
                                   $"(mean of each step's largest {sumOff / Mathf.Max(1, Steps) * 100f:F1} cm)";

            float P(int i, float fallback) => Data.param != null && Data.param.Length > i ? Data.param[i] : fallback;

            public GridSim(RoeDoaRig.Grid g, RoeDoaRig.Collider[] colliders)
            {
                Data = g;
                this.colliders = colliders;
                n = g.cps.Length;
                fixedCount = g.skinnedRows * g.cols;
                x = new Vector3[n];
                xOld = new Vector3[n];
                anim = new Vector3[n];
                target = new Vector3[n];
                targetPrev = new Vector3[n];
                restPos = g.cps.Select(t => t.localPosition).ToArray();
                restRot = g.cps.Select(t => t.localRotation).ToArray();
                keep = 1f - Mathf.Clamp(P(5, 0.05f), 0f, 0.9f);
                // word 6: the share of the way back to its rest shape on the parent per 1/60 s, as the chains' word 8 reads
                // (guess: 0.2 on the skirt, 0.7 on the puffed sleeves and the flap that keep their shape; without it the
                // skirt flipped up by her 2.5 m drop into the fight stayed bunched on her hips for 2.5 s)
                restore = Mathf.Clamp01(P(6, 0.2f));
                shear = Mathf.Clamp01(P(7, 0.7f));
                bend = Mathf.Clamp01(P(8, 0.1f));
                friction = Mathf.Clamp01(P(12, 0.8f));
                hit = new bool[n];
                int sum = g.iterations != null ? g.iterations.Sum() : 0;
                iterations = Mathf.Clamp(sum > 0 ? sum : 4, 3, 8);
                column = new int[n];
                tether = new float[n];
                for (int i = 0; i < n; i++)
                {
                    int r = i / g.cols, c = i % g.cols;
                    int top = (g.skinnedRows - 1) * g.cols + c;
                    column[i] = r < g.skinnedRows ? -1 : top;
                    float len = 0f;
                    for (int k = g.skinnedRows; k <= r; k++)
                        len += Vector3.Distance(g.restLocal[(k - 1) * g.cols + c], g.restLocal[k * g.cols + c]);
                    tether[i] = len;
                }
            }

            public void Rest()
            {
                for (int i = 0; i < n; i++)
                    Data.cps[i].SetLocalPositionAndRotation(restPos[i], restRot[i]);
            }

            public void Reset()
            {
                ready = false;
                Steps = 0;
                maxOff = sumOff = 0f;
                maxAt = 0;
            }

            void Targets()
            {
                for (int i = 0; i < n; i++)
                {
                    anim[i] = Data.parent.TransformPoint(Data.restLocal[i]);
                    if (i >= fixedCount)
                        continue;
                    var t = Vector3.zero;
                    float w = 0f;
                    for (int k = Data.targetStart[i]; k < Data.targetStart[i + 1]; k++)
                    {
                        t += Data.targetWeight[k] * Data.targetBone[k].TransformPoint(Data.targetLocal[k]);
                        w += Data.targetWeight[k];
                    }
                    target[i] = w > 0f ? t / w : anim[i];
                }
            }

            public void Step(float dt, int sub, float floor)
            {
                System.Array.Copy(target, targetPrev, fixedCount);
                Targets();
                float jump = 0f;
                for (int i = 0; i < fixedCount; i++)
                    jump = Mathf.Max(jump, (target[i] - targetPrev[i]).sqrMagnitude);
                if (!ready || sub == 0 || jump > Teleport * Teleport)
                {
                    for (int i = 0; i < n; i++)
                        x[i] = xOld[i] = i < fixedCount ? target[i] : anim[i];
                    System.Array.Copy(target, targetPrev, fixedCount);
                    ready = true;
                    if (sub == 0)
                    {
                        Write();
                        return;
                    }
                }
                float h = dt / sub;
                float kv = Mathf.Pow(keep, h * 60f);
                float kr = 1f - Mathf.Pow(1f - Mathf.Clamp01(restore * GridRestore), h * 60f);
                var g = Vector3.down * 9.8f * GridGravity * h * h;
                float sShear = Mathf.Clamp01(shear * GridShear), sBend = Mathf.Clamp01(bend * GridBend);
                for (int s = 0; s < sub; s++)
                {
                    float f = (s + 1f) / sub;
                    for (int i = 0; i < fixedCount; i++)
                        x[i] = xOld[i] = Vector3.LerpUnclamped(targetPrev[i], target[i], f);
                    for (int i = fixedCount; i < n; i++)
                    {
                        var v = (x[i] - xOld[i]) * kv;
                        xOld[i] = x[i];
                        x[i] += v + g;
                        x[i] += (anim[i] - x[i]) * kr;
                        hit[i] = false;
                    }
                    for (int it = 0; it < iterations; it++)
                    {
                        for (int e = 0; e < Data.springRest.Length; e++)
                        {
                            int a = Data.springs[2 * e], b = Data.springs[2 * e + 1];
                            float wa = a < fixedCount ? 0f : 1f, wb = b < fixedCount ? 0f : 1f;
                            if (wa + wb == 0f)
                                continue;
                            float stiff = Data.springClass[e] == 0 ? 1f : Data.springClass[e] == 1 ? sShear : sBend;
                            var d = x[b] - x[a];
                            float len = d.magnitude;
                            if (len < 1e-7f)
                                continue;
                            var corr = d / len * ((len - Data.springRest[e]) * stiff / (wa + wb));
                            x[a] += corr * wa;
                            x[b] -= corr * wb;
                        }
                        for (int i = fixedCount; i < n; i++)
                        {
                            // no further from the top of its column than the cloth reaches there
                            var top = x[column[i]];
                            var d = x[i] - top;
                            float max = tether[i] * GridStretch;
                            if (d.sqrMagnitude > max * max)
                                x[i] = top + d.normalized * max;
                            if (SoftColliders)
                                foreach (var c in colliders)
                                    hit[i] |= PushOut(ref x[i], c, Data.radius, Depth(anim[i], c, Data.radius));
                            if (x[i].y < floor + Data.radius)
                            {
                                x[i].y = floor + Data.radius;
                                hit[i] = true;
                            }
                        }
                    }
                    // friction once a substep where it touched (each iteration it held the skirt on her hips)
                    for (int i = fixedCount; i < n; i++)
                        if (hit[i])
                            xOld[i] += (x[i] - xOld[i]) * friction * 0.25f;
                }
                Steps++;
                float off = 0f;
                for (int i = fixedCount; i < n; i++)
                    off = Mathf.Max(off, Vector3.Distance(x[i], anim[i]));
                if (off > maxOff)
                {
                    maxOff = off;
                    maxAt = Steps;
                }
                sumOff += off;
                Write();
            }

            void Write()
            {
                // row by row: a point's column hangs from it, so the ones above are placed first
                for (int i = 0; i < n; i++)
                    Data.cps[i].position = x[i];
            }
        }

        // ---- lattice soft bodies

        class SoftSim
        {
            public readonly RoeDoaRig.Soft Data;
            public string Name => $"{Data.kind} {Data.nodes.Length} nodes";
            readonly RoeDoaRig.Collider[] colliders;
            readonly Transform root;
            readonly int n;
            readonly Vector3[] x, xOld, target, targetPrev, now, restLocal, grad;
            readonly bool[] pinned;
            readonly float[] share;             // w_self: how much of its own swing a node shows (0.6 at the cleavage)
            bool ready;
            public int Steps;
            float maxOff, sumOff;
            int maxAt, clamped;
            float[] pushMax;
            int[] pushes;
            public string Stats => $"{Data.kind} nodes max {maxOff * 100f:F1} cm at step {maxAt} (mean of each step's largest {sumOff / Mathf.Max(1, Steps) * 100f:F1} cm, " +
                                   $"{clamped} node steps at the per-axis limit; pushed by " +
                                   string.Join(" ", Enumerable.Range(0, colliders.Length).Where(c => pushes[c] > 0).OrderByDescending(c => pushMax[c]).Take(4)
                                       .Select(c => $"{colliders[c].bone.name} x{pushes[c]} max {pushMax[c] * 100f:F1} cm")) + ")";

            public SoftSim(RoeDoaRig.Soft s, RoeDoaRig.Collider[] colliders, Transform root)
            {
                Data = s;
                this.colliders = colliders;
                this.root = root;
                n = s.nodes.Length;
                x = new Vector3[n];
                xOld = new Vector3[n];
                target = new Vector3[n];
                targetPrev = new Vector3[n];
                now = new Vector3[n];
                grad = new Vector3[n];
                restLocal = s.nodes.Select(t => t.localPosition).ToArray();
                // 0x01: the chest wall of a breast; a hip-class body (0x80) has 0x81 on every node, and is no rigid lump
                pinned = s.flags.Select(f => (f & 1) != 0 && (f & 0x80) == 0).ToArray();
                share = s.wSelf.Select(w => Mathf.Clamp01(w)).ToArray();
                pushMax = new float[colliders.Length];
                pushes = new int[colliders.Length];
            }

            public void Rest()
            {
                for (int i = 0; i < n; i++)
                    Data.nodes[i].localPosition = restLocal[i];
            }

            public void Reset()
            {
                ready = false;
                Steps = 0;
                maxOff = sumOff = 0f;
                clamped = 0;
                System.Array.Clear(pushMax, 0, pushMax.Length);
                System.Array.Clear(pushes, 0, pushes.Length);
            }

            void Targets()
            {
                for (int i = 0; i < n; i++)
                {
                    var t = Vector3.zero;
                    float w = 0f;
                    for (int k = Data.targetStart[i]; k < Data.targetStart[i + 1]; k++)
                    {
                        t += Data.targetWeight[k] * Data.targetBone[k].TransformPoint(Data.targetLocal[k]);
                        w += Data.targetWeight[k];
                    }
                    target[i] = w > 0f ? t / w : Data.nodes[i].position;
                }
            }

            public void Step(float dt, int sub)
            {
                System.Array.Copy(target, targetPrev, n);
                Targets();
                // a teleport (a new round placed her elsewhere): start again from the pose
                float jump = 0f;
                for (int i = 0; i < n; i++)
                    jump = Mathf.Max(jump, (target[i] - targetPrev[i]).sqrMagnitude);
                if (!ready || sub == 0 || jump > Teleport * Teleport)
                {
                    System.Array.Copy(target, x, n);
                    System.Array.Copy(target, xOld, n);
                    System.Array.Copy(target, targetPrev, n);
                    ready = true;
                    if (sub == 0)
                    {
                        Write();
                        return;
                    }
                }
                float h = dt / sub;
                // the target spring: stiffness word as k (1/s2, 270 = 2.6 Hz); damping word as the share of the swing's speed
                // kept per 1/60 s, as the chains' damping words read (0.75: damping ratio 0.53; both guesses - read as
                // 1 - damping ratio, 0.25, the breasts flew 10 cm up out of the costume in a side kick)
                float k = Mathf.Max(1f, Data.stiffness) * SoftSpring;
                float c = -Mathf.Log(Mathf.Clamp(Data.damping, 0.01f, 0.999f)) * 60f * SoftDampingRatio;
                // gravity: the modelled shape already hangs under it as she stands, so only the change pulls - none upright,
                // towards her face when she bends forward, sideways on her side (all of it pulled the breasts 2-4 cm down
                // out of shape while she stood)
                var g = Vector3.up * Data.gravity * SoftGravity;
                if (Data.restDown.sqrMagnitude > 0.5f && Data.parent != null)
                    g = (Vector3.down - Data.parent.rotation * Data.restDown) * (-Data.gravity * SoftGravity);
                float edgeStiff = Data.coef != null && Data.coef.Length > 0 ? Data.coef[0] : 0.9f;
                float diagStiff = Data.coef != null && Data.coef.Length > 1 ? Data.coef[1] : 0.5f;
                float volStiff = Data.coef != null && Data.coef.Length > 2 ? Data.coef[2] : 0.4f;
                var axes = root != null ? root.rotation : Quaternion.identity;
                bool fromPose = SoftRestFromPose;
                for (int s = 0; s < sub; s++)
                {
                    // the targets in between the fight's steps: on a straight line from the last ones
                    float f = (s + 1f) / sub;
                    for (int i = 0; i < n; i++)
                        now[i] = Vector3.LerpUnclamped(targetPrev[i], target[i], f);
                    // the lattice's rest shape: the targets as the pose has them (their skinning bends it over a joint -
                    // her hips' lattice spans the hip joint), or the bind pose's
                    float restVolume = !fromPose ? Data.restVolume : Data.hull != null ? Mathf.Abs(Volume(now)) : 0f;
                    for (int i = 0; i < n; i++)
                    {
                        if (pinned[i])
                        {
                            x[i] = now[i];
                            xOld[i] = now[i];
                            continue;
                        }
                        // damped against the target's own velocity: the breast goes along with the body and only
                        // its swing about the target dies away (damping the world velocity dragged the breasts
                        // 4-8 cm behind her as she walked)
                        var vTarget = (target[i] - targetPrev[i]) / dt;
                        var v = (x[i] - xOld[i]) / h;
                        var a = (now[i] - x[i]) * k - (v - vTarget) * c + g;
                        v += a * h;
                        xOld[i] = x[i];
                        x[i] += v * h;
                    }
                    for (int it = 0; it < 2; it++)
                    {
                        // lattice edges and face diagonals keep their length
                        for (int e = 0; e < Data.springRest.Length; e++)
                        {
                            int i = Data.springs[2 * e], j = Data.springs[2 * e + 1];
                            float stiff = e < Data.edgeCount ? edgeStiff : diagStiff;
                            var d = x[j] - x[i];
                            float len = d.magnitude;
                            if (len < 1e-7f)
                                continue;
                            float wi = pinned[i] ? 0f : 1f, wj = pinned[j] ? 0f : 1f;
                            if (wi + wj == 0f)
                                continue;
                            float rest = fromPose ? Vector3.Distance(now[i], now[j]) : Data.springRest[e];
                            var corr = d / len * ((len - rest) * stiff / (wi + wj));
                            x[i] += corr * wi;
                            x[j] -= corr * wj;
                        }
                        // the hull keeps its volume
                        if (Data.hull != null && Data.hull.Length >= 3 && restVolume > 0f)
                        {
                            System.Array.Clear(grad, 0, n);
                            float vol = 0f;
                            for (int t = 0; t + 2 < Data.hull.Length; t += 3)
                            {
                                int p0 = Data.hull[t], p1 = Data.hull[t + 1], p2 = Data.hull[t + 2];
                                vol += Vector3.Dot(x[p0], Vector3.Cross(x[p1], x[p2])) / 6f;
                                grad[p0] += Vector3.Cross(x[p1], x[p2]) / 6f;
                                grad[p1] += Vector3.Cross(x[p2], x[p0]) / 6f;
                                grad[p2] += Vector3.Cross(x[p0], x[p1]) / 6f;
                            }
                            float sign = Mathf.Sign(vol);
                            float cErr = Mathf.Abs(vol) - restVolume;
                            float sum = 0f;
                            for (int i = 0; i < n; i++)
                                if (!pinned[i])
                                    sum += grad[i].sqrMagnitude;
                            if (sum > 1e-12f)
                            {
                                float lambda = -cErr / sum * volStiff;
                                for (int i = 0; i < n; i++)
                                    if (!pinned[i])
                                        x[i] += grad[i] * (lambda * sign);
                            }
                        }
                    }
                    // the arms, legs and hands of its group push in; no node strays further than the per-axis words allow
                    for (int i = 0; i < n; i++)
                    {
                        if (pinned[i])
                            continue;
                        // as deep as the animated pose itself has the node in it is allowed: the lattice stands up to a
                        // cell (3 cm) out of the skin, and in the guard her upper arms are in its sides (pushed out
                        // all the way the breasts stood squeezed into points, the costume split under them)
                        for (int ci = 0; ci < (SoftColliders ? colliders.Length : 0); ci++)
                        {
                            var before = x[i];
                            if (PushOut(ref x[i], colliders[ci], NodeRadius, Depth(now[i], colliders[ci], NodeRadius)))
                            {
                                pushes[ci]++;
                                pushMax[ci] = Mathf.Max(pushMax[ci], Vector3.Distance(before, x[i]));
                            }
                        }
                        if (Data.perAxis.sqrMagnitude > 0f)
                        {
                            var d = Quaternion.Inverse(axes) * (x[i] - now[i]);
                            var cd = new Vector3(Mathf.Clamp(d.x, -Data.perAxis.x, Data.perAxis.x), Mathf.Clamp(d.y, -Data.perAxis.y, Data.perAxis.y),
                                                 Mathf.Clamp(d.z, -Data.perAxis.z, Data.perAxis.z));
                            if (cd != d)
                                clamped++;
                            x[i] = now[i] + axes * cd;
                        }
                    }
                }
                float off = 0f;
                for (int i = 0; i < n; i++)
                    if (!pinned[i])
                        off = Mathf.Max(off, Vector3.Distance(x[i], target[i]));
                Steps++;
                if (off > maxOff)
                {
                    maxOff = off;
                    maxAt = Steps;
                }
                sumOff += off;
                Write();
            }

            public string Probe(int top)
            {
                var axes = root != null ? root.rotation : Quaternion.identity;
                // all the free nodes: how far off on average, and which way on average (her frame: x right, y up, z ahead)
                var mean = Vector3.zero;
                float meanLength = 0f;
                int free = 0;
                for (int i = 0; i < n; i++)
                    if (!pinned[i])
                    {
                        var off = Quaternion.Inverse(axes) * (x[i] - target[i]);
                        mean += off;
                        meanLength += off.magnitude;
                        free++;
                    }
                mean /= Mathf.Max(1, free);
                meanLength /= Mathf.Max(1, free);
                var sb = new System.Text.StringBuilder($"{Data.name}: free nodes {meanLength * 100f:F1} cm off on average, " +
                                                       $"mean offset ({mean.x * 100f:F1}, {mean.y * 100f:F1}, {mean.z * 100f:F1}) cm");
                foreach (int i in Enumerable.Range(0, n).OrderByDescending(i => (x[i] - target[i]).sqrMagnitude).Take(top))
                {
                    var d = Quaternion.Inverse(axes) * (x[i] - target[i]) * 100f;
                    var bones = Enumerable.Range(Data.targetStart[i], Data.targetStart[i + 1] - Data.targetStart[i])
                        .Select(k => $"{Data.targetBone[k].name} {Data.targetWeight[k]:F2}");
                    var inside = colliders.Select(c => (c.bone.name, depth: Depth(target[i], c, NodeRadius))).Where(c => c.depth > 0f)
                        .Select(c => $"{c.name} by {c.depth * 100f:F1} cm");
                    // how far its springs are stretched (rest = 1; the rest the solver uses)
                    float worst = 1f;
                    for (int e = 0; e < Data.springRest.Length; e++)
                        if (Data.springs[2 * e] == i || Data.springs[2 * e + 1] == i)
                        {
                            int a = Data.springs[2 * e], b = Data.springs[2 * e + 1];
                            float rest = SoftRestFromPose ? Vector3.Distance(target[a], target[b]) : Data.springRest[e];
                            float r = Vector3.Distance(x[a], x[b]) / Mathf.Max(1e-6f, rest);
                            if (Mathf.Abs(r - 1f) > Mathf.Abs(worst - 1f))
                                worst = r;
                        }
                    sb.Append($"\n    node {i} flags 0x{Data.flags[i]:X2} wSelf {Data.wSelf[i]:F2}: off ({d.x:F1}, {d.y:F1}, {d.z:F1}) cm, springs at {worst:F2} of rest, " +
                              $"target on {string.Join(" ", bones)}{(inside.Any() ? ", target inside " + string.Join(" ", inside) : "")}");
                }
                return sb.ToString();
            }

            float Volume(Vector3[] p)
            {
                float v = 0f;
                for (int t = 0; t + 2 < Data.hull.Length; t += 3)
                    v += Vector3.Dot(p[Data.hull[t]], Vector3.Cross(p[Data.hull[t + 1]], p[Data.hull[t + 2]])) / 6f;
                return v;
            }

            /// <summary>The nodes where the mesh takes them: each shows its share (w_self) of its swing off the target - all
            /// of it but at the cleavage, where the game's data has 0.6-0.99 (guess; read as stiffness, it changed little).</summary>
            void Write()
            {
                for (int i = 0; i < n; i++)
                    Data.nodes[i].position = target[i] + (x[i] - target[i]) * share[i];
            }
        }
    }
}
