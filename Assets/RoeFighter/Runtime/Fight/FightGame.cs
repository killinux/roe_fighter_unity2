using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Playables;

namespace RoeFighter.Fight
{
    /// <summary>
    /// The fight: two fighters on one of the game's stages, best of three rounds, 60 seconds each.
    /// Runs at a fixed 60 steps per second.  Each step: inputs (keyboard, pad or CPU), the fighters'
    /// state machines, their poses, then the hit checks on the posed bodies (the striking bone
    /// against the opponent's body capsule), the special moves' hits, the effects, the round
    /// rules, the camera.  Step() can also be driven from the editor to film a CPU match.
    /// </summary>
    public class FightGame : MonoBehaviour
    {
        [Serializable]
        public class NamedPrefab
        {
            public string name;
            public GameObject prefab;
        }

        public FighterRig[] rigs = new FighterRig[2];     // the two of this match (roster[pick], set by Setup / the select screen)
        public FighterRig[] roster = new FighterRig[0];   // everyone the scene holds; empty: rigs as built
        public int[] pick = { 0, 1 };                      // roster entries of 1P and 2P
        public bool selectFirst = true;                    // play mode, more than two in the roster: the select screen first
        public bool[] cpu = { false, true };
        public List<Move> moves = new List<Move>();
        public List<Special> specials = new List<Special>();
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
        public Vector3 axis = Vector3.right;         // round start: fighters stand along this line
        public float arenaRadius = 7f;
        public float startGap = 3.2f;
        public Camera cam;
        // the camera keeps out of the stage (user 10-05 "继续" after 10-04's "镜头跑到栅栏外面"): it films from where the
        // stage's room (measured when the scene is built) has space and nothing stands between it and the two - square-on
        // if it can, else swung round up to camMaxOrbit, closer (the view widens to keep the framing), round on the other
        // side of the two, and with none of that clear what is in the way is cut away (the near plane moves past it)
        public CameraRoom room;
        public bool camAvoid = true;          // -roeCamAvoid 0: the old camera, which went wherever the framing put it
        public float camMargin = 0.6f;        // metres the camera keeps from the stage
        public float camMaxOrbit = 60f;       // degrees it may swing round from square-on, either way
        public float camMinDistance = 2.4f;   // the closest it comes
        public float camMaxFov = 50f;         // the widest view
        public bool camOtherSide = true;      // with no room on its side, it may go round to the other side
        public float camCutAngle = 100f;      // a turn round the two larger than this is a cut, not a swing
        public float camFov;                  // the view square-on (0: the camera's own, taken at the first frame)
        public float camNear = 0.1f;
        public FightHud hud;
        public int roundsToWin = 2;
        public float roundSeconds = 60f;
        public string voiceLanguage = "2";           // the game's voice language: 2 Japanese, 1 English
        public int seed = 1;
        public AudioClip music;                      // the stage's battle music, looped
        public float musicVolume = 0.3f;
        AudioSource musicSource;
        public List<MotionPack> motionPacks = new List<MotionPack>();   // basic moves that can be swapped (F3)
        public int motionPack;
        float noticeTime;

        /// <summary>Lines under the timer, one per setting: the motion pack, the cloth, the skirt, the clothes burst, the camera -
        /// for a few seconds after a switch or a new match.</summary>
        public string Notice => noticeTime <= 0f ? "" :
            string.Join("\n", new[] { Pack != null ? $"MOTIONS: {Pack.title}" : null, $"CLOTH: {RoeClothBackends.Find(FighterRig.ClothBackend).title}",
                                       RoeClothBackends.Find(FighterRig.ClothBackend).skinnedSkirt ? $"SKIRT: {(RoeSkirtRig.Drape > 0f ? "hangs" : "fitted")}" : null,
                                       rigs.Any(r => r != null && r.burst != null) ? $"CLOTHES BURST: {(RoeClothesBurst.Enabled ? "on" : "off")}" : null,
                                       room != null && room.Valid ? $"CAMERA: {(camAvoid ? "avoids walls" : "free")}" : null }.Where(s => s != null));

        public MotionPack Pack => motionPacks.Count > 0 ? motionPacks[Mathf.Clamp(motionPack, 0, motionPacks.Count - 1)] : null;

        public const float Dt = 1f / 60f;
        public readonly Fighter[] f = new Fighter[2];
        public int frame;
        public float timer;
        public int round;
        public readonly int[] wins = new int[2];
        public string message = "";
        public enum Phase { Select, Intro, Fight, RoundOver, MatchOver }
        public Phase phase = Phase.Intro;
        public readonly bool[] picked = new bool[2];   // the select screen: this side's fighter is confirmed
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
        Vector3 camSide;
        Vector3 camPos, camTarget;
        float shakeTime, shakeAmplitude, shakeDuration;
        Vector3 camAim;                 // the direction chosen last step (the next choice prefers to stay near it)
        float camDistance;
        bool camWidened, camCut;
        public int camCuts, camJumps;   // recordings: cuts to the other side, jumps straight to the shot

        public enum CamMode { Cutaway = -1, SquareOn, SwungRound, Closer, OtherSide }
        public CamMode camMode;
        public readonly int[] camModeSteps = new int[5];    // recordings: how often the camera did what (index = mode + 1)

        void Start()
        {
            // the player: ROEFighter.exe -roeBurst 0 starts with the clothes burst off (F6 switches it);
            // -roeP1 b10 -roeP2 g05 picks the two; -roeSelect 0 goes straight into the match; -roeCloth magica starts with
            // that physics setup (F4 goes on from it)
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
            if (Arg("-roeCamAvoid") != null)
                camAvoid = Arg("-roeCamAvoid") != "0";
            PickIds(Arg("-roeP1"), Arg("-roeP2"));
            if (Arg("-roeSelect") != null)
                selectFirst = Arg("-roeSelect") != "0";
            if (Application.isPlaying && selectFirst && roster != null && roster.Length > 2)
                EnterSelect();
            else
                Setup();
        }

        /// <summary>Picks by character id (null or unknown: that side's pick stays).</summary>
        public void PickIds(string p1, string p2)
        {
            if (roster == null)
                return;
            for (int i = 0; i < 2; i++)
            {
                string id = i == 0 ? p1 : p2;
                int k = string.IsNullOrEmpty(id) ? -1 : Array.FindIndex(roster, r => r != null && r.id == id);
                if (k >= 0)
                    pick[i] = k;
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

        /// <summary>A new match with the picked two.</summary>
        public void Setup()
        {
            Prepare();
            StartMatch();
        }

        /// <summary>The picked two ready to fight: the motion pack's moves, their rigs, whole outfits.</summary>
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
                noticeTime = 4f;
            }
            for (int i = 0; i < 2; i++)
                PrepareSide(i);
            f[0].foe = f[1];
            f[1].foe = f[0];
        }

        void PrepareSide(int i)
        {
            if (Pack != null)
                rigs[i].UseMotions(Pack);
            rigs[i].Init();
            f[i] = new Fighter { rig = rigs[i], game = this, index = i };
            ai[i] = new FightAI(seed * 7919 + i * 104729);
            // her own strikes (g04: Mai Shiranui's) or the pack's
            var own = rigs[i].strikePack;
            f[i].moves = own != null && own.strikes.Count > 0 ? own.CopyStrikes() : moves;
            foreach (var m in f[i].moves)
                if (string.IsNullOrEmpty(m.hitSound))
                    m.hitSound = "hit";
            // a new match: the outfit is whole again
            var burst = rigs[i].burst;
            if (burst != null)
            {
                burst.Restore(seed * 7919 + i * 104729 + 31);
                var who = f[i];
                burst.onPieceOff = (at, size, cloth) => PieceOff(who, at, size, cloth);
            }
        }

        // ---- the select screen: who fights (KOF-like: a card per fighter, the two picked stand on the stage)

        /// <summary>
        /// The select screen as ROE's own menus show a heroine (user 10-06: "选人环节也做个动画，ROE里面标准的动画"): the fighter of
        /// a card turns from facing the other this far towards the camera (0..1) and loops her outfit's showcase idle
        /// (idle_02); a confirmed pick plays her showcase reaction - the game's idle02_react01, react_01 with its "yes" line
        /// (FighterRig.showcase) - and the match starts once both have played out (at most selectWait s after the second pick).
        /// </summary>
        public float selectFaceCamera = 0.8f;
        public string selectReaction = "idle02_react01";
        public float selectWait = 3.5f;
        readonly float[] reactUntil = new float[2];      // phaseTime at which a pick's reaction is over (0: none)
        readonly bool[] showingReact = new bool[2];
        readonly bool[] wasPicked = new bool[2];

        /// <summary>To the select screen; the cards start on the current picks.</summary>
        public void EnterSelect()
        {
            Prepare();
            phase = Phase.Select;
            phaseTime = 0f;
            message = "";
            picked[0] = picked[1] = false;
            wasPicked[0] = wasPicked[1] = false;
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
                var me = i == 0 ? a : b;
                var other = i == 0 ? b : a;
                f[i].Reset(me, Yaw(other - me));
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

        /// <summary>Picks confirmed or taken back since the last frame: the reaction, or back to the idle; a reaction played out: the idle.</summary>
        void ShowPicks()
        {
            for (int i = 0; i < 2; i++)
            {
                if (picked[i] && !wasPicked[i])
                    ShowPicked(i);
                else if (!picked[i] && wasPicked[i])
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
                wasPicked[i] = picked[i];
            }
        }

        /// <summary>Keys on the select screen: left / right move a card, the first attack button confirms, the second goes back.</summary>
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
                // the computer on both sides: two at random
                var rng = new System.Random(seed * 31 + frame);
                pick[0] = rng.Next(roster.Length);
                pick[1] = (pick[0] + 1 + rng.Next(roster.Length - 1)) % roster.Length;
                picked[0] = picked[1] = true;
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
                {
                    StandForSelect(i);
                    wasPicked[i] = false;
                }
            if (hud != null)
                hud.Build(this);
        }

        /// <summary>The select screen is done (both picked): the match starts.</summary>
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
            bool alone = cpu[0] != cpu[1];      // one human: she picks her fighter, then the computer's
            if (back)
            {
                if (picked[side])
                    picked[side] = false;
                else if (alone && picked[1 - side])
                {
                    picked[1 - side] = false;
                    choosing = 1 - side;
                }
                return;
            }
            if (picked[side])
                return;
            if (move != 0 && roster.Length > 0)
                pick[side] = (pick[side] + move + roster.Length) % roster.Length;
            if (ok)
            {
                picked[side] = true;
                if (alone && !picked[1 - side])
                    choosing = 1 - side;
            }
        }

        void StartMatch()
        {
            wins[0] = wins[1] = 0;
            round = 0;
            frame = 0;
            clock = 0f;
            soundLog.Clear();
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
            f[0].Reset(a, Yaw(b - a));
            f[1].Reset(b, Yaw(a - b));
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
            message = $"ROUND {round}";
            hitStop = 0f;
            slowMotion = 0f;
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
                // the camera keeps out of the stage / goes wherever the framing puts it (the old one, through fences)
                camAvoid = !camAvoid;
                cam.fieldOfView = camFov > 0f ? camFov : cam.fieldOfView;
                cam.nearClipPlane = camNear;
                camWidened = camCut = false;
                noticeTime = 4f;
                Debug.Log($"[ROE] camera: {(camAvoid ? "avoids walls" : "free")} (F8)");
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
            // after the match: back to the select screen (the same two are still picked: confirm twice for a rematch)
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

            var camRight = cam != null ? Flat(cam.transform.right) : Vector3.right;
            var camForward = cam != null ? Flat(cam.transform.forward) : Vector3.forward;
            for (int i = 0; i < 2; i++)
            {
                var me = f[i];
                var input = phase == Phase.Fight
                    ? (cpu[i] ? ai[i].Decide(me, dt, camRight) : inputs != null && inputs.Length > i ? inputs[i] : default)
                    : default;
                var toFoe = Flat(me.foe.pos - me.pos);
                int screenSign = Vector3.Dot(toFoe, camRight) >= 0f ? 1 : -1;
                int fwd = input.x * screenSign;
                // a side step goes into the screen (up) or out of it (down), across the line between the two
                var across = Vector3.Cross(Vector3.up, toFoe).normalized;
                if (Vector3.Dot(across, camForward) < 0f)
                    across = -across;
                var sideDir = input.y != 0 ? across * input.y : Vector3.zero;
                me.Step(dt, fwd, input.y, sideDir, input);
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

        void KeepApart()
        {
            var d = f[1].pos - f[0].pos;
            d.y = 0f;
            float dist = d.magnitude;
            float min = Fighter.BodyRadius * 2f;
            bool special = f[0].state == FightState.Special || f[1].state == FightState.Special;
            if (dist < min && dist > 1e-4f && !special)
            {
                var push = d / dist * ((min - dist) * 0.5f);
                f[0].pos -= push;
                f[1].pos += push;
            }
            foreach (var x in f)
            {
                var r = x.pos - centre;
                r.y = 0f;
                float limit = arenaRadius - 0.4f;
                if (r.magnitude > limit)
                    x.pos = centre + r.normalized * limit + Vector3.up * (x.pos.y - centre.y);
            }
        }

        void CheckStrikes()
        {
            for (int i = 0; i < 2; i++)
            {
                var atk = f[i];
                var vic = f[1 - i];
                var p = atk.ActiveHit();
                if (p == null || !vic.Hittable || vic.state == FightState.Special)
                    continue;
                var m = atk.move;
                if (vic.DistanceToBody(p.Value) > m.radius + Fighter.BodyRadius)
                    continue;
                atk.hitDone = true;
                atk.connected = true;
                var dir = Flat(vic.pos - atk.pos);
                if (vic.CanBlock)
                {
                    vic.BlockHit(m.blockstun, dir * (m.push * 0.5f / 0.12f));
                    atk.meter = Mathf.Min(100f, atk.meter + m.meterGain * 0.5f);
                    PlaySound(vic, "block", 0.8f);
                    StrikeSpark(p.Value, m, true);
                    hitStop = 0.05f;
                }
                else
                {
                    vic.TakeHit(m.damage, m.hitstun, dir * (m.push / 0.12f), m.knockdown);
                    atk.meter = Mathf.Min(100f, atk.meter + m.meterGain);
                    PlaySound(atk, m.hitSound, 1f);
                    StrikeSpark(p.Value, m, false);
                    hitStop = m.damage >= 80 ? 0.1f : 0.07f;
                    Shake(m.damage >= 80 ? 0.6f : 0.3f, 0.2f);
                }
            }
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
                float reach = BlowReach(atk.rig.sheet != null ? atk.rig.sheet.Find(s.action) : null, vic);
                atk.specialOutcome = !vic.Hittable || Vector3.Distance(atk.pos, vic.pos) > reach ? 3 : vic.CanBlock ? 2 : 1;
            }
            if (atk.specialOutcome == 3 || !vic.Hittable)
                return;
            var dir = Flat(vic.pos - atk.pos);
            float hold = atk.SpecialRemaining + 0.15f;
            if (atk.specialOutcome == 2)
            {
                vic.hp = Mathf.Max(1, vic.hp - Mathf.RoundToInt(damage * 0.1f));
                vic.BlockHit(last ? 0.3f : hold, dir * 0.6f);
                hitStop = 0.04f;
                return;
            }
            bool knockdown = last && s.meterCost > 0f;
            vic.TakeHit(damage, last ? 0.6f : hold, dir * (last ? 2.5f : 0.4f), knockdown);
            atk.meter = Mathf.Min(100f, atk.meter + (s.meterCost > 0f ? 0f : 4f));
            hitStop = last ? 0.12f : 0.04f;
            // the super's finishing blow takes the next stage of her outfit off
            if (knockdown)
                BurstClothes(vic, dir);
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
            else if (m.knockdown || m.damage >= hitHeavyDamage)
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
                        bool final = winner >= 0 && wins[winner] + 1 >= roundsToWin && burstAllOnFinalKO;
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
                        if (wins[0] >= roundsToWin || wins[1] >= roundsToWin)
                        {
                            phase = Phase.MatchOver;
                            phaseTime = 0f;
                            int w = wins[0] >= roundsToWin ? 0 : 1;
                            message = $"{f[w].rig.displayName} WINS THE MATCH";
                        }
                        else
                        {
                            StartRound();
                        }
                    }
                    break;
            }
        }

        int roundWinner = -1;

        // ---- camera: from the side of the line between the two, far enough to keep both in the picture

        public void SnapCamera()
        {
            camSide = Vector3.zero;
            camAim = Vector3.zero;
            camDistance = 0f;
            UpdateCamera(10f);
        }

        bool Avoiding => camAvoid && room != null && room.Valid;

        public void UpdateCamera(float dt)
        {
            if (cam == null || f[0] == null)
                return;
            if (camFov <= 0f)
                camFov = cam.fieldOfView;
            var a = f[0].pos;
            var b = f[1].pos;
            var line = Flat(b - a);
            var side = Vector3.Cross(line, Vector3.up).normalized;
            if (camSide.sqrMagnitude < 0.5f)
                camSide = Flat(cam.transform.position - (a + b) * 0.5f);
            if (Vector3.Dot(side, camSide) < 0f)
                side = -side;
            float sep = Vector3.Distance(a, b);
            float distance = Mathf.Clamp(sep * 0.85f + 2.9f, 3.6f, 9.5f);
            var mid = (a + b) * 0.5f;
            var (dir, far, mode) = Frame(mid, side, distance, a, b);
            camAim = dir;
            camMode = mode;
            camModeSteps[(int)mode + 1]++;

            // swing round about the upright through the two; round to the other side is a cut (a swing would pass
            // through the fence or the two themselves)
            bool snap = dt >= 1f || (mode == CamMode.OtherSide && Vector3.Angle(camSide, dir) > camCutAngle);
            if (snap && dt < 1f)
                camCuts++;
            float k3 = snap ? 1f : 1f - Mathf.Exp(-dt * 3f);
            float turn = Vector3.SignedAngle(camSide, dir, Vector3.up);
            camSide = (Quaternion.AngleAxis(turn * k3, Vector3.up) * camSide).normalized;
            camDistance = !Avoiding || camDistance <= 0f || snap ? far : Mathf.Lerp(camDistance, far, k3);

            var target = mid + Vector3.up * 1.0f;
            var eye = target + camSide * camDistance + Vector3.up * 0.45f;
            float k = 1f - Mathf.Exp(-dt * 5f);
            camPos = snap ? eye : Vector3.Lerp(camPos, eye, k);
            camTarget = snap ? target : Vector3.Lerp(camTarget, target, k);
            // on its way from one shot to the next the camera may pass where the stage is in the way: then straight to the shot
            if (Avoiding && mode != CamMode.Cutaway && !snap && !Sees(camPos, a, b))
            {
                camSide = dir;
                camDistance = far;
                camPos = target + camSide * camDistance + Vector3.up * 0.45f;
                camTarget = target;
                camJumps++;
            }
            // closer than the framing wants: the view widens to keep both in the picture
            float fov = Mathf.Clamp(2f * Mathf.Atan(Mathf.Tan(camFov * 0.5f * Mathf.Deg2Rad) * distance / Mathf.Max(0.1f, camDistance)) * Mathf.Rad2Deg,
                                    camFov, Mathf.Max(camFov, camMaxFov));
            if (Avoiding && (fov > camFov + 0.01f || camWidened))
            {
                cam.fieldOfView = fov;
                camWidened = fov > camFov + 0.01f;
            }
            // nothing clear: the near plane moves past whatever stands between the camera and the two
            if (Avoiding && (mode == CamMode.Cutaway || camCut))
            {
                cam.nearClipPlane = mode == CamMode.Cutaway ? Cutaway(camPos, a, b) : camNear;
                camCut = mode == CamMode.Cutaway;
            }
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

        /// <summary>
        /// Where to film from this step: the direction from the two's midpoint to the camera, its distance and how it
        /// came about.  Tries square-on first, then swung round (5-degree steps up to camMaxOrbit), closer (0.3 m steps,
        /// no closer than the widest view allows), then the same round on the other side; each try costs by how far it
        /// is from square-on, how much closer, the other side, and how far it is from the last choice (so the camera
        /// does not flick between two near-equal shots).  The cheapest that stands in the room with a clear view of
        /// both wins; with none, square-on and the cut-away.
        /// </summary>
        (Vector3 dir, float distance, CamMode mode) Frame(Vector3 mid, Vector3 side, float ideal, Vector3 a, Vector3 b)
        {
            if (!Avoiding)
                return (side, ideal, CamMode.SquareOn);
            float closest = Mathf.Min(ideal, Mathf.Max(camMinDistance,
                ideal * Mathf.Tan(camFov * 0.5f * Mathf.Deg2Rad) / Mathf.Tan(Mathf.Max(camFov, camMaxFov) * 0.5f * Mathf.Deg2Rad)));
            var aim = camAim.sqrMagnitude > 0.5f ? camAim : side;
            int steps = Mathf.Max(1, Mathf.CeilToInt(camMaxOrbit / 5f));
            float best = float.MaxValue;
            var shot = (side, ideal, CamMode.Cutaway);
            for (int other = 0; other < (camOtherSide ? 2 : 1); other++)
                for (int j = 0; j <= 2 * steps; j++)
                {
                    int i = (j + 1) / 2 * (j % 2 == 1 ? 1 : -1);     // 0, 1, -1, 2, -2, ...: the likely ones first
                    float orbit = camMaxOrbit * i / steps;
                    var dir = Quaternion.AngleAxis(orbit + 180f * other, Vector3.up) * side;
                    float away = Vector3.Angle(dir, aim) / 90f;
                    float cost0 = Sq(orbit / Mathf.Max(1f, camMaxOrbit)) + 1.5f * other + 0.5f * away * away;
                    for (float d = ideal; d >= closest - 1e-3f && cost0 < best; d -= 0.3f)
                    {
                        float cost = cost0 + 2f * Sq((ideal - d) / ideal);
                        if (cost >= best)
                            break;
                        var eye = mid + dir * d;
                        if (room.Clearance(eye) < camMargin || !room.Clear(eye, a) || !room.Clear(eye, b))
                            continue;
                        best = cost;
                        shot = (dir, d, other == 1 ? CamMode.OtherSide : d < ideal - 0.01f ? CamMode.Closer : i != 0 ? CamMode.SwungRound : CamMode.SquareOn);
                        break;
                    }
                }
            return shot;
        }

        static float Sq(float x) => x * x;

        /// <summary>Does the camera at <paramref name="eye"/> have room and a clear view of both?</summary>
        bool Sees(Vector3 eye, Vector3 a, Vector3 b) => room.Clearance(eye) >= camMargin * 0.5f && room.Clear(eye, a) && room.Clear(eye, b);

        /// <summary>The near plane just past the last of the stage between the camera and the two (never past the nearer one).</summary>
        float Cutaway(Vector3 eye, Vector3 a, Vector3 b)
        {
            float last = Mathf.Max(room.LastBlocked(eye, a), room.LastBlocked(eye, b));
            if (last < 0f)
                return camNear;
            float nearer = Mathf.Min(Vector3.Distance(Flat0(eye), Flat0(a)), Vector3.Distance(Flat0(eye), Flat0(b)));
            return Mathf.Clamp(last + 0.3f, camNear, Mathf.Max(camNear, nearer - 0.8f));
        }

        static Vector3 Flat0(Vector3 v) => new Vector3(v.x, 0f, v.z);

        public void Shake(float amplitude, float duration)
        {
            shakeAmplitude = amplitude;
            shakeDuration = duration;
            shakeTime = 0f;
        }
    }
}
