using System.Collections.Generic;
using UnityEngine;

namespace RoeFighter.Fight
{
    /// <summary>
    /// A simple CPU opponent for the side-view fight.  It thinks every 0.1-0.25 s (a human-like reaction
    /// time), keeps to striking range, blocks (crouching against lows) or rolls or hops away from strikes
    /// it sees coming, jumps over or blocks what is thrown or shot at it, answers a jump-in with a standing
    /// heavy, mixes standing and crouching strikes by distance, throws up close, jumps in or runs in from
    /// afar, fires its ranged skills from a distance and uses its specials now and then; a strike that
    /// connects it may cancel into a special, a skill that lands into the super; with stocks it goes MAX
    /// now and then and may guard-cancel out of a block.  Longer actions (a run, a jump-in) are small
    /// plans played out step by step.  Answers in screen terms like a player's pad.
    /// </summary>
    public class FightAI
    {
        struct Planned
        {
            public int steps, fwd, up;
            public string button;     // pressed on the first step of this part
        }

        readonly System.Random rng;
        readonly List<Planned> plan = new List<Planned>();
        float think;
        int fwd, up;
        string button;
        float holdFor;
        bool airPressed, cancelTried, superTried, guardCancelTried;

        public FightAI(int seed)
        {
            rng = new System.Random(seed);
        }

        float R() => (float)rng.NextDouble();

        void Plan(int steps, int fwd, int up, string button = null) => plan.Add(new Planned { steps = steps, fwd = fwd, up = up, button = button });

        /// <summary>The shortcut of a skill of hers that is ranged (FighterRig.ranged), or null.</summary>
        static string RangedSkill(Fighter me) => me.rig.IsRanged("skill2") ? "S2" : me.rig.IsRanged("skill1") ? "S1" : null;

        public FighterInput Decide(Fighter me, float dt, Vector3 camRight)
        {
            var input = new FighterInput();
            var foe = me.foe;
            float dist = me.Dist;
            int screenSign = Vector3.Dot(me.game.axis * me.FoeSide, camRight) >= 0f ? 1 : -1;
            think -= dt;
            holdFor -= dt;
            button = null;
            if (!me.Airborne)
                airPressed = false;
            if (me.state != FightState.Attack)
                cancelTried = false;
            if (me.state != FightState.Special)
                superTried = false;
            if (me.state != FightState.Block)
                guardCancelTried = false;
            if (plan.Count > 0)
            {
                // a plan in progress: its current part
                var p = plan[0];
                fwd = p.fwd;
                up = p.up;
                button = p.button;
                p.button = null;
                p.steps--;
                if (p.steps <= 0)
                    plan.RemoveAt(0);
                else
                    plan[0] = p;
                think = 0.05f;
            }
            else if (me.state == FightState.Attack && me.connected && !cancelTried && me.move != null && me.move.stance != Stance.Air)
            {
                // a strike that connected: now and then cancelled into a special or the super (they hold her for it)
                cancelTried = true;
                float r = R();
                if (me.Stocks >= 1 && r < 0.12f && dist < me.SpecialReach("skill3") - 0.3f)
                    button = "S3";
                else if (r < 0.35f)
                {
                    string pick = R() < 0.5f ? "S1" : "S2";
                    if (dist < me.SpecialReach(pick == "S1" ? "skill1" : "skill2") - 0.3f)
                        button = pick;
                }
                else if (r < 0.6f)
                    button = R() < 0.5f ? "C" : "D";      // or another strike
                else if (r < 0.66f && me.Stocks >= 1 && !me.Max)
                    button = "MAX";                       // MAX cancel
            }
            else if (me.state == FightState.Special && !superTried && (me.specialOutcome == 1 || me.specialOutcome == 2) &&
                     me.special != null && me.special.meterCost <= 0f)
            {
                // a skill that landed: the super cancel, now and then
                superTried = true;
                if ((me.Max || me.Stocks >= 1) && R() < 0.4f)
                    button = "S3";
            }
            else if (me.state == FightState.Block && !guardCancelTried && me.Stocks >= 1)
            {
                guardCancelTried = true;
                float r = R();
                if (r < 0.1f)
                    button = "AB";
                else if (r < 0.2f)
                    button = "CD";
                if (button != null)
                {
                    fwd = -1;
                    up = 0;
                }
            }
            else if (think <= 0f)
            {
                think = 0.1f + R() * 0.15f;
                bool threatened = (foe.state == FightState.Attack && foe.move != null && dist < foe.move.reach + 0.9f)
                               || (foe.state == FightState.Special && foe.t < 0.6f && !foe.rig.IsRanged(foe.special != null ? foe.special.name : ""))
                               || (foe.Airborne && foe.state != FightState.Launched && dist < 2.2f && foe.H > 0.5f);
                float incoming = me.game.IncomingThreat(me);
                if (me.Airborne)
                {
                    // in the air: one attack when the opponent is in reach
                    fwd = 0;
                    up = 0;
                    if (!airPressed && dist < 1.6f && foe.state != FightState.Down && R() < 0.7f)
                    {
                        button = R() < 0.5f ? "C" : "D";
                        airPressed = true;
                    }
                }
                else if (me.Neutral || me.state == FightState.Land)
                {
                    if (incoming >= 0f && incoming < 3.5f)
                    {
                        // something thrown or shot her way: jump over it, block it, or roll through it
                        float r = R();
                        if (r < 0.4f)
                        {
                            Plan(6, 0, 1);
                            Plan(30, 0, 0);
                        }
                        else if (r < 0.85f)
                        {
                            fwd = -1;
                            up = 0;
                            holdFor = 0.5f;
                        }
                        else if (me.rig.Has("roll") || me.rig.Has("dash"))
                            button = "AB";
                    }
                    else if (threatened)
                    {
                        float r = R();
                        bool lowComing = foe.move != null && foe.move.Low;
                        if (foe.Airborne && dist < 1.9f && r < 0.45f)
                            button = R() < 0.6f ? "C" : "D";                 // anti-air: a standing heavy
                        else if (r < 0.6f)
                        {
                            fwd = -1;                                        // block; crouch against lows (and sometimes anyway)
                            up = lowComing || R() < 0.35f ? -1 : 0;
                            holdFor = 0.35f;
                        }
                        else if (r < 0.72f && dist < 1.8f && (me.rig.Has("roll") || me.rig.Has("dash")))
                            button = "AB";                                   // roll through it
                        else if (r < 0.82f)
                        {
                            // back-dash: two taps back
                            Plan(2, -1, 0);
                            Plan(2, 0, 0);
                            Plan(3, -1, 0);
                        }
                        else if (r < 0.9f)
                        {
                            Plan(2, -1, 1);                                  // hop back
                            Plan(12, -1, 0);
                        }
                        else
                        {
                            fwd = 0;
                            up = 0;
                        }
                    }
                    else if (foe.state == FightState.Down || foe.state == FightState.Getup || foe.state == FightState.Launched)
                    {
                        // she is down: keep a step away, ready
                        fwd = dist > 1.9f ? 1 : dist < 1.3f ? -1 : 0;
                        up = 0;
                        if (foe.state == FightState.Launched && dist < 1.5f && foe.juggleHits < Fighter.JuggleLimit && R() < 0.3f)
                            button = "C";                                    // a juggle
                    }
                    else if (holdFor <= 0f)
                    {
                        fwd = 0;
                        up = 0;
                        float r = R();
                        // a skill that does not slide up to her (the supers, skill 2) is used only where its blows reach
                        // MAX: with two stocks now and then, with one when she is close (a round rarely fills more than one; the
                        // super in MAX is free) - often enough to come before the super below spends the stock
                        if (!me.Max && ((me.Stocks >= 2 && R() < 0.06f) || (me.Stocks >= 1 && dist < 2.5f && R() < 0.08f)))
                            button = "MAX";
                        else if ((me.Stocks >= 1 || me.Max) && dist < me.SpecialReach("skill3") - 0.3f && R() < (me.Max ? 0.3f : 0.15f))
                            button = "S3";
                        else if (dist > 2.2f && R() < 0.05f && RangedSkill(me) is string ranged)
                            button = ranged;                                 // a ranged skill (thrown weapon, pistols) from afar
                        else if (dist < 3.6f && R() < 0.035f)
                        {
                            string pick = R() < 0.5f ? "S1" : "S2";
                            if (dist < me.SpecialReach(pick == "S1" ? "skill1" : "skill2") - 0.3f)
                                button = pick;
                        }
                        else if (dist < 1.0f && foe.Throwable && R() < 0.14f)
                        {
                            fwd = R() < 0.7f ? 1 : -1;                       // a throw: forward (or back) + C
                            button = "C";
                        }
                        else if (dist < 1.3f && r < 0.72f)
                        {
                            float k = R();
                            if (k < 0.3f)
                            {
                                up = -1;                                     // a crouching strike (D sweeps)
                                holdFor = 0.2f;
                                float s = R();
                                button = s < 0.4f ? "A" : s < 0.7f ? "B" : "D";
                            }
                            else if (k < 0.38f)
                                button = "CD";                               // the blowback attack
                            else
                            {
                                float s = R();
                                button = dist < 0.95f ? (s < 0.4f ? "A" : s < 0.65f ? "B" : s < 0.85f ? "C" : "D")
                                                      : (s < 0.45f ? "D" : s < 0.8f ? "C" : "B");
                            }
                        }
                        else if (dist > 1.4f && dist < 3.0f && r < 0.12f)
                        {
                            // a jump-in: forward and up (held past the take-off: a full jump), the strike on the way down
                            Plan(6, 1, 1);
                            Plan(20, 1, 0);
                            Plan(1, 1, 0, R() < 0.5f ? "C" : "D");
                            Plan(20, 0, 0);
                        }
                        else if (dist > 1.4f && dist < 2.6f && r < 0.2f)
                        {
                            Plan(2, 1, 1);                                   // a hop in (up let go at once)
                            Plan(14, 1, 0);
                            Plan(1, 1, 0, "B");
                            Plan(16, 0, 0);
                        }
                        else if (dist > 2.6f && r < 0.4f)
                        {
                            Plan(1, 1, 0);                                   // a run: forward, let go, forward
                            Plan(1, 0, 0);
                            Plan(Mathf.Clamp(Mathf.RoundToInt((dist - 1.3f) / Fighter.RunSpeed * 60f), 6, 60), 1, 0);
                        }
                        else if (dist > 1.15f)
                            fwd = 1;
                        else if (R() < 0.3f)
                        {
                            fwd = -1;
                            holdFor = 0.4f;
                        }
                    }
                }
                else if (me.state == FightState.Crouch && holdFor <= 0f)
                    up = 0;
            }
            input.x = fwd * screenSign;
            input.y = up;
            input.a = button == "A";
            input.b = button == "B";
            input.c = button == "C";
            input.d = button == "D";
            input.ab = button == "AB";
            input.cd = button == "CD";
            input.max = button == "MAX";
            input.s1 = button == "S1";
            input.s2 = button == "S2";
            input.s3 = button == "S3";
            button = null;      // a press, not a hold
            return input;
        }
    }

    /// <summary>
    /// Keyboard and pad.  Player 1: A/D left/right, W jump, S crouch, J K U I = A B C D, H = A+B (the roll), Y = C+D
    /// (the blowback attack), G = B+C (MAX mode), 1 2 3 (the number row, as ROE's three skill buttons) or L O P =
    /// specials 1 2 3 (or 236/214/236236 + A or C).  Player 2: arrow keys, keypad 1 2 4 5 = A B C D, keypad 7 = A+B,
    /// keypad 8 = C+D, keypad 0 = MAX, keypad 3 6 9 = specials.  A pad works for player 1 (stick or d-pad, buttons
    /// 0-3, shoulders 4/5 = specials 1/2, 6 = super, 7 = A+B, 8 = MAX).
    /// </summary>
    public static class PlayerInput
    {
        public static FighterInput Read(int player)
        {
            var i = new FighterInput();
            if (player == 0)
            {
                i.x = (Input.GetKey(KeyCode.D) ? 1 : 0) - (Input.GetKey(KeyCode.A) ? 1 : 0);
                i.y = (Input.GetKey(KeyCode.W) ? 1 : 0) - (Input.GetKey(KeyCode.S) ? 1 : 0);
                float h = Input.GetAxisRaw("Horizontal"), v = Input.GetAxisRaw("Vertical");
                if (i.x == 0 && Mathf.Abs(h) > 0.5f && !Input.GetKey(KeyCode.LeftArrow) && !Input.GetKey(KeyCode.RightArrow))
                    i.x = h > 0f ? 1 : -1;
                if (i.y == 0 && Mathf.Abs(v) > 0.5f && !Input.GetKey(KeyCode.UpArrow) && !Input.GetKey(KeyCode.DownArrow))
                    i.y = v > 0f ? 1 : -1;
                i.a = Input.GetKeyDown(KeyCode.J) || Input.GetKeyDown(KeyCode.JoystickButton0);
                i.b = Input.GetKeyDown(KeyCode.K) || Input.GetKeyDown(KeyCode.JoystickButton1);
                i.c = Input.GetKeyDown(KeyCode.U) || Input.GetKeyDown(KeyCode.JoystickButton2);
                i.d = Input.GetKeyDown(KeyCode.I) || Input.GetKeyDown(KeyCode.JoystickButton3);
                i.ab = Input.GetKeyDown(KeyCode.H) || Input.GetKeyDown(KeyCode.JoystickButton7);
                i.cd = Input.GetKeyDown(KeyCode.Y);
                i.max = Input.GetKeyDown(KeyCode.G) || Input.GetKeyDown(KeyCode.JoystickButton8);
                i.s1 = Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.L) || Input.GetKeyDown(KeyCode.JoystickButton4);
                i.s2 = Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.O) || Input.GetKeyDown(KeyCode.JoystickButton5);
                i.s3 = Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.P) || Input.GetKeyDown(KeyCode.JoystickButton6);
            }
            else
            {
                i.x = (Input.GetKey(KeyCode.RightArrow) ? 1 : 0) - (Input.GetKey(KeyCode.LeftArrow) ? 1 : 0);
                i.y = (Input.GetKey(KeyCode.UpArrow) ? 1 : 0) - (Input.GetKey(KeyCode.DownArrow) ? 1 : 0);
                i.a = Input.GetKeyDown(KeyCode.Keypad1);
                i.b = Input.GetKeyDown(KeyCode.Keypad2);
                i.c = Input.GetKeyDown(KeyCode.Keypad4);
                i.d = Input.GetKeyDown(KeyCode.Keypad5);
                i.ab = Input.GetKeyDown(KeyCode.Keypad7);
                i.cd = Input.GetKeyDown(KeyCode.Keypad8);
                i.max = Input.GetKeyDown(KeyCode.Keypad0);
                i.s1 = Input.GetKeyDown(KeyCode.Keypad3);
                i.s2 = Input.GetKeyDown(KeyCode.Keypad6);
                i.s3 = Input.GetKeyDown(KeyCode.Keypad9);
            }
            return i;
        }
    }
}
