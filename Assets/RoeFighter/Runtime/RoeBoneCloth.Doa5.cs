using System;
using System.Collections.Generic;
using UnityEngine;

namespace RoeFighter
{
    /// <summary>
    /// Dead or Alive 5 Last Round's cloth on the bone cloth's chains (user 10-04: "doa5lr 也调研一下……看物理系统是否有其他差别，
    /// 最好做成可插拔的设计"; the "doa5lr_style" setup on F4, docs/doa-physics.md section 11).  From the game's data
    /// (ripper_tpose scripts/doa5lr/tmc_physics.py, Kasumi's KASUMI_COS_004 skirt, her strings and hair):
    ///   - a particle per bone, joined by springs of five classes: structural (along a chain), horizontal (to the next chain),
    ///     shear (a cell's diagonals), bend (skip one along) and long range (from the chain's root);
    ///   - each spring: its rest length, the share each end moves (0.5 / 0.5; 0 / 1 from a fixed root) and two stiffness
    ///     words.  On the skirt the second is always 1.5-2 x the first (0.5 / 1, 1 / 1.5, 1 / 2), on strings and hair both
    ///     10 along and 1 skip-one: read as the stiffness when shorter than rest and when longer - cloth gives way to folding,
    ///     not to stretching (the puffed sleeves' 2 / 1 resist being squashed);
    ///   - per object four words (p2-p5) and per particle five (its b: hair roots 1, 1, 1, 0.5, 0 - pinned to the pose).
    /// Here the chains, their links across, the colliders and the floor are the bone cloth's, so what differs from the
    /// Magica-style mode is the constraint system: Magica's angle restoration and angle limits there, this spring net here.
    /// Each substep: Verlet with the swing against the animation damped (word p4 read as the share lost per 1/60 s),
    /// gravity (9.8 x word p3), a pull towards the animated place (word p5 per 1/60 s; a pinned particle all the way); then
    /// iterations of every spring at the stiffness for its sign (a word k as 1 - e^(-k / Doa5K) per iteration; long range
    /// only against stretching), the bone lengths, the bone cloth's angle limit (Doa5AngleLimit), colliders, floor.  The
    /// readings of the words are guesses (the game's code is encrypted); the topology and the stiffness classes are its
    /// data.  No inertia carry: the damping is against the animation's own speed, so the cloth goes along with her and only
    /// its swing about the pose dies away.
    /// </summary>
    public partial class RoeBoneCloth
    {
        /// <summary>One spring of the net: particles i and j (j &lt; 0: the fixed head of i's chain), class, stiffness words.</summary>
        struct Spring5
        {
            public int i, j, cls;              // cls: 1 across, 2 shear, 3 bend along, 4 bend across, 5 long range
            public float kShort, kLong;
        }

        /// <summary>Tuning on top of the words: the stiffness scale (a word k is 1 - e^(-k / Doa5K) per iteration), damping, pull, gravity.</summary>
        public static float Doa5K = 2f, Doa5Damping = 1f, Doa5Pull = 1f, Doa5Gravity = 1f;
        /// <summary>Whether the long-range springs (no further from the chain's root than the pose has it) are on.</summary>
        public static bool Doa5LongRange = true;
        /// <summary>
        /// Whether each bone also stays within the bone cloth's angle limit of its animated direction (limitRoot..limitTip).
        /// On: without it the leg capsules pushed a chain through to the far side of a thigh and it flipped over (g04's
        /// skirt tips up to 167 degrees off the animation, 170 in one step; colliders off: 20-40) - DOA5LR's cloth is not
        /// bone chains against our capsules, and what its particles' words c, d do is not known.
        /// </summary>
        public static bool Doa5AngleLimit = true;

        static readonly string[] Doa5Classes = { "", "across", "shear", "bend", "bend across", "long range" };

        /// <summary>
        /// DOA5LR's numbers for a kind, on the bone cloth preset (radius, colliders, links kept): the skirt from KASUMI_COS_004's
        /// skirt (WGT_acs_skt: 14 chains, 134 particles, 850 springs; the commonest pair of each class), strings from her
        /// cords and sashes, hair from KASUMI_HAIR_001's 17 strands.  DOA5LR does not simulate breasts (presets drive them).
        /// </summary>
        static Settings Doa5(string kind, Settings s)
        {
            switch (kind)
            {
                case "skirt":
                    s.kAcross = new Vector2(1f, 2f);
                    s.kShear = new Vector2(0.5f, 1f);
                    s.kBend = new Vector2(0.5f, 1f);
                    s.kBendAcross = new Vector2(0.5f, 1f);
                    s.kLongRange = new Vector2(0.5f, 1f);
                    s.gravityWord = 0.8f;
                    s.dampingWord = 0.5f;
                    s.pullWord = 0.11f;
                    break;
                case "hair":
                    s.kBend = new Vector2(1f, 1f);
                    s.gravityWord = 0.5f;
                    s.dampingWord = 0.43f;
                    s.pullWord = 0.04f;
                    s.pinRoot = 0.33f;
                    break;
                case "ribbon":
                case "chain":
                    s.kBend = new Vector2(1f, 1f);
                    s.gravityWord = 0.7f;
                    s.dampingWord = 0.15f;
                    s.pullWord = 0.1f;
                    break;
                default:
                    s.kBend = new Vector2(1f, 1f);
                    s.gravityWord = 0.5f;
                    s.dampingWord = 0.4f;
                    s.pullWord = 0.2f;
                    break;
            }
            return s;
        }

        static float Stiff(float k) => k > 0f ? 1f - Mathf.Exp(-k / Mathf.Max(0.01f, Doa5K)) : 0f;

        /// <summary>The spring net of a chain set and its pins, from its chains and their links across.</summary>
        void BuildDoa5(ChainSet c)
        {
            var s = c.s;
            int n = c.bones.Length;
            c.springs5 = new List<Spring5>();
            c.pin = new float[n];
            // the root share pinned (hair: 1, 1, 1, 0.5, 0 over nine particles)
            for (int i = 0; i < n; i++)
                c.pin[i] = s.pinRoot <= 0f ? 0f : c.depth[i] < s.pinRoot - 1e-4f ? 1f : c.depth[i] < s.pinRoot * 1.5f ? 0.5f : 0f;
            var child = new int[n];
            for (int i = 0; i < n; i++)
                child[i] = -1;
            for (int i = 0; i < n; i++)
                if (c.parent[i] >= 0 && child[c.parent[i]] < 0)
                    child[c.parent[i]] = i;
            void Add(int i, int j, int cls, Vector2 k)
            {
                if (k.x > 0f || k.y > 0f)
                    c.springs5.Add(new Spring5 { i = i, j = j, cls = cls, kShort = k.x, kLong = k.y });
            }
            // across, and the cell's diagonals
            var across = new Dictionary<int, List<int>>();
            foreach (var level in c.links)
                foreach (var (i, j) in level)
                {
                    Add(i, j, 1, s.kAcross);
                    if (child[i] >= 0)
                        Add(j, child[i], 2, s.kShear);
                    if (child[j] >= 0)
                        Add(i, child[j], 2, s.kShear);
                    if (!across.TryGetValue(i, out var li))
                        across[i] = li = new List<int>();
                    if (!across.TryGetValue(j, out var lj))
                        across[j] = lj = new List<int>();
                    li.Add(j);
                    lj.Add(i);
                }
            // skip one across: the two neighbours of a particle
            var seen = new HashSet<(int, int)>();
            foreach (var kv in across)
                for (int a = 0; a < kv.Value.Count; a++)
                    for (int b = a + 1; b < kv.Value.Count; b++)
                    {
                        int i = kv.Value[a], j = kv.Value[b];
                        if (i != j && seen.Add(i < j ? (i, j) : (j, i)))
                            Add(i, j, 4, s.kBendAcross);
                    }
            // skip one along (from the fixed head for the root's child), and long range from the root's head
            for (int i = 0; i < n; i++)
            {
                if (child[i] >= 0 && child[child[i]] >= 0)
                    Add(i, child[child[i]], 3, s.kBend);
                if (c.parent[i] < 0 && child[i] >= 0)
                    Add(child[i], -1, 3, s.kBend);
                if (c.levelOf[i] >= 1)
                    Add(i, -1, 5, s.kLongRange);
            }
            var counts = new int[Doa5Classes.Length];
            foreach (var sp in c.springs5)
                counts[sp.cls]++;
            var parts = new List<string>();
            for (int k = 1; k < counts.Length; k++)
                if (counts[k] > 0)
                    parts.Add($"{Doa5Classes[k]} {counts[k]}");
            int pinned = 0;
            foreach (var p in c.pin)
                if (p > 0f)
                    pinned++;
            c.linkReport += $"; springs {string.Join(", ", parts)}{(pinned > 0 ? $"; {pinned} pinned" : "")}";
        }

        /// <summary>One substep of the DOA5LR-style net (see the class): w / wPrev = where in the fight's step it ends / starts.</summary>
        void SimulateDoa5(ChainSet c, float dt, float wPrev, float w, float floor)
        {
            var s = c.s;
            int n = c.bones.Length;
            float k60 = dt * 60f;
            float keep = Mathf.Pow(1f - Mathf.Clamp01(s.dampingWord * Doa5Damping), k60);
            float pull = 1f - Mathf.Pow(1f - Mathf.Clamp01(s.pullWord * Doa5Pull), k60);
            var gravity = Vector3.down * 9.8f * s.gravityWord * Doa5Gravity;
            // the animated pose in between the fight's steps, and its speed
            var hb = new Vector3[n];
            var tb = new Vector3[n];
            var d0 = new Vector3[n];
            var vAnim = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                hb[i] = Vector3.Lerp(c.hbPrev[i], c.hb[i], w);
                tb[i] = Vector3.Lerp(c.tbPrev[i], c.tb[i], w);
                vAnim[i] = (tb[i] - Vector3.Lerp(c.tbPrev[i], c.tb[i], wPrev)) / dt;
                d0[i] = (tb[i] - hb[i]).normalized;
            }
            var cPos = Vector3.Lerp(c.anchorPosPrev, c.anchorPos, w);
            var cRot = Quaternion.Slerp(c.anchorRotPrev, c.anchorRot, w);
            var axis = cRot * c.anchorAxis;

            // Verlet: the swing about the animation damped and limited, gravity, the pull and the pins
            for (int i = 0; i < n; i++)
            {
                var swing = (c.x[i] - c.xOld[i]) / dt - vAnim[i];
                swing *= keep;
                float sp = swing.magnitude;
                if (sp > s.particleLimit)
                    swing *= s.particleLimit / sp;
                var v = vAnim[i] + swing + gravity * dt;
                c.xOld[i] = c.x[i];
                c.x[i] += v * dt;
                c.x[i] += (tb[i] - c.x[i]) * Mathf.Max(pull, c.pin[i]);
            }

            for (int it = 0; it < s.iterations; it++)
            {
                // the springs, each end half the way (the fixed head none), at the stiffness for the spring's sign
                foreach (var sp in c.springs5)
                {
                    bool toHead = sp.j < 0;
                    var a = c.x[sp.i];
                    var b = toHead ? hb[c.root[sp.i]] : c.x[sp.j];
                    var dv = b - a;
                    float len = dv.magnitude;
                    float rest = Vector3.Distance(tb[sp.i], toHead ? hb[c.root[sp.i]] : tb[sp.j]);
                    if (len < 1e-6f || (sp.cls == 5 && (len <= rest || !Doa5LongRange)))
                        continue;
                    float k = Stiff(len < rest ? sp.kShort : sp.kLong);
                    var corr = dv / len * ((len - rest) * k);
                    if (toHead)
                        c.x[sp.i] += corr;
                    else
                    {
                        c.x[sp.i] += corr * 0.5f;
                        c.x[sp.j] -= corr * 0.5f;
                    }
                }
                // root to tip: the bone lengths, the pins, colliders, floor
                foreach (var lv in c.levels)
                    foreach (int i in lv)
                    {
                        int p = c.parent[i];
                        c.head[i] = p < 0 ? hb[i] : c.x[p];
                        if (c.pin[i] >= 1f)
                            c.x[i] = tb[i];
                        c.x[i] = OnSphere(c, i, c.x[i]);
                        if (Doa5AngleLimit)
                        {
                            // the parent's turn away from its animated pose carries this bone's baseline, as in the Magica-style mode
                            c.d[i] = p < 0 ? Quaternion.identity : c.rot[p] * Quaternion.Inverse(c.rb[p]);
                            c.g[i] = (c.d[i] * d0[i]).normalized;
                            var dir = Vector3.RotateTowards(c.g[i], (c.x[i] - c.head[i]).normalized, c.limit[i], 0f).normalized;
                            c.x[i] = c.head[i] + dir * c.length[i];
                            c.rot[i] = Quaternion.FromToRotation(c.g[i], dir) * c.d[i] * c.rb[i];
                        }
                        PushColliders(c, i, hb, tb);
                        if (s.floor)
                            Floor(c, i, floor, cPos, axis);
                    }
                // the links collide too (a leg cannot pass between two linked chains), as in the Magica-style mode
                if (c.capsules.Length > 0 && !NoColliders && !(skirtOnSkin && c.kind == "skirt"))
                    foreach (var level in c.links)
                        foreach (var (i, j) in level)
                            PushLink(c, i, j);
            }

            // each bone's frame: the animated one carried by the parent, swung onto the simulated direction
            foreach (var lv in c.levels)
                foreach (int i in lv)
                {
                    int p = c.parent[i];
                    c.d[i] = p < 0 ? Quaternion.identity : c.rot[p] * Quaternion.Inverse(c.rb[p]);
                    c.head[i] = p < 0 ? hb[i] : c.x[p];
                    c.g[i] = (c.d[i] * d0[i]).normalized;
                    c.rot[i] = Quaternion.FromToRotation(c.g[i], (c.x[i] - c.head[i]).normalized) * c.d[i] * c.rb[i];
                }
            if (c.stabilizeLeft > 0f)
                Array.Copy(c.x, c.xOld, n);
        }
    }
}
