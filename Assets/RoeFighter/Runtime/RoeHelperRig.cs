using System;
using System.Collections.Generic;
using UnityEngine;

namespace RoeFighter
{
    /// <summary>
    /// The game's limb helper bones, driven from the human bones.
    ///
    /// Much of the arm and leg skin of the ROE characters hangs from helper bones, not from the
    /// limb bones: forearm twist bones parented to the upper arm, calf twist bones parented to the
    /// thigh, upper-arm twist bones, knee and elbow helpers.  (g04's upper arm, forearm and calf
    /// carry no skin at all.)  The game's clips animate every helper, frame by frame; animation
    /// that only knows the human bones (motion capture) leaves them where the battle stance puts
    /// them, and the skin of the forearm or calf stays behind when the elbow or knee bends.
    ///
    /// Each helper follows one human bone (or a blend of two, for the knee and elbow helpers):
    /// a fixed rotation in that bone's frame, plus a share of the roll of the next bone (the
    /// hand's roll spreads along the forearm twist bones), and, when the helper is attached to
    /// the bone, a fixed offset from it.  RoeHelperFit (editor) measures all of that on the game's
    /// own clips.  Apply() runs after the pose is evaluated.
    /// </summary>
    public class RoeHelperRig : MonoBehaviour
    {
        [Serializable]
        public class Drive
        {
            public Transform helper;
            public Transform driver;          // the human bone the helper follows
            public Transform driver2;         // a second one for a blend (null: none)
            public float blend;               // 0 = driver, 1 = driver2
            public Quaternion align2 = Quaternion.identity;   // driver2's frame turned to match driver's at rest
            public Quaternion rotation = Quaternion.identity; // helper rotation in the driver frame, without its twist
            public Vector3 twistAxis = Vector3.right;         // in the driver frame
            public Transform twistSource;     // the next human bone, whose roll the helper shares
            public Quaternion twistRest = Quaternion.identity; // twist source in the driver frame at rest
            public float twistShare, twistOffset;             // helper twist = share * source twist + offset (degrees)
            public Transform twistSource2;    // the bone before: an upper-arm twist bone takes back part of the arm's own roll
            public Quaternion twistRest2 = Quaternion.identity;
            public float twistShare2;
            public Transform positionBone;    // the human bone the helper is attached to (null: its parent places it)
            public Vector3 offset;            // its position in that bone's frame
            public float fitError;            // degrees (RMS) on the game's clips, for the record
        }

        public List<Drive> drives = new List<Drive>();

        /// <summary>Signed roll (degrees) of a rotation about an axis.</summary>
        public static float TwistAngle(Quaternion q, Vector3 axis)
        {
            float p = q.x * axis.x + q.y * axis.y + q.z * axis.z;
            float angle = 2f * Mathf.Atan2(p, q.w) * Mathf.Rad2Deg;
            return Mathf.DeltaAngle(0f, angle);
        }

        public static Quaternion Frame(Drive d)
        {
            var f = d.driver.rotation;
            if (d.driver2 != null && d.blend > 0f)
                f = Quaternion.Slerp(f, d.driver2.rotation * d.align2, d.blend);
            return f;
        }

        /// <summary>The helper's world rotation from the drivers as they are now.</summary>
        public static Quaternion Rotation(Drive d)
        {
            var frame = Frame(d);
            float twist = d.twistOffset;
            if (d.twistSource != null && d.twistShare != 0f)
            {
                var local = Quaternion.Inverse(frame) * d.twistSource.rotation * Quaternion.Inverse(d.twistRest);
                twist += d.twistShare * TwistAngle(local, d.twistAxis);
            }
            if (d.twistSource2 != null && d.twistShare2 != 0f)
            {
                var local = Quaternion.Inverse(frame) * d.twistSource2.rotation * Quaternion.Inverse(d.twistRest2);
                twist += d.twistShare2 * TwistAngle(local, d.twistAxis);
            }
            return frame * Quaternion.AngleAxis(twist, d.twistAxis) * d.rotation;
        }

        /// <summary>Drive the helpers; weight 1 = fully, 0 = leave them as the animation put them.</summary>
        public void Apply(float weight = 1f)
        {
            if (weight <= 0f)
                return;
            foreach (var d in drives)
            {
                if (d.helper == null || d.driver == null)
                    continue;
                var rotation = Rotation(d);
                var position = d.positionBone != null ? d.positionBone.position + d.positionBone.rotation * d.offset : d.helper.position;
                if (weight < 1f)
                {
                    rotation = Quaternion.Slerp(d.helper.rotation, rotation, weight);
                    position = Vector3.Lerp(d.helper.position, position, weight);
                }
                d.helper.SetPositionAndRotation(position, rotation);
            }
        }
    }
}
