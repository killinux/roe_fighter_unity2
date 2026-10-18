using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace RoeFighter
{
    /// <summary>
    /// Bone cloth: skirts, hair, chains and breasts that swing with the body.
    ///
    /// The method of Magica Cloth 2's BoneCloth (docs/magica-cloth-2.md), first ported from the Blender
    /// add-on bone_cloth of the ripper_tpose repository (scripts/blender_addons/bone_cloth/core.py).  The
    /// animated pose is the baseline: each bone tail is a particle; every step
    ///   1. inertia: the particles ride the body except the share of its motion the cloth "feels", never
    ///      above the speed limits - world inertia (the fighter moving over the floor, measured on its
    ///      root) and local inertia (the bone the chain hangs on moving in the fighter's frame, i.e. the
    ///      animation; less near the root), as Magica splits them;
    ///   2. Verlet integration with damping, a particle speed limit and gravity;
    ///   3. root to tip, a few iterations: angle restoration (turn a share back towards the animated
    ///      direction, most of that move not becoming speed), angle limit (root strict, tip loose),
    ///      the cross links (chains that are one piece of cloth keep their distance, as Magica's mesh
    ///      connection does), colliders (capsules on the thighs, calves and hips; bone segments and
    ///      cross links collide as capsules, so a leg cannot slip between two chains; as deep as the
    ///      animation itself goes is allowed), backstop (no point sinks behind its animated place
    ///      towards the body), max distance from the animated place, tether (a chain does not fold up
    ///      towards its root), the floor.
    /// Bone lengths are rigid.  The result is written as bone rotations, blended by a weight.
    ///
    /// Chains are found by name (skirt / hair, braid, bangs / ribbons / chains, waist ornaments / breasts);
    /// a bone with several simulated children is not simulated itself (its children hang on it).
    /// Chains of one set are linked when the skin has triangles across them.  The ROE skirts are keyed
    /// by hand in the game's own clips; the fight blends this in under the motion-capture moves only,
    /// over the skirt pose RoeSkirtRig makes follow the legs.  legacy = the 10-02 version (independent
    /// chains, one inertia on the anchor bone), kept as a backend to compare.  doa5 = the same chains and inertia under
    /// Dead or Alive 5 Last Round's spring net instead of the angle constraints (RoeBoneCloth.Doa5.cs).
    /// </summary>
    public partial class RoeBoneCloth : IRoeCloth
    {
        [Serializable]
        public class Settings
        {
            public float gravity = 5f, damping = 0.05f, radius = 0.02f;
            public float restore = 0.2f, attenuation = 0.8f;          // angle restoration and its velocity attenuation
            public float limitRoot = 25f, limitTip = 60f;               // angle limit, degrees
            // local inertia: the anchor bone's motion in the fighter's frame (legacy: all of its motion)
            public float inertia = 0.4f, moveLimit = 3f, turnLimit = 360f;
            public float depthInertia;                                  // a root particle feels this much less of it, a tip all of it
            // world inertia: the fighter moving over the floor
            public float worldInertia = 0.4f, worldMoveLimit = 3f, worldTurnLimit = 360f;
            public float particleLimit = 4f;
            public bool backstop = true;
            public float backstopRadius = 10f, backstopDistance = 0.02f;
            public float maxDistanceRoot, maxDistanceTip;               // from the animated place, by depth (m); 0 = off
            public float tether;                                        // no closer to the chain's root than (1 - tether) x the animated distance; 0 = off
            public bool links;                                          // link chains that share cloth
            public float linkStiffness = 1f;
            public bool edge = true;                                    // bone segments collide as capsules
            public float friction = 0.05f;
            public float pushAttenuation = 0.9f;                        // of a push out of a collider, the share that does not become speed
            public bool floor = true;
            public float stabilize = 0.1f;                              // after a reset no speed is carried over for this long (s)
            public int iterations = 4;
            // doa5 (RoeBoneCloth.Doa5.cs): DOA5LR's spring classes, stiffness words (x: shorter than rest, y: longer); 0 = none
            public Vector2 kAcross, kShear, kBend, kBendAcross, kLongRange;
            public float gravityWord = 1f, dampingWord, pullWord;     // the object's words p3 (x 9.8 m/s2), p4, p5 (readings guessed)
            public float pinRoot;                                       // the share of a chain from its root pinned to the animated pose
        }

        // Presets; Magica Cloth 2's numbers are per 1/90 s step.  The skirt is stiffer than the add-on's
        // (restore 0.2, damping 0.05, limits 25/60, inertia 0.4): with those g04's long panels, whose chains
        // start right at the waist, swung 12 degrees rms standing still and jumped 14-39 degrees in a step
        // (RoeFightProbe.SkirtSwing).  Magica-style: the skirt's own animation pose follows the legs now
        // (RoeSkirtRig), so the cloth is held near it (max distance) and feels less of the fighter's travel.
        public static Settings Skirt(bool legacy, string extra = null) => Tuned(Tuned(legacy
            ? new Settings { restore = 0.35f, damping = 0.15f, limitRoot = 5f, limitTip = 35f, inertia = 0.3f, stabilize = 0f }
            : new Settings
            {
                restore = 0.35f, damping = 0.15f, limitRoot = 5f, limitTip = 35f,
                inertia = 0.3f, moveLimit = 3f, turnLimit = 360f, depthInertia = 0.3f,
                worldInertia = 0.15f, worldMoveLimit = 1f, worldTurnLimit = 120f,
                maxDistanceRoot = 0.03f, maxDistanceTip = 0.25f, tether = 0.15f, links = true,
            }, extra), SkirtTuning);

        /// <summary>For checks: "key=value,..." over the skirt preset (see Tuned).</summary>
        public static string SkirtTuning = "";

        /// <summary>
        /// The skirt swinging like her game's hand keys (user 10-11: "做吧，让动捕动作里的裙子也甩起来"; the "swing" setup, which
        /// "auto" takes for these fighters): per fighter, settings over the skirt preset ("drape" is the share of
        /// RoeSkirtRig's drape), found by RoeSkirtTune against her game's own keys - g04: 65.1 -> 62.8 deg RMS on skill 1 and
        /// 2, held out (super, hurt, react_02) 43.6 -> 42.9.  She feels her own turns and kicks fully (inertia 1), is pulled
        /// back to the animation pose less (restore 0.1) and may swing further (limitTip 60, root 12 cm off the pose); her
        /// gravity stays the preset's 5 (the search's 1.5 scored 0.4 deg better, but in her crouched guard the floor-long
        /// panels reach the floor and, that light, the narrow left one curled out sideways from it instead of falling and
        /// lying there - user 10-11: "luf的左边的裙子为什么翘起来了一点").  She still hangs fully (drape 1): the first version took
        /// the search's drape 0.3 (58.2 deg), but then 70 % of a panel's pose was the skirt rig's fitted one, which carries
        /// g04's front panel on her right thigh (0.55-0.65) - a kick lifted the panel with the leg as if glued to it (user
        /// 10-11: "似乎和裙子有粘连").  Override, the exe too: -roeSkirtSwing "g04:restore=0.1,...|a08:..." (an empty entry
        /// takes a fighter off; "drape=0.3" gives the leg-carried look back).
        /// </summary>
        public static readonly Dictionary<string, string> SwingTunings = new Dictionary<string, string>
        {
            ["g04"] = "restore=0.1,inertia=1,worldInertia=0.75,limitTip=60,maxDistanceRoot=0.12",
        };

        public static string SwingTuning(string id) => id != null && SwingTunings.TryGetValue(id, out var t) ? t : "";

        /// <summary>Her swing tuning's share of the drape (1 when it names none).</summary>
        public static float SwingDrape(string id)
        {
            foreach (var pair in SwingTuning(id).Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = pair.Split('=');
                if (kv.Length == 2 && kv[0].Trim() == "drape" &&
                    float.TryParse(kv[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v))
                    return Mathf.Clamp01(v);
            }
            return 1f;
        }

        /// <summary>-roeSkirtSwing "g04:restore=0.1,...|a08:...": entries over SwingTunings; an empty one takes a fighter off.</summary>
        public static void SwingFromArg(string arg)
        {
            foreach (var entry in (arg ?? "").Split('|'))
            {
                int colon = entry.IndexOf(':');
                if (colon <= 0)
                    continue;
                string id = entry.Substring(0, colon).Trim(), tuning = entry.Substring(colon + 1).Trim();
                if (tuning.Length == 0)
                    SwingTunings.Remove(id);
                else
                    SwingTunings[id] = tuning;
            }
        }

        static Settings Tuned(Settings s, string tuning)
        {
            foreach (var pair in (tuning ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = pair.Split('=');
                if (kv.Length != 2 || !float.TryParse(kv[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v))
                    continue;
                switch (kv[0].Trim())
                {
                    case "gravity": s.gravity = v; break;
                    case "damping": s.damping = v; break;
                    case "restore": s.restore = v; break;
                    case "attenuation": s.attenuation = v; break;
                    case "limitRoot": s.limitRoot = v; break;
                    case "limitTip": s.limitTip = v; break;
                    case "inertia": s.inertia = v; break;
                    case "depthInertia": s.depthInertia = v; break;
                    case "worldInertia": s.worldInertia = v; break;
                    case "worldMoveLimit": s.worldMoveLimit = v; break;
                    case "particleLimit": s.particleLimit = v; break;
                    case "pushAttenuation": s.pushAttenuation = v; break;
                    case "maxDistanceTip": s.maxDistanceTip = v; break;
                    case "maxDistanceRoot": s.maxDistanceRoot = v; break;
                    case "moveLimit": s.moveLimit = v; break;
                    case "turnLimit": s.turnLimit = v; break;
                    case "worldTurnLimit": s.worldTurnLimit = v; break;
                    case "tether": s.tether = v; break;
                    case "links": s.links = v > 0f; break;
                    case "floor": s.floor = v > 0f; break;
                    case "linkStiffness": s.linkStiffness = v; break;
                }
            }
            return s;
        }

        /// <summary>Ribbons and sashes (g05's Riband_*): light like hair, the segments collide (they pass the legs).</summary>
        public static Settings Ribbon(bool legacy)
        {
            var s = Hair(legacy);
            s.radius = 0.02f;
            s.edge = true;
            return s;
        }

        /// <summary>
        /// A long sleeve (b14's Sleeve_R1: eleven bones from her right forearm down past the knee): a ribbon that hangs.  Nothing
        /// pulls it back to its animated pose and only its root keeps close to the forearm (25 degrees, 150 at the tip) - with a
        /// ribbon's (15 / 50 and a pull of 0.3) it kept the shape it has on her lowered arm in her stance, so a raised forearm
        /// (blocking, a punch) stood it up over her head like a board.
        /// </summary>
        public static Settings Sleeve(bool legacy)
        {
            var s = Ribbon(legacy);
            s.restore = 0f;
            s.limitRoot = 25f;
            s.limitTip = 150f;
            s.inertia = 0.5f;
            s.worldInertia = 0.5f;
            return s;
        }

        static readonly Regex SleeveName = new Regex(@"^sleeve_", RegexOptions.IgnoreCase);

        // hair, chains and breasts: the add-on's presets, world inertia as their old inertia on the anchor
        public static Settings Hair(bool legacy) => new Settings
        {
            radius = 0.015f, restore = 0.3f, limitRoot = 15f, limitTip = 50f, inertia = 0.3f, moveLimit = 2f,
            worldInertia = 0.3f, worldMoveLimit = 2f, particleLimit = 3f, backstopDistance = 0.01f, edge = false,
            stabilize = legacy ? 0f : 0.1f,
        };
        public static Settings Chain(bool legacy) => new Settings
        {
            radius = 0.012f, restore = 0.12f, limitRoot = 30f, limitTip = 80f, inertia = 0.5f, moveLimit = 3f,
            worldInertia = 0.5f, worldMoveLimit = 3f, particleLimit = 4f, backstop = false, edge = false,
            stabilize = legacy ? 0f : 0.1f,
        };
        public static Settings Breast(bool legacy) => new Settings
        {
            gravity = 3f, radius = 0.03f, restore = 0.35f, attenuation = 0.5f, limitRoot = 6f, limitTip = 12f,
            inertia = 0.6f, moveLimit = 4f, turnLimit = 720f, worldInertia = 0.6f, worldMoveLimit = 4f, worldTurnLimit = 720f,
            particleLimit = 2f, backstop = false, edge = false, floor = false, stabilize = legacy ? 0f : 0.1f,
        };

        /// <summary>The kind of cloth a ROE bone's name says it is (skirt, hair, ribbon ...), or null (for checks: FighterRig.MeasureKeyKinds).</summary>
        public static string RoeKindOf(string bone)
        {
            foreach (var (kind, name) in Kinds)
                if (name.IsMatch(bone))
                    return kind;
            return null;
        }

        // (b14's long right sleeve, Sleeve_R1 - eleven bones from the forearm down past her knee - is a ribbon with its own
        // settings, Sleeve; her belt's tassels, Tassel_L1 / R1, swing like chains)
        static readonly (string kind, Regex name)[] Kinds =
        {
            ("chain", new Regex(@"skirt_chain|^chain\d|chain_arm|dec_wrist|^tassel_", RegexOptions.IgnoreCase)),
            ("skirt", new Regex(@"skirt", RegexOptions.IgnoreCase)),
            ("hair", new Regex(@"hair|braid|bangs|hari", RegexOptions.IgnoreCase)),
            ("ribbon", new Regex(@"riband|ribbon|^sleeve_", RegexOptions.IgnoreCase)),
            ("breast", new Regex(@"breast|chest_[lr]", RegexOptions.IgnoreCase)),
        };

        // Vindictus (UE5 / MetaHuman rigs, first match wins; "" = not cloth): the face rig's FACIAL_*Hair* joints are hairline
        // skin; a *_root only hangs a group (Fiona_hair_root carries the whole scalp: simulated, the bob would swing as one);
        // of the breast rig only breast_physics_02 / 03 swing - the eight rays under each are shape helpers that ride on
        // them; feathers (PCF_005's tiara, collar and arm bands) and the tiara's earrings swing like hair (in her game one
        // KawaiiPhysics node holds the tiara's feathers and earrings together: kind "hair" in both).
        static readonly (string kind, Regex name)[] UeKinds =
        {
            ("", new Regex(@"^FACIAL_|_root$|^breast_[lr]$|^breast_physics_0[145]_|^breast_physics_0[23]_[a-h]_", RegexOptions.IgnoreCase)),
            ("breast", new Regex(@"^breast_physics_0[23]_[lr]$", RegexOptions.IgnoreCase)),
            ("skirt", new Regex(@"_skirt_", RegexOptions.IgnoreCase)),
            ("hair", new Regex(@"_hair_|feather|earring", RegexOptions.IgnoreCase)),
        };

        public class Capsule
        {
            public string name;
            public Transform a, b;
            public float ra, rb;
            public bool leg;                    // thigh or calf (the checks measure the skirt against these)
        }

        class ChainSet
        {
            public string name, kind;
            public bool sleeve;                 // a long sleeve: a ribbon, but on the game's keys in its clips (not AlwaysKinds)
            public Settings s;
            public Transform anchor;
            public Vector3 anchorAxis;          // the anchor's local axis that pointed up at rest: the body axis for the backstop
            public Transform[] bones;
            public int[] parent, root;          // root: the first particle of the bone's chain
            public float[] length, limit, depth, maxDistance;   // limit in radians, by depth
            public Vector3[] tailLocal;
            public Quaternion[] restLocal;
            public int[][] levels;
            public int[] levelOf;
            public Capsule[] capsules;
            public List<(int i, int j)>[] links;    // per level: particles linked across chains
            public string linkReport = "";
            public float[] restPenetration;     // how deep each bone segment lies in the legs in the stance (for checks)
            public Dictionary<(int, int), float> linkRestPenetration = new Dictionary<(int, int), float>();
            // how deep each bone segment / tail / cross link lies in each capsule in the stance: as deep is allowed
            public float[][] restEdge, restPoint;
            public Dictionary<(int, int), float[]> restLink = new Dictionary<(int, int), float[]>();
            public float[] allow;
            // state
            public bool ready;
            public float stabilizeLeft;
            public Vector3[] x, xOld, hb, tb, hbPrev, tbPrev, head, g;
            public Quaternion[] rb, rot, d;
            public Vector3 anchorPos, anchorPosPrev;
            public Quaternion anchorRot, anchorRotPrev;
            // doa5: the spring net and each particle's pin to its animated place
            public List<Spring5> springs5;
            public float[] pin;
        }

        readonly List<ChainSet> chains = new List<ChainSet>();
        public readonly List<Capsule> capsules = new List<Capsule>();
        readonly Transform world;
        readonly bool legacy, doa5;
        Vector3 worldPos, worldPosPrev;
        Quaternion worldRot = Quaternion.identity, worldRotPrev = Quaternion.identity;
        public float weight = 1f;
        public float Weight { get => weight; set => weight = value; }

        /// <summary>
        /// Kinds that are simulated over the game's own clips too (at alwaysWeight), not only under the motion capture
        /// (Weight): g05's sashes - her battle clips key them almost rigid, and in a hurt they swung out a metre like
        /// boards.  The keys stay their animated pose: the angle limits hold the cloth near them, so a skill's swirl
        /// is still there, only it bends and trails.
        /// </summary>
        public static readonly HashSet<string> AlwaysKinds = new HashSet<string> { "ribbon" };
        readonly string skirtTuning;            // this fighter's settings over the skirt preset (the "swing" setup)
        public float alwaysWeight = 1f;
        public static bool NoColliders, NoBackstop;     // for checks (RoeFightProbe.SkirtSwing -roeVariant)
        /// <summary>
        /// The skirt's animated pose already rests on the skin (RoeSkirtRig.Drape; FighterRig sets this each
        /// step): its chains may lie in the leg capsules as deep as that pose does - the capsules are a
        /// little fatter than the legs and would push a hanging skirt back out.
        /// </summary>
        public bool skirtOnSkin;
        public string Report { get; private set; }

        /// <summary>
        /// Finds the chains under a humanoid model in its current pose (the battle stance) and measures
        /// the leg, hip and torso colliders on its skin.  world: the fighter's root (world inertia);
        /// exclude: bones something else drives; legacy: the 10-02 solver.
        /// </summary>
        public RoeBoneCloth(Animator animator, Transform world, ICollection<Transform> exclude, bool legacy = false)
            : this(new RoeClothScope { animator = animator, world = world, exclude = exclude }, legacy)
        {
        }

        /// <summary>
        /// The same for a physics setup (RoeClothBackends): only the kinds in scope.kinds (another solver takes the rest),
        /// and the loose bones by scope.kindOf when the rig's bone names do not tell (DOA6's bone_&lt;id&gt;), its sheets by
        /// scope.anchorOf / neighbours.  doa5: Dead or Alive 5 Last Round's spring net (RoeBoneCloth.Doa5.cs).
        /// </summary>
        public RoeBoneCloth(RoeClothScope scope, bool legacy, bool doa5 = false, string skirtTuning = null)
        {
            this.skirtTuning = skirtTuning;
            var animator = scope.animator;
            var world = scope.world;
            var exclude = scope.exclude;
            this.world = world != null ? world : animator.transform;
            this.legacy = legacy && !doa5;
            this.doa5 = doa5;
            var human = new Dictionary<Transform, HumanBodyBones>();
            foreach (HumanBodyBones hb in Enum.GetValues(typeof(HumanBodyBones)))
                if (hb != HumanBodyBones.LastBone && animator.GetBoneTransform(hb) != null)
                    human[animator.GetBoneTransform(hb)] = hb;
            var skin = SkinPoints(animator);
            var kindOf = scope.kindOf != null
                ? scope.kindOf.Where(kv => kv.Key != null && !human.ContainsKey(kv.Key) && (exclude == null || !exclude.Contains(kv.Key)))
                    .ToDictionary(kv => kv.Key, kv => kv.Value)
                : Classify(animator.transform, human.Keys.ToList(), exclude);
            foreach (var t in kindOf.Keys.Where(t => !scope.Takes(kindOf[t])).ToList())
                kindOf.Remove(t);

            // colliders: thighs, calves, the hips; the torso for hair
            Transform B(HumanBodyBones b) => animator.GetBoneTransform(b);
            var legs = new List<Capsule>();
            foreach (var (up, low, foot, side) in new[]
            {
                (HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, "left"),
                (HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, "right"),
            })
            {
                if (B(up) == null || B(low) == null || B(foot) == null)
                    continue;
                var thigh = Measure($"{side} thigh", B(up), B(low), skin, 70f, 0.07f, 0.05f);
                var calf = Measure($"{side} calf", B(low), B(foot), skin, 70f, 0.05f, 0.035f);
                thigh.leg = calf.leg = true;
                legs.Add(thigh);
                legs.Add(calf);
            }
            if (B(HumanBodyBones.LeftUpperLeg) != null && B(HumanBodyBones.RightUpperLeg) != null)
            {
                var hips = Measure("hips", B(HumanBodyBones.LeftUpperLeg), B(HumanBodyBones.RightUpperLeg), skin, 60f, 0.09f, 0.09f);
                hips.ra = hips.rb = Mathf.Max(hips.ra, hips.rb);
                legs.Add(hips);
            }
            capsules.AddRange(legs);
            var neck = B(HumanBodyBones.Neck) != null ? B(HumanBodyBones.Neck) : B(HumanBodyBones.Head);
            var torso = B(HumanBodyBones.Hips) != null && neck != null ? Measure("torso", B(HumanBodyBones.Hips), neck, skin, 50f, 0.12f, 0.1f) : null;
            if (torso != null)
                capsules.Add(torso);

            // chain sets: one per kind and anchor (a rig's own sheet: the anchor it gives, every column of a DOA6 skirt on the hips)
            var meshes = animator.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var group in kindOf.Keys.GroupBy(t => (kindOf[t], scope.anchorOf != null && scope.anchorOf.TryGetValue(t, out var given) ? given : Anchor(t, kindOf))))
            {
                var (kind, anchor) = group.Key;
                if (anchor == null)
                    continue;
                var bones = group.OrderBy(Depth).ToArray();
                bool sleeve = kind == "ribbon" && bones.Any(b => SleeveName.IsMatch(b.name));
                var s = kind == "skirt" ? Skirt(this.legacy, skirtTuning) : kind == "hair" ? Hair(this.legacy)
                      : kind == "ribbon" ? (sleeve ? Sleeve(this.legacy) : Ribbon(this.legacy))
                      : kind == "chain" ? Chain(this.legacy) : Breast(this.legacy);
                if (doa5)
                    s = Doa5(kind, s);
                Transform owner = anchor;
                while (owner != null && !human.ContainsKey(owner))
                    owner = owner.parent;
                var lower = owner != null && (human[owner] == HumanBodyBones.Hips || human[owner] == HumanBodyBones.Spine);
                // ribbons hang from anywhere (g05's from her arms, a metre down past her hips): the legs and the torso
                var cs = kind == "ribbon" ? legs.Concat(torso != null ? new[] { torso } : new Capsule[0]).ToArray()
                       : lower ? legs.ToArray()
                       : kind == "hair" && torso != null ? new[] { torso }
                       : new Capsule[0];
                // inertia and the backstop's body axis come from the body part the chain belongs to
                // (the pelvis for a skirt, even when its panels hang on a skirt bone)
                var c = Build($"{(sleeve ? "sleeve" : kind)} on {anchor.name}", kind, s, owner != null ? owner : anchor, bones, cs, skin);
                // a sleeve keeps to the game's keys in its clips, like a skirt (its game keys fling it about on purpose)
                c.sleeve = sleeve;
                if (s.links && !this.legacy && !(scope.neighbours != null && LinkNeighbours(c, scope.neighbours)))
                    Link(c, meshes);
                if (doa5)
                    BuildDoa5(c);
                chains.Add(c);
            }
            Report = $"{(doa5 ? "DOA5LR-style" : this.legacy ? "legacy" : "Magica-style")}: {chains.Count} chain sets, {chains.Sum(c => c.bones.Length)} bones: " +
                     string.Join(", ", chains.Select(c => $"{c.name} ({c.bones.Length}{c.linkReport})")) +
                     "; colliders " + string.Join(", ", capsules.Select(c => $"{c.name} r {c.ra:F3}/{c.rb:F3}"));
        }

        /// <summary>
        /// The cloth bones under a model, by kind (skirt, hair, chain, breast), from their names.  A bone with
        /// two or more cloth children of its kind hangs them and is not simulated itself; a skirt piece of a
        /// single bone (g04's waist ornaments) is left out - the game keeps it on the pelvis.
        /// </summary>
        public static Dictionary<Transform, string> Classify(Transform root, ICollection<Transform> human, ICollection<Transform> exclude)
        {
            var kindOf = new Dictionary<Transform, string>();
            var all = root.GetComponentsInChildren<Transform>(true);
            bool ue = all.Any(t => t.name == "pelvis") && all.Any(t => t.name == "spine_01");
            foreach (var t in all)
            {
                if (t == root || human.Contains(t) || (exclude != null && exclude.Contains(t)) || t.GetComponent<Renderer>() != null)
                    continue;
                if (t.name.EndsWith("_ALL") || t.name.Contains("Dummy") || t.name.Contains("Nub") || t.name.Contains("nub"))
                    continue;
                foreach (var (kind, re) in ue ? UeKinds : Kinds)
                    if (re.IsMatch(t.name))
                    {
                        if (kind.Length > 0)
                            kindOf[t] = kind;
                        break;
                    }
            }
            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (var t in kindOf.Keys.ToList())
                {
                    int kids = 0;
                    foreach (Transform c in t)
                        if (kindOf.TryGetValue(c, out var k) && k == kindOf[t])
                            kids++;
                    if (kids >= 2)
                    {
                        kindOf.Remove(t);
                        changed = true;
                    }
                }
            }
            foreach (var t in kindOf.Keys.Where(t => kindOf[t] == "skirt").ToList())
            {
                bool hung = t.parent != null && kindOf.TryGetValue(t.parent, out var pk) && pk == "skirt";
                bool hangs = t.Cast<Transform>().Any(c => kindOf.TryGetValue(c, out var ck) && ck == "skirt");
                if (!hung && !hangs)
                    kindOf.Remove(t);
            }
            return kindOf;
        }

        static int Depth(Transform t)
        {
            int d = 0;
            for (; t != null; t = t.parent)
                d++;
            return d;
        }

        static Transform Anchor(Transform t, Dictionary<Transform, string> kindOf)
        {
            var p = t.parent;
            while (p != null && kindOf.TryGetValue(p, out var k) && k == kindOf[t])
                p = p.parent;
            return p;
        }

        /// <summary>Every skin vertex in the current pose (world), with the bone it hangs on most.</summary>
        public static List<(Vector3 p, Transform bone)> SkinPoints(Component root)
        {
            var points = new List<(Vector3, Transform)>();
            var mesh = new Mesh();
            foreach (var r in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (r.sharedMesh == null)
                    continue;
                r.BakeMesh(mesh, true);
                var v = mesh.vertices;
                var w = r.sharedMesh.boneWeights;
                var m = r.transform.localToWorldMatrix;   // (BakeMesh with scale: the renderer's own space - a DOA6 mesh's is in cm, scale 0.01)
                for (int i = 0; i < v.Length && i < w.Length; i++)
                    if (w[i].weight0 >= 0.5f && w[i].boneIndex0 < r.bones.Length && r.bones[w[i].boneIndex0] != null)
                        points.Add((m.MultiplyPoint3x4(v[i]), r.bones[w[i].boneIndex0]));
            }
            UnityEngine.Object.DestroyImmediate(mesh);
            return points;
        }

        /// <summary>
        /// Where a bone's tail is (world): its simulated child; else as far as the bone's own skin reaches;
        /// else on along its parent; else 6 cm along its x axis.
        /// </summary>
        public static Vector3 Tail(Transform b, Transform child, Transform parentBone, List<(Vector3 p, Transform bone)> skin)
        {
            Vector3 tail;
            if (child != null)
                tail = child.position;
            else
            {
                var mine = skin.Where(v => v.bone == b).Select(v => v.p).ToList();
                if (mine.Count >= 4)
                {
                    var mean = mine.Aggregate(Vector3.zero, (acc, v) => acc + v) / mine.Count;
                    tail = b.position + (mean - b.position) * 1.6f;
                }
                else if (parentBone != null)
                    tail = b.position + (b.position - parentBone.position).normalized * 0.6f * Vector3.Distance(b.position, parentBone.position);
                else
                    tail = b.position + b.rotation * Vector3.right * 0.06f;
            }
            if ((tail - b.position).sqrMagnitude < 1e-6f)
                tail = b.position + b.rotation * Vector3.right * 0.03f;
            return tail;
        }

        /// <summary>
        /// A capsule from bone a to bone b, as thick as the skin round it (a percentile of the distances of
        /// the vertices of every bone that lies on the segment: twist and knee helpers carry leg skin too).
        /// </summary>
        static Capsule Measure(string name, Transform a, Transform b, List<(Vector3 p, Transform bone)> skin, float pct, float r0, float r1)
        {
            Vector3 A = a.position, ab = b.position - a.position;
            float L2 = Mathf.Max(ab.sqrMagnitude, 1e-8f);
            var onSegment = new HashSet<Transform> { a };
            foreach (var (p, bone) in skin)
            {
                if (onSegment.Contains(bone))
                    continue;
                float t = Vector3.Dot(bone.position - A, ab) / L2;
                if (t >= -0.05f && t <= 0.95f && Vector3.Distance(bone.position, A + t * ab) <= 0.03f)
                    onSegment.Add(bone);
            }
            var halves = new[] { new List<float>(), new List<float>() };
            foreach (var (p, bone) in skin)
            {
                if (!onSegment.Contains(bone))
                    continue;
                float t = Mathf.Clamp01(Vector3.Dot(p - A, ab) / L2);
                float d = Vector3.Distance(p, A + t * ab);
                if (d < 0.3f)
                    halves[t < 0.5f ? 0 : 1].Add(d);
            }
            float Pct(List<float> list, float fallback)
            {
                if (list.Count < 8)
                    return fallback;
                list.Sort();
                return Mathf.Clamp(list[Mathf.Clamp((int)(list.Count * pct / 100f), 0, list.Count - 1)], 0.02f, 0.25f);
            }
            float ra = Pct(halves[0], halves[1].Count >= 8 ? -1f : r0), rb = Pct(halves[1], halves[0].Count >= 8 ? -1f : r1);
            if (ra < 0f) ra = rb;
            if (rb < 0f) rb = ra;
            return new Capsule { name = name, a = a, b = b, ra = ra, rb = rb };
        }

        ChainSet Build(string name, string kind, Settings s, Transform anchor, Transform[] bones, Capsule[] caps, List<(Vector3 p, Transform bone)> skin)
        {
            int n = bones.Length;
            var index = new Dictionary<Transform, int>();
            for (int i = 0; i < n; i++)
                index[bones[i]] = i;
            var c = new ChainSet
            {
                name = name, kind = kind, s = s, anchor = anchor, bones = bones, capsules = caps,
                parent = new int[n], root = new int[n], length = new float[n], limit = new float[n], depth = new float[n],
                maxDistance = new float[n], tailLocal = new Vector3[n], restLocal = new Quaternion[n], levelOf = new int[n],
                x = new Vector3[n], xOld = new Vector3[n], hb = new Vector3[n], tb = new Vector3[n], hbPrev = new Vector3[n],
                tbPrev = new Vector3[n], head = new Vector3[n], g = new Vector3[n], rb = new Quaternion[n], rot = new Quaternion[n],
                d = new Quaternion[n], restPenetration = new float[n], restEdge = new float[n][], restPoint = new float[n][],
                allow = new float[caps.Length],
            };
            // the anchor's axis that points up at rest
            var axes = new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            c.anchorAxis = axes.OrderByDescending(ax => Vector3.Dot(anchor.rotation * ax, Vector3.up)).First();
            for (int i = 0; i < n; i++)
            {
                var b = bones[i];
                c.parent[i] = b.parent != null && index.TryGetValue(b.parent, out int p) ? p : -1;
                c.levelOf[i] = c.parent[i] < 0 ? 0 : c.levelOf[c.parent[i]] + 1;
                c.root[i] = c.parent[i] < 0 ? i : c.root[c.parent[i]];
                c.restLocal[i] = b.localRotation;
                // the tail: the one simulated child; else where the bone's own skin reaches; else on along the parent
                Transform child = null;
                foreach (Transform k in b)
                    if (index.ContainsKey(k))
                        child = k;
                var tail = Tail(b, child, c.parent[i] >= 0 ? bones[c.parent[i]] : null, skin);
                c.tailLocal[i] = b.InverseTransformPoint(tail);
                c.length[i] = Vector3.Distance(b.position, tail);
                c.restPenetration[i] = LegDepth(b.position, tail, s.radius, caps);
                c.restEdge[i] = caps.Select(cap => Mathf.Max(0f, EdgeDepth(b.position, tail, s.radius, cap, out _, out _))).ToArray();
                c.restPoint[i] = caps.Select(cap => Mathf.Max(0f, PointDepth(tail, s.radius, cap, out _))).ToArray();
            }
            // depth along its own chain: 0 at the root, 1 at the deepest tip below that root
            var deepest = (int[])c.levelOf.Clone();
            for (int i = n - 1; i >= 0; i--)
                if (c.parent[i] >= 0)
                    deepest[c.parent[i]] = Mathf.Max(deepest[c.parent[i]], deepest[i]);
            var chainMax = (int[])deepest.Clone();
            for (int i = 0; i < n; i++)
                if (c.parent[i] >= 0)
                    chainMax[i] = chainMax[c.parent[i]];
            for (int i = 0; i < n; i++)
            {
                c.depth[i] = chainMax[i] > 0 ? (float)c.levelOf[i] / chainMax[i] : 0f;
                c.limit[i] = Mathf.Deg2Rad * Mathf.Lerp(s.limitRoot, s.limitTip, c.depth[i]);
                c.maxDistance[i] = Mathf.Lerp(s.maxDistanceRoot, s.maxDistanceTip, c.depth[i]);
            }
            int levels = n == 0 ? 0 : c.levelOf.Max() + 1;
            c.levels = Enumerable.Range(0, levels).Select(l => Enumerable.Range(0, n).Where(i => c.levelOf[i] == l).ToArray()).ToArray();
            c.links = Enumerable.Range(0, levels).Select(_ => new List<(int, int)>()).ToArray();
            return c;
        }

        /// <summary>
        /// Magica's mesh connection, found from the skin: two chains of the set are one piece of cloth when
        /// triangles of the mesh lie across them (their corners hang mostly on bones of different chains).
        /// Linked chains are joined level by level - the tails of their n-th bones - as far as both go.
        /// a08: each long side panel over its front, side and back chains, the two short back pieces; g04:
        /// the front panel's two chains (tools/magica_survey.py links).
        /// </summary>
        void Link(ChainSet c, SkinnedMeshRenderer[] meshes)
        {
            var chainOf = new Dictionary<Transform, int>();
            for (int i = 0; i < c.bones.Length; i++)
                chainOf[c.bones[i]] = c.root[i];
            if (chainOf.Values.Distinct().Count() < 2)
                return;
            var across = new Dictionary<(int, int), int>();
            var baked = new Mesh();
            foreach (var r in meshes)
            {
                var mesh = r.sharedMesh;
                if (mesh == null)
                    continue;
                var w = mesh.boneWeights;
                if (w.Length == 0)
                    continue;
                // the game's meshes are not Read/Write: in a player build their own triangles cannot be read
                // ("Not allowed to access triangles"), a baked copy's can
                r.BakeMesh(baked, true);
                // a vertex belongs to the chain that carries most of its weight (summed over the chain's bones)
                var chainOfVertex = new int[w.Length];
                var influences = new (int bone, float weight)[4];
                for (int v = 0; v < w.Length; v++)
                {
                    chainOfVertex[v] = -1;
                    influences[0] = (w[v].boneIndex0, w[v].weight0);
                    influences[1] = (w[v].boneIndex1, w[v].weight1);
                    influences[2] = (w[v].boneIndex2, w[v].weight2);
                    influences[3] = (w[v].boneIndex3, w[v].weight3);
                    int best = -1;
                    float bestWeight = 0.5f;
                    for (int a = 0; a < 4; a++)
                    {
                        if (influences[a].weight <= 0f || influences[a].bone >= r.bones.Length || r.bones[influences[a].bone] == null ||
                            !chainOf.TryGetValue(r.bones[influences[a].bone], out int ch))
                            continue;
                        float sum = 0f;
                        for (int b2 = 0; b2 < 4; b2++)
                            if (influences[b2].weight > 0f && influences[b2].bone < r.bones.Length && r.bones[influences[b2].bone] != null &&
                                chainOf.TryGetValue(r.bones[influences[b2].bone], out int ch2) && ch2 == ch)
                                sum += influences[b2].weight;
                        if (sum > bestWeight)
                        {
                            bestWeight = sum;
                            best = ch;
                        }
                    }
                    chainOfVertex[v] = best;
                }
                var tris = baked.triangles;
                for (int t = 0; t + 2 < tris.Length; t += 3)
                {
                    int a = chainOfVertex[tris[t]], b = chainOfVertex[tris[t + 1]], d = chainOfVertex[tris[t + 2]];
                    foreach (var (p, q) in new[] { (a, b), (b, d), (a, d) })
                        if (p >= 0 && q >= 0 && p != q)
                        {
                            var key = p < q ? (p, q) : (q, p);
                            across[key] = (across.TryGetValue(key, out int k) ? k : 0) + 1;
                        }
                }
            }
            UnityEngine.Object.DestroyImmediate(baked);
            var parts = new List<string>();
            foreach (var kv in across.Where(kv => kv.Value >= 6))
            {
                var (ra, rb) = kv.Key;
                var A = ChainOf(c, ra);
                var Bc = ChainOf(c, rb);
                int joined = 0;
                for (int l = 0; l < Mathf.Min(A.Count, Bc.Count); l++)
                {
                    int i = A[l], j = Bc[l];
                    var ti = c.bones[i].TransformPoint(c.tailLocal[i]);
                    var tj = c.bones[j].TransformPoint(c.tailLocal[j]);
                    if (Vector3.Distance(ti, tj) > 0.3f)
                        continue;
                    c.links[c.levelOf[i]].Add((i, j));
                    c.linkRestPenetration[(i, j)] = LegDepth(ti, tj, c.s.radius, c.capsules);
                    c.restLink[(i, j)] = c.capsules.Select(cap => Mathf.Max(0f, EdgeDepth(ti, tj, c.s.radius, cap, out _, out _))).ToArray();
                    joined++;
                }
                if (joined > 0)
                    parts.Add($"{c.bones[ra].name}~{c.bones[rb].name} x{joined}");
            }
            if (parts.Count > 0)
                c.linkReport = "; linked " + string.Join(" ", parts);
        }

        /// <summary>
        /// A rig's own sheet (RoeClothScope.neighbours, a DOA6 grid cloth's columns): chains whose first bones are neighbours
        /// are linked level by level, as the skin's triangles would link them.  False when the rig names none of this set's.
        /// </summary>
        bool LinkNeighbours(ChainSet c, IDictionary<Transform, Transform[]> neighbours)
        {
            var rootOf = new Dictionary<Transform, int>();
            for (int i = 0; i < c.bones.Length; i++)
                if (c.parent[i] < 0)
                    rootOf[c.bones[i]] = i;
            var pairs = new HashSet<(int, int)>();
            foreach (var kv in rootOf)
                if (neighbours.TryGetValue(kv.Key, out var near))
                    foreach (var t in near)
                        if (t != null && rootOf.TryGetValue(t, out int j) && j != kv.Value)
                            pairs.Add(kv.Value < j ? (kv.Value, j) : (j, kv.Value));
            if (pairs.Count == 0)
                return false;
            int joined = 0;
            foreach (var (ra, rb) in pairs)
            {
                var A = ChainOf(c, ra);
                var Bc = ChainOf(c, rb);
                for (int l = 0; l < Mathf.Min(A.Count, Bc.Count); l++)
                {
                    int i = A[l], j = Bc[l];
                    var ti = c.bones[i].TransformPoint(c.tailLocal[i]);
                    var tj = c.bones[j].TransformPoint(c.tailLocal[j]);
                    c.links[c.levelOf[i]].Add((i, j));
                    c.linkRestPenetration[(i, j)] = LegDepth(ti, tj, c.s.radius, c.capsules);
                    c.restLink[(i, j)] = c.capsules.Select(cap => Mathf.Max(0f, EdgeDepth(ti, tj, c.s.radius, cap, out _, out _))).ToArray();
                    joined++;
                }
            }
            c.linkReport = $"; the rig's sheet: {pairs.Count} neighbouring chains, {joined} links";
            return true;
        }

        /// <summary>The particles of the chain that starts at particle root, root to tip (the first child each time).</summary>
        static List<int> ChainOf(ChainSet c, int root)
        {
            var list = new List<int> { root };
            for (int i = root; ;)
            {
                int next = -1;
                for (int k = 0; k < c.bones.Length; k++)
                    if (c.parent[k] == i)
                    {
                        next = k;
                        break;
                    }
                if (next < 0)
                    return list;
                list.Add(next);
                i = next;
            }
        }

        /// <summary>A piece of cloth for another solver (RoeMagicaCloth): its chains' first bones - in link order
        /// when they are linked into one sheet - the settings, the bone it belongs to and its colliders.</summary>
        public class Piece
        {
            public string kind;
            public Settings settings;
            public List<Transform> roots = new List<Transform>();
            public bool mesh;                   // the chains are linked across (one sheet)
            public bool loop;                   // ... all the way round (a skirt's ring, a sleeve): the last linked to the first
            public Transform owner;
            public Capsule[] capsules;
        }

        /// <summary>
        /// The pieces of cloth: per chain set, the chains joined by cross links make one piece (a sheet,
        /// its chains in the order the links run), every other chain a piece of its own.
        /// </summary>
        public List<Piece> Pieces()
        {
            var pieces = new List<Piece>();
            foreach (var c in chains)
            {
                var roots = Enumerable.Range(0, c.bones.Length).Where(i => c.parent[i] < 0).ToList();
                var next = new Dictionary<int, HashSet<int>>();
                foreach (var r in roots)
                    next[r] = new HashSet<int>();
                foreach (var level in c.links)
                    foreach (var (i, j) in level)
                    {
                        next[c.root[i]].Add(c.root[j]);
                        next[c.root[j]].Add(c.root[i]);
                    }
                var done = new HashSet<int>();
                foreach (var r in roots)
                {
                    if (done.Contains(r))
                        continue;
                    // walk the sheet from one of its ends, so the order follows the links
                    var group = new List<int>();
                    var stack = new Stack<int>();
                    stack.Push(r);
                    var seen = new HashSet<int> { r };
                    while (stack.Count > 0)
                    {
                        int k = stack.Pop();
                        group.Add(k);
                        foreach (var m in next[k])
                            if (seen.Add(m))
                                stack.Push(m);
                    }
                    // (a ring has no end: from any of its chains)
                    int start = group.Any(k => next[k].Count <= 1) ? group.First(k => next[k].Count <= 1) : group[0];
                    var order = new List<int> { start };
                    for (int prev = -1, at = start; ;)
                    {
                        int to = next[at].Where(m => m != prev && !order.Contains(m)).DefaultIfEmpty(-1).First();
                        if (to < 0)
                            break;
                        order.Add(to);
                        prev = at;
                        at = to;
                    }
                    foreach (var k in group.Where(k => !order.Contains(k)))
                        order.Add(k);
                    foreach (var k in order)
                        done.Add(k);
                    pieces.Add(new Piece
                    {
                        kind = c.kind, settings = c.s, owner = c.anchor, capsules = c.capsules, mesh = order.Count > 1,
                        loop = order.Count > 2 && group.All(k => next[k].Count >= 2) && next[order[order.Count - 1]].Contains(order[0]),
                        roots = order.Select(k => c.bones[k]).ToList(),
                    });
                }
            }
            return pieces;
        }

        /// <summary>Before the animation writes the pose: bones no clip animates go back to their rest rotation.</summary>
        public void Rest()
        {
            foreach (var c in chains)
                for (int i = 0; i < c.bones.Length; i++)
                    c.bones[i].localRotation = c.restLocal[i];
        }

        /// <summary>
        /// For checks, after Step: every chain tip (a bone with no child in its set) of the given kind, how
        /// far its simulated tail is turned away from the animated one, seen from where its chain hangs
        /// (degrees), and the simulated direction.
        /// </summary>
        public IEnumerable<(string set, string tip, float angle, Vector3 dir, Vector3 animatedDir)> TipDeviations(string kind)
        {
            foreach (var c in chains.Where(c => c.kind == kind && c.ready))
            {
                var isParent = new HashSet<int>(c.parent.Where(p => p >= 0));
                for (int i = 0; i < c.bones.Length; i++)
                {
                    if (isParent.Contains(i))
                        continue;
                    int r = c.root[i];
                    var animated = c.tb[i] - c.hb[r];
                    var simulated = c.x[i] - c.hb[r];
                    yield return (c.name, c.bones[i].name, Vector3.Angle(animated, simulated), simulated.normalized, animated.normalized);
                }
            }
        }

        /// <summary>
        /// For checks, after Step: how deep the cloth of a kind is in the legs (thigh and calf capsules), beyond
        /// how deep the same segment lies in them in the battle stance - bone segments and cross links (m) -
        /// how far the cross links are stretched or squeezed against the animated pose (largest, as a share),
        /// and how deep the animated pose itself goes (the base the cloth starts from).
        /// </summary>
        public (float boneDepth, float linkDepth, float strain, int links, float baseDepth) Measures(string kind)
        {
            float bone = 0f, link = 0f, strain = 0f, animated = 0f;
            int count = 0;
            foreach (var c in chains.Where(c => c.kind == kind && c.ready))
            {
                for (int i = 0; i < c.bones.Length; i++)
                {
                    bone = Mathf.Max(bone, LegDepth(c.head[i], c.x[i], c.s.radius, c.capsules) - c.restPenetration[i]);
                    animated = Mathf.Max(animated, LegDepth(c.hb[i], c.tb[i], c.s.radius, c.capsules) - c.restPenetration[i]);
                }
                foreach (var level in c.links)
                    foreach (var (i, j) in level)
                    {
                        link = Mathf.Max(link, LegDepth(c.x[i], c.x[j], c.s.radius, c.capsules) - c.linkRestPenetration[(i, j)]);
                        float rest = Vector3.Distance(c.tb[i], c.tb[j]);
                        if (rest > 1e-4f)
                            strain = Mathf.Max(strain, Mathf.Abs(Vector3.Distance(c.x[i], c.x[j]) / rest - 1f));
                        count++;
                    }
            }
            return (bone, link, strain, count, animated);
        }

        static float LegDepth(Vector3 a, Vector3 b, float r, Capsule[] caps)
        {
            float worst = 0f;
            foreach (var cap in caps)
                if (cap.leg)
                    worst = Mathf.Max(worst, EdgeDepth(a, b, r, cap, out _, out _));
            return worst;
        }

        /// <summary>Start again from the animated pose (a teleport, a new round).</summary>
        public void Reset()
        {
            foreach (var c in chains)
                c.ready = false;
        }

        /// <summary>One step of the fight (dt), on the pose the animation and the rig made; floor = world height.</summary>
        public void Step(float dt, float floor)
        {
            worldPos = world.position;
            worldRot = world.rotation;
            bool first = false;
            foreach (var c in chains)
            {
                ReadPose(c);
                if (!c.ready)
                {
                    Array.Copy(c.tb, c.x, c.x.Length);
                    Array.Copy(c.tb, c.xOld, c.x.Length);
                    Array.Copy(c.hb, c.hbPrev, c.x.Length);
                    Array.Copy(c.tb, c.tbPrev, c.x.Length);
                    Array.Copy(c.rb, c.rot, c.x.Length);
                    Array.Copy(c.hb, c.head, c.x.Length);
                    c.anchorPosPrev = c.anchorPos;
                    c.anchorRotPrev = c.anchorRot;
                    c.stabilizeLeft = c.s.stabilize;
                    c.ready = true;
                    first = true;
                }
            }
            if (first)
            {
                worldPosPrev = worldPos;
                worldRotPrev = worldRot;
            }
            foreach (var c in chains)
            {
                if (dt > 0f)
                {
                    int sub = Mathf.Max(1, Mathf.RoundToInt(dt * 120f));
                    for (int k = 0; k < sub; k++)
                    {
                        if (doa5)
                            SimulateDoa5(c, dt / sub, (float)k / sub, (k + 1f) / sub, floor);
                        else
                            Simulate(c, dt / sub, (float)k / sub, (k + 1f) / sub, floor);
                    }
                    Array.Copy(c.hb, c.hbPrev, c.x.Length);
                    Array.Copy(c.tb, c.tbPrev, c.x.Length);
                    c.anchorPosPrev = c.anchorPos;
                    c.anchorRotPrev = c.anchorRot;
                    c.stabilizeLeft -= dt;
                }
                Write(c);
            }
            if (dt > 0f)
            {
                worldPosPrev = worldPos;
                worldRotPrev = worldRot;
            }
        }

        static void ReadPose(ChainSet c)
        {
            for (int i = 0; i < c.bones.Length; i++)
            {
                var b = c.bones[i];
                c.hb[i] = b.position;
                c.tb[i] = b.TransformPoint(c.tailLocal[i]);
                c.rb[i] = b.rotation;
            }
            c.anchorPos = c.anchor.position;
            c.anchorRot = c.anchor.rotation;
        }

        /// <summary>
        /// Carry the particles with a rigid motion (from -> to about a pivot), all but the share the cloth
        /// feels: felt = inertia, less above the speed limits.  share: per particle, how much of felt applies.
        /// </summary>
        static void Carry(ChainSet c, Vector3 pivot, Vector3 move, Quaternion turn, float dt, float inertia, float moveLimit, float turnLimit, float[] share)
        {
            float speed = move.magnitude / dt;
            float feltT = speed > 1e-6f ? inertia * Mathf.Min(1f, moveLimit / speed) : inertia;
            turn.ToAngleAxis(out float angle, out _);
            if (angle > 180f)
                angle = 360f - angle;
            float feltR = angle > 1e-4f ? inertia * Mathf.Min(1f, turnLimit * dt / angle) : inertia;
            for (int i = 0; i < c.x.Length; i++)
            {
                float k = share != null ? share[i] : 1f;
                var carry = Quaternion.Slerp(Quaternion.identity, turn, 1f - feltR * k);
                var shift = move * (1f - feltT * k);
                c.x[i] = carry * (c.x[i] - pivot) + pivot + shift;
                c.xOld[i] = carry * (c.xOld[i] - pivot) + pivot + shift;
            }
        }

        /// <summary>One substep: the fight's step is split in ~120 Hz substeps; w / wPrev = where in the step it ends / starts.</summary>
        void Simulate(ChainSet c, float dt, float wPrev, float w, float floor)
        {
            var s = c.s;
            int n = c.bones.Length;
            float step90 = dt * 90f;
            float damp = 1f - Mathf.Pow(1f - s.damping, step90);
            float restore = 1f - Mathf.Pow(1f - s.restore, step90);
            var gravity = Vector3.down * s.gravity;

            // the animated pose and the anchor in between the fight's steps
            var hb = new Vector3[n];
            var tb = new Vector3[n];
            var d0 = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                hb[i] = Vector3.Lerp(c.hbPrev[i], c.hb[i], w);
                tb[i] = Vector3.Lerp(c.tbPrev[i], c.tb[i], w);
                d0[i] = (tb[i] - hb[i]).normalized;
            }
            var cPos = Vector3.Lerp(c.anchorPosPrev, c.anchorPos, w);
            var cRot = Quaternion.Slerp(c.anchorRotPrev, c.anchorRot, w);
            var prevPos = Vector3.Lerp(c.anchorPosPrev, c.anchorPos, wPrev);
            var prevRot = Quaternion.Slerp(c.anchorRotPrev, c.anchorRot, wPrev);

            // 1. inertia
            if (legacy)
                Carry(c, prevPos, cPos - prevPos, cRot * Quaternion.Inverse(prevRot), dt, s.inertia, s.moveLimit, s.turnLimit, null);
            else
            {
                // world: the fighter's root over the floor
                var wPos = Vector3.Lerp(worldPosPrev, worldPos, w);
                var wRot = Quaternion.Slerp(worldRotPrev, worldRot, w);
                var wPos0 = Vector3.Lerp(worldPosPrev, worldPos, wPrev);
                var wRot0 = Quaternion.Slerp(worldRotPrev, worldRot, wPrev);
                Carry(c, wPos0, wPos - wPos0, wRot * Quaternion.Inverse(wRot0), dt, s.worldInertia, s.worldMoveLimit, s.worldTurnLimit, null);
                // local: the anchor in the fighter's frame (the animation), felt less near the roots
                var local0 = Quaternion.Inverse(wRot0) * (prevPos - wPos0);
                var local1 = Quaternion.Inverse(wRot) * (cPos - wPos);
                var lr0 = Quaternion.Inverse(wRot0) * prevRot;
                var lr1 = Quaternion.Inverse(wRot) * cRot;
                var pivot = wPos + wRot * local0;
                var share = new float[n];
                for (int i = 0; i < n; i++)
                    share[i] = 1f - s.depthInertia * (1f - c.depth[i]);
                Carry(c, pivot, wRot * (local1 - local0), wRot * (lr1 * Quaternion.Inverse(lr0)) * Quaternion.Inverse(wRot), dt, s.inertia, s.moveLimit, s.turnLimit, share);
            }

            // 2. Verlet: damping, speed limit, gravity
            for (int i = 0; i < n; i++)
            {
                var v = (c.x[i] - c.xOld[i]) / dt * (1f - damp);
                float sp = v.magnitude;
                if (sp > s.particleLimit)
                    v *= s.particleLimit / sp;
                v += gravity * dt;
                c.xOld[i] = c.x[i];
                c.x[i] += v * dt;
            }

            // 3. constraints, root to tip
            var axis = cRot * c.anchorAxis;
            for (int it = 0; it < s.iterations; it++)
            {
                for (int l = 0; l < c.levels.Length; l++)
                {
                    var lv = c.levels[l];
                    // angle restoration (once a step; most of the move does not become speed) and the angle limit
                    foreach (int i in lv)
                    {
                        int p = c.parent[i];
                        // the parent's turn away from its animated pose carries this bone's baseline
                        c.d[i] = p < 0 ? Quaternion.identity : c.rot[p] * Quaternion.Inverse(c.rb[p]);
                        c.head[i] = p < 0 ? hb[i] : c.x[p];
                        c.g[i] = (c.d[i] * d0[i]).normalized;
                        var dir = (c.x[i] - c.head[i]).normalized;
                        if (it == 0)
                        {
                            var d2 = Turn(dir, c.g[i], restore);
                            var nx = c.head[i] + d2 * c.length[i];
                            c.xOld[i] += (nx - c.x[i]) * s.attenuation;
                            c.x[i] = nx;
                            dir = d2;
                        }
                        dir = Vector3.RotateTowards(c.g[i], dir, c.limit[i], 0f).normalized;
                        c.x[i] = c.head[i] + dir * c.length[i];
                    }
                    // the cross links: linked chains keep their distance as in the animated pose
                    foreach (var (i, j) in c.links[l])
                    {
                        var dv = c.x[j] - c.x[i];
                        float dist = dv.magnitude, rest = Vector3.Distance(tb[i], tb[j]);
                        if (dist < 1e-6f)
                            continue;
                        var corr = dv / dist * ((dist - rest) * 0.5f * s.linkStiffness);
                        c.x[i] = OnSphere(c, i, c.x[i] + corr);
                        c.x[j] = OnSphere(c, j, c.x[j] - corr);
                    }
                    // collisions, backstop, max distance, tether, floor
                    foreach (int i in lv)
                    {
                        var h = c.head[i];
                        float L = c.length[i];
                        PushColliders(c, i, hb, tb);
                        // backstop: a big sphere behind the animated tail, on the side of the body's axis
                        if (s.backstop && !NoBackstop)
                        {
                            var rel = tb[i] - cPos;
                            var nrm = (rel - Vector3.Dot(rel, axis) * axis).normalized;
                            var centre = tb[i] - nrm * (s.backstopRadius + s.backstopDistance);
                            var dv = c.x[i] - centre;
                            float dl = dv.magnitude;
                            if (dl < s.backstopRadius && dl > 1e-6f)
                                c.x[i] = h + (centre + dv / dl * s.backstopRadius - h).normalized * L;
                        }
                        // max distance from the animated place (Magica's motion constraint)
                        if (c.maxDistance[i] > 0f)
                        {
                            var off = c.x[i] - tb[i];
                            if (off.magnitude > c.maxDistance[i])
                                c.x[i] = OnSphere(c, i, tb[i] + off.normalized * c.maxDistance[i]);
                        }
                        // tether: the chain does not fold up towards its root
                        if (s.tether > 0f)
                        {
                            var r0 = hb[c.root[i]];
                            float near = (1f - s.tether) * Vector3.Distance(tb[i], r0);
                            var out0 = c.x[i] - r0;
                            if (out0.magnitude < near && out0.magnitude > 1e-6f)
                                c.x[i] = OnSphere(c, i, r0 + out0.normalized * near);
                        }
                        if (s.floor)
                            Floor(c, i, floor, cPos, axis);
                    }
                    // the cross links collide too: a leg cannot pass between two linked chains (not for a skirt
                    // hung on the skin: its pose already clears the legs, and link pushes against the capsules,
                    // which are fatter than the legs, fought the links' lengths - a08 shook 13 degrees a step)
                    if (c.capsules.Length > 0 && !NoColliders && !(skirtOnSkin && c.kind == "skirt"))
                        foreach (var (i, j) in c.links[l])
                            PushLink(c, i, j);
                    // each bone's frame: the animated one carried by the parent, swung onto the simulated direction
                    foreach (int i in lv)
                        c.rot[i] = Quaternion.FromToRotation(c.g[i], (c.x[i] - c.head[i]).normalized) * c.d[i] * c.rb[i];
                }
            }
            // just after a reset nothing that was pushed into place becomes speed
            if (c.stabilizeLeft > 0f)
                Array.Copy(c.x, c.xOld, n);
        }

        /// <summary>
        /// The particle out of the colliders, its bone kept at its length (hb, tb: the animated heads and tails of this
        /// substep).  As deep as the stance has it is allowed (legacy: as deep as the animated pose goes; a skirt hung on
        /// the skin: as deep as either).
        /// </summary>
        void PushColliders(ChainSet c, int i, Vector3[] hb, Vector3[] tb)
        {
            if (c.capsules.Length == 0 || NoColliders)
                return;
            var s = c.s;
            var h = c.head[i];
            float L = c.length[i];
            var allow = legacy ? c.allow : s.edge ? c.restEdge[i] : c.restPoint[i];
            bool onSkin = !legacy && skirtOnSkin && c.kind == "skirt";
            if (legacy || onSkin)
            {
                var stance = allow;
                allow = c.allow;
                for (int k = 0; k < c.capsules.Length; k++)
                    allow[k] = Mathf.Max(onSkin ? stance[k] : 0f, s.edge ? EdgeDepth(hb[i], tb[i], s.radius, c.capsules[k], out _, out _)
                                                                         : PointDepth(tb[i], s.radius, c.capsules[k], out _));
            }
            bool hit;
            Vector3 nx = s.edge ? PushEdge(h, c.x[i], s.radius, c.capsules, allow, out hit)
                                : PushPoint(c.x[i], s.radius, c.capsules, allow, out hit);
            nx = h + (nx - h).normalized * L;
            if (hit)
            {
                // the leg carries the cloth along, it does not bat it away: most of the push does not become speed
                // (all of it did: g04's front panels popped 24-39 degrees in a step when a thigh came through), then friction
                c.xOld[i] += (nx - c.x[i]) * s.pushAttenuation;
                c.xOld[i] += (nx - c.xOld[i]) * s.friction;
            }
            c.x[i] = nx;
        }

        /// <summary>The floor: the bone turns up just enough; into the floor the speed is stopped.</summary>
        static void Floor(ChainSet c, int i, float floor, Vector3 cPos, Vector3 axis)
        {
            var s = c.s;
            var h = c.head[i];
            float L = c.length[i];
            float lo = floor + s.radius;
            var d = (c.x[i] - h).normalized;
            float zmin = Mathf.Clamp((lo - h.y) / Mathf.Max(L, 1e-6f), -1f, 1f);
            if (d.y >= zmin)
                return;
            var hor = new Vector3(d.x, 0f, d.z);
            if (hor.sqrMagnitude < 1e-8f)
            {
                var rel = c.x[i] - cPos;
                hor = rel - Vector3.Dot(rel, axis) * axis;
                hor.y = 0f;
                if (hor.sqrMagnitude < 1e-8f)
                    hor = Vector3.forward;
            }
            hor.Normalize();
            var nd = hor * Mathf.Sqrt(Mathf.Max(0f, 1f - zmin * zmin)) + Vector3.up * zmin;
            var nx = h + nd * L;
            var vel = c.x[i] - c.xOld[i];
            vel.y = Mathf.Max(vel.y, 0f);
            vel.x *= 1f - s.friction;
            vel.z *= 1f - s.friction;
            c.x[i] = nx;
            c.xOld[i] = nx - vel;
        }

        /// <summary>A point put back on the particle's bone sphere (its head, its length).</summary>
        static Vector3 OnSphere(ChainSet c, int i, Vector3 p)
        {
            var dv = p - c.head[i];
            return dv.sqrMagnitude > 1e-12f ? c.head[i] + dv.normalized * c.length[i] : c.x[i];
        }

        static Vector3 Turn(Vector3 a, Vector3 b, float fraction)
        {
            float angle = Vector3.Angle(a, b) * Mathf.Deg2Rad;
            return angle < 1e-6f ? b : Vector3.RotateTowards(a, b, angle * fraction, 0f).normalized;
        }

        // ---- colliders

        static float PointDepth(Vector3 p, float r, Capsule cap, out Vector3 normal)
        {
            Vector3 a = cap.a.position, ab = cap.b.position - a;
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-8f));
            var d = p - (a + t * ab);
            float dist = d.magnitude;
            normal = dist > 1e-9f ? d / dist : Vector3.up;
            return Mathf.Lerp(cap.ra, cap.rb, t) + r - dist;
        }

        static Vector3 PushPoint(Vector3 p, float r, Capsule[] caps, float[] allow, out bool hit)
        {
            hit = false;
            for (int pass = 0; pass < 2; pass++)
            {
                bool any = false;
                for (int k = 0; k < caps.Length; k++)
                {
                    float pen = PointDepth(p, r, caps[k], out var n) - allow[k];
                    if (pen > 0f)
                    {
                        p += n * pen;
                        any = true;
                    }
                }
                hit |= any;
                if (!any)
                    break;
            }
            return p;
        }

        /// <summary>Closest points of segments p0-p1 and a-b: parameter s on the first, t on the second.</summary>
        static void Closest(Vector3 p0, Vector3 p1, Vector3 a, Vector3 b, out float s, out float t)
        {
            Vector3 d1 = p1 - p0, d2 = b - a, r = p0 - a;
            float A = Vector3.Dot(d1, d1), E = Vector3.Dot(d2, d2), F = Vector3.Dot(d2, r), C = Vector3.Dot(d1, r), Bd = Vector3.Dot(d1, d2);
            float den = A * E - Bd * Bd;
            s = den > 1e-12f ? Mathf.Clamp01((Bd * F - C * E) / den) : 0f;
            float tt = (Bd * s + F) / Mathf.Max(E, 1e-12f);
            t = Mathf.Clamp01(tt);
            if (!Mathf.Approximately(t, tt))
                s = Mathf.Clamp01((Bd * t - C) / Mathf.Max(A, 1e-12f));
        }

        static float EdgeDepth(Vector3 head, Vector3 tail, float r, Capsule cap, out Vector3 normal, out float s)
        {
            Vector3 a = cap.a.position, b = cap.b.position;
            Closest(head, tail, a, b, out s, out float t);
            var d = head + s * (tail - head) - (a + t * (b - a));
            float dist = d.magnitude;
            normal = dist > 1e-9f ? d / dist : Vector3.up;
            return Mathf.Lerp(cap.ra, cap.rb, t) + r - dist;
        }

        /// <summary>
        /// A bone segment against the capsules: its tail moves so the closest point clears them.  The
        /// tail moves pen / s for a contact at s along the segment (the lever); near the head that was up
        /// to 5x the depth in one go and the panel jumped, so the lever stops at 3x and one push at
        /// MaxPush - the rest follows in the next iterations and substeps.
        /// </summary>
        static Vector3 PushEdge(Vector3 head, Vector3 tail, float r, Capsule[] caps, float[] allow, out bool hit)
        {
            hit = false;
            var move = Vector3.zero;
            for (int k = 0; k < caps.Length; k++)
            {
                float pen = EdgeDepth(head, tail, r, caps[k], out var n, out float s) - allow[k];
                if (pen > 0f && s > 0.05f)
                {
                    move += n * (pen / Mathf.Max(s, 0.35f));
                    hit = true;
                }
            }
            return tail + Vector3.ClampMagnitude(move, MaxPush);
        }

        /// <summary>
        /// A cross link against the capsules: both ends move so the closest point clears them, each by its
        /// share (the contact's place along the link), as deep as the link lies in the stance being allowed.
        /// </summary>
        void PushLink(ChainSet c, int i, int j)
        {
            var s = c.s;
            var mi = Vector3.zero;
            var mj = Vector3.zero;
            bool hit = false;
            var allow = c.restLink[(i, j)];
            bool onSkin = skirtOnSkin && c.kind == "skirt";
            for (int k = 0; k < c.capsules.Length; k++)
            {
                float allowed = onSkin ? Mathf.Max(allow[k], EdgeDepth(c.tb[i], c.tb[j], s.radius, c.capsules[k], out _, out _)) : allow[k];
                float pen = EdgeDepth(c.x[i], c.x[j], s.radius, c.capsules[k], out var nrm, out float u) - allowed;
                if (pen <= 0f)
                    continue;
                float wi = 1f - u, wj = u, norm = wi * wi + wj * wj;
                mi += nrm * (pen * wi / norm);
                mj += nrm * (pen * wj / norm);
                hit = true;
            }
            if (!hit)
                return;
            var ni = OnSphere(c, i, c.x[i] + Vector3.ClampMagnitude(mi, MaxPush));
            var nj = OnSphere(c, j, c.x[j] + Vector3.ClampMagnitude(mj, MaxPush));
            c.xOld[i] += (ni - c.x[i]) * s.pushAttenuation;
            c.xOld[j] += (nj - c.x[j]) * s.pushAttenuation;
            c.x[i] = ni;
            c.x[j] = nj;
        }

        const float MaxPush = 0.025f;

        /// <summary>Write the simulated rotations, blended with the animated ones by ``weight`` (``alwaysWeight`` for AlwaysKinds but sleeves).</summary>
        void Write(ChainSet c)
        {
            float w = AlwaysKinds.Contains(c.kind) && !c.sleeve ? alwaysWeight : weight;
            if (w <= 0f)
                return;
            foreach (var lv in c.levels)
                foreach (int i in lv)
                    c.bones[i].rotation = w >= 1f ? c.rot[i] : Quaternion.Slerp(c.bones[i].rotation, c.rot[i], w);
        }
    }
}
