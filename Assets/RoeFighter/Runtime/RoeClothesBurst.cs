using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RoeFighter
{
    /// <summary>
    /// 爆衣 (clothes burst), step 1: the outer pieces of the outfit come off in stages and fly or drop to
    /// the floor; nothing underneath is changed (only pieces with the whole body under them take part).
    ///
    /// The editor (RoeBurstBuilder, rules in Editor/Burst/&lt;id&gt;.json) splits the outfit: every piece that
    /// can come off is a SkinnedMeshRenderer of its own on the same bones as the body, so it looks exactly
    /// as before; the rest of the outfit stays on the original renderer.  Drop() takes the next stage off:
    /// each of its pieces is baked in its pose of that moment (BakeMesh, on the CPU), its renderer goes off
    /// and the snapshot moves on as a rigid body of its own, stepped by the fight (Step, 60 Hz, nothing runs
    /// by itself; the random numbers come from the match seed, so a recorded match is the same each time):
    /// gravity, air drag, bounces and friction on the floor at the fighter's feet, the edge of the arena.
    /// Armour flies off along the blow and tumbles; cloth falls slowly and lies down flat where it lands.
    /// Then it fades out with the characters' own dither fade (_IGNOpacity) and is gone.  Restore() puts
    /// everything back on (a new match).
    /// </summary>
    public class RoeClothesBurst : MonoBehaviour
    {
        [Serializable]
        public class Piece
        {
            public string name;                   // "long skirt panels L"
            public string group;                  // "long skirt panels"
            public string title;                  // the group as shown to people ("长裙片")
            public int stage = 1;                 // 1 comes off first
            public bool cloth;                    // drops and lies down; otherwise armour: flies and tumbles
            public bool fade;                     // paint on the skin: fades out on the body where it is (fadeInPlace), nothing flies
            public SkinnedMeshRenderer renderer;  // on the body's bones; off once the piece has come off
            public int triangles;
        }

        /// <summary>
        /// A part of the body under the outfit drawn only once a stage is off (RoeNudeBody, "reveal": a nude base that does not
        /// fit under the outfit - Eve's is fuller than her suits and would come out through them).
        /// </summary>
        [Serializable]
        public class Reveal
        {
            public int stage;                     // drawn once this stage is off
            public SkinnedMeshRenderer renderer;
            public int triangles;
        }

        public List<Piece> pieces = new List<Piece>();
        public List<Reveal> reveals = new List<Reveal>();
        public int stages;                        // how many stages this outfit has

        [Header("Armour: flies off along the blow and tumbles")]
        public Vector2 armourHitSpeed = new Vector2(2f, 4f);       // m/s along the blow (random in this range)
        public Vector2 armourOutSpeed = new Vector2(0.8f, 1.6f);   // away from the body
        public Vector2 armourUpSpeed = new Vector2(1.6f, 2.8f);
        public Vector2 armourSpin = new Vector2(4f, 10f);           // rad/s about a random axis
        public float armourBounce = 0.3f, armourFriction = 0.5f;
        public float armourDrag = 0.15f;                            // 1/s

        [Header("Cloth: drops, tumbles slowly, lies down flat")]
        public Vector2 clothHitSpeed = new Vector2(0.8f, 1.8f);
        public Vector2 clothOutSpeed = new Vector2(0.4f, 1.0f);
        public Vector2 clothUpSpeed = new Vector2(0.6f, 1.4f);
        public Vector2 clothSpin = new Vector2(1f, 3f);
        public float clothFriction = 0.9f;
        public float clothDrag = 2.2f;                              // 1/s: falls at about gravity / drag at most
        public float clothLieDown = 0.45f;                          // seconds to settle flat once it touches the floor
        public float clothFolds = 0.025f;                           // m: how far its folds stand up from the floor

        [Header("Both")]
        public float gravity = 9.81f;
        public float life = 2.6f;                                   // seconds on screen before the fade (cloth 0.8 s more)
        public float fade = 0.5f;
        public float stagger = 0.3f;                                // a stage dropped while another is due waits this long
        public float fadeInPlace = 0.8f;                            // s: paint pieces (Piece.fade) fading out on the body

        /// <summary>Burst on or off for every fighter (F6 in the fight, -roeBurst 0/1).</summary>
        public static bool Enabled = true;

        /// <summary>Called for every piece that comes off: where, how big (m), cloth or not - for a spark and a sound.</summary>
        [NonSerialized] public Action<Vector3, float, bool> onPieceOff;

        /// <summary>Stages taken off so far (and due).</summary>
        public int Dropped => dropped + due.Count;
        public int Flying => debris.Count;

        /// <summary>For checks: how long each stage took to come off (ms): all of it, and the snapshots alone (BakeMesh + copies).</summary>
        public readonly List<float> takeOffMs = new List<float>(), snapshotMs = new List<float>();

        int dropped;
        System.Random random = new System.Random(1);
        float clock;
        readonly List<(float at, Vector3 dir, float floor)> due = new List<(float, Vector3, float)>();
        readonly List<Debris> debris = new List<Debris>();
        readonly List<(Piece piece, float age)> fading = new List<(Piece, float)>();
        static readonly int Opacity = Shader.PropertyToID("_IGNOpacity");
        static Transform holder;

        class Debris
        {
            public GameObject go;
            public Mesh mesh;
            public MeshRenderer renderer;
            public MaterialPropertyBlock block;
            public bool cloth, dither;
            public Vector3 v, w;                 // world: linear (m/s) and angular (rad/s) velocity
            public Vector3[] hull;               // a few far-out vertices, local: the floor contacts
            public float radius, invInertia;     // m; 1 / (inertia / mass), from the size
            public float age, still, lie = -1f, floor, scale = 1f;
            public Vector3[] restVertices, restNormals;    // cloth: as baked (local), and how high each fold stands when lying
            public float[] lift;
            public Vector3[] vertices, normals;
        }

        /// <summary>Everything back on, nothing flying; the random numbers start again from this seed.</summary>
        public void Restore(int seed)
        {
            ClearDebris();
            StopFading();
            Show(0);
            dropped = 0;
            due.Clear();
            clock = 0f;
            takeOffMs.Clear();
            snapshotMs.Clear();
            random = new System.Random(seed);
        }

        /// <summary>
        /// The outfit as it is once this many stages are off, at once and with nothing flying (checks and pictures; 0 = all on,
        /// stages = all off): the pieces of those stages hidden, the body under them shown.
        /// </summary>
        public void Show(int stagesOff)
        {
            StopFading();
            foreach (var p in pieces)
                if (p.renderer != null)
                    p.renderer.enabled = p.stage > stagesOff;
            foreach (var r in reveals)
                if (r.renderer != null)
                    r.renderer.enabled = r.stage <= stagesOff;
        }

        /// <summary>The pieces in the air or on the floor go away at once (a new round).</summary>
        public void ClearDebris()
        {
            foreach (var d in debris)
                Kill(d);
            debris.Clear();
        }

        /// <summary>
        /// Take the next stage off (at the next Step, or a little later when another stage is still due).
        /// dir: the way the blow went (flat); floor: the height of the floor under the fighter.
        /// False when burst is off or nothing is left.
        /// </summary>
        public bool Drop(Vector3 dir, float floor)
        {
            if (!Enabled || Dropped >= stages)
                return false;
            float at = due.Count > 0 ? due[due.Count - 1].at + stagger : clock;
            due.Add((at, dir, floor));
            return true;
        }

        /// <summary>One step of the fight (dt 0 during a hit stop): due stages come off, the pieces move.</summary>
        public void Step(float dt, Vector3 arenaCentre, float arenaRadius)
        {
            clock += dt;
            while (due.Count > 0 && due[0].at <= clock + 1e-5f)
            {
                var d = due[0];
                due.RemoveAt(0);
                dropped++;
                TakeOff(dropped, d.dir, d.floor);
            }
            if (dt <= 0f)
                return;
            for (int i = 0; i < fading.Count; i++)
            {
                var (p, age) = fading[i];
                age += dt;
                if (p.renderer == null || age >= fadeInPlace)
                {
                    EndFade(p);
                    fading.RemoveAt(i--);
                    continue;
                }
                fading[i] = (p, age);
                SetPieceOpacity(p, 1f - age / fadeInPlace);
            }
            for (int i = 0; i < debris.Count; i++)
            {
                var d = debris[i];
                if (d.go == null || !Move(d, dt, arenaCentre, arenaRadius))
                {
                    Kill(d);
                    debris.RemoveAt(i--);
                }
            }
        }

        // ---- taking a stage off

        void TakeOff(int stage, Vector3 dir, float floor)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            double snapshots = 0;
            dir.y = 0f;
            dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : transform.forward;
            var hips = transform.position;
            var animator = GetComponent<Animator>();
            if (animator != null && animator.isHuman && animator.GetBoneTransform(HumanBodyBones.Hips) != null)
                hips = animator.GetBoneTransform(HumanBodyBones.Hips).position;
            if (holder == null)
            {
                var go = GameObject.Find("Burst Debris") ?? new GameObject("Burst Debris");
                go.hideFlags = HideFlags.DontSave;
                holder = go.transform;
            }
            foreach (var p in pieces)
            {
                if (p.stage != stage || p.renderer == null || !p.renderer.enabled)
                    continue;
                if (p.fade)
                {
                    fading.Add((p, 0f));
                    continue;
                }
                double before = watch.Elapsed.TotalMilliseconds;
                var d = Snapshot(p);
                snapshots += watch.Elapsed.TotalMilliseconds - before;
                p.renderer.enabled = false;
                if (d == null)
                    continue;
                d.floor = floor;
                var at = d.go.transform.position;
                var outward = at - hips;
                outward.y = 0f;
                outward = outward.sqrMagnitude > 1e-6f ? outward.normalized : dir;
                d.v = dir * Range(p.cloth ? clothHitSpeed : armourHitSpeed)
                    + outward * Range(p.cloth ? clothOutSpeed : armourOutSpeed)
                    + Vector3.up * Range(p.cloth ? clothUpSpeed : armourUpSpeed);
                d.w = RandomAxis() * Range(p.cloth ? clothSpin : armourSpin);
                debris.Add(d);
                onPieceOff?.Invoke(at, d.radius, p.cloth);
            }
            foreach (var r in reveals)
                if (r.stage == stage && r.renderer != null)
                    r.renderer.enabled = true;
            takeOffMs.Add((float)watch.Elapsed.TotalMilliseconds);
            snapshotMs.Add((float)snapshots);
        }

        static MaterialPropertyBlock fadeBlock;

        static void SetPieceOpacity(Piece p, float opacity)
        {
            fadeBlock = fadeBlock ?? new MaterialPropertyBlock();
            p.renderer.GetPropertyBlock(fadeBlock);
            fadeBlock.SetFloat(Opacity, Mathf.Clamp01(opacity));
            p.renderer.SetPropertyBlock(fadeBlock);
        }

        static void EndFade(Piece p)
        {
            if (p.renderer == null)
                return;
            p.renderer.enabled = false;
            p.renderer.SetPropertyBlock(null);
        }

        /// <summary>Paint still fading goes at once (a new match, or Show).</summary>
        void StopFading()
        {
            foreach (var (p, _) in fading)
                if (p.renderer != null)
                    p.renderer.SetPropertyBlock(null);
            fading.Clear();
        }

        /// <summary>The piece as it looks now, as a mesh of its own around its centre.</summary>
        Debris Snapshot(Piece p)
        {
            var mesh = new Mesh { name = "burst " + p.name };
            p.renderer.BakeMesh(mesh, true);
            var vertices = mesh.vertices;
            if (vertices.Length == 0)
            {
                DestroyAny(mesh);
                return null;
            }
            var centre = Vector3.zero;
            foreach (var v in vertices)
                centre += v;
            centre /= vertices.Length;
            for (int i = 0; i < vertices.Length; i++)
                vertices[i] -= centre;
            mesh.vertices = vertices;
            mesh.RecalculateBounds();

            var t = p.renderer.transform;
            var go = new GameObject("burst " + p.name) { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(holder, false);
            go.transform.SetPositionAndRotation(t.position + t.rotation * centre, t.rotation);
            go.layer = p.renderer.gameObject.layer;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = p.renderer.sharedMaterials;
            r.shadowCastingMode = p.renderer.shadowCastingMode;
            r.receiveShadows = p.renderer.receiveShadows;
            r.lightProbeUsage = p.renderer.lightProbeUsage;
            r.probeAnchor = p.renderer.probeAnchor;

            var d = new Debris { go = go, mesh = mesh, renderer = r, cloth = p.cloth, block = new MaterialPropertyBlock() };
            d.dither = true;
            foreach (var m in r.sharedMaterials)
                d.dither &= m != null && m.HasProperty(Opacity);
            d.hull = Hull(vertices, out d.radius);
            // a plate or a shell more than a ball: inertia / mass about a third of the radius squared
            d.invInertia = 1f / Mathf.Max(0.3f * d.radius * d.radius, 1e-4f);
            if (p.cloth)
            {
                d.restVertices = vertices;
                d.restNormals = mesh.normals;
                if (d.restNormals.Length != vertices.Length)
                {
                    d.restNormals = new Vector3[vertices.Length];
                    for (int i = 0; i < vertices.Length; i++)
                        d.restNormals[i] = Vector3.up;
                }
                d.vertices = new Vector3[vertices.Length];
                d.normals = new Vector3[vertices.Length];
                // the folds of a cloth lying on the floor: gentle waves over its own shape, a few centimetres high
                d.lift = new float[vertices.Length];
                float ph = (float)random.NextDouble() * 6.283f;
                for (int i = 0; i < vertices.Length; i++)
                {
                    var v = vertices[i];
                    float wave = 0.5f + 0.5f * Mathf.Sin(v.x * 17f + ph) * Mathf.Sin(v.y * 13f + v.z * 11f + ph * 0.7f);
                    d.lift[i] = 0.004f + clothFolds * wave;
                }
            }
            return d;
        }

        /// <summary>Up to 40 vertices that stick out most (along 26 directions, plus a spread of the rest).</summary>
        static Vector3[] Hull(Vector3[] v, out float radius)
        {
            var picked = new HashSet<int>();
            radius = 0f;
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if (dx == 0 && dy == 0 && dz == 0)
                            continue;
                        var dir = new Vector3(dx, dy, dz).normalized;
                        int best = 0;
                        float far = float.NegativeInfinity;
                        for (int i = 0; i < v.Length; i++)
                        {
                            float s = Vector3.Dot(v[i], dir);
                            if (s > far)
                            {
                                far = s;
                                best = i;
                            }
                        }
                        picked.Add(best);
                    }
            int step = Mathf.Max(1, v.Length / 14);
            for (int i = 0; i < v.Length; i += step)
                picked.Add(i);
            var hull = new Vector3[picked.Count];
            int k = 0;
            foreach (int i in picked)
            {
                hull[k++] = v[i];
                radius = Mathf.Max(radius, v[i].magnitude);
            }
            return hull;
        }

        // ---- moving the pieces

        /// <summary>False once the piece is gone.</summary>
        bool Move(Debris d, float dt, Vector3 arenaCentre, float arenaRadius)
        {
            d.age += dt;
            var t = d.go.transform;
            bool lying = d.lie >= 0f;
            d.v += Vector3.down * (gravity * dt);
            d.v *= Mathf.Exp(-(d.cloth ? clothDrag : armourDrag) * dt);
            d.w *= Mathf.Exp(-(d.cloth ? 1.5f : 0.3f) * dt);
            if (lying)
            {
                // a cloth on the floor slides to a stop and does not turn any more
                d.v = new Vector3(d.v.x * Mathf.Exp(-5f * dt), d.v.y, d.v.z * Mathf.Exp(-5f * dt));
                d.w *= Mathf.Exp(-12f * dt);
            }
            t.position += d.v * dt;
            if (d.w.sqrMagnitude > 1e-8f)
                t.rotation = Quaternion.AngleAxis(d.w.magnitude * dt * Mathf.Rad2Deg, d.w.normalized) * t.rotation;

            bool touched = Floor(d, t);
            // the arena's edge: it bounces back softly
            var r = t.position - arenaCentre;
            r.y = 0f;
            if (arenaRadius > 0f && r.magnitude > arenaRadius)
            {
                var n = r.normalized;
                t.position = new Vector3(arenaCentre.x, t.position.y, arenaCentre.z) + n * arenaRadius;
                float out_ = Vector3.Dot(d.v, n);
                if (out_ > 0f)
                    d.v -= n * (out_ * 1.3f);
            }
            if (d.cloth && touched && d.lie < 0f)
            {
                d.lie = 0f;
                d.w *= 0.2f;
            }
            if (d.cloth && d.lie >= 0f && d.lie < 1f)
            {
                d.lie = Mathf.Min(1f, d.lie + dt / Mathf.Max(0.05f, clothLieDown));
                LieDown(d, t, d.lie * d.lie * (3f - 2f * d.lie));
            }
            // asleep once it has stopped on the floor
            if (touched && d.v.magnitude < 0.06f && d.w.magnitude < 0.4f)
                d.still += dt;
            else
                d.still = 0f;
            if (d.still > 0.2f)
            {
                d.v = Vector3.zero;
                d.w = Vector3.zero;
            }

            float end = life + (d.cloth ? 0.8f : 0f);
            if (d.age > end + fade)
                return false;
            if (d.age > end)
                SetOpacity(d, 1f - (d.age - end) / Mathf.Max(0.01f, fade));
            return true;
        }

        /// <summary>Contacts of the hull with the floor: impulses with bounce and friction, then out of the floor.  True if it touched.</summary>
        bool Floor(Debris d, Transform t)
        {
            float bounce = d.cloth ? 0f : armourBounce, mu = d.cloth ? clothFriction : armourFriction;
            float deepest = 0f;
            bool touched = false;
            var x = t.position;
            var q = t.rotation;
            for (int pass = 0; pass < 2; pass++)
                foreach (var h in d.hull)
                {
                    var r = q * h;
                    float below = d.floor - (x.y + r.y);
                    if (below < -0.002f)
                        continue;
                    touched = true;
                    deepest = Mathf.Max(deepest, below);
                    var vp = d.v + Vector3.Cross(d.w, r);
                    if (vp.y >= 0f)
                        continue;
                    var rn = Vector3.Cross(r, Vector3.up);
                    float jn = -(1f + bounce) * vp.y / (1f + d.invInertia * rn.sqrMagnitude);
                    var impulse = Vector3.up * jn;
                    var vt = new Vector3(vp.x, 0f, vp.z);
                    float slide = vt.magnitude;
                    if (slide > 1e-5f)
                    {
                        var along = vt / slide;
                        var rt = Vector3.Cross(r, along);
                        impulse -= along * Mathf.Min(mu * jn, slide / (1f + d.invInertia * rt.sqrMagnitude));
                    }
                    d.v += impulse;
                    d.w += d.invInertia * Vector3.Cross(r, impulse);
                }
            if (deepest > 0f)
                t.position = x + Vector3.up * deepest;
            return touched;
        }

        /// <summary>A cloth settles flat on the floor: each vertex goes down to the height of its fold (s 0..1).</summary>
        void LieDown(Debris d, Transform t, float s)
        {
            var toWorld = t.localToWorldMatrix;
            var toLocal = t.worldToLocalMatrix;
            for (int i = 0; i < d.restVertices.Length; i++)
            {
                var p = toWorld.MultiplyPoint3x4(d.restVertices[i]);
                var n = toWorld.MultiplyVector(d.restNormals[i]);
                // the side that faced up stays up, the other one down (2 mm under it: two-sided panels are two layers)
                bool up = n.y >= 0f;
                p.y = Mathf.Lerp(p.y, d.floor + d.lift[i] + (up ? 0.002f : 0f), s);
                d.vertices[i] = toLocal.MultiplyPoint3x4(p);
                var flat = up ? Vector3.up : Vector3.down;
                d.normals[i] = toLocal.MultiplyVector(Vector3.Slerp(n.normalized, flat, s)).normalized;
            }
            d.mesh.vertices = d.vertices;
            d.mesh.normals = d.normals;
            d.mesh.RecalculateBounds();
        }

        void SetOpacity(Debris d, float a)
        {
            a = Mathf.Clamp01(a);
            if (d.dither)
            {
                d.renderer.GetPropertyBlock(d.block);
                d.block.SetFloat(Opacity, a);
                d.renderer.SetPropertyBlock(d.block);
            }
            else
            {
                d.go.transform.localScale = Vector3.one * Mathf.Max(0.001f, a);
            }
        }

        void Kill(Debris d)
        {
            if (d.go != null)
                DestroyAny(d.go);
            if (d.mesh != null)
                DestroyAny(d.mesh);
        }

        static void DestroyAny(Object o)
        {
            if (Application.isPlaying)
                Destroy(o);
            else
                DestroyImmediate(o);
        }

        float Range(Vector2 r) => Mathf.Lerp(r.x, r.y, (float)random.NextDouble());

        Vector3 RandomAxis()
        {
            for (int k = 0; k < 16; k++)
            {
                var v = new Vector3((float)random.NextDouble() * 2f - 1f, (float)random.NextDouble() * 2f - 1f, (float)random.NextDouble() * 2f - 1f);
                if (v.sqrMagnitude > 0.05f && v.sqrMagnitude <= 1f)
                    return v.normalized;
            }
            return Vector3.right;
        }

        void OnDestroy()
        {
            ClearDebris();
        }

        /// <summary>For checks: the pieces of each stage.</summary>
        public string Report()
        {
            var parts = new List<string>();
            for (int s = 1; s <= stages; s++)
            {
                var names = new List<string>();
                int tris = 0;
                foreach (var p in pieces)
                    if (p.stage == s)
                    {
                        names.Add(p.name);
                        tris += p.triangles;
                    }
                int shown = 0;
                foreach (var r in reveals)
                    if (r.stage == s)
                        shown += r.triangles;
                parts.Add($"stage {s}: {names.Count} pieces, {tris} triangles ({string.Join(", ", names)})" +
                          (shown > 0 ? $", then the body under them ({shown} triangles)" : ""));
            }
            return string.Join("; ", parts);
        }
    }
}
