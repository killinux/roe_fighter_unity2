using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RoeFighter
{
    /// <summary>
    /// The body a skirt rests on: points of the skin round the hips and legs, with their normals, posed
    /// on the CPU each step the way the renderer skins them.
    ///
    /// Built once in the battle stance: the vertices of every skinned mesh whose strongest bone is a leg
    /// bone (a thigh or anything under one: calves, feet, twist helpers - shoes and leg armour count), or
    /// the pelvis or spine if the vertex is skin (a material named *_skin: belts and ornaments on the
    /// pelvis are left out, the skirt hangs from them).  Thinned to one point per few-centimetre cell.
    /// Each point keeps its place in the frame of each of its bones, so Pose() is linear blend skinning
    /// against the stance.  Signed() is the distance of a point from the skin (positive outside) as the
    /// nearest point sees it (along its normal).  Player builds cannot read the game's meshes (not
    /// Read/Write): the points come from a BakeMesh copy, the bone weights from the shared mesh.
    /// </summary>
    public class RoeBodySurface
    {
        public int Count => count;

        readonly Transform[] bones;                 // the distinct bones the points hang on
        readonly int[] boneOf;                      // per point and slot (4): index into bones, -1 none
        readonly float[] weightOf;
        readonly Vector3[] localPos, localNormal;   // per point and slot: in that bone's frame
        readonly Vector3[] pos, normal;             // posed
        readonly Matrix4x4[] boneMatrix;
        readonly Quaternion[] boneRotation;
        readonly int count;

        // a spatial hash of the posed points
        const int Buckets = 4096;
        readonly int[] head = new int[Buckets];
        readonly int[] next;
        readonly float cell;

        public RoeBodySurface(Animator animator, float spacing = 0.025f, float cell = 0.07f)
        {
            this.cell = cell;
            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            var spine = animator.GetBoneTransform(HumanBodyBones.Spine);
            var thighs = new[] { animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg), animator.GetBoneTransform(HumanBodyBones.RightUpperLeg) };
            float top = hips.position.y + 0.15f, bottom = animator.transform.position.y + 0.01f;
            bool Leg(Transform b) => b != null && ((thighs[0] != null && b.IsChildOf(thighs[0])) || (thighs[1] != null && b.IsChildOf(thighs[1])));
            // the lines the body is round: each leg (thigh - knee - ankle - toes) and the trunk (straight up
            // through the hips); a point is kept only if its normal faces away from them - the inside faces
            // of shells (armour, shoes) and folds face in and would pull a skirt into the leg
            var lines = new List<(Vector3 a, Vector3 b)>();
            foreach (var side in new[] { (HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes),
                                         (HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, HumanBodyBones.RightToes) })
            {
                var chain = new[] { side.Item1, side.Item2, side.Item3, side.Item4 }.Select(animator.GetBoneTransform).Where(t => t != null).ToList();
                for (int k = 0; k + 1 < chain.Count; k++)
                    lines.Add((chain[k].position, chain[k + 1].position));
            }
            Vector3 Outward(Vector3 p, bool leg)
            {
                if (!leg)
                {
                    var r = p - hips.position;
                    r.y = 0f;
                    return r.normalized;
                }
                Vector3 best = Vector3.zero;
                float bestD = float.PositiveInfinity;
                foreach (var (a, b) in lines)
                {
                    var ab = b - a;
                    float u = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-8f));
                    var r = p - (a + ab * u);
                    if (r.sqrMagnitude < bestD)
                    {
                        bestD = r.sqrMagnitude;
                        best = r;
                    }
                }
                return best.normalized;
            }

            var picked = new List<(Vector3 p, Vector3 n, BoneWeight w, Transform[] bones)>();
            var taken = new HashSet<Vector3Int>();
            var baked = new Mesh();
            foreach (var smr in animator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.sharedMesh == null || smr.name.ToLowerInvariant().Contains("hair"))
                    continue;
                var mats = smr.sharedMaterials;
                bool weapon = false;
                foreach (var m in mats)
                    weapon |= m != null && m.name.StartsWith("wp_");
                if (weapon)
                    continue;
                // (baked with scale: in the renderer's own space, to the world by its whole matrix - a DOA6 mesh's space is in
                // cm under a 0.01 scale; until 10-04 position and rotation only, which found no skin on a DOA6 character)
                smr.BakeMesh(baked, true);
                var v = baked.vertices;
                var nrm = baked.normals;
                var w = smr.sharedMesh.boneWeights;
                var smrBones = smr.bones;
                if (v.Length == 0 || w.Length != v.Length || nrm.Length != v.Length)
                    continue;
                var skin = new bool[v.Length];
                // skin: the suit's *_skin materials, or anything on the skin shader (the nude body, RoeNudeBody)
                for (int s = 0; s < baked.subMeshCount && s < mats.Length; s++)
                    if (mats[s] != null && (mats[s].name.ToLowerInvariant().Contains("_skin") || (mats[s].shader != null && mats[s].shader.name == "ROE/Skin")))
                        foreach (int i in baked.GetIndices(s))
                            skin[i] = true;
                var toWorld = smr.transform.localToWorldMatrix;
                for (int i = 0; i < v.Length; i++)
                {
                    var bw = w[i];
                    if (bw.boneIndex0 >= smrBones.Length)
                        continue;
                    var b = smrBones[bw.boneIndex0];
                    if (b == null || !(Leg(b) || (skin[i] && (b == hips || b == spine))))
                        continue;
                    var p = toWorld.MultiplyPoint3x4(v[i]);
                    if (p.y > top || p.y < bottom)
                        continue;
                    var nw = toWorld.MultiplyVector(nrm[i]).normalized;
                    if (Vector3.Dot(nw, Outward(p, Leg(b))) < 0.2f)
                        continue;
                    var key = new Vector3Int(Mathf.FloorToInt(p.x / spacing), Mathf.FloorToInt(p.y / spacing), Mathf.FloorToInt(p.z / spacing));
                    if (!taken.Add(key))
                        continue;
                    picked.Add((p, nw, bw, smrBones));
                }
            }
            if (Application.isPlaying)
                Object.Destroy(baked);
            else
                Object.DestroyImmediate(baked);

            count = picked.Count;
            boneOf = new int[count * 4];
            weightOf = new float[count * 4];
            localPos = new Vector3[count * 4];
            localNormal = new Vector3[count * 4];
            pos = new Vector3[count];
            normal = new Vector3[count];
            next = new int[count];
            var index = new Dictionary<Transform, int>();
            var list = new List<Transform>();
            for (int i = 0; i < count; i++)
            {
                var (p, n, bw, smrBones) = picked[i];
                pos[i] = p;
                normal[i] = n;
                var slots = new[] { (bw.boneIndex0, bw.weight0), (bw.boneIndex1, bw.weight1), (bw.boneIndex2, bw.weight2), (bw.boneIndex3, bw.weight3) };
                float total = 0f;
                foreach (var (bi, wt) in slots)
                    if (wt > 0f && bi < smrBones.Length && smrBones[bi] != null)
                        total += wt;
                for (int k = 0; k < 4; k++)
                {
                    var (bi, wt) = slots[k];
                    boneOf[i * 4 + k] = -1;
                    if (wt <= 0f || bi >= smrBones.Length || smrBones[bi] == null || total <= 0f)
                        continue;
                    var b = smrBones[bi];
                    if (!index.TryGetValue(b, out int at))
                    {
                        at = list.Count;
                        index[b] = at;
                        list.Add(b);
                    }
                    boneOf[i * 4 + k] = at;
                    weightOf[i * 4 + k] = wt / total;
                    localPos[i * 4 + k] = b.InverseTransformPoint(p);
                    localNormal[i * 4 + k] = Quaternion.Inverse(b.rotation) * n;
                }
            }
            bones = list.ToArray();
            boneMatrix = new Matrix4x4[bones.Length];
            boneRotation = new Quaternion[bones.Length];
            Rehash();
        }

        /// <summary>
        /// The vertices each of the given bones carries most of (weight at least half), world positions in
        /// the current pose - from BakeMesh copies, as player builds cannot read the game's meshes.
        /// </summary>
        public static Dictionary<Transform, List<Vector3>> BoneVertices(Animator animator, ICollection<Transform> wanted)
        {
            var result = new Dictionary<Transform, List<Vector3>>();
            var baked = new Mesh();
            foreach (var smr in animator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.sharedMesh == null)
                    continue;
                var smrBones = smr.bones;
                bool any = false;
                foreach (var b in smrBones)
                    any |= b != null && wanted.Contains(b);
                if (!any)
                    continue;
                smr.BakeMesh(baked, true);
                var v = baked.vertices;
                var w = smr.sharedMesh.boneWeights;
                var toWorld = smr.transform.localToWorldMatrix;
                for (int i = 0; i < v.Length && i < w.Length; i++)
                {
                    if (w[i].weight0 < 0.5f || w[i].boneIndex0 >= smrBones.Length)
                        continue;
                    var b = smrBones[w[i].boneIndex0];
                    if (b == null || !wanted.Contains(b))
                        continue;
                    if (!result.TryGetValue(b, out var list))
                        result[b] = list = new List<Vector3>();
                    list.Add(toWorld.MultiplyPoint3x4(v[i]));
                }
            }
            if (Application.isPlaying)
                Object.Destroy(baked);
            else
                Object.DestroyImmediate(baked);
            return result;
        }

        /// <summary>Skin the points to the bones' current pose.</summary>
        public void Pose()
        {
            for (int b = 0; b < bones.Length; b++)
            {
                boneMatrix[b] = bones[b].localToWorldMatrix;
                boneRotation[b] = bones[b].rotation;
            }
            for (int i = 0; i < count; i++)
            {
                Vector3 p = Vector3.zero, n = Vector3.zero;
                for (int k = 0; k < 4; k++)
                {
                    int b = boneOf[i * 4 + k];
                    if (b < 0)
                        continue;
                    float wt = weightOf[i * 4 + k];
                    p += wt * boneMatrix[b].MultiplyPoint3x4(localPos[i * 4 + k]);
                    n += wt * (boneRotation[b] * localNormal[i * 4 + k]);
                }
                pos[i] = p;
                normal[i] = n.normalized;
            }
            Rehash();
        }

        static int Hash(int x, int y, int z) => ((x * 73856093) ^ (y * 19349663) ^ (z * 83492791)) & (Buckets - 1);

        void Rehash()
        {
            for (int i = 0; i < Buckets; i++)
                head[i] = -1;
            for (int i = 0; i < count; i++)
            {
                int h = Hash(Mathf.FloorToInt(pos[i].x / cell), Mathf.FloorToInt(pos[i].y / cell), Mathf.FloorToInt(pos[i].z / cell));
                next[i] = head[h];
                head[h] = i;
            }
        }

        /// <summary>
        /// The signed distance of x from the skin (positive outside) and the skin's normal there: the
        /// distances from the tangent planes of the points within a cell, averaged with Gaussian weights
        /// (sigma 2 cm) - smooth as x moves, so a skirt hung on it does not jump from one point to the next.
        /// Only points whose normal faces along facing count (weight eased in round minFacing), or that face
        /// up (a raised thigh holds the skirt from below).  False: no such point near.
        /// </summary>
        public bool Signed(Vector3 x, Vector3 facing, float minFacing, out float distance, out Vector3 n)
        {
            distance = float.PositiveInfinity;
            n = Vector3.up;
            int cx = Mathf.FloorToInt(x.x / cell), cy = Mathf.FloorToInt(x.y / cell), cz = Mathf.FloorToInt(x.z / cell);
            float r2 = cell * cell, inv2s2 = 1f / (2f * Sigma * Sigma);
            bool any = facing.sqrMagnitude < 1e-8f;
            float sw = 0f, sd = 0f;
            var sn = Vector3.zero;
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    for (int dz = -1; dz <= 1; dz++)
                        for (int i = head[Hash(cx + dx, cy + dy, cz + dz)]; i >= 0; i = next[i])
                        {
                            var rel = x - pos[i];
                            float d2 = rel.sqrMagnitude;
                            if (d2 >= r2)
                                continue;
                            var ni = normal[i];
                            float f = any ? 1f : Mathf.Max(Mathf.SmoothStep(0f, 1f, (Vector3.Dot(ni, facing) - minFacing) / 0.3f + 0.5f),
                                                           Mathf.SmoothStep(0f, 1f, (ni.y - 0.3f) / 0.2f));
                            if (f <= 0f)
                                continue;
                            float w = Mathf.Exp(-d2 * inv2s2) * f;
                            sw += w;
                            sd += w * Vector3.Dot(rel, ni);
                            sn += w * ni;
                        }
            if (sw < 1e-12f)
                return false;
            // a little weight for "far outside": where only a few far points are in reach (their planes can
            // say anything) the distance eases out instead of jumping to theirs
            distance = (sd + Background * cell) / (sw + Background);
            n = sn.sqrMagnitude > 1e-12f ? sn.normalized : Vector3.up;
            return true;
        }

        const float Sigma = 0.02f, Background = 0.05f;

        /// <summary>The nearest posed point to x, any facing (for checks); false if none within a cell.</summary>
        public bool Nearest(Vector3 x, out Vector3 p, out Vector3 n)
        {
            p = x;
            n = Vector3.up;
            int cx = Mathf.FloorToInt(x.x / cell), cy = Mathf.FloorToInt(x.y / cell), cz = Mathf.FloorToInt(x.z / cell);
            float best = float.PositiveInfinity;
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    for (int dz = -1; dz <= 1; dz++)
                        for (int i = head[Hash(cx + dx, cy + dy, cz + dz)]; i >= 0; i = next[i])
                        {
                            float d2 = (pos[i] - x).sqrMagnitude;
                            if (d2 < best)
                            {
                                best = d2;
                                p = pos[i];
                                n = normal[i];
                            }
                        }
            return best <= cell * cell * 2.25f;
        }
    }
}
