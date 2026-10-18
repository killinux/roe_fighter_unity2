using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using RoeFighter.Fight;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RoeFighter.EditorTools
{
    /// <summary>
    /// Scripted demos of the side-view fight, filmed by its camera with the HUD (batch mode has no keys: the inputs are
    /// scripted the way a pad gives them).  Each demo is a row of short scenes, each with a caption; frames, timeline.json
    /// (the sounds, the music) and captions.json (Chinese lines by frame, burnt in by tools/caption_frames.py) for
    /// tools/make_video.py.
    ///   ranged   the ranged skills as real projectiles: Inase's thrown greatsword from 6 m and from 3 m, jumped over;
    ///            Kart's thrown axe; her bullets blocked and jumped; a bullet meeting the thrown sword; Amano's tentacles
    ///            and Inase's swords of light landing on the spot - or not, when she walks out of it.
    ///   system   MAX mode, a strike cancelled into a skill, the super cancel, a MAX super, the two guard cancels, the back
    ///            throw against the forward one.
    ///   moves    each fighter's own jump, crouch, guard (standing and crouching), roll and back dash where her game has them.
    ///   -executeMethod RoeFighter.EditorTools.RoeKofDemo.Run -roeDemo ranged [-roeOut _work\demo_ranged] [-roeSize 1280x720]
    ///   [-roeIds eve09,fio005,kas,mai,raven,a08] (moves)
    /// </summary>
    public static class RoeKofDemo
    {
        static FightGame game;
        static string frameDir;
        static int shot, step, seen;
        static int width, height;
        static readonly List<string> sounds = new List<string>();
        static readonly List<string> captions = new List<string>();
        static readonly StringBuilder log = new StringBuilder();
        static string caption = "";
        static int captionFrom;

        public static void Run()
        {
            string demo = RoeCapture.Arg("-roeDemo", "ranged");
            string stage = RoeCapture.Arg("-roeStage", "e23_steel_s02");
            string outDir = RoeCapture.Arg("-roeOut", Path.Combine(Path.GetDirectoryName(Application.dataPath), "_work", "demo_" + demo));
            var size = RoeCapture.Arg("-roeSize", "1280x720").Split('x');
            width = int.Parse(size[0]);
            height = int.Parse(size[1]);
            ShaderUtil.allowAsyncCompilation = false;
            RoeClothesBurst.Enabled = RoeCapture.Arg("-roeBurst", "1") != "0";
            EditorSceneManager.OpenScene(RoeFightScene.ScenePath(stage), OpenSceneMode.Single);
            game = Object.FindFirstObjectByType<FightGame>();
            game.hud.drawnByCamera = true;
            game.hud.showHelp = false;
            game.cpu = new[] { false, false };
            game.teamSize = 1;
            game.roundSeconds = 999f;
            foreach (var rig in game.roster)
                foreach (var smr in rig.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    smr.forceMatrixRecalculationPerRender = true;     // many renders inside one editor update
            frameDir = Path.Combine(outDir, "frames");
            if (Directory.Exists(frameDir))
                Directory.Delete(frameDir, true);
            Directory.CreateDirectory(frameDir);
            shot = step = seen = 0;
            sounds.Clear();
            captions.Clear();
            log.Clear().Append($"[ROE] demo {demo}:");
            switch (demo)
            {
                case "ranged": Ranged(); break;
                case "system": SystemDemo(); break;
                case "moves": Moves(RoeCapture.Arg("-roeIds", "eve09,fio005,kas,mai,raven,a08").Split(',')); break;
                default: throw new Exception("no demo " + demo);
            }
            Caption("");
            string music = game.music != null
                ? $",\"music\":{{\"path\":\"{AssetDatabase.GetAssetPath(game.music)}\",\"volume\":{game.musicVolume.ToString("F2", CultureInfo.InvariantCulture)}}}"
                : "";
            File.WriteAllText(Path.Combine(outDir, "timeline.json"), $"{{\"fps\":30,\"frames\":{shot}{music},\"sounds\":[\n  {string.Join(",\n  ", sounds)}\n]}}\n");
            File.WriteAllText(Path.Combine(outDir, "captions.json"), "[\n  " + string.Join(",\n  ", captions) + "\n]\n", new UTF8Encoding(false));
            Debug.Log(log.Append($"\n[ROE]   {shot} frames ({shot / 30f:F1} s) to {frameDir}; events: {string.Join("; ", game.events)}").ToString());
        }

        // ---- filming

        static readonly HashSet<string> checkedThrows = new HashSet<string>();
        static Fighter P1 => game.f[0];
        static Fighter P2 => game.f[1];

        /// <summary>A new scene: these two, a fresh match (no intro filmed), at these places along the line (P1 on the left).</summary>
        static void Begin(string p1, string p2, float apart, string line)
        {
            game.PickIds(p1, p2);
            game.Setup();
            foreach (var rig in game.rigs)
                foreach (var smr in rig.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    smr.forceMatrixRecalculationPerRender = true;
            for (int i = 0; i < 200 && game.phase != FightGame.Phase.Fight; i++)
                game.Step(null);
            game.message = "";
            // the game's help lines (mode, motions, cloth) would cover the first seconds of every scene
            typeof(FightGame).GetField("noticeTime", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(game, 0f);
            P1.U = -apart * 0.5f;
            P2.U = apart * 0.5f;
            P1.Place();
            P2.Place();
            game.SnapCamera();
            Caption(line);
            log.Append($"\n[ROE]   {shot / 30f,5:F1}s {p1} v {p2}, {apart:F1} m: {line}");
            foreach (var who in new[] { P1, P2 })
                foreach (var r in who.rig.ranged.Where(x => x.IsThrow))
                    if (checkedThrows.Add(who.rig.id + r.skill))
                        log.Append($"\n[ROE]     {who.rig.id} {r.skill} throws {r.weapon} (out {r.release:F2} s, {r.reach:F1} m, back {r.back:F2} s): " +
                                   $"its bone {(who.rig.ThrownWeaponBone(r) != null ? "found" : "NOT FOUND")}");
            Film(0.5f);
        }

        /// <summary>The caption from now on (its Chinese line goes to captions.json with the frames it covers).</summary>
        static void Caption(string line)
        {
            if (!string.IsNullOrEmpty(caption) && shot > captionFrom)
                captions.Add($"{{\"from\":{captionFrom},\"to\":{shot},\"text\":\"{caption.Replace("\"", "'")}\"}}");
            caption = line;
            captionFrom = shot;
        }

        /// <summary>Screen x that walks / holds this fighter towards (+1) or away from (-1) the other.</summary>
        static int Screen(Fighter me, int towards)
        {
            var camRight = game.cam.transform.right;
            camRight.y = 0f;
            return (Vector3.Dot(game.axis * me.FoeSide, camRight) >= 0f ? 1 : -1) * towards;
        }

        /// <summary>Steps with these inputs held (presses on the first step only), filmed every second step.</summary>
        static void Film(float seconds, FighterInput? i1 = null, FighterInput? i2 = null, Func<bool> until = null)
        {
            int steps = Mathf.RoundToInt(seconds * 60f);
            for (int s = 0; s < steps; s++)
            {
                var a = i1 ?? default;
                var b = i2 ?? default;
                if (s > 0)
                {
                    a = new FighterInput { x = a.x, y = a.y };
                    b = new FighterInput { x = b.x, y = b.y };
                }
                OneStep(a, b);
                if (until != null && until())
                    break;
            }
        }

        static void OneStep(FighterInput a, FighterInput b)
        {
            game.Step(new[] { a, b });
            game.UpdateCamera(FightGame.Dt);
            if (game.soundLog.Count < seen)
                seen = 0;
            for (; seen < game.soundLog.Count; seen++)
            {
                var e = game.soundLog[seen];
                var clip = game.roster.Concat(game.rigs).FirstOrDefault(r => r != null && r.id == e.fighter)?.sounds.FirstOrDefault(x => x.name == e.name)?.clip;
                if (clip != null)
                    sounds.Add($"{{\"path\":\"{AssetDatabase.GetAssetPath(clip)}\",\"frame\":{shot},\"volume\":{e.volume.ToString("F2", CultureInfo.InvariantCulture)}}}");
            }
            if (step++ % 2 == 0)
            {
                game.hud.Refresh(game);
                Canvas.ForceUpdateCanvases();
                if (shot == 0)
                {
                    RoeCapture.Render(game.cam, 320, 180, Path.Combine(frameDir, "..", "_warm.jpg"), 80);
                    RoeCapture.Render(game.cam, 320, 180, Path.Combine(frameDir, "..", "_warm.jpg"), 80);
                }
                RoeCapture.Render(game.cam, width, height, Path.Combine(frameDir, $"{shot:D5}.jpg"), 92);
                shot++;
            }
        }

        /// <summary>Film until both stand ready again (at most this long), then a little more.</summary>
        static void Settle(float most = 6f, float after = 0.4f)
        {
            Film(most, until: () => (P1.Neutral || P1.state == FightState.Land) && (P2.Neutral || P2.state == FightState.Land) && P1.special == null);
            Film(after);
        }

        static FighterInput Press(string what, int x = 0, int y = 0) => new FighterInput
        {
            x = x, y = y, a = what == "A", b = what == "B", c = what == "C", d = what == "D", ab = what == "AB", cd = what == "CD",
            max = what == "MAX", s1 = what == "S1", s2 = what == "S2", s3 = what == "S3",
        };

        static FighterInput Hold(int x, int y = 0) => new FighterInput { x = x, y = y };

        static void Note(string what) => log.Append($"\n[ROE]   {shot / 30f,5:F1}s   {what} | P1 {P1.rig.id} {P1.state} hp {P1.hp} meter {P1.meter:F0} outcome {P1.specialOutcome} | P2 {P2.rig.id} {P2.state} hp {P2.hp}");

        // ---- the demos

        static void Ranged()
        {
            Begin("a08", "eve09", 6f, "Inase 技能 2：把巨剑扔出去，剑飞到 6 米外的 Eve 身上才算打中");
            Film(0.05f, Press("S2"));
            Settle();
            Note("a08 skill 2 from 6 m");

            Begin("a08", "eve09", 3f, "同一招在 3 米：剑只飞 3 米，落点跟着对手的实际距离");
            Film(0.05f, Press("S2"));
            Settle();
            Note("a08 skill 2 from 3 m");

            // (her greatsword spins end over end as tall as a jump: it is rolled through, not jumped)
            Begin("a08", "eve09", 6f, "Eve 看准剑出手的时机往前翻滚：翻滚中无敌，剑从她身上穿过去，打空");
            float release = P1.rig.Ranged("skill2")?.release ?? 2.2f;
            Film(0.05f, Press("S2"));
            Film(4f, until: () => P1.special != null && P1.t >= release - 0.1f);
            Film(0.05f, null, Press("AB"));
            Settle();
            Note("a08 skill 2 rolled through");

            Begin("b10", "eve09", 5f, "Kart 技能 1：甩出斧头，飞到对手身上");
            Film(0.05f, Press("S1"));
            Settle();
            Note("b10 skill 1 (axe)");

            Begin("b10", "eve09", 6f, "Kart 技能 2：双枪，每一发子弹真的飞过去；Eve 按住后退全部挡住");
            Film(0.05f, Press("S2"), Hold(Screen(P2, -1)));
            Film(4f, null, Hold(Screen(P2, -1)), () => P1.special == null);
            Settle();
            Note("b10 skill 2 blocked");

            Begin("b10", "eve09", 6f, "再来一次，Eve 大跳（先下再上，跳得更高）躲过子弹；第三发晚到，落地后再跳一次");
            // a super jump is above the bullets (chest height) from 0.24 to 0.79 s after it leaves the floor: one for each
            // bunch of bullets, leaving the floor 0.35 s before the first of the bunch arrives (at its blow's moment)
            var arrivals = P1.rig.sheet?.Find("skill2")?.Blows.Select(h => h.time).ToList() ?? new List<float>();
            Film(0.05f, Press("S2"));
            float upTill = -1f;
            foreach (float arrive in arrivals)
            {
                if (arrive <= upTill)
                    continue;
                float liftOff = arrive - 0.35f;
                Film(4f, until: () => P1.special != null && P1.t >= liftOff - 0.12f);
                Film(0.05f, null, Hold(0, -1));
                Film(0.12f, null, Hold(0, 1));
                upTill = liftOff + 0.75f;
            }
            Settle();
            Note("b10 skill 2 jumped");

            Begin("b10", "b10", 6f, "对射：Kart 甩出的斧头撞上对面 Kart 的子弹，两边一起抵消（后面的子弹照样打过来）");
            // the axe is out in a few frames (then it spins there): the other one fires first, and the axe leaves the hand
            // 0.1 s after her first bullet left the gun (the bullet: 6 m before her first blow at 26 m/s)
            var axe = P1.rig.Ranged("skill1");
            var gun = P2.rig.Ranged("skill2");
            float firstShot = (P2.rig.sheet?.Find("skill2")?.Blows.FirstOrDefault()?.time ?? 0.5f) - (gun != null ? gun.anchorReach / Mathf.Max(1f, gun.speed) : 0.23f);
            float throwAt = Mathf.Max(0f, firstShot + 0.1f - (axe?.release ?? 0.53f));
            Film(0.05f, null, Press("S2"));
            Film(4f, until: () => P2.special != null && P2.t >= throwAt);
            Film(0.05f, Press("S1"));
            Settle();
            Note("bullet v axe");

            Begin("m02", "eve09", 5f, "天野超必杀：锁定对手站的位置落下；Eve 在落下前后撤步离开那里，打空");
            P1.meter = 100f;
            float lockAt = P1.rig.Ranged("skill3")?.lockAt ?? 2f;
            Film(0.05f, Press("S3"));
            Film(4f, until: () => P1.special != null && P1.t >= lockAt + 0.1f);
            Film(0.05f, null, Hold(Screen(P2, -1)));
            Film(0.05f);
            Film(0.25f, null, Hold(Screen(P2, -1)));
            Settle();
            Note("m02 super dodged");

            Begin("m02", "eve09", 5f, "这次 Eve 站着不动：超必杀落在她身上（爆衣一段）");
            P1.meter = 100f;
            Film(0.05f, Press("S3"));
            Settle(8f);
            Note("m02 super lands");

            Begin("a08", "eve09", 6f, "Inase 超必杀（光之剑落下）从 6 米外打中");
            P1.meter = 100f;
            Film(0.05f, Press("S3"));
            Settle(8f);
            Note("a08 super");
        }

        static void SystemDemo()
        {
            Begin("eve_nikke06", "kas", 1.2f, "MAX 模式（B+C，或 G 键）：用一格能量，10 秒内攻击加 20%，身上有光");
            P1.meter = 300f;
            Film(0.05f, Press("MAX"));
            Film(1.2f);
            Note("MAX");

            Caption("普通攻击打中后取消接技能 1（对手被锁住，接得上）");
            Film(0.05f, Press("C"));
            Film(1.5f, until: () => P1.connected && P1.move != null && P1.t * P1.move.speed >= P1.move.length * P1.move.cancel);
            Film(0.05f, Press("S1"));
            Film(5f, until: () => P1.specialOutcome == 1 || P1.specialOutcome == 2 || P1.special == null);
            Caption("技能打中后超级取消接超必杀；MAX 中不耗能量格，伤害再加 30%（MAX 超必杀）");
            Film(0.05f, Press("S3"));
            Settle(9f);
            Note("cancel + super cancel");

            foreach (var (what, line) in new[] { ("AB", "防御反击 · 回避：挡住对手的攻击时按 A+B，耗一格能量，滚到对手身后"),
                                                 ("CD", "防御反击 · 吹飞：挡住时按 C+D，耗一格能量，打回去（伤害减半）") })
            {
                Begin("eve_nikke06", "kas", 1.0f, line);
                P1.meter = 300f;
                // the opponent strikes until one is blocked
                for (int k = 0; k < 5 && P1.state != FightState.Block; k++)
                {
                    Film(0.05f, Hold(Screen(P1, -1)), Press(k % 2 == 0 ? "C" : "A"));
                    Film(0.6f, Hold(Screen(P1, -1)), null, () => P1.state == FightState.Block);
                }
                Film(0.05f, new FighterInput { x = Screen(P1, -1), ab = what == "AB", cd = what == "CD" }, null);
                Settle();
                Note("guard cancel " + what);
            }

            Begin("fio005", "mai", 0.9f, "投技：贴身 前 + C 往前摔");
            Film(0.05f, Press("C", Screen(P1, 1)));
            Settle();
            Note("forward throw");

            Begin("fio005", "mai", 0.9f, "背摔：贴身 后 + C，转身把对手摔到另一边（两人换位）");
            Film(0.05f, Press("C", Screen(P1, -1)));
            Settle();
            Note("back throw");
        }

        static void Moves(IEnumerable<string> ids)
        {
            foreach (var id in ids.Select(x => x.Trim()).Where(x => x.Length > 0))
            {
                string partner = id == "a08" ? "g04" : "a08";
                Begin(id, partner, 2.4f, $"{Name(id)}：原地跳、前跳");
                Film(0.25f, Hold(0, 1));
                Settle(2f, 0.2f);
                Film(0.25f, Hold(Screen(P1, 1), 1));
                Settle(2f, 0.3f);
                Caption($"{Name(id)}：下蹲");
                Film(1.2f, Hold(0, -1));
                Film(0.3f);
                Caption($"{Name(id)}：站防（对手 C）、蹲防（对手蹲 B，下段）");
                P1.U = P2.U - 1.1f * P1.FoeSide;
                Film(0.05f, Hold(Screen(P1, -1)), Press("C"));
                Film(1.0f, Hold(Screen(P1, -1)));
                Film(0.05f, Hold(Screen(P1, -1), -1), Press("B", 0, -1));
                Film(1.0f, Hold(Screen(P1, -1), -1), Hold(0, -1));
                Film(0.3f);
                Caption($"{Name(id)}：A+B 翻滚（穿过对手）、后撤步");
                Film(0.05f, Press("AB"));
                Settle(2f, 0.3f);
                Film(0.05f, Hold(Screen(P1, -1)));
                Film(0.05f);
                Film(0.2f, Hold(Screen(P1, -1)));
                Settle(2f, 0.4f);
                Note($"moves of {id}");
            }
        }

        /// <summary>
        /// A check of MAX mode's aura outside the fight: the prefab stepped the ways a particle system can be stepped by hand,
        /// the particles alive after a second each way (the log).
        ///   -executeMethod RoeFighter.EditorTools.RoeKofDemo.AuraTest
        /// </summary>
        public static void AuraTest()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/RoeFighter/Generated/fx/max_aura.prefab");
            var lines = new StringBuilder("[ROE] aura test:");
            for (int way = 0; way < 4; way++)
            {
                var go = Object.Instantiate(prefab);
                var ps = go.GetComponentsInChildren<ParticleSystem>(true);
                foreach (var p in ps)
                {
                    if (way == 1 || way == 3)
                        p.Play(true);
                    p.Simulate(0f, true, true, way >= 2);
                }
                for (int s = 0; s < 60; s++)
                    foreach (var p in ps)
                        p.Simulate(1f / 60f, true, false, way >= 2);
                lines.Append($"\n[ROE]   way {way} (play first {way == 1 || way == 3}, fixed step {way >= 2}): {ps.Sum(p => p.particleCount)} particles, " +
                             $"time {ps[0].time:F2}, playing {ps[0].isPlaying}, emitting {ps[0].isEmitting}, paused {ps[0].isPaused}");
                Object.DestroyImmediate(go);
            }
            // ... and in the fight: MAX mode on, a second later the aura's particles and where they are
            EditorSceneManager.OpenScene(RoeFightScene.ScenePath("e23_steel_s02"), OpenSceneMode.Single);
            game = Object.FindFirstObjectByType<FightGame>();
            game.cpu = new[] { false, false };
            game.teamSize = 1;
            game.PickIds("eve_nikke06", "kas");
            game.Setup();
            for (int i = 0; i < 200 && game.phase != FightGame.Phase.Fight; i++)
                game.Step(null);
            game.f[0].meter = 300f;
            game.Step(new[] { new FighterInput { max = true }, default });
            for (int i = 0; i < 80; i++)
                game.Step(null);
            var aura = Object.FindObjectsByType<ParticleSystem>(FindObjectsInactive.Include).Where(p => p.transform.root.name.StartsWith("max_aura")).ToList();
            lines.Append($"\n[ROE]   in the fight: MAX {game.f[0].Max} ({game.f[0].maxTime:F1} s left), aura systems {aura.Count}: " +
                         string.Join(", ", aura.Select(p => $"{p.transform.root.name} at {p.transform.position} active {p.gameObject.activeInHierarchy}, {p.particleCount} particles, time {p.time:F2}")) +
                         $"; her place {game.f[0].pos}");
            Debug.Log(lines.ToString());
        }

        static string Name(string id)
        {
            var rig = game.roster.FirstOrDefault(r => r.id == id);
            return rig != null ? $"{rig.displayName}（{rig.outfit}）" : id;
        }
    }
}
