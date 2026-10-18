using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using RoeFighter.Fight;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace RoeFighter.EditorTools
{
    /// <summary>
    /// The clothes burst (爆衣) in the real fight, the way a match brings it about: for each victim the
    /// other fighter, gauge full, lands her super (its finishing blow takes stage 1 off), then fights on as
    /// the CPU against the victim left on 1 HP until the KO (stage 2).  Filmed by two cameras, the fight's
    /// own (with the HUD) and a close one on the victim; frames, captions and the sounds per take for
    /// tools/burst_video.py.
    ///   -executeMethod RoeFighter.EditorTools.RoeBurstDemo.Run [-roeChars g04,a08] [-roeFoes b10,g05] [-roeOut dir] [-roeSize 960x540]
    ///                  [-roeFaces 1] [-roeSsao 0] [-roeShadows 0] [-roeShots 520]
    /// </summary>
    public static class RoeBurstDemo
    {
        static string ProjectDir => Path.GetDirectoryName(Application.dataPath);

        [MenuItem("ROE Fighter/Burst/Demo frames")]
        public static void Run()
        {
            string stage = RoeCapture.Arg("-roeStage", "e23_steel_s02");
            string outDir = RoeCapture.Arg("-roeOut", Path.Combine(ProjectDir, "_work", "burst_demo"));
            var victims = RoeCapture.Arg("-roeChars", "g04,a08").Split(',');
            // who each one fights (same order; empty: the scene's picks, the victim in place of 2P if she is not in them)
            var foes = RoeCapture.Arg("-roeFoes", "").Split(',');
            var size = RoeCapture.Arg("-roeSize", "960x540").Split('x');
            int width = int.Parse(size[0]), height = int.Parse(size[1]);
            int maxShots = int.Parse(RoeCapture.Arg("-roeShots", "100000"));     // frames per take at most (a quick look)
            ShaderUtil.allowAsyncCompilation = false;
            RoeClothesBurst.Enabled = true;
            // opened once (a second open in the same session lost URP's volume components: black frames)
            EditorSceneManager.OpenScene(RoeFightScene.ScenePath(stage), OpenSceneMode.Single);
            var game = Object.FindFirstObjectByType<FightGame>();
            if (game.rigs.Any(r => r.burst == null))
                Debug.LogWarning("[ROE] burst demo: a fighter has no clothes burst - rebuild the scene (RoeFightScene.Build)");
            game.hud.drawnByCamera = true;
            foreach (var rig in game.rigs)
                foreach (var smr in rig.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    smr.forceMatrixRecalculationPerRender = true;
            // -roeSsao 0: screen-space ambient occlusion off for this run (to see what it darkens; switched back at the end)
            var ssaoOff = RoeCapture.Arg("-roeSsao", "1") == "0"
                ? AssetDatabase.FindAssets("t:UniversalRendererData").Select(g => AssetDatabase.LoadAssetAtPath<UniversalRendererData>(AssetDatabase.GUIDToAssetPath(g)))
                    .Where(d => d != null).SelectMany(d => d.rendererFeatures).OfType<ScreenSpaceAmbientOcclusion>().Where(f => f.isActive).ToList()
                : new List<ScreenSpaceAmbientOcclusion>();
            foreach (var f in ssaoOff)
                f.SetActive(false);
            // -roeShadows 0: no light casts shadows (the scene is not saved)
            if (RoeCapture.Arg("-roeShadows", "1") == "0")
                foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    l.shadows = LightShadows.None;
            var closeGo = Object.Instantiate(game.cam.gameObject);
            closeGo.name = "Burst Close Camera";
            closeGo.hideFlags = HideFlags.DontSave;
            closeGo.tag = "Untagged";
            foreach (var l in closeGo.GetComponents<AudioListener>())
                Object.DestroyImmediate(l);
            var close = closeGo.GetComponent<Camera>();
            close.fieldOfView = 30f;
            Directory.CreateDirectory(outDir);
            var log = new StringBuilder("[ROE] burst demo:");
            var takes = new List<string>();

            for (int vi = 0; vi < victims.Length; vi++)
            {
                string id = victims[vi];
                string foe = vi < foes.Length ? foes[vi].Trim() : "";
                if (foe.Length > 0)
                    game.PickIds(foe, id);
                else if (game.roster.Length > 0 && game.roster[game.pick[0]].id != id && game.roster[game.pick[1]].id != id)
                    game.PickIds(null, id);
                game.cpu = new[] { false, false };
                game.Setup();
                int v = game.f[0].rig.id == id ? 0 : 1;
                var victim = game.f[v];
                var attacker = victim.foe;
                var burst = victim.rig.burst;
                foreach (var rig in game.rigs)
                    foreach (var smr in rig.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                        smr.forceMatrixRecalculationPerRender = true;
                // -roeFaces 1: the victim drawn by which side of each triangle shows, unlit (Hidden/ROE/FaceSide: grey front,
                // magenta back, yellow a front whose normal looks away) - what her moves and physics do to the meshes, apart
                // from the light (the pieces that come off fly in it too)
                if (RoeCapture.Arg("-roeFaces", "0") == "1")
                {
                    var side = new Material(Shader.Find("Hidden/ROE/FaceSide")) { hideFlags = HideFlags.DontSave };
                    foreach (var r in victim.rig.GetComponentsInChildren<Renderer>(true))
                        r.sharedMaterials = Enumerable.Repeat(side, r.sharedMaterials.Length).ToArray();
                }
                string dir = Path.Combine(outDir, id);
                if (Directory.Exists(dir))
                    Directory.Delete(dir, true);
                Directory.CreateDirectory(Path.Combine(dir, "fight"));
                Directory.CreateDirectory(Path.Combine(dir, "close"));
                var captions = new StringBuilder();
                string part = "intro";
                int shot = 0, dropped = 0, after = -1;
                float partTime = 0f;
                bool superPressed = false;
                var look = victim.pos + Vector3.up * 1.0f;
                Vector3 eye = Vector3.zero;
                float sideSign = 0f;
                captions.Append($"0\t{Title(victim)}：开场（衣服完整）\n");
                for (int s = 0; s < 60 * 90; s++)
                {
                    var inputs = new FighterInput[2];
                    partTime += FightGame.Dt;
                    switch (part)
                    {
                        case "intro":
                            if (game.phase == FightGame.Phase.Fight)
                            {
                                part = "super";
                                partTime = 0f;
                                attacker.meter = 100f;
                            }
                            break;
                        case "super":
                            // walk up until the super's blows reach (it does not slide up by itself), the super
                            // (shortcut S3), then wait until the victim is up again
                            if (!superPressed && partTime > 0.6f && attacker.Neutral)
                            {
                                float dist = Vector3.Distance(attacker.pos, victim.pos);
                                if (dist > attacker.SpecialReach("skill3") - 0.6f)
                                {
                                    var camRight = game.cam.transform.right;
                                    camRight.y = 0f;
                                    inputs[attacker.index].x = Vector3.Dot(victim.pos - attacker.pos, camRight) >= 0f ? 1 : -1;
                                }
                                else
                                {
                                    inputs[attacker.index].s3 = true;
                                    superPressed = true;
                                    captions.Append($"{shot}\t{Title(attacker)} 走近，放超必杀\n");
                                }
                            }
                            if (superPressed && partTime > 2f && attacker.state != FightState.Special && victim.Neutral)
                            {
                                part = "ko";
                                partTime = 0f;
                                victim.hp = 1;
                                game.cpu[attacker.index] = true;
                                captions.Append($"{shot}\t{Title(victim)} 只剩 1 点血，{Title(attacker)} 由电脑接着打\n");
                            }
                            if (partTime > 14f)
                                part = "end";
                            break;
                        case "ko":
                            if (game.phase != FightGame.Phase.Fight || partTime > 15f)
                            {
                                // the next round the same again (its KO ends the match: all that is left comes off)
                                part = game.phase == FightGame.Phase.RoundOver ? "between" : "end";
                                partTime = 0f;
                                game.cpu[attacker.index] = false;
                            }
                            break;
                        case "between":
                            if (game.phase == FightGame.Phase.MatchOver || partTime > 12f)
                            {
                                part = "end";
                                partTime = 0f;
                            }
                            else if (game.phase == FightGame.Phase.Fight)
                            {
                                part = "super";
                                partTime = 0f;
                                superPressed = false;
                                attacker.meter = 100f;
                                captions.Append($"{shot}\t第 {game.round} 局\n");
                            }
                            break;
                    }
                    game.Step(inputs);
                    game.UpdateCamera(FightGame.Dt);
                    if (burst != null && burst.Dropped != dropped)
                    {
                        dropped = burst.Dropped;
                        string why = part == "super" ? "超必杀命中" : game.wins.Max() >= game.roundsToWin ? "决胜的 KO，剩下的全掉" : "KO";
                        captions.Append($"{shot}\t第 {dropped} 段掉了（{why}）：" +
                                        string.Join("、", burst.pieces.Where(p => p.stage == dropped).Select(p => p.title).Distinct()) + "\n");
                    }
                    if (part == "end" && after < 0)
                        after = s + 60 * 4;
                    if ((after >= 0 && s >= after) || shot >= maxShots)
                        break;
                    // the close camera: on the victim, from the front and the side the fight camera is not on
                    var fwd = victim.Forward;
                    var side = Vector3.Cross(Vector3.up, fwd).normalized;
                    if (sideSign == 0f)
                        sideSign = Vector3.Dot(game.cam.transform.position - victim.pos, side) > 0f ? -1f : 1f;
                    // on her hips: the fall clips carry the body a metre and more away from where she stood
                    var hips = victim.rig.animator.GetBoneTransform(HumanBodyBones.Hips).position;
                    var target = new Vector3(hips.x, Mathf.Max(victim.pos.y + 0.6f, hips.y - 0.05f), hips.z);
                    var wanted = target + (fwd * 0.5f + side * (0.866f * sideSign)).normalized * 4.2f + Vector3.up * 0.45f;
                    look = s == 0 ? target : Vector3.Lerp(look, target, 0.06f);
                    eye = s == 0 ? wanted : Vector3.Lerp(eye, wanted, 0.04f);
                    close.transform.position = eye;
                    close.transform.LookAt(look, Vector3.up);
                    if (s % 2 != 0)
                        continue;
                    game.hud.Refresh(game);
                    Canvas.ForceUpdateCanvases();
                    if (shot == 0)
                    {
                        RoeCapture.Render(game.cam, 320, 180, Path.Combine(outDir, "_warm.jpg"), 80);
                        RoeCapture.Render(close, 320, 180, Path.Combine(outDir, "_warm.jpg"), 80);
                    }
                    RoeCapture.Render(game.cam, width, height, Path.Combine(dir, "fight", $"{shot:D5}.jpg"), 90);
                    RoeCapture.Render(close, width, height, Path.Combine(dir, "close", $"{shot:D5}.jpg"), 90);
                    shot++;
                }
                // sounds for the video: frames of the 30 fps video
                var lines = new List<string>();
                foreach (var e in game.soundLog)
                {
                    var rig = game.rigs.FirstOrDefault(r => r.id == e.fighter);
                    var clip = rig?.sounds.FirstOrDefault(x => x.name == e.name)?.clip;
                    if (clip != null)
                        lines.Add($"{{\"path\":\"{AssetDatabase.GetAssetPath(clip)}\",\"frame\":{e.frame / 2},\"volume\":{e.volume.ToString("F2", CultureInfo.InvariantCulture)}}}");
                }
                File.WriteAllText(Path.Combine(dir, "timeline.json"), $"{{\"fps\":30,\"frames\":{shot},\"sounds\":[\n  {string.Join(",\n  ", lines)}\n]}}\n");
                File.WriteAllText(Path.Combine(dir, "captions.txt"), captions.ToString());
                takes.Add(id);
                log.Append($"\n[ROE]   {id}: {shot} frames, stages off {(burst != null ? burst.Dropped : 0)}/{(burst != null ? burst.stages : 0)}, " +
                           $"{lines.Count} sounds, hp {victim.hp}, phase {game.phase}; took " +
                           (burst != null ? string.Join(" / ", burst.takeOffMs.Zip(burst.snapshotMs, (all, snap) => $"{all:F1} ms (snapshots {snap:F1})")) : "-") +
                           $"; {(burst != null ? burst.Report() : "no burst")}");
            }
            File.WriteAllText(Path.Combine(outDir, "takes.txt"), string.Join("\n", takes));
            Object.DestroyImmediate(closeGo);
            foreach (var f in ssaoOff)
                f.SetActive(true);
            if (ssaoOff.Count > 0)
                log.Append($"\n[ROE]   ambient occlusion was off ({ssaoOff.Count} renderer features), on again");
            Debug.Log(log.ToString());
        }

        static string Title(Fighter f) => $"{f.rig.displayName}（{f.rig.id}）";
    }
}
