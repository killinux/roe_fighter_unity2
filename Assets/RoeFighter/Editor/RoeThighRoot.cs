using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace RoeFighter.EditorTools
{
    /// <summary>
    /// The skin round the top of each thigh, graded from the pelvis to the thigh by where it lies along the thigh - after
    /// the ROE PMX export's rule (ripper_tpose export_character_model_blender.skin_rotation_share: GRANT_BLEND 0.35, skin
    /// at the hip joint follows the thigh half, skin 0.35 thigh lengths below it wholly, as far above it not at all), here
    /// per vertex instead of per helper bone.
    ///
    /// Why (user 10-06: "大腿根的权重还是不对呀"): Kart's (b10) body took its weights from what lay on it - the suit's skin
    /// below the hem of her shorts (the game's thigh skin: 80-95 % thigh), the shorts above it (70-85 % pelvis).  The step
    /// sits 0.2 thigh lengths below the hip: in a lunge the thigh turns 90 degrees from the pelvis, the skin above the step
    /// stays with the pelvis, the skin below goes with the thigh, and the band between bulges like a ring round the top of
    /// the thigh - from behind it looks like the buttock hanging down onto the thigh.
    ///
    /// Tunables (defaults; RoeThighRoot.Set "center=0,blend=0.35,..." overrides): center (thigh lengths below the hip where
    /// the skin follows the thigh half), blend (half the width of the change), reach (m from the thigh's axis), midline
    /// (m: the skin nearer the middle than this keeps its weights - the crotch and the cleft of the buttocks follow both
    /// thighs as the game weighted them), island (only skin that had some of the leg), other (drop the other leg's weight
    /// from skin clearly on this side).
    /// </summary>
    public static class RoeThighRoot
    {
        public static float Center = 0f;
        public static float Blend = 0.35f;
        public static float Reach = 0.2f;
        public static float Midline = 0.04f;
        public static bool Island = false;
        public static bool Other = true;
        public static float SoftShare = 0.1f;    // a renderer with fewer of its hip vertices partly on a leg is a rigid piece
        public static bool GradeOn = true;       // grade the share along the thigh (Grade)
        public static bool Helper = false;       // re-hang the blend through a half-turning hip helper (Split)
        public static float RollBack = -1f;      // probe: the share of the thigh's roll the upper twist bone gives back (-1: as driven)
        public static bool ForceDrive = false;   // probe: the helper rig drives the helpers even on a game clip (which keys them)
        public static float Unroll = 0f;         // probe: the share of the thigh's own roll against the pelvis taken out of the leg
        public static float Spread = 1f;         // probe: a thigh's swing out past SpreadFrom degrees from down is kept at this share
        public static float SpreadFrom = 40f;
        public static bool ButtOn = false;       // the buttocks back on the pelvis (Butt)
        public static float FoldLo = 0.15f;      // Butt: thigh lengths below the hip where the buttock's skin starts to take the leg
        public static float FoldHi = 0.4f;       //   and where it has all it had
        public static float Behind = 0.06f;      //   m behind the hip joint from which a vertex counts as wholly the buttock's

        public static void Reset()
        {
            Center = 0f;
            Blend = 0.35f;
            Reach = 0.2f;
            Midline = 0.04f;
            Island = false;
            Other = true;
            SoftShare = 0.1f;
            GradeOn = true;
            Helper = false;
            RollBack = -1f;
            ForceDrive = false;
            ButtOn = false;
            Unroll = 0f;
            Spread = 1f;
            SpreadFrom = 40f;
            FoldLo = 0.15f;
            FoldHi = 0.4f;
            Behind = 0.06f;
        }

        /// <summary>Overrides as "name=value,..." (center, blend, reach, midline, island, other).</summary>
        public static string Set(string spec)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            foreach (var part in (spec ?? "").Split(',').Select(p => p.Trim()).Where(p => p.Length > 0))
            {
                var kv = part.Split('=');
                string k = kv[0].Trim().ToLowerInvariant(), v = kv.Length > 1 ? kv[1].Trim() : "1";
                switch (k)
                {
                    case "center": Center = float.Parse(v, inv); break;
                    case "blend": Blend = float.Parse(v, inv); break;
                    case "reach": Reach = float.Parse(v, inv); break;
                    case "midline": Midline = float.Parse(v, inv); break;
                    case "island": Island = v != "0" && v != "false"; break;
                    case "other": Other = v != "0" && v != "false"; break;
                    case "soft": SoftShare = float.Parse(v, inv); break;
                    case "grade": GradeOn = v != "0" && v != "false"; break;
                    case "helper": Helper = v != "0" && v != "false"; break;
                    case "roll": RollBack = float.Parse(v, inv); break;
                    case "drive": ForceDrive = v != "0" && v != "false"; break;
                    case "butt": ButtOn = v != "0" && v != "false"; break;
                    case "unroll": Unroll = float.Parse(v, inv); break;
                    case "spread": Spread = float.Parse(v, inv); break;
                    case "spreadfrom": SpreadFrom = float.Parse(v, inv); break;
                    case "foldlo": FoldLo = float.Parse(v, inv); break;
                    case "foldhi": FoldHi = float.Parse(v, inv); break;
                    case "behind": Behind = float.Parse(v, inv); break;
                    default: Debug.LogWarning($"[ROE] thigh root: no setting '{k}'"); break;
                }
            }
            return Describe();
        }

        public static string Describe() =>
            (GradeOn ? $"graded: center {Center:F2}, blend {Blend:F2}, reach {Reach:F2} m, midline {Midline:F2} m{(Island ? ", island" : "")}{(Other ? ", other leg dropped" : "")}" : "not graded") +
            (Helper ? "; hip helpers" : "");

        /// <summary>Which leg a bone belongs to by its name: 1 left, 2 right, 0 none.</summary>
        public static int LegSide(string name)
        {
            if (!Regex.IsMatch(name, "Thigh|Calf|Knee|knee|Foot|Toe|Leg|leg"))
                return 0;
            var m = Regex.Match(name, @"^Bip001 ([LR])(?: |[A-Z])");
            if (m.Success)
                return m.Groups[1].Value == "L" ? 1 : 2;
            if (Regex.IsMatch(name, @"(_L|_LT|\.L|Left)\b|^L_|\bL\b"))
                return 1;
            if (Regex.IsMatch(name, @"(_R|_RT|\.R|Right)\b|^R_|\bR\b"))
                return 2;
            return 0;
        }

        /// <summary>The leg a bone moves with: its own name's, else its nearest ancestor's (Kart's holster_L hangs on the left
        /// thigh, Rope_L on the left twist bone) - up to the pelvis.</summary>
        public static int LegSide(Transform bone)
        {
            for (var x = bone; x != null && x.name != "Bip001 Pelvis" && x.name != "Bip001"; x = x.parent)
            {
                int s = LegSide(x.name);
                if (s != 0)
                    return s;
            }
            return 0;
        }

        /// <summary>
        /// Whether a renderer's skin round the hips is soft - a share (at least Soft) of its vertices there hang partly on a
        /// leg and partly elsewhere, as skin and cloth do; rigid pieces (holsters: wholly on one bone) are left alone.
        /// </summary>
        public static bool IsSoft(Mesh mesh, Transform[] bones, List<Leg> legs, out float share)
        {
            var verts = mesh.vertices;
            var bw = mesh.boneWeights;
            var sideOf = bones.Select(b => b != null ? LegSide(b) : 0).ToArray();
            int near = 0, mixed = 0;
            for (int v = 0; v < verts.Length && v < bw.Length; v++)
            {
                var (own, (t, d)) = legs.Select(l => (leg: l, a: Along(l, verts[v]))).OrderBy(x => x.a.d).First();
                if (d > Reach || t < Center - Blend || t > Center + Blend)
                    continue;
                near++;
                var w = bw[v];
                float legW = (sideOf[w.boneIndex0] == own.side ? w.weight0 : 0f) + (sideOf[w.boneIndex1] == own.side ? w.weight1 : 0f)
                           + (sideOf[w.boneIndex2] == own.side ? w.weight2 : 0f) + (sideOf[w.boneIndex3] == own.side ? w.weight3 : 0f);
                if (legW > 0.05f && legW < 0.95f)
                    mixed++;
            }
            share = near > 0 ? (float)mixed / near : 0f;
            return near > 0 && share >= SoftShare;
        }

        public class Leg
        {
            public int side;                     // 1 left, 2 right
            public Vector3 hip, knee;            // bind, the mesh's space
            public Vector3 forward;              // where she faces, the mesh's space (ROE meshes lie z up)
            public string upper;                 // the bone that takes thigh weight a vertex had none of (upper twist bone or thigh)
        }

        /// <summary>
        /// The hip and knee of each leg in a renderer's bind space, from the renderer whose bones have them (her body): the
        /// hip where the thigh's upper twist bone (or the thigh) starts, the knee where the calf does; facing: her model
        /// root's forward.
        /// </summary>
        public static List<Leg> Legs(SkinnedMeshRenderer r, Transform modelRoot)
        {
            var forward = r.transform.InverseTransformDirection(modelRoot.forward).normalized;
            var binds = r.sharedMesh.bindposes;
            var bones = r.bones;
            var legs = new List<Leg>();
            foreach (var (side, s) in new[] { (1, "L"), (2, "R") })
            {
                int hi = System.Array.FindIndex(bones, b => b != null && Regex.IsMatch(b.name, $@"^Bip001 {s} ?ThighTwist$"));
                if (hi < 0)
                    hi = System.Array.FindIndex(bones, b => b != null && b.name == $"Bip001 {s} Thigh");
                int ki = System.Array.FindIndex(bones, b => b != null && b.name == $"Bip001 {s} Calf");
                if (hi < 0 || ki < 0 || hi >= binds.Length || ki >= binds.Length)
                    continue;
                legs.Add(new Leg
                {
                    side = side,
                    hip = binds[hi].inverse.MultiplyPoint3x4(Vector3.zero),
                    knee = binds[ki].inverse.MultiplyPoint3x4(Vector3.zero),
                    forward = forward,
                    upper = bones[hi].name,
                });
            }
            return legs;
        }

        /// <summary>The same legs in another renderer's space (renderers of one model keep their places to each other).</summary>
        public static List<Leg> Moved(List<Leg> legs, SkinnedMeshRenderer from, SkinnedMeshRenderer to)
        {
            var m = to.transform.worldToLocalMatrix * from.transform.localToWorldMatrix;
            return legs.Select(l => new Leg
            {
                side = l.side, hip = m.MultiplyPoint3x4(l.hip), knee = m.MultiplyPoint3x4(l.knee), forward = m.MultiplyVector(l.forward).normalized, upper = l.upper,
            }).ToList();
        }

        /// <summary>
        /// The buttocks back on the pelvis: behind each hip (fully from Behind m behind the joint, not at all in front of it)
        /// a vertex keeps of its weight on that leg only what smoothstep(FoldLo, FoldHi, t) says (t along hip -> knee in thigh
        /// lengths: the gluteal fold lies at ~0.3), the rest goes to the trunk as it had it (the pelvis when it had none).
        /// Before Kart's knee fix (10-06) her upper twist bones stayed on the spine and her buttocks were round in every lunge;
        /// with them following the thigh, the skin of the buttock they carry (the shorts' weights above the hem) went along.
        /// </summary>
        public static int Butt(Mesh mesh, Transform[] bones, List<Leg> legs, out string note)
        {
            var weights = mesh.boneWeights;
            var verts = mesh.vertices;
            var sideOf = bones.Select(b => b != null ? LegSide(b) : 0).ToArray();
            int pelvis = System.Array.FindIndex(bones, b => b != null && b.name == "Bip001 Pelvis");
            int changed = 0;
            float before = 0f, after = 0f;
            for (int v = 0; v < verts.Length && v < weights.Length; v++)
            {
                var (own, (t, d)) = legs.Select(l => (leg: l, a: Along(l, verts[v]))).OrderBy(x => x.a.d).First();
                if (d > Reach || t > FoldHi)
                    continue;
                float behind = -Vector3.Dot(verts[v] - own.hip, own.forward);
                float back = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, Behind, behind));
                if (back <= 0f)
                    continue;
                float keep = Mathf.Lerp(1f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(FoldLo, FoldHi, t)), back);
                var w = weights[v];
                var map = new Dictionary<int, float>();
                foreach (var (bi, wt) in new[] { (w.boneIndex0, w.weight0), (w.boneIndex1, w.weight1), (w.boneIndex2, w.weight2), (w.boneIndex3, w.weight3) })
                    if (wt > 0f)
                        map[bi] = (map.TryGetValue(bi, out float x) ? x : 0f) + wt;
                float legW = map.Where(kv => sideOf[kv.Key] == own.side).Sum(kv => kv.Value);
                if (legW <= 0.01f || keep >= 0.999f)
                    continue;
                float trunkW = map.Where(kv => sideOf[kv.Key] == 0).Sum(kv => kv.Value);
                if (trunkW <= 1e-4f && pelvis < 0)
                    continue;
                float moved = legW * (1f - keep);
                var result = new Dictionary<int, float>();
                foreach (var kv in map)
                {
                    if (sideOf[kv.Key] == own.side)
                        result[kv.Key] = kv.Value * keep;
                    else if (sideOf[kv.Key] == 0 && trunkW > 1e-4f)
                        result[kv.Key] = kv.Value + kv.Value / trunkW * moved;
                    else
                        result[kv.Key] = kv.Value;
                }
                if (trunkW <= 1e-4f)
                    result[pelvis] = (result.TryGetValue(pelvis, out float y) ? y : 0f) + moved;
                var top = result.Where(kv => kv.Value > 0f).OrderByDescending(kv => kv.Value).Take(4).ToList();
                float sum = top.Sum(kv => kv.Value);
                while (top.Count < 4)
                    top.Add(new KeyValuePair<int, float>(top[0].Key, 0f));
                weights[v] = new BoneWeight
                {
                    boneIndex0 = top[0].Key, weight0 = top[0].Value / sum, boneIndex1 = top[1].Key, weight1 = top[1].Value / sum,
                    boneIndex2 = top[2].Key, weight2 = top[2].Value / sum, boneIndex3 = top[3].Key, weight3 = top[3].Value / sum,
                };
                before += legW;
                after += legW * keep;
                changed++;
            }
            if (changed > 0)
                mesh.boneWeights = weights;
            note = $"{changed} vertices behind the hips gave leg weight back to the trunk ({before / Mathf.Max(1, changed):F2} -> {after / Mathf.Max(1, changed):F2} on average)";
            return changed;
        }

        static (float t, float d) Along(Leg leg, Vector3 p)
        {
            var seg = leg.knee - leg.hip;
            float t = Vector3.Dot(p - leg.hip, seg) / seg.sqrMagnitude;
            return (t, Vector3.Distance(p, leg.hip + seg * Mathf.Clamp01(t)));
        }

        /// <summary>
        /// Grades a mesh's weights in place (see the class); legs in the mesh's bind space.  The leg's new share is spread
        /// over its bones as before (the upper twist bone when it had none), the rest over the trunk's bones as before (the
        /// pelvis when it had none); a vertex that would need a bone the renderer is not skinned to is left alone.  Returns
        /// how many vertices changed; note says by how much.
        /// </summary>
        public static int Grade(Mesh mesh, Transform[] bones, List<Leg> legs, out string note)
        {
            var weights = mesh.boneWeights;
            var verts = mesh.vertices;
            var sideOf = bones.Select(b => b != null ? LegSide(b) : 0).ToArray();
            int pelvis = System.Array.FindIndex(bones, b => b != null && b.name == "Bip001 Pelvis");
            var upperOf = legs.ToDictionary(l => l, l => System.Array.FindIndex(bones, b => b != null && b.name == l.upper));
            int changed = 0;
            float before = 0f, after = 0f;
            for (int v = 0; v < verts.Length && legs.Count > 0; v++)
            {
                var along = legs.Select(l => (leg: l, a: Along(l, verts[v]))).OrderBy(x => x.a.d).ToList();
                var (own, (t, d)) = along[0];
                if (d > Reach || t < Center - Blend || t > Center + Blend)
                    continue;
                // the middle (crotch, cleft): as it was; clearly on one side: the graded share
                float margin = along.Count > 1 ? along[1].a.d - d : float.MaxValue;
                float g = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(Midline * 0.5f, Midline * 2f, margin));
                if (g <= 0f)
                    continue;
                var w = weights[v];
                var map = new Dictionary<int, float>();
                foreach (var (bi, wt) in new[] { (w.boneIndex0, w.weight0), (w.boneIndex1, w.weight1), (w.boneIndex2, w.weight2), (w.boneIndex3, w.weight3) })
                    if (wt > 0f)
                        map[bi] = (map.TryGetValue(bi, out float x) ? x : 0f) + wt;
                float legW = map.Where(kv => sideOf[kv.Key] == own.side).Sum(kv => kv.Value);
                float otherW = map.Where(kv => sideOf[kv.Key] != 0 && sideOf[kv.Key] != own.side).Sum(kv => kv.Value);
                float trunkW = 1f - legW - otherW;
                if (Island && legW <= 0.01f)
                    continue;
                float target = Mathf.Clamp01(0.5f + (t - Center) / (2f * Blend));
                float otherNew = Other ? Mathf.Lerp(otherW, 0f, g) : otherW;
                float legNew = Mathf.Min(Mathf.Lerp(legW, target, g), 1f - otherNew);
                float trunkNew = Mathf.Max(0f, 1f - legNew - otherNew);
                // a share with no bone to go to (no leg weight and no upper twist bone in this renderer, or no trunk weight
                // and no pelvis): left as it was
                if ((legNew > 0f && legW <= 1e-4f && upperOf[own] < 0) || (trunkNew > 0f && trunkW <= 1e-4f && pelvis < 0))
                    continue;
                var result = new Dictionary<int, float>();
                void Spread(IEnumerable<KeyValuePair<int, float>> part, float had, float now, int fallback)
                {
                    if (now <= 0f)
                        return;
                    var list = part.ToList();
                    if (had > 1e-4f && list.Count > 0)
                        foreach (var kv in list)
                            result[kv.Key] = (result.TryGetValue(kv.Key, out float y) ? y : 0f) + kv.Value / had * now;
                    else
                        result[fallback] = (result.TryGetValue(fallback, out float y) ? y : 0f) + now;
                }
                Spread(map.Where(kv => sideOf[kv.Key] == own.side), legW, legNew, upperOf[own]);
                Spread(map.Where(kv => sideOf[kv.Key] != 0 && sideOf[kv.Key] != own.side), otherW, otherNew, upperOf[own]);
                Spread(map.Where(kv => sideOf[kv.Key] == 0), trunkW, trunkNew, pelvis);
                var top = result.Where(kv => kv.Value > 0f).OrderByDescending(kv => kv.Value).Take(4).ToList();
                float sum = top.Sum(kv => kv.Value);
                if (sum <= 0f)
                    continue;
                while (top.Count < 4)
                    top.Add(new KeyValuePair<int, float>(top[0].Key, 0f));
                weights[v] = new BoneWeight
                {
                    boneIndex0 = top[0].Key, weight0 = top[0].Value / sum, boneIndex1 = top[1].Key, weight1 = top[1].Value / sum,
                    boneIndex2 = top[2].Key, weight2 = top[2].Value / sum, boneIndex3 = top[3].Key, weight3 = top[3].Value / sum,
                };
                before += legW;
                after += legNew;
                changed++;
            }
            if (changed > 0)
                mesh.boneWeights = weights;
            note = $"{changed} vertices round the hips graded, their leg weight {before / Mathf.Max(1, changed):F2} -> {after / Mathf.Max(1, changed):F2} on average";
            return changed;
        }

        /// <summary>
        /// The skin between the pelvis and a thigh re-hung through a hip helper that turns half way between them
        /// (rootIndex: the helper of each leg's side among the mesh's bones).  A vertex that follows the thigh by f (its leg
        /// weight over leg + pelvis) keeps f, but blends two bones 45 degrees apart where it blended two 90 degrees apart:
        /// f up to 0.5 between the pelvis and the helper, above it between the helper and the leg.  Linear blending of two
        /// turns pulls the skin in towards the joint (by 1 - cos(half the angle between them) at an even blend: 29 % at 90
        /// degrees, 8 % at 45) - the pinched ring at the top of the thigh with the thigh's full round below it.  Only within
        /// Reach of the thigh's axis from 2 Blend above the hip to 2 Blend below it.  Returns how many vertices changed.
        /// </summary>
        public static int Split(Mesh mesh, Transform[] bones, List<Leg> legs, Dictionary<int, int> rootIndex, out string note)
        {
            var weights = mesh.boneWeights;
            var verts = mesh.vertices;
            var sideOf = bones.Select(b => b != null ? LegSide(b) : 0).ToArray();
            int pelvis = System.Array.FindIndex(bones, b => b != null && b.name == "Bip001 Pelvis");
            int changed = 0;
            float onHelper = 0f;
            for (int v = 0; v < verts.Length && pelvis >= 0; v++)
            {
                var (own, (t, d)) = legs.Select(l => (leg: l, a: Along(l, verts[v]))).OrderBy(x => x.a.d).First();
                if (d > Reach || t < -2f * Blend || t > 2f * Blend || !rootIndex.TryGetValue(own.side, out int helper))
                    continue;
                var w = weights[v];
                var map = new Dictionary<int, float>();
                foreach (var (bi, wt) in new[] { (w.boneIndex0, w.weight0), (w.boneIndex1, w.weight1), (w.boneIndex2, w.weight2), (w.boneIndex3, w.weight3) })
                    if (wt > 0f)
                        map[bi] = (map.TryGetValue(bi, out float x) ? x : 0f) + wt;
                float legW = map.Where(kv => sideOf[kv.Key] == own.side).Sum(kv => kv.Value);
                float pelvisW = map.TryGetValue(pelvis, out float pw) ? pw : 0f;
                float both = legW + pelvisW;
                if (legW <= 0.01f || pelvisW <= 0.01f)
                    continue;
                float f = legW / both;
                float toPelvis = f <= 0.5f ? both * (1f - 2f * f) : 0f;
                float toHelper = f <= 0.5f ? both * 2f * f : both * 2f * (1f - f);
                float toLeg = f <= 0.5f ? 0f : both * (2f * f - 1f);
                var result = map.Where(kv => kv.Key != pelvis && sideOf[kv.Key] != own.side).ToDictionary(kv => kv.Key, kv => kv.Value);
                if (toPelvis > 0f)
                    result[pelvis] = toPelvis;
                result[helper] = (result.TryGetValue(helper, out float hw) ? hw : 0f) + toHelper;
                foreach (var kv in map.Where(kv => sideOf[kv.Key] == own.side))
                    if (toLeg > 0f)
                        result[kv.Key] = (result.TryGetValue(kv.Key, out float y) ? y : 0f) + kv.Value / legW * toLeg;
                var top = result.Where(kv => kv.Value > 0f).OrderByDescending(kv => kv.Value).Take(4).ToList();
                float sum = top.Sum(kv => kv.Value);
                while (top.Count < 4)
                    top.Add(new KeyValuePair<int, float>(top[0].Key, 0f));
                weights[v] = new BoneWeight
                {
                    boneIndex0 = top[0].Key, weight0 = top[0].Value / sum, boneIndex1 = top[1].Key, weight1 = top[1].Value / sum,
                    boneIndex2 = top[2].Key, weight2 = top[2].Value / sum, boneIndex3 = top[3].Key, weight3 = top[3].Value / sum,
                };
                onHelper += toHelper / sum;
                changed++;
            }
            if (changed > 0)
                mesh.boneWeights = weights;
            note = $"{changed} vertices re-hung through the hip helpers ({onHelper / Mathf.Max(1, changed):F2} on the helper on average)";
            return changed;
        }
    }
}
