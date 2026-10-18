using UnityEngine;
using UnityEngine.UI;

namespace RoeFighter.Fight
{
    /// <summary>
    /// Health bars, timer, the teams (a face per member under each bar, the beaten ones darkened) or the
    /// round marks of a single match, the power gauge with its stocks, and the big messages, built in
    /// code with uGUI.  Screen-space overlay in play mode; when the editor films a match the canvas is
    /// drawn by the fight camera (screen-space camera), so it ends up in the pictures.  On the select
    /// screen (FightGame.Phase.Select) a card per fighter of the roster instead, each side's team listed
    /// in order, the two highlighted standing on the stage.
    /// </summary>
    public class FightHud : MonoBehaviour
    {
        public bool drawnByCamera;
        public bool showHelp = true;

        const int MaxTeam = 5, MaxStocks = 3;

        Canvas canvas;
        RectTransform[] hpFill = new RectTransform[2], hpLag = new RectTransform[2], meterFill = new RectTransform[2];
        Image[] meterImage = new Image[2];
        Text[] names = new Text[2], meterText = new Text[2];
        Image[,] roundMarks = new Image[2, 3];
        Image[,] teamFrame = new Image[2, MaxTeam], teamShade = new Image[2, MaxTeam], stockBox = new Image[2, MaxStocks];
        RawImage[,] teamFace = new RawImage[2, MaxTeam];
        Text timer, message, help, notice;
        float[] lag = { 1f, 1f };
        Font font;
        Transform layer;                    // where Box and Label put what they make
        GameObject fightLayer, selectLayer;

        // the select screen
        Image[] cardFrame;
        Text[] cardTag;
        Text selectTitle, selectWho, selectHelp;
        readonly Text[] pickName = new Text[2], pickOutfit = new Text[2], teamList = new Text[2];

        static readonly Color Gold = new Color(1f, 0.82f, 0.25f);
        static readonly Color HpColor = new Color(1f, 0.78f, 0.2f);
        static readonly Color LagColor = new Color(0.85f, 0.15f, 0.12f);
        static readonly Color Dark = new Color(0.05f, 0.05f, 0.08f, 0.75f);
        static readonly Color MeterColor = new Color(0.25f, 0.65f, 1f);
        static readonly Color[] SideColor = { new Color(0.95f, 0.27f, 0.22f), new Color(0.27f, 0.58f, 1f) };

        public void Build(FightGame game)
        {
            var old = new System.Collections.Generic.List<GameObject>();
            foreach (Transform child in transform)
                old.Add(child.gameObject);
            foreach (var go in old)
                DestroyAny(go);
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            canvas = gameObject.GetComponent<Canvas>();
            if (canvas == null)
                canvas = gameObject.AddComponent<Canvas>();
            if (drawnByCamera && game.cam != null)
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = game.cam;
                canvas.planeDistance = 0.5f;
            }
            else
            {
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            }
            var scaler = gameObject.GetComponent<CanvasScaler>();
            if (scaler == null)
                scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 1f;

            fightLayer = Layer("fight");
            selectLayer = Layer("select");
            layer = fightLayer.transform;
            for (int i = 0; i < 2; i++)
            {
                bool left = i == 0;
                // health bar
                var frame = Box($"hp{i}", Dark, left ? new Vector2(0.03f, 0.905f) : new Vector2(0.555f, 0.905f),
                                left ? new Vector2(0.445f, 0.945f) : new Vector2(0.97f, 0.945f));
                hpLag[i] = Box("lag", LagColor, Vector2.zero, Vector2.one, frame).rectTransform;
                hpFill[i] = Box("fill", HpColor, Vector2.zero, Vector2.one, frame).rectTransform;
                Inset(hpLag[i]);
                Inset(hpFill[i]);
                names[i] = Label($"name{i}", game.rigs[i] != null ? game.rigs[i].displayName : "", 34, left ? TextAnchor.LowerLeft : TextAnchor.LowerRight,
                                 left ? new Vector2(0.03f, 0.948f) : new Vector2(0.555f, 0.948f),
                                 left ? new Vector2(0.445f, 0.995f) : new Vector2(0.97f, 0.995f));
                // a single match: its round marks
                for (int r = 0; r < 3; r++)
                {
                    float x0 = left ? 0.405f - r * 0.02f : 0.58f + r * 0.02f;
                    roundMarks[i, r] = Box($"round{i}{r}", Dark, new Vector2(x0, 0.875f), new Vector2(x0 + 0.014f, 0.897f));
                }
                // a team battle: a face per member, in fighting order from the outside in
                for (int k = 0; k < MaxTeam; k++)
                {
                    float x0 = left ? 0.03f + k * 0.052f : 0.97f - (k + 1) * 0.052f + 0.006f;
                    var slot = Box($"team{i}{k}", Dark, new Vector2(x0, 0.835f), new Vector2(x0 + 0.046f, 0.9f));
                    teamFrame[i, k] = slot;
                    var inner = Box("inner", new Color(0.12f, 0.12f, 0.15f, 1f), Vector2.zero, Vector2.one, slot);
                    Inset(inner.rectTransform, 3f);
                    var go = new GameObject("face", typeof(RectTransform), typeof(RawImage));
                    go.transform.SetParent(inner.transform, false);
                    var rt = (RectTransform)go.transform;
                    rt.anchorMin = Vector2.zero;
                    rt.anchorMax = Vector2.one;
                    rt.offsetMin = rt.offsetMax = Vector2.zero;
                    teamFace[i, k] = go.GetComponent<RawImage>();
                    teamFace[i, k].raycastTarget = false;
                    teamShade[i, k] = Box("shade", new Color(0f, 0f, 0f, 0.7f), Vector2.zero, Vector2.one, inner);
                }
                // power gauge: the stock being filled as a bar, the stocks held as marks over it
                var gauge = Box($"meter{i}", Dark, left ? new Vector2(0.03f, 0.045f) : new Vector2(0.72f, 0.045f),
                                left ? new Vector2(0.28f, 0.07f) : new Vector2(0.97f, 0.07f));
                var fill = Box("fill", MeterColor, Vector2.zero, Vector2.one, gauge);
                meterImage[i] = fill;
                meterFill[i] = fill.rectTransform;
                Inset(meterFill[i]);
                for (int s = 0; s < MaxStocks; s++)
                {
                    float x0 = left ? 0.03f + s * 0.03f : 0.97f - (s + 1) * 0.03f + 0.005f;
                    stockBox[i, s] = Box($"stock{i}{s}", Dark, new Vector2(x0, 0.074f), new Vector2(x0 + 0.025f, 0.098f));
                }
                meterText[i] = Label($"meterText{i}", "", 26, left ? TextAnchor.LowerLeft : TextAnchor.LowerRight,
                                     left ? new Vector2(0.125f, 0.072f) : new Vector2(0.72f, 0.072f),
                                     left ? new Vector2(0.28f, 0.11f) : new Vector2(0.875f, 0.11f));
            }
            timer = Label("timer", "60", 64, TextAnchor.MiddleCenter, new Vector2(0.455f, 0.875f), new Vector2(0.545f, 0.985f));
            help = Label("help", "1P: A/D move  W jump (tap: hop)  S crouch  back = block  J K U I = A B C D  H = A+B roll  Y = C+D blowback  1 2 3 = skills (236 / 214 / 236236 + J or U)   F1/F2: CPU   F3: motions   F4: cloth   F5: skirt   F6: clothes burst   F7: select   F8: team / single",
                         18, TextAnchor.LowerCenter, new Vector2(0.1f, 0.0f), new Vector2(0.9f, 0.04f));
            help.gameObject.SetActive(showHelp && !drawnByCamera);
            BuildSelect(game);
            // over both screens
            layer = transform;
            message = Label("message", "", 120, TextAnchor.MiddleCenter, new Vector2(0.03f, 0.4f), new Vector2(0.97f, 0.62f));
            message.color = Gold;
            // long names ("GODDESS LUF WINS THE MATCH") get smaller instead of running off the screen
            message.horizontalOverflow = HorizontalWrapMode.Wrap;
            message.resizeTextForBestFit = true;
            message.resizeTextMinSize = 40;
            message.resizeTextMaxSize = 120;
            notice = Label("notice", "", 22, TextAnchor.UpperCenter, new Vector2(0.1f, 0.63f), new Vector2(0.9f, 0.83f));
            notice.horizontalOverflow = HorizontalWrapMode.Wrap;
            notice.verticalOverflow = VerticalWrapMode.Truncate;
            notice.resizeTextForBestFit = true;
            notice.resizeTextMinSize = 10;
            notice.resizeTextMaxSize = 22;
            Refresh(game);
        }

        /// <summary>A card per fighter along the bottom, the teams by name on either side, whose turn it is at the top.</summary>
        void BuildSelect(FightGame game)
        {
            layer = selectLayer.transform;
            int n = game.roster != null ? game.roster.Length : 0;
            cardFrame = new Image[n];
            cardTag = new Text[n];
            // the cards a row fits on the screen holds 13 of; more get smaller, all of a card alike
            float fit = Mathf.Min(1f, 13f / Mathf.Max(1, n));
            float w = 0.085f * fit, gap = 0.014f * fit, y0 = 0.075f, y1 = y0 + 0.24f * fit;
            float x = 0.5f - (n * w + (n - 1) * gap) * 0.5f;
            for (int k = 0; k < n; k++, x += w + gap)
            {
                var rig = game.roster[k];
                var frame = Box($"card{k}", Dark, new Vector2(x - 0.004f, y0 - 0.007f), new Vector2(x + w + 0.004f, y1 + 0.007f));
                cardFrame[k] = frame;
                var inner = Box("inner", new Color(0.12f, 0.12f, 0.15f, 1f), Vector2.zero, Vector2.one, frame);
                Inset(inner.rectTransform, 5f);
                if (rig != null && rig.portrait != null)
                {
                    var go = new GameObject("portrait", typeof(RectTransform), typeof(RawImage));
                    go.transform.SetParent(inner.transform, false);
                    var rt = (RectTransform)go.transform;
                    rt.anchorMin = Vector2.zero;
                    rt.anchorMax = Vector2.one;
                    rt.offsetMin = rt.offsetMax = Vector2.zero;
                    var img = go.GetComponent<RawImage>();
                    img.texture = rig.portrait;
                    img.raycastTarget = false;
                }
                var band = Box("band", new Color(0f, 0f, 0f, 0.6f), new Vector2(0f, 0f), new Vector2(1f, 0.2f), inner);
                var name = Label($"cardName{k}", rig != null ? rig.displayName : "?", 22, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one);
                name.transform.SetParent(band.transform, false);
                // a long name shrinks into its card, on two lines if that is bigger
                name.horizontalOverflow = HorizontalWrapMode.Wrap;
                name.verticalOverflow = VerticalWrapMode.Truncate;
                name.resizeTextForBestFit = true;
                name.resizeTextMinSize = 10;
                name.resizeTextMaxSize = 22;
                name.lineSpacing = 0.85f;
                cardTag[k] = Label($"cardTag{k}", "", 26, TextAnchor.LowerCenter, new Vector2(x - 0.02f, y1 + 0.01f), new Vector2(x + w + 0.02f, y1 + 0.06f));
                cardTag[k].resizeTextForBestFit = true;
                cardTag[k].resizeTextMinSize = 10;
                cardTag[k].resizeTextMaxSize = 26;
            }
            for (int i = 0; i < 2; i++)
            {
                bool left = i == 0;
                pickName[i] = Label($"pick{i}", "", 64, left ? TextAnchor.LowerLeft : TextAnchor.LowerRight,
                                    left ? new Vector2(0.04f, 0.42f) : new Vector2(0.56f, 0.42f), left ? new Vector2(0.44f, 0.52f) : new Vector2(0.96f, 0.52f));
                pickName[i].color = SideColor[i];
                pickOutfit[i] = Label($"outfit{i}", "", 28, left ? TextAnchor.UpperLeft : TextAnchor.UpperRight,
                                      left ? new Vector2(0.04f, 0.37f) : new Vector2(0.56f, 0.37f), left ? new Vector2(0.44f, 0.42f) : new Vector2(0.96f, 0.42f));
                teamList[i] = Label($"team{i}", "", 26, left ? TextAnchor.UpperLeft : TextAnchor.UpperRight,
                                    left ? new Vector2(0.04f, 0.74f) : new Vector2(0.56f, 0.74f), left ? new Vector2(0.44f, 0.86f) : new Vector2(0.96f, 0.86f));
                teamList[i].color = SideColor[i];
            }
            selectTitle = Label("selectTitle", "CHARACTER SELECT", 64, TextAnchor.MiddleCenter, new Vector2(0.1f, 0.88f), new Vector2(0.9f, 0.97f));
            selectTitle.color = Gold;
            selectWho = Label("selectWho", "", 30, TextAnchor.MiddleCenter, new Vector2(0.1f, 0.83f), new Vector2(0.9f, 0.88f));
            selectHelp = Label("selectHelp", "1P: A / D choose   J add to the team   K take back        2P: ← / → choose   keypad 1 add   keypad 2 back        F1 / F2: CPU on / off   F8: team / single",
                               22, TextAnchor.LowerCenter, new Vector2(0.1f, 0.005f), new Vector2(0.9f, 0.045f));
            selectHelp.gameObject.SetActive(showHelp && !drawnByCamera);
        }

        GameObject Layer(string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return go;
        }

        static void Inset(RectTransform r, float by = 3f)
        {
            r.offsetMin = new Vector2(by, by);
            r.offsetMax = new Vector2(-by, -by);
        }

        Image Box(string name, Color color, Vector2 min, Vector2 max, Image parent = null)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent != null ? parent.transform : layer != null ? layer : transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        Text Label(string name, string text, int size, TextAnchor anchor, Vector2 min, Vector2 max)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text), typeof(Outline));
            go.transform.SetParent(layer != null ? layer : transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var t = go.GetComponent<Text>();
            t.font = font;
            t.fontSize = size;
            t.fontStyle = FontStyle.Bold;
            t.alignment = anchor;
            t.color = Color.white;
            t.text = text;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            var o = go.GetComponent<Outline>();
            o.effectColor = new Color(0f, 0f, 0f, 0.85f);
            o.effectDistance = new Vector2(2f, -2f);
            return t;
        }

        public void Refresh(FightGame game)
        {
            if (timer == null || game.f[0] == null)
                return;
            bool selecting = game.phase == FightGame.Phase.Select;
            if (fightLayer != null && fightLayer.activeSelf == selecting)
                fightLayer.SetActive(!selecting);
            if (selectLayer != null && selectLayer.activeSelf != selecting)
                selectLayer.SetActive(selecting);
            message.text = game.message;
            if (notice != null)
            {
                // in the match one setting a line under the timer; on the select screen one line along the top edge (below
                // it: the title and whose turn it is); the type shrinks to fit
                notice.text = selecting ? game.Notice.Replace("\n", "    ") : game.Notice;
                var rt = notice.rectTransform;
                rt.anchorMin = selecting ? new Vector2(0.05f, 0.962f) : new Vector2(0.1f, 0.63f);
                rt.anchorMax = selecting ? new Vector2(0.95f, 0.998f) : new Vector2(0.9f, 0.83f);
                notice.alignment = selecting ? TextAnchor.MiddleCenter : TextAnchor.UpperCenter;
            }
            if (selecting)
            {
                RefreshSelect(game);
                return;
            }
            bool teams = game.TeamMode;
            for (int i = 0; i < 2; i++)
            {
                var x = game.f[i];
                if (x.rig != null)
                    names[i].text = x.rig.displayName;
                float hp = Mathf.Clamp01(x.hp / (float)x.maxHp);
                lag[i] = Mathf.Max(hp, Mathf.MoveTowards(lag[i], hp, 0.012f));
                SetFill(hpFill[i], hp, i == 0);
                SetFill(hpLag[i], lag[i], i == 0);
                // the gauge: the stock being filled, and the stocks held
                int stocks = x.Stocks;
                float part = stocks >= MaxStocks ? 1f : Mathf.Clamp01((x.meter - stocks * Fighter.Stock) / Fighter.Stock);
                SetFill(meterFill[i], part, i == 0);
                meterImage[i].color = stocks >= MaxStocks ? Gold : MeterColor;
                for (int s = 0; s < MaxStocks; s++)
                    stockBox[i, s].color = s < stocks ? Gold : Dark;
                meterText[i].text = stocks >= MaxStocks ? "MAX" : stocks > 0 ? $"x{stocks}" : "";
                for (int r = 0; r < 3; r++)
                {
                    roundMarks[i, r].gameObject.SetActive(!teams && r < game.roundsToWin);
                    roundMarks[i, r].color = r < game.wins[i] ? Gold : Dark;
                }
                for (int k = 0; k < MaxTeam; k++)
                {
                    bool shown = teams && k < game.teamSize && k < game.team[i].Count;
                    if (teamFrame[i, k].gameObject.activeSelf != shown)
                        teamFrame[i, k].gameObject.SetActive(shown);
                    if (!shown)
                        continue;
                    int idx = game.team[i][k];
                    var rig = game.roster != null && idx >= 0 && idx < game.roster.Length ? game.roster[idx] : null;
                    teamFace[i, k].texture = rig != null ? rig.portrait : null;
                    teamFace[i, k].enabled = rig != null && rig.portrait != null;
                    bool beaten = k < game.current[i];
                    teamShade[i, k].gameObject.SetActive(beaten);
                    teamFrame[i, k].color = k == game.current[i] ? SideColor[i] : Dark;
                }
            }
            timer.text = Mathf.CeilToInt(game.timer).ToString();
        }

        void RefreshSelect(FightGame game)
        {
            if (cardFrame == null)
                return;
            bool alone = game.cpu[0] != game.cpu[1];
            for (int k = 0; k < cardFrame.Length; k++)
            {
                bool p1 = game.pick[0] == k, p2 = game.pick[1] == k;
                bool in1 = game.team[0].Contains(k), in2 = game.team[1].Contains(k);
                float blink = 0.6f + 0.4f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 6f));
                Color c = Dark;
                if (p1 && p2)
                    c = Color.Lerp(SideColor[0], SideColor[1], Mathf.PingPong(Time.unscaledTime * 2f, 1f));
                else if (p1)
                    c = SideColor[0] * (game.picked[0] ? 1f : blink);
                else if (p2)
                    c = SideColor[1] * (game.picked[1] ? 1f : blink);
                else if (in1 && in2)
                    c = Color.Lerp(SideColor[0], SideColor[1], 0.5f) * 0.6f;
                else if (in1)
                    c = SideColor[0] * 0.6f;
                else if (in2)
                    c = SideColor[1] * 0.6f;
                c.a = 1f;
                cardFrame[k].color = c;
                string Tag(int side) => game.cpu[side] ? "CPU" : side == 0 ? "1P" : "2P";
                string Order(int side) => in1 && side == 0 ? $" {game.team[0].IndexOf(k) + 1}" : in2 && side == 1 ? $" {game.team[1].IndexOf(k) + 1}" : "";
                string tag = p1 && p2 ? $"{Tag(0)}  {Tag(1)}" : p1 ? Tag(0) + Order(0) : p2 ? Tag(1) + Order(1)
                           : in1 && in2 ? $"{Order(0).Trim()} / {Order(1).Trim()}" : in1 ? Order(0).Trim() : in2 ? Order(1).Trim() : "";
                cardTag[k].text = tag;
                cardTag[k].color = (p1 || in1) && !(p2 || in2) ? SideColor[0] : (p2 || in2) && !(p1 || in1) ? SideColor[1] : Color.white;
            }
            for (int i = 0; i < 2; i++)
            {
                var rig = game.rigs[i];
                pickName[i].text = rig != null ? rig.displayName : "";
                pickOutfit[i].text = rig != null ? $"{rig.outfit}  ({rig.id})" + (game.picked[i] ? "   OK" : "") : "";
                if (game.TeamMode)
                {
                    var parts = new System.Collections.Generic.List<string>();
                    for (int k = 0; k < game.teamSize; k++)
                    {
                        string who = k < game.team[i].Count && game.roster != null && game.team[i][k] < game.roster.Length ? game.roster[game.team[i][k]].displayName : "?";
                        parts.Add($"{k + 1}. {who}");
                    }
                    teamList[i].text = string.Join("\n", parts);
                }
                else
                    teamList[i].text = "";
            }
            if (game.picked[0] && game.picked[1])
                selectWho.text = "READY";
            else if (alone)
                selectWho.text = game.choosing == (game.cpu[0] ? 0 : 1) ? (game.TeamMode ? "CHOOSE THE COMPUTER'S TEAM" : "CHOOSE THE COMPUTER'S FIGHTER")
                                                                        : (game.TeamMode ? $"CHOOSE YOUR TEAM ({game.team[game.choosing].Count + 1} OF {game.teamSize})" : "CHOOSE YOUR FIGHTER");
            else if (game.cpu[0] && game.cpu[1])
                selectWho.text = "COMPUTER AGAINST COMPUTER";
            else
                selectWho.text = game.TeamMode ? "1P AND 2P: CHOOSE YOUR TEAMS" : "1P AND 2P: CHOOSE";
        }

        static void SetFill(RectTransform r, float amount, bool fromLeft)
        {
            r.anchorMin = new Vector2(fromLeft ? 0f : 1f - amount, 0f);
            r.anchorMax = new Vector2(fromLeft ? amount : 1f, 1f);
        }

        static void DestroyAny(GameObject go)
        {
            if (Application.isPlaying)
                Destroy(go);
            else
                DestroyImmediate(go);
        }
    }
}
