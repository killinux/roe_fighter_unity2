using System.Linq;
using UnityEngine;

namespace RoeFighter.Fight
{
    /// <summary>
    /// A simple CPU opponent.  It thinks every 0.1-0.25 s (a human-like reaction time), keeps to
    /// striking range, blocks or side-steps some of the strikes it sees coming, mixes the four
    /// strikes by distance and uses its specials now and then, the super when its gauge is full.
    /// Answers in screen terms like a player's pad.
    /// </summary>
    public class FightAI
    {
        readonly System.Random rng;
        float think;
        int fwd, up;
        string button;
        float holdFor;

        public FightAI(int seed)
        {
            rng = new System.Random(seed);
        }

        float R() => (float)rng.NextDouble();

        public FighterInput Decide(Fighter me, float dt, Vector3 camRight)
        {
            var input = new FighterInput();
            var toFoe = me.foe.pos - me.pos;
            toFoe.y = 0f;
            float dist = toFoe.magnitude;
            int screenSign = Vector3.Dot(toFoe, camRight) >= 0f ? 1 : -1;
            think -= dt;
            holdFor -= dt;
            if (think <= 0f)
            {
                think = 0.1f + R() * 0.15f;
                button = null;
                var foe = me.foe;
                bool threatened = (foe.state == FightState.Attack && foe.move != null && dist < foe.move.reach + 0.9f)
                               || (foe.state == FightState.Special && foe.t < 0.6f);
                if (me.Neutral || me.state == FightState.Block)
                {
                    if (threatened && R() < 0.55f)
                    {
                        fwd = -1;           // block
                        up = 0;
                        holdFor = 0.35f;
                    }
                    else if (threatened && R() < 0.25f)
                    {
                        fwd = 0;            // step out of the line
                        up = R() < 0.5f ? 1 : -1;
                        holdFor = 0.3f;
                    }
                    else if (holdFor <= 0f)
                    {
                        fwd = 0;
                        up = 0;
                        // a skill that does not slide up to her (the supers, skill 2) is used only where its blows reach
                        if (me.meter >= 100f && dist < me.SpecialReach("skill3") - 0.3f && R() < 0.25f)
                            button = "S3";
                        else if (dist < 4.5f && R() < 0.06f)
                        {
                            string pick = R() < 0.5f ? "S1" : "S2";
                            if (dist < me.SpecialReach(pick == "S1" ? "skill1" : "skill2") - 0.3f)
                                button = pick;
                        }
                        else if (dist < 1.25f && R() < 0.75f)
                        {
                            // in range: pick a strike that reaches
                            float r = R();
                            button = dist < 0.95f ? (r < 0.4f ? "A" : r < 0.65f ? "B" : r < 0.85f ? "C" : "D")
                                                  : (r < 0.45f ? "D" : r < 0.8f ? "C" : "B");
                        }
                        else if (dist > 1.1f)
                        {
                            fwd = 1;
                            if (R() < 0.12f)
                            {
                                fwd = 0;
                                up = R() < 0.5f ? 1 : -1;
                                holdFor = 0.4f;
                            }
                        }
                        else if (R() < 0.3f)
                        {
                            fwd = -1;
                            holdFor = 0.4f;
                        }
                    }
                }
                else if (me.state == FightState.Attack && me.connected && R() < 0.5f)
                {
                    float r = R();
                    button = r < 0.5f ? "C" : "D";      // follow a connecting strike
                }
            }
            input.x = fwd * screenSign;
            input.y = up;
            input.a = button == "A";
            input.b = button == "B";
            input.c = button == "C";
            input.d = button == "D";
            input.s1 = button == "S1";
            input.s2 = button == "S2";
            input.s3 = button == "S3";
            if (button != null)
                button = null;      // a press, not a hold
            return input;
        }
    }

    /// <summary>
    /// Keyboard and pad.  Player 1: A/D left/right, W/S step in/out of the screen, J K U I = A B C D,
    /// 1 2 3 (the number row, as ROE's three skill buttons - user 10-11 "战斗按钮123，用luf在ROE里的三个招数") or
    /// L O P = specials 1 2 3 (or 236/214/236236 + A or C).  Player 2: arrow keys, keypad 1 2 4 5 =
    /// A B C D, keypad 3 6 9 = specials.  A pad works for player 1 (stick or d-pad, buttons 0-3,
    /// shoulders 4/5 = specials 1/2, 6 = super).
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
                i.s1 = Input.GetKeyDown(KeyCode.Keypad3);
                i.s2 = Input.GetKeyDown(KeyCode.Keypad6);
                i.s3 = Input.GetKeyDown(KeyCode.Keypad9);
            }
            return i;
        }
    }
}
