using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RoeFighter.Fight
{
    /// <summary>
    /// What one fighter does, step by step: the state machine of a side-view fighting game in the manner of
    /// The King of Fighters.  Both stand on one line (FightGame.axis) and always face each other.
    ///   neutral   stand in guard, walk forward / back, run (tap forward twice), back-dash (tap back twice),
    ///             crouch (hold down), jump (up: a hop when tapped, a full jump when held; after a crouch a
    ///             hyper hop / super jump).  Holding back blocks: standing it stops mids and overheads,
    ///             crouching mids and lows.
    ///   strikes   A B C D standing (her own game's moves, or the fight's motion pack), crouching and jumping
    ///             (the basics pack: FightGame.basics); a strike that connects or is blocked can be followed by
    ///             another, by a special or the super, or by MAX mode, from its cancel point on.  C+D is the
    ///             blowback attack (knocks the opponent flying), A+B the roll (through strikes and through the
    ///             opponent), forward + C up close a throw, back + C the throw the other way.
    ///   specials  the game's own skills: 236+A/C or the shortcut keys; 236236+A/C (or the third shortcut) is
    ///             the super, it costs one stock of the gauge.  The skill sheet says when the fighter slides up
    ///             to the opponent and back and when the hits land; a skill that reaches from a distance in her game
    ///             (RangedSkill) throws its weapon, fires its bullets or aims its spot, and lands only what reaches.
    ///             A skill that landed (or was blocked) can give way to the super: the super cancel.
    ///   MAX       B+C (or its own key) with a stock: ten seconds of MAX mode - strikes and skills 20% stronger, and
    ///             the super costs no stock (it ends MAX, 30% stronger still).
    ///   guard cancels  while blocking, A+B rolls out and C+D strikes back with the blowback attack, a stock each.
    ///   reactions hurt (standing, crouching, low), block, launched into the air (juggles, at most JuggleLimit
    ///             hits), knocked down (the game's fall and lying clips), getting up, thrown, KO.
    /// The fight (FightGame) does the hit checks, the projectiles, the round, the camera and the inputs.
    /// </summary>
    public class Fighter
    {
        public FighterRig rig;
        public FightGame game;
        public Fighter foe;
        public int index;
        public Vector3 pos;                  // world; on the fight line: FightGame.centre + axis * U + up * H
        public float yaw;
        public int facing = 1;               // +1 she faces along the fight axis (screen right from the camera's side), -1 against it
        public int maxHp = 1000, hp = 1000;
        public float meter;                  // the power gauge: a stock is Stock, at most MeterMax
        public const float Stock = 100f, MeterMax = 300f;
        public int Stocks => Mathf.FloorToInt(meter / Stock + 1e-4f);
        public FightState state = FightState.Intro;
        public float t;                      // seconds in the current state
        public Move move;
        public Special special;
        public bool hitDone;                 // the current strike already connected or was blocked
        public bool connected;               // ... and it was not a whiff (for cancels)
        public float stun;                   // hurt / block time left
        public float invul;                  // seconds no blow lands on her (a roll, a back-dash, a guard cancel's start)
        public int fwdHeld, downHeld, upHeld;    // the stick this step: +1 towards the opponent / -1 away; down; up
        public int comboHits;
        public int juggleHits;               // hits taken in the air since she left the floor
        public bool ko;                      // KO'd in the air: she stays down when she lands
        public bool atWall;                  // against the edge of the stage
        public bool guardCancel;             // the current strike is a guard cancel's blowback (half damage)
        float slide;                         // pushback along the axis (m/s, signed along the axis)
        float vu, vh;                        // airborne velocity along the axis and up (m/s)
        float jumpAlong;                     // the jump's travel along the axis (m/s, signed), set at the squat
        bool jumpSuper, jumpFromRun, liftedOff;
        bool blockLow, hurtLow;
        string pairButton;                   // a strike just started by this button may still become A+B, C+D or B+C
        float pairWindow;
        float tapFwd = 99f, tapBack = 99f;   // seconds since forward / back was last pressed (double taps)
        int lastFwd;
        Vector3 specialHome, specialDir;
        int specialHitIndex;
        List<float> specialHits = new List<float>();
        List<float> specialRatios = new List<float>();
        public int specialOutcome;           // 0 not decided yet, 1 hits, 2 blocked, 3 missed
        public Vector3 lastHitDir;           // the way the last blow that hit her went (flat)
        public List<Move> moves;             // her standing strikes: her own (FighterRig.strikePack) or the fight's (FightGame.moves)
        public List<Move> basics;            // the basics pack's strikes: crouching, jumping, the blowback attack, the throw
        readonly List<int> history = new List<int>();     // numpad directions, newest last
        float dieLength, downFor;
        float getupLength;                   // a get-up clip of her own ("getup"); 0: the basics pack's, or a fade back to the guard
        readonly List<float> throwHits = new List<float>();
        int throwHitIndex;
        bool throwBack;                      // the throw goes the other way (back + C): she turns and carries the opponent over
        float throwFromU;

        // MAX mode
        public float maxTime;                // seconds of MAX mode left
        public bool Max => maxTime > 0f;
        public const float MaxDuration = 10f, MaxDamage = 1.2f, MaxSuperDamage = 1.3f;
        public bool maxSuper;                // the running super is a MAX super
        /// <summary>Her blows' damage factor now (MAX mode, a MAX super).</summary>
        public float DamageScale => (Max ? MaxDamage : 1f) * (maxSuper && special != null && special.meterCost > 0f ? MaxSuperDamage * MaxDamage : 1f);

        public const float BodyRadius = 0.32f;
        // the walk clips play at the rate that keeps a planted foot still (FighterRig.StrideSpeed)
        public const float WalkSpeed = 1.25f, BackSpeed = 1.0f, RunSpeed = 3.4f;
        public const float Gravity = 15f;
        public const float JumpUp = 6.6f, HopUp = 4.4f, SuperJumpUp = 7.7f;          // m/s up at take-off
        public const float JumpAlong = 2.6f, HopAlong = 2.3f, SuperAlong = 3.4f, HyperHopAlong = 3.6f, RunJumpExtra = 0.8f;
        public const float JumpSquat = 4f / 60f, LandLock = 0.1f, TapWindow = 0.25f;
        public const float BackDashTime = 0.36f, BackDashLength = 1.6f, BackDashInvul = 0.1f;
        public const float RollTime = 0.55f, RollLength = 2.5f, RollInvul = 0.42f;
        public const int JuggleLimit = 2;
        public const float ThrowRange = 1.05f, ThrowHold = 0.85f, ThrowToss = 0.97f, ThrowRecover = 1.3f, ThrowCarry = 0.3f;
        public const float CrouchTop = 0.95f, StandTop = 1.6f;

        public Vector3 Forward => game.axis * facing;
        /// <summary>Her place along the fight line (m from the centre, along the axis).</summary>
        public float U
        {
            get => Vector3.Dot(pos - game.centre, game.axis);
            set => pos = game.centre + game.axis * value + Vector3.up * H;
        }
        /// <summary>Her height above the floor.</summary>
        public float H
        {
            get => pos.y - game.centre.y;
            set => pos = new Vector3(pos.x, game.centre.y + value, pos.z);
        }
        /// <summary>Which way along the axis the opponent is (+1 / -1).</summary>
        public int FoeSide => foe == null || foe.U >= U ? 1 : -1;
        public float Dist => foe != null ? Mathf.Abs(foe.U - U) : 99f;
        public bool Airborne => state == FightState.Jump ? liftedOff
                              : state == FightState.Launched || (state == FightState.Attack && move != null && move.stance == Stance.Air);
        public bool Grounded => !Airborne;
        public bool Crouching => state == FightState.Crouch
                                 || (state == FightState.Attack && move != null && move.stance == Stance.Crouch)
                                 || (state == FightState.Block && blockLow) || (state == FightState.Hurt && hurtLow);
        public bool Neutral => state == FightState.Idle || state == FightState.Walk || state == FightState.Run || state == FightState.Crouch;
        public bool Hittable => state != FightState.Down && state != FightState.Getup && state != FightState.KO &&
                                state != FightState.Intro && state != FightState.Win && state != FightState.Lose &&
                                state != FightState.Throw && state != FightState.Thrown && invul <= 0f &&
                                !(state == FightState.Launched && juggleHits >= JuggleLimit);
        /// <summary>Holding back on the floor: a strike is blocked - standing or crouching, as the stick says (Blocks).</summary>
        public bool CanBlock => Grounded && fwdHeld < 0 && (Neutral || state == FightState.Block || state == FightState.Land);
        /// <summary>Whether a strike she can block is stopped by the block she holds: overheads only standing, lows only crouching.</summary>
        public bool Blocks(Move m) => m.Overhead ? downHeld <= 0 : !m.Low || downHeld > 0;
        /// <summary>On the floor and not in the middle of something: a throw grabs her.</summary>
        public bool Throwable => Grounded && invul <= 0f && (Neutral || state == FightState.Land || (state == FightState.Block && stun < 0.05f));
        /// <summary>The body's top (crouching lower), above her feet.</summary>
        public float Top => Crouching ? CrouchTop : StandTop;
        public float SpecialRemaining => special != null ? specialEnd - t : 0f;
        // the game's skills go on for 1-5 s after their last hit (a pose, the way back): over here
        // the body is free this long after the last hit, while the skill's effects play out
        public const float SpecialTail = 0.8f;
        float specialEnd;

        static float Yaw(Vector3 v) => Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;

        /// <summary>At her mark for a round, facing along the axis (+1) or against it (-1); health and gauge are the fight's business.</summary>
        public void Reset(Vector3 position, int face)
        {
            pos = position;
            facing = face;
            yaw = Yaw(Forward);
            state = FightState.Intro;
            t = 0f;
            stun = 0f;
            invul = 0f;
            slide = 0f;
            vu = vh = 0f;
            move = null;
            if (special != null)
                EndRanged();
            special = null;
            juggleHits = 0;
            ko = false;
            comboHits = 0;
            liftedOff = false;
            pairWindow = 0f;
            tapFwd = tapBack = 99f;
            lastFwd = 0;
            guardCancel = maxSuper = false;
            history.Clear();
            rig.Airborne = false;
            rig.ShowTimeline(null, 0f);
            rig.ShowWeapons(true);
            rig.Play(rig.Has("react_02") ? "react_02" : "guard", 1f, 0f);
            dieLength = rig.Length("die");
            getupLength = rig.Has("getup") ? rig.Length("getup") : 0f;
            Place();
            rig.ResetCloth();
        }

        /// <summary>Full health, empty gauge, no MAX: a fresh fighter.</summary>
        public void Revive()
        {
            hp = maxHp;
            meter = 0f;
            if (maxTime > 0f)
            {
                maxTime = 0f;
                game.OnMaxEnd(this);
            }
        }

        public void Go()
        {
            Enter(FightState.Idle);
        }

        void Enter(FightState s)
        {
            state = s;
            t = 0f;
            switch (s)
            {
                case FightState.Idle:
                    rig.Play("guard", 1f, 0.15f, false);
                    break;
                case FightState.Crouch:
                    PlayAny(1f, 0.1f, false, "crouch", "guard");
                    break;
                case FightState.Win:
                    rig.Play(rig.Has("idle_02") ? "idle_02" : "react_01", 1f, 0.3f);
                    break;
                case FightState.Lose:
                    rig.Play("hurt", 0.6f, 0.3f);
                    break;
            }
            rig.ShowWeapons(s == FightState.Special || s == FightState.Intro || s == FightState.Win);
        }

        /// <summary>The first of these clips she has, played; false if none.</summary>
        bool PlayAny(float speed, float fade, bool restart, params string[] names)
        {
            foreach (var n in names)
                if (rig.Has(n))
                {
                    rig.Play(n, speed, fade, restart);
                    return true;
                }
            return false;
        }

        /// <summary>Numpad direction from forward (+1 towards the opponent) and up (+1 up, -1 down).</summary>
        static int Direction(int fwd, int up)
        {
            int row = up > 0 ? 7 : up < 0 ? 1 : 4;
            return row + (fwd > 0 ? 2 : fwd < 0 ? 0 : 1);
        }

        bool Motion(params int[] sequence)
        {
            int k = sequence.Length - 1;
            for (int i = history.Count - 1; i >= 0 && i >= history.Count - 18; i--)
            {
                if (history[i] == sequence[k])
                {
                    k--;
                    if (k < 0)
                        return true;
                }
            }
            return false;
        }

        /// <summary>Down was held within the last few steps (a super jump / hyper hop: down, then up).</summary>
        bool RecentDown(int steps = 10)
        {
            for (int i = history.Count - 1; i >= 0 && i >= history.Count - steps; i--)
                if (history[i] <= 3)
                    return true;
            return false;
        }

        /// <summary>The super's input this step: its shortcut, or 236236 + A or C.</summary>
        bool SuperInput(FighterInput input) => input.s3 || ((input.a || input.c) && Motion(2, 3, 6, 2, 3, 6));

        /// <param name="fwd">+1 towards the opponent, -1 away</param>
        /// <param name="up">+1 up (jump), -1 down (crouch)</param>
        public void Step(float dt, int fwd, int up, FighterInput input)
        {
            t += dt;
            fwdHeld = fwd;
            downHeld = up < 0 ? 1 : 0;
            upHeld = up > 0 ? 1 : 0;
            if (invul > 0f)
                invul -= dt;
            if (maxTime > 0f)
            {
                maxTime -= dt;
                if (maxTime <= 0f)
                {
                    maxTime = 0f;
                    game.OnMaxEnd(this);
                }
            }
            history.Add(Direction(fwd, up));
            if (history.Count > 40)
                history.RemoveAt(0);
            // double taps: forward twice runs, back twice dashes back
            tapFwd += dt;
            tapBack += dt;
            bool runTap = false, backTap = false;
            if (fwd > 0 && lastFwd <= 0)
            {
                runTap = tapFwd < TapWindow;
                tapFwd = 0f;
            }
            if (fwd < 0 && lastFwd >= 0)
            {
                backTap = tapBack < TapWindow;
                tapBack = 0f;
            }
            lastFwd = fwd;

            // sliding back from a hit or a block
            if (Mathf.Abs(slide) > 1e-4f)
            {
                MoveAlong(slide * dt);
                slide = Mathf.MoveTowards(slide, 0f, 9f * dt);
            }

            switch (state)
            {
                case FightState.Intro:
                case FightState.Win:
                case FightState.Lose:
                    break;

                case FightState.Idle:
                case FightState.Walk:
                case FightState.Run:
                case FightState.Crouch:
                    Face(dt);
                    if (TryStart(input))
                        break;
                    if (up > 0)
                    {
                        StartJump(fwd);
                        break;
                    }
                    if (state == FightState.Run)
                    {
                        if (fwd > 0)
                        {
                            MoveLocomotion("run", RunSpeed, fwd);
                            break;
                        }
                        Enter(FightState.Idle);
                        break;
                    }
                    if (up < 0)
                    {
                        if (state != FightState.Crouch)
                            Enter(FightState.Crouch);
                        break;
                    }
                    if (runTap && rig.Has("run"))
                    {
                        state = FightState.Run;
                        t = 0f;
                        MoveLocomotion("run", RunSpeed, fwd);
                        break;
                    }
                    if (backTap)
                    {
                        StartBackDash();
                        break;
                    }
                    if (fwd != 0)
                    {
                        state = FightState.Walk;
                        var pack = rig.motions;
                        float speed = fwd > 0 ? (pack != null && pack.walkSpeed > 0f ? pack.walkSpeed : WalkSpeed)
                                              : (pack != null && pack.backSpeed > 0f ? pack.backSpeed : BackSpeed);
                        MoveLocomotion(fwd > 0 ? "walk" : "walk_back", speed, fwd);
                    }
                    else if (state != FightState.Idle)
                        Enter(FightState.Idle);
                    break;

                case FightState.Jump:
                    if (!liftedOff)
                    {
                        if (t >= JumpSquat)
                            LiftOff();
                        break;
                    }
                    if (TryStart(input))
                        break;
                    AirStep(dt);
                    break;

                case FightState.Land:
                    Face(dt);
                    if (t >= LandLock)
                        Enter(downHeld > 0 ? FightState.Crouch : FightState.Idle);
                    break;

                case FightState.Attack:
                    StepAttack(dt, input);
                    break;

                case FightState.Block:
                    Face(dt, 360f);
                    // a guard cancel: out of the block with a roll or the blowback attack, for a stock
                    if (stun > 0f && TryGuardCancel(input))
                        break;
                    stun -= dt;
                    if (stun <= 0f)
                        Enter(downHeld > 0 ? FightState.Crouch : FightState.Idle);
                    break;

                case FightState.Hurt:
                    Face(dt, 360f);
                    stun -= dt;
                    if (stun <= 0f)
                        Enter(downHeld > 0 ? FightState.Crouch : FightState.Idle);
                    break;

                case FightState.Launched:
                    AirStep(dt);
                    break;

                case FightState.Down:
                    if (t >= dieLength && rig.Current == "die")
                        rig.Play("rip", 1f, 0.2f);
                    if (rig.Current == "fall_down" && t >= rig.Length("fall_down") && rig.Has("rip"))
                        rig.Play("rip", 1f, 0.3f);
                    if (t >= downFor)
                    {
                        state = FightState.Getup;
                        t = 0f;
                        if (getupLength > 0f)
                            rig.Play("getup", 1f, 0.2f);
                        else if (!PlayAny(1f, 0.2f, true, "getup"))
                            rig.Play("guard", 1f, 0.45f);
                    }
                    break;

                case FightState.Getup:
                {
                    float length = getupLength > 0f ? getupLength : rig.Has("getup") ? rig.Length("getup") : 0.5f;
                    Face(dt);
                    if (t >= length)
                        Enter(FightState.Idle);
                    break;
                }

                case FightState.KO:
                    if (t >= dieLength && rig.Current == "die")
                        rig.Play("rip", 1f, 0.2f);
                    if (rig.Current == "fall_down" && t >= rig.Length("fall_down") && rig.Has("rip"))
                        rig.Play("rip", 1f, 0.3f);
                    break;

                case FightState.Special:
                    StepSpecial(dt, input);
                    break;

                case FightState.Roll:
                    MoveAlong(FoeSideAtStart * RollLength * dt / RollTime);
                    if (t >= RollTime)
                        Enter(FightState.Idle);
                    break;

                case FightState.BackDash:
                    MoveAlong(-FoeSideAtStart * BackDashLength * dt / BackDashTime);
                    if (t >= BackDashTime)
                        Enter(FightState.Idle);
                    break;

                case FightState.Throw:
                    StepThrow(dt);
                    break;

                case FightState.Thrown:
                    // held in front of the thrower until she lets go (Throw's toss turns this into Launched)
                    break;
            }
            Bound();
            rig.Airborne = Airborne;
            rig.LockFeet(state == FightState.Walk || state == FightState.Run);
            Place();
        }

        int FoeSideAtStart;      // the way she faced when a roll / back-dash started (she does not turn mid-way)

        /// <summary>Turns to the opponent (in the neutral states) and eases the body round.</summary>
        void Face(float dt, float rate = 1080f)
        {
            if (Dist > 0.02f)
                facing = FoeSide;
            yaw = Mathf.MoveTowardsAngle(yaw, Yaw(Forward), rate * dt);
        }

        /// <summary>Walking or running: the clip at the rate at which a planted foot keeps pace with the body (AfterPose moves her).</summary>
        void MoveLocomotion(string clip, float speed, int fwd)
        {
            locoSpeed = speed;
            locoDir = game.axis * (FoeSide * fwd);
            rig.Play(clip, speed / rig.StrideSpeed(clip), 0.15f, false);
        }

        void MoveAlong(float metres)
        {
            if (Mathf.Abs(metres) > 1e-7f)
                U += metres;
        }

        /// <summary>Inside the stage and no farther from the opponent than the picture holds.</summary>
        void Bound()
        {
            float u = U, lim = game.HalfWidth;
            float bounded = Mathf.Clamp(u, -lim, lim);
            if (foe != null && state != FightState.Thrown && state != FightState.Throw)
            {
                float fu = foe.U;
                bounded = Mathf.Clamp(bounded, fu - game.maxSeparation, fu + game.maxSeparation);
            }
            if (Mathf.Abs(bounded - u) > 1e-6f)
            {
                U = bounded;
                if (Airborne)
                    vu = 0f;
            }
            atWall = Mathf.Abs(bounded) >= lim - 0.01f;
        }

        // ---- jumping

        void StartJump(int fwd)
        {
            jumpFromRun = state == FightState.Run;
            jumpSuper = RecentDown();
            jumpAlong = fwd * FoeSide;      // the sign along the axis; the speed comes at lift-off
            state = FightState.Jump;
            t = 0f;
            liftedOff = false;
            PlayAny(1f, 0.03f, true, "takeoff", "guard");
            rig.ShowWeapons(false);
        }

        void LiftOff()
        {
            liftedOff = true;
            bool held = upHeld > 0;         // still held at lift-off: a full jump; let go: a hop
            vh = jumpSuper ? (held ? SuperJumpUp : HopUp * 1.05f) : (held ? JumpUp : HopUp);
            float along = jumpSuper ? (held ? SuperAlong : HyperHopAlong) : (held ? JumpAlong : HopAlong);
            vu = jumpAlong * (along + (jumpFromRun ? RunJumpExtra : 0f));
            // a jump of her own game in one piece (DOA6: a somersault from take-off to landing, cut to the part in the air):
            // played over the time she is in the air; else the jump's rise, then its fall
            float air = 2f * vh / Gravity;
            string arc = jumpAlong != 0f && rig.Has("jump_arc_fwd") ? "jump_arc_fwd" : rig.Has("jump_arc") ? "jump_arc" : null;
            if (arc != null)
                rig.Play(arc, Mathf.Max(0.3f, rig.Length(arc) / Mathf.Max(0.2f, air)), 0.05f);
            else
                PlayAny(1f, 0.05f, true, "jump", "guard");
        }

        void AirStep(float dt)
        {
            MoveAlong(vu * dt);
            H = Mathf.Max(0f, H + vh * dt);
            vh -= Gravity * dt;
            if (state == FightState.Jump && vh < 0f && rig.Current == "jump" && rig.Has("fall"))
                rig.Play("fall", 1f, 0.15f);
            if (H <= 0f && vh <= 0f)
                Land();
        }

        void Land()
        {
            H = 0f;
            vu = vh = 0f;
            liftedOff = false;
            if (state == FightState.Launched)
            {
                juggleHits = 0;
                rig.ShowWeapons(false);
                if (ko)
                {
                    state = FightState.KO;
                    t = 0f;
                    if (!PlayAny(1f, 0.05f, true, "fall_down", "die"))
                        rig.Play("rip", 1f, 0.2f);
                    return;
                }
                state = FightState.Down;
                t = 0f;
                PlayAny(1f, 0.05f, true, "fall_down", "die", "rip");
                downFor = (rig.Has("fall_down") ? rig.Length("fall_down") : dieLength) + 0.6f;
                return;
            }
            juggleHits = 0;
            state = FightState.Land;
            t = 0f;
            Face(0f);
            yaw = Yaw(Forward);
            PlayAny(1f, 0.05f, true, "landing", "guard");
        }

        // ---- strikes

        /// <param name="cancel">from a strike that connected (or was blocked): strikes, the blowback attack, the specials and
        /// MAX mode may follow; no throw, roll or jump</param>
        bool TryStart(FighterInput input, bool cancel = false)
        {
            bool air = Airborne;
            if (!air)
            {
                // MAX mode: B+C (or its key), with a stock; from a strike that connected it cancels it (she is free at once)
                if ((input.max || (input.b && input.c)) && TryMax())
                {
                    if (state == FightState.Attack)
                        Enter(FightState.Idle);
                    return true;
                }
                bool punch = input.a || input.c;
                if (SuperInput(input) && StartSpecial("skill3", cancel))
                    return true;
                if ((input.s1 || (punch && Motion(2, 3, 6))) && StartSpecial("skill1", cancel))
                    return true;
                if ((input.s2 || (punch && Motion(2, 1, 4))) && StartSpecial("skill2", cancel))
                    return true;
                if (!cancel && (input.ab || (input.a && input.b)) && StartRoll())
                    return true;
                if ((input.cd || (input.c && input.d)) && StartMove(FindMove("CD", Stance.Stand)))
                    return true;
                // a throw: forward or back + C, up close, the opponent on her feet (back + C throws her the other way)
                if (!cancel && input.c && fwdHeld != 0 && Dist < ThrowRange && foe.Throwable && downHeld <= 0 && state != FightState.Crouch &&
                    StartMove(FindMove("T", Stance.Stand)))
                {
                    throwBack = fwdHeld < 0;
                    return true;
                }
            }
            else if (cancel)
                return false;       // no chains in the air
            string button = input.a ? "A" : input.b ? "B" : input.c ? "C" : input.d ? "D" : null;
            if (button == null)
                return false;
            var stance = air ? Stance.Air : (downHeld > 0 || state == FightState.Crouch || (cancel && move != null && move.stance == Stance.Crouch))
                ? Stance.Crouch : Stance.Stand;
            var m = FindMove(button, stance);
            if (m == null || !StartMove(m))
                return false;
            pairButton = button;
            pairWindow = 2.5f / 60f;
            return true;
        }

        /// <summary>A button's strike in a stance: standing, her own (or the fight pack's); crouching, jumping, CD and the throw from the basics.</summary>
        public Move FindMove(string button, Stance stance)
        {
            if (stance == Stance.Stand && button.Length == 1)
            {
                var own = (moves ?? game.moves)?.FirstOrDefault(x => x.button == button && x.stance == Stance.Stand);
                if (own != null && rig.Has(own.clip))
                    return own;
            }
            return basics?.FirstOrDefault(x => x.button == button && x.stance == stance && rig.Has(x.clip));
        }

        bool StartMove(Move m)
        {
            if (m == null || !rig.Has(m.clip))
                return false;
            move = m;
            hitDone = connected = false;
            guardCancel = false;
            if (state != FightState.Jump && state != FightState.Attack)
                liftedOff = false;
            state = FightState.Attack;
            t = 0f;
            pairWindow = 0f;
            // a strike on the floor locks the facing on the opponent; in the air she keeps the way she took off
            if (m.stance != Stance.Air && Dist > 0.02f)
            {
                facing = FoeSide;
                yaw = Yaw(Forward);
            }
            rig.Play(m.clip, m.speed, 0.06f);
            rig.ShowWeapons(false);
            return true;
        }

        bool StartRoll()
        {
            if ((!rig.Has("roll") && !rig.Has("dash")) || Airborne)
                return false;
            state = FightState.Roll;
            t = 0f;
            invul = RollInvul;
            FoeSideAtStart = FoeSide;
            facing = FoeSide;
            yaw = Yaw(Forward);
            string clip = rig.Has("roll") ? "roll" : "dash";
            rig.Play(clip, Mathf.Max(0.5f, rig.Length(clip) / RollTime), 0.05f);
            rig.ShowWeapons(false);
            return true;
        }

        void StartBackDash()
        {
            state = FightState.BackDash;
            t = 0f;
            invul = BackDashInvul;
            FoeSideAtStart = FoeSide;
            if (!PlayAny(Mathf.Max(0.5f, rig.Length("backdash") / BackDashTime), 0.05f, true, "backdash"))
                PlayAny(1f, 0.05f, true, "walk_back", "guard");
            rig.ShowWeapons(false);
        }

        /// <summary>MAX mode for a stock: false if she has none, is in MAX already or in the air.</summary>
        bool TryMax()
        {
            if (Max || meter < Stock || Airborne)
                return false;
            meter -= Stock;
            maxTime = MaxDuration;
            game.OnMaxStart(this);
            return true;
        }

        /// <summary>A guard cancel while blocking, a stock each: A+B rolls out of the block, C+D strikes back with the blowback attack.</summary>
        bool TryGuardCancel(FighterInput input)
        {
            if (meter < Stock)
                return false;
            if ((input.ab || (input.a && input.b)) && (rig.Has("roll") || rig.Has("dash")))
            {
                meter -= Stock;
                StartRoll();
                game.OnGuardCancel(this, "roll");
                return true;
            }
            if (input.cd || (input.c && input.d))
            {
                var m = FindMove("CD", Stance.Stand);
                if (m == null)
                    return false;
                meter -= Stock;
                StartMove(m);
                guardCancel = true;
                invul = 0.3f;
                // out of the block's push-back into a short step in: the attacker who was blocked is right there to be hit
                slide = FoeSide * 2.5f;
                game.OnGuardCancel(this, "blowback");
                return true;
            }
            return false;
        }

        void StepAttack(float dt, FighterInput input)
        {
            bool air = move.stance == Stance.Air;
            float clipTime = t * move.speed;
            // two buttons pressed a hair apart: A+B is the roll, C+D the blowback attack, B+C MAX mode - the strike just started gives way
            if (pairWindow > 0f && !air && Grounded)
            {
                pairWindow -= dt;
                if (((pairButton == "A" && input.b) || (pairButton == "B" && input.a)) && StartRoll())
                    return;
                if (((pairButton == "C" && input.d) || (pairButton == "D" && input.c)) && StartMove(FindMove("CD", Stance.Stand)))
                    return;
                if (((pairButton == "B" && input.c) || (pairButton == "C" && input.b)) && TryMax())
                {
                    Enter(FightState.Idle);
                    return;
                }
            }
            if (air)
            {
                AirStep(dt);
                if (state != FightState.Attack)      // landed: the attack is over
                    return;
            }
            // a strike that steps in (its travel taken out of the clip): she moves along with it
            if (move.length > 0f && move.travel != 0f && !air)
            {
                float before = Mathf.Clamp(clipTime - dt * move.speed, 0f, move.length), now = Mathf.Min(clipTime, move.length);
                MoveAlong(facing * move.travel * ((now - before) / move.length));
            }
            // cancels: a strike that connected (or was blocked) gives way from its cancel point to another strike, the blowback
            // attack, a special or the super (the opponent stays held for it), or MAX mode
            if (connected && clipTime >= move.length * move.cancel && move.kind != "throw" && TryStart(input, cancel: true))
                return;
            if (clipTime >= move.length * move.recover)
            {
                if (air)
                {
                    state = FightState.Jump;
                    liftedOff = true;
                    PlayAny(1f, 0.12f, true, vh < 0f ? "fall" : "jump", "guard");
                }
                else
                    Enter(downHeld > 0 ? FightState.Crouch : FightState.Idle);
            }
        }

        /// <summary>
        /// World position of the striking bone while the strike can hit, else null; a strike of the hand holding a blade
        /// (RoeBlade) hits with the point along the blade nearest the opponent's body.  A throw's grab is tested by the fight.
        /// </summary>
        public Vector3? ActiveHit()
        {
            if (state != FightState.Attack || move == null || hitDone || move.kind == "throw")
                return null;
            float clipTime = t * move.speed;
            if (clipTime < move.hitStart || clipTime > move.hitEnd)
                return null;
            var blade = rig.Blade;
            if (blade != null && blade.hand == move.bone && foe != null)
                return blade.Nearest(foe.DistanceToBody);
            var bone = rig.Bone(move.bone);
            return bone != null ? bone.position : (Vector3?)null;
        }

        /// <summary>Her throw is grabbing right now (its active frames).</summary>
        public bool Grabbing => state == FightState.Attack && move != null && move.kind == "throw" && !hitDone &&
                                t * move.speed >= move.hitStart && t * move.speed <= move.hitEnd;

        /// <summary>The opponent's body as a vertical capsule (shorter crouching): distance from a point to it.</summary>
        public float DistanceToBody(Vector3 p)
        {
            float y = Mathf.Clamp(p.y, pos.y + 0.15f, pos.y + Top);
            var c = new Vector3(pos.x, y, pos.z);
            return Vector3.Distance(p, c);
        }

        /// <summary>
        /// A blow lands.  push: metres she slides back on the floor.  A launcher (or any hit while she is in the air) sends
        /// her flying - up and away - and she falls down at the end; a knockdown drops her where she stands.  KO in the air:
        /// she falls and stays down.
        /// </summary>
        public void TakeHit(int damage, float hitstun, float push, bool knockdown, bool launch = false, float launchUp = 0f,
                            float launchBack = 0f, bool lowHit = false)
        {
            hp = Mathf.Max(0, hp - damage);
            game.AddMeter(this, game.meterTaken);
            int away = -FoeSide;
            lastHitDir = game.axis * away;
            rig.ShowTimeline(null, 0f);
            rig.ShowWeapons(false);
            bool wasAir = Airborne, wasCrouching = Crouching;
            if (special != null)
                EndRanged();
            special = null;
            if (hp <= 0)
            {
                if (wasAir)
                {
                    ko = true;
                    state = FightState.Launched;
                    t = 0f;
                    vh = Mathf.Max(vh, 3.5f);
                    vu = away * 2.5f;
                    PlayAny(1f, 0.04f, true, "hit_air", "hurt");
                    return;
                }
                state = FightState.KO;
                t = 0f;
                vu = vh = 0f;
                rig.Play("die", 1f, 0.06f);
                return;
            }
            if (wasAir || launch)
            {
                state = FightState.Launched;
                t = 0f;
                liftedOff = true;
                juggleHits++;
                vh = launch ? launchUp : 4.0f;
                vu = away * (launch ? launchBack : 2.0f);
                if (!(launch && !wasAir && rig.Has("knocked_back") && PlayAny(1f, 0.04f, true, "knocked_back")))
                    PlayAny(1f, 0.04f, true, "hit_air", "hurt");
                return;
            }
            if (knockdown)
            {
                state = FightState.Down;
                t = 0f;
                downFor = dieLength + 0.8f;
                rig.Play("die", 1f, 0.06f);
                return;
            }
            state = FightState.Hurt;
            t = 0f;
            stun = hitstun;
            slide = away * push / 0.12f;
            hurtLow = wasCrouching;
            if (wasCrouching && rig.Has("hit_crouch"))
                rig.Play("hit_crouch", 1.2f, 0.04f);
            else if (lowHit && rig.Has("hit_low"))
                rig.Play("hit_low", 1.2f, 0.04f);
            else
                rig.Play("hurt", 1.2f, 0.04f);
        }

        /// <summary>Slid away from the opponent (an attacker whose blow landed on someone against the wall).</summary>
        public void PushBack(float metres)
        {
            if (Grounded)
                slide = -FoeSide * metres / 0.12f;
        }

        public void BlockHit(float blockstun, float push, bool low)
        {
            state = FightState.Block;
            t = 0f;
            stun = blockstun;
            slide = -FoeSide * push / 0.12f;
            blockLow = low;
            if (low)
                PlayAny(1f, 0.05f, true, "crouch_block_hit", "crouch_block", "crouch", "guard");
            else
                PlayAny(1f, 0.05f, true, "block_hit", "block", "guard");
        }

        /// <summary>Held where she is (a cancel into a special or the super from a blow that landed): until then at least.</summary>
        public void HoldFor(float seconds)
        {
            if (state == FightState.Hurt || state == FightState.Block)
                stun = Mathf.Max(stun, Mathf.Min(3f, seconds));
        }

        // ---- throws

        /// <summary>
        /// Her throw grabs the opponent: both play the pair of clips, the opponent held in front of her until the toss.  A
        /// back throw (back + C): she turns round and carries the opponent over to the other side first.
        /// </summary>
        public void Grab(Fighter vic)
        {
            hitDone = connected = true;
            state = FightState.Throw;
            t = 0f;
            throwHits.Clear();
            throwHits.Add(0.28f);
            throwHits.Add(ThrowToss);
            throwHitIndex = 0;
            if (throwBack)
            {
                facing = -facing;
                yaw = Yaw(Forward);
            }
            throwFromU = vic.U;
            PlayAny(1f, 0.05f, true, "throw_confirm", move.clip);
            vic.state = FightState.Thrown;
            vic.t = 0f;
            vic.vu = vic.vh = 0f;
            if (vic.special != null)
                vic.EndRanged();
            vic.special = null;
            vic.rig.ShowTimeline(null, 0f);
            vic.rig.ShowWeapons(false);
            vic.facing = -facing;
            vic.yaw = Yaw(vic.Forward);
            vic.PlayAny(1f, 0.05f, true, "throw_react", "hurt");
            HoldVictim(vic);
        }

        void HoldVictim(Fighter vic)
        {
            float target = U + facing * ThrowHold;
            vic.U = throwBack ? Mathf.Lerp(throwFromU, target, Mathf.SmoothStep(0f, 1f, t / ThrowCarry)) : target;
            vic.H = 0f;
            vic.Place();
        }

        void StepThrow(float dt)
        {
            var vic = foe;
            if (vic.state == FightState.Thrown)
                HoldVictim(vic);
            while (throwHitIndex < throwHits.Count && t >= throwHits[throwHitIndex])
            {
                bool toss = throwHitIndex == throwHits.Count - 1;
                int damage = Mathf.RoundToInt(move.damage * (toss ? 0.45f : 0.55f) * DamageScale);
                game.ThrowHit(this, vic, damage, toss);
                throwHitIndex++;
            }
            if (t >= ThrowRecover)
                Enter(FightState.Idle);
        }

        /// <summary>Tossed at the end of a throw: she flies away from the thrower and falls down.</summary>
        public void Tossed(int damage)
        {
            hp = Mathf.Max(0, hp - damage);
            int away = -FoeSide;
            lastHitDir = game.axis * away;
            if (hp <= 0)
                ko = true;
            state = FightState.Launched;
            t = 0f;
            liftedOff = true;
            juggleHits = JuggleLimit;      // nothing lands on her on the way down
            vh = 3.5f;
            vu = away * 3.5f;
            PlayAny(1f, 0.08f, true, "knocked_back", "hit_air", "hurt");
        }

        // ---- specials: the game's skills

        /// <summary>
        /// How close the opponent must be for a special to connect: a skill that slides up to her (the skill
        /// sheet has an "attack" move) as far as it may start at all; a ranged one (RangedSkill) as far as the two can be
        /// apart; the others only as far as their blows reach (FightGame.SpecialHit).  0 if the special is missing.
        /// </summary>
        public float SpecialReach(string name)
        {
            var s = game.specials.FirstOrDefault(x => x.name == name);
            var action = s != null && rig.sheet != null ? rig.sheet.Find(s.action) : null;
            if (action == null)
                return 0f;
            if (rig.Ranged(name) != null)
                return RangedReach(s);
            if (action.Dashes)
                return s.range;
            return Mathf.Min(s.range, FightGame.BlowReach(action, foe));
        }

        /// <summary>A ranged skill reaches as far as the two can be apart (thrown swords, pistols: the whole fight line).</summary>
        float RangedReach(Special s) => Mathf.Max(s.range, game.maxSeparation + 1f);

        /// <param name="cancel">it cancels a strike or a skill that landed: the opponent stays held until its first blow</param>
        bool StartSpecial(string name, bool cancel = false)
        {
            var s = game.specials.FirstOrDefault(x => x.name == name);
            if (s == null || !rig.Has(s.clip) || rig.sheet == null)
                return false;
            // the super in MAX mode costs no stock (it ends MAX: a MAX super)
            bool free = s.meterCost > 0f && Max;
            if (!free && meter < s.meterCost)
                return false;
            var action = rig.sheet.Find(s.action);
            if (action == null)
                return false;
            if (Dist > (rig.Ranged(name) != null ? RangedReach(s) : s.range))
                return false;
            if (special != null)
                EndRanged();
            maxSuper = free;
            if (free)
            {
                maxTime = 0f;
                game.OnMaxEnd(this);
            }
            else
                meter -= s.meterCost;
            special = s;
            state = FightState.Special;
            t = 0f;
            facing = FoeSide;
            yaw = Yaw(Forward);
            specialHome = pos;
            specialDir = Forward;
            var hits = action.Blows;
            specialHits = hits.Select(h => h.time).ToList();
            float total = hits.Sum(h => Mathf.Max(0f, h.ratio));
            specialRatios = hits.Select(h => total > 0f ? Mathf.Max(0f, h.ratio) / total : 1f / hits.Count).ToList();
            specialHitIndex = 0;
            specialOutcome = 0;
            float lastMove = action.moves.Count > 0 ? action.moves.Max(m => m.end) : 0f;
            specialEnd = specialHits.Count > 0
                ? Mathf.Min(action.BodyEnd, Mathf.Max(specialHits[specialHits.Count - 1] + SpecialTail, Mathf.Min(lastMove, specialHits[specialHits.Count - 1] + 1.5f)))
                : action.BodyEnd;
            BeginRanged(rig.Ranged(name));
            // a thrown weapon is back in her hand before she moves again
            if (ranged != null && ranged.IsThrow)
                specialEnd = Mathf.Min(action.BodyEnd, Mathf.Max(specialEnd, ranged.back + 0.1f));
            // a cancel: the opponent the strike (or skill) held stays held until this one's first blow
            if (cancel && specialHits.Count > 0)
                foe.HoldFor(specialHits[0] + 0.15f);
            var body = action.Body;
            rig.Play(s.clip, 1f, 0.05f, true, body != null ? body.clipIn : 0f);
            rig.ShowWeapons(true);
            game.OnSpecialStart(this, action);
            return true;
        }

        void StepSpecial(float dt, FighterInput input)
        {
            var action = rig.sheet.Find(special.action);
            // the super cancel: a skill that landed (or was blocked) gives way to the super - a stock, or in MAX mode none
            if (special.meterCost <= 0f && (specialOutcome == 1 || specialOutcome == 2) && SuperInput(input) && (Max || meter >= Stock))
            {
                var was = special;
                rig.ShowTimeline(null, 0f);
                EndRanged();
                special = null;
                if (StartSpecial("skill3", cancel: true))
                {
                    game.OnSuperCancel(this);
                    return;
                }
                special = was;      // (it could not start: the skill goes on)
                BeginRanged(rig.Ranged(was.name), keepState: true);
            }
            float end = specialEnd;
            // the game's staging: slide up to the opponent and back
            float advance = 0f;
            float gap = Vector3.Distance(specialHome, foe.pos);
            foreach (var m in action.moves.OrderBy(m => m.start))
            {
                float targetAdvance = m.kind == "attack" ? Mathf.Max(0f, gap - (foe.rig.sheet != null ? foe.rig.sheet.receiveRadius : 1f) - m.radius)
                                    : m.kind == "return" ? 0f : advance;
                if (t >= m.end)
                    advance = targetAdvance;
                else if (t > m.start)
                {
                    advance = Mathf.Lerp(advance, targetAdvance, Mathf.SmoothStep(0f, 1f, (t - m.start) / Mathf.Max(1e-4f, m.end - m.start)));
                    break;
                }
                else
                    break;
            }
            pos = specialHome + specialDir * advance;
            rig.ShowTimeline(special.action, t);

            if (ranged != null)
                StepRanged();
            else
                while (specialHitIndex < specialHits.Count && t >= specialHits[specialHitIndex])
                {
                    bool last = specialHitIndex == specialHits.Count - 1;
                    game.SpecialHit(this, special, BlowDamage(specialHitIndex), last);
                    specialHitIndex++;
                }
            if (t >= end)
            {
                rig.LingerTimeline(t);
                ranged = null;          // (the rig keeps the far end where it reached while the effects play out)
                special = null;
                maxSuper = false;
                state = FightState.Idle;
                t = 0f;
                // one who hovers in the game's clips (g05) may end a skill high in the air: she floats down, longer the higher she is
                float fade = rig.gameHover > 0f ? Mathf.Clamp(0.3f + 0.4f * rig.SoleHeight, 0.3f, 0.8f) : 0.3f;
                rig.Play("guard", 1f, fade, false);
                rig.ShowWeapons(false);
            }
        }

        /// <summary>Blow i of the running skill: its share of the skill's damage (MAX mode, a MAX super).</summary>
        int BlowDamage(int i) => Mathf.RoundToInt(special.damage * specialRatios[i] * DamageScale);

        // ---- ranged skills (RangedSkill): the weapon thrown, the bullets fired, the spot aimed at

        RangedSkill ranged;
        bool rangedLocked, rangedSpent, rangedImpact;
        float rangedAim, rangedSpot, impactAt, throwFarthest;
        float throwClosest = 99f, throwClosestHeight;      // (for the log: how near a thrown weapon that missed came)
        readonly List<bool> blowDone = new List<bool>();

        /// <summary>The thrown weapon is on its way (it can meet a bullet or the other's weapon).</summary>
        public bool ThrowInFlight => ranged != null && ranged.IsThrow && rangedLocked && !rangedSpent && !rangedImpact && t <= ranged.back;

        /// <summary>Where her thrown weapon is now (world), when it is in flight.</summary>
        public Vector3? ThrowPoint => ThrowInFlight ? rig.ThrownWeaponPoint(ranged) : null;

        /// <summary>
        /// For the CPU: the next blow her running ranged skill sends at the opponent - which blow, how long until it would land
        /// where the opponent stands now (a bullet: until it reaches her, fired or not; a thrown weapon: until it leaves her
        /// hand, 0 while it flies) and, for a placed skill, the spot once it is fixed; null when nothing more comes.
        /// </summary>
        public RangedThreat? RangedComing
        {
            get
            {
                var r = ranged;
                if (special == null || r == null || foe == null || specialOutcome == 3)
                    return null;
                if (r.IsThrow)
                    return rangedSpent || rangedImpact ? (RangedThreat?)null
                        : new RangedThreat { kind = r.kind, until = rangedLocked ? 0f : r.release - t };
                int next = blowDone.IndexOf(false);
                if (!r.IsShot)
                    return next < 0 ? (RangedThreat?)null
                        : new RangedThreat { kind = r.kind, blow = next, until = specialHits[next] - t, locked = rangedLocked, spot = rangedSpot };
                // bullets: the nearest one on its way to her, or the next one to be fired
                RangedThreat? best = null;
                float speed = Mathf.Max(1f, r.speed);
                foreach (var p in game.projectiles)
                    if (!p.dead && p.owner == this && Mathf.Sign(foe.U - p.u) == p.dir)
                    {
                        float until = Mathf.Abs(foe.U - p.u) / Mathf.Max(1f, p.speed);
                        if (best == null || until < best.Value.until)
                            best = new RangedThreat { kind = r.kind, blow = p.blow, until = until };
                    }
                if (next >= 0)
                {
                    float until = Mathf.Max(0.05f, specialHits[next] - r.anchorReach / speed) - t + Dist / speed;
                    if (best == null || until < best.Value.until)
                        best = new RangedThreat { kind = r.kind, blow = next, until = until };
                }
                return best;
            }
        }

        /// <summary>Her thrown weapon met a bullet or the other's weapon: it lands nothing more (it flies back as the game has it).</summary>
        public void SpendThrow()
        {
            if (ranged != null)
                rangedSpent = true;
        }

        void BeginRanged(RangedSkill r, bool keepState = false)
        {
            ranged = r;
            if (r == null)
                return;
            if (!keepState)
            {
                rangedLocked = rangedSpent = rangedImpact = false;
                throwFarthest = 0f;
                throwClosest = 99f;
                blowDone.Clear();
                for (int i = 0; i < specialHits.Count; i++)
                    blowDone.Add(false);
            }
        }

        /// <summary>The ranged skill is over (or broken off): the game's far effects and the weapon back where the game has them.</summary>
        void EndRanged()
        {
            if (ranged != null)
                rig.EndRemap();
            ranged = null;
        }

        void Land(int i)
        {
            blowDone[i] = true;
            bool last = i == specialHits.Count - 1;
            game.SpecialHit(this, special, BlowDamage(i), last, landed: true, blow: i);
        }

        void StepRanged()
        {
            var r = ranged;
            int n = specialHits.Count;
            if (r.IsThrow)
            {
                // the weapon leaves her hand: it flies to where the opponent stands now
                if (!rangedLocked && t >= r.release)
                {
                    rangedLocked = true;
                    rangedAim = Mathf.Clamp(Dist, 1f, game.maxSeparation + 0.5f);
                    rig.BeginRemap(r, rangedAim);
                }
                // its way out: from her hand to its farthest moment in the game's clip (a spinning weapon's bone swings to and fro on
                // the way, so the clip's timing says when it turns, not its bone)
                float outEnd = (r.farAt > r.release ? r.farAt : r.back) + 0.1f;
                if (rangedLocked && !rangedSpent && !rangedImpact && t <= outEnd)
                {
                    rig.PlaceThrownWeapon();
                    var bone = rig.ThrownWeaponBone(r);
                    var p = rig.ThrownWeaponPoint(r);
                    if (bone != null && p.HasValue)
                    {
                        float ahead = Vector3.Dot(bone.position - pos, Forward);
                        throwFarthest = Mathf.Max(throwFarthest, ahead);
                        float gapNow = Mathf.Abs(Vector3.Dot(p.Value - game.centre, game.axis) - foe.U);
                        if (gapNow < throwClosest)
                        {
                            throwClosest = gapNow;
                            throwClosestHeight = p.Value.y - game.centre.y - foe.H;
                        }
                        // it meets her body with its middle or its bone (a weapon at head height hits her head)
                        if (game.Meets(p.Value, foe, 0.45f, 0.35f) || game.Meets(bone.position, foe, 0.45f, 0.35f))
                        {
                            rangedImpact = true;
                            impactAt = t;
                            game.events.Add($"{game.frame / 60f:F2}s {rig.id} {special.name} thrown weapon meets {foe.rig.id} " +
                                            $"(its bone {ahead:F2} m out, aimed at {rangedAim:F2})");
                        }
                    }
                }
                if (rangedLocked && t > outEnd && !rangedImpact && !rangedSpent)
                {
                    rangedSpent = true;     // out and on its way back without having met her: a miss
                    game.events.Add($"{game.frame / 60f:F2}s {rig.id} {special.name} thrown weapon missed: nearest " +
                                    $"{throwClosest:F2} m along the line, {throwClosestHeight:F2} m above her feet (its bone out to {throwFarthest:F2} m, aimed at {rangedAim:F2})");
                }
                if (rangedImpact)
                    for (int i = 0; i < n; i++)
                        if (!blowDone[i] && t >= impactAt + (specialHits[i] - specialHits[0]))
                            Land(i);
            }
            else if (r.IsShot)
            {
                // one bullet a blow, fired so that it would reach the game's enemy line at the blow's moment
                for (int i = 0; i < n; i++)
                    if (!blowDone[i] && t >= Mathf.Max(0.05f, specialHits[i] - r.anchorReach / Mathf.Max(1f, r.speed)))
                    {
                        blowDone[i] = true;
                        game.Fire(this, special, r, i, BlowDamage(i), i == n - 1);
                    }
            }
            else
            {
                // the spot is fixed shortly before the first blow; each blow lands if she is still on it, on the ground
                if (!rangedLocked && t >= r.lockAt)
                {
                    rangedLocked = true;
                    rangedSpot = foe.U;
                    rangedAim = Mathf.Abs(rangedSpot - U);
                    rig.BeginRemap(r, rangedAim);
                }
                for (int i = 0; i < n; i++)
                    if (!blowDone[i] && t >= specialHits[i])
                    {
                        if (!rangedLocked)
                        {
                            rangedLocked = true;
                            rangedSpot = foe.U;
                        }
                        blowDone[i] = true;
                        if (Mathf.Abs(foe.U - rangedSpot) < 0.9f && foe.H < 0.5f && foe.Hittable)
                            Land(i);
                    }
            }
            // nothing landed and nothing more can: a miss (for the AI and the super cancel)
            if (specialOutcome == 0 && ((r.IsThrow && rangedSpent && !rangedImpact) || (!r.IsThrow && blowDone.All(b => b) && !r.IsShot)))
                specialOutcome = 3;
        }

        public void Place()
        {
            rig.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        }

        float locoSpeed;
        Vector3 locoDir;

        /// <summary>
        /// After the pose: a walking or running fighter moves by as much as its planted foot moved back under it
        /// in this step, so that foot stays put on the floor (the body surges and slows within each
        /// step, as walking does).  Along the direction of travel only, at most 2.5 times the set speed;
        /// with no foot planted (crossfades) at the set speed.
        /// </summary>
        public void AfterPose(float dt)
        {
            if ((state != FightState.Walk && state != FightState.Run) || dt <= 0f)
                return;
            float nominal = locoSpeed * dt;
            float along = rig.HasContact ? Vector3.Dot(-rig.transform.TransformVector(rig.ContactDelta), locoDir) : nominal;
            MoveAlong(Vector3.Dot(locoDir, game.axis) * Mathf.Clamp(along, 0f, 2.5f * nominal));
            Bound();
            Place();
        }

        public void Finish(bool won)
        {
            // KO'd: she stays down - or, knocked out in the air, falls first (Land takes her to KO)
            if (state == FightState.KO || (state == FightState.Launched && ko))
                return;
            if (state == FightState.Special)
            {
                rig.ShowTimeline(null, 0f);
                EndRanged();
                special = null;
            }
            if (Airborne)
            {
                H = 0f;
                vu = vh = 0f;
                liftedOff = false;
            }
            rig.Airborne = false;
            Enter(won ? FightState.Win : FightState.Lose);
        }
    }
}
