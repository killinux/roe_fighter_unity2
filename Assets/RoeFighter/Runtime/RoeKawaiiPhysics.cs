// The simulation in this file is a C# port of KawaiiPhysics' AnimNode_KawaiiPhysics.cpp (v1.14,
// https://github.com/pafuhana1213/KawaiiPhysics), under its license:
//
// MIT License
//
// Copyright (c) 2019-2022 pafuhana1213
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RoeFighter
{
    /// <summary>
    /// KawaiiPhysics (pafuhana1213, MIT; the solver of AnimNode_KawaiiPhysics.cpp v1.14 - the generation Vindictus' 2024
    /// build has) on a fighter, with her game's settings (RoeKawaiiRig): one "node" per root bone, every bone under the
    /// root a particle (the root follows the animation), each step in the fighter's own space ("component space"):
    ///   velocity = (position - previous) / previous dt, times (1 - damping), moved by;
    ///   plus (1 - world damping) of the fighter's own move and turn since the last step (so 0.8 keeps 80 % of the
    ///   body's movement out of the cloth's inertia);
    ///   gravity (only the skirt has any: 9.8 m/s2);
    ///   pulled towards where the animation has it relative to its parent's simulated place by
    ///   1 - (1 - stiffness)^(60 dt);
    ///   pushed out of the colliders (capsules on body bones, the bone's radius added);
    ///   the angle limit against the animation's direction (the breasts: 70 down to 35 degrees);
    ///   back to its length from the parent.
    /// A parent with one child turns to point at it; the others keep the animation's rotation and only move.  Settings
    /// vary along a chain by the node's curves (0 at the root, 1 at the farthest bone).
    /// UE's axes: the game's capsule offsets are in UE bone space; through the PSK import (Y mirrored) and the FBX export
    /// (Unity mirrors X) a bone-space vector (x, y, z) is (-x, -y, z) here (checked on the thigh, hand and hair capsules:
    /// RoeKawaiiPhysics.Check).  Centimetres become metres.
    /// </summary>
    public class RoeKawaiiPhysics : IRoeCloth
    {
        public static bool Covers(RoeClothScope scope, string kind)
        {
            var rig = scope.animator != null ? scope.animator.GetComponent<RoeKawaiiRig>() : null;
            return rig != null && rig.Kinds().Contains(kind);
        }

        class Bone
        {
            public Transform t;
            public int parent = -1;
            public readonly List<int> children = new List<int>();
            public float lengthFromRoot;
            public float damping, stiffness, radius, limitAngle, worldLocation, worldRotation;
            public Vector3 location, previous, pose;
            public Quaternion poseRotation;
            public Vector3 restPosition;
            public Quaternion restRotation;
        }

        class Capsule
        {
            public Transform bone;
            public Vector3 offset, axis;        // in the bone's space (Unity's axes, metres)
            public float radius, half;
            public Vector3 a, b;                // this step, in the fighter's space
        }

        class Node
        {
            public string name, kind;
            public readonly List<Bone> bones = new List<Bone>();
            public Vector3 gravity;             // world, m/s2
            public float targetFramerate;
            public readonly List<Capsule> capsules = new List<Capsule>();
        }

        readonly List<Node> nodes = new List<Node>();
        readonly Transform space;
        readonly RoeKawaiiRig rig;
        Vector3 lastPosition;
        Quaternion lastRotation;
        bool started;
        float dtOld;
        public float Weight { get; set; } = 1f;
        public string Report { get; }

        /// <summary>UE's teleport thresholds (plugin defaults): a move over 3 m or a turn over 10 degrees in a step is not inertia.</summary>
        public static float TeleportDistance = 3f, TeleportDegrees = 10f;

        public RoeKawaiiPhysics(RoeClothScope scope)
        {
            var animator = scope.animator;
            space = scope.world != null ? scope.world : animator.transform;
            rig = animator.GetComponent<RoeKawaiiRig>();
            var byName = new Dictionary<string, Transform>();
            foreach (var t in animator.GetComponentsInChildren<Transform>(true))
                if (!byName.ContainsKey(t.name))
                    byName[t.name] = t;
            var data = rig != null ? rig.Get() : new RoeKawaiiRig.Data();
            foreach (var n in data.nodes.Where(n => scope.Takes(n.kind)))
            {
                if (!byName.TryGetValue(n.root, out var root))
                    continue;
                var node = new Node { name = n.root, kind = n.kind, targetFramerate = n.targetFramerate > 0f ? n.targetFramerate : 60f };
                var exclude = new HashSet<string>(n.exclude ?? new List<string>());
                Add(node, root, -1, exclude);
                float total = node.bones.Max(b => b.lengthFromRoot);
                foreach (var b in node.bones)
                {
                    float rate = total > 0f ? b.lengthFromRoot / total : 0f;
                    b.damping = Mathf.Clamp01(n.damping * Eval(n.dampingCurve, rate));
                    b.stiffness = Mathf.Clamp01(n.stiffness * Eval(n.stiffnessCurve, rate));
                    b.radius = Mathf.Max(0f, n.radius * Eval(n.radiusCurve, rate)) * 0.01f;
                    b.limitAngle = Mathf.Max(0f, n.limitAngle * Eval(n.limitAngleCurve, rate));
                    b.worldLocation = Mathf.Clamp01(n.worldDampingLocation);
                    b.worldRotation = Mathf.Clamp01(n.worldDampingRotation);
                }
                var g = n.gravity != null && n.gravity.Length == 3 ? n.gravity : new float[3];
                node.gravity = FromUe(new Vector3(g[0], g[1], g[2]), world: true) * 0.01f;
                foreach (var c in n.capsules)
                {
                    if (!byName.TryGetValue(c.bone, out var bone) || c.radius <= 0f || c.length <= 0f)
                        continue;
                    node.capsules.Add(new Capsule
                    {
                        bone = bone, radius = c.radius * 0.01f, half = c.length * 0.005f,
                        offset = FromUe(new Vector3(c.offset[0], c.offset[1], c.offset[2]), world: false) * 0.01f,
                        axis = FromUe(UeRotatorAxisZ(c.rotation[0], c.rotation[1], c.rotation[2]), world: false).normalized,
                    });
                }
                nodes.Add(node);
            }
            Report = $"KawaiiPhysics (her game's settings): {nodes.Count} nodes, {nodes.Sum(x => x.bones.Count)} bones (" +
                     string.Join(", ", nodes.GroupBy(x => x.kind).Select(gr => $"{gr.Key} {gr.Count()}/{gr.Sum(x => x.bones.Count)}")) +
                     $"), {nodes.Sum(x => x.capsules.Count)} capsules";
        }

        void Add(Node node, Transform t, int parent, HashSet<string> exclude)
        {
            if (exclude.Contains(t.name))
                return;
            var (restPosition, restRotation) = rig != null ? rig.RestOf(t) : (t.localPosition, t.localRotation);
            var b = new Bone { t = t, parent = parent, restPosition = restPosition, restRotation = restRotation };
            if (parent >= 0)
            {
                var p = node.bones[parent];
                b.lengthFromRoot = p.lengthFromRoot + restPosition.magnitude;
            }
            int index = node.bones.Count;
            node.bones.Add(b);
            if (parent >= 0)
                node.bones[parent].children.Add(index);
            foreach (Transform c in t)
                if (c.GetComponent<Renderer>() == null)
                    Add(node, c, index, exclude);
        }

        /// <summary>A curve as the plugin's rich curves here are (linear keys): flat [t0, v0, t1, v1, ...]; none = 1.</summary>
        static float Eval(float[] keys, float t)
        {
            if (keys == null || keys.Length < 2)
                return 1f;
            if (t <= keys[0])
                return keys[1];
            for (int i = 2; i + 1 < keys.Length; i += 2)
                if (t <= keys[i])
                {
                    float t0 = keys[i - 2], v0 = keys[i - 1], t1 = keys[i], v1 = keys[i + 1];
                    return t1 > t0 ? Mathf.Lerp(v0, v1, (t - t0) / (t1 - t0)) : v1;
                }
            return keys[keys.Length - 1];
        }

        /// <summary>
        /// UE's axes to ours.  World (component) vectors: UE (x forward-ish, y, z up) went through the PSK import (y
        /// mirrored) and Blender's FBX export into Unity as (-x, z, -y) of Blender's, i.e. (-x, z, y) of UE's.  Bone-space
        /// vectors: the bones' frames went the same way, the FBX export's axis turn sitting on the root only, so a bone-space
        /// (x, y, z) is (-x, -y, z) (Blender's (x, -y, z), then Unity's import mirrors x).
        /// </summary>
        public static Vector3 FromUe(Vector3 v, bool world) => world ? new Vector3(-v.x, v.z, v.y) : new Vector3(-v.x, -v.y, v.z);

        /// <summary>The Z axis of UE's FRotator(pitch, yaw, roll) (FRotator::Quaternion, then FQuat::RotateVector), in UE's axes.</summary>
        public static Vector3 UeRotatorAxisZ(float pitch, float yaw, float roll)
        {
            float h = Mathf.Deg2Rad * 0.5f;
            float sp = Mathf.Sin(pitch * h), cp = Mathf.Cos(pitch * h);
            float sy = Mathf.Sin(yaw * h), cy = Mathf.Cos(yaw * h);
            float sr = Mathf.Sin(roll * h), cr = Mathf.Cos(roll * h);
            var q = new Vector3(cr * sp * sy - sr * cp * cy, -cr * sp * cy - sr * cp * sy, cr * cp * sy - sr * sp * cy);
            float w = cr * cp * cy + sr * sp * sy;
            var v = new Vector3(0f, 0f, 1f);
            var t = 2f * Vector3.Cross(q, v);
            return v + w * t + Vector3.Cross(q, t);
        }

        public void Rest()
        {
            foreach (var n in nodes)
                foreach (var b in n.bones)
                    b.t.SetLocalPositionAndRotation(b.restPosition, b.restRotation);
        }

        public void Reset() => started = false;

        public void Step(float dt, float floor)
        {
            if (dt <= 0f || nodes.Count == 0)
                return;
            var spaceRotation = space.rotation;
            var inverse = Quaternion.Inverse(spaceRotation);
            if (!started)
            {
                foreach (var n in nodes)
                {
                    ReadPose(n, inverse);
                    foreach (var b in n.bones)
                        b.location = b.previous = b.pose;
                }
                lastPosition = space.position;
                lastRotation = spaceRotation;
                dtOld = 1f / 60f;
                started = true;
            }
            // the fighter's move and turn since the last step, in her space now (UpdateSkelCompMove)
            var move = space.InverseTransformPoint(lastPosition);
            if (move.sqrMagnitude > TeleportDistance * TeleportDistance)
                move = Vector3.zero;
            var turn = inverse * lastRotation;
            if (Quaternion.Angle(Quaternion.identity, turn) > TeleportDegrees)
                turn = Quaternion.identity;
            lastPosition = space.position;
            lastRotation = spaceRotation;

            // one node after the other, as UE's anim graph runs them: a node reads the pose the ones before it wrote (Eve's
            // tie is two nodes, the second rooted on the first's last bone)
            foreach (var n in nodes)
            {
                ReadPose(n, inverse);
                Simulate(n, dt, move, turn, inverse);
                Apply(n, spaceRotation);
            }
            dtOld = dt;
        }

        /// <summary>The animation's pose of a node's bones (as the nodes before it left them), in the fighter's space.</summary>
        void ReadPose(Node n, Quaternion inverse)
        {
            foreach (var b in n.bones)
            {
                b.pose = space.InverseTransformPoint(b.t.position);
                b.poseRotation = inverse * b.t.rotation;
            }
        }

        void Simulate(Node n, float dt, Vector3 move, Quaternion turn, Quaternion inverse)
        {
            var bones = n.bones;
            // the colliders where the animation has the body now
            foreach (var c in n.capsules)
            {
                var centre = c.bone.TransformPoint(c.offset);
                var axis = c.bone.TransformDirection(c.axis) * c.half;
                c.a = space.InverseTransformPoint(centre + axis);
                c.b = space.InverseTransformPoint(centre - axis);
            }
            var gravity = inverse * n.gravity;
            float exponent = n.targetFramerate * dt;
            foreach (var b in bones)
                if (b.parent < 0)
                {
                    b.previous = b.location;
                    b.location = b.pose;
                }
            // Simulate
            foreach (var b in bones)
            {
                if (b.parent < 0)
                    continue;
                var velocity = (b.location - b.previous) / dtOld;
                b.previous = b.location;
                velocity *= 1f - b.damping;
                b.location += velocity * dt;
                b.location += move * (1f - b.worldLocation);
                b.location += (turn * b.previous - b.previous) * (1f - b.worldRotation);
                b.location += 0.5f * gravity * dt * dt;
                var p = bones[b.parent];
                var target = p.location + (b.pose - p.pose);
                b.location += (target - b.location) * (1f - Mathf.Pow(1f - b.stiffness, exponent));
            }
            // collisions
            foreach (var b in bones)
            {
                if (b.parent < 0)
                    continue;
                foreach (var c in n.capsules)
                {
                    var closest = ClosestOnSegment(b.location, c.a, c.b);
                    float limit = b.radius + c.radius;
                    var away = b.location - closest;
                    if (away.sqrMagnitude < limit * limit && away.sqrMagnitude > 1e-12f)
                        b.location = closest + away.normalized * limit;
                }
            }
            // angle limit, then the bone's length
            foreach (var b in bones)
            {
                if (b.parent < 0)
                    continue;
                var p = bones[b.parent];
                if (b.limitAngle > 0f)
                {
                    var dir = (b.location - p.location).normalized;
                    var poseDir = (b.pose - p.pose).normalized;
                    var axis = Vector3.Cross(poseDir, dir);
                    float angle = Mathf.Atan2(axis.magnitude, Vector3.Dot(poseDir, dir)) * Mathf.Rad2Deg;
                    float over = angle - b.limitAngle;
                    if (over > 0f && axis.sqrMagnitude > 1e-12f)
                    {
                        dir = Quaternion.AngleAxis(-over, axis.normalized) * dir;
                        b.location = p.location + dir * (b.location - p.location).magnitude;
                    }
                }
                float length = (b.pose - p.pose).magnitude;
                var d = b.location - p.location;
                b.location = p.location + (d.sqrMagnitude > 1e-12f ? d.normalized * length : Vector3.zero);
            }
        }

        static Vector3 ClosestOnSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            var ab = b - a;
            float len = ab.sqrMagnitude;
            if (len < 1e-12f)
                return a;
            return a + ab * Mathf.Clamp01(Vector3.Dot(p - a, ab) / len);
        }

        /// <summary>ApplySimulateResult: the root where the animation has it, every other bone where it was simulated; a bone
        /// with one child turned so the child lies where it was simulated.  Written parents first, blended by Weight.</summary>
        void Apply(Node n, Quaternion spaceRotation)
        {
            float w = Mathf.Clamp01(Weight);
            if (w <= 0f)
                return;
            {
                var bones = n.bones;
                foreach (var b in bones)
                {
                    var position = b.parent < 0 ? b.pose : b.location;
                    var rotation = b.poseRotation;
                    if (b.children.Count == 1)
                    {
                        var c = bones[b.children[0]];
                        var poseVector = c.pose - b.pose;
                        var simVector = c.location - (b.parent < 0 ? b.pose : b.location);
                        if (poseVector.sqrMagnitude > 1e-12f && simVector.sqrMagnitude > 1e-12f)
                            rotation = Quaternion.FromToRotation(poseVector, simVector) * b.poseRotation;
                    }
                    if (w < 1f)
                    {
                        position = Vector3.Lerp(b.pose, position, w);
                        rotation = Quaternion.Slerp(b.poseRotation, rotation, w);
                    }
                    b.t.SetPositionAndRotation(space.TransformPoint(position), spaceRotation * rotation);
                }
            }
        }

        /// <summary>For checks: each capsule's centre and axis in the world now, with its bone.</summary>
        public IEnumerable<(string node, string bone, Vector3 centre, Vector3 axis, float radius, float half)> Capsules() =>
            nodes.SelectMany(n => n.capsules.Select(c => (n.name, c.bone.name, c.bone.TransformPoint(c.offset), c.bone.TransformDirection(c.axis), c.radius, c.half)));
    }
}
