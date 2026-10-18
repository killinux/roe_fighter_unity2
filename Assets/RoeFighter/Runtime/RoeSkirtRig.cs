using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RoeFighter
{
    /// <summary>
    /// The skirt's animation pose under motion capture: the skirt follows the legs.
    ///
    /// The game keys its skirts by hand in every clip, following the legs; motion capture has no skirt,
    /// so the battle stance's skirt stayed pinned to the pelvis and every step put a thigh through it.
    /// Magica Cloth 2 asks for an animation pose that follows the legs (an artist's skinning or keys, or
    /// its Custom Skinning) with the simulation laid over it (docs/magica-cloth-2.md); this is that.
    ///
    /// Each skirt bone's tail is skinned to six drivers - the hips' heading (hanging), the pelvis, both
    /// thighs and both calves - with weights RoeHelperFit.FitSkirt fitted to the game's own skirt keys
    /// (linear blend skinning of the tail point, weights on the simplex).  At run time the rest of both
    /// the drivers and the skirt is the game's battle stance, as the fit has it: each driver carries the
    /// bones weighted to it from where it is in the stance to where it is now (RestOnStance).  Each bone is
    /// turned (swing only, its roll stays) to point from its skinned head to its skinned tail; the bone
    /// cloth swings from there.
    ///
    /// The first version (10-02, RestOnStance false) put the drivers' rest in the motion-capture guard
    /// instead, so that in the guard the skirt hung exactly as in the stance against the hips' heading.
    /// But the stance's pelvis is tilted (the right hip ~23 degrees up; 25-31 degrees off the guard's
    /// pelvis in all, RoeFightProbe.SkirtRests) while the guard's is about level, and a panel the game
    /// keys on the pelvis has to tilt with it: g04's back panel, which lies on her right buttock in the
    /// game, stood 3.4 cm off it in the guard (RoeFightProbe.ButtGap; 0.6 with the stance rest), and
    /// a08's skirt sat 3.5-3.7 cm into her legs in the guard and the steps (0.3-0.6 with the stance
    /// rest, RoeFightProbe.SkirtDepth, Bandai moves).
    /// </summary>
    public class RoeSkirtRig : MonoBehaviour
    {
        public const int Drivers = 6;
        public static readonly string[] DriverNames = { "heading", "pelvis", "L thigh", "R thigh", "L calf", "R calf" };

        [Serializable]
        public class Joint
        {
            public Transform bone;
            public int parent = -1;              // the joint of the bone's parent, if that is a skirt bone too
            public Vector3 tailLocal;            // the point the weights place, in the bone's frame
            public float[] weights = new float[Drivers];
            public float fitError, rigidError;   // degrees (RMS of the bone direction) on clips the fit did not see
        }

        public List<Joint> joints = new List<Joint>();          // parents before children
        public Transform hips, leftThigh, rightThigh, leftCalf, rightCalf;
        [TextArea] public string fitReport;

        /// <summary>
        /// The drivers' rest: the game's battle stance (true to the fit: a bone the game keys on the pelvis
        /// keeps its place on the pelvis - g04's back panel lies on the buttocks) or the motion-capture guard
        /// (the guard shows the stance's skirt as it hangs against the hips' heading, whatever the pelvis).
        /// </summary>
        public static bool RestOnStance = true;

        /// <summary>
        /// How much the skirt hangs straight down instead of keeping the fitted pose (0..1): with 1 every
        /// bone points down and is turned out of the body only as far as the skin (RoeBodySurface) is in the
        /// way, so a panel rests on the buttocks and the thighs and falls straight from there.  The fitted
        /// pose is the game stance's skirt carried by the legs - in the stance it hangs over her own body,
        /// which leans and sticks the hips out, so on the upright motion-capture body it stood off.
        /// </summary>
        public static float Drape = 1f;
        /// <summary>
        /// How far off the skin the cloth of a hanging panel stays (m): its bone's line keeps this plus how
        /// thick the cloth round it is (a fur hem several centimetres thick keeps its bone that much further).
        /// </summary>
        public static float DrapeClearance = 0.012f;
        /// <summary>
        /// How much a piece of cloth lying on the body turns about its bone to lie flat on it (0..1): a flat
        /// panel turned off the skin's tangent sinks one edge into a round buttock; turned flat on it, both
        /// edges clear.  Only near the skin (5 cm, fully from 1.5 cm) and for pieces wider than thick.
        /// </summary>
        public static float DrapeTwist = 1f;
        /// <summary>
        /// Keep a sample of each piece's own vertices out of the skin too (m off it; negative: off): a wide
        /// fur hem wraps round a heel from the side, where its bone's line is far from the leg.  Only on the
        /// lower part of a chain (from DrapeMeshFrom of its length; higher up, pushing a whole panel off the
        /// buttock for one corner stood it off again) and never lifting a bone past DrapeMeshLift degrees.
        /// </summary>
        public static float DrapeMeshClearance = 0.003f;
        public static float DrapeMeshFrom = 0.45f, DrapeMeshLift = 45f;
        /// <summary>
        /// Which skin a chain rests on: only points whose normal faces the chain's side of the body (dot with
        /// the level direction from the hips to the chain's root at least this) - a front panel between the
        /// thighs is pushed forward by them, not into the gap between them.
        /// </summary>
        public static float DrapeFacing = 0.2f;
        /// <summary>
        /// How fast a bone falls back to hanging (s, time constant): each step starts from where it hung the
        /// step before, so a leg sweeping past pushes the skirt aside instead of the skirt jumping from one
        /// side of the leg to the other.
        /// </summary>
        public static float DrapeSettle = 0.05f;
        /// <summary>
        /// How much the top bones of each chain drape, by their place in it (the rest drape fully): the hip end of a panel
        /// keeps the fitted pose - the game's stance carried by the hips and legs - as the game's own stance has it (user
        /// 10-11: "luf胯部那一段也改成和游戏一样吧": draped, g04's left panel stood out flat from the hip in her crouched guard,
        /// 94 degrees from straight down against the game stance's 51, and her front panels hung straight down at the waist,
        /// 11 against 46): the first two bones (RoeFightProbe.SkirtHang, top two bones against the game's stance - left panel
        /// 58 / 40 against 51 / 25, front panel 59 / 35 against 47 / 17; with the second bone draped halfway the push off the
        /// skin stood it up to 66).  From the third bone down the panel still hangs: the fitted pose there rides on the thighs
        /// (a kick lifted the front panel with the leg).
        /// </summary>
        public static float[] DrapeByLevel = { 0f, 0f };

        RoeBodySurface body;
        int[] chainRoot;
        Vector3[] through;       // per joint: its piece of cloth's normal, in the bone's rotation frame (zero: not flat)
        float[] thick;           // per joint: how far its cloth reaches off the bone's line, through the piece (m)
        Vector3[][] samples;     // per joint: a spread of its cloth's vertices, in the bone's rotation frame from its head
        float[][] sampleLever;   // per sample: how far along the bone, as a share of its length (0.5 .. 1.5)
        float[] chainDepth;      // per joint: how far down its chain it starts (0 = the chain's first bone)
        int[] chainLevel;        // per joint: its place in its chain (0 = the first bone)
        Vector3[] hung;          // per joint: the direction it hung in at the last step (world)
        bool hungValid;

        /// <summary>The skin the skirt rests on (null until the stance is captured).</summary>
        public RoeBodySurface Body => body;

        // the rests, measured on this body by FighterRig.Init
        Vector3[] stanceTail, stanceHead;                        // in the heading frame
        Vector3[] stancePos = new Vector3[Drivers];
        Quaternion[] stanceRot = new Quaternion[Drivers];
        Vector3[] guardPos;
        Vector4[] guardRotSum;
        Quaternion[] guardRot;
        int guardSamples;
        Vector3[] predicted, aimedFrom;
        Matrix4x4[] carry = new Matrix4x4[Drivers];

        public bool Ready => stanceTail != null && guardRot != null && joints.Count > 0;

        /// <summary>Whether the drape may run on this fighter (FighterRig: off under a solver that hangs the skirt itself).</summary>
        [NonSerialized] public bool drapeAllowed = true;
        /// <summary>This fighter's share of the drape (the cloth setup's: RoeClothBackends.Backend.DrapeFor - the "swing" skirt hangs less).</summary>
        [NonSerialized] public float drapeScale = 1f;

        Transform DriverBone(int k) => k switch { 1 => hips, 2 => leftThigh, 3 => rightThigh, 4 => leftCalf, 5 => rightCalf, _ => null };

        /// <summary>In the battle stance: where each tail is against the hips' heading.</summary>
        public void CaptureStance(Vector3 headingPos, Quaternion headingRot)
        {
            stanceTail = new Vector3[joints.Count];
            stanceHead = new Vector3[joints.Count];
            var inv = Quaternion.Inverse(headingRot);
            for (int k = 1; k < Drivers; k++)
            {
                var b = DriverBone(k);
                stancePos[k] = b != null ? inv * (b.position - headingPos) : Vector3.zero;
                stanceRot[k] = b != null ? inv * b.rotation : Quaternion.identity;
            }
            for (int j = 0; j < joints.Count; j++)
                if (joints[j].bone != null)
                {
                    stanceTail[j] = inv * (joints[j].bone.TransformPoint(joints[j].tailLocal) - headingPos);
                    stanceHead[j] = inv * (joints[j].bone.position - headingPos);
                }
            chainRoot = new int[joints.Count];
            var level = new int[joints.Count];
            for (int j = 0; j < joints.Count; j++)
            {
                int r = j;
                while (joints[r].parent >= 0 && joints[r].parent != r)
                {
                    r = joints[r].parent;
                    level[j]++;
                }
                chainRoot[j] = r;
            }
            chainLevel = level;
            chainDepth = new float[joints.Count];
            for (int j = 0; j < joints.Count; j++)
            {
                int bones = 1 + Enumerable.Range(0, joints.Count).Where(k => chainRoot[k] == chainRoot[j]).Select(k => level[k]).Max();
                chainDepth[j] = (float)level[j] / bones;
            }
            var animator = GetComponent<Animator>();
            body = animator != null ? new RoeBodySurface(animator) : null;
            // each bone's piece of cloth: its widest spread across the bone (the main axis of its vertices
            // round the bone's line) and the normal through it; zero where the piece is not much wider than
            // thick (strings, cords)
            through = new Vector3[joints.Count];
            thick = new float[joints.Count];
            samples = new Vector3[joints.Count][];
            sampleLever = new float[joints.Count][];
            var verts = animator != null ? RoeBodySurface.BoneVertices(animator, new HashSet<Transform>(joints.Where(jt => jt.bone != null).Select(jt => jt.bone)))
                                         : new Dictionary<Transform, List<Vector3>>();
            for (int j = 0; j < joints.Count; j++)
            {
                var jt = joints[j];
                if (jt.bone == null || !verts.TryGetValue(jt.bone, out var list) || list.Count < 6)
                    continue;
                var unturn = Quaternion.Inverse(jt.bone.rotation);
                var axis = unturn * (jt.bone.TransformPoint(jt.tailLocal) - jt.bone.position);
                if (axis.sqrMagnitude < 1e-8f)
                    continue;
                axis.Normalize();
                var u = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
                var v = Vector3.Cross(axis, u);
                float cuu = 0f, cvv = 0f, cuv = 0f, mu = 0f, mv = 0f;
                foreach (var w in list)
                {
                    var o = unturn * (w - jt.bone.position);
                    mu += Vector3.Dot(o, u);
                    mv += Vector3.Dot(o, v);
                }
                mu /= list.Count;
                mv /= list.Count;
                foreach (var w in list)
                {
                    var o = unturn * (w - jt.bone.position);
                    float a = Vector3.Dot(o, u) - mu, b = Vector3.Dot(o, v) - mv;
                    cuu += a * a;
                    cvv += b * b;
                    cuv += a * b;
                }
                float phi = 0.5f * Mathf.Atan2(2f * cuv, cuu - cvv);
                var across = Mathf.Cos(phi) * u + Mathf.Sin(phi) * v;
                float tr = cuu + cvv, det = cuu * cvv - cuv * cuv;
                float big = 0.5f * tr + Mathf.Sqrt(Mathf.Max(0f, 0.25f * tr * tr - det)), small = tr - big;
                if (big > 16f * Mathf.Max(small, 1e-12f))         // four times as wide as thick
                    through[j] = Vector3.Cross(axis, across).normalized;
                // how thick: through a flat piece, or all round a cord - the 98th percentile (a fur hem is a few
                // of a piece's vertices), at most 4 cm
                var normalAxis = through[j];
                var reach = list.Select(w =>
                {
                    var o = unturn * (w - jt.bone.position);
                    var radial = o - Vector3.Dot(o, axis) * axis;
                    return normalAxis != Vector3.zero ? Mathf.Abs(Vector3.Dot(radial, normalAxis)) : radial.magnitude;
                }).OrderBy(d => d).ToList();
                thick[j] = Mathf.Min(0.04f, reach[Mathf.Clamp((int)(reach.Count * 0.98f), 0, reach.Count - 1)]);
                // a spread of the piece's vertices (farthest-point picks, 24 at most)
                var all = list.Select(w => unturn * (w - jt.bone.position)).ToList();
                var pick = new List<Vector3> { all.OrderByDescending(o => (o - Vector3.Dot(o, axis) * axis).sqrMagnitude).First() };
                var nearest = all.Select(o => (o - pick[0]).sqrMagnitude).ToArray();
                while (pick.Count < Mathf.Min(24, all.Count))
                {
                    int far = 0;
                    for (int k = 1; k < all.Count; k++)
                        if (nearest[k] > nearest[far])
                            far = k;
                    if (nearest[far] < 1e-6f)
                        break;
                    pick.Add(all[far]);
                    for (int k = 0; k < all.Count; k++)
                        nearest[k] = Mathf.Min(nearest[k], (all[k] - all[far]).sqrMagnitude);
                }
                float len = (jt.bone.TransformPoint(jt.tailLocal) - jt.bone.position).magnitude;
                samples[j] = pick.ToArray();
                sampleLever[j] = pick.Select(o => Mathf.Clamp(Vector3.Dot(o, axis) / Mathf.Max(len, 1e-4f), 0.5f, 1.5f)).ToArray();
            }
        }

        public void BeginGuard()
        {
            guardPos = new Vector3[Drivers];
            guardRotSum = new Vector4[Drivers];
            guardRot = null;
            guardSamples = 0;
        }

        /// <summary>One pose of the guard: each driver against the hips' heading.</summary>
        public void AddGuard(Vector3 headingPos, Quaternion headingRot)
        {
            var inv = Quaternion.Inverse(headingRot);
            for (int k = 1; k < Drivers; k++)
            {
                var b = DriverBone(k);
                if (b == null)
                    continue;
                guardPos[k] += inv * (b.position - headingPos);
                var q = inv * b.rotation;
                var first = guardRotSum[k];
                float s = first.sqrMagnitude > 0f && Vector4.Dot(first, new Vector4(q.x, q.y, q.z, q.w)) < 0f ? -1f : 1f;
                guardRotSum[k] += new Vector4(q.x, q.y, q.z, q.w) * s;
            }
            guardSamples++;
        }

        public void EndGuard()
        {
            if (guardSamples == 0)
                return;
            guardRot = new Quaternion[Drivers];
            for (int k = 0; k < Drivers; k++)
            {
                guardPos[k] /= guardSamples;
                var v = guardRotSum[k].sqrMagnitude > 1e-12f ? guardRotSum[k].normalized : new Vector4(0f, 0f, 0f, 1f);
                guardRot[k] = new Quaternion(v.x, v.y, v.z, v.w);
            }
        }

        /// <summary>
        /// Where the weights put each tail, and where each bone's head is aimed from, with the drivers' rest
        /// in the stance or in the guard.
        /// </summary>
        public void Predict(Vector3 headingPos, Quaternion headingRot, bool onStance, Vector3[] tails, Vector3[] heads)
        {
            // each driver's move from its rest, as a matrix from the stance's heading frame to the world
            carry[0] = Matrix4x4.TRS(headingPos, headingRot, Vector3.one);
            for (int k = 1; k < Drivers; k++)
            {
                var b = DriverBone(k);
                carry[k] = b == null ? carry[0]
                    : Matrix4x4.TRS(b.position, b.rotation, Vector3.one) *
                      (onStance ? Matrix4x4.TRS(stancePos[k], stanceRot[k], Vector3.one) : Matrix4x4.TRS(guardPos[k], guardRot[k], Vector3.one)).inverse;
            }
            for (int j = 0; j < joints.Count; j++)
            {
                var p = Vector3.zero;
                var w = joints[j].weights;
                for (int k = 0; k < Drivers; k++)
                    if (w[k] > 0f)
                        p += w[k] * carry[k].MultiplyPoint3x4(stanceTail[j]);
                tails[j] = p;
            }
            // a chain's first bone: from where the pelvis carries its stance head (the head itself sits where
            // the pelvis puts it, which is elsewhere when the pelvis tilts unlike the stance's)
            for (int j = 0; j < joints.Count; j++)
                heads[j] = joints[j].parent >= 0 ? tails[joints[j].parent] : carry[1].MultiplyPoint3x4(stanceHead[j]);
        }

        /// <summary>For the probes: per bone whose name starts so, its cloth thickness and its tail's and middle's distance off the skin (cm).</summary>
        public string HangReport(string prefix)
        {
            if (body == null || thick == null)
                return "no drape";
            body.Pose();
            var parts = new List<string>();
            for (int j = 0; j < joints.Count; j++)
            {
                var b = joints[j].bone;
                if (b == null || !b.name.StartsWith(prefix))
                    continue;
                var tail = b.TransformPoint(joints[j].tailLocal);
                var outward = joints[chainRoot[j]].bone.position - hips.position;
                outward.y = 0f;
                outward = outward.normalized;
                string D(Vector3 x) => body.Signed(x, outward, DrapeFacing, out float s, out _) ? (s * 100f).ToString("F1") : "-";
                string A(Vector3 x) => body.Signed(x, Vector3.zero, -2f, out float s, out _) ? (s * 100f).ToString("F1") : "-";
                parts.Add($"{b.name} thick {thick[j] * 100f:F1} tail {D(tail)}/{A(tail)} mid {D((tail + b.position) * 0.5f)}/{A((tail + b.position) * 0.5f)}");
            }
            return string.Join("; ", parts);
        }

        /// <summary>The drivers' rest, stance against guard: degrees and cm per driver (for the probes).</summary>
        public string RestReport()
        {
            if (!Ready)
                return "not ready";
            var parts = new List<string>();
            for (int k = 1; k < Drivers; k++)
                parts.Add($"{DriverNames[k]} {Quaternion.Angle(stanceRot[k], guardRot[k]):F0} deg {(stancePos[k] - guardPos[k]).magnitude * 100f:F1} cm");
            return string.Join(", ", parts);
        }

        /// <summary>A step without Apply (the game's own clips): the hanging starts over from the animation.</summary>
        public void Idle() => hungValid = false;

        /// <summary>Turn the skirt bones to their skinned pose; weight 0 leaves them as they are.</summary>
        public void Apply(Vector3 headingPos, Quaternion headingRot, float weight, float dt = 1f / 60f)
        {
            if (!Ready || weight <= 0f)
                return;
            if (predicted == null || predicted.Length != joints.Count)
            {
                predicted = new Vector3[joints.Count];
                aimedFrom = new Vector3[joints.Count];
            }
            Predict(headingPos, headingRot, RestOnStance, predicted, aimedFrom);
            bool drape = drapeAllowed && Drape * drapeScale > 0f && body != null && body.Count > 0 && chainRoot != null;
            if (drape)
            {
                body.Pose();
                if (hung == null || hung.Length != joints.Count)
                {
                    hung = new Vector3[joints.Count];
                    hungValid = false;
                }
            }
            float settle = dt > 0f ? 1f - Mathf.Exp(-dt / Mathf.Max(DrapeSettle, 1e-4f)) : 1f;
            for (int j = 0; j < joints.Count; j++)
            {
                var jt = joints[j];
                if (jt.bone == null)
                    continue;
                var target = predicted[j] - aimedFrom[j];
                var current = jt.bone.rotation * jt.tailLocal;
                if (target.sqrMagnitude < 1e-10f || current.sqrMagnitude < 1e-10f)
                    continue;
                if (drape)
                {
                    target = Hang(j, target.normalized, hungValid ? hung[j] : current.normalized, settle);
                    hung[j] = target.normalized;
                }
                var turned = Quaternion.FromToRotation(current, target) * jt.bone.rotation;
                if (drape && DrapeTwist > 0f)
                    turned = LieFlat(j, turned, target);
                jt.bone.rotation = weight >= 1f ? turned : Quaternion.Slerp(jt.bone.rotation, turned, weight);
            }
            hungValid = drape;
        }

        /// <summary>
        /// Turn a bone about its own line so its piece of cloth lies flat on the skin under it: the cloth's
        /// normal onto the skin's (whichever face is nearer; at most 60 degrees), by how near the skin is.
        /// </summary>
        Quaternion LieFlat(int j, Quaternion rotation, Vector3 along)
        {
            if (through == null || j >= through.Length || through[j] == Vector3.zero)
                return rotation;
            var bone = joints[j].bone;
            var a = along.normalized;
            var mid = bone.position + along * 0.5f;
            var outward = joints[chainRoot[j]].bone.position - hips.position;
            outward.y = 0f;
            outward = outward.sqrMagnitude > 1e-6f ? outward.normalized : Vector3.zero;
            if (!body.Signed(mid, outward, DrapeFacing, out float s, out var n))
                return rotation;
            float near = 1f - Mathf.SmoothStep(0f, 1f, (s - 0.015f) / 0.035f);
            if (near <= 0f)
                return rotation;
            var cloth = rotation * through[j];
            var pc = cloth - Vector3.Dot(cloth, a) * a;
            var pn = n - Vector3.Dot(n, a) * a;
            if (pc.sqrMagnitude < 1e-6f || pn.sqrMagnitude < 1e-6f)
                return rotation;
            if (Vector3.Dot(pc, pn) < 0f)
                pn = -pn;
            float angle = Mathf.Clamp(Vector3.SignedAngle(pc, pn, a), -60f, 60f);
            return Quaternion.AngleAxis(angle * near * DrapeTwist, a) * rotation;
        }

        /// <summary>
        /// A bone hung from where its head is now (its parent already turned): from the direction it hung in
        /// the step before, part way (settle) down - or towards part way from the fitted direction - then
        /// turned out of the skin: the tail and the middle of the bone pushed off along the skin's normal to
        /// DrapeClearance, the length kept, a few times over.
        /// </summary>
        Vector3 Hang(int j, Vector3 fitted, Vector3 from, float settle)
        {
            var bone = joints[j].bone;
            var head = bone.position;
            float length = (bone.TransformPoint(joints[j].tailLocal) - head).magnitude;
            float share = Drape * drapeScale *
                          (chainLevel != null && j < chainLevel.Length && chainLevel[j] < DrapeByLevel.Length ? DrapeByLevel[chainLevel[j]] : 1f);
            var hanging = share >= 1f ? Vector3.down : Vector3.Slerp(fitted, Vector3.down, share).normalized;
            var dir = Vector3.Slerp(from, hanging, settle).normalized;
            var outward = joints[chainRoot[j]].bone.position - hips.position;
            outward.y = 0f;
            outward = outward.sqrMagnitude > 1e-6f ? outward.normalized : Vector3.zero;
            var tail = head + dir * length;
            float clear = DrapeClearance + (thick != null && j < thick.Length ? thick[j] : 0f);
            for (int it = 0; it < 4; it++)
            {
                bool moved = false;
                for (int k = 0; k < 2; k++)
                {
                    var x = k == 0 ? tail : (head + tail) * 0.5f;
                    if (body.Signed(x, outward, DrapeFacing, out float s, out var n) && s < clear)
                    {
                        tail += n * ((clear - s) * (k == 0 ? 1f : 2f));
                        tail = head + (tail - head).normalized * length;
                        moved = true;
                    }
                }
                // the piece's own vertices, carried by the bone turned this way: the deepest one sets the push
                // (at the tail, by its lever; at most 2 cm a round), along the skin's normal there; only skin
                // that faces the chain's side counts here, not the tops of feet
                if (samples != null && samples[j] != null && samples[j].Length > 0 && chainDepth[j] >= DrapeMeshFrom)
                {
                    var turned = Quaternion.FromToRotation(bone.rotation * joints[j].tailLocal, tail - head) * bone.rotation;
                    float worst = 0f;
                    var push = Vector3.zero;
                    for (int k = 0; k < samples[j].Length; k++)
                    {
                        var x = head + turned * samples[j][k];
                        if (body.Signed(x, outward, DrapeFacing, out float s, out var n) && s < DrapeMeshClearance && Vector3.Dot(n, outward) > 0f)
                        {
                            float need = (DrapeMeshClearance - s) / sampleLever[j][k];
                            if (need > worst)
                            {
                                worst = need;
                                push = n;
                            }
                        }
                    }
                    if (worst > 0f)
                    {
                        tail += push * Mathf.Min(worst, 0.02f);
                        var d = Vector3.RotateTowards(Vector3.down, (tail - head).normalized, DrapeMeshLift * Mathf.Deg2Rad, 0f);
                        if (Vector3.Angle(Vector3.down, tail - head) > DrapeMeshLift && Vector3.Angle(Vector3.down, dir) <= DrapeMeshLift)
                            tail = head + d * length;
                        else
                            tail = head + (tail - head).normalized * length;
                        moved = true;
                    }
                }
                if (!moved)
                    break;
            }
            // a bone that keeps (part of) the fitted pose - the hip end of a panel (DrapeByLevel) - is pushed off the skin only
            // as far as it drapes: the fitted pose lies where the game's stance has it (pushed whole, g04's left panel stood out
            // flat from her hip in the crouched guard)
            if (share < 1f)
                return Vector3.Slerp(fitted.normalized, (tail - head).normalized, Mathf.Clamp01(share)) * length;
            return tail - head;
        }
    }
}
