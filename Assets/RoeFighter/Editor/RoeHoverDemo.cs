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
    /// The game's own clips on a fighter who hovers in them (g05), the way the fight uses them: she is hit twice
    /// (hurt), casts her first skill, then the opponent's super knocks her down (die, lying, getting up).  Filmed from
    /// her side at floor level, once per variant, for tools/cloth_demo_video.py:
    ///   game   as the game has it: the clips hover (FighterRig.gameHover 0), her sashes follow their keys
    ///   floor  as the fight has it now: reactions on the floor, skills rising to the game's hover, sashes simulated
    ///   -executeMethod RoeFighter.EditorTools.RoeHoverDemo.Run [-roeChars g05] [-roeFoe b10] [-roeVariants game,floor] [-roeOut dir]
    /// </summary>
    public static class RoeHoverDemo
    {
        // (time after the fight starts, who: 0 the victim, 1 the opponent, what is pressed, caption)
        static readonly (float t, int who, FighterInput input, string what)[] Script =
        {
            (0.0f, 0, default, "站架"),
            (0.6f, 1, new FighterInput { a = true }, "被打中（受击）"),
            (1.5f, 1, new FighterInput { d = true }, "被打中（受击）"),
            (2.8f, 0, new FighterInput { s1 = true }, "技能 1"),
            (7.0f, 1, new FighterInput { s3 = true }, "被超必杀打倒（倒地、躺地、起身）"),
        };
        const float Length = 15f;

        [MenuItem("ROE Fighter/Fight/Hover demo")]
        public static void Run()
        {
            string stage = RoeCapture.Arg("-roeStage", "e23_steel_s02");
            string outDir = RoeCapture.Arg("-roeOut", Path.Combine(Path.GetDirectoryName(Application.dataPath), "_work", "hover_demo"));
            var ids = RoeCapture.Arg("-roeChars", "g05").Split(',');
            string foeId = RoeCapture.Arg("-roeFoe", "b10");
            var variants = RoeCapture.Arg("-roeVariants", "game,floor").Split(',');
            var size = RoeCapture.Arg("-roeSize", "960x720").Split('x');
            int width = int.Parse(size[0]), height = int.Parse(size[1]);
            ShaderUtil.allowAsyncCompilation = false;
            bool burst = RoeClothesBurst.Enabled;
            RoeClothesBurst.Enabled = false;            // the outfit stays on: this is about the body
            // opened once (a second open in the same session lost URP's volume components: black frames)
            EditorSceneManager.OpenScene(RoeFightScene.ScenePath(stage), OpenSceneMode.Single);
            var game = Object.FindFirstObjectByType<FightGame>();
            game.hud.gameObject.SetActive(false);
            var rigs = game.roster.Length > 0 ? game.roster : game.rigs;
            foreach (var rig in rigs)
                foreach (var smr in rig.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    smr.forceMatrixRecalculationPerRender = true;
            var built = rigs.ToDictionary(r => r, r => r.gameHover);
            var always = RoeBoneCloth.AlwaysKinds.ToList();
            Directory.CreateDirectory(outDir);
            var log = new StringBuilder("[ROE] hover demo:");
            var takes = new List<string>();
            foreach (var id in ids)
                foreach (var variant in variants)
                {
                    foreach (var rig in rigs)
                        rig.gameHover = variant == "game" ? 0f : built[rig];
                    RoeBoneCloth.AlwaysKinds.Clear();
                    if (variant != "game")
                        RoeBoneCloth.AlwaysKinds.UnionWith(always);
                    game.PickIds(foeId, id);
                    game.cpu = new[] { false, false };
                    game.Setup();
                    while (game.phase != FightGame.Phase.Fight)
                        game.Step(new FighterInput[2]);
                    int v = game.f[0].rig.id == id ? 0 : 1;
                    var me = game.f[v];
                    var foe = me.foe;
                    // close enough for the strikes
                    foe.pos = me.pos + (foe.pos - me.pos).normalized * 1.15f;
                    foe.Place();
                    string about = variant == "game" ? "游戏原样：原版动作浮空 23 cm，飘带照关键帧" : "现在：受击、倒地落地，技能慢慢升到游戏的高度，飘带模拟";
                    takes.Add($"{id}\t{variant}\t{about}\t");
                    string dir = Path.Combine(outDir, $"{id}_{variant}");
                    if (Directory.Exists(dir))
                        Directory.Delete(dir, true);
                    Directory.CreateDirectory(dir);
                    var cam = game.cam;
                    cam.fieldOfView = 34f;
                    var hips = me.rig.animator.GetBoneTransform(HumanBodyBones.Hips);
                    var line = (foe.pos - me.pos).normalized;
                    var side = Vector3.Cross(Vector3.up, line).normalized;
                    // from the side of the line between them, the camera's side of the stage
                    if (Vector3.Dot(cam.transform.position - me.pos, side) < 0f)
                        side = -side;
                    Vector3 Aim() => new Vector3(hips.position.x, me.pos.y + 0.85f, hips.position.z) + line * 0.35f;
                    var look = Aim();
                    var states = new StringBuilder();
                    FightState last = me.state;
                    float maxDrop = 0f, lowestHips = float.MaxValue;
                    int shot = 0, steps = Mathf.RoundToInt(Length * 60f);
                    for (int s = 0; s < steps; s++)
                    {
                        float t = s / 60f;
                        var inputs = new FighterInput[2];
                        foreach (var (at, who, input, _) in Script)
                            if (Mathf.Abs(t - at) < 0.5f / 60f)
                            {
                                var f = who == 0 ? me : foe;
                                if (input.s3)
                                    f.meter = 100f;
                                inputs[f.index] = input;
                            }
                        game.Step(inputs);
                        if (me.state != last)
                        {
                            states.Append($" {t:F2}s {me.state}/{me.rig.Current} (soles {me.rig.SoleHeight * 100f:F0} cm up)");
                            last = me.state;
                        }
                        maxDrop = Mathf.Max(maxDrop, me.rig.HoverDrop);
                        lowestHips = Mathf.Min(lowestHips, hips.position.y - me.pos.y);
                        look = Vector3.Lerp(look, Aim(), s == 0 ? 1f : 0.06f);
                        cam.transform.position = look + side * 4.4f + Vector3.up * 0.05f;
                        cam.transform.LookAt(look, Vector3.up);
                        if (s % 2 != 0)
                            continue;
                        if (shot == 0)
                        {
                            RoeCapture.Render(cam, 240, 180, Path.Combine(outDir, "_warm.jpg"), 80);
                            RoeCapture.Render(cam, 240, 180, Path.Combine(outDir, "_warm.jpg"), 80);
                        }
                        RoeCapture.Render(cam, width, height, Path.Combine(dir, $"{shot:D5}.jpg"), 90);
                        shot++;
                    }
                    log.Append($"\n[ROE]   {id} {variant} (vs {foe.rig.id}): {shot} frames, hp {me.hp}, most brought down {maxDrop * 100f:F1} cm, " +
                               $"hips lowest {lowestHips * 100f:F0} cm; states{states}");
                }
            foreach (var rig in rigs)
                rig.gameHover = built[rig];
            RoeBoneCloth.AlwaysKinds.Clear();
            RoeBoneCloth.AlwaysKinds.UnionWith(always);
            RoeClothesBurst.Enabled = burst;
            File.WriteAllText(Path.Combine(outDir, "script.txt"), string.Join("\n", Script.Select(x =>
                $"{x.t.ToString("F1", CultureInfo.InvariantCulture)} {x.what}")));
            File.WriteAllText(Path.Combine(outDir, "takes.txt"), string.Join("\n", takes));
            Debug.Log(log.ToString());
        }
    }
}
