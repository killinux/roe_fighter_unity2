using System;
using UnityEngine;

namespace RoeFighter.Fight
{
    /// <summary>Where a strike lands.  Low strikes must be blocked crouching (no crouch yet: all block standing).</summary>
    public enum Level { High, Mid, Low }

    /// <summary>One attack: which clip, how fast, when it can hit and what a hit does.</summary>
    [Serializable]
    public class Move
    {
        public string name;                // "jab"
        public string button;              // "A", "B", "C", "D"
        public string clip;                // clip name in the fighter rig
        public float speed = 1.5f;         // playback speed (the mocap actor performed slowly)
        public HumanBodyBones bone;        // the bone that hits
        public float radius = 0.16f;       // hit sphere around it, metres
        public float hitStart, hitEnd;     // clip seconds (at speed 1) in which it can hit
        public float length;               // clip seconds (at speed 1)
        public float recover = 0.85f;      // fraction of the clip after which the fighter can act again
        public float cancel = 0.55f;       // after a hit or block another strike may follow from this fraction on
        public int damage = 50;
        public float hitstun = 0.35f;      // seconds the opponent cannot act after a hit
        public float blockstun = 0.2f;
        public float push = 0.3f;          // metres the opponent slides back on hit (half on block)
        public bool knockdown;
        public Level level = Level.Mid;
        public float meterGain = 6f;       // special gauge for the attacker on hit (half on block)
        public float reach;                // metres from the hips at the farthest, for the CPU
        public string hitSound;            // sound name in the rig
        public float travel, travelSide;   // metres the strike carries her forward / to her right over the clip (taken out of
                                           // the clip; the fight moves her along, so she does not jump back when it ends)
    }

    /// <summary>A skill of the game used as a special move.</summary>
    [Serializable]
    public class Special
    {
        public string name;                // "skill1"
        public string clip;                // humanoid clip in the rig (the game's skill_01 converted)
        public string action;              // the game's timeline (skill1)
        public int damage = 160;           // split over the skill's damage hits by the game's ratios
        public float meterCost;            // 0 for normal specials, 100 for the super
        public float range = 6f;           // starts only when the opponent is closer than this
    }

    /// <summary>Input of one fighter for one simulation step, in screen terms.</summary>
    public struct FighterInput
    {
        public int x;                      // -1 left, +1 right on the screen
        public int y;                      // +1 up = step into the screen, -1 = towards the camera
        public bool a, b, c, d;            // pressed this step
        public bool s1, s2, s3;            // special shortcuts pressed this step
        public bool anyPressed => a || b || c || d || s1 || s2 || s3;
    }

    public enum FightState { Intro, Idle, Walk, Side, Attack, Block, Hurt, Down, Getup, Special, KO, Win, Lose }
}
