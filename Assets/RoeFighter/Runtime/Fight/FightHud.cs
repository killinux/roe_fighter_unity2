using UnityEngine;
using UnityEngine.UI;

namespace RoeFighter.Fight
{
    /// <summary>
    /// Health bars, timer, round marks, special gauges and the big messages, built in code with
    /// uGUI.  Screen-space overlay in play mode; when the editor films a match the canvas is drawn
    /// by the fight camera (screen-space camera), so it ends up in the pictures.  On the select screen
    /// (FightGame.Phase.Select) a card per fighter of the roster instead, the two picked standing on the stage.
    /// </summary>
    public class FightHud : MonoBehaviour
    {
        public bool drawnByCamera;
        public bool showHelp = true;

        Canvas canvas;
        RectTransform[] hpFill = new RectTransform[2], hpLag = new RectTransform[2], meterFill = new RectTransform[2];
        Image[] meterImage = new Image[2];
        Text[] names = new Text[2], meterText = new Text[2];
        Image[,] roundMarks = new Image[2, 3];
        Text timer, message, help, notice;
        float[] lag = { 1f, 1f };
        Font font;
        Transform layer;                    // where Box and Label put what they make
        GameObject fightLayer, selectLayer;

        // the select screen
        Image[] cardFrame;
        Text[] cardTag;
        Text selectTitle, selectWho, selectHelp;
        readonly Text[] pickName = new Text[2], pickOutfit = new Text[2];

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
                names[i] = Label($"name{i}", game.rigs[i].displayName, 34, left ? TextAnchor.LowerLeft : TextAnchor.LowerRight,
                                 left ? new Vector2(0.03f, 0.948f) : new Vector2(0.555f, 0.948f),
                                 left ? new Vector2(0.445f, 0.995f) : new Vector2(0.97f, 0.995f));
                for (int r = 0; r < 3; r++)
                {
                    float x0 = left ? 0.405f - r * 0.02f : 0.58f + r * 0.02f;
                    roundMarks[i, r] = Box($"round{i}{r}", Dark, new Vector2(x0, 0.875f), new Vector2(x0 + 0.014f, 0.897f));
                }
                // special gauge
                var gauge = Box($"meter{i}", Dark, left ? new Vector2(0.03f, 0.045f) : new Vector2(0.72f, 0.045f),
                                left ? new Vector2(0.28f, 0.07f) : new Vector2(0.97f, 0.07f));
                var fill = Box("fill", MeterColor, Vector2.zero, Vector2.one, gauge);
                meterImage[i] = fill;
                meterFill[i] = fill.rectTransform;
                Inset(meterFill[i]);
                meterText[i] = Label($"meterText{i}", "", 26, left ? TextAnchor.LowerLeft : TextAnchor.LowerRight,
                                     left ? new Vector2(0.03f, 0.072f) : new Vector2(0.72f, 0.072f),
                                     left ? new Vector2(0.28f, 0.11f) : new Vector2(0.97f, 0.11f));
            }
            timer = Label("timer", "60", 64, TextAnchor.MiddleCenter, new Vector2(0.455f, 0.875f), new Vector2(0.545f, 0.985f));
            help = Label("help", "1P: A/D move  W/S side step  J K U I = A B C D  1 2 3 / L O P = skills (or 236 / 214 / 236236 + J or U)   F1/F2: CPU on/off   F3: motions   F4: cloth   F5: skirt   F6: clothes burst   F7: select   F8: camera",
                         20, TextAnchor.LowerCenter, new Vector2(0.2f, 0.0f), new Vector2(0.8f, 0.04f));
            help.gameObject.SetActive(showHelp && !drawnByCamera);
            BuildSelect(game);
            // over both screens
            layer = transform;
            message = Label("message", "", 120, TextAnchor.MiddleCenter, new Vector2(0.03f, 0.4f), new Vector2(0.97f, 0.62f));
            message.color = Gold;
            // long names ("GODDESS LUF WINS THE MATCH") get smaller instead of running off the screen
            message.horizontalOverflow = HorizontalWrapMode.Wrap;
            message.resizeTextForBestFit = true;
            message.resizeTextMinSize = 48;
            message.resizeTextMaxSize = 120;
            notice = Label("notice", "", 22, TextAnchor.UpperCenter, new Vector2(0.1f, 0.63f), new Vector2(0.9f, 0.865f));
            notice.horizontalOverflow = HorizontalWrapMode.Wrap;
            notice.verticalOverflow = VerticalWrapMode.Truncate;
            notice.resizeTextForBestFit = true;
            notice.resizeTextMinSize = 10;
            notice.resizeTextMaxSize = 22;
            Refresh(game);
        }

        /// <summary>A card per fighter along the bottom, the two picked by name on either side, whose turn it is at the top.</summary>
        void BuildSelect(FightGame game)
        {
            layer = selectLayer.transform;
            int n = game.roster != null ? game.roster.Length : 0;
            cardFrame = new Image[n];
            cardTag = new Text[n];
            // the cards a row fits on the screen holds 13 of; more get smaller, all of a card alike (14 since 10-17: the row ran off
            // both edges)
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
                // a long name shrinks into its card, on two lines if that is bigger (13 cards since 10-17: CRANE KART and PINK
                // CRANE KART ran into each other at 22)
                name.horizontalOverflow = HorizontalWrapMode.Wrap;
                name.verticalOverflow = VerticalWrapMode.Truncate;
                name.resizeTextForBestFit = true;
                name.resizeTextMinSize = 10;
                name.resizeTextMaxSize = 22;
                name.lineSpacing = 0.85f;
                cardTag[k] = Label($"cardTag{k}", "", 28, TextAnchor.LowerCenter, new Vector2(x - 0.02f, y1 + 0.01f), new Vector2(x + w + 0.02f, y1 + 0.06f));
            }
            for (int i = 0; i < 2; i++)
            {
                bool left = i == 0;
                pickName[i] = Label($"pick{i}", "", 64, left ? TextAnchor.LowerLeft : TextAnchor.LowerRight,
                                    left ? new Vector2(0.04f, 0.4f) : new Vector2(0.56f, 0.4f), left ? new Vector2(0.44f, 0.5f) : new Vector2(0.96f, 0.5f));
                pickName[i].color = SideColor[i];
                pickOutfit[i] = Label($"outfit{i}", "", 28, left ? TextAnchor.UpperLeft : TextAnchor.UpperRight,
                                      left ? new Vector2(0.04f, 0.35f) : new Vector2(0.56f, 0.35f), left ? new Vector2(0.44f, 0.4f) : new Vector2(0.96f, 0.4f));
            }
            selectTitle = Label("selectTitle", "CHARACTER SELECT", 64, TextAnchor.MiddleCenter, new Vector2(0.1f, 0.88f), new Vector2(0.9f, 0.97f));
            selectTitle.color = Gold;
            selectWho = Label("selectWho", "", 30, TextAnchor.MiddleCenter, new Vector2(0.1f, 0.83f), new Vector2(0.9f, 0.88f));
            selectHelp = Label("selectHelp", "1P: A / D choose   J confirm   K back        2P: ← / → choose   1 confirm   2 back        F1 / F2: CPU on / off",
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
                // it: the title and whose turn it is); the type shrinks to fit (it ran off both sides at 1280 wide)
                notice.text = selecting ? game.Notice.Replace("\n", "    ") : game.Notice;
                var rt = notice.rectTransform;
                rt.anchorMin = selecting ? new Vector2(0.05f, 0.962f) : new Vector2(0.1f, 0.63f);
                rt.anchorMax = selecting ? new Vector2(0.95f, 0.998f) : new Vector2(0.9f, 0.865f);
                notice.alignment = selecting ? TextAnchor.MiddleCenter : TextAnchor.UpperCenter;
            }
            if (selecting)
            {
                RefreshSelect(game);
                return;
            }
            for (int i = 0; i < 2; i++)
            {
                var x = game.f[i];
                float hp = Mathf.Clamp01(x.hp / (float)x.maxHp);
                lag[i] = Mathf.Max(hp, Mathf.MoveTowards(lag[i], hp, 0.012f));
                SetFill(hpFill[i], hp, i == 0);
                SetFill(hpLag[i], lag[i], i == 0);
                float m = Mathf.Clamp01(x.meter / 100f);
                SetFill(meterFill[i], m, i == 0);
                meterImage[i].color = m >= 1f ? Gold : MeterColor;
                meterText[i].text = m >= 1f ? "MAX" : "";
                for (int r = 0; r < 3; r++)
                {
                    roundMarks[i, r].gameObject.SetActive(r < game.roundsToWin);
                    roundMarks[i, r].color = r < game.wins[i] ? Gold : Dark;
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
                float blink = 0.6f + 0.4f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 6f));
                Color c = Dark;
                if (p1 && p2)
                    c = Color.Lerp(SideColor[0], SideColor[1], Mathf.PingPong(Time.unscaledTime * 2f, 1f));
                else if (p1)
                    c = SideColor[0] * (game.picked[0] ? 1f : blink);
                else if (p2)
                    c = SideColor[1] * (game.picked[1] ? 1f : blink);
                c.a = 1f;
                cardFrame[k].color = p1 || p2 ? c : Dark;
                string Tag(int side) => game.cpu[side] ? "CPU" : side == 0 ? "1P" : "2P";
                cardTag[k].text = p1 && p2 ? $"{Tag(0)}  {Tag(1)}" : p1 ? Tag(0) : p2 ? Tag(1) : "";
                cardTag[k].color = p1 && !p2 ? SideColor[0] : p2 && !p1 ? SideColor[1] : Color.white;
            }
            for (int i = 0; i < 2; i++)
            {
                var rig = game.rigs[i];
                pickName[i].text = rig != null ? rig.displayName : "";
                pickOutfit[i].text = rig != null ? $"{rig.outfit}  ({rig.id})" + (game.picked[i] ? "   OK" : "") : "";
            }
            if (game.picked[0] && game.picked[1])
                selectWho.text = "READY";
            else if (alone)
                selectWho.text = game.choosing == (game.cpu[0] ? 0 : 1) ? "CHOOSE THE COMPUTER'S FIGHTER" : "CHOOSE YOUR FIGHTER";
            else if (game.cpu[0] && game.cpu[1])
                selectWho.text = "COMPUTER AGAINST COMPUTER";
            else
                selectWho.text = "1P AND 2P: CHOOSE";
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
