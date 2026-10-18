using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RoeFighter
{
    /// <summary>
    /// A cloth simulation the fighter can swap (F4 in the fight, like the motion packs on F3).  It runs on
    /// the finished pose of each step: Rest() before the animation is evaluated (bones no clip animates go
    /// back to rest), Step() after the pose (and the skirt's skinned pose) is done.
    /// </summary>
    public interface IRoeCloth
    {
        string Report { get; }
        /// <summary>How much of the simulation is written over the animation (0 = none: the game's own clips).</summary>
        float Weight { get; set; }
        void Rest();
        void Step(float dt, float floor);
        /// <summary>Start again from the animated pose (a teleport, a new round).</summary>
        void Reset();
        // a solver that puts components in the scene (RoeMagicaCloth) also implements IDisposable: FighterRig disposes
        // the old one before it makes the next (every round, F4)
    }

    /// <summary>
    /// What one solver takes on, on one fighter: the model, which kinds of loose parts, and how they are told apart.
    /// The kinds: breast, body (other soft flesh: hips, thighs), hair, skirt (any garment cloth: skirts, sleeves,
    /// flaps), ribbon (sashes, ties), chain (chains, tassels, hard ornaments).
    /// </summary>
    public class RoeClothScope
    {
        /// <summary>The fighter (her settings tuned to her own game: RoeBoneCloth.SwingTunings).</summary>
        public string id;
        public Animator animator;
        public Transform world;
        /// <summary>Bones something else drives (the limb helpers).</summary>
        public ICollection<Transform> exclude;
        /// <summary>The kinds this solver simulates; null: every kind.</summary>
        public ICollection<string> kinds;
        /// <summary>The loose bones by kind, when their names do not tell (DOA6's bone_&lt;id&gt;: from the game's physics data, RoeDoaRig); null: by name.</summary>
        public IDictionary<Transform, string> kindOf;
        /// <summary>
        /// A rig's own sheets of cloth (a DOA6 grid cloth, RoeDoaRig.Sheets): the bone a loose bone's chain hangs on as one
        /// piece (every column of a skirt on the hips), and per chain's first bone its neighbours in the sheet; null: from the
        /// bone tree, and from the skin's triangles across chains.
        /// </summary>
        public IDictionary<Transform, Transform> anchorOf;
        public IDictionary<Transform, Transform[]> neighbours;
        /// <summary>Her game's own Magica Cloth 2 set-up (ROE's lobby prefab, tools/roe_mc2_dump.py -> Assets/ROE/&lt;id&gt;/magica_lobby.json); null: none.</summary>
        public string gameMagica;

        public bool Takes(string kind) => kinds == null || kinds.Contains(kind);

        public RoeClothScope With(ICollection<string> only) => new RoeClothScope
        {
            id = id, animator = animator, world = world, exclude = exclude, kinds = only, kindOf = kindOf, anchorOf = anchorOf, neighbours = neighbours,
            gameMagica = gameMagica,
        };
    }

    /// <summary>
    /// The solvers a physics setup can give a kind to.
    ///   bone          RoeBoneCloth the way Magica Cloth 2 does it (10-03): chains, the skirt's animation pose follows the
    ///                 legs (RoeSkirtRig), chains that are one piece of cloth linked across, tether, world and local inertia
    ///                 (docs/magica-cloth-2.md)
    ///   bone_legacy   RoeBoneCloth as it was on 10-02 (e4b0b28): independent chains, the skirt pinned to the pelvis
    ///   doa6          Dead or Alive 6's own systems from the character's data (RoeDoaRig): soft bodies (breasts), bone
    ///                 chains, swing bones, colliders grouped per piece (docs/doa-physics.md) - only for a character that
    ///                 has DOA6 data; the kinds it cannot take go to the bone cloth
    ///   doa5lr        Dead or Alive 5 Last Round's spring net (across, shear, skip-one, long range; stiffness for shorter /
    ///                 longer than rest; pinned roots) on the bone cloth's chains instead of its angle constraints
    ///                 (RoeBoneCloth.Doa5.cs) - any fighter
    ///   kawaii        KawaiiPhysics with a character's own game settings (RoeKawaiiRig: Vindictus' Fiona - skirt, feathers,
    ///                 hair, breasts; RoeKawaiiPhysics) - only for a character that has them
    ///   stellar       Stellar Blade's own physics on Eve (RoeSbRig + RoeKawaiiRig; RoeSbPhysics): UE's spring bones, her
    ///                 KawaiiPhysics nodes, PhysX rigid bodies for the ponytail and the back panels - only for her
    ///   magica        Magica Cloth 2 itself, once it is installed in Assets/MagicaCloth2 (RoeMagicaCloth, behind the
    ///                 MAGICACLOTH2 define the package adds to the project when it loads)
    ///   roe           Rise of Eros' own: the game's keys in its clips and in its stance, its lobby's Magica Cloth 2 set-up
    ///                 elsewhere (RoeMagicaCloth with game: true; docs: README "Luf 的裙子：完全照 ROE")
    /// A new solver (another engine, another game's system) is one more entry here: a class implementing IRoeCloth
    /// that simulates only scope.kinds, and says which kinds it can take on a model (Covers).
    /// </summary>
    public static class RoeClothSolvers
    {
        public class Solver
        {
            public string name, title;
            public bool skinnedSkirt;           // RoeSkirtRig turns the skirt to follow the legs first
            /// <summary>
            /// The solver hangs the skirt as its game does (Stellar Blade's rig and KawaiiPhysics): FighterRig leaves the
            /// panels hung on the hips where the animation puts them, instead of turning them to the stance against the
            /// hips' heading (that is for the hand-keyed skirts of ROE under motion capture).
            /// </summary>
            public bool ownSkirt;
            /// <summary>
            /// The solver moves the cloth in the game's own clips too: their skirt keys are left to it as under motion capture
            /// (the skirt follows the legs, RoeSkirtRig), their hair and breast keys are its animation pose.
            /// </summary>
            public bool overGameClips;
            /// <summary>The solver hangs the skirt by itself: RoeSkirtRig's drape (our natural hang, F5) stays off.</summary>
            public bool noDrape;
            public Func<RoeClothScope, IRoeCloth> make;
            /// <summary>Per fighter: the share of RoeSkirtRig's drape (our natural hang, F5) her skirt takes; null: all of it.</summary>
            public Func<string, float> drape;
            /// <summary>Whether it can take this kind on this fighter (null: any kind, any fighter).</summary>
            public Func<RoeClothScope, string, bool> covers;
        }

        public static readonly List<Solver> All = new List<Solver>
        {
            new Solver { name = "bone", title = "Magica-style bone cloth", skinnedSkirt = true, make = s => new RoeBoneCloth(s, legacy: false) },
            // the skirt swinging like her game's hand keys: her own tuned settings over the skirt preset (RoeBoneCloth.SwingTunings)
            new Solver
            {
                name = "bone_swing", title = "Bone cloth, the skirt swinging like her game's hand keys", skinnedSkirt = true,
                make = s => new RoeBoneCloth(s, legacy: false, skirtTuning: RoeBoneCloth.SwingTuning(s.id)), drape = RoeBoneCloth.SwingDrape,
            },
            new Solver { name = "bone_legacy", title = "Bone cloth (10-02)", make = s => new RoeBoneCloth(s, legacy: true) },
            new Solver
            {
                name = "doa6", title = "DOA6 physics", make = s => new RoeDoaPhysics(s),
                covers = (s, kind) => RoeDoaPhysics.Covers(s, kind),
            },
            new Solver { name = "doa5lr", title = "DOA5LR-style spring net", skinnedSkirt = true, make = s => new RoeBoneCloth(s, legacy: false, doa5: true) },
            new Solver
            {
                name = "kawaii", title = "KawaiiPhysics (Vindictus)", make = s => new RoeKawaiiPhysics(s),
                covers = (s, kind) => RoeKawaiiPhysics.Covers(s, kind),
            },
            new Solver
            {
                name = "stellar", title = "Stellar Blade's own (spring bones, KawaiiPhysics, PhysX bodies)", make = s => new RoeSbPhysics(s), ownSkirt = true,
                covers = (s, kind) => RoeSbPhysics.Covers(s, kind),
            },
#if MAGICACLOTH2
            new Solver
            {
                name = "magica", title = "Magica Cloth 2", skinnedSkirt = true, make = s => new RoeMagicaCloth(s),
                covers = (s, kind) => RoeMagicaCloth.Covers(s, kind),
            },
            new Solver
            {
                name = "magica_full", title = "Magica Cloth 2, everything (its own presets; the game's clips too)", skinnedSkirt = true,
                overGameClips = true, noDrape = true, make = s => new RoeMagicaCloth(s, presets: true),
                covers = (s, kind) => RoeMagicaCloth.Covers(s, kind),
            },
            // Rise of Eros' own (user 10-11 "就完全遵从ROE本身的物理的角度再看看"): its hand keys wherever it has them, and where it has
            // none its own Magica Cloth 2 - her lobby prefab's components as the game set them up (Luf: one BoneCloth on her six
            // hair chains, its colliders), every other chain on the same numbers, a line per chain as the game builds them.  Of
            // ours only the skirt's pose that follows the legs (RoeSkirtRig, fitted to the game's own keys) stays, as the pose
            // Magica holds the skirt to: on the stance's pose its stiff top stood out from the hips like boards once the legs
            // moved.  No drape, no tuning.
            new Solver
            {
                name = "roe", title = "Rise of Eros' own (its keys; its Magica Cloth 2 where it has none)", skinnedSkirt = true, noDrape = true,
                make = s => new RoeMagicaCloth(s, game: true),
                covers = (s, kind) => RoeMagicaCloth.Covers(s, kind),
            },
#endif
        };

        public static Solver Find(string name) => All.FirstOrDefault(s => s.name == name);
    }

    /// <summary>
    /// The physics setups, in the order F4 goes through them: which solver takes which kind (user 10-04: "最好做成可插拔的设计，
    /// 后续我买到magic cloth也能替换").  A kind not listed goes to the setup's fallback; a kind its solver cannot take on a
    /// fighter (DOA6 data on a ROE character) goes to "bone".
    ///   auto          each fighter her own game's: a DOA6 character on "doa6", Vindictus' Fiona on "kawaii", Stellar Blade's
    ///                 Eve on "stellar", the others on "magica_style" (the default)
    ///   kawaii        a Vindictus character on her game's KawaiiPhysics settings (other kinds: the bone cloth)
    ///   stellar       Stellar Blade's Eve on her game's own physics (other kinds: the bone cloth)
    ///   magica_style  everything on the Magica-style bone cloth
    ///   doa6          a DOA6 character on her game's own systems: soft-body breasts, cloth grids, swing hair
    ///   doa6_breasts  DOA6 soft-body breasts, everything else on the bone cloth (to compare one kind at a time)
    ///   doa5lr_style  skirts, hair, ribbons, chains on DOA5LR's spring net; breasts on the bone cloth (DOA5LR has presets for them)
    ///   legacy        the 10-02 bone cloth
    ///   off           no simulation: skirts and hair keep the battle stance's keys
    ///   magica        Magica Cloth 2 for everything (when installed)
    ///   magica_full   Magica Cloth 2 for everything on its own presets, in the game's own clips too, without our drape
    ///                 (when installed; Inase's pick under auto then - Preferred)
    ///   roe           Rise of Eros' own: its keys in its clips and its stance, its lobby's Magica Cloth 2 elsewhere, breasts
    ///                 keyed (when installed)
    /// </summary>
    public static class RoeClothBackends
    {
        public static readonly string[] Kinds = { "breast", "body", "hair", "skirt", "ribbon", "chain" };

        public class Backend
        {
            public string name, title;
            public string fallback;             // the solver for kinds not in byKind; null: none (off)
            public Dictionary<string, string> byKind = new Dictionary<string, string>();

            public string SolverFor(string kind) => byKind.TryGetValue(kind, out var s) ? s : fallback;
            /// <summary>The share of the drape her skirt takes under this setup (RoeClothSolvers.Solver.drape).</summary>
            public float DrapeFor(string id) => RoeClothSolvers.Find(SolverFor("skirt") ?? "")?.drape?.Invoke(id) ?? 1f;

            /// <summary>The skirt follows the legs first (RoeSkirtRig) when its solver wants it.</summary>
            public bool skinnedSkirt => RoeClothSolvers.Find(SolverFor("skirt") ?? "")?.skinnedSkirt ?? false;

            /// <summary>The skirt hangs as its game has it (RoeClothSolvers.Solver.ownSkirt).</summary>
            public bool ownSkirt => RoeClothSolvers.Find(SolverFor("skirt") ?? "")?.ownSkirt ?? false;

            /// <summary>The cloth moves in the game's own clips too (RoeClothSolvers.Solver.overGameClips).</summary>
            public bool overGameClips => RoeClothSolvers.Find(SolverFor("skirt") ?? "")?.overGameClips ?? false;

            /// <summary>Our drape off: the solver hangs the skirt (RoeClothSolvers.Solver.noDrape).</summary>
            public bool noDrape => RoeClothSolvers.Find(SolverFor("skirt") ?? "")?.noDrape ?? false;

            /// <summary>The solvers on one fighter, one per solver used, each with its kinds; null when nothing is simulated.</summary>
            public IRoeCloth Make(RoeClothScope scope)
            {
                var assign = new Dictionary<string, List<string>>();
                foreach (var kind in Kinds)
                {
                    string name = SolverFor(kind);
                    var solver = name != null ? RoeClothSolvers.Find(name) : null;
                    if (solver == null)
                        continue;
                    if (solver.covers != null && !solver.covers(scope, kind))
                        solver = RoeClothSolvers.Find("bone");
                    if (!assign.TryGetValue(solver.name, out var list))
                        assign[solver.name] = list = new List<string>();
                    list.Add(kind);
                }
                var parts = assign.Select(kv => (solver: RoeClothSolvers.Find(kv.Key), kinds: kv.Value))
                    .Select(p => (p.solver, p.kinds, cloth: p.solver.make(scope.With(p.kinds)))).Where(p => p.cloth != null).ToList();
                if (parts.Count == 0)
                    return null;
                return parts.Count == 1 && parts[0].kinds.Count == Kinds.Length ? parts[0].cloth : new RoeClothRouter(parts);
            }
        }

        static Backend All_(string name, string title, string solver) => new Backend { name = name, title = title, fallback = solver };

        public static readonly List<Backend> All = new List<Backend>
        {
            All_("auto", "Each her own game's (DOA6, Vindictus' KawaiiPhysics, Stellar Blade's, else bone cloth; Luf's skirt swings like her game's)", "bone"),
            All_("magica_style", "Magica-style bone cloth", "bone"),
            All_("swing", "Bone cloth, the skirt swinging like her game's hand keys (tuned to them; others as magica_style)", "bone_swing"),
            All_("doa6", "DOA6 physics (soft bodies, cloth grids)", "doa6"),
            new Backend { name = "doa6_breasts", title = "DOA6 soft breasts + bone cloth", fallback = "bone", byKind = { { "breast", "doa6" }, { "body", "doa6" } } },
            new Backend { name = "doa5lr_style", title = "DOA5LR-style spring net (breasts: bone cloth)", fallback = "doa5lr", byKind = { { "breast", "bone" }, { "body", "bone" } } },
            All_("kawaii", "KawaiiPhysics with the game's settings (Vindictus)", "kawaii"),
            All_("stellar", "Stellar Blade's own physics (Eve)", "stellar"),
            All_("legacy", "Bone cloth (10-02)", "bone_legacy"),
            All_("off", "No cloth", null),
#if MAGICACLOTH2
            All_("magica", "Magica Cloth 2", "magica"),
            All_("magica_full", "Magica Cloth 2, everything (its presets; also in the game's clips)", "magica_full"),
            // breasts and other flesh stay on the keys: ROE keys them in every clip ("keys" is no solver: nothing simulates them)
            new Backend
            {
                name = "roe", title = "Rise of Eros' own: its keys, its lobby's Magica Cloth 2 where it has none (breasts keyed)", fallback = "roe",
                byKind = { { "breast", "keys" }, { "body", "keys" } },
            },
#endif
        };

        /// <summary>
        /// A fighter's own pick under "auto", when the build has its solver (user 10-05: "inase完全用magic cloth 2": Inase on
        /// Magica Cloth 2 for everything when the plugin is installed; without it she stays on the bone cloth).  Only in play
        /// mode - the game, the exe: Magica moves on the player loop, and the editor's batch recordings step the fight by hand.
        /// </summary>
        public static readonly Dictionary<string, string> Preferred = new Dictionary<string, string> { ["a08"] = "magica_full" };

        public static Backend Find(string name) => All.FirstOrDefault(b => b.name == name) ?? All[0];

        /// <summary>The setup a fighter takes for a choice: "auto" picks her own game's.</summary>
        public static string Resolve(string name, Animator animator, string id = null) =>
            name != "auto" ? name
            : id != null && Application.isPlaying && Preferred.TryGetValue(id, out var own) && All.Any(b => b.name == own) ? own
            : animator != null && animator.GetComponent<RoeDoaRig>() != null ? "doa6"
                                  : animator != null && animator.GetComponent<RoeSbRig>() != null ? "stellar"
                                  : animator != null && animator.GetComponent<RoeKawaiiRig>() != null ? "kawaii"
                                  // a ROE fighter whose skirt is tuned to her game's hand keys (user 10-11: "让动捕动作里的裙子也甩起来")
                                  : id != null && RoeBoneCloth.SwingTunings.ContainsKey(id) ? "swing" : "magica_style";

        public static string Next(string name)
        {
            int i = All.FindIndex(b => b.name == name);
            return All[(i + 1) % All.Count].name;
        }
    }

    /// <summary>Several solvers on one fighter, each on its own kinds (RoeClothBackends.Backend.Make).</summary>
    public class RoeClothRouter : IRoeCloth, IDisposable
    {
        public void Dispose()
        {
            foreach (var p in parts)
                (p.cloth as IDisposable)?.Dispose();
        }

        readonly List<(RoeClothSolvers.Solver solver, List<string> kinds, IRoeCloth cloth)> parts;
        float weight = 1f;

        public RoeClothRouter(List<(RoeClothSolvers.Solver solver, List<string> kinds, IRoeCloth cloth)> parts)
        {
            this.parts = parts;
        }

        public string Report => string.Join(" | ", parts.Select(p => $"{p.solver.name} [{string.Join(" ", p.kinds)}]: {p.cloth.Report}"));

        public float Weight
        {
            get => weight;
            set
            {
                weight = value;
                foreach (var p in parts)
                    p.cloth.Weight = value;
            }
        }

        public void Rest()
        {
            foreach (var p in parts)
                p.cloth.Rest();
        }

        public void Step(float dt, float floor)
        {
            foreach (var p in parts)
                p.cloth.Step(dt, floor);
        }

        public void Reset()
        {
            foreach (var p in parts)
                p.cloth.Reset();
        }

        /// <summary>The first part of a type (the bone cloth, for checks).</summary>
        public T Part<T>() where T : class => parts.Select(p => p.cloth as T).FirstOrDefault(c => c != null);

        /// <summary>The part of a type that simulates a kind, else the first of that type.</summary>
        public T PartFor<T>(string kind) where T : class =>
            parts.Where(p => p.kinds.Contains(kind)).Select(p => p.cloth as T).FirstOrDefault(c => c != null) ?? Part<T>();
    }
}
