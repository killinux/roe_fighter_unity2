using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Playables;

namespace RoeFighter.Fight
{
    /// <summary>
    /// The fight: a side-view fighting game in the manner of The King of Fighters on one of the game's stages.
    /// Two fighters on one line (axis), the camera square to it; teams of three (teamSize) that fight one
    /// member at a time, the winner staying on with what health she has left, or a single match best of
    /// three rounds (teamSize 1).  60 seconds a round.
    /// Runs at a fixed 60 steps per second.  Each step: inputs (keyboard, pad or CPU), the fighters'
    /// state machines, their poses, then the hit checks on the posed bodies (the striking bone
    /// against the opponent's body capsule, shorter when she crouches), the throws, the special
    /// moves' hits, the effects, the round rules, the camera.  Step() can also be driven from the
    /// editor to film a CPU match.
    /// </summary>
    public class FightGame : MonoBehaviour
    {
        [Serializable]
        public class NamedPrefab
        {
            public string name;
            public GameObject prefab;
        }

        public FighterRig[] rigs = new FighterRig[2];     // the two fighting now (roster[pick], set by Setup / the select screen)
        public FighterRig[] roster = new FighterRig[0];   // everyone the scene holds; empty: rigs as built
        public int[] pick = { 0, 1 };                      // roster entries of the two on the stage (the select screen's highlighted cards)
        public bool selectFirst = true;                    // play mode, more than two in the roster: the select screen first
        public bool[] cpu = { false, true };
        public List<Move> moves = new List<Move>();
        public List<Special> specials = new List<Special>();
        public MotionPack basics;                          // crouching, jumping, blocking, the reactions, the crouching / jumping / CD strikes, the throw
        public List<NamedPrefab> effects = new List<NamedPrefab>();
        public string hitEffect;                     // a small hit spark for strikes (and armour coming off)
        /// <summary>
        /// The strikes' hit effects by weight (user 10-05: "普通攻击的效果不用luf的特效了，看有没有其他碰撞的特效"): UFE 2's hit
        /// particles when the project has them (set by the scene build, RoeHitFx), else hitEffect.  Heavy: a knockdown or
        /// hitHeavyDamage and more; medium: hitMediumDamage and more; light: the rest; block: a blocked strike.  Each at its own
        /// size (UFE made them about a metre across: at 0.45 / 0.55 / 0.7 the white flash still covered both fighters).
        /// </summary>
        public string hitLight, hitMedium, hitHeavy, hitBlock;
        public float hitLightScale = 0.35f, hitMediumScale = 0.45f, hitHeavyScale = 0.55f, hitBlockScale = 0.4f;
        public int hitMediumDamage = 55, hitHeavyDamage = 80;
        public Vector3 centre;
        public Vector3 axis = Vector3.right;         // the fight line: fighter 1 stands at -, fighter 2 at +; screen right from the camera's side
        public Vector3 normal;                       // from the fight line towards the camera (zero: worked out from the axis)
        public float arenaRadius = 7f;
        public float startGap = 3.2f;
        public float maxSeparation = 6.8f;           // the two never get farther apart than this (the picture holds both)
        public Camera cam;
        public float camFov = 30f;                   // the camera's view (0: the camera's own)
        public float camMinDistance = 5.2f, camMaxDistance = 8.8f;   // from the fight line, by how far apart the two are
        public float camHeight = 0.9f;               // m above the floor the camera looks at (half a metre of floor under the line stays in view)
        public float camLift = 0.45f;                // m the look-at rises per metre of the higher fighter's height (jumps)
        public float camRise = 0.3f;                 // m the camera stands above the point it looks at
        public float camEdge = 0.6f;                 // the picture may reach this far past the edge of the stage
        public FightHud hud;
        public int roundsToWin = 2;                  // single matches (teamSize 1)
        public int teamSize = 3;                     // 3: KOF-style teams, one member at a time; 1: a single match
        public float roundSeconds = 60f;
        public string voiceLanguage = "2";           // the game's voice language: 2 Japanese, 1 English
        public int seed = 1;
        public AudioClip music;                      // the stage's battle music, looped
        public float musicVolume = 0.3f;
        AudioSource musicSource;
        public List<MotionPack> motionPacks = new List<MotionPack>();   // basic moves that can be swapped (F3)
        public int motionPack;
        float noticeTime;

        /// <summary>The teams: roster entries in fighting order; current = who of each is fighting.</summary>
        public readonly List<int>[] team = { new List<int>(), new List<int>() };
        public readonly int[] current = new int[2];
        public bool TeamMode => teamSize > 1;

        /// <summary>Half the length of the fight line the fighters may use (short of the stage's edge: the ring's gates and fences
        /// stand there, and a camera looking at someone against them films through them).</summary>
        public float HalfWidth => arenaRadius - edgeMargin;
        public float edgeMargin = 1.6f;
        public Vector3 Normal => normal.sqrMagnitude > 0.5f ? normal.normalized : Vector3.Cross(axis, Vector3.up).normalized;

        /// <summary>Lines under the timer, one per setting: the mode, the motion pack, the cloth, the skirt, the clothes burst -
        /// for a few seconds after a switch or a new match.</summary>
        public string Notice => noticeTime <= 0f ? "" :
            string.Join("\n", new[] { $"MODE: {(TeamMode ? $"{teamSize} vs {teamSize} team battle" : $"1 vs 1, best of {roundsToWin * 2 - 1}")}",
                                       Pack != null ? $"MOTIONS: {Pack.title}" : null, $"CLOTH: {RoeClothBackends.Find(FighterRig.ClothBackend).title}",
                                       RoeClothBackends.Find(FighterRig.ClothBackend).skinnedSkirt ? $"SKIRT: {(RoeSkirtRig.Drape > 0f ? "hangs" : "fitted")}" : null,
                                       rigs.Any(r => r != null && r.burst != null) ? $"CLOTHES BURST: {(RoeClothesBurst.Enabled ? "on" : "off")}" : null }.Where(s => s != null));

        public MotionPack Pack => motionPacks.Count > 0 ? motionPacks[Mathf.Clamp(motionPack, 0, motionPacks.Count - 1)] : null;

        public const float Dt = 1f / 60f;
        public readonly Fighter[] f = new Fighter[2];
        public int frame;
        public float timer;
        public int round;
        public readonly int[] wins = new int[2];       // single: rounds won; teams: opponents beaten
        public string message = "";
        public enum Phase { Select, Intro, Fight, RoundOver, MatchOver }
        public Phase phase = Phase.Intro;
        public readonly bool[] picked = new bool[2];   // the select screen: this side's team is complete
        public int choosing;                           // the select screen: the side whose card the keys move (one human: hers, then the CPU's)
        readonly Dictionary<int, FighterRig> twins = new Dictionary<int, FighterRig>();   // a second copy of a fighter, for a mirror match
        float phaseTime, hitStop, slowMotion;
        float accumulator;
        readonly FightAI[] ai = new FightAI[2];

        // what the editor needs to make a video: sounds with their frame numbers
        [Serializable]
        public struct SoundEvent
        {
            public int frame;
            public string fighter, name;
            public float volume;
        }

        public readonly List<SoundEvent> soundLog = new List<SoundEvent>();

        class LiveEffect
        {
            public GameObject instance;
            public PlayableDirector director;
            public ParticleSystem[] roots;
            public float time, life, simulated = -1f;
        }

        readonly List<LiveEffect> live = new List<LiveEffect>();

        class Scheduled
        {
            public float at;
            public Action run;
        }

        readonly List<Scheduled> schedule = new List<Scheduled>();
        float clock;
        Vector3 camPos, camTarget;
        bool camSnap = true;
        float shakeTime, shakeAmplitude, shakeDuration;

        void Start()
        {
            // the player: ROEFighter2.exe -roeBurst 0 starts with the clothes burst off (F6 switches it);
            // -roeP1 a08,g04,b10 -roeP2 kas,mai,raven picks the teams (-roeTeam 1: a single match, -roeP1 b10 -roeP2 g05);
            // -roeSelect 0 goes straight into the match; -roeCloth magica starts with that physics setup (F4 goes on from it)
            var args = Environment.GetCommandLineArgs();
            string Arg(string name)
            {
                int at = Array.IndexOf(args, name);
                return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
            }
            if (Arg("-roeBurst") != null)
                RoeClothesBurst.Enabled = Arg("-roeBurst") != "0";
            if (Arg("-roeCloth") != null)
                FighterRig.ClothBackend = RoeClothBackends.Find(Arg("-roeCloth")).name;
            // -roeSkirtSwing "g04:restore=0.1,...": a fighter's skirt swinging like her game's keys, tuned otherwise
            if (Arg("-roeSkirtSwing") != null)
                RoeBoneCloth.SwingFromArg(Arg("-roeSkirtSwing"));
            if (Arg("-roeTeam") != null && int.TryParse(Arg("-roeTeam"), out int size))
                teamSize = Mathf.Clamp(size, 1, 5);
            PickIds(Arg("-roeP1"), Arg("-roeP2"));
            if (Arg("-roeSelect") != null)
                selectFirst = Arg("-roeSelect") != "0";
            if (Application.isPlaying && selectFirst && roster != null && roster.Length > 2)
                EnterSelect();
            else
                Setup();
        }

        /// <summary>Picks by character id, a comma-separated team each (null or unknown ids: that side's picks stay).</summary>
        public void PickIds(string p1, string p2)
        {
            if (roster == null)
                return;
            for (int i = 0; i < 2; i++)
            {
                string ids = i == 0 ? p1 : p2;
                if (string.IsNullOrEmpty(ids))
                    continue;
                var found = ids.Split(',').Select(id => Array.FindIndex(roster, r => r != null && r.id == id.Trim())).Where(k => k >= 0).Distinct().ToList();
                if (found.Count == 0)
                    continue;
                team[i].Clear();
                team[i].AddRange(found);
                pick[i] = found[0];
            }
        }

        /// <summary>Every team filled to teamSize: the highlighted card first, then the next of the roster not yet in it.</summary>
        void FillTeams()
        {
            int n = roster != null ? roster.Length : 0;
            for (int i = 0; i < 2; i++)
            {
                team[i].RemoveAll(k => k < 0 || k >= n);
                if (team[i].Count > teamSize)
                    team[i].RemoveRange(teamSize, team[i].Count - teamSize);
                if (team[i].Count == 0)
                    team[i].Add(Mathf.Clamp(pick[i], 0, Mathf.Max(0, n - 1)));
                int next = team[i][team[i].Count - 1];
                while (team[i].Count < teamSize && team[i].Count < n)
                {
                    next = (next + 1) % n;
                    if (!team[i].Contains(next))
                        team[i].Add(next);
                }
            }
        }

        /// <summary>The two fighters of the picks, the others put away; the same one on both sides gets a copy.</summary>
        void ApplyPicks()
        {
            if (roster == null || roster.Length == 0)
                return;
            for (int i = 0; i < 2; i++)
                pick[i] = Mathf.Clamp(pick[i], 0, roster.Length - 1);
            var a = roster[pick[0]];
            var b = pick[1] == pick[0] ? Twin(pick[1]) : roster[pick[1]];
            rigs = new[] { a, b };
            foreach (var r in roster.Concat(twins.Values))
                if (r != null && r.gameObject.activeSelf != (r == a || r == b))
                    r.gameObject.SetActive(r == a || r == b);
        }

        /// <summary>A second copy of a roster fighter (a mirror match), made the first time it is needed.</summary>
        FighterRig Twin(int k)
        {
            if (!twins.TryGetValue(k, out var twin) || twin == null)
            {
                var go = Instantiate(roster[k].gameObject, roster[k].transform.parent);
                go.name = roster[k].gameObject.name + " (2)";
                go.hideFlags = HideFlags.DontSave;
                twins[k] = twin = go.GetComponent<FighterRig>();
            }
            return twin;
        }

        /// <summary>A new match with the picked teams.</summary>
        public void Setup()
        {
            StartMatch();
        }

        /// <summary>The two of pick[] ready to fight: the motion pack's moves, their rigs, whole outfits.</summary>
        void Prepare()
        {
            Application.targetFrameRate = 60;
            ApplyPicks();
            // the basic moves of the chosen motion pack, on both fighters
            var pack = Pack;
            if (pack != null)
            {
                moves = pack.CopyStrikes();
                foreach (var m in moves)
                    if (string.IsNullOrEmpty(m.hitSound))
                        m.hitSound = "hit";
            }
            noticeTime = 4f;
            for (int i = 0; i < 2; i++)
                PrepareSide(i);
            f[0].foe = f[1];
            f[1].foe = f[0];
        }

        void PrepareSide(int i)
        {
            if (Pack != null)
                rigs[i].UseMotions(Pack);
            rigs[i].AddMotions(basics);
            rigs[i].Init();
            f[i] = new Fighter { rig = rigs[i], game = this, index = i };
            if (f[1 - i] != null)
            {
                f[i].foe = f[1 - i];
                f[1 - i].foe = f[i];
            }
            ai[i] = new FightAI(seed * 7919 + i * 104729 + round * 31);
            // her own strikes (g04: Mai Shiranui's) or the pack's; the basics for everything else
            var own = rigs[i].strikePack;
            f[i].moves = own != null && own.strikes.Count > 0 ? own.CopyStrikes() : moves;
            foreach (var m in f[i].moves)
                if (string.IsNullOrEmpty(m.hitSound))
                    m.hitSound = "hit";
            f[i].basics = basics != null ? basics.CopyStrikes() : new List<Move>();
            foreach (var m in f[i].basics)
                if (string.IsNullOrEmpty(m.hitSound))
                    m.hitSound = "hit";
            // a new fighter: the outfit is whole
            var burst = rigs[i].burst;
            if (burst != null)
            {
                burst.Restore(seed * 7919 + i * 104729 + 31);
                var who = f[i];
                burst.onPieceOff = (at, size, cloth) => PieceOff(who, at, size, cloth);
            }
        }

        // ---- the select screen: who fights (KOF-like: a card per fighter, the two highlighted stand on the stage; a team is picked in order)

        /// <summary>
        /// The select screen as ROE's own menus show a heroine (user 10-06: "选人环节也做个动画，ROE里面标准的动画"): the fighter of
        /// a card turns from facing the other this far towards the camera (0..1) and loops her outfit's showcase idle
        /// (idle_02); a confirmed pick plays her showcase reaction - the game's idle02_react01, react_01 with its "yes" line
        /// (FighterRig.showcase) - and the match starts once both teams are complete and the last reactions have played out
        /// (at most selectWait s after the last pick).
        /// </summary>
        public float selectFaceCamera = 0.8f;
        public string selectReaction = "idle02_react01";
        public float selectWait = 3.5f;
        readonly float[] reactUntil = new float[2];      // phaseTime at which a pick's reaction is over (0: none)
        readonly bool[] showingReact = new bool[2];
        readonly int[] shownPicks = new int[2];          // how many members each side had confirmed at the last frame

        /// <summary>To the select screen; the cards start on the current picks, the teams are chosen afresh.</summary>
        public void EnterSelect()
        {
            for (int i = 0; i < 2; i++)
            {
                if (team[i].Count > 0)
                    pick[i] = team[i][0];
                team[i].Clear();
            }
            Prepare();
            phase = Phase.Select;
            phaseTime = 0f;
            message = "";
            picked[0] = picked[1] = false;
            shownPicks[0] = shownPicks[1] = 0;
            choosing = cpu[0] && !cpu[1] ? 1 : 0;
            StandForSelect();
            PlayMusic();
            if (hud != null)
                hud.Build(this);
            SnapCamera();
            TurnForSelect(true);
        }

        /// <summary>The fighters of the cards on their marks, in their showcase idle (one side: only that one).</summary>
        void StandForSelect(int only = -1)
        {
            var a = centre - axis * (startGap * 0.5f);
            var b = centre + axis * (startGap * 0.5f);
            for (int i = 0; i < 2; i++)
            {
                if (only >= 0 && i != only)
                    continue;
                f[i].Reset(i == 0 ? a : b, i == 0 ? 1 : -1);
                f[i].Revive();
                reactUntil[i] = 0f;
                showingReact[i] = false;
                ShowIdle(i, 0.2f);
            }
            TurnForSelect(true);
        }

        void ShowIdle(int i, float fade) => f[i].rig.Play(f[i].rig.Has("idle_02") ? "idle_02" : "guard", 1f, fade);

        /// <summary>One step of the select screen (in Step, so a filmed select screen has it too): picks shown, turned to the camera.</summary>
        void SelectStep(float dt)
        {
            ShowPicks();
            TurnForSelect(false, dt);
        }

        /// <summary>Each fighter of the select screen turned from facing the other towards the camera (selectFaceCamera).</summary>
        void TurnForSelect(bool snap, float dt = 0f)
        {
            if (f[0] == null || f[1] == null)
                return;
            for (int i = 0; i < 2; i++)
            {
                var me = f[i];
                float target = Yaw(me.foe != null ? me.foe.pos - me.pos : f[1 - i].pos - me.pos);
                if (cam != null)
                {
                    var toCam = cam.transform.position - me.pos;
                    toCam.y = 0f;
                    if (toCam.sqrMagnitude > 1e-4f)
                        target = Mathf.LerpAngle(target, Yaw(toCam), selectFaceCamera);
                }
                me.yaw = snap ? target : Mathf.MoveTowardsAngle(me.yaw, target, 240f * dt);
                me.Place();
            }
        }

        /// <summary>A confirmed pick: her showcase reaction (the game's timeline: its react clip, its voice line).</summary>
        void ShowPicked(int i)
        {
            var rig = f[i].rig;
            var show = rig.showcase.FirstOrDefault(s => s.timeline == selectReaction) ?? rig.showcase.FirstOrDefault();
            if (show == null || !rig.Has(show.clip))
            {
                reactUntil[i] = 0f;
                return;
            }
            rig.Play(show.clip, 1f, 0.15f);
            showingReact[i] = true;
            reactUntil[i] = phaseTime + rig.Length(show.clip);
            if (!string.IsNullOrEmpty(show.sound))
            {
                var who = f[i];
                At(clock + show.voiceAt, () =>
                {
                    if (phase == Phase.Select && f[i] == who)
                        PlaySound(who, show.sound, 1f);
                });
            }
        }

        /// <summary>A member confirmed since the last frame: her reaction; one taken back: the idle; a reaction played out: the idle.</summary>
        void ShowPicks()
        {
            for (int i = 0; i < 2; i++)
            {
                int count = team[i].Count;
                if (count > shownPicks[i] && team[i][count - 1] == pick[i])
                    ShowPicked(i);
                else if (count < shownPicks[i])
                {
                    reactUntil[i] = 0f;
                    showingReact[i] = false;
                    ShowIdle(i, 0.25f);
                }
                else if (showingReact[i] && phaseTime >= reactUntil[i])
                {
                    showingReact[i] = false;
                    ShowIdle(i, 0.3f);
                }
                shownPicks[i] = count;
            }
        }

        /// <summary>Keys on the select screen: left / right move a card, the first attack button adds the card to the team, the second takes the last one back.</summary>
        void UpdateSelect()
        {
            int[] before = { pick[0], pick[1] };
            bool human0 = !cpu[0], human1 = !cpu[1];
            if (human0 && human1)
            {
                SelectKeys(0, 0);
                SelectKeys(1, 1);
            }
            else if (human0 || human1)
                SelectKeys(human0 ? 0 : 1, choosing);
            else if (phaseTime > 2f && !picked[0])
            {
                // the computer on both sides: random teams
                var rng = new System.Random(seed * 31 + frame);
                for (int i = 0; i < 2; i++)
                {
                    team[i].Clear();
                    while (team[i].Count < Mathf.Min(teamSize, roster.Length))
                    {
                        int k = rng.Next(roster.Length);
                        if (!team[i].Contains(k))
                            team[i].Add(k);
                    }
                    pick[i] = team[i][team[i].Count - 1];
                    picked[i] = true;
                }
            }
            PicksChanged(before);
            if (picked[0] && picked[1])
            {
                if (selectDone < 0f)
                    selectDone = phaseTime;
                // her reaction plays out first
                float reacted = Mathf.Min(Mathf.Max(reactUntil[0], reactUntil[1]), selectDone + selectWait);
                if (phaseTime - selectDone > 0.6f && phaseTime >= reacted)
                {
                    selectDone = -1f;
                    StartMatch();
                }
            }
            else
                selectDone = -1f;
        }

        /// <summary>After a card moved on the select screen: the new fighter comes out on the stage.</summary>
        public void PicksChanged(int[] before)
        {
            var changed = new bool[2];
            for (int i = 0; i < 2; i++)
                if (pick[i] != before[i])
                {
                    changed[i] = true;
                    ApplyPicks();
                    PrepareSide(i);
                    if (pick[1 - i] == before[1 - i] && rigs[1 - i] != f[1 - i].rig)
                    {
                        PrepareSide(1 - i);      // a twin came or went on the other side
                        changed[1 - i] = true;
                    }
                }
            if (!changed[0] && !changed[1])
                return;
            f[0].foe = f[1];
            f[1].foe = f[0];
            // the one who came out stands in her showcase idle; the other goes on with what she was doing
            for (int i = 0; i < 2; i++)
                if (changed[i])
                    StandForSelect(i);
            if (hud != null)
                hud.Build(this);
        }

        /// <summary>The select screen is done (both teams complete): the match starts.</summary>
        public void BeginMatch() => StartMatch();

        float selectDone = -1f;
        readonly float[] lastAxis = new float[2];

        void SelectKeys(int keys, int side)
        {
            int move;
            bool ok, back;
            if (keys == 0)
            {
                float h = Input.GetAxisRaw("Horizontal");
                int axisEdge = Mathf.Abs(h) > 0.5f && Mathf.Abs(lastAxis[0]) <= 0.5f && !Input.GetKey(KeyCode.LeftArrow) && !Input.GetKey(KeyCode.RightArrow)
                    && !Input.GetKey(KeyCode.A) && !Input.GetKey(KeyCode.D) ? (h > 0f ? 1 : -1) : 0;
                lastAxis[0] = h;
                move = Input.GetKeyDown(KeyCode.A) ? -1 : Input.GetKeyDown(KeyCode.D) ? 1 : axisEdge;
                ok = Input.GetKeyDown(KeyCode.J) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.JoystickButton0);
                back = Input.GetKeyDown(KeyCode.K) || Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.JoystickButton1);
            }
            else
            {
                move = Input.GetKeyDown(KeyCode.LeftArrow) ? -1 : Input.GetKeyDown(KeyCode.RightArrow) ? 1 : 0;
                ok = Input.GetKeyDown(KeyCode.Keypad1) || Input.GetKeyDown(KeyCode.KeypadEnter);
                back = Input.GetKeyDown(KeyCode.Keypad2);
            }
            bool alone = cpu[0] != cpu[1];      // one human: she picks her team, then the computer's
            if (back)
            {
                if (team[side].Count > 0)
                {
                    team[side].RemoveAt(team[side].Count - 1);
                    picked[side] = false;
                }
                else if (alone && picked[1 - side])
                {
                    picked[1 - side] = false;
                    choosing = 1 - side;
                    if (team[1 - side].Count > 0)
                        team[1 - side].RemoveAt(team[1 - side].Count - 1);
                }
                return;
            }
            if (picked[side])
                return;
            if (move != 0 && roster.Length > 0)
                pick[side] = (pick[side] + move + roster.Length) % roster.Length;
            if (ok && !team[side].Contains(pick[side]))
            {
                team[side].Add(pick[side]);
                if (team[side].Count >= Mathf.Min(teamSize, roster.Length))
                {
                    picked[side] = true;
                    if (alone && !picked[1 - side])
                        choosing = 1 - side;
                }
            }
        }

        void StartMatch()
        {
            FillTeams();
            wins[0] = wins[1] = 0;
            round = 0;
            frame = 0;
            clock = 0f;
            soundLog.Clear();
            current[0] = current[1] = 0;
            pick[0] = team[0][0];
            pick[1] = team[1][0];
            Prepare();
            f[0].Revive();
            f[1].Revive();
            PlayMusic();
            if (hud != null)
                hud.Build(this);
            StartRound();
            SnapCamera();
        }

        /// <summary>The stage's music from the start (it goes on through the select screen and into the match).</summary>
        void PlayMusic()
        {
            if (music == null || !Application.isPlaying)
                return;
            if (musicSource == null)
            {
                musicSource = gameObject.AddComponent<AudioSource>();
                musicSource.loop = true;
                musicSource.playOnAwake = false;
                musicSource.spatialBlend = 0f;
            }
            if (musicSource.isPlaying && musicSource.clip == music)
                return;
            musicSource.clip = music;
            musicSource.volume = musicVolume;
            musicSource.Play();
        }

        void StartRound()
        {
            round++;
            timer = roundSeconds;
            var a = centre - axis * (startGap * 0.5f);
            var b = centre + axis * (startGap * 0.5f);
            f[0].Reset(a, 1);
            f[1].Reset(b, -1);
            foreach (var x in live)
                if (x.instance != null)
                    DestroyAny(x.instance);
            live.Clear();
            schedule.Clear();
            // what came off stays off for the whole match; the pieces still lying about go
            foreach (var rig in rigs)
                if (rig.burst != null)
                    rig.burst.ClearDebris();
            phase = Phase.Intro;
            phaseTime = 0f;
            message = TeamMode ? $"ROUND {round}\n{f[0].rig.displayName}  vs  {f[1].rig.displayName}" : $"ROUND {round}";
            hitStop = 0f;
            slowMotion = 0f;
            if (hud != null)
                hud.Build(this);
        }

        static float Yaw(Vector3 v) => Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;

        void Update()
        {
            if (!Application.isPlaying || f[0] == null)
                return;
            if (Input.GetKeyDown(KeyCode.F1))
                cpu[0] = !cpu[0];
            if (Input.GetKeyDown(KeyCode.F2))
                cpu[1] = !cpu[1];
            if ((Input.GetKeyDown(KeyCode.F1) || Input.GetKeyDown(KeyCode.F2)) && phase == Phase.Select)
                choosing = cpu[0] && !cpu[1] ? 1 : picked[0] && cpu[1] ? 1 : 0;
            if (Input.GetKeyDown(KeyCode.F3) && motionPacks.Count > 1)
            {
                // the next motion pack; the match starts over with it (on the select screen: the two take it up)
                motionPack = (motionPack + 1) % motionPacks.Count;
                Restart();
            }
            if (Input.GetKeyDown(KeyCode.F4))
            {
                // the next cloth backend (Magica-style bone cloth, the 10-02 one, none); the match starts over
                FighterRig.ClothBackend = RoeClothBackends.Next(FighterRig.ClothBackend);
                Restart();
            }
            if (Input.GetKeyDown(KeyCode.F7) && roster != null && roster.Length > 0 && phase != Phase.Select)
                EnterSelect();
            if (Input.GetKeyDown(KeyCode.F5))
            {
                // the skirt's animation pose: hanging on the body (RoeSkirtRig.Drape 1) or the fitted pose
                // (0, as of 52f11de); takes effect at once
                RoeSkirtRig.Drape = RoeSkirtRig.Drape > 0f ? 0f : 1f;
                noticeTime = 4f;
            }
            if (Input.GetKeyDown(KeyCode.F8))
            {
                // team battle (3 vs 3) or a single match; back to the select screen
                teamSize = TeamMode ? 1 : 3;
                noticeTime = 4f;
                if (roster != null && roster.Length > 2)
                    EnterSelect();
                else
                    Setup();
            }
            if (Input.GetKeyDown(KeyCode.F6))
            {
                // clothes burst on / off; off puts every outfit back on at once
                RoeClothesBurst.Enabled = !RoeClothesBurst.Enabled;
                if (!RoeClothesBurst.Enabled)
                    for (int i = 0; i < 2; i++)
                        if (rigs[i].burst != null)
                            rigs[i].burst.Restore(seed * 7919 + i * 104729 + 31 + frame);
                noticeTime = 4f;
            }
            // after the match: back to the select screen
            if (phase == Phase.MatchOver && phaseTime > 2f && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.JoystickButton7)))
            {
                if (roster != null && roster.Length > 2)
                    EnterSelect();
                else
                    Setup();
            }
            else if (phase == Phase.Select)
                UpdateSelect();
            accumulator += Mathf.Min(Time.deltaTime, 0.1f);
            var pending = new FighterInput[2];
            for (int i = 0; i < 2; i++)
                pending[i] = cpu[i] ? default : PlayerInput.Read(i);
            bool first = true;
            while (accumulator >= Dt)
            {
                // presses count once, in the first step of this frame
                Step(first ? pending : new[] { Held(pending[0]), Held(pending[1]) });
                first = false;
                accumulator -= Dt;
            }
            UpdateCamera(Time.deltaTime);
            if (hud != null)
                hud.Refresh(this);
        }

        static FighterInput Held(FighterInput i) => new FighterInput { x = i.x, y = i.y };

        void Restart()
        {
            if (phase == Phase.Select)
            {
                Prepare();
                StandForSelect();
                noticeTime = 4f;
            }
            else
                Setup();
        }

        /// <summary>One simulation step (1/60 s).  Human inputs in screen terms; CPU fighters decide here.</summary>
        public void Step(FighterInput[] inputs)
        {
            frame++;
            phaseTime += Dt;
            noticeTime -= Dt;
            float dt = Dt * (slowMotion > 0f ? 0.35f : 1f);
            if (slowMotion > 0f)
                slowMotion -= Dt;
            clock += dt;
            RunSchedule();

            if (hitStop > 0f)
            {
                hitStop -= Dt;
                foreach (var x in f)
                    x.rig.Tick(0f);
                StepEffects(0f);
                return;
            }

            switch (phase)
            {
                case Phase.Intro:
                    if (phaseTime > 1.2f)
                        message = "FIGHT!";
                    if (phaseTime > 2.0f)
                    {
                        message = "";
                        phase = Phase.Fight;
                        phaseTime = 0f;
                        foreach (var x in f)
                            x.Go();
                    }
                    break;
                case Phase.Fight:
                    timer = Mathf.Max(0f, timer - dt);
                    break;
                case Phase.Select:
                    SelectStep(Dt);
                    break;
            }

            var camRight = cam != null ? Flat(cam.transform.right) : axis;
            for (int i = 0; i < 2; i++)
            {
                var me = f[i];
                var input = phase == Phase.Fight
                    ? (cpu[i] ? ai[i].Decide(me, dt, camRight) : inputs != null && inputs.Length > i ? inputs[i] : default)
                    : default;
                // towards the opponent is forward, whichever way the camera looks at the line
                int screenSign = Vector3.Dot(axis * me.FoeSide, camRight) >= 0f ? 1 : -1;
                me.Step(dt, input.x * screenSign, input.y, input);
            }
            KeepApart();
            foreach (var x in f)
            {
                x.Place();
                x.rig.Tick(dt);
                x.AfterPose(dt);
            }
            if (phase == Phase.Fight)
                CheckStrikes();
            StepEffects(dt);
            Rules();
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-8f ? v.normalized : Vector3.forward;
        }

        /// <summary>Two bodies on the floor do not overlap: pushed apart along the line (one against the wall: the other gives way).</summary>
        void KeepApart()
        {
            bool skip = f.Any(x => x.Airborne || x.state == FightState.Roll || x.state == FightState.Special || x.state == FightState.Throw ||
                                   x.state == FightState.Thrown || x.state == FightState.Down || x.state == FightState.KO);
            if (skip)
                return;
            float d = f[1].U - f[0].U;
            float min = Fighter.BodyRadius * 2f;
            if (Mathf.Abs(d) >= min)
                return;
            float sign = d >= 0f ? 1f : -1f;
            float need = (min - Mathf.Abs(d)) * 0.5f;
            if (f[0].atWall && !f[1].atWall)
                f[1].U += sign * need * 2f;
            else if (f[1].atWall && !f[0].atWall)
                f[0].U -= sign * need * 2f;
            else
            {
                f[0].U -= sign * need;
                f[1].U += sign * need;
            }
        }

        void CheckStrikes()
        {
            for (int i = 0; i < 2; i++)
            {
                var atk = f[i];
                var vic = f[1 - i];
                // a throw: the grab
                if (atk.Grabbing)
                {
                    if (vic.Throwable && atk.Dist < Fighter.ThrowRange + 0.1f && vic.state != FightState.Special)
                    {
                        atk.Grab(vic);
                        PlaySound(atk, "hit", 0.7f);
                        hitStop = 0.05f;
                    }
                    continue;
                }
                var p = atk.ActiveHit();
                if (p == null || !vic.Hittable || vic.state == FightState.Special)
                    continue;
                var m = atk.move;
                if (vic.DistanceToBody(p.Value) > m.radius + Fighter.BodyRadius)
                    continue;
                atk.hitDone = true;
                atk.connected = true;
                if (vic.CanBlock && vic.Blocks(m))
                {
                    vic.BlockHit(m.blockstun, m.push * 0.5f, vic.downHeld > 0);
                    if (vic.atWall)
                        atk.PushBack(m.push * 0.5f);
                    atk.meter = Mathf.Min(Fighter.MeterMax, atk.meter + m.meterGain * 0.5f);
                    PlaySound(vic, "block", 0.8f);
                    StrikeSpark(p.Value, m, true);
                    hitStop = 0.05f;
                }
                else
                {
                    bool heavy = m.knockdown || m.launch || m.damage >= hitHeavyDamage;
                    vic.TakeHit(m.damage, m.hitstun, m.push, m.knockdown, m.launch, m.launchUp, m.launchBack, m.Low);
                    // the blowback attack (C+D) that lands takes the next stage of her outfit off (user 10-18 "爆衣效果也加上")
                    if (m.launch)
                        BurstClothes(vic, vic.lastHitDir);
                    if (vic.atWall && !vic.Airborne)
                        atk.PushBack(m.push * 0.6f);
                    atk.meter = Mathf.Min(Fighter.MeterMax, atk.meter + m.meterGain);
                    atk.comboHits++;
                    PlaySound(atk, m.hitSound, 1f);
                    StrikeSpark(p.Value, m, false);
                    hitStop = heavy ? 0.1f : 0.07f;
                    Shake(heavy ? 0.6f : 0.3f, 0.2f);
                }
            }
        }

        /// <summary>A throw's blows: the hold (she stays grabbed, never KO'd by it) and the toss (she flies and falls).</summary>
        public void ThrowHit(Fighter atk, Fighter vic, int damage, bool toss)
        {
            if (vic.state != FightState.Thrown)
                return;
            if (!toss)
            {
                vic.hp = Mathf.Max(1, vic.hp - damage);
                PlaySound(atk, "hit", 1f);
                hitStop = 0.05f;
                Shake(0.3f, 0.15f);
                return;
            }
            vic.Tossed(damage);
            BurstClothes(vic, vic.lastHitDir);      // thrown: a stage of her outfit comes off
            atk.meter = Mathf.Min(Fighter.MeterMax, atk.meter + 8f);
            atk.comboHits++;
            PlaySound(atk, "hit", 1f);
            Shake(0.5f, 0.2f);
            hitStop = 0.08f;
        }

        // ---- specials

        public void OnSpecialStart(Fighter atk, RoeSkillSheet.Action action)
        {
            float start = clock;
            foreach (var s in action.sounds.Where(s => !s.muted))
                At(start + s.start, () => PlaySound(atk, s.clip, s.volume));
            foreach (var v in action.voices.Where(v => !v.muted))
            {
                var clip = v.clips.FirstOrDefault(c => c.language == voiceLanguage)?.clip;
                if (clip != null)
                    At(start + v.start, () => PlaySound(atk, clip, 1f));
            }
            foreach (var s in action.shakes)
                At(start + s.time, () => Shake(s.amplitude, s.duration));
            foreach (var s in action.spawns)
            {
                var prefab = effects.FirstOrDefault(e => e.name == s.prefab)?.prefab;
                if (prefab == null)
                    continue;
                At(start + s.time, () =>
                {
                    if (atk.state != FightState.Special)
                        return;
                    var at = s.kind == "target" ? atk.foe : atk;
                    var anchor = at.rig.Anchor(s.kind == "target" ? s.name : "center");
                    Spawn(prefab, anchor.position, atk.rig.transform.rotation);
                });
            }
            if (action.name == "skill3")
                slowMotion = 0.25f;
        }

        /// <summary>A skill's blows reach this far beyond the opponent's own radius (the game's receiveRadius).</summary>
        public const float SpecialReachExtra = 1.0f + 0.9f;

        /// <summary>
        /// How far a skill's blows reach the victim: her radius + SpecialReachExtra (a skill that dashes up stops there), or
        /// for a skill that stays where she is, as far as the game puts its hit effects in front of her plus the victim's
        /// radius (user 10-11 "战斗技能都作为3个技能加入进来": Amano's super, Inase's thrown swords and Kart's pistols are
        /// ranged in the game and used to miss anyone more than 2.9 m away; RoeSkillSheet.Action.HitsAhead).
        /// </summary>
        public static float BlowReach(RoeSkillSheet.Action action, Fighter vic)
        {
            float radius = vic.rig.sheet != null ? vic.rig.sheet.receiveRadius : 1f;
            float reach = radius + SpecialReachExtra;
            if (action != null && !action.Dashes)
                reach = Mathf.Max(reach, action.HitsAhead + radius);
            return reach;
        }

        /// <summary>
        /// A damage moment of a skill.  The first one decides: the opponent blocks it (holding back),
        /// has stepped out of reach (a miss), or is hit.  A hit or a block then holds the opponent
        /// until the skill is over, the way the game stages it; the super's last hit knocks down.
        /// </summary>
        public void SpecialHit(Fighter atk, Special s, int damage, bool last)
        {
            var vic = atk.foe;
            if (atk.specialOutcome == 0)
            {
                // a ranged skill (Inase's thrown swords, Kart's pistols, Amano's super; FighterRig.rangedSkills) reaches her anywhere
                // along the line - but not in the air, where a jump takes her over it
                bool ranged = atk.rig.IsRanged(s.name);
                float reach = ranged ? Mathf.Max(s.range, maxSeparation + 1f) : BlowReach(atk.rig.sheet != null ? atk.rig.sheet.Find(s.action) : null, vic);
                bool over = ranged && vic.Airborne && vic.H > 0.5f;
                atk.specialOutcome = !vic.Hittable || over || Vector3.Distance(atk.pos, vic.pos) > reach ? 3 : vic.CanBlock ? 2 : 1;
            }
            if (atk.specialOutcome == 3 || !vic.Hittable)
                return;
            float hold = atk.SpecialRemaining + 0.15f;
            if (atk.specialOutcome == 2)
            {
                vic.hp = Mathf.Max(1, vic.hp - Mathf.RoundToInt(damage * 0.1f));
                vic.BlockHit(last ? 0.3f : hold, 0.07f, vic.downHeld > 0);
                hitStop = 0.04f;
                return;
            }
            bool knockdown = last && s.meterCost > 0f;
            if (vic.Airborne)
                vic.TakeHit(damage, last ? 0.6f : hold, 0.05f, false);
            else
                vic.TakeHit(damage, last ? 0.6f : hold, last ? 0.3f : 0.05f, knockdown);
            atk.meter = Mathf.Min(Fighter.MeterMax, atk.meter + (s.meterCost > 0f ? 0f : 4f));
            atk.comboHits++;
            hitStop = last ? 0.12f : 0.04f;
            // the super's finishing blow takes the next stage of her outfit off
            if (knockdown)
                BurstClothes(vic, vic.lastHitDir);
        }

        // ---- clothes burst (爆衣): a super that lands and a KO each take the next stage of the victim's outfit off

        /// <summary>The KO that ends the match takes everything left off the loser (user 10-03: "让爆衣都爆掉").</summary>
        public bool burstAllOnFinalKO = true;

        /// <summary>Take the next stage of a fighter's outfit off (all stages left: one after the other); false if burst is off or nothing is left.</summary>
        public bool BurstClothes(Fighter who, Vector3 dir, bool all = false)
        {
            var burst = who.rig.burst;
            if (burst == null)
                return false;
            bool any = false;
            while (burst.Drop(dir, who.pos.y))
            {
                any = true;
                if (!all)
                    break;
            }
            return any;
        }

        int burstSoundFrame = -1;

        /// <summary>A piece came off: armour gives a spark; one sound per fighter and step.</summary>
        void PieceOff(Fighter who, Vector3 at, float size, bool cloth)
        {
            if (!cloth)
                Spark(at, Mathf.Clamp(size * 2.5f, 0.5f, 1.2f));
            if (burstSoundFrame != frame * 2 + who.index)
            {
                burstSoundFrame = frame * 2 + who.index;
                PlaySound(who, "hit", 0.8f);
            }
        }

        void At(float time, Action run)
        {
            schedule.Add(new Scheduled { at = time, run = run });
        }

        void RunSchedule()
        {
            for (int i = 0; i < schedule.Count; i++)
            {
                if (schedule[i].at > clock)
                    continue;
                var s = schedule[i];
                schedule.RemoveAt(i--);
                s.run();
            }
        }

        // ---- effects (the game's prefabs, stepped like the game's timelines step them)

        /// <summary>A strike's hit effect: the block one, or by the strike's weight (hitLight ... hitBlock); else the old spark.</summary>
        void StrikeSpark(Vector3 at, Move m, bool blocked)
        {
            string name;
            float scale;
            if (blocked)
                (name, scale) = (hitBlock, hitBlockScale);
            else if (m.knockdown || m.launch || m.damage >= hitHeavyDamage)
                (name, scale) = (hitHeavy, hitHeavyScale);
            else if (m.damage >= hitMediumDamage)
                (name, scale) = (hitMedium, hitMediumScale);
            else
                (name, scale) = (hitLight, hitLightScale);
            if (string.IsNullOrEmpty(name) || effects.All(e => e.name != name))
                Spark(at, blocked ? 0.6f : 1f);
            else
                Spark(at, scale, name);
        }

        void Spark(Vector3 at, float scale, string name = null)
        {
            var prefab = effects.FirstOrDefault(e => e.name == (name ?? hitEffect))?.prefab;
            if (prefab == null)
                return;
            var e = Spawn(prefab, at, cam != null ? Quaternion.LookRotation(cam.transform.position - at) : Quaternion.identity);
            if (e == null)
                return;
            e.instance.transform.localScale *= scale;
            // a named spark is a plain particle effect (UFE's): every system takes the size of the whole
            if (name != null)
                foreach (var p in e.instance.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var main = p.main;
                    main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                }
        }

        LiveEffect Spawn(GameObject prefab, Vector3 at, Quaternion rotation)
        {
            var go = Instantiate(prefab, at, rotation);
            go.hideFlags = HideFlags.DontSave;
            var e = new LiveEffect { instance = go };
            e.director = go.GetComponentInChildren<PlayableDirector>(true);
            if (e.director != null && e.director.playableAsset != null)
            {
                e.director.playOnAwake = false;
                e.director.timeUpdateMode = DirectorUpdateMode.Manual;
                e.director.RebuildGraph();
                e.life = (float)e.director.playableAsset.duration;
            }
            else
            {
                e.director = null;
                e.roots = go.GetComponentsInChildren<ParticleSystem>(true)
                    .Where(p => p.transform.parent == null || p.transform.parent.GetComponentInParent<ParticleSystem>() == null).ToArray();
                foreach (var p in go.GetComponentsInChildren<ParticleSystem>(true))
                {
                    p.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                    p.useAutoRandomSeed = false;
                }
                float longest = 0.5f;
                foreach (var p in go.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var main = p.main;
                    longest = Mathf.Max(longest, main.startDelay.constantMax + main.duration + main.startLifetime.constantMax);
                }
                e.life = Mathf.Min(longest, 4f);
            }
            live.Add(e);
            return e;
        }

        void StepEffects(float dt)
        {
            foreach (var rig in rigs)
                if (rig.burst != null)
                    rig.burst.Step(dt, centre, arenaRadius);
            for (int i = 0; i < live.Count; i++)
            {
                var e = live[i];
                e.time += dt;
                if (e.instance == null || e.time > e.life)
                {
                    if (e.instance != null)
                        DestroyAny(e.instance);
                    live.RemoveAt(i--);
                    continue;
                }
                if (e.director != null)
                {
                    e.director.time = e.time;
                    e.director.Evaluate();
                }
                else if (e.roots != null)
                {
                    foreach (var p in e.roots)
                    {
                        if (e.simulated < 0f)
                            p.Simulate(0f, true, true, false);
                        p.Simulate(e.time - Mathf.Max(0f, e.simulated), true, false, true);
                    }
                    e.simulated = e.time;
                }
            }
        }

        static void DestroyAny(GameObject go)
        {
            if (Application.isPlaying)
                Destroy(go);
            else
                DestroyImmediate(go);
        }

        void PlaySound(Fighter who, string name, float volume)
        {
            if (string.IsNullOrEmpty(name))
                return;
            who.rig.Sound(name, volume);
            soundLog.Add(new SoundEvent { frame = frame, fighter = who.rig.id, name = name, volume = volume });
        }

        // ---- rounds

        int roundWinner = -1;

        /// <summary>The side that won the match (-1: none yet / a draw).</summary>
        public int matchWinner = -1;

        void Rules()
        {
            switch (phase)
            {
                case Phase.Fight:
                {
                    bool ko0 = f[0].hp <= 0, ko1 = f[1].hp <= 0;
                    if (ko0 || ko1 || timer <= 0f)
                    {
                        int winner = ko0 && !ko1 ? 1 : ko1 && !ko0 ? 0 : f[0].hp > f[1].hp ? 0 : f[1].hp > f[0].hp ? 1 : -1;
                        message = ko0 || ko1 ? "K.O." : "TIME UP";
                        if (ko0 || ko1)
                            slowMotion = 0.8f;
                        // a KO takes the next stage of the loser's outfit off; the KO that ends the match all that is left
                        bool final = winner >= 0 && burstAllOnFinalKO && (TeamMode ? current[1 - winner] + 1 >= team[1 - winner].Count : wins[winner] + 1 >= roundsToWin);
                        if (ko0)
                            BurstClothes(f[0], f[0].lastHitDir, final && winner == 1);
                        if (ko1)
                            BurstClothes(f[1], f[1].lastHitDir, final && winner == 0);
                        if (winner >= 0)
                            wins[winner]++;
                        phase = Phase.RoundOver;
                        phaseTime = 0f;
                        roundWinner = winner;
                    }
                    break;
                }
                case Phase.RoundOver:
                    if (phaseTime > 1.6f && phaseTime - Dt <= 1.6f)
                    {
                        for (int i = 0; i < 2; i++)
                            f[i].Finish(i == roundWinner);
                        message = roundWinner >= 0 ? $"{f[roundWinner].rig.displayName} WINS" : "DRAW";
                    }
                    if (phaseTime > 4.2f)
                    {
                        if (TeamMode)
                            NextInTeams();
                        else if (wins[0] >= roundsToWin || wins[1] >= roundsToWin)
                            EndMatch(wins[0] >= roundsToWin ? 0 : 1);
                        else
                        {
                            f[0].Revive();
                            f[1].Revive();
                            StartRound();
                        }
                    }
                    break;
            }
        }

        /// <summary>
        /// Team battle, after a round: the loser's next member comes in (her side out of members: the match is over); the
        /// winner stays with her health, a little of it back by the time she had left; a draw sends both teams' next ones in.
        /// </summary>
        void NextInTeams()
        {
            var fresh = new bool[2];
            for (int i = 0; i < 2; i++)
                if (roundWinner != i)
                {
                    current[i]++;
                    fresh[i] = true;
                }
            bool out0 = current[0] >= team[0].Count, out1 = current[1] >= team[1].Count;
            if (out0 || out1)
            {
                EndMatch(out0 && out1 ? -1 : out0 ? 1 : 0);
                return;
            }
            for (int i = 0; i < 2; i++)
            {
                if (fresh[i])
                {
                    pick[i] = team[i][current[i]];
                    ApplyPicks();
                    PrepareSide(i);
                    f[i].Revive();
                }
                else
                    f[i].hp = Mathf.Min(f[i].maxHp, f[i].hp + Mathf.RoundToInt(f[i].maxHp * (0.08f + 0.12f * timer / roundSeconds)));
            }
            // (a twin may have come or gone: both sides' rigs must be the ones in play; the one who stays keeps her health and gauge)
            for (int i = 0; i < 2; i++)
                if (f[i].rig != rigs[i])
                {
                    int hp = f[i].hp;
                    float meter = f[i].meter;
                    PrepareSide(i);
                    f[i].hp = hp;
                    f[i].meter = meter;
                }
            f[0].foe = f[1];
            f[1].foe = f[0];
            StartRound();
        }

        void EndMatch(int winner)
        {
            phase = Phase.MatchOver;
            phaseTime = 0f;
            matchWinner = winner;
            if (winner < 0)
                message = "DRAW GAME";
            else if (TeamMode)
                message = $"{(winner == 0 ? "1P" : "2P")} TEAM WINS\n{f[winner].rig.displayName} STANDS";
            else
                message = $"{f[winner].rig.displayName} WINS THE MATCH";
        }

        // ---- camera: square to the fight line, following the two along it, wider as they part

        public void SnapCamera()
        {
            camSnap = true;
            UpdateCamera(0f);
        }

        public void UpdateCamera(float dt)
        {
            if (cam == null || f[0] == null)
                return;
            if (camFov <= 0f)
                camFov = cam.fieldOfView;
            cam.fieldOfView = camFov;
            float ua = f[0].U, ub = f[1].U;
            float sep = Mathf.Abs(ua - ub), mid = (ua + ub) * 0.5f;
            float high = Mathf.Max(f[0].H, f[1].H);
            // wider as the two part, and a little as one of them goes up (a jump at close range kept her head in the picture)
            float distance = Mathf.Clamp(sep * 0.8f + 3.3f + 0.7f * Mathf.Clamp(high, 0f, 2.5f), camMinDistance, camMaxDistance);
            // the picture stays on the stage: the view's half width at the fight line against the stage's half length
            float aspect = cam.aspect > 0.1f ? cam.aspect : 16f / 9f;
            float halfView = distance * Mathf.Tan(camFov * 0.5f * Mathf.Deg2Rad) * aspect;
            float limit = Mathf.Max(0f, HalfWidth + camEdge - halfView);
            mid = Mathf.Clamp(mid, -limit, limit);
            var target = centre + axis * mid + Vector3.up * (camHeight + camLift * Mathf.Clamp(high, 0f, 2.5f));
            var eye = target + Normal * distance + Vector3.up * camRise;
            float k = camSnap || dt >= 1f ? 1f : 1f - Mathf.Exp(-dt * 6f);
            camSnap = false;
            camPos = Vector3.Lerp(camPos, eye, k);
            camTarget = Vector3.Lerp(camTarget, target, k);
            var shake = Vector3.zero;
            if (shakeTime < shakeDuration)
            {
                shakeTime += dt;
                float fade = 1f - shakeTime / Mathf.Max(0.01f, shakeDuration);
                float amp = shakeAmplitude * 0.045f * fade * fade;
                float w = 2f * Mathf.PI * 14f * shakeTime;
                shake = cam.transform.right * (amp * Mathf.Sin(w)) + Vector3.up * (amp * Mathf.Sin(w * 1.37f + 1.3f));
            }
            cam.transform.position = camPos + shake;
            cam.transform.LookAt(camTarget + shake, Vector3.up);
        }

        public void Shake(float amplitude, float duration)
        {
            shakeAmplitude = amplitude;
            shakeDuration = duration;
            shakeTime = 0f;
        }
    }
}
