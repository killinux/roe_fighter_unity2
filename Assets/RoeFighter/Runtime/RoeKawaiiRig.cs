using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RoeFighter
{
    /// <summary>
    /// A character's KawaiiPhysics settings, as her game has them (Vindictus: Defying Fate drives Fiona's skirt, feathers,
    /// hair and breasts with the KawaiiPhysics plugin - tools/vdf_research, tools/vdf_kawaii.py): the data file
    /// (Assets/VDF/&lt;id&gt;/kawaii.json) on her fighter prefab, for RoeKawaiiPhysics.
    /// </summary>
    public class RoeKawaiiRig : MonoBehaviour
    {
        public TextAsset data;

        [Serializable]
        public class Data
        {
            public string source;
            public List<Node> nodes = new List<Node>();
        }

        [Serializable]
        public class Node
        {
            public string name, abp, kind, root;
            public List<string> exclude = new List<string>();
            public float damping, stiffness, worldDampingLocation, worldDampingRotation, radius, limitAngle, targetFramerate = 60f;
            public float[] gravity = new float[3];
            public float[] dampingCurve, stiffnessCurve, radiusCurve, limitAngleCurve;
            public List<Capsule> capsules = new List<Capsule>();
        }

        [Serializable]
        public class Capsule
        {
            public string bone;
            public float radius, length;
            public float[] offset = new float[3], rotation = new float[3];
        }

        Data parsed;
        readonly Dictionary<Transform, (Vector3 position, Quaternion rotation)> rest = new Dictionary<Transform, (Vector3, Quaternion)>();

        public Data Get() => parsed ??= data != null ? JsonUtility.FromJson<Data>(data.text) : new Data();

        /// <summary>
        /// A bone's local rest, as the model first had it (the bind pose): kept here, on the model, so a solver made later
        /// (F4, a new round) does not take a pose the last one left behind for the rest.
        /// </summary>
        public (Vector3 position, Quaternion rotation) RestOf(Transform t)
        {
            if (!rest.TryGetValue(t, out var r))
                rest[t] = r = (t.localPosition, t.localRotation);
            return r;
        }

        public IEnumerable<string> Kinds() => Get().nodes.Select(n => n.kind).Distinct();
    }
}
