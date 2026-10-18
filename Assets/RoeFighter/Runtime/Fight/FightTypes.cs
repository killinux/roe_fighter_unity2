using System;
using UnityEngine;

namespace RoeFighter.Fight
{
    /// <summary>
    /// How a strike must be blocked.  Mid is blocked standing or crouching; Low (sweeps, crouching kicks) only
    /// crouching; High only standing - every jumping attack is High (an overhead).  A standing strike measured
    /// high (a jab at head height) is Mid for blocking: it simply passes over a crouching body, since the hit
    /// test is geometric (Fighter.DistanceToBody).
    /// </summary>
    public enum Level { High, Mid, Low }

    /// <summary>The stance a strike is thrown from; the fight picks a button's strike by the fighter's stance.</summary>
    public enum Stance { Stand, Crouch, Air }

    /// <summary>One attack: which clip, how fast, when it can hit and what a hit does.</summary>
    [Serializable]
    public class Move
    {
        public string name;                // "jab"
        public string button;              // "A", "B", "C", "D"; "CD" the blowback attack, "T" the throw
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
        public bool knockdown;             // the victim falls where she stands (her game's fall clip)
        public Level level = Level.Mid;
        public float meterGain = 6f;       // special gauge for the attacker on hit (half on block)
        public float reach;                // metres from the hips at the farthest, for the CPU
        public string hitSound;            // sound name in the rig
        public float travel, travelSide;   // metres the strike carries her forward / to her right over the clip (taken out of
                                           // the clip; the fight moves her along, so she does not jump back when it ends)
        public Stance stance = Stance.Stand;
        public string kind = "";           // "" a strike; "blowback" the CD attack; "throw" a grab (its hit window is the grab)
        public bool launch;                // sends the victim flying: up launchUp m/s, back launchBack m/s, then down and out
        public float launchUp = 4.5f, launchBack = 3f;

        /// <summary>Must be blocked standing: every jumping attack.</summary>
        public bool Overhead => stance == Stance.Air;
        /// <summary>Must be blocked crouching.</summary>
        public bool Low => level == Level.Low;
    }

    /// <summary>A skill of the game used as a special move.</summary>
    [Serializable]
    public class Special
    {
        public string name;                // "skill1"
        public string clip;                // humanoid clip in the rig (the game's skill_01 converted)
        public string action;              // the game's timeline (skill1)
        public int damage = 160;           // split over the skill's damage hits by the game's ratios
        public float meterCost;            // 0 for normal specials, one stock (Fighter.Stock) for the super
        public float range = 6f;           // starts only when the opponent is closer than this
    }

    /// <summary>Input of one fighter for one simulation step, in screen terms.</summary>
    public struct FighterInput
    {
        public int x;                      // -1 left, +1 right on the screen
        public int y;                      // +1 up (jump), -1 down (crouch)
        public bool a, b, c, d;            // pressed this step
        public bool ab, cd;                // the paired presses on their own keys (A+B the roll, C+D the blowback attack)
        public bool s1, s2, s3;            // special shortcuts pressed this step
        public bool anyPressed => a || b || c || d || ab || cd || s1 || s2 || s3;
    }

    public enum FightState
    {
        Intro, Idle, Walk, Run, BackDash, Crouch, Jump, Land, Attack, Block, Hurt, Launched, Down, Getup, Special,
        Roll, Throw, Thrown, KO, Win, Lose,
    }
}
