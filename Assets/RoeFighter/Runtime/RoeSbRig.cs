using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RoeFighter
{
    /// <summary>
    /// Stellar Blade's own physics for a character, as her game has it (tools/sb_physics.py reads it out of the cooked
    /// packages): the spring bones of her main anim blueprint (UE's AnimNode_SpringBone) and the rigid bodies of her
    /// physics assets (the ponytail from Ab-TL-HairB03 down, the back panels) and the control rigs of her outfit's blueprint
    /// (the pleated skirt's) - Assets/SB/&lt;id&gt;/sbphysics.json on her
    /// fighter prefab, for RoeSbPhysics.  Her KawaiiPhysics nodes are in RoeKawaiiRig (kawaii.json) as Fiona's are.
    /// Units as the game's: centimetres, kilograms, degrees, UE bone space.
    /// </summary>
    public class RoeSbRig : MonoBehaviour
    {
        public TextAsset data;

        [Serializable]
        public class Data
        {
            public string source;
            public List<Spring> springs = new List<Spring>();
            public List<Body> bodies = new List<Body>();
            public List<Joint> joints = new List<Joint>();
            /// <summary>The outfit blueprint's control rigs, as programs for RoeRigVM (tools/sb_controlrig.py).</summary>
            public List<RoeRigVM.Program> rigs = new List<RoeRigVM.Program>();
        }

        [Serializable]
        public class Spring
        {
            public string name, bone, kind;
            public float maxDisplacement, stiffness, damping, errorResetThresh = 256f, alpha = 1f;
            public bool limitDisplacement, localSpace;
            public bool[] translate = { true, true, true }, rotate = { false, false, false };
            public int averageVelocityFrames;
        }

        [Serializable]
        public class Shape
        {
            public string type;                       // capsule, sphere, box
            public float[] center = new float[3], rotation = new float[3], size = new float[3];
            public float radius, length;
        }

        [Serializable]
        public class Body
        {
            public string bone, chain;                // chain: ponytail, cape
            public bool simulate, kinematicInGame;
            public float mass, linearDamping, angularDamping;
            public List<Shape> shapes = new List<Shape>();
        }

        [Serializable]
        public class Joint
        {
            public string chain, child, parent, swing1Motion, swing2Motion, twistMotion;
            public float[] pos1 = new float[3], pri1 = { 1, 0, 0 }, sec1 = { 0, 1, 0 }, pos2 = new float[3], pri2 = { 1, 0, 0 }, sec2 = { 0, 1, 0 };
            public float swing1, swing2, twist;
        }

        /// <summary>What a chain of rigid bodies is, among the fight's kinds of loose parts (RoeClothScope).</summary>
        // "hair": a character's own simulated strands (Raven's long hair, tools/sb_physics.py --abp/--physics)
        public static string KindOf(string chain) => chain == "ponytail" || chain == "hair" ? "hair" : chain == "cape" ? "skirt" : "chain";

        Data parsed;
        readonly Dictionary<Transform, (Vector3 position, Quaternion rotation)> rest = new Dictionary<Transform, (Vector3, Quaternion)>();

        public Data Get() => parsed ??= data != null ? JsonUtility.FromJson<Data>(data.text) : new Data();

        /// <summary>A bone's local rest as the model first had it (the bind pose), as RoeKawaiiRig keeps it.</summary>
        public (Vector3 position, Quaternion rotation) RestOf(Transform t)
        {
            if (!rest.TryGetValue(t, out var r))
                rest[t] = r = (t.localPosition, t.localRotation);
            return r;
        }

        public IEnumerable<string> Kinds() =>
            Get().springs.Select(s => s.kind).Concat(Get().bodies.Where(b => b.simulate).Select(b => KindOf(b.chain)))
                .Concat(Get().rigs.Select(r => r.kind)).Distinct();
    }
}
