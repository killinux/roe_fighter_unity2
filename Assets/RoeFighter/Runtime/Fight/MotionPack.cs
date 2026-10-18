using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RoeFighter.Fight
{
    /// <summary>
    /// A set of basic moves that can be swapped for another: which clip the fight plays in each role
    /// (stance, walking on, walking back, running, crouching, jumping, blocking, the reactions...) and
    /// which strike each attack button throws, with its timing.  Every clip is a humanoid clip
    /// (muscles), so any humanoid source fits - the public motion-capture datasets converted by the
    /// editor tools, or animation bought from the Asset Store and imported as Humanoid.  The game's
    /// own clips (skills, hurt, falls) are not part of a pack; they belong to each character.
    ///
    /// Packs are built by RoeMotionPacks (editor) into Assets/RoeFighter/Generated/motionpacks; the
    /// fight scene gets all of them, starts with the one chosen at build time (-roeMotions) and F3
    /// switches to the next one in play.  The side-view fight also has one "basics" pack (kof_basics)
    /// for everything a character's own game has no clip for: crouching, jumping, blocking, the
    /// crouching and jumping attacks, the blowback attack, the throw, flying and falling (FightGame.basics).
    /// </summary>
    [CreateAssetMenu(menuName = "ROE Fighter/Motion pack")]
    public class MotionPack : ScriptableObject
    {
        [Serializable]
        public class Clip
        {
            public string role;          // guard, walk, walk_back, run, ..., or a strike's clip name
            public AnimationClip clip;
            public bool loop;
        }

        /// <summary>The roles the fight plays besides the strikes; "guard" is required.</summary>
        public static readonly string[] Roles =
        {
            "guard", "walk", "walk_back", "run",
            "crouch", "takeoff", "jump", "fall", "landing",
            "block", "block_hit", "crouch_block", "crouch_block_hit",
            "hit_crouch", "hit_low", "hit_air", "hit_sweep", "knocked_back", "fall_down", "getup",
            "dash", "backdash", "throw_confirm", "throw_react",
        };

        /// <summary>The roles that play as loops.</summary>
        public static readonly string[] LoopRoles = { "guard", "walk", "walk_back", "run", "crouch", "block", "crouch_block" };

        public string title;             // shown when switched to
        public string source;            // where the motions come from
        public string license;           // and under which terms
        public bool strikesOnly;         // only a character's own strikes (FighterRig.strikePack), not one of the fight's packs (F3)
        public bool grip;                // strikes with the weapon in her hand: she holds it through the basic moves (FighterRig.Holds)
        public float walkSpeed, backSpeed;   // m/s the fight walks on / back with these clips; 0: the fight's own
        public List<Clip> clips = new List<Clip>();
        public List<Move> strikes = new List<Move>();   // Move.clip names a role in clips

        public AnimationClip Get(string role) => clips.FirstOrDefault(c => c.role == role)?.clip;

        /// <summary>The strikes, copied (the fight changes nothing in them, but a pack is an asset).</summary>
        public List<Move> CopyStrikes() => strikes.Select(m => JsonUtility.FromJson<Move>(JsonUtility.ToJson(m))).ToList();
    }
}
