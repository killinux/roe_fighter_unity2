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
    /// The skirt's physics tuned toward the game's own hand keys (user 10-11: "做吧，让动捕动作里的裙子也甩起来" - the
    /// skirt under motion capture swinging the way the game's artists key it, README "Luf 的衣服：和游戏里比").
    ///
    /// Her game clips are played with their skirt keys left to the bone cloth (FighterRig.IgnoreSkirtKeys: the skirt
    /// follows the legs, hangs and swings as under motion capture) and the skirt is measured against the keys
    /// (FighterRig.SkirtKeyRms: each skirt bone's direction in the pelvis frame, RMS degrees).  A coordinate search over
    /// the skirt's settings (RoeBoneCloth.SkirtTuning) and the drape (RoeSkirtRig.Drape): one setting at a time, each of
    /// its candidate values tried, the best kept; two passes.  Trained on skill 1 and skill 2 (RoeClothDemo.SkillScript),
    /// judged on a held-out script (the super, a hurt, react_02) - the game keys every one of them by hand.  Nothing is
    /// rendered; every trial is a fresh fight setup, so the numbers do not depend on the order.
    ///   -executeMethod RoeFighter.EditorTools.RoeSkirtTune.Run -roeChar g04 [-roePasses 2] [-roeStart "restore=0.2,drape=1"] [-roeSkip drape]
    ///   [-roeOut _work\skirt_tune_g04]      -> trials.txt (every trial), best.txt (the tuning string, then the numbers)
    /// </summary>
    public static class RoeSkirtTune
    {
        /// <summary>The held-out script: the super, then a hurt and a showcase reaction played as clips.</summary>
        internal static readonly (float from, float to, FighterInput input, string what)[] HeldScript =
        {
            (0.0f, 1.0f, default, "guard"),
            (1.0f, 1.0f, new FighterInput { s3 = true }, "super"),
            (7.5f, 7.5f, default, "clip:hurt"),
            (9.5f, 9.5f, default, "clip:react_02"),
        };
        internal const float HeldLength = 12.5f;

        /// <summary>The settings searched and their candidates; the first of each is the present preset's value.</summary>
        static readonly (string key, float[] values)[] Space =
        {
            ("drape", new[] { 1f, 0.6f, 0.3f, 0f }),
            ("restore", new[] { 0.35f, 0.2f, 0.1f, 0.05f, 0.02f }),
            ("damping", new[] { 0.15f, 0.08f, 0.04f, 0.02f, 0f }),
            ("inertia", new[] { 0.3f, 0.5f, 0.7f, 0.9f, 1f }),
            ("worldInertia", new[] { 0.15f, 0.3f, 0.5f, 0.75f, 1f }),
            ("worldTurnLimit", new[] { 120f, 360f, 720f }),
            ("limitTip", new[] { 35f, 60f, 90f, 130f, 170f }),
            ("limitRoot", new[] { 5f, 15f, 30f, 50f }),
            ("maxDistanceTip", new[] { 0.25f, 0.4f, 0.6f, 1f, 0f }),
            ("maxDistanceRoot", new[] { 0.03f, 0.06f, 0.12f, 0f }),
            ("gravity", new[] { 5f, 3f, 1.5f, 0.5f }),
            ("tether", new[] { 0.15f, 0.3f, 0f }),
        };

        [MenuItem("ROE Fighter/Fight/Tune the skirt toward the game's keys")]
        public static void Run()
        {
            string id = RoeCapture.Arg("-roeChar", "g04");
            string stage = RoeCapture.Arg("-roeStage", "e23_steel_s02");
            string outDir = RoeCapture.Arg("-roeOut", Path.Combine(Path.GetDirectoryName(Application.dataPath), "_work", "skirt_tune_" + id));
            int passes = int.Parse(RoeCapture.Arg("-roePasses", "2"));
            // -roeSkip drape: settings held where -roeStart puts them (user 10-11: with the drape at 0.3 the skirt rig's fitted
            // pose - the front panel on her right thigh - lifted the panel with a kick, "和裙子有粘连")
            var skip = new HashSet<string>(RoeCapture.Arg("-roeSkip", "").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0));
            Directory.CreateDirectory(outDir);
            var current = Space.ToDictionary(p => p.key, p => p.values[0]);
            foreach (var (k, v) in Parse(RoeCapture.Arg("-roeStart", "")))
                current[k] = v;

            EditorSceneManager.OpenScene(RoeFightScene.ScenePath(stage), OpenSceneMode.Single);
            var trials = new StringBuilder();
            var log = new StringBuilder($"[ROE] skirt tune {id}:");
            var watch = System.Diagnostics.Stopwatch.StartNew();
            FighterRig.IgnoreSkirtKeys = true;
            try
            {
                float Train(Dictionary<string, float> p) => Evaluate(id, p, RoeClothDemo.SkillScript, RoeClothDemo.SkillLength);
                float best = Train(current);
                float start = best;
                trials.AppendLine($"start\t{best:F2}\t{Text(current)}");
                log.Append($"\n[ROE]   start {best:F1} deg ({Text(current)}), {watch.Elapsed.TotalSeconds:F0} s");
                for (int pass = 1; pass <= passes; pass++)
                {
                    bool moved = false;
                    foreach (var (key, values) in Space.Where(p => !skip.Contains(p.key)))
                        foreach (float v in values)
                        {
                            if (Mathf.Approximately(v, current[key]))
                                continue;
                            var trial = new Dictionary<string, float>(current) { [key] = v };
                            float e = Train(trial);
                            trials.AppendLine($"{pass}\t{e:F2}\t{Text(trial)}");
                            if (e < best - 0.05f)
                            {
                                best = e;
                                current = trial;
                                moved = true;
                                log.Append($"\n[ROE]   pass {pass}: {key} {v.ToString(CultureInfo.InvariantCulture)} -> {best:F1} deg ({watch.Elapsed.TotalSeconds:F0} s)");
                            }
                        }
                    if (!moved)
                        break;
                }
                // the held-out clips: the preset against the tuned
                var preset = Space.ToDictionary(p => p.key, p => p.values[0]);
                float heldPreset = Evaluate(id, preset, HeldScript, HeldLength), heldBest = Evaluate(id, current, HeldScript, HeldLength);
                log.Append($"\n[ROE]   trained (skill 1 + 2): {start:F1} -> {best:F1} deg; held out (super, hurt, react_02): " +
                           $"{heldPreset:F1} -> {heldBest:F1} deg; {watch.Elapsed.TotalMinutes:F1} min");
                log.Append($"\n[ROE]   best: {Text(current)}");
                File.WriteAllText(Path.Combine(outDir, "trials.txt"), trials.ToString());
                File.WriteAllText(Path.Combine(outDir, "best.txt"),
                    $"{Text(current)}\ntrained {start:F2} -> {best:F2}\nheld out {heldPreset:F2} -> {heldBest:F2}\n");
            }
            finally
            {
                FighterRig.IgnoreSkirtKeys = false;
                RoeBoneCloth.SkirtTuning = "";
                RoeSkirtRig.Drape = 1f;
            }
            Debug.Log(log.ToString());
        }

        static IEnumerable<(string key, float value)> Parse(string text)
        {
            foreach (var pair in (text ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = pair.Split('=');
                if (kv.Length == 2 && float.TryParse(kv[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                    yield return (kv[0].Trim(), v);
            }
        }

        internal static string Text(Dictionary<string, float> p) =>
            string.Join(",", p.Select(kv => $"{kv.Key}={kv.Value.ToString("0.###", CultureInfo.InvariantCulture)}"));

        /// <summary>One take of the script with these settings, the skirt left to the cloth: RMS degrees off the game's keys.</summary>
        internal static float Evaluate(string id, Dictionary<string, float> p, (float from, float to, FighterInput input, string what)[] script, float length)
        {
            RoeSkirtRig.Drape = p.TryGetValue("drape", out float drape) ? drape : 1f;
            RoeBoneCloth.SkirtTuning = string.Join(",", p.Where(kv => kv.Key != "drape")
                .Select(kv => $"{kv.Key}={kv.Value.ToString(CultureInfo.InvariantCulture)}"));
            var game = Object.FindFirstObjectByType<FightGame>();
            game.cpu = new[] { false, false };
            if (game.hud != null)
                game.hud.gameObject.SetActive(false);
            if (game.roster.Length > 0 && game.roster[game.pick[0]].id != id && game.roster[game.pick[1]].id != id)
                game.PickIds(null, id);
            FighterRig.ClothBackend = "magica_style";
            foreach (var rig in (game.roster.Length > 0 ? game.roster : game.rigs).Where(r => r != null))
            {
                rig.useCloth = true;
                rig.clothWeight = 1f;
            }
            game.Setup();
            for (int guard = 0; game.phase != FightGame.Phase.Fight && guard < 3600; guard++)
                game.Step(new FighterInput[2]);
            int who = game.f[0].rig.id == id ? 0 : 1;
            var me = game.f[who];
            me.foe.pos = me.pos + (me.foe.pos - me.pos).normalized * 5.5f;
            me.foe.Place();
            me.rig.ResetCloth();
            me.rig.ResetSkirtKeyError();
            int steps = Mathf.RoundToInt(length * 60f);
            for (int s = 0; s < steps; s++)
            {
                float t = s / 60f;
                var input = default(FighterInput);
                foreach (var (from, to, inp, _) in script)
                    if (from == to ? Mathf.Abs(t - from) < 0.5f / 60f : t >= from && t < to)
                        input = inp;
                if (input.s1 || input.s2 || input.s3)
                    me.meter = 100f;
                RoeClothDemo.PlayScriptClips(script, t, me);
                var inputs = new FighterInput[2];
                inputs[who] = input;
                game.Step(inputs);
            }
            return me.rig.SkirtKeyRms;
        }
    }
}
