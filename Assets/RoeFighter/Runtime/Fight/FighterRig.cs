using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace RoeFighter.Fight
{
    /// <summary>
    /// One fighter's body: the high-detail humanoid model and the game's own unit rig for skills.
    ///
    ///   model    our humanoid character (Generated/&lt;id&gt;/&lt;id&gt;_fighter.prefab).  A playable graph
    ///            drives it in three layers:
    ///              0  the game's battle stance, always: it carries the hair, fan, skirt and weapon curves;
    ///              1  the basic moves from motion capture, crossfaded among themselves - humanoid
    ///                 curves only, so the other bones keep the stance's values (a closed fan);
    ///              2  the game's own clips converted to humanoid (hurt, fall, skills, poses), which
    ///                 move every bone; this layer fades in over the others while one of them plays.
    ///            (One mixer for both kinds would blend the bones the mocap does not move towards
    ///            the bind pose: the fan opened wide during every punch.)
    ///   unit     the game's unit prefab (gameplay_prefab_*), cleaned up in the editor.  Its
    ///            timelines animate a hidden low-detail copy of the character that carries the
    ///            effect anchors; during a skill the timeline runs in step with the model, so
    ///            the game's effects appear where the game puts them.
    /// Nothing advances by itself: the fight calls Tick(dt) once per simulation step.
    /// </summary>
    public class FighterRig : MonoBehaviour
    {
        [Serializable]
        public class NamedClip
        {
            public string name;
            public AnimationClip clip;
            public bool loop;
            public bool game;       // the game's clip: moves every bone (layer 2); else motion capture (layer 1)
            public bool hovers;     // a game clip that keeps the game's hover (gameHover): the skills, their effects placed for it
        }

        [Serializable]
        public class NamedDirector
        {
            public string action;
            public PlayableDirector director;
        }

        [Serializable]
        public class NamedSound
        {
            public string name;
            public AudioClip clip;
        }

        public string id;
        public string displayName;
        public string outfit;                        // a word on the suit for the select screen ("goddess")
        public Texture2D portrait;                   // the select screen's card (rendered when the scene is built)
        public Animator animator;
        public AnimationClip stance;                 // layer 0: the game's battle stance (or a standing clip, if that floats)
        public List<NamedClip> clips = new List<NamedClip>();
        public List<NamedDirector> directors = new List<NamedDirector>();
        public List<NamedSound> sounds = new List<NamedSound>();
        public TextAsset skillSheetJson;
        /// <summary>Her game's own Magica Cloth 2 set-up (ROE's lobby prefab: Assets/ROE/&lt;id&gt;/magica_lobby.json, tools/roe_mc2_dump.py) for the "roe" cloth setup; null: none.</summary>
        public TextAsset gameMagica;
        public Renderer[] weaponRenderers;           // shown only during skills (a08's sword) - always while she holds them (Holds)

        /// <summary>
        /// Under motion capture a thigh whose foot is on the floor turns about its own axis against the pelvis (in or out) as
        /// the clip has it up to rollFrom degrees; past that it keeps rollKeep of the rest (1: all of it), the leg below
        /// turning with it (rollFoot: where the foot goes then).  Kart's strikes (motion capture on her) turn her planted,
        /// bent thigh in 45-60 degrees (ax_lunge, ax_chop, ax_slam; her own game clips at most 36): the shin swings out, the
        /// leg stands like a W and the buttock looks as if it hung down onto the thigh - every weighting tried only moved
        /// that (user 10-06: "大腿根的权重还是不对呀"; README).  A lifted leg (a kick) keeps its turn.  Set per fighter when
        /// the scene is built (RoeFightScene -roeLegRoll).
        /// </summary>
        /// <summary>
        /// What ROE's menus play when a heroine is touched (user 10-06: "选人环节也做个动画，ROE里面标准的动画"): every outfit's
        /// hd bundle - the model the menus show - carries idle_02 (looping) and two timelines, idle02_react01 and
        /// idle02_react02, each a react clip with one voice line (meta_armor_pc_&lt;id&gt; maps the line to a localized clip).
        /// Read by tools/roe_showcase.py into tools/roe/&lt;id&gt;_showcase.json, set when the scene is built; the select screen
        /// plays them (FightGame.ShowPicked).
        /// </summary>
        [Serializable]
        public class ShowReaction
        {
            public string timeline;     // the game's timeline (idle02_react01)
            public string clip;         // its react clip (react_01)
            public string sound;        // its voice line among the sounds ("": none)
            public float voiceAt;       // seconds into the clip
        }

        public List<ShowReaction> showcase = new List<ShowReaction>();

        public float rollFrom = 15f;
        public float rollKeep = 1f;
        public int rollFoot = 0;    // the foot after the turn: 0 where the turned leg puts it, 1 at the clip's height, 2 where the clip put it
                                    // (2 turns the knee back with it: the leg stands as the clip had it, the thigh's roll is gone
                                    // but the knee twists - and the W-shaped leg is back)

        /// <summary>She holds her weapons in her hands through the basic moves (RoeGrips on her model: a08's greatsword) when
        /// her own strikes are made for it (MotionPack.grip: sword strikes); her weapons then show all the time.</summary>
        public bool Holds => Grips != null && Grips.grips.Length > 0 && strikePack != null && strikePack.grip;
        RoeGrips Grips => grips != null ? grips : grips = animator != null ? animator.GetComponentInChildren<RoeGrips>(true) : null;
        [NonSerialized] RoeGrips grips;
        public AudioSource audioSource;
        public Transform unitRoot;
        public RoeClothesBurst burst;                // the outfit's pieces that come off (爆衣), on the model; null: none

        [NonSerialized] public RoeSkillSheet sheet;
        PlayableGraph graph;
        AnimationLayerMixerPlayable layers;
        AnimationMixerPlayable mocapMixer, gameMixer;
        AnimationClipPlayable stancePlayable;
        readonly Dictionary<string, int> index = new Dictionary<string, int>();
        AnimationClipPlayable[] playables;
        int[] slot;                         // input of the clip in its mixer
        float[] weight, target, rate;
        float gameWeight, gameTarget;
        int current = -1;
        float fade = 0.12f;
        float stanceTime;
        PlayableDirector activeDirector;
        bool weaponsShown = true;
        RoeHelperRig helpers;
        Transform hips, head;
        Transform[] toes, feet;
        bool grounded;
        float toeSole, ankleSole, groundOffset;
        readonly Quaternion[] footRest = new Quaternion[2], toeRest = new Quaternion[2];   // the stance: foot against its heading, toe bone
        readonly Vector3[] footSide = new Vector3[2];                                      // the foot's axis that points to its right along the floor (it pitches about it)
        Transform[] thighs, hipCloth;                                                      // hipCloth: skirt panels hung on the hips
        Quaternion[] hipClothRest;                                                         // their stance rotation against the hips' heading
        Quaternion pelvisRef = Quaternion.identity;                                        // the guard's pelvis against the hips' heading
        readonly Vector3[] lastAnkle = new Vector3[2];
        readonly float[] plantWeight = new float[2];                                       // a still foot near the floor is put down on it
        readonly float[] standWeight = new float[2];                                       // how far each foot was posed as standing, this frame
        bool plantValid;

        public bool Ready => graph.IsValid();

        /// <summary>
        /// The basic moves from a motion pack: every clip that is not the game's own is replaced by the
        /// pack's (named by role).  Takes effect at the next Init().
        /// </summary>
        public void UseMotions(MotionPack pack)
        {
            if (pack == null)
                return;
            clips.RemoveAll(c => !c.game);
            int at = 0;
            foreach (var c in pack.clips.Where(c => c.clip != null))
                clips.Insert(at++, new NamedClip { name = c.role, clip = c.clip, loop = c.loop });
            // this fighter's own stance (and walks, if it has them) in place of the pack's: her strikes start from it
            // (user 10-03: Luffee stands in Mai Shiranui's stance, DOA6 00000)
            if (strikePack != null)
                foreach (var c in strikePack.clips.Where(c => c.clip != null && MotionPack.Roles.Contains(c.role)))
                {
                    int i = clips.FindIndex(x => x.name == c.role && !x.game);
                    if (i >= 0)
                        clips[i] = new NamedClip { name = c.role, clip = c.clip, loop = c.loop };
                    else
                        clips.Insert(at++, new NamedClip { name = c.role, clip = c.clip, loop = c.loop });
                }
            // her guard of her own over both (ownGuard: Luf stands in her game's battle stance)
            if (ownGuard != null)
            {
                int i = clips.FindIndex(x => x.name == "guard" && !x.game);
                var own = new NamedClip { name = "guard", clip = ownGuard, loop = true };
                if (i >= 0)
                    clips[i] = own;
                else
                    clips.Insert(at++, own);
            }
            // this fighter's own strikes (strikePack) besides the pack's: their clips under their own names
            if (strikePack != null)
                foreach (var c in strikePack.clips.Where(c => c.clip != null && strikePack.strikes.Any(m => m.clip == c.role)))
                {
                    if (clips.Any(x => x.name == c.role))
                    {
                        Debug.LogWarning($"[ROE] {id}: own strike clip '{c.role}' has the name of a clip of motion pack {pack.name} - left out");
                        continue;
                    }
                    clips.Insert(at++, new NamedClip { name = c.role, clip = c.clip, loop = false });
                }
            motions = pack;
        }

        [NonSerialized] public MotionPack motions;

        /// <summary>This fighter's own strikes instead of the motion pack's (g04: Mai Shiranui's, from DOA6); null: the pack's.</summary>
        public MotionPack strikePack;

        /// <summary>
        /// The guard she stands in between moves, in place of the motion pack's and her strike pack's (user 10-11 "luf还原ROE里的姿势吧":
        /// Luf stands in her game's battle stance idle_01, not in Mai Shiranui's crouch): the stance's humanoid curves only
        /// (Editor RoeHumanoidClips.HumanOnly), so the bones the motion capture leaves alone - fan, hair, skirt - keep layer 0's
        /// and her helper bones follow the limbs as under any motion capture; null: the pack's guard.  Takes effect at the next Init().
        /// </summary>
        public AnimationClip ownGuard;

        /// <summary>
        /// Flat feet - no heels (g05 is barefoot) - as the scene build measured them (Editor RoeFeet: her planted feet in
        /// the game's standing clips keep the ankle 8 cm up, heels keep it 15-16).  The motion capture's feet stay in the
        /// bind pose's angle, and ROE models bare feet pointed 72 degrees like a high heel: she stood and walked on the tips
        /// of her toes.  So under the motion capture her feet are turned up about the ankle by flatPitch (the bind pose's
        /// pitch less her standing one), a planted foot takes her stand from the game's clips (flatRest / flatToe: foot
        /// against the direction it points along the floor, the toe bone) instead of the battle stance's, and the soles are
        /// that stand's toe and ankle heights (flatSoles).  Heels keep the stance's high-heeled angle (the stance has them
        /// on their heels).
        /// </summary>
        public bool flatFeet;
        public Quaternion[] flatRest = new Quaternion[2], flatToe = new Quaternion[2];
        public Vector3[] flatAxis = new Vector3[2];
        public float[] flatPitch = new float[2];
        public Vector2 flatSoles;

        /// <summary>
        /// Feet held at the ankle as the bind pose has them, in every clip (tools/sb/eve09.json "feet": "bind" - Stellar Blade's
        /// Eve).  Her foot points 77 degrees down into a platform shoe, 16 degrees off her shin, and her bind pose stands the
        /// whole sole on the floor.  Every clip she plays is another body's (motion capture, UFE 2): a heel the clip raises
        /// took her toes on past straight down and round to the back - in the guard 129-157 degrees off her facing, the
        /// ankles 15 cm up, the soles on their front edge - and the stance's feet, taken as how she stands, kept her so.  So
        /// the foot and toe bones keep their bind rotations against the shin (the ankle as stiff as her shoe), and a planted
        /// foot stands as the bind pose does: flatFeet, with flatRest / flatToe / flatSoles from the bind pose, flatPitch 0
        /// (RoeFightScene.BindFeet).
        /// </summary>
        public bool bindFeet;
        public Quaternion[] bindFoot = new Quaternion[2], bindToe = new Quaternion[2];

        /// <summary>
        /// How high the game's battle clips hold her above the floor (m), measured when the scene is built: g05 hovers
        /// 23 cm in her battle stance and in every battle clip that starts from it (hurt, die, the skills); 0 = they
        /// stand on the floor.  The fight stands her on the floor - her basic moves walk on it - and a hit lifted her
        /// 23 cm into the air in 0.04 s.  So the game's clips come down by up to this much: never further than putting
        /// the lowest sole on the floor (a pose that lifts her higher keeps the rest of its lift), and not at all once
        /// the soles are below their standing height (lying on the floor at the end of a fall).  Clips that hover
        /// (NamedClip.hovers: the skills, whose effects the game places for the hovering body) rise to the game's
        /// height instead, over HoverRise seconds, and come down again when she leaves them.
        /// </summary>
        public float gameHover;
        public const float HoverRise = 0.3f;
        /// <summary>
        /// ...and never so far that a bone of the body (all human bones but the feet) comes closer to the floor than
        /// this (m): falling back, her legs fly up while her back comes down - brought down by the soles, the hips
        /// went 8 cm into the floor.
        /// </summary>
        public const float BodyClearance = 0.1f;
        float hoverWeight, hoverTarget;     // how much of the game's hover the clips keep (1: all of it)
        Transform[] body;                   // the human bones but the feet and toes

        /// <summary>How far the game's clips were brought down in the last step (m; for checks).</summary>
        public float HoverDrop { get; private set; }

        /// <summary>Size of the weapons (g04's fan): 1 = the game's.  Scales the bone that carries each weapon's own bones.</summary>
        [Range(0.2f, 2f)] public float weaponScale = 1f;
        [NonSerialized] Transform[] weaponRoots;
        [NonSerialized] RoeDoaRig doaRig;
        [NonSerialized] RoeUeRig ueRig;
        [NonSerialized] Vector3[] weaponRootScale;
        public string Current => current >= 0 ? clips[current].name : "";
        public float CurrentTime => current >= 0 ? (float)playables[current].GetTime() : 0f;
        public float CurrentRate => current >= 0 ? rate[current] : 0f;

        public void Init()
        {
            if (graph.IsValid())
                graph.Destroy();
            if (skillSheetJson != null)
                sheet = RoeSkillSheet.FromJson(skillSheetJson.text);
            if (weaponRoots == null)
                FindWeaponRoots();
            // the last match's solver puts the bones it moved back to their rest first: the next one takes its rest from
            // the bones as they are now (10-05: after F4 the bone cloth held Eve's ponytail up where her game's physics had
            // left it, and in a demo every take after the first started from the take before's last frame)
            cloth?.Rest();
            // The Animator takes its default values from the bones as they are now: put the model in
            // its battle stance first, so bones no clip animates rest there (closed fan, not the bind pose).
            if (stance != null)
                stance.SampleAnimation(animator.gameObject, 0f);
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            // the soles: how high the toe and ankle bones are when the game's stance stands on the floor
            hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            head = animator.GetBoneTransform(HumanBodyBones.Head);
            toes = new[] { animator.GetBoneTransform(HumanBodyBones.LeftToes), animator.GetBoneTransform(HumanBodyBones.RightToes) };
            feet = new[] { animator.GetBoneTransform(HumanBodyBones.LeftFoot), animator.GetBoneTransform(HumanBodyBones.RightFoot) };
            grounded = stance != null && toes.All(t => t != null) && feet.All(t => t != null);
            body = Enum.GetValues(typeof(HumanBodyBones)).Cast<HumanBodyBones>()
                .Where(b => b != HumanBodyBones.LastBone).Select(animator.GetBoneTransform)
                .Where(t => t != null && !toes.Contains(t) && !feet.Contains(t)).ToArray();
            if (grounded)
            {
                float floor = animator.transform.position.y;
                toeSole = toes.Average(t => t.position.y) - floor;
                ankleSole = feet.Average(t => t.position.y) - floor;
                // and how each foot stands in its shoe: turned against the direction it points along the floor
                for (int i = 0; i < 2; i++)
                {
                    var ahead = Flat(toes[i].position - feet[i].position);
                    ahead = ahead.sqrMagnitude > 1e-6f ? ahead.normalized : Flat(animator.transform.forward).normalized;
                    footRest[i] = Quaternion.Inverse(Quaternion.LookRotation(ahead, Vector3.up)) * feet[i].rotation;
                    toeRest[i] = toes[i].localRotation;
                }
                // flat feet stand the way her flattest stand in the game's clips has them (RoeFeet), not as the stance
                if (flatFeet && flatSoles.y > 0f && flatRest.Length == 2 && flatToe.Length == 2)
                {
                    toeSole = flatSoles.x;
                    ankleSole = flatSoles.y;
                    for (int i = 0; i < 2; i++)
                    {
                        footRest[i] = flatRest[i];
                        toeRest[i] = flatToe[i];
                    }
                }
                // the foot's own axis across it, to its right while it stands: the heading it points along the floor is
                // square to it, however far the foot is pitched (see Stand)
                for (int i = 0; i < 2; i++)
                    footSide[i] = Quaternion.Inverse(footRest[i]) * Vector3.right;
            }
            helpers = animator.GetComponent<RoeHelperRig>();
            doaRig = animator.GetComponent<RoeDoaRig>();
            ueRig = animator.GetComponent<RoeUeRig>();
            // a DOA6 character's breast meshes are skinned to their soft-body nodes, up to 12 bones a vertex, whatever moves them
            if (doaRig != null && doaRig.softs.Count > 0)
                QualitySettings.skinWeights = SkinWeights.Unlimited;
            // the skirt panels hung on the hips (bones with a chain under them; single bones such as g04's
            // waist pieces and a08's sword points stay rigid on the pelvis, as the game keeps them): how they
            // hang in the stance, against the hips' heading
            thighs = new[] { animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg), animator.GetBoneTransform(HumanBodyBones.RightUpperLeg) };
            hipCloth = null;
            if (stance != null && hips != null && thighs.All(t => t != null))
            {
                var human = new HashSet<Transform>();
                foreach (HumanBodyBones b in Enum.GetValues(typeof(HumanBodyBones)))
                    if (b != HumanBodyBones.LastBone && animator.GetBoneTransform(b) != null)
                        human.Add(animator.GetBoneTransform(b));
                var driven = new HashSet<Transform>(helpers != null ? helpers.drives.Select(d => d.helper) : Enumerable.Empty<Transform>());
                // (a DOA6 grid cloth's points are not panels: its top rows follow their skinning, RoeDoaRig.DriveGrids)
                if (doaRig != null)
                    driven.UnionWith(doaRig.grids.SelectMany(g => g.cps));
                hipCloth = hips.Cast<Transform>().Where(t => !human.Contains(t) && !driven.Contains(t) && t.childCount > 0).ToArray();
                var heading = HipHeading();
                hipClothRest = hipCloth.Select(t => Quaternion.Inverse(heading) * t.rotation).ToArray();
                pelvisRef = Quaternion.Inverse(heading) * hips.rotation;     // replaced by the guard's below, once the clips are in
                Debug.Log($"[ROE] {id} hung on the hips: {string.Join(", ", hipCloth.Select(t => t.name))}");
            }
            // skirts, hair, chains and breasts: found and measured in the stance pose, by the chosen backend
            // (a scene stores an unset clothBackend as "", not null: F4 changed only the notice until 10-04)
            backend = RoeClothBackends.Find(RoeClothBackends.Resolve(useCloth ? (string.IsNullOrEmpty(clothBackend) ? ClothBackend : clothBackend) : "off", animator, id));
            var sheets = doaRig != null ? doaRig.Sheets() : default;
            // a solver that put things in the scene (Magica's components) takes them away again
            (cloth as IDisposable)?.Dispose();
            cloth = backend.Make(new RoeClothScope
            {
                id = id, animator = animator, world = transform,
                exclude = helpers != null ? helpers.drives.Select(d => d.helper).ToList() : null,
                // a DOA6 character: her loose bones by kind from her physics data (their names are only numbers), her grid
                // cloth's columns as one sheet each
                kindOf = RoeDoaPhysics.KindsOf(animator),
                anchorOf = sheets.anchorOf, neighbours = sheets.neighbours,
                gameMagica = gameMagica != null ? gameMagica.text : null,
            });
            if (cloth != null)
                Debug.Log($"[ROE] {id} cloth {backend.name}: {cloth.Report}");
            // the skirt's animation pose under motion capture: its rest is the stance's skirt against the hips'
            // heading, and it hangs on the skin (measured here, in the stance)
            skirtRig = backend.skinnedSkirt ? animator.GetComponent<RoeSkirtRig>() : null;
            if (skirtRig != null)
            {
                skirtRig.drapeAllowed = !backend.noDrape;      // a solver that hangs the skirt itself: our drape off
                skirtRig.drapeScale = backend.DrapeFor(id);     // the "swing" skirt hangs less (RoeBoneCloth.SwingTunings)
            }
            if (skirtRig != null && hips != null && thighs.All(t => t != null))
            {
                skirtRig.CaptureStance(hips.position, HipHeading());
                Debug.Log($"[ROE] {id} skirt rig: {skirtRig.joints.Count} bones, hung on {(skirtRig.Body != null ? skirtRig.Body.Count : 0)} skin points");
            }
            else
                skirtRig = null;
            animator.Rebind();
            legs = null;
            var legBones = new[] { HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot,
                                   HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot }.Select(animator.GetBoneTransform).ToArray();
            if (legBones.All(b => b != null))
                legs = new[]
                {
                    new Leg { upper = legBones[0], lower = legBones[1], foot = legBones[2] },
                    new Leg { upper = legBones[3], lower = legBones[4], foot = legBones[5] },
                };
            stepping = legsPlanted = false;
            stepWeight = 0f;
            nextLeg = -1;
            lockingFeet = false;
            lockWeight = 0f;
            System.Array.Clear(contactDown, 0, contactDown.Length);
            System.Array.Clear(plantWeight, 0, plantWeight.Length);
            plantValid = false;
            stanceTime = 0f;
            hoverWeight = hoverTarget = 0f;
            HoverDrop = 0f;

            graph = PlayableGraph.Create($"{id} fighter");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var output = AnimationPlayableOutput.Create(graph, "animation", animator);
            layers = AnimationLayerMixerPlayable.Create(graph, 3);
            output.SetSourcePlayable(layers);
            stancePlayable = AnimationClipPlayable.Create(graph, stance);
            stancePlayable.SetApplyFootIK(false);
            graph.Connect(stancePlayable, 0, layers, 0);
            layers.SetInputWeight(0, 1f);

            int mocapCount = clips.Count(c => !c.game), gameCount = clips.Count(c => c.game);
            mocapMixer = AnimationMixerPlayable.Create(graph, mocapCount);
            gameMixer = AnimationMixerPlayable.Create(graph, gameCount);
            graph.Connect(mocapMixer, 0, layers, 1);
            graph.Connect(gameMixer, 0, layers, 2);
            layers.SetInputWeight(1, 1f);
            layers.SetInputWeight(2, 0f);
            playables = new AnimationClipPlayable[clips.Count];
            slot = new int[clips.Count];
            weight = new float[clips.Count];
            target = new float[clips.Count];
            rate = new float[clips.Count];
            index.Clear();
            int nextMocap = 0, nextGame = 0;
            for (int i = 0; i < clips.Count; i++)
            {
                index[clips[i].name] = i;
                playables[i] = AnimationClipPlayable.Create(graph, clips[i].clip);
                playables[i].SetApplyFootIK(false);
                playables[i].Pause();       // time is set by Tick, not by the graph
                slot[i] = clips[i].game ? nextGame++ : nextMocap++;
                graph.Connect(playables[i], 0, Mixer(i), slot[i]);
                Mixer(i).SetInputWeight(slot[i], 0f);
                rate[i] = 1f;
            }
            // the mocap layer always holds a pose (all weights zero would be the bind pose)
            if (index.TryGetValue("guard", out int guard))
                weight[guard] = target[guard] = 1f;
            gameWeight = gameTarget = 0f;
            ApplyWeights();
            foreach (var d in directors)
            {
                if (d.director == null)
                    continue;
                d.director.playOnAwake = false;
                d.director.timeUpdateMode = DirectorUpdateMode.Manual;
            }
            current = -1;
            ShowWeapons(false);

            strideSpeeds.Clear();
            foreach (var name in new[] { "walk", "walk_back", "run" })
                if (index.ContainsKey(name))
                    strideSpeeds[name] = MeasureStride(name);
            // back to the guard, as if nothing had played
            for (int i = 0; i < clips.Count; i++)
            {
                weight[i] = target[i] = 0f;
                rate[i] = 1f;
                playables[i].SetTime(0);
            }
            if (index.TryGetValue("guard", out guard))
                weight[guard] = target[guard] = 1f;
            gameWeight = gameTarget = 0f;
            ApplyWeights();
            // the legs' rest for the skirt that follows them: the guard as the fight poses it (on the floor,
            // feet planted - the IK bends the legs a little), over one cycle
            if (skirtRig != null && index.TryGetValue("guard", out guard))
            {
                skirtRig.BeginGuard();
                capturingGuard = true;
                Play("guard", 1f, 0f);
                float length = Mathf.Max(0.1f, clips[guard].clip.length);
                for (int k = 0; k < 24; k++)
                    Tick(length / 24f);
                capturingGuard = false;
                skirtRig.EndGuard();
                for (int i = 0; i < clips.Count; i++)
                {
                    weight[i] = target[i] = 0f;
                    playables[i].SetTime(0);
                }
                weight[guard] = target[guard] = 1f;
                ApplyWeights();
            }
            // the pelvis the skirts are measured against: the guard's, averaged over its cycle - or, under the
            // skirt that follows the legs with its rest in the stance (RoeSkirtRig.RestOnStance), the stance's
            // as Init measured it, so the panels' roots and the bones aimed from them agree
            if (hipCloth != null && index.TryGetValue("guard", out guard) && !(skirtRig != null && RoeSkirtRig.RestOnStance))
            {
                var samples = new List<Quaternion>();
                float length = Mathf.Max(0.01f, clips[guard].clip.length);
                for (int k = 0; k < 8; k++)
                {
                    playables[guard].SetTime(length * k / 8f);
                    graph.Evaluate(0f);
                    samples.Add(Quaternion.Inverse(HipHeading()) * hips.rotation);
                }
                playables[guard].SetTime(0);
                var sum = Vector4.zero;
                foreach (var q in samples)
                {
                    float s = Quaternion.Dot(q, samples[0]) < 0f ? -1f : 1f;
                    sum += new Vector4(q.x, q.y, q.z, q.w) * s;
                }
                sum.Normalize();
                pelvisRef = new Quaternion(sum.x, sum.y, sum.z, sum.w);
            }
            current = -1;
            System.Array.Clear(plantWeight, 0, plantWeight.Length);
            System.Array.Clear(contactDown, 0, contactDown.Length);
            plantValid = false;
            stanceTime = 0f;
            cloth?.Reset();
            skirtRig?.Idle();
        }

        readonly Dictionary<string, float> strideSpeeds = new Dictionary<string, float>();

        /// <summary>
        /// How fast a planted foot travels back under the body in a locomotion clip played in place
        /// at rate 1 (m/s).  The fight moves the body at its own speed; playing the clip at
        /// speed / this keeps the planted foot still on the floor.  Measured on this body in Init.
        /// </summary>
        public float StrideSpeed(string clip) => strideSpeeds.TryGetValue(clip, out var v) && v > 0.05f ? v : 1f;

        float MeasureStride(string clip)
        {
            if (toes == null || toes.Any(b => b == null))
                return 0f;
            const float dt = 1f / 30f;
            Play(clip, 1f, 0f);
            float length = Length(clip);
            var heights = new List<Vector2>[] { new List<Vector2>(), new List<Vector2>() };   // per ball: (height, forward)
            for (float t = 0f; t < length; t += dt)
            {
                Tick(t == 0f ? 0f : dt);
                for (int k = 0; k < 2; k++)
                {
                    var p = transform.InverseTransformPoint(toes[k].position);
                    heights[k].Add(new Vector2(p.y, p.z));
                }
            }
            // the ball of a foot is planted within 1 cm of its lowest point (these heels stand on it)
            float sum = 0f;
            int n = 0;
            for (int k = 0; k < 2; k++)
            {
                float floor = heights[k].Min(h => h.x) + 0.01f;
                for (int f = 1; f < heights[k].Count; f++)
                    if (heights[k][f].x < floor && heights[k][f - 1].x < floor)
                    {
                        sum += (heights[k][f].y - heights[k][f - 1].y) / dt;
                        n++;
                    }
            }
            return n > 0 ? Mathf.Abs(sum / n) : 0f;
        }

        /// <summary>The stride speeds measured in Init, for the log.</summary>
        public string StrideReport() => string.Join(", ", strideSpeeds.Select(kv => $"{kv.Key} {kv.Value:F2} m/s"));

        void OnDestroy()
        {
            if (graph.IsValid())
                graph.Destroy();
            (cloth as IDisposable)?.Dispose();
        }

        AnimationMixerPlayable Mixer(int clip) => clips[clip].game ? gameMixer : mocapMixer;

        public bool Has(string clip) => index.ContainsKey(clip);

        public float Length(string clip) => index.TryGetValue(clip, out int i) && clips[i].clip != null ? clips[i].clip.length : 0f;

        /// <summary>Crossfade to a clip.  restart = from its start; otherwise it keeps its own time.</summary>
        public void Play(string clip, float speed = 1f, float fadeSeconds = 0.12f, bool restart = true, float startTime = 0f)
        {
            if (!index.TryGetValue(clip, out int i))
                return;
            fade = Mathf.Max(0.0001f, fadeSeconds);
            if (restart || current != i)
                playables[i].SetTime(startTime);
            rate[i] = speed;
            bool game = clips[i].game;
            // a crossfade among the clips of the same layer; the other layer keeps its pose underneath
            for (int k = 0; k < target.Length; k++)
                if (clips[k].game == game)
                    target[k] = k == i ? 1f : 0f;
            gameTarget = game ? 1f : 0f;
            hoverTarget = game && clips[i].hovers ? 1f : 0f;
            if (fadeSeconds <= 0f)
            {
                for (int k = 0; k < weight.Length; k++)
                    weight[k] = target[k];
                gameWeight = gameTarget;
                hoverWeight = hoverTarget;
                ApplyWeights();
            }
            current = i;
        }

        public void SetSpeed(float speed)
        {
            if (current >= 0)
                rate[current] = speed;
        }

        public void SetTime(float time)
        {
            if (current >= 0)
                playables[current].SetTime(time);
        }

        /// <summary>Advance the clips and evaluate the pose (and a running skill timeline).</summary>
        public void Tick(float dt)
        {
            if (!graph.IsValid())
                return;
            stanceTime += dt;
            if (stance != null)
                stancePlayable.SetTime(stanceTime % Mathf.Max(0.01f, stance.length));
            float step = dt / fade;
            gameWeight = Mathf.MoveTowards(gameWeight, gameTarget, step);
            for (int i = 0; i < playables.Length; i++)
            {
                if (weight[i] <= 0f && target[i] <= 0f)
                    continue;
                weight[i] = Mathf.MoveTowards(weight[i], target[i], step);
                double t = playables[i].GetTime() + dt * rate[i];
                var c = clips[i];
                if (c.loop && c.clip.length > 0f)
                    t %= c.clip.length;
                else
                    t = Math.Min(t, c.clip.length);
                // her own guard is her game's stance (ownGuard): in step with layer 0's, so the skirt, hair and fan there are
                // keyed for the very frame the body is in
                if (ownGuard != null && c.clip == ownGuard && stance != null)
                    t = stanceTime % Mathf.Max(0.01f, stance.length);
                playables[i].SetTime(t);
            }
            ApplyWeights();
            cloth?.Rest();                    // cloth bones no clip animates start from their rest pose again
            // the weapons at their own size: back to the bone's scale before the clips write the frame (a scale
            // no clip animates must not compound), scaled after
            for (int k = 0; k < weaponRoots.Length; k++)
                weaponRoots[k].localScale = weaponRootScale[k];
            ueRig?.Rest();                    // an Unreal body's unmapped spine joints where the humanoid expects them
            graph.Evaluate(0f);
            ueRig?.Distribute();              // ... and their share of the bend given back (RoeUeRig)
            if (weaponScale != 1f)
                foreach (var w in weaponRoots)
                    w.localScale *= weaponScale;
            StepLinger(dt);
            float mocap = 1f - gameWeight;
            LimitRoll(mocap);
            // (IgnoreSkirtKeys: the skirt as under motion capture in the game's own clips too, its keys kept to measure against)
            // (a solver over the game's clips too - magica_full - takes their skirt the same way)
            bool overGame = IgnoreSkirtKeys || (backend != null && backend.overGameClips);
            float skirtMocap = overGame ? 1f : mocap;
            // standing in her game's stance - her guard is that clip (ownGuard) - she shows the game's keys as in its own clips
            // (user 10-11 "luf的裙子能像ROE一样自然下垂么"): the skirt neither follows the legs nor turns with the hips there, and the
            // cloth writes nothing; it takes over as she moves out of it (a solver over the game's clips too keeps it) - and as
            // her legs step sideways (StepLegs: a side step is the guard's upper body over legs we step)
            float stanceKeys = !overGame && ownGuard != null && index.TryGetValue("guard", out int g) ? weight[g] * (1f - stepWeight) : 0f;
            float skirtFollow = skirtMocap * (1f - stanceKeys);
            bool measureKeys = IgnoreSkirtKeys && gameWeight > 0.99f && skirtRig != null && skirtRig.Ready && hips != null;
            if (measureKeys)
                KeepSkirtKeys();
            bool measureChains = MeasureKeyKinds.Count > 0 && gameWeight > 0.99f && hips != null;
            if (measureChains)
                KeepChainKeys();
            // the ankle as stiff as her shoe (bindFeet): the foot and toes at their bind rotations against the shin, before
            // the feet are stood on the floor
            if (bindFeet && grounded && bindFoot.Length == 2 && bindToe.Length == 2)
                for (int i = 0; i < 2; i++)
                {
                    feet[i].localRotation = bindFoot[i];
                    toes[i].localRotation = bindToe[i];
                }
            if (dt > 0f)
                hoverWeight = Mathf.MoveTowards(hoverWeight, hoverTarget, dt / HoverRise);
            // the share of the game's hover that comes down (gameHover)
            float unhover = gameHover > 0f ? gameWeight * (1f - hoverWeight) : 0f;
            HoverDrop = 0f;
            // motion capture on the floor: retargeted onto these long-legged, high-heeled bodies it
            // floats 11-18 cm; the lowest sole goes down to the floor (the game's clips stay as they are,
            // but for a hover that comes down: gameHover)
            if (grounded && (mocap > 0f || unhover > 0f))
            {
                // Flat feet (flatFeet): the motion capture leaves her feet in the bind pose's angle, pointed like a
                // high heel - turned up about the ankle to her standing angle first.
                if (flatFeet && mocap > 0f && flatPitch.Length == 2 && flatAxis.Length == 2)
                    for (int i = 0; i < 2; i++)
                        feet[i].localRotation *= Quaternion.AngleAxis(flatPitch[i] * mocap, flatAxis[i]);
                // A planted foot stands in its high-heeled shoe the way the game's stance has it.  The
                // motion capture's feet are flat shoes': put on these heels as they were, a standing foot
                // tipped onto its toes, the heel spike 10 cm in the air and the toes 2-3 cm in the floor.
                // (Flat feet stand the way her flattest stand in the game's clips has them.)
                // The foot keeps the direction the clip points it in; feet lifted by the clip (steps,
                // kicks) keep the clip's angle, blended by how far the ankle is above its standing height.
                float lowest = LowestSole();
                for (int i = 0; i < 2 && mocap > 0f; i++)
                {
                    float above = feet[i].position.y - lowest - ankleSole;
                    standWeight[i] = (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.03f, 0.12f, above))) * mocap;
                    Stand(i, standWeight[i]);
                    toes[i].localRotation = Quaternion.Slerp(toes[i].localRotation, toeRest[i], mocap);
                }
                lowest = LowestSole();
                groundOffset = animator.transform.position.y - lowest;
                if (unhover > 0f)
                {
                    float floor = animator.transform.position.y, room = float.MaxValue;
                    foreach (var b in body)
                        room = Mathf.Min(room, b.position.y - floor - BodyClearance);
                    HoverDrop = Mathf.Min(Mathf.Clamp(-groundOffset, 0f, gameHover), Mathf.Max(0f, room)) * unhover;
                }
                hips.position += Vector3.up * (groundOffset * mocap - HoverDrop);
            }
            // bindFeet: her "game clips" are other bodies' too (UFE 2's), and retargeted onto her shoes the standing ones put
            // the soles 13-14 cm into the floor (the intro).  While she is upright her body comes up until the lowest sole is
            // on the floor - only up: a jump keeps its height, and lying down (head no higher than the hips) nothing changes.
            if (bindFeet && grounded && gameWeight > 0f && head != null)
            {
                float sink = animator.transform.position.y - LowestSole();
                var spine = head.position - hips.position;
                float upright = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.4f, 0.75f, spine.y / Mathf.Max(spine.magnitude, 1e-4f)));
                if (sink > 0f)
                    hips.position += Vector3.up * (sink * upright);
            }
            if (grounded && mocap > 0f)
            {
                // The other foot: retargeted onto these legs, a foot the actor had on the floor often
                // hovers 1-3 cm above it.  A foot that stays put and is that close goes down to the
                // floor (two-bone IK); a foot that is travelling (a step, a kick) is left alone.
                float floor = animator.transform.position.y;
                for (int i = 0; i < 2 && legs != null; i++)
                {
                    var ankle = feet[i].position;
                    float above = ankle.y - floor - ankleSole;
                    if (dt > 0f)
                    {
                        float speed = plantValid ? Flat(ankle - lastAnkle[i]).magnitude / dt : 0f;
                        plantWeight[i] = Mathf.MoveTowards(plantWeight[i], above < 0.05f && speed < 0.35f ? 1f : 0f, dt / 0.1f);
                    }
                    lastAnkle[i] = ankle;
                    if (plantWeight[i] > 0f && above > 0f)
                        TwoBoneIK(legs[i].upper, legs[i].lower, legs[i].foot, ankle - Vector3.up * (above * plantWeight[i] * mocap), transform.forward);
                    // and stands as a planted foot does: posed by its height before it came down, g05's front foot in the
                    // guard (5 cm higher in the motion capture, heel up) kept a quarter of the clip's angle - 43 degrees
                    // instead of her 36, the toes 1.5 cm in the floor
                    float stand = plantWeight[i] * mocap;
                    if (stand > standWeight[i] && standWeight[i] < 1f)
                        Stand(i, (stand - standWeight[i]) / (1f - standWeight[i]));
                }
                plantValid = dt > 0f;
            }
            // Skirts hang the way the stance has them, and move with the pelvis from there.  The game keys
            // its skirt panels nearly rigid on the pelvis (RoeHelperFit.SkirtReport: closest of the simple
            // models), but its stance's pelvis tilts (the right hip 23 degrees above the left): the same
            // keys on the motion capture's level pelvis held a08's long side panels out at 45 degrees like
            // boards.  So in the guard a panel takes its stance rotation against the hips' heading, and
            // any turn of the pelvis away from the guard's (swaying hips, a kick) turns it along; the
            // cloth swings it from there.  Under the skirt that follows the legs (RoeSkirtRig) the bones
            // below are aimed anew, and with its rest in the stance the reference is the stance's pelvis:
            // the panels' roots then sit where the skinned skirt expects them.
            // (not under a solver that hangs the skirt as its own game does: Stellar Blade's Eve 37 - her skirt rig reads the
            // panels' roots where the animation has them)
            if (hipCloth != null && skirtFollow > 0f && !NoHipCloth && !(backend != null && backend.ownSkirt))
            {
                var heading = HipHeading();
                var turn = Quaternion.Inverse(heading) * hips.rotation * Quaternion.Inverse(pelvisRef);
                for (int k = 0; k < hipCloth.Length; k++)
                    hipCloth[k].rotation = Quaternion.Slerp(hipCloth[k].rotation, heading * turn * hipClothRest[k], skirtFollow);
            }
            TrackContact();                 // on the animated pose, before the legs are bent to locked feet
            StepLegs(dt, mocap);
            LockFeet(dt, mocap);
            // the limb helpers: the game's clips animate them; under motion capture they follow the limbs
            if (helpers != null)
                helpers.Apply(mocap);
            // a DOA6 character's twist helpers (her game turns them by script, her clips do not key them), then her grid
            // cloth's skinned rows on their bones (they follow the hips and thighs in any setup, the solvers move the rest)
            if (doaRig != null)
            {
                doaRig.DriveHelpers();
                doaRig.DriveGrids();
            }
            // an Unreal body's twist bones (Vindictus: the humanoid does not move them), on the legs' final pose
            ueRig?.DriveTwists();
            // her weapon in her hand under the motion capture (Holds), before the cloth swings its chain; the game's own
            // clips key it themselves (thrown, two-handed, the other hand).  Standing in her game's stance (ownGuard) it stays
            // as that stance keys it - layer 0 plays it in step - and the grip she strikes with comes in as she moves out of it
            // (user 10-17 "inase的默认动作能换成ROE中的inase的么": a08 stands with her greatsword's tip on the floor, her hand on
            // the pommel; her strikes hold it as she cuts in skill_01).  A side step keeps the stance's upper body, and the sword.
            float stanceWeapon = ownGuard != null && index.TryGetValue("guard", out int gw) ? weight[gw] : 0f;
            if (Holds && mocap > 0f)
                Grips.Apply(animator, mocap * (1f - stanceWeapon));
            if (Holds)
                ShowWeapons(weaponsShown);       // a burst stage may have taken a held weapon's holster off
            // the skirt's animation pose under motion capture (RoeSkirtRig): fitted to follow the legs as the
            // game's animators key it, then hung on the body (the skin, posed with the helpers above); the
            // cloth swings from there.  On the legs' final pose.
            if (capturingGuard)
                skirtRig.AddGuard(hips.position, HipHeading());
            else if (skirtRig != null && skirtRig.Ready && skirtFollow > 0f && !NoHipCloth)
                skirtRig.Apply(hips.position, HipHeading(), skirtFollow, dt);
            else
                skirtRig?.Idle();
            // the cloth on the finished pose; the game's own clips keep their hand-keyed skirts and hair
            if (cloth != null)
            {
                if (Cloth is RoeBoneCloth boneCloth)
                {
                    boneCloth.skirtOnSkin = skirtRig != null && skirtRig.Ready && RoeSkirtRig.Drape > 0f;
                    boneCloth.alwaysWeight = clothWeight;     // sashes: over the game's clips too (RoeBoneCloth.AlwaysKinds)
                }
                cloth.Weight = (clothOverGameClips || overGame ? 1f : mocap * (1f - stanceKeys)) * clothWeight;
                cloth.Step(dt, transform.position.y);
            }
            // (MeasureKeysLate: taken when the frame is drawn, after Magica Cloth 2 has written its bones - MeasureKeysNow)
            if (measureKeys)
            {
                if (MeasureKeysLate)
                    skirtMeasureDue = true;
                else
                    MeasureSkirtKeys();
            }
            if (measureChains)
            {
                if (MeasureKeysLate)
                    chainMeasureDue = true;
                else
                    MeasureChainKeys();
            }
            // a DOA6 character's visible cloth (skirt, sleeves): rebuilt from its control points, whichever solver moved them
            if (doaRig != null)
                doaRig.RebuildSurfaces();
        }

        // ---- cloth (RoeClothBackends): skirts, hair, chains, breasts

        public bool useCloth = true;
        [Range(0f, 1f)] public float clothWeight = 1f;
        public string clothBackend;         // null or empty: the fight's choice (ClothBackend)
        /// <summary>The cloth backend every fighter uses unless it names its own (F4 in the fight).</summary>
        public static string ClothBackend = "auto";
        IRoeCloth cloth;
        RoeClothBackends.Backend backend;
        RoeSkirtRig skirtRig;
        bool capturingGuard;

        /// <summary>The cloth starts again from the animated pose (after a teleport: a new round).</summary>
        public void ResetCloth() => cloth?.Reset();

        public static bool NoHipCloth;      // for checks (RoeFightProbe.SkirtSwing -roeVariant nohip)

        /// <summary>
        /// For checks (RoeClothDemo -roeSkirtKeys off): the game's own clips leave their hand-keyed skirt to the solver as
        /// motion capture does (the skirt follows the legs, RoeSkirtRig, the cloth over every clip), and how far the skirt
        /// ends up from the keys is measured: each skirt bone's direction in the pelvis frame against the keyed one
        /// (SkirtKeyError, over the frames of the game's clips).
        /// </summary>
        public static bool IgnoreSkirtKeys;
        double skirtKeySum;
        int skirtKeyCount;
        float skirtKeyMax;
        Quaternion[] keyedSkirt = new Quaternion[0];
        Quaternion keyedPelvis;

        public string SkirtKeyError => skirtKeyCount == 0 ? "no frames of the game's clips"
            : $"{Math.Sqrt(skirtKeySum / skirtKeyCount):F1} deg RMS (worst {skirtKeyMax:F0}) over {skirtKeyCount} bone-frames";

        /// <summary>The same as a number (RMS degrees; NaN before any frame of the game's clips) - RoeSkirtTune.</summary>
        public float SkirtKeyRms => skirtKeyCount == 0 ? float.NaN : (float)Math.Sqrt(skirtKeySum / skirtKeyCount);

        public void ResetSkirtKeyError()
        {
            skirtKeySum = 0;
            skirtKeyCount = 0;
            skirtKeyMax = 0f;
        }

        void KeepSkirtKeys()
        {
            var joints = skirtRig.joints;
            if (keyedSkirt.Length != joints.Count)
                keyedSkirt = new Quaternion[joints.Count];
            for (int k = 0; k < joints.Count; k++)
                keyedSkirt[k] = joints[k].bone.rotation;
            keyedPelvis = hips.rotation;
        }

        void MeasureSkirtKeys()
        {
            var joints = skirtRig.joints;
            var nowPelvis = Quaternion.Inverse(hips.rotation);
            var keyPelvis = Quaternion.Inverse(keyedPelvis);
            for (int k = 0; k < joints.Count && k < keyedSkirt.Length; k++)
            {
                var j = joints[k];
                var keyed = keyPelvis * (keyedSkirt[k] * j.tailLocal);
                var now = nowPelvis * (j.bone.rotation * j.tailLocal);
                float a = Vector3.Angle(keyed, now);
                skirtKeySum += a * a;
                skirtKeyCount++;
                skirtKeyMax = Mathf.Max(skirtKeyMax, a);
            }
        }

        /// <summary>
        /// For checks (RoeClothDemo / RoeClothPlayDemo -roeMeasureKeys ribbon,hair): in the game's own clips the bones of these
        /// cloth kinds (RoeBoneCloth.RoeKindOf, by name) are measured against their keys like the skirt (ChainKeyError) -
        /// whatever moves them, so every solver is measured the same way (Goddess Luf's sashes run over the game's clips
        /// anyway, RoeBoneCloth.AlwaysKinds).  The top bone of each chain (its anchor, which nothing simulates) is left out.
        /// </summary>
        public static readonly HashSet<string> MeasureKeyKinds = new HashSet<string>();

        /// <summary>
        /// For checks in play mode (RoeClothPlayDemo): Magica Cloth 2 writes its bones after the fight's step (in
        /// PreLateUpdate), so the skirt's and the chains' measurements against the keys wait for MeasureKeysNow, called when
        /// the frame is drawn; the keys themselves are still taken in the step, before anything moves the bones.
        /// </summary>
        public static bool MeasureKeysLate;
        bool skirtMeasureDue, chainMeasureDue;

        /// <summary>The measurements a step left for later (MeasureKeysLate), on the pose as it is now.</summary>
        public void MeasureKeysNow()
        {
            if (skirtMeasureDue && skirtRig != null && hips != null)
                MeasureSkirtKeys();
            if (chainMeasureDue && hips != null)
                MeasureChainKeys();
            skirtMeasureDue = chainMeasureDue = false;
        }
        List<(Transform bone, Vector3 tail, string kind)> keyChain;
        Quaternion[] keyedChain = new Quaternion[0];
        Quaternion keyedChainPelvis;
        // per kind: sum of squares, bone-frames, worst, bones
        readonly SortedDictionary<string, (double sum, int count, float max, int bones)> chainKeyError =
            new SortedDictionary<string, (double, int, float, int)>();

        /// <summary>Per kind: RMS degrees against the keys, the worst, over how many bone-frames of how many bones.</summary>
        public string ChainKeyError => chainKeyError.Count == 0 || chainKeyError.Values.All(e => e.count == 0) ? "no frames of the game's clips"
            : string.Join(", ", chainKeyError.Select(kv => $"{kv.Key} {Math.Sqrt(kv.Value.sum / Math.Max(1, kv.Value.count)):F1} deg RMS " +
                                                            $"(worst {kv.Value.max:F0}) over {kv.Value.count} bone-frames of {kv.Value.bones} bones"));

        public void ResetChainKeyError()
        {
            chainKeyError.Clear();
            keyChain = null;
        }

        void KeepChainKeys()
        {
            if (keyChain == null)
            {
                keyChain = new List<(Transform, Vector3, string)>();
                foreach (var t in animator.GetComponentsInChildren<Transform>(true))
                {
                    string kind = RoeBoneCloth.RoeKindOf(t.name);
                    if (kind == null || !MeasureKeyKinds.Contains(kind) || t.parent == null || RoeBoneCloth.RoeKindOf(t.parent.name) != kind)
                        continue;
                    Transform tip = null;
                    foreach (Transform c in t)
                        if (c.localPosition.sqrMagnitude > 1e-8f)
                        {
                            tip = c;
                            break;
                        }
                    if (tip != null)
                        keyChain.Add((t, tip.localPosition, kind));
                }
                keyedChain = new Quaternion[keyChain.Count];
                foreach (var g in keyChain.GroupBy(c => c.kind))
                    chainKeyError[g.Key] = (0, 0, 0f, g.Count());
            }
            for (int k = 0; k < keyChain.Count; k++)
                keyedChain[k] = keyChain[k].bone.rotation;
            keyedChainPelvis = hips.rotation;
        }

        void MeasureChainKeys()
        {
            if (keyChain == null)
                return;
            var nowPelvis = Quaternion.Inverse(hips.rotation);
            var keyPelvis = Quaternion.Inverse(keyedChainPelvis);
            for (int k = 0; k < keyChain.Count; k++)
            {
                var (bone, tail, kind) = keyChain[k];
                float a = Vector3.Angle(keyPelvis * (keyedChain[k] * tail), nowPelvis * (bone.rotation * tail));
                var e = chainKeyError[kind];
                chainKeyError[kind] = (e.sum + a * a, e.count + 1, Mathf.Max(e.max, a), e.bones);
            }
        }

        /// <summary>The bone cloth, for checks (null when off or another backend).</summary>
        public RoeBoneCloth Cloth => cloth as RoeBoneCloth ?? (cloth as RoeClothRouter)?.PartFor<RoeBoneCloth>("skirt");
        /// <summary>The solver of a type among the fighter's cloth (for checks).</summary>
        public T ClothPart<T>() where T : class => cloth as T ?? (cloth as RoeClothRouter)?.Part<T>();

        /// <summary>
        /// The cloth is simulated over the game-style clips too: a DOA6 character's clips (her moves, hurt, knockdown) key
        /// none of her loose bones - unlike ROE's, whose skills key skirts and hair by hand.
        /// </summary>
        public bool clothOverGameClips;

        public string ClothReport => cloth != null ? cloth.Report : "off";

        /// <summary>The cloth backend in use (and whether the skirt follows the legs).</summary>
        public string ClothTitle => backend == null ? "" : backend.title + (skirtRig != null ? ", skirt follows the legs" : "");

        // ---- a planted thigh turned about its own axis no further than the skin was weighted for (rollFrom, rollKeep)

        /// <summary>The last Tick's turn of each thigh about its axis against the pelvis as the clip had it and as it stands (degrees), for the probes.</summary>
        public Vector2[] Roll { get; } = new Vector2[2];

        // per leg: a drive of a helper on the thigh that reads its roll against the pelvis (RoeHelperFit: the thigh's axis in
        // its own frame, the pelvis' rest turn in the thigh's frame) - the same for every helper the thigh drives
        (Vector3 axis, Quaternion rest)?[] rollRefs;

        void LimitRoll(float mocap)
        {
            if (legs == null || helpers == null || hips == null)
                return;
            if (rollRefs == null)
                rollRefs = legs.Select(l =>
                {
                    foreach (var d in helpers.drives)
                        if (d.driver == l.upper)
                        {
                            if (d.twistSource == hips)
                                return ((Vector3 axis, Quaternion rest)?)(d.twistAxis, d.twistRest);
                            if (d.twistSource2 == hips)
                                return (d.twistAxis, d.twistRest2);
                        }
                    return null;
                }).ToArray();
            float lowest = Mathf.Min(legs[0].foot.position.y, legs[1].foot.position.y);
            for (int i = 0; i < 2; i++)
            {
                var r = rollRefs[i];
                if (r == null)
                    continue;
                var l = legs[i];
                var (axis, rest) = r.Value;
                float tau = RoeHelperRig.TwistAngle(Quaternion.Inverse(l.upper.rotation) * hips.rotation * Quaternion.Inverse(rest), axis);
                float roll = -tau;
                Roll[i] = new Vector2(roll, roll);
                if (rollKeep >= 1f || mocap <= 0f || !grounded || Mathf.Abs(roll) <= rollFrom)
                    continue;
                // a foot off the floor (above the other one's: a kick, a step) keeps its turn
                float above = l.foot.position.y - lowest;
                float w = (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.05f, 0.2f, above))) * mocap;
                if (w <= 0f)
                    continue;
                float held = Mathf.Lerp(roll, Mathf.Sign(roll) * (rollFrom + (Mathf.Abs(roll) - rollFrom) * rollKeep), w);
                var ankle = l.foot.position;
                var footRotation = l.foot.rotation;
                l.upper.rotation = l.upper.rotation * Quaternion.AngleAxis(tau + held, axis);      // its roll is now "held"
                // the foot: where the turned leg puts it (it stands on the floor by the usual rules after this), at the
                // clip's height, or back where the clip had it (the knee then turns back with it - and so does the bulge)
                if (rollFoot == 2)
                    TwoBoneIK(l.upper, l.lower, l.foot, ankle, transform.forward);
                else if (rollFoot == 1)
                    TwoBoneIK(l.upper, l.lower, l.foot, new Vector3(l.foot.position.x, ankle.y, l.foot.position.z), transform.forward);
                l.foot.rotation = footRotation;
                Roll[i] = new Vector2(roll, -RoeHelperRig.TwistAngle(Quaternion.Inverse(l.upper.rotation) * hips.rotation * Quaternion.Inverse(rest), axis));
            }
        }

        // ---- walking: a foot the clip puts down stays where it landed (two-bone IK)

        bool lockingFeet;
        float lockWeight;

        /// <summary>While walking: feet the clip puts down are locked where they landed.</summary>
        public void LockFeet(bool on) => lockingFeet = on;

        void LockFeet(float dt, float mocap)
        {
            if (legs == null || !grounded)
                return;
            if (dt > 0f)
                lockWeight = Mathf.MoveTowards(lockWeight, lockingFeet ? 1f : 0f, dt / 0.1f);
            for (int i = 0; i < 2; i++)
            {
                var l = legs[i];
                var ankle = l.foot.position;
                var local = transform.InverseTransformPoint(ankle);
                var ball = transform.InverseTransformPoint(toes[i].position);
                bool down = lockWeight > 0f && mocap > 0f && (local.y < ankleSole + 0.015f || ball.y < toeSole + 0.012f);
                if (down && !l.locked)
                {
                    l.locked = true;
                    l.lockAt = ankle;
                }
                else if (!down)
                    l.locked = false;
                if (l.locked && Flat(l.lockAt - ankle).magnitude > 0.25f)   // the body went on too far: let go
                    l.locked = false;
                if (dt > 0f)
                    l.lockBlend = Mathf.MoveTowards(l.lockBlend, l.locked ? 1f : 0f, dt / 0.08f);
                if (l.lockBlend <= 0f)
                    continue;
                var at = new Vector3(l.lockAt.x, ankle.y, l.lockAt.z);
                TwoBoneIK(l.upper, l.lower, l.foot, Vector3.Lerp(ankle, at, l.lockBlend * lockWeight * mocap), transform.forward);
            }
        }

        // ---- the planted foot, for walking: the body goes where the foot pushes it

        readonly Vector3[] lastContact = new Vector3[4];
        readonly bool[] contactDown = new bool[4];

        /// <summary>How far the planted parts of the feet (balls, heels) moved under the body in the last Tick (in the fighter's frame, flat).</summary>
        public Vector3 ContactDelta { get; private set; }
        public bool HasContact { get; private set; }

        void TrackContact()
        {
            HasContact = false;
            ContactDelta = Vector3.zero;
            if (!grounded)
                return;
            var sum = Vector3.zero;
            int n = 0;
            for (int k = 0; k < 4; k++)
            {
                // 0, 1 the balls of the feet; 2, 3 the ankles (a heel coming down)
                var bone = k < 2 ? toes[k] : feet[k - 2];
                var p = transform.InverseTransformPoint(bone.position);
                bool down = p.y < (k < 2 ? toeSole : ankleSole) + 0.012f;
                if (down && contactDown[k])
                {
                    sum += p - lastContact[k];
                    n++;
                }
                contactDown[k] = down;
                lastContact[k] = p;
            }
            if (n > 0)
            {
                HasContact = true;
                ContactDelta = Flat(sum / n);
            }
        }

        // ---- side steps: procedural legs
        //
        // The motion capture has no step to the side while facing forward, and a made-up clip cannot
        // keep pace with the speed the fight moves the body at: the feet slid.  While the fighter
        // steps sideways the legs step by themselves.  The upper body keeps the guard; a planted foot
        // stays where it landed; when it lags behind its place under the body it swings in an arc to
        // a point ahead (lead foot first, then the other), and two-bone IK bends the leg to the foot.

        class Leg
        {
            public Transform upper, lower, foot;
            public Vector3 planted, from, to;
            public bool swinging;
            public float t;
            public bool locked;            // walking: the foot is held where it landed
            public Vector3 lockAt;
            public float lockBlend;
        }

        Leg[] legs;
        bool stepping, legsPlanted;
        Vector3 stepVelocity;
        float stepWeight;
        int nextLeg = -1;
        public const float SwingTime = 0.17f, SwingLift = 0.07f;

        /// <summary>The fighter steps sideways at this velocity (zero: it does not).</summary>
        public void SideStep(Vector3 velocity)
        {
            stepping = velocity.sqrMagnitude > 1e-4f;
            if (stepping)
                stepVelocity = velocity;
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        void StepLegs(float dt, float mocap)
        {
            if (legs == null)
                return;
            if (dt > 0f)
                stepWeight = Mathf.MoveTowards(stepWeight, stepping ? 1f : 0f, dt / (stepping ? 0.06f : 0.15f));
            if (stepWeight <= 0f || mocap <= 0f)
            {
                legsPlanted = false;
                nextLeg = -1;
                return;
            }
            var home = new[] { legs[0].foot.position, legs[1].foot.position };     // where the animation puts the ankles
            if (!legsPlanted)
            {
                for (int i = 0; i < 2; i++)
                {
                    legs[i].planted = home[i];
                    legs[i].swinging = false;
                }
                legsPlanted = true;
            }
            foreach (var l in legs)
            {
                if (!l.swinging)
                    continue;
                l.t += dt / SwingTime;
                if (l.t >= 1f)
                {
                    l.swinging = false;
                    l.planted = l.to;
                }
            }
            if (stepping && !legs[0].swinging && !legs[1].swinging)
            {
                var dir = stepVelocity.normalized;
                var centre = (home[0] + home[1]) * 0.5f;
                if (nextLeg < 0)
                    nextLeg = Vector3.Dot(home[0] - centre, dir) >= Vector3.Dot(home[1] - centre, dir) ? 0 : 1;
                var l = legs[nextLeg];
                if (Flat(home[nextLeg] - l.planted).magnitude > stepVelocity.magnitude * SwingTime * 0.5f)
                {
                    l.swinging = true;
                    l.t = 0f;
                    l.from = l.planted;
                    // under its place in the middle of the next stance
                    l.to = home[nextLeg] + Flat(stepVelocity) * (SwingTime * 1.5f);
                    nextLeg = 1 - nextLeg;
                }
            }
            for (int i = 0; i < 2; i++)
            {
                var l = legs[i];
                var p = l.planted;
                if (l.swinging)
                    p = Vector3.Lerp(l.from, l.to, Mathf.SmoothStep(0f, 1f, l.t)) + Vector3.up * (SwingLift * Mathf.Sin(Mathf.PI * l.t));
                TwoBoneIK(l.upper, l.lower, l.foot, Vector3.Lerp(home[i], p, stepWeight * mocap), transform.forward);
            }
        }

        /// <summary>Bend upper and lower bone so that the end bone reaches the target; the end keeps its world rotation.</summary>
        static void TwoBoneIK(Transform upper, Transform lower, Transform end, Vector3 target, Vector3 bendHint)
        {
            var endRotation = end.rotation;
            Vector3 a = upper.position, b = lower.position, c = end.position;
            float la = (b - a).magnitude, lb = (c - b).magnitude;
            var toTarget = target - a;
            if (la < 1e-4f || lb < 1e-4f || toTarget.sqrMagnitude < 1e-8f)
                return;
            float d = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(la - lb) + 1e-3f, la + lb - 1e-3f);
            var u = toTarget.normalized;
            var pole = (b - a) - Vector3.Dot(b - a, u) * u;          // the knee keeps its side
            if (pole.sqrMagnitude < 1e-6f)
                pole = bendHint - Vector3.Dot(bendHint, u) * u;
            pole.Normalize();
            float cosA = Mathf.Clamp((la * la + d * d - lb * lb) / (2f * la * d), -1f, 1f);
            var knee = a + la * (cosA * u + Mathf.Sqrt(1f - cosA * cosA) * pole);
            upper.rotation = Quaternion.FromToRotation(b - a, knee - a) * upper.rotation;
            lower.rotation = Quaternion.FromToRotation(end.position - lower.position, a + u * d - lower.position) * lower.rotation;
            end.rotation = endRotation;
        }

        /// <summary>Vertical shift that put the motion capture on the floor in the last step (for checks).</summary>
        public float GroundOffset => groundOffset;

        /// <summary>How high the lowest sole stands above the floor in the current pose (m; for checks, and how slowly a hovering fighter comes down after a skill).</summary>
        public float SoleHeight => grounded ? LowestSole() - animator.transform.position.y : 0f;

        /// <summary>The direction the hips face along the floor (from the thighs).</summary>
        public Quaternion HipHeading()
        {
            var forward = Vector3.Cross(thighs[1].position - thighs[0].position, Vector3.up);
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-8f)
                forward = Flat(transform.forward);
            return Quaternion.LookRotation(forward.normalized, Vector3.up);
        }

        /// <summary>
        /// Turn foot i towards the way it stands (footRest, about the direction it points along the floor) by w.  The
        /// direction comes from the foot's axis across it (the ankle's hinge), not from where the toes point: ROE models
        /// its feet pointed 67-74 degrees (bare or in heels), and a heel the motion capture raises on top takes the toes
        /// past straight down - the heading from the toes then turns round, and the foot stands on the floor the wrong
        /// way round (g05's back foot, toes 146-180 degrees off the knee in 40 of 91 planted frames, while RoeFeet also
        /// turned her right foot down instead of up).  A foot lying on its side (the axis near upright) falls back to
        /// the toes.
        /// </summary>
        void Stand(int i, float w)
        {
            var ahead = Heading(i);
            if (w > 0f)
            {
                FootToes[i] = toes[i].position - feet[i].position;
                FootAhead[i] = ahead;
                FootWeight[i] = w;
            }
            if (w > 0f && ahead.sqrMagnitude > 1e-6f)
                feet[i].rotation = Quaternion.Slerp(feet[i].rotation, Quaternion.LookRotation(ahead.normalized, Vector3.up) * footRest[i], w);
        }

        /// <summary>The direction foot i points along the floor (not normalized), from its axis across it (see Stand).</summary>
        public Vector3 Heading(int i)
        {
            var side = feet[i].rotation * footSide[i];
            return Mathf.Abs(side.y) < 0.94f ? Vector3.Cross(side, Vector3.up) : Flat(toes[i].position - feet[i].position);
        }

        /// <summary>For checks: where each foot's toes pointed along the floor before it was turned to stand, and the heading it stood in.</summary>
        public readonly Vector3[] FootToes = new Vector3[2], FootAhead = new Vector3[2];
        public readonly float[] FootWeight = new float[2];

        /// <summary>Where the floor would be under the lowest sole (toe or ankle bone at its standing height).</summary>
        float LowestSole()
        {
            float lowest = float.MaxValue;
            for (int i = 0; i < 2; i++)
                lowest = Mathf.Min(lowest, Mathf.Min(toes[i].position.y - toeSole, feet[i].position.y - ankleSole));
            return lowest;
        }

        /// <summary>
        /// The mixers' input weights from the clip weights, every clip every time, normalized per
        /// mixer.  A humanoid mixer adds its inputs up as they are: two clips at weight 1 put the
        /// hips twice as high and every helper bone twice as far from its parent (a stale weight
        /// left by an instant switch did that from the second round on: g04 floated with spikes
        /// for shins), and weights that sum to less than 1 pull the pose towards the T-pose.
        /// </summary>
        void ApplyWeights()
        {
            float mocapSum = 0f, gameSum = 0f;
            for (int i = 0; i < weight.Length; i++)
            {
                if (clips[i].game)
                    gameSum += weight[i];
                else
                    mocapSum += weight[i];
            }
            for (int i = 0; i < weight.Length; i++)
            {
                float sum = clips[i].game ? gameSum : mocapSum;
                Mixer(i).SetInputWeight(slot[i], sum > 1e-5f ? weight[i] / sum : 0f);
            }
            layers.SetInputWeight(2, gameWeight);
        }

        /// <summary>The clips the mixers actually blend (input weight), for checks.</summary>
        public string MixerWeights()
        {
            if (!graph.IsValid())
                return "no graph";
            var parts = new List<string>();
            for (int i = 0; i < clips.Count; i++)
            {
                float w = Mixer(i).GetInputWeight(slot[i]);
                if (w > 0f || weight[i] > 0f || target[i] > 0f)
                    parts.Add($"{clips[i].name} {w:F2} (weight {weight[i]:F2} target {target[i]:F2})");
            }
            return $"game layer {layers.GetInputWeight(2):F2}: " + string.Join(", ", parts);
        }

        // ---- skills: the game's timeline in step with the model

        bool lingering;
        float lingerTime;

        /// <summary>
        /// The skill is over for the body, but its timeline plays on to its end by itself (its
        /// effects fade out instead of vanishing); a new skill or ShowTimeline(null) stops it.
        /// </summary>
        public void LingerTimeline(float time)
        {
            if (activeDirector == null)
                return;
            lingering = true;
            lingerTime = time;
        }

        void StepLinger(float dt)
        {
            if (!lingering || activeDirector == null)
                return;
            lingerTime += dt;
            if (lingerTime >= activeDirector.duration)
            {
                activeDirector.Stop();
                activeDirector = null;
                lingering = false;
                return;
            }
            activeDirector.time = lingerTime;
            activeDirector.Evaluate();
        }

        /// <summary>Start (or keep showing) a skill's timeline at a time; null stops it.</summary>
        public void ShowTimeline(string action, float time)
        {
            lingering = false;
            var director = action == null ? null : directors.FirstOrDefault(d => d.action == action)?.director;
            if (director != activeDirector)
            {
                if (activeDirector != null)
                    activeDirector.Stop();
                activeDirector = director;
                if (director != null)
                    director.RebuildGraph();
            }
            if (director == null)
                return;
            director.time = Mathf.Clamp(time, 0f, (float)director.duration);
            director.Evaluate();
        }

        /// <summary>
        /// The bone that carries each weapon's own bones - the lowest one above all the bones of the weapon's
        /// mesh that no body mesh uses (g04's fan: All_Fan_ctrl, the fan's pivot).  Never the skeleton itself:
        /// a weapon skinned straight to the hand is left at its size.  Found once (Init runs again on F3 / F4
        /// and must not take a scaled bone for the original).
        /// </summary>
        void FindWeaponRoots()
        {
            weaponRoots = WeaponRoots(animator, weaponRenderers);
            weaponRootScale = weaponRoots.Select(t => t.localScale).ToArray();
        }

        /// <summary>The bones that carry the given weapons (see FindWeaponRoots), for tools too.</summary>
        public static Transform[] WeaponRoots(Animator animator, Renderer[] weaponRenderers)
        {
            var weapons = new HashSet<Renderer>(weaponRenderers ?? new Renderer[0]);
            var body = new HashSet<Transform>();
            foreach (var r in animator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (!weapons.Contains(r))
                    foreach (var b in r.bones)
                        if (b != null)
                            body.Add(b);
            var hipsBone = animator.GetBoneTransform(HumanBodyBones.Hips);
            var roots = new List<Transform>();
            foreach (var r in weapons)
            {
                if (r == null)
                    continue;
                Transform top = r.transform;
                if (r is SkinnedMeshRenderer s)
                {
                    var own = s.bones.Where(b => b != null && !body.Contains(b)).ToList();
                    top = null;
                    if (own.Count > 0)
                        for (var t = own[0]; t != null && top == null; t = t.parent)
                            if (own.All(b => b.IsChildOf(t)))
                                top = t;
                }
                if (top == null || body.Contains(top) || top == animator.transform || (hipsBone != null && hipsBone.IsChildOf(top)))
                    continue;
                if (!roots.Contains(top))
                    roots.Add(top);
            }
            return roots.ToArray();
        }

        /// <summary>
        /// Her weapons shown (her game's own clips) or not.  While she holds them (Holds) the ones in her grips show anyway,
        /// each until the clothes burst takes its stage off (b10's pistols go with their holsters); Tick asks again each step.
        /// </summary>
        public void ShowWeapons(bool show)
        {
            weaponsShown = show;
            if (weaponRenderers == null)
                return;
            bool holds = Holds;
            int dropped = burst != null ? burst.Dropped : 0;
            foreach (var r in weaponRenderers)
            {
                if (r == null)
                    continue;
                bool on = show;
                if (!on && holds && Grips.Carrying(r) is RoeGrips.Grip g)
                    on = g.burstStage <= 0 || dropped < g.burstStage;
                if (r.enabled != on)
                    r.enabled = on;
            }
        }

        public Transform Bone(HumanBodyBones bone) => animator.GetBoneTransform(bone);

        /// <summary>A blade in one of her hands (RoeBlade: Fiona's longsword; a08's greatsword while she holds it - Holds), or null.</summary>
        public RoeBlade Blade => (blade != null ? blade : blade = animator != null ? animator.GetComponentInChildren<RoeBlade>(true) : null) is RoeBlade b &&
                                 (Grips == null || Grips.grips.Length == 0 || Holds) ? b : null;
        [NonSerialized] RoeBlade blade;

        public void Sound(string name, float volume = 1f)
        {
            if (string.IsNullOrEmpty(name) || audioSource == null || !Application.isPlaying)
                return;
            var s = sounds.FirstOrDefault(x => x.name == name);
            if (s?.clip != null)
                audioSource.PlayOneShot(s.clip, volume);
        }

        /// <summary>The anchor the game uses for effects on this character ("center", "hurt", ...).</summary>
        public Transform Anchor(string name)
        {
            var node = sheet?.anchors.FirstOrDefault(a => a.name == name)?.node;
            var t = node == null || string.IsNullOrEmpty(node.path) || unitRoot == null ? null : unitRoot.Find(node.path);
            return t != null ? t : transform;
        }
    }
}
