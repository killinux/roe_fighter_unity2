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
    ///             another from its cancel point on.  C+D is the blowback attack (knocks the opponent flying),
    ///             A+B the roll (through strikes and through the opponent), forward or back + C up close a throw.
    ///   specials  the game's own skills: 236+A/C or the shortcut keys; 236236+A/C (or the third shortcut) is
    ///             the super, it costs one stock of the gauge.  The skill sheet says when the fighter slides up
    ///             to the opponent and back and when the hits land.
    ///   reactions hurt (standing, crouching, low), block, launched into the air (juggles, at most JuggleLimit
    ///             hits), knocked down (the game's fall and lying clips), getting up, thrown, KO.
    /// The fight (FightGame) does the hit checks, the round, the camera and the inputs.
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
        public int fwdHeld, downHeld, upHeld;    // the stick this step: +1 towards the opponent / -1 away; down; up
        public int comboHits;
        public int juggleHits;               // hits taken in the air since she left the floor
        public bool ko;                      // KO'd in the air: she stays down when she lands
        public bool atWall;                  // against the edge of the stage
        float slide;                         // pushback along the axis (m/s, signed along the axis)
        float vu, vh;                        // airborne velocity along the axis and up (m/s)
        float jumpAlong;                     // the jump's travel along the axis (m/s, signed), set at the squat
        bool jumpSuper, jumpFromRun, liftedOff;
        bool blockLow, hurtLow;
        string pairButton;                   // a strike just started by this button may still become A+B or C+D
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

        public const float BodyRadius = 0.32f;
        // the walk clips play at the rate that keeps a planted foot still (FighterRig.StrideSpeed)
        public const float WalkSpeed = 1.25f, BackSpeed = 1.0f, RunSpeed = 3.4f;
        public const float Gravity = 15f;
        public const float JumpUp = 6.6f, HopUp = 4.4f, SuperJumpUp = 7.7f;          // m/s up at take-off
        public const float JumpAlong = 2.6f, HopAlong = 2.3f, SuperAlong = 3.4f, HyperHopAlong = 3.6f, RunJumpExtra = 0.8f;
        public const float JumpSquat = 4f / 60f, LandLock = 0.1f, TapWindow = 0.25f;
        public const float BackDashTime = 0.36f, BackDashLength = 1.6f, BackDashInvul = 0.1f;
        public const float RollTime = 0.5f, RollLength = 2.6f, RollInvul = 0.38f;
        public const int JuggleLimit = 2;
        public const float ThrowRange = 1.05f, ThrowHold = 0.85f, ThrowToss = 0.97f, ThrowRecover = 1.3f;
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
                                state != FightState.Throw && state != FightState.Thrown &&
                                !(state == FightState.Roll && t < RollInvul) && !(state == FightState.BackDash && t < BackDashInvul) &&
                                !(state == FightState.Launched && juggleHits >= JuggleLimit);
        /// <summary>Holding back on the floor: a strike is blocked - standing or crouching, as the stick says (Blocks).</summary>
        public bool CanBlock => Grounded && fwdHeld < 0 && (Neutral || state == FightState.Block || state == FightState.Land);
        /// <summary>Whether a strike she can block is stopped by the block she holds: overheads only standing, lows only crouching.</summary>
        public bool Blocks(Move m) => m.Overhead ? downHeld <= 0 : !m.Low || downHeld > 0;
        /// <summary>On the floor and not in the middle of something: a throw grabs her.</summary>
        public bool Throwable => Grounded && (Neutral || state == FightState.Land || (state == FightState.Block && stun < 0.05f));
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
            slide = 0f;
            vu = vh = 0f;
            move = null;
            special = null;
            juggleHits = 0;
            ko = false;
            comboHits = 0;
            liftedOff = false;
            pairWindow = 0f;
            tapFwd = tapBack = 99f;
            lastFwd = 0;
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

        /// <summary>Full health, empty gauge: a fresh fighter.</summary>
        public void Revive()
        {
            hp = maxHp;
            meter = 0f;
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

        /// <param name="fwd">+1 towards the opponent, -1 away</param>
        /// <param name="up">+1 up (jump), -1 down (crouch)</param>
        public void Step(float dt, int fwd, int up, FighterInput input)
        {
            t += dt;
            fwdHeld = fwd;
            downHeld = up < 0 ? 1 : 0;
            upHeld = up > 0 ? 1 : 0;
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
                    StepSpecial(dt);
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

        bool TryStart(FighterInput input, bool strikesOnly = false)
        {
            bool air = Airborne;
            if (!strikesOnly && !air)
            {
                bool punch = input.a || input.c;
                bool super = input.s3 || (punch && Motion(2, 3, 6, 2, 3, 6));
                if (super && meter >= Stock && StartSpecial("skill3"))
                    return true;
                if ((input.s1 || (punch && Motion(2, 3, 6))) && StartSpecial("skill1"))
                    return true;
                if ((input.s2 || (punch && Motion(2, 1, 4))) && StartSpecial("skill2"))
                    return true;
                if ((input.ab || (input.a && input.b)) && StartRoll())
                    return true;
                if ((input.cd || (input.c && input.d)) && StartMove(FindMove("CD", Stance.Stand)))
                    return true;
                // a throw: forward or back + C, up close, the opponent on her feet
                if (input.c && fwdHeld != 0 && Dist < ThrowRange && foe.Throwable && downHeld <= 0 && state != FightState.Crouch &&
                    StartMove(FindMove("T", Stance.Stand)))
                    return true;
            }
            string button = input.a ? "A" : input.b ? "B" : input.c ? "C" : input.d ? "D" : null;
            if (button == null)
                return false;
            var stance = air ? Stance.Air : (downHeld > 0 || state == FightState.Crouch) ? Stance.Crouch : Stance.Stand;
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
            if (!rig.Has("dash") || Airborne)
                return false;
            state = FightState.Roll;
            t = 0f;
            FoeSideAtStart = FoeSide;
            facing = FoeSide;
            yaw = Yaw(Forward);
            rig.Play("dash", Mathf.Max(0.5f, rig.Length("dash") / RollTime), 0.05f);
            rig.ShowWeapons(false);
            return true;
        }

        void StartBackDash()
        {
            state = FightState.BackDash;
            t = 0f;
            FoeSideAtStart = FoeSide;
            if (!PlayAny(Mathf.Max(0.5f, rig.Length("backdash") / BackDashTime), 0.05f, true, "backdash"))
                PlayAny(1f, 0.05f, true, "walk_back", "guard");
            rig.ShowWeapons(false);
        }

        void StepAttack(float dt, FighterInput input)
        {
            bool air = move.stance == Stance.Air;
            float clipTime = t * move.speed;
            // two buttons pressed a hair apart: A+B is the roll, C+D the blowback attack - the strike just started gives way
            if (pairWindow > 0f && !air && Grounded)
            {
                pairWindow -= dt;
                if (((pairButton == "A" && input.b) || (pairButton == "B" && input.a)) && StartRoll())
                    return;
                if (((pairButton == "C" && input.d) || (pairButton == "D" && input.c)) && StartMove(FindMove("CD", Stance.Stand)))
                    return;
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
            if (connected && clipTime >= move.length * move.cancel && TryStart(input, strikesOnly: true))
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
            float y = Mathf.Clamp(p.y, pos.y + 0.15f, pos.y + (Crouching ? CrouchTop : StandTop));
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
            meter = Mathf.Min(MeterMax, meter + 3f);
            int away = -FoeSide;
            lastHitDir = game.axis * away;
            rig.ShowTimeline(null, 0f);
            rig.ShowWeapons(false);
            bool wasAir = Airborne, wasCrouching = Crouching;
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

        // ---- throws

        /// <summary>Her throw grabs the opponent: both play the pair of clips, the opponent held in front of her until the toss.</summary>
        public void Grab(Fighter vic)
        {
            hitDone = connected = true;
            state = FightState.Throw;
            t = 0f;
            throwHits.Clear();
            throwHits.Add(0.28f);
            throwHits.Add(ThrowToss);
            throwHitIndex = 0;
            PlayAny(1f, 0.05f, true, "throw_confirm", move.clip);
            vic.state = FightState.Thrown;
            vic.t = 0f;
            vic.vu = vic.vh = 0f;
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
            vic.U = U + facing * ThrowHold;
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
                int damage = Mathf.RoundToInt(move.damage * (toss ? 0.45f : 0.55f));
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
        /// sheet has an "attack" move) as far as it may start at all; the others only as far as their blows
        /// reach (FightGame.SpecialHit).  0 if the special is missing.
        /// </summary>
        public float SpecialReach(string name)
        {
            var s = game.specials.FirstOrDefault(x => x.name == name);
            var action = s != null && rig.sheet != null ? rig.sheet.Find(s.action) : null;
            if (action == null)
                return 0f;
            if (rig.IsRanged(name))
                return RangedReach(s);
            if (action.Dashes)
                return s.range;
            return Mathf.Min(s.range, FightGame.BlowReach(action, foe));
        }

        /// <summary>A ranged skill reaches as far as the two can be apart (thrown swords, pistols: the whole fight line).</summary>
        float RangedReach(Special s) => Mathf.Max(s.range, game.maxSeparation + 1f);

        bool StartSpecial(string name)
        {
            var s = game.specials.FirstOrDefault(x => x.name == name);
            if (s == null || !rig.Has(s.clip) || rig.sheet == null || meter < s.meterCost)
                return false;
            var action = rig.sheet.Find(s.action);
            if (action == null)
                return false;
            if (Dist > (rig.IsRanged(name) ? RangedReach(s) : s.range))
                return false;
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
            var body = action.Body;
            rig.Play(s.clip, 1f, 0.05f, true, body != null ? body.clipIn : 0f);
            rig.ShowWeapons(true);
            game.OnSpecialStart(this, action);
            return true;
        }

        void StepSpecial(float dt)
        {
            var action = rig.sheet.Find(special.action);
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

            while (specialHitIndex < specialHits.Count && t >= specialHits[specialHitIndex])
            {
                bool last = specialHitIndex == specialHits.Count - 1;
                int damage = Mathf.RoundToInt(special.damage * specialRatios[specialHitIndex]);
                game.SpecialHit(this, special, damage, last);
                specialHitIndex++;
            }
            if (t >= end)
            {
                rig.LingerTimeline(t);
                special = null;
                state = FightState.Idle;
                t = 0f;
                // one who hovers in the game's clips (g05) may end a skill high in the air: she floats down, longer the higher she is
                float fade = rig.gameHover > 0f ? Mathf.Clamp(0.3f + 0.4f * rig.SoleHeight, 0.3f, 0.8f) : 0.3f;
                rig.Play("guard", 1f, fade, false);
                rig.ShowWeapons(false);
            }
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
                rig.ShowTimeline(null, 0f);
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
