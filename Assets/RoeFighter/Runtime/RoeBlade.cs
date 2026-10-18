using UnityEngine;

namespace RoeFighter
{
    /// <summary>
    /// A blade held in a hand (Fiona's longsword, VdfFighter.AttachWeapons): from the hilt to the tip in the weapon's own
    /// frame.  A strike whose bone is that hand hits with the blade - the point along it nearest the opponent
    /// (Fighter.ActiveHit) - and is measured by its tip (RoeMotionPacks.Measure): the hand alone was a metre short.
    /// </summary>
    public class RoeBlade : MonoBehaviour
    {
        public HumanBodyBones hand = HumanBodyBones.RightHand;
        public Vector3 hilt;                    // where the blade leaves the guard
        public Vector3 tip;                     // its far end
        [Range(2, 16)] public int samples = 6;  // points along it tried for the nearest

        public Vector3 Hilt => transform.TransformPoint(hilt);
        public Vector3 Tip => transform.TransformPoint(tip);

        /// <summary>The point along the blade where distance (to the opponent's body) is least.</summary>
        public Vector3 Nearest(System.Func<Vector3, float> distance)
        {
            Vector3 a = Hilt, b = Tip, best = b;
            float bestD = float.MaxValue;
            for (int i = 0; i < samples; i++)
            {
                var p = Vector3.Lerp(a, b, i / (float)(samples - 1));
                float d = distance(p);
                if (d < bestD)
                {
                    bestD = d;
                    best = p;
                }
            }
            return best;
        }
    }
}
