using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RoeFighter.Fight
{
    /// <summary>
    /// What one fighter does, step by step: the state machine of a 3D fighting game.
    ///   neutral   stand in guard, walk forward / back, step to the side (circling the opponent);
    ///             holding back blocks.  Always turned towards the opponent.
    ///   strikes   A jab, B slash, C straight, D kick (motion capture); a strike that connects or is
    ///             blocked can be followed by another from its cancel point on.
    ///   specials  the game's own skills: 236+A/C or the shortcut keys; 236236+A/C (or the third
    ///             shortcut) is the super, it costs a full gauge.  The skill sheet says when the
    ///             fighter slides up to the opponent and back and when the hits land.
    ///   reactions hurt, block, knocked down (the game's fall and lying clips), getting up, KO.
    /// The fight (FightGame) does the hit checks, the round, the camera and the inputs.
    /// </summary>
    public class Fighter
    {
        public FighterRig rig;
        public FightGame game;
        public Fighter foe;
        public int index;
        public Vector3 pos;
        public float yaw;
        public int maxHp = 1000, hp = 1000;
        public float meter;
        public FightState state = FightState.Intro;
        public float t;                      // seconds in the current state
        public Move move;
        public Special special;
        public bool hitDone;                 // the current strike already connected or was blocked
        public bool connected;               // ... and it was not a whiff (for cancels)
        public float stun;                   // hurt / block time left
        public int fwdHeld;                  // last forward/back input: +1 towards the opponent
        public int comboHits;
        Vector3 slideVel;
        Vector3 specialHome, specialDir;
        float specialStartYaw;
        int specialHitIndex;
        List<float> specialHits = new List<float>();
        List<float> specialRatios = new List<float>();
        public int specialOutcome;           // 0 not decided yet, 1 hits, 2 blocked, 3 missed
        public Vector3 lastHitDir;           // the way the last blow that hit her went (flat)
        public List<Move> moves;             // her strikes: her own (FighterRig.strikePack) or the fight's (FightGame.moves)
        readonly List<int> history = new List<int>();     // numpad directions, newest last
        float dieLength, downFor;
        float getupLength;                   // a get-up clip of her own ("getup": Fiona's, from UFE 2); 0: fade back to the guard

        public const float BodyRadius = 0.32f;
        // the walk clips play at the rate that keeps a planted foot still (FighterRig.StrideSpeed)
        public const float WalkSpeed = 1.15f, BackSpeed = 0.95f, SideSpeed = 1.5f;

        public Vector3 Forward => Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        public bool Neutral => state == FightState.Idle || state == FightState.Walk || state == FightState.Side;
        public bool Hittable => state != FightState.Down && state != FightState.Getup && state != FightState.KO &&
                                state != FightState.Intro && state != FightState.Win && state != FightState.Lose;
        public bool CanBlock => (Neutral || state == FightState.Block) && fwdHeld < 0;
        public float SpecialRemaining => special != null ? specialEnd - t : 0f;
        // the game's skills go on for 1-5 s after their last hit (a pose, the way back): over here
        // the body is free this long after the last hit, while the skill's effects play out
        public const float SpecialTail = 0.8f;
        float specialEnd;

        public void Reset(Vector3 position, float facing)
        {
            pos = position;
            yaw = facing;
            hp = maxHp;
            state = FightState.Intro;
            t = 0f;
            stun = 0f;
            slideVel = Vector3.zero;
            move = null;
            special = null;
            history.Clear();
            rig.ShowTimeline(null, 0f);
            rig.ShowWeapons(true);
            rig.Play(rig.Has("react_02") ? "react_02" : "guard", 1f, 0f);
            dieLength = rig.Length("die");
            getupLength = rig.Has("getup") ? rig.Length("getup") : 0f;
            Place();
            rig.ResetCloth();
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
                case FightState.Win:
                    rig.Play(rig.Has("idle_02") ? "idle_02" : "react_01", 1f, 0.3f);
                    break;
                case FightState.Lose:
                    rig.Play("hurt", 0.6f, 0.3f);
                    break;
            }
            rig.ShowWeapons(s == FightState.Special || s == FightState.Intro || s == FightState.Win);
        }

        /// <summary>Numpad direction from forward (+1 towards the opponent) and up (+1 into the screen).</summary>
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

        /// <param name="fwd">+1 towards the opponent, -1 away</param>
        /// <param name="sideDir">world direction of a side step (zero for none)</param>
        public void Step(float dt, int fwd, int up, Vector3 sideDir, FighterInput input)
        {
            t += dt;
            var sideVelocity = Vector3.zero;
            fwdHeld = fwd;
            history.Add(Direction(fwd, up));
            if (history.Count > 40)
                history.RemoveAt(0);

            // sliding back from a hit
            if (slideVel.sqrMagnitude > 1e-6f)
            {
                pos += slideVel * dt;
                slideVel = Vector3.MoveTowards(slideVel, Vector3.zero, 9f * dt);
            }

            var toFoe = foe.pos - pos;
            toFoe.y = 0f;
            float foeYaw = toFoe.sqrMagnitude > 1e-6f ? Mathf.Atan2(toFoe.x, toFoe.z) * Mathf.Rad2Deg : yaw;

            switch (state)
            {
                case FightState.Intro:
                case FightState.Win:
                case FightState.Lose:
                    break;

                case FightState.Idle:
                case FightState.Walk:
                case FightState.Side:
                    yaw = Mathf.MoveTowardsAngle(yaw, foeYaw, 720f * dt);
                    if (TryStart(input))
                        break;
                    if (sideDir.sqrMagnitude > 0.1f && fwd >= 0)
                    {
                        state = FightState.Side;
                        // the guard above, the legs step by themselves (FighterRig.SideStep)
                        rig.Play("guard", 1f, 0.12f, false);
                        sideVelocity = sideDir.normalized * SideSpeed;
                        pos += sideVelocity * dt;
                    }
                    else if (fwd != 0)
                    {
                        state = FightState.Walk;
                        // played at the rate at which a planted foot keeps pace with the body; the body
                        // itself moves after the pose, by what the planted foot pushed (AfterPose)
                        string clip = fwd > 0 ? "walk" : "walk_back";
                        var pack = rig.motions;
                        walkSpeed = fwd > 0 ? (pack != null && pack.walkSpeed > 0f ? pack.walkSpeed : WalkSpeed)
                                            : (pack != null && pack.backSpeed > 0f ? pack.backSpeed : BackSpeed);
                        walkDir = toFoe.normalized * fwd;
                        rig.Play(clip, walkSpeed / rig.StrideSpeed(clip), 0.15f, false);
                    }
                    else if (state != FightState.Idle)
                    {
                        Enter(FightState.Idle);
                    }
                    break;

                case FightState.Attack:
                {
                    float clipTime = t * move.speed;
                    // a strike that steps in (its travel taken out of the clip): she moves along with it
                    if (move.length > 0f && (move.travel != 0f || move.travelSide != 0f))
                    {
                        float before = Mathf.Clamp(clipTime - dt * move.speed, 0f, move.length), now = Mathf.Min(clipTime, move.length);
                        var right = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
                        pos += (Forward * move.travel + right * move.travelSide) * ((now - before) / move.length);
                    }
                    if (connected && clipTime >= move.length * move.cancel && TryStart(input, strikesOnly: true))
                        break;
                    if (clipTime >= move.length * move.recover)
                        Enter(FightState.Idle);
                    break;
                }

                case FightState.Block:
                case FightState.Hurt:
                    yaw = Mathf.MoveTowardsAngle(yaw, foeYaw, 360f * dt);
                    stun -= dt;
                    if (stun <= 0f)
                        Enter(FightState.Idle);
                    break;

                case FightState.Down:
                    if (t >= dieLength && rig.Current == "die")
                        rig.Play("rip", 1f, 0.2f);
                    if (t >= downFor)
                    {
                        state = FightState.Getup;
                        t = 0f;
                        if (getupLength > 0f)
                            rig.Play("getup", 1f, 0.2f);
                        else
                            rig.Play("guard", 1f, 0.45f);
                    }
                    break;

                case FightState.Getup:
                    if (t >= (getupLength > 0f ? getupLength : 0.5f))
                        Enter(FightState.Idle);
                    break;

                case FightState.KO:
                    if (t >= dieLength && rig.Current == "die")
                        rig.Play("rip", 1f, 0.2f);
                    break;

                case FightState.Special:
                    StepSpecial(dt);
                    break;
            }
            rig.SideStep(state == FightState.Side ? sideVelocity : Vector3.zero);
            rig.LockFeet(state == FightState.Walk);
            Place();
        }

        bool TryStart(FighterInput input, bool strikesOnly = false)
        {
            if (!strikesOnly)
            {
                bool punch = input.a || input.c;
                bool super = input.s3 || (punch && Motion(2, 3, 6, 2, 3, 6));
                if (super && meter >= 100f && StartSpecial("skill3"))
                    return true;
                if ((input.s1 || (punch && Motion(2, 3, 6))) && StartSpecial("skill1"))
                    return true;
                if ((input.s2 || (punch && Motion(2, 1, 4))) && StartSpecial("skill2"))
                    return true;
            }
            string button = input.a ? "A" : input.b ? "B" : input.c ? "C" : input.d ? "D" : null;
            if (button == null)
                return false;
            var m = (moves ?? game.moves).FirstOrDefault(x => x.button == button);
            if (m == null || !rig.Has(m.clip))
                return false;
            move = m;
            hitDone = connected = false;
            state = FightState.Attack;
            t = 0f;
            // strikes lock the facing: a side step can make them miss
            var toFoe = foe.pos - pos;
            if (toFoe.sqrMagnitude > 1e-6f)
                yaw = Mathf.Atan2(toFoe.x, toFoe.z) * Mathf.Rad2Deg;
            rig.Play(m.clip, m.speed, 0.06f);
            rig.ShowWeapons(false);
            return true;
        }

        /// <summary>
        /// World position of the striking bone while the strike can hit, else null; a strike of the hand holding a blade
        /// (RoeBlade) hits with the point along the blade nearest the opponent's body.
        /// </summary>
        public Vector3? ActiveHit()
        {
            if (state != FightState.Attack || move == null || hitDone)
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

        /// <summary>The opponent's body as a vertical capsule: distance from a point to it.</summary>
        public float DistanceToBody(Vector3 p)
        {
            float y = Mathf.Clamp(p.y, pos.y + 0.15f, pos.y + 1.6f);
            var c = new Vector3(pos.x, y, pos.z);
            return Vector3.Distance(p, c);
        }

        public void TakeHit(int damage, float hitstun, Vector3 push, bool knockdown)
        {
            hp = Mathf.Max(0, hp - damage);
            meter = Mathf.Min(100f, meter + 3f);
            slideVel = push;
            var away = push.sqrMagnitude > 1e-6f ? push : pos - foe.pos;
            away.y = 0f;
            lastHitDir = away.sqrMagnitude > 1e-6f ? away.normalized : -Forward;
            rig.ShowTimeline(null, 0f);
            if (hp <= 0)
            {
                state = FightState.KO;
                t = 0f;
                rig.Play("die", 1f, 0.06f);
                rig.ShowWeapons(false);
                return;
            }
            if (knockdown)
            {
                state = FightState.Down;
                t = 0f;
                downFor = dieLength + 0.8f;
                rig.Play("die", 1f, 0.06f);
                rig.ShowWeapons(false);
                return;
            }
            state = FightState.Hurt;
            t = 0f;
            stun = hitstun;
            rig.Play("hurt", 1.2f, 0.04f);
            rig.ShowWeapons(false);
        }

        public void BlockHit(float blockstun, Vector3 push)
        {
            state = FightState.Block;
            t = 0f;
            stun = blockstun;
            slideVel = push;
            rig.Play("guard", 1f, 0.05f);
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
            if (action.Dashes)
                return s.range;
            return Mathf.Min(s.range, FightGame.BlowReach(action, foe));
        }

        bool StartSpecial(string name)
        {
            var s = game.specials.FirstOrDefault(x => x.name == name);
            if (s == null || !rig.Has(s.clip) || rig.sheet == null || meter < s.meterCost)
                return false;
            var action = rig.sheet.Find(s.action);
            if (action == null)
                return false;
            float dist = Vector3.Distance(pos, foe.pos);
            if (dist > s.range)
                return false;
            meter -= s.meterCost;
            special = s;
            state = FightState.Special;
            t = 0f;
            specialHome = pos;
            var toFoe = foe.pos - pos;
            toFoe.y = 0f;
            specialDir = toFoe.sqrMagnitude > 1e-6f ? toFoe.normalized : Forward;
            specialStartYaw = Mathf.Atan2(specialDir.x, specialDir.z) * Mathf.Rad2Deg;
            yaw = specialStartYaw;
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

        float walkSpeed;
        Vector3 walkDir;

        /// <summary>
        /// After the pose: a walking fighter moves by as much as its planted foot moved back under it
        /// in this step, so that foot stays put on the floor (the body surges and slows within each
        /// step, as walking does).  Along the walking direction only, at most 2.5 times the set speed;
        /// with no foot planted (crossfades) at the set speed.
        /// </summary>
        public void AfterPose(float dt)
        {
            if (state != FightState.Walk || dt <= 0f)
                return;
            float nominal = walkSpeed * dt;
            float along = rig.HasContact ? Vector3.Dot(-rig.transform.TransformVector(rig.ContactDelta), walkDir) : nominal;
            pos += walkDir * Mathf.Clamp(along, 0f, 2.5f * nominal);
            Place();
        }

        public void Finish(bool won)
        {
            if (state == FightState.KO)
                return;
            if (state == FightState.Special)
                rig.ShowTimeline(null, 0f);
            Enter(won ? FightState.Win : FightState.Lose);
        }
    }
}
