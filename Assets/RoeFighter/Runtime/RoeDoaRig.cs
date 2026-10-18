using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace RoeFighter
{
    /// <summary>
    /// A Dead or Alive 6 character's physics, mapped onto her fighter prefab (DoaFighter.BuildPhysics, from her game data:
    /// tools/doa6_physics.py).  Everything is on her own bones, in metres; RoeDoaPhysics simulates it, and the bone cloth
    /// gets the kinds of her loose bones from here (their names are only numbers).
    ///   chains   bone chains (DOA6 NUNO4): ponytail strands, sash tails, cords - a particle per bone, the first one fixed
    ///   swings   swing bones (.swg): short hair strands with angle limits
    ///   softs    lattice soft bodies (SOFT): breasts (and hips in some outfits) - a node per lattice point, each with its
    ///            animated target skinned to body bones; the breast meshes are skinned to the nodes (8 per vertex), blended
    ///            back to plain skinning towards the body
    ///   colliders capsules and ellipsoids on bones, in groups; each chain / soft body collides with its own groups
    ///   twists   the game's twist helpers (RF_*): bones its rig script turns with a share of a limb bone's own twist
    ///            (the clips never key them; DriveHelpers, after each pose)
    ///   grids    grid cloth (NUNO3: skirts, sleeves, flaps): a coarse grid of control points, each a transform (a column
    ///            hangs as a chain from the cloth's bone, so a bone solver can swing it too), the top rows skinned to the body
    ///   surfaces the visible cloth meshes, rebuilt from the control points every frame as the game does (RebuildSurfaces):
    ///            each vertex a bicubic patch over 4x4 control points, pushed out along the patch normal by its depth
    /// </summary>
    public class RoeDoaRig : MonoBehaviour
    {
        [Serializable]
        public class Collider
        {
            public Transform bone;
            public int type;                // 5 capsule along local Y (a radius, b half-length), 6 ellipsoid (radii a b c)
            public float a, b, c;           // metres
            public Vector3 center;          // in the bone's frame
            public Quaternion rotation = Quaternion.identity;
        }

        [Serializable]
        public class Group
        {
            public string name;
            public int[] colliders;
        }

        [Serializable]
        public class Chain
        {
            public string name, kind;       // hair, ribbon, chain
            public Transform parent;
            public Transform[] bones;       // bones[0] stays on the parent
            public float[] restLength;      // metres, to the previous bone
            public float[] param;           // the game's 17 words: gravity/mass, flags, damping a, damping b, stiffness a b c, iterations, restore, friction
            public int[] groups;
        }

        [Serializable]
        public class Swing
        {
            public Transform bone;
            public int group;               // the strand
            public float[] param;           // f0-f3 angle limits (degrees), f13 damping
        }

        [Serializable]
        public class Soft
        {
            public string name, kind;       // breast, body
            public Transform parent;
            public Transform[] nodes;
            public int[] flags;             // 0x01 pinned (the chest wall), 0x20 interior, 0x40 surface
            public float[] wSelf;
            // the animated target of node i: sum over k in [targetStart[i], targetStart[i+1]) of targetWeight[k] * targetBone[k].TransformPoint(targetLocal[k])
            public int[] targetStart;
            public Transform[] targetBone;
            public Vector3[] targetLocal;
            public float[] targetWeight;
            public int[] springs;           // pairs of nodes: lattice edges, then face diagonals
            public float[] springRest;      // metres
            public int edgeCount;           // the first edgeCount springs are lattice edges
            public int[] hull;              // closed triangle hull over the surface nodes
            public float restVolume;        // m^3
            public float gravity, damping, stiffness, scale;
            /// <summary>Which way is down in the parent bone when she stands as modelled: the rest shape already hangs that way.</summary>
            public Vector3 restDown;
            public Vector3 perAxis;         // metres (the game's per-axis words, taken as displacement limits)
            public float[] coef;
            public int[] groups;
        }

        [Serializable]
        public class Attachment
        {
            public Transform bone;
            public int[] body;              // per ref
            public float[] weight;          // per ref
            public int[] nodes;             // 8 per ref
            public float[] nodeWeight;      // 8 per ref
            public Vector3 offset;          // rest position minus the interpolated rest position (world, at build)
        }

        [Serializable]
        public class Twist
        {
            public Transform bone;
            public Transform source;        // the limb bone whose twist it shares
            public Vector3 axis;            // the twist axis in the source's frame (towards the next limb bone)
            public Quaternion sourceRest;   // the source's local rotation at rest
            public Quaternion restToSource; // the helper's rotation in the source's frame at rest
            public float share;             // 0: none of the twist (the limb's swing only), 1: all of it
        }

        /// <summary>A grid cloth (DOA6 NUNO3): skirt, sleeve, flap.</summary>
        [Serializable]
        public class Grid
        {
            public string name, kind;       // kind: skirt (any garment cloth)
            public Transform parent;
            public int cols, rows;
            public bool ring;               // the last column links back to the first (skirts, sleeves)
            public Transform[] cps;         // row-major; each column a chain of transforms hanging from the parent
            public Vector3[] restLocal;     // each control point in the parent's frame at rest
            public int[] links;             // 4 per control point: left, right, up, down (-1: none)
            public int skinnedRows;         // the top rows follow the body (2 in the game's data)
            // the skinned control points' targets (as soft nodes): weighted bone-local points
            public int[] targetStart;
            public Transform[] targetBone;
            public Vector3[] targetLocal;
            public float[] targetWeight;
            public int[] springs;           // pairs: structural (the grid's links), then shear (cell diagonals), then bend (skip one)
            public float[] springRest;      // metres
            public int[] springClass;       // 0 structural, 1 shear, 2 bend
            public float[] param;           // the 46 NUNO3 words (docs/doa-physics.md)
            public int[] iterations;        // word 37's bytes
            public int[] groups;
            public float radius;            // a control point's collision radius, metres (word 36)
        }

        /// <summary>A visible cloth mesh rebuilt from a grid (DOA6 mesh type 1).</summary>
        [Serializable]
        public class Surface
        {
            public string name;
            public int grid;
            public SkinnedMeshRenderer source;  // the imported mesh, hidden: its skinning places the rigid vertices (waistband)
            public MeshFilter filter;           // the rebuilt mesh's renderer
            public TextAsset binding;           // per vertex (DoaPhysicsBuilder): rigid, or the 4x4 patch, its weights, depth, coefficients
        }

        public string source;
        public List<Grid> grids = new List<Grid>();
        public List<Surface> surfaces = new List<Surface>();
        public List<Twist> twists = new List<Twist>();
        public List<Collider> colliders = new List<Collider>();
        public List<Group> groups = new List<Group>();
        public List<Chain> chains = new List<Chain>();
        public List<Swing> swings = new List<Swing>();
        public List<Soft> softs = new List<Soft>();
        public List<Attachment> attachments = new List<Attachment>();

        /// <summary>
        /// The twist helpers on the pose just made: each takes its limb bone's swing and its share of the limb's twist
        /// about the limb (the upper arm's twist split 0 / 1/2 / all from the shoulder to the elbow, the thigh's from
        /// the hip to the knee; the NoTwist ones none).  Left on their parents they turned with all of it, and the chest
        /// wall of her soft-body breasts, partly skinned to the shoulder end, turned away with every roll of the arm.
        /// </summary>
        public void DriveHelpers()
        {
            foreach (var t in twists)
            {
                if (t.bone == null || t.source == null)
                    continue;
                var r = Quaternion.Inverse(t.sourceRest) * t.source.localRotation;
                var along = Vector3.Project(new Vector3(r.x, r.y, r.z), t.axis);
                var twist = new Quaternion(along.x, along.y, along.z, r.w);
                float m = Mathf.Sqrt(twist.x * twist.x + twist.y * twist.y + twist.z * twist.z + twist.w * twist.w);
                twist = m > 1e-6f ? new Quaternion(twist.x / m, twist.y / m, twist.z / m, twist.w / m) : Quaternion.identity;
                t.bone.rotation = t.source.rotation * Quaternion.Inverse(twist) * Quaternion.Slerp(Quaternion.identity, twist, t.share) * t.restToSource;
            }
        }

        /// <summary>
        /// Her grid cloth's skinned rows (the top two in the game's data) placed on their bones as the game skins them, in
        /// any setup: top row first, each column hanging from them, so the rows below come along.  The DOA6 solver places
        /// them again itself; a bone solver swings the columns from them; with none the rest hangs as it is.
        /// </summary>
        public void DriveGrids()
        {
            foreach (var g in grids)
            {
                if (g.targetStart == null || g.cps == null)
                    continue;
                int fixedCount = Mathf.Min(g.skinnedRows * g.cols, g.cps.Length);
                for (int i = 0; i < fixedCount; i++)
                {
                    var t = Vector3.zero;
                    float w = 0f;
                    for (int k = g.targetStart[i]; k < g.targetStart[i + 1]; k++)
                    {
                        t += g.targetWeight[k] * g.targetBone[k].TransformPoint(g.targetLocal[k]);
                        w += g.targetWeight[k];
                    }
                    if (w > 0f && g.cps[i] != null)
                        g.cps[i].position = t / w;
                }
            }
        }

        // ---- the cloth surfaces

        class SurfaceState
        {
            public Mesh mesh;
            public bool[] rigid;
            public int[] cp;                // 16 per vertex
            public float[] w;               // 16 per vertex: w_h[4] w_v[4] dw_h[4] dw_v[4]
            public float[] depth;           // per vertex, for metres (the game's x 100)
            public Vector3[] nCoef;
            public Vector4[] tCoef;
            public Vector3[] restPos, restNormal;
            public Vector4[] restTangent;
            public BoneWeight[] skin;
            public Matrix4x4[] bind;
            public Vector3[] pos, normal;
            public Vector4[] tangent;
            public Matrix4x4[] skinMatrix;
        }

        [NonSerialized] List<SurfaceState> surfaceStates;
        [NonSerialized] Vector3[] cpWorld = new Vector3[0];

        /// <summary>
        /// The binding a cloth surface's vertices have (DoaPhysicsBuilder writes it): per vertex an int (0 rigid, 1 rebuilt),
        /// then for every vertex 16 shorts (control points, 4 rows of 4), 16 floats (w_h, w_v, dw_h, dw_v), the depth (the
        /// game's, per cm^2 of patch), 3 + 4 floats (normal and tangent coefficients).
        /// </summary>
        public static byte[] WriteBinding(bool[] rigid, int[] cp, float[] w, float[] depth, Vector3[] nCoef, Vector4[] tCoef)
        {
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            bw.Write(0x44525247);   // GRRD
            bw.Write(1);
            bw.Write(rigid.Length);
            foreach (var r in rigid)
                bw.Write(r ? 0 : 1);
            for (int v = 0; v < rigid.Length; v++)
            {
                for (int k = 0; k < 16; k++)
                    bw.Write((short)cp[16 * v + k]);
                for (int k = 0; k < 16; k++)
                    bw.Write(w[16 * v + k]);
                bw.Write(depth[v]);
                bw.Write(nCoef[v].x); bw.Write(nCoef[v].y); bw.Write(nCoef[v].z);
                bw.Write(tCoef[v].x); bw.Write(tCoef[v].y); bw.Write(tCoef[v].z); bw.Write(tCoef[v].w);
            }
            return ms.ToArray();
        }

        SurfaceState Load(Surface s)
        {
            var st = new SurfaceState();
            using var br = new BinaryReader(new MemoryStream(s.binding.bytes));
            if (br.ReadInt32() != 0x44525247 || br.ReadInt32() != 1)
                throw new InvalidDataException($"{s.name}: not a cloth surface binding");
            int n = br.ReadInt32();
            st.rigid = new bool[n];
            for (int v = 0; v < n; v++)
                st.rigid[v] = br.ReadInt32() == 0;
            st.cp = new int[16 * n];
            st.w = new float[16 * n];
            st.depth = new float[n];
            st.nCoef = new Vector3[n];
            st.tCoef = new Vector4[n];
            for (int v = 0; v < n; v++)
            {
                for (int k = 0; k < 16; k++)
                    st.cp[16 * v + k] = br.ReadInt16();
                for (int k = 0; k < 16; k++)
                    st.w[16 * v + k] = br.ReadSingle();
                // the game's formula is in cm: in metres b and c are 1/100 of it and d = b x c 1/10000, so whatever multiplies d
                // (the depth, and d's share of the normal and the tangent) takes x 100 to keep the same mix
                st.depth[v] = br.ReadSingle() * 100f;
                st.nCoef[v] = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle() * 100f);
                st.tCoef[v] = new Vector4(br.ReadSingle(), br.ReadSingle(), br.ReadSingle() * 100f, br.ReadSingle());
            }
            var src = s.source.sharedMesh;
            st.restPos = src.vertices;
            st.restNormal = src.normals;
            st.restTangent = src.tangents.Length == st.restPos.Length ? src.tangents : new Vector4[st.restPos.Length];
            st.skin = src.boneWeights;
            st.bind = src.bindposes;
            st.skinMatrix = new Matrix4x4[st.bind.Length];
            st.mesh = Instantiate(s.filter.sharedMesh);
            st.mesh.name = s.filter.sharedMesh.name;
            st.mesh.MarkDynamic();
            s.filter.sharedMesh = st.mesh;
            st.pos = new Vector3[n];
            st.normal = new Vector3[n];
            st.tangent = new Vector4[n];
            return st;
        }

        /// <summary>
        /// The visible cloth on the control points as they are now (simulated, swung by a bone solver, or at rest), the
        /// game's way (G1M mesh type 1, ripper_tpose docs/doa-clothing-and-cloth.md):
        ///   u_k = sum_j w_h[j] CP[k][j], v_k = sum_j dw_h[j] CP[k][j] (4 rows k of 4 points j)
        ///   a = sum_k w_v[k] u_k, b = sum_k dw_v[k] u_k, c = sum_k w_v[k] v_k, d = b x c (not normalised)
        ///   position = a + d * depth, normal = normalise(c n.x + b n.y + d n.z), the tangent likewise.
        /// Her model is the game's mirrored (x), which turns a cross product round: d = c x b here.  The tangent's
        /// coefficients are fitted to the imported tangents (DoaPhysicsBuilder), the frame her normal maps are read in.  The
        /// rigid vertices (the waistband) are skinned as imported.  Called after the cloth each step (FighterRig), and for
        /// stills (DoaFighter.CheckSurfaces measures a rebuild at rest against the import).
        /// </summary>
        public void RebuildSurfaces()
        {
            if (surfaces.Count == 0)
                return;
            if (surfaceStates == null)
            {
                surfaceStates = new List<SurfaceState>();
                foreach (var s in surfaces)
                    surfaceStates.Add(s.binding != null && s.source != null && s.filter != null ? Load(s) : null);
            }
            for (int si = 0; si < surfaces.Count; si++)
            {
                var s = surfaces[si];
                var st = surfaceStates[si];
                if (st == null || s.grid < 0 || s.grid >= grids.Count)
                    continue;
                var g = grids[s.grid];
                if (cpWorld.Length < g.cps.Length)
                    cpWorld = new Vector3[g.cps.Length];
                for (int i = 0; i < g.cps.Length; i++)
                    cpWorld[i] = g.cps[i].position;
                var bones = s.source.bones;
                for (int b = 0; b < st.bind.Length; b++)
                    st.skinMatrix[b] = bones[b] != null ? bones[b].localToWorldMatrix * st.bind[b] : Matrix4x4.identity;
                var toLocal = s.filter.transform.worldToLocalMatrix;
                int n = st.rigid.Length;
                for (int v = 0; v < n; v++)
                {
                    if (st.rigid[v])
                    {
                        var bw = st.skin[v];
                        var m = Blend(st.skinMatrix, bw);
                        st.pos[v] = toLocal.MultiplyPoint3x4(m.MultiplyPoint3x4(st.restPos[v]));
                        st.normal[v] = toLocal.MultiplyVector(m.MultiplyVector(st.restNormal[v])).normalized;
                        var rt = st.restTangent[v];
                        var tt = toLocal.MultiplyVector(m.MultiplyVector(new Vector3(rt.x, rt.y, rt.z))).normalized;
                        st.tangent[v] = new Vector4(tt.x, tt.y, tt.z, rt.w);
                        continue;
                    }
                    Patch(cpWorld, st.cp, st.w, 16 * v, out var a, out var bb, out var c, out var d);
                    var nc = st.nCoef[v];
                    var tc = st.tCoef[v];
                    st.pos[v] = toLocal.MultiplyPoint3x4(a + d * st.depth[v]);
                    st.normal[v] = toLocal.MultiplyVector(c * nc.x + bb * nc.y + d * nc.z).normalized;
                    var t = toLocal.MultiplyVector(c * tc.x + bb * tc.y + d * tc.z).normalized;
                    st.tangent[v] = new Vector4(t.x, t.y, t.z, -tc.w);
                }
                st.mesh.SetVertices(st.pos);
                st.mesh.SetNormals(st.normal);
                st.mesh.SetTangents(st.tangent);
                st.mesh.RecalculateBounds();
            }
        }

        /// <summary>
        /// The game's 4x4 patch at one vertex: control points cp[o..o+15] (4 rows k of 4 points j) and weights w[o..o+15]
        /// (w_h, w_v, dw_h, dw_v) give the point a, the patch's derivatives b and c, and d = c x b (the game's b x c, turned
        /// round by her mirrored model).
        /// </summary>
        public static void Patch(Vector3[] points, int[] cp, float[] w, int o, out Vector3 a, out Vector3 b, out Vector3 c, out Vector3 d)
        {
            a = b = c = Vector3.zero;
            for (int k = 0; k < 4; k++)
            {
                Vector3 u = Vector3.zero, du = Vector3.zero;
                for (int j = 0; j < 4; j++)
                {
                    var p = points[cp[o + 4 * k + j]];
                    u += w[o + j] * p;              // w_h
                    du += w[o + 8 + j] * p;         // dw_h
                }
                a += w[o + 4 + k] * u;              // w_v
                b += w[o + 12 + k] * u;             // dw_v
                c += w[o + 4 + k] * du;
            }
            d = Vector3.Cross(c, b);
        }

        static Matrix4x4 Blend(Matrix4x4[] m, BoneWeight w)
        {
            var r = new Matrix4x4();
            void Add(int i, float x)
            {
                if (x <= 0f)
                    return;
                var a = m[i];
                for (int k = 0; k < 16; k++)
                    r[k] += a[k] * x;
            }
            Add(w.boneIndex0, w.weight0);
            Add(w.boneIndex1, w.weight1);
            Add(w.boneIndex2, w.weight2);
            Add(w.boneIndex3, w.weight3);
            return r;
        }

        /// <summary>
        /// Her grid cloths as sheets for a bone solver (RoeClothScope.anchorOf / neighbours): every column below the skinned
        /// rows hangs on the grid's parent as one piece, and each column's first point has the first points of the columns
        /// left and right of it (a skirt's ring closes) - the skin cannot tell, her cloth is rebuilt, not skinned to them.
        /// </summary>
        public (Dictionary<Transform, Transform> anchorOf, Dictionary<Transform, Transform[]> neighbours) Sheets()
        {
            var anchorOf = new Dictionary<Transform, Transform>();
            var neighbours = new Dictionary<Transform, Transform[]>();
            foreach (var g in grids)
            {
                if (g.cps == null || g.parent == null || g.skinnedRows >= g.rows)
                    continue;
                for (int i = g.skinnedRows * g.cols; i < g.cps.Length; i++)
                    if (g.cps[i] != null)
                        anchorOf[g.cps[i]] = g.parent;
                int top = g.skinnedRows * g.cols;
                for (int c = 0; c < g.cols; c++)
                {
                    int i = top + c;
                    var near = new List<Transform>();
                    foreach (int k in new[] { 0, 1 })   // left, right
                    {
                        int j = g.links != null && g.links.Length > 4 * i + k ? g.links[4 * i + k] : -1;
                        if (j >= 0 && j < g.cps.Length && g.cps[j] != null && j != i)
                            near.Add(g.cps[j]);
                    }
                    if (g.cps[i] != null)
                        neighbours[g.cps[i]] = near.Distinct().ToArray();
                }
            }
            return (anchorOf, neighbours);
        }

        /// <summary>Her loose bones by kind (for the bone cloth, which tells kinds by bone names otherwise).</summary>
        public Dictionary<Transform, string> KindsOf()
        {
            var kindOf = new Dictionary<Transform, string>();
            // a chain's first bone stays on its parent but turns towards the next one, like the rest
            foreach (var c in chains)
                foreach (var b in c.bones)
                    if (b != null)
                        kindOf[b] = c.kind;
            foreach (var s in swings)
                if (s.bone != null)
                    kindOf[s.bone] = "hair";
            // grid cloth for a bone solver: each column below the skinned rows a chain hanging from them
            foreach (var g in grids)
                for (int i = g.skinnedRows * g.cols; i < g.cps.Length; i++)
                    if (g.cps[i] != null)
                        kindOf[g.cps[i]] = g.kind;
            // a breast for a bone solver: the pivot on the chest wall and the breast bone below it, swung as one
            foreach (var s in softs)
                if (s.parent != null && s.parent.parent != null && s.parent.parent.name.EndsWith("_pivot"))
                {
                    kindOf[s.parent.parent] = s.kind;
                    kindOf[s.parent] = s.kind;
                }
            return kindOf;
        }
    }
}
