using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace RoeFighter
{
    /// <summary>
    /// Stellar Blade's own physics on Eve (user 10-05: "物理能用剑星自己的就用自己的"), with her game's settings
    /// (RoeSbRig, RoeKawaiiRig; tools/sb_physics.py; docs/stellar-blade-eve.md).  In the game's order, after the animation:
    ///   1. spring bones - UE 4.26's AnimNode_SpringBone (breasts, the hips' Ab-*-Hip-Reg, the thighs' twist bones, the
    ///      forearm bands): the bone's place in the fighter's own space (SB's bUseLocalSpace) is a mass on a spring towards
    ///      where the animation has it, integrated at UE's fixed 120 Hz: velocity += (stiffness x error - damping x
    ///      velocity) dt (the damping's safety scale over 1 / dt), clamped to ErrorResetThresh per step, moved, kept within
    ///      MaxDisplacement of the target; the bone moves there (its rotation is the animation's, turned by the
    ///      parent-to-bone swing on the axes the node allows);
    ///   2. the control rigs of her outfit's blueprint, run from their own byte code (RoeRigVM): the pleated skirt's
    ///      (Eve 37) turns each panel's root by how far the thigh under it is lifted, before the panels' KawaiiPhysics
    ///      nodes swing what hangs from it;
    ///   3. KawaiiPhysics nodes (RoeKawaiiPhysics with her game's settings): the hair's locks, the ponytail's first two
    ///      bones, the tie, the skirt's panels;
    ///   4. rigid bodies - PhysX, as the game's physics assets have them (Unity runs PhysX too): the ponytail from
    ///      Ab-TL-HairB03 down and the back panels, each body with the game's mass, linear and angular damping and
    ///      collision shapes, each joint with its cone and twist limits (a ConfigurableJoint: twist about the joint's
    ///      primary axis, swing 2 about the secondary, swing 1 about the third), against the game's kinematic bodies on
    ///      her limbs and trunk.  Simulated in her own space - the game's components have bLocalSpaceSimulation: walking,
    ///      being knocked back does not swing them, only her own motion does - in a physics scene of her own, stepped with
    ///      the fight.  The bones take the bodies' rotations.
    /// UE's axes: bone space (x, y, z) is (-x, -y, z) in her bones (as RoeKawaiiPhysics); her component axes are UE's
    /// turned (tools/sb_fbx.py turned her to face Unity's +z): UE x is our z, UE y our x, UE z our y.  Centimetres become
    /// metres.
    /// </summary>
    public class RoeSbPhysics : IRoeCloth, IDisposable
    {
        public static bool Covers(RoeClothScope scope, string kind)
        {
            var a = scope.animator;
            var sb = a != null ? a.GetComponent<RoeSbRig>() : null;
            if (sb == null)
                return false;
            var kw = a.GetComponent<RoeKawaiiRig>();
            return sb.Kinds().Contains(kind) || (kw != null && kw.Kinds().Contains(kind));
        }

        /// <summary>Whether the outfit's control rigs run (false: to film what they add - RoeClothDemo's "stellar_norig").</summary>
        public static bool UseControlRigs = true;

        /// <summary>UE's spring bones step at a fixed 120 Hz (AnimNode_SpringBone).</summary>
        public const float SpringStep = 1f / 120f;
        /// <summary>PhysX solver iterations per body (UE's BodyInstance defaults: 8 position, 1 velocity).</summary>
        public static int PositionIterations = 8, VelocityIterations = 1;
        /// <summary>Joint projection as UE's constraints default to it (5 cm; any angle).</summary>
        public static float ProjectionDistance = 0.05f, ProjectionAngle = 179f;

        class Spring
        {
            public RoeSbRig.Spring d;
            public Transform bone;
            public Vector3 restPosition;
            public Quaternion restRotation;
            public Vector3 location, velocity;
            public float remaining;
            public bool started;
        }

        readonly Transform space;
        readonly RoeSbRig rig;
        readonly RoeKawaiiPhysics kawaii;
        readonly List<Spring> springs = new List<Spring>();
        readonly List<(RoeRigVM vm, RoeRigVM.UnityBones bones)> rigs = new List<(RoeRigVM, RoeRigVM.UnityBones)>();
        readonly Rigid rigid;
        float weight = 1f;
        readonly string report;

        /// <summary>What it runs; with a control rig, how far the rig has turned the bones it sets since the last Reset.</summary>
        public string Report => report + string.Concat(rigs.Select(r =>
            $"; {r.vm.program.name} turned its bones up to {r.bones.turned:F0} deg" +
            (r.vm.program.skip.Count > 0 ? $", asked {string.Join(" ", r.vm.program.skip)} (left alone) for {r.bones.skippedMove:F1} cm {r.bones.skippedTurn:F1} deg" : "")));

        public float Weight
        {
            get => weight;
            set
            {
                weight = value;
                if (kawaii != null)
                    kawaii.Weight = value;
            }
        }

        public RoeSbPhysics(RoeClothScope scope)
        {
            var animator = scope.animator;
            space = scope.world != null ? scope.world : animator.transform;
            rig = animator.GetComponent<RoeSbRig>();
            var byName = new Dictionary<string, Transform>();
            foreach (var t in animator.GetComponentsInChildren<Transform>(true))
                if (!byName.ContainsKey(t.name))
                    byName[t.name] = t;
            var data = rig != null ? rig.Get() : new RoeSbRig.Data();
            if (animator.GetComponent<RoeKawaiiRig>() != null)
                kawaii = new RoeKawaiiPhysics(scope);
            foreach (var s in data.springs.Where(s => scope.Takes(s.kind)))
            {
                if (!byName.TryGetValue(s.bone, out var bone) || bone.parent == null)
                    continue;
                var (p, r) = rig.RestOf(bone);
                springs.Add(new Spring { d = s, bone = bone, restPosition = p, restRotation = r });
            }
            foreach (var p in data.rigs.Where(r => UseControlRigs && scope.Takes(r.kind)))
            {
                try
                {
                    var vm = new RoeRigVM(p);
                    rigs.Add((vm, new RoeRigVM.UnityBones(vm, space, byName, rig.RestOf)));
                }
                catch (Exception e) when (e is NotSupportedException || e is ArgumentException)
                {
                    Debug.LogWarning($"[ROE] Stellar Blade physics: control rig {p.name} left out - {e.Message}");
                }
            }
            var chains = data.bodies.Where(b => b.simulate && scope.Takes(RoeSbRig.KindOf(b.chain))).Select(b => b.chain).Distinct().ToList();
            if (chains.Count > 0)
                rigid = new Rigid(this, data, chains, byName);
            report = $"Stellar Blade's own: {springs.Count} spring bones ({string.Join(" ", springs.Select(s => s.bone.name))}); " +
                     (rigs.Count > 0 ? string.Join("; ", rigs.Select(r => $"control rig {r.vm.program.name} ({r.vm.program.code.Count} steps, sets " +
                                                                        $"{string.Join(" ", r.bones.Moved.Select(b => b.name))})")) + "; " : "") +
                     $"{(kawaii != null ? kawaii.Report : "no KawaiiPhysics")}; {(rigid != null ? rigid.Report : "no rigid bodies")}";
        }

        public void Rest()
        {
            foreach (var s in springs)
                s.bone.SetLocalPositionAndRotation(s.restPosition, s.restRotation);
            foreach (var r in rigs)
                r.bones.Rest();
            kawaii?.Rest();
            rigid?.Rest();
        }

        public void Reset()
        {
            foreach (var s in springs)
                s.started = false;
            foreach (var r in rigs)
                r.bones.turned = r.bones.skippedMove = r.bones.skippedTurn = 0f;
            kawaii?.Reset();
            rigid?.Reset();
        }

        public void Step(float dt, float floor)
        {
            if (dt <= 0f)
                return;
            foreach (var s in springs)
                StepSpring(s, dt);
            // the rigs set their bones from the pose as it is (they add to what they read: Rest put the bones back)
            foreach (var r in rigs)
                r.vm.Run(r.bones, Mathf.Clamp01(weight * r.vm.program.alpha));
            kawaii?.Step(dt, floor);
            rigid?.Step(dt);
        }

        public void Dispose() => rigid?.Dispose();

        // ---- UE's component axes in hers: UE x, y, z are our z, x, y (tools/sb_fbx.py's turn)

        static readonly int[] UeAxis = { 2, 0, 1 };

        static Quaternion ToUeComponent(Quaternion q) => new Quaternion(q.z, q.x, q.y, q.w);
        static Quaternion FromUeComponent(Quaternion q) => new Quaternion(q.y, q.z, q.x, q.w);

        void StepSpring(Spring s, float dt)
        {
            var d = s.d;
            var target = space.InverseTransformPoint(s.bone.position);
            if (!s.started)
            {
                s.location = target;
                s.velocity = Vector3.zero;
                s.remaining = 0f;
                s.started = true;
            }
            s.remaining += dt;
            float reset = d.errorResetThresh * 0.01f, max = d.maxDisplacement * 0.01f;
            while (s.remaining > SpringStep)
            {
                if ((target - s.location).sqrMagnitude > reset * reset)
                {
                    s.location = target;
                    s.velocity = Vector3.zero;
                }
                var acceleration = d.stiffness * (target - s.location) - d.damping * s.velocity;
                float cutoff = 1f / SpringStep;
                s.velocity += (d.damping > cutoff ? cutoff / d.damping : 1f) * acceleration * SpringStep;
                float speed = s.velocity.magnitude;
                if (speed * SpringStep > reset)
                    s.velocity *= reset / (speed * SpringStep);
                var old = s.location;
                s.location += s.velocity * SpringStep;
                // the axes it may move along (UE's component axes)
                for (int a = 0; a < 3; a++)
                    if (d.translate != null && a < d.translate.Length && !d.translate[a])
                        s.location[UeAxis[a]] = target[UeAxis[a]];
                if (d.limitDisplacement)
                {
                    var disp = s.location - target;
                    if (disp.sqrMagnitude > max * max)
                        s.location = target + disp.normalized * max;
                }
                s.velocity = (s.location - old) / SpringStep;
                s.remaining -= SpringStep;
            }
            float w = Mathf.Clamp01(weight * d.alpha);
            if (w <= 0f)
                return;
            var position = Vector3.Lerp(target, s.location, w);
            var rotation = s.bone.rotation;
            if (d.rotate != null && d.rotate.Any(x => x))
            {
                var parent = space.InverseTransformPoint(s.bone.parent.position);
                var toTarget = target - parent;
                var toCurrent = position - parent;
                if (toTarget.sqrMagnitude > 1e-12f && toCurrent.sqrMagnitude > 1e-12f)
                {
                    // FQuat::FindBetweenNormals, then only the Euler angles the node allows (UE's roll x, pitch y, yaw z)
                    var add = ToUeComponent(Quaternion.FromToRotation(toTarget, toCurrent));
                    var r = RoeUeRig.UeRotator(add);
                    var euler = new Vector3(d.rotate[1] ? r.x : 0f, d.rotate[2] ? r.y : 0f, d.rotate[0] ? r.z : 0f);
                    var turn = FromUeComponent(RoeUeRig.UeQuat(euler));
                    rotation = space.rotation * turn * Quaternion.Inverse(space.rotation) * rotation;
                }
            }
            s.bone.SetPositionAndRotation(space.TransformPoint(position), rotation);
        }

        // ---- the rigid bodies

        /// <summary>UE bone space to hers: (x, y, z) is (-x, -y, z); a rotation turns the same way (half a turn about z).</summary>
        static Vector3 BoneVector(float[] v) => v != null && v.Length == 3 ? new Vector3(-v[0], -v[1], v[2]) : Vector3.zero;
        static Quaternion BoneRotation(Quaternion q) => new Quaternion(-q.x, -q.y, q.z, q.w);

        class Rigid : IDisposable
        {
            class Body
            {
                public RoeSbRig.Body d;
                public Transform bone;
                public GameObject go;
                public Rigidbody rb;
                public readonly List<Collider> colliders = new List<Collider>();
                public Vector3 restPosition;
                public Quaternion restRotation;
            }

            readonly RoeSbPhysics owner;
            readonly Scene scene;
            readonly PhysicsScene physics;
            readonly bool preview;
            readonly List<Body> bodies = new List<Body>();
            readonly List<Body> simulated = new List<Body>();       // parents first
            public string Report { get; }

            static int made;

            public Rigid(RoeSbPhysics owner, RoeSbRig.Data data, List<string> chains, Dictionary<string, Transform> byName)
            {
                this.owner = owner;
                string name = $"roe_sb_physics_{owner.space.name}_{made++}";
#if UNITY_EDITOR
                if (!Application.isPlaying)
                {
                    scene = EditorSceneManager.NewPreviewScene();
                    preview = true;
                }
                else
#endif
                    scene = SceneManager.CreateScene(name, new CreateSceneParameters(LocalPhysicsMode.Physics3D));
                physics = scene.GetPhysicsScene();
                bool local = physics != Physics.defaultPhysicsScene;

                // bodies, at her pose now (fighter space)
                foreach (var d in data.bodies.Where(b => chains.Contains(b.chain)))
                {
                    if (!byName.TryGetValue(d.bone, out var bone))
                        continue;
                    var go = new GameObject($"{d.chain}:{d.bone}");
                    SceneManager.MoveGameObjectToScene(go, scene);
                    var (p, r) = owner.rig.RestOf(bone);
                    var b = new Body { d = d, bone = bone, go = go, restPosition = p, restRotation = r };
                    Place(b);
                    var rb = go.AddComponent<Rigidbody>();
                    rb.isKinematic = !d.simulate;
                    rb.useGravity = d.simulate;
                    if (d.mass > 0f)
                        rb.mass = d.mass;
                    rb.linearDamping = d.linearDamping;
                    rb.angularDamping = d.angularDamping;
                    rb.solverIterations = PositionIterations;
                    rb.solverVelocityIterations = VelocityIterations;
                    rb.maxAngularVelocity = 3600f * Mathf.Deg2Rad;       // UE's physics settings' MaxAngularVelocity
                    rb.interpolation = RigidbodyInterpolation.None;
                    b.rb = rb;
                    foreach (var s in d.shapes)
                    {
                        var sgo = new GameObject(s.type);
                        sgo.transform.SetParent(go.transform, false);
                        sgo.transform.localPosition = BoneVector(s.center) * 0.01f;
                        if (s.rotation != null && s.rotation.Length == 3)
                            sgo.transform.localRotation = BoneRotation(RoeUeRig.UeQuat(new Vector3(s.rotation[0], s.rotation[1], s.rotation[2])));
                        Collider c;
                        if (s.type == "sphere")
                            c = sgo.AddComponent<SphereCollider>().Also(x => x.radius = s.radius * 0.01f);
                        else if (s.type == "box")
                            c = sgo.AddComponent<BoxCollider>().Also(x => x.size = new Vector3(s.size[0], s.size[1], s.size[2]) * 0.01f);
                        else
                            c = sgo.AddComponent<CapsuleCollider>().Also(x =>
                            {
                                x.direction = 2;
                                x.radius = s.radius * 0.01f;
                                x.height = (s.length + 2f * s.radius) * 0.01f;
                            });
                        b.colliders.Add(c);
                    }
                    if (d.mass > 0f)
                        rb.mass = d.mass;           // after the colliders: they would set it from their volume
                    bodies.Add(b);
                }

                // joints, parents first: the child put where the game's constraint frames have it against its parent at
                // rest (frame 2 in the parent's space, frame 1 in the child's), then the joint made - its zero is that rest
                var byBone = bodies.GroupBy(b => (b.d.chain, b.d.bone)).ToDictionary(g => g.Key, g => g.First());
                var joints = data.joints.Where(j => chains.Contains(j.chain)).ToList();
                var placed = new HashSet<Body>(bodies.Where(b => !b.d.simulate));
                int made_ = 0;
                for (int guard = 0; guard < 64 && joints.Count > 0; guard++)
                {
                    foreach (var j in joints.ToList())
                    {
                        if (!byBone.TryGetValue((j.chain, j.child), out var child) || !byBone.TryGetValue((j.chain, j.parent), out var parent))
                        {
                            joints.Remove(j);
                            continue;
                        }
                        if (!placed.Contains(parent))
                            continue;
                        var frame2 = Frame(j.pos2, j.pri2, j.sec2);
                        var frame1 = Frame(j.pos1, j.pri1, j.sec1);
                        var rel = frame2 * frame1.inverse;              // the child body in the parent body's space, at rest
                        var pt = parent.go.transform;
                        child.go.transform.SetPositionAndRotation(pt.TransformPoint(rel.GetColumn(3)), pt.rotation * rel.rotation);
                        child.rb.position = child.go.transform.position;
                        child.rb.rotation = child.go.transform.rotation;
                        var cj = child.go.AddComponent<ConfigurableJoint>();
                        cj.connectedBody = parent.rb;
                        cj.anchor = frame1.GetColumn(3);
                        cj.axis = frame1.GetColumn(0);
                        cj.secondaryAxis = frame1.GetColumn(1);
                        cj.autoConfigureConnectedAnchor = true;
                        cj.xMotion = cj.yMotion = cj.zMotion = ConfigurableJointMotion.Locked;
                        cj.angularXMotion = Motion(j.twistMotion);
                        cj.angularYMotion = Motion(j.swing2Motion);
                        cj.angularZMotion = Motion(j.swing1Motion);
                        float twist = Mathf.Clamp(j.twist, 0f, 177f);
                        cj.lowAngularXLimit = new SoftJointLimit { limit = -twist };
                        cj.highAngularXLimit = new SoftJointLimit { limit = twist };
                        cj.angularYLimit = new SoftJointLimit { limit = Mathf.Clamp(j.swing2, 0f, 177f) };
                        cj.angularZLimit = new SoftJointLimit { limit = Mathf.Clamp(j.swing1, 0f, 177f) };
                        cj.projectionMode = JointProjectionMode.PositionAndRotation;
                        cj.projectionDistance = ProjectionDistance;
                        cj.projectionAngle = ProjectionAngle;
                        cj.enableCollision = false;
                        placed.Add(child);
                        joints.Remove(j);
                        made_++;
                    }
                }
                foreach (var b in bodies.Where(b => b.d.simulate && !placed.Contains(b)))
                    Debug.LogWarning($"[ROE] Stellar Blade physics: {b.d.bone} ({b.d.chain}) has no joint to a placed body - left kinematic");
                foreach (var b in bodies.Where(b => b.d.simulate && !placed.Contains(b)))
                {
                    b.rb.isKinematic = true;
                    b.d.simulate = false;
                }
                // parents first: by depth under the root
                simulated.AddRange(bodies.Where(b => b.d.simulate).OrderBy(b => Depth(b.bone)));

                // what collides: a simulated body with the colliders of its own chain that it does not touch at rest (the
                // game's physics assets leave those pairs out, as UE's physics asset editor does), not with the bodies of
                // its own chain (the joints keep them apart) nor with another chain's
                int ignored = 0, overlapping = 0;
                foreach (var a in bodies.Where(x => x.d.simulate))
                    foreach (var b in bodies)
                    {
                        if (a == b)
                            continue;
                        bool ignore = b.d.chain != a.d.chain || b.d.simulate;
                        if (!ignore)
                            foreach (var ca in a.colliders)
                                foreach (var cb in b.colliders)
                                    if (Physics.ComputePenetration(ca, ca.transform.position, ca.transform.rotation, cb, cb.transform.position, cb.transform.rotation, out _, out _))
                                    {
                                        ignore = true;
                                        overlapping++;
                                    }
                        if (ignore)
                            foreach (var ca in a.colliders)
                                foreach (var cb in b.colliders)
                                {
                                    Physics.IgnoreCollision(ca, cb, true);
                                    ignored++;
                                }
                    }
                Reset();
                Report = $"rigid bodies ({(local ? "her own physics scene" : "the SHARED physics scene")}, {(preview ? "edit mode" : "play mode")}): " +
                         string.Join(", ", chains.Select(c => $"{c} {bodies.Count(b => b.d.chain == c && b.d.simulate)} simulated / " +
                                                              $"{bodies.Count(b => b.d.chain == c && !b.d.simulate)} colliders")) +
                         $", {made_} joints, {overlapping} body pairs touching at rest left out";
            }

            static ConfigurableJointMotion Motion(string m) =>
                m == "ACM_Locked" ? ConfigurableJointMotion.Locked : m == "ACM_Limited" ? ConfigurableJointMotion.Limited : ConfigurableJointMotion.Free;

            /// <summary>A UE constraint frame (position, primary and secondary axes in a body's bone space) in her bone space.</summary>
            static Matrix4x4 Frame(float[] pos, float[] pri, float[] sec)
            {
                var x = BoneVector(pri).normalized;
                var y = BoneVector(sec);
                y = (y - Vector3.Dot(y, x) * x).normalized;
                var z = Vector3.Cross(x, y);
                var m = Matrix4x4.identity;
                m.SetColumn(0, x);
                m.SetColumn(1, y);
                m.SetColumn(2, z);
                var p = BoneVector(pos) * 0.01f;
                m.SetColumn(3, new Vector4(p.x, p.y, p.z, 1f));
                return m;
            }

            static int Depth(Transform t)
            {
                int d = 0;
                for (; t != null; t = t.parent)
                    d++;
                return d;
            }

            /// <summary>A body where her bone is now, in her space.</summary>
            void Place(Body b)
            {
                var s = owner.space;
                b.go.transform.SetPositionAndRotation(s.InverseTransformPoint(b.bone.position), Quaternion.Inverse(s.rotation) * b.bone.rotation);
            }

            public void Rest()
            {
                foreach (var b in simulated)
                    b.bone.SetLocalPositionAndRotation(b.restPosition, b.restRotation);
            }

            public void Reset()
            {
                foreach (var b in bodies)
                {
                    Place(b);
                    b.rb.position = b.go.transform.position;
                    b.rb.rotation = b.go.transform.rotation;
                    if (!b.rb.isKinematic)
                    {
                        b.rb.linearVelocity = Vector3.zero;
                        b.rb.angularVelocity = Vector3.zero;
                    }
                }
            }

            public void Step(float dt)
            {
                var s = owner.space;
                var inverse = Quaternion.Inverse(s.rotation);
                foreach (var b in bodies)
                    if (b.rb.isKinematic)
                    {
                        b.rb.MovePosition(s.InverseTransformPoint(b.bone.position));
                        b.rb.MoveRotation(inverse * b.bone.rotation);
                    }
                physics.Simulate(dt);
                float w = Mathf.Clamp01(owner.weight);
                if (w <= 0f)
                    return;
                foreach (var b in simulated)
                {
                    var r = s.rotation * b.rb.rotation;
                    b.bone.rotation = w < 1f ? Quaternion.Slerp(b.bone.rotation, r, w) : r;
                }
            }

            public void Dispose()
            {
                foreach (var b in bodies)
                    if (b.go != null)
                        UnityEngine.Object.DestroyImmediate(b.go);
#if UNITY_EDITOR
                if (preview)
                {
                    EditorSceneManager.ClosePreviewScene(scene);
                    return;
                }
#endif
                if (scene.IsValid() && scene.isLoaded)
                    SceneManager.UnloadSceneAsync(scene);
            }
        }
    }

    static class RoeSbExtensions
    {
        public static T Also<T>(this T x, Action<T> f)
        {
            f(x);
            return x;
        }
    }
}
