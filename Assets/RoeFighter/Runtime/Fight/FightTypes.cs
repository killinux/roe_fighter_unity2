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

    /// <summary>
    /// A skill of hers that hits from a distance in her game (user 10-18 "inase有远程攻击也加上", then "要做真正飞过去的弹道物体" -
    /// "做"): the game stands the enemy line several metres off, and these skills do not dash up to it.  The scene build finds
    /// them (no "attack" slide in the skill sheet, hit effects 3 m or more ahead) and how each one reaches:
    ///   throw  her weapon flies in the game's clip (Inase's greatsword in skill 2, Kart's axe in skill 1).  It flies to where
    ///          the opponent stood when it left her hand - the game's own flight stretched or shortened to that distance - and
    ///          lands the skill's blows when it meets her body on the way; a jump over it, a roll through it or a step away
    ///          and it misses.  Thrown back the way the game's clip catches it.
    ///   shot   bullets (Kart's pistols): one per blow, fired so that each would reach the game's distance at the blow's
    ///          moment, flying along the fight line (Projectile); each lands on what it meets, blockable, or flies on past.
    ///   place  the blow comes down where she aimed (Amano's tentacles, Inase's swords of light): the spot is fixed shortly
    ///          before the first blow and the blow lands only if the opponent is still there, on the ground.
    /// The game's far effects (its hit anchors, the trail riding on the thrown weapon) are moved to the distance the skill
    /// really reaches (FighterRig.BeginRemap).
    /// </summary>
    [Serializable]
    public class RangedSkill
    {
        public string skill;               // "skill2"
        public string kind = "place";      // throw | shot | place
        public float anchorReach;          // m ahead of her where the game puts this skill's hit effects (its enemy line)
        public string weapon;              // throw: name of the weapon bone that flies (one of the rig's weapon roots)
        public Vector3 weaponCentre;       // throw: the middle of the weapon in that bone's frame
        public float reach;                // throw: how far ahead the weapon gets in the game's clip (m, from her root)
        public float release, back;        // throw: timeline seconds the weapon leaves her hand / is back in it
        public float farAt;                // throw: timeline seconds it is farthest out (its way out ends there)
        public float[] path;               // throw: how far ahead of her root it is in the game's clip, every 1/30 s from release to back
        public float lockAt;               // place: timeline seconds the spot is fixed
        public float speed = 26f;          // shot: m/s
        public bool IsThrow => kind == "throw";
        public bool IsShot => kind == "shot";
        public bool IsPlace => kind == "place";
    }

    /// <summary>For the CPU (Fighter.RangedComing): the next blow an opponent's ranged skill sends her way.</summary>
    public struct RangedThreat
    {
        public string kind;                // throw | shot | place (RangedSkill.kind)
        public int blow;                   // which blow of the skill
        public float until;                // seconds until it would land where she stands now (a thrown weapon: until it leaves the hand, 0 in flight)
        public bool locked;                // place: the spot is fixed
        public float spot;                 // place: the spot, along the fight axis
    }

    /// <summary>A bullet in flight along the fight line (RangedSkill "shot"), stepped by the fight.</summary>
    public class Projectile
    {
        public Fighter owner;
        public Special special;            // the skill that fired it (its damage moment is this projectile's)
        public float u, h;                 // along the fight axis (m from the centre), height above the floor
        public int dir;                    // +1 along the axis, -1 against it
        public float speed, travelled;
        public int damage;
        public bool last;                  // the skill's last blow (the super's knocks down)
        public int blow;                   // which blow of the skill
        public GameObject visual;
        public bool dead;
    }

    /// <summary>Input of one fighter for one simulation step, in screen terms.</summary>
    public struct FighterInput
    {
        public int x;                      // -1 left, +1 right on the screen
        public int y;                      // +1 up (jump), -1 down (crouch)
        public bool a, b, c, d;            // pressed this step
        public bool ab, cd;                // the paired presses on their own keys (A+B the roll, C+D the blowback attack)
        public bool max;                   // MAX mode (B+C on its own key)
        public bool s1, s2, s3;            // special shortcuts pressed this step
        public bool anyPressed => a || b || c || d || ab || cd || max || s1 || s2 || s3;
    }

    public enum FightState
    {
        Intro, Idle, Walk, Run, BackDash, Crouch, Jump, Land, Attack, Block, Hurt, Launched, Down, Getup, Special,
        Roll, Throw, Thrown, KO, Win, Lose,
    }
}
