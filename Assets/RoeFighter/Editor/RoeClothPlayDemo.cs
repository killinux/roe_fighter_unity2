using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using RoeFighter.Fight;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.LowLevel;
using Object = UnityEngine.Object;

namespace RoeFighter.EditorTools
{
    /// <summary>
    /// RoeClothDemo in play mode: the same script and cameras, for solvers that run on Unity's player loop and do not move
    /// when the editor steps the fight by hand - Magica Cloth 2 (user 10-04: "magic cloth2 下载到……看看能不能用"; "inase和一个
    /// kasumi 先试试，两个游戏的骨骼和衣服是否都能适配").  The batch method enters play mode (the job waits in SessionState over
    /// the domain reload); there the fight's own Update is switched off and two systems join the player loop: after
    /// Update one step of the fight per frame (Time.captureFramerate 60: every frame 1/60 s, for Magica too), at the start
    /// of PostLateUpdate - after Magica's update in PreLateUpdate - the camera renders the frame.  Then play mode ends and
    /// the editor quits.  Takes as RoeClothDemo writes them (tools/cloth_demo_video.py).
    ///   unity_batch.ps1 -NoQuit -Graphics -Method RoeFighter.EditorTools.RoeClothPlayDemo.Run
    ///     -Extra '-roeChars','a08,kas011','-roeCloths','magica,magica_style,off' [-roeView front|backright|backleft] [-roeOut dir]
    /// A setup may carry Magica mesh settings after a colon ("magica:edge=1;gravity=3", RoeMagicaCloth.MeshSettings): its
    /// take is named after them.  Every take is also measured the same way whatever moves the cloth (Meter): the numbers
    /// per part of the script go to the log and metrics.txt.  -roeNoFrames: numbers only, nothing rendered.
    /// -roeScript skills and -roeSkirtKeys off as RoeClothDemo has them (her game clips, the skirt left to the solver and
    /// measured against the keys).  The setup word "keys" ("off:keys") keeps the game's keys for that take - the game's own
    /// look, the reference column; -roeMeasureKeys ribbon,hair measures those chains against the keys too (after Magica has
    /// written its bones: FighterRig.MeasureKeysLate); -roeView follow is the front view rising with her hips (user 10-11:
    /// "再看看游戏里的效果是什么，看看差异" - the two Lufs against their game's hand keys, README "Luf 的衣服：和游戏里比").
    /// Body checks (user 10-05: "爆衣，乳摇，都身体权重，inase的头发也衣服，这些都检查一下"): -roeView chest (close in front of
    /// her chest) or hair (behind her head and shoulders); the setup word "nude" ("auto:nude") takes every outfit piece that
    /// can come off away before the take (RoeClothesBurst); every take also measures the breasts (BreastMeter: how far each
    /// one moves against the chest, whatever moves it - breasts.txt) and, in the hair view or with -roeHairMeter, the hair
    /// that hangs (HairMeter: inside the body or what she wears - hair.txt).
    /// </summary>
    [InitializeOnLoad]
    public static class RoeClothPlayDemo
    {
        [Serializable]
        class Job
        {
            public string[] chars, cloths;
            public string outDir, view, stage, script;
            public int width, height;
            public bool done, noFrames, ignoreKeys;
            public string measureKinds = "";       // -roeMeasureKeys ribbon,hair (FighterRig.MeasureKeyKinds)
        }

        const string Key = "RoeFighter.RoeClothPlayDemo";

        static RoeClothPlayDemo()
        {
            EditorApplication.playModeStateChanged += OnPlayMode;
        }

        public static void Run()
        {
            var size = RoeCapture.Arg("-roeSize", "960x720").Split('x');
            var job = new Job
            {
                chars = RoeCapture.Arg("-roeChars", "a08,kas011").Split(','),
                cloths = RoeCapture.Arg("-roeCloths", "magica,magica_style,off").Split(','),
                outDir = RoeCapture.Arg("-roeOut", Path.Combine(Path.GetDirectoryName(Application.dataPath), "_work", "play_demo")),
                view = RoeCapture.Arg("-roeView", "front"),
                stage = RoeCapture.Arg("-roeStage", "e23_steel_s02"),
                width = int.Parse(size[0]), height = int.Parse(size[1]),
                noFrames = Environment.GetCommandLineArgs().Contains("-roeNoFrames"),
                script = RoeCapture.Arg("-roeScript", "moves"),
                ignoreKeys = RoeCapture.Arg("-roeSkirtKeys", "on") == "off",
                measureKinds = RoeCapture.Arg("-roeMeasureKeys", ""),
            };
            SessionState.SetString(Key, JsonUtility.ToJson(job));
            ShaderUtil.allowAsyncCompilation = false;
            EditorSceneManager.OpenScene(RoeFightScene.ScenePath(job.stage), OpenSceneMode.Single);
            Debug.Log($"[ROE] play demo: {string.Join(",", job.chars)} x {string.Join(",", job.cloths)} -> {job.outDir}; entering play mode");
            EditorApplication.EnterPlaymode();
        }

        static void OnPlayMode(PlayModeStateChange mode)
        {
            var json = SessionState.GetString(Key, "");
            if (json.Length == 0)
                return;
            var job = JsonUtility.FromJson<Job>(json);
            if (mode == PlayModeStateChange.EnteredPlayMode && !job.done)
                Begin(job);
            else if (mode == PlayModeStateChange.EnteredEditMode && job.done)
            {
                SessionState.EraseString(Key);
                if (Application.isBatchMode)
                    EditorApplication.Exit(0);
            }
        }

        // ---- the run, in play mode

        struct StepSystem { }
        struct CaptureSystem { }

        static Job current;
        static FightGame game;
        static Fighter me;
        static int take, step, shot, who, warm;
        static bool running, captureNow, building;
        static Vector3 look;
        static string dir, label;
        static Meter meter;
        static BreastMeter breastMeter;
        static HairMeter hairMeter;
        static bool nude, hairMeterOn;
        static List<List<Transform>> chestMarks = new List<List<Transform>>();   // the breasts' bones (soft-body nodes): the chest view's aim
        static readonly System.Text.StringBuilder breastTable = new System.Text.StringBuilder();
        static readonly System.Text.StringBuilder hairTable = new System.Text.StringBuilder();
        static readonly Dictionary<SkinnedMeshRenderer, Mesh> meshes = new Dictionary<SkinnedMeshRenderer, Mesh>();
        static readonly List<string> takes = new List<string>();
        static readonly System.Text.StringBuilder log = new System.Text.StringBuilder();
        static readonly System.Text.StringBuilder metrics = new System.Text.StringBuilder();

        static void Begin(Job job)
        {
            current = job;
            game = Object.FindFirstObjectByType<FightGame>();
            if (game == null)
            {
                Debug.LogError("[ROE] play demo: no FightGame in the scene");
                Finish();
                return;
            }
            // the fight is stepped from here; its own Update (keyboard, real time) and Start (select screen) stay off
            game.enabled = false;
            game.cpu = new[] { false, false };
            if (game.hud != null)
                game.hud.gameObject.SetActive(false);
            Time.captureFramerate = 60;
            FighterRig.IgnoreSkirtKeys = job.ignoreKeys;
            FighterRig.MeasureKeysLate = true;     // Magica writes its bones after the step: measured in OnCapture
            FighterRig.MeasureKeyKinds.Clear();
            foreach (var kind in (job.measureKinds ?? "").Split(','))
                if (kind.Trim().Length > 0)
                    FighterRig.MeasureKeyKinds.Add(kind.Trim());
            Directory.CreateDirectory(job.outDir);
            take = -1;
            running = true;
            takes.Clear();
            log.Clear().Append("[ROE] play demo:");
            metrics.Clear().Append("char\tsetup\tpart\tframes\tinside %\tdeepest cm\tjerk mm\tworst jerk mm\tstretch %\tworst stretch %\taway cm\tworst away cm\tturned %\n");
            breastTable.Clear().Append("char\tsetup\tpart\tframes\tmean cm\tmax cm\tspeed cm/s\n");
            hairTable.Clear().Append("char\tsetup\tpart\tframes\thanging hair verts\tinside %\tdeepest cm\n");
            hairMeterOn = job.view == "hair" || Environment.GetCommandLineArgs().Contains("-roeHairMeter");
            Hook(true);
            NextTake();
        }

        /// <summary>Our two systems in the player loop (put in after whatever is there - Magica's own - and taken out again).</summary>
        static void Hook(bool on)
        {
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            var systems = loop.subSystemList.ToList();
            for (int i = 0; i < systems.Count; i++)
            {
                var category = systems[i];
                var list = (category.subSystemList ?? new PlayerLoopSystem[0]).Where(s => s.type != typeof(StepSystem) && s.type != typeof(CaptureSystem)).ToList();
                if (on && category.type.Name == "Update")
                    list.Add(new PlayerLoopSystem { type = typeof(StepSystem), updateDelegate = OnStep });
                if (on && category.type.Name == "PostLateUpdate")
                    list.Insert(0, new PlayerLoopSystem { type = typeof(CaptureSystem), updateDelegate = OnCapture });
                category.subSystemList = list.ToArray();
                systems[i] = category;
            }
            loop.subSystemList = systems.ToArray();
            PlayerLoop.SetPlayerLoop(loop);
        }

        static void NextTake()
        {
            take++;
            int total = current.chars.Length * current.cloths.Length;
            if (take >= total)
            {
                File.WriteAllText(Path.Combine(current.outDir, "script.txt"), string.Join("\n", Script.Select(x =>
                    $"{x.from.ToString("F1", CultureInfo.InvariantCulture)} {x.what}")));
                File.WriteAllText(Path.Combine(current.outDir, "takes.txt"), string.Join("\n", takes));
                File.WriteAllText(Path.Combine(current.outDir, "metrics.txt"), metrics.ToString());
                File.WriteAllText(Path.Combine(current.outDir, "breasts.txt"), breastTable.ToString());
                if (hairMeterOn)
                    File.WriteAllText(Path.Combine(current.outDir, "hair.txt"), hairTable.ToString());
                Debug.Log(log.ToString());
                Finish();
                return;
            }
            // "magica:edge=1;gravity=3": the setup, and Magica mesh settings over its defaults (the take named after them)
            string id = current.chars[take / current.cloths.Length], entry = current.cloths[take % current.cloths.Length];
            int colon = entry.IndexOf(':');
            string variant = colon < 0 ? entry : entry.Substring(0, colon), tuning = colon < 0 ? "" : entry.Substring(colon + 1);
            label = colon < 0 ? entry : variant + "_" + System.Text.RegularExpressions.Regex.Replace(tuning, "[^A-Za-z0-9.]+", "_").Trim('_');
            // "nohip": the skirt does not follow the legs or the pelvis's turn (FighterRig.NoHipCloth), the rest is for Magica
            var words = tuning.Split(';').ToList();
            FighterRig.NoHipCloth = words.Remove("nohip");
            // "nude": every outfit piece that can come off is off for the take (the body under them, burst or not)
            nude = words.Remove("nude");
            // "keys": this take keeps the game's keys in her game clips although the run leaves them to the solver
            // (-roeSkirtKeys off) - "off:keys" is the game's own look, the reference column of a comparison
            FighterRig.IgnoreSkirtKeys = current.ignoreKeys && !words.Remove("keys");
            tuning = string.Join(";", words);
#if MAGICACLOTH2
            RoeMagicaCloth.Tuning = tuning;
#endif
            FighterRig.ClothBackend = variant;
            foreach (var rig in game.roster.Length > 0 ? game.roster : game.rigs)
                if (rig != null)
                    rig.useCloth = true;
            if (game.roster.Length > 0 && game.roster[game.pick[0]].id != id && game.roster[game.pick[1]].id != id)
                game.PickIds(null, id);
            game.Setup();
            foreach (var rig in game.rigs)
                foreach (var smr in rig.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    smr.forceMatrixRecalculationPerRender = true;
            // the intro in one go (nothing filmed), then the cloth starts from the pose
            for (int guard = 0; game.phase != FightGame.Phase.Fight && guard < 3600; guard++)
                game.Step(new FighterInput[2]);
            who = game.f[0].rig.id == id ? 0 : 1;
            me = game.f[who];
            chestMarks = RoeBodyCheck.Breasts(me.rig.animator, out _).Select(b => b.moved).ToList();
            me.foe.pos = me.pos + (me.foe.pos - me.pos).normalized * 5.5f;
            me.foe.Place();
            if (nude && me.rig.burst != null)
                me.rig.burst.Show(me.rig.burst.stages);
            me.rig.ResetCloth();
            foreach (var rig in game.rigs)
            {
                rig.ResetSkirtKeyError();
                rig.ResetChainKeyError();
            }
            string about = $"{me.rig.ClothTitle}; {me.rig.ClothReport}";
            takes.Add($"{id}\t{label}\t{about}\t" + string.Join("|", (me.moves ?? game.moves).Select(m => $"{m.button}={m.name}")));
            log.Append($"\n[ROE]   {id} {label}: {about}");
            dir = Path.Combine(current.outDir, $"{id}_{label}");
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            step = 0;
            shot = 0;
            look = Aim();
            // Magica builds its components in the background: the take starts once they run (the cloth pieces' own meshes
            // kept from now, before a MeshCloth puts its copy on their renderers: the meter's lengths at rest)
            building = true;
            warm = 0;
            meter?.Dispose();
            meter = null;
            breastMeter = null;
            hairMeter?.Dispose();
            hairMeter = null;
            meshes.Clear();
            if (me.rig.burst != null)
                foreach (var p in me.rig.burst.pieces.Where(p => p.cloth && p.renderer != null && p.renderer.sharedMesh != null))
                    meshes[p.renderer] = p.renderer.sharedMesh;
        }

        static bool ClothReady()
        {
#if MAGICACLOTH2
            var magica = me.rig.ClothPart<RoeMagicaCloth>();
            return magica == null || magica.Ready;
#else
            return true;
#endif
        }

        static string ClothFailures()
        {
#if MAGICACLOTH2
            var magica = me.rig.ClothPart<RoeMagicaCloth>();
            return magica != null ? magica.Failures : "";
#else
            return "";
#endif
        }

        static void Finish()
        {
            running = false;
            Time.captureFramerate = 0;
            FighterRig.IgnoreSkirtKeys = false;
            FighterRig.MeasureKeysLate = false;
            FighterRig.MeasureKeyKinds.Clear();
            Hook(false);
            if (current != null)
            {
                current.done = true;
                SessionState.SetString(Key, JsonUtility.ToJson(current));
            }
            EditorApplication.ExitPlaymode();
        }

        static bool Close => current.view.StartsWith("back");
        static bool ChestView => current.view == "chest";
        static bool HairView => current.view == "hair";

        static Vector3 Aim()
        {
            var animator = me.rig.animator;
            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            if (ChestView)
            {
                // the breasts' roots (a ROE rig's humanoid Chest is Spine1, at the belly)
                if (chestMarks.Count > 0)
                {
                    var mid = Vector3.zero;
                    foreach (var mark in chestMarks)
                        mid += mark.Aggregate(Vector3.zero, (a, t) => a + t.position) / Mathf.Max(1, mark.Count);
                    return mid / chestMarks.Count + me.Forward * 0.04f + Vector3.up * 0.03f;
                }
                var chest = animator.GetBoneTransform(HumanBodyBones.UpperChest) ?? animator.GetBoneTransform(HumanBodyBones.Chest) ?? hips;
                return chest.position + me.Forward * 0.08f;
            }
            if (HairView)
            {
                var head = animator.GetBoneTransform(HumanBodyBones.Head);
                var neck = animator.GetBoneTransform(HumanBodyBones.Neck) ?? head;
                return (head.position + neck.position) * 0.5f - Vector3.up * 0.12f;
            }
            if (FollowView)
                return new Vector3(me.pos.x, Mathf.Max(me.pos.y + 0.95f, hips.position.y), me.pos.z);
            return Close ? new Vector3(me.pos.x, hips.position.y - 0.12f, me.pos.z) : me.pos + Vector3.up * 0.95f;
        }

        /// <summary>-roeView follow: the front view, its aim rising with her hips (Goddess Luf flies up in her skills).</summary>
        static bool FollowView => current.view == "follow";

        /// <summary>After Update: one step of the fight, then the camera (in front of her, a little to the side, following softly; or behind her at the hips).</summary>
        static void OnStep()
        {
            if (!running || me == null)
                return;
            try
            {
                StepTake();
            }
            catch (Exception e)
            {
                // a take that throws is cut short (it would throw every frame and the run never end)
                Debug.LogException(e);
                log.Append($"\n[ROE]   {me.rig.id} {label}: stopped at step {step} by {e.GetType().Name}: {e.Message}");
                try
                {
                    NextTake();
                }
                catch (Exception e2)
                {
                    Debug.LogException(e2);
                    Finish();
                }
            }
        }

        static void StepTake()
        {
            if (building)
            {
                if (!ClothReady() && warm++ < 600)
                    return;
                building = false;
                me.rig.ResetCloth();
                string failed = ClothFailures();
                log.Append($"\n[ROE]   {me.rig.id} {label}: cloth ready after {warm} frames{(failed.Length > 0 ? $"; FAILED: {failed}" : "")}");
                meter = new Meter(me.rig, meshes);
                breastMeter = new BreastMeter(me.rig);
                if (hairMeterOn)
                    hairMeter = new HairMeter(me.rig);
            }
            float t = step / 60f;
            if (t > Length)
            {
                log.Append($"\n[ROE]   {me.rig.id} {label}: {shot} frames to {dir}; after: {me.rig.ClothReport}" +
                           (FighterRig.IgnoreSkirtKeys ? $"; skirt against the game's keys: {me.rig.SkirtKeyError}" : "") +
                           (FighterRig.MeasureKeyKinds.Count > 0 ? $"; {string.Join("+", FighterRig.MeasureKeyKinds)} against the game's keys: {me.rig.ChainKeyError}" : ""));
                if (meter != null)
                {
                    log.Append(meter.Report($"[ROE]     {me.rig.id} {label}"));
                    metrics.Append(meter.Table(me.rig.id, label));
                    meter.Dispose();
                    meter = null;
                }
                if (breastMeter != null)
                {
                    log.Append(breastMeter.Report($"[ROE]     {me.rig.id} {label}"));
                    breastTable.Append(breastMeter.Table(me.rig.id, label));
                    breastMeter = null;
                }
                if (hairMeter != null)
                {
                    log.Append(hairMeter.Report($"[ROE]     {me.rig.id} {label}"));
                    hairTable.Append(hairMeter.Table(me.rig.id, label));
                    hairMeter.Dispose();
                    hairMeter = null;
                }
                NextTake();
                return;
            }
            var input = default(FighterInput);
            foreach (var (from, to, inp, _) in Script)
                if (from == to ? Mathf.Abs(t - from) < 0.5f / 60f : t >= from && t < to)
                    input = inp;
            if (input.s1 || input.s2 || input.s3)
                me.meter = 100f;            // a skill whatever the meter
            RoeClothDemo.PlayScriptClips(Script, t, me);
            var cam = game.cam;
            var camRight = cam.transform.right;
            camRight.y = 0f;
            input.x *= Vector3.Dot(me.foe.pos - me.pos, camRight) >= 0f ? 1 : -1;
            var inputs = new FighterInput[2];
            inputs[who] = input;
            game.Step(inputs);
            float sideways = current.view == "backleft" ? -1f : 1f;
            var fwd = me.Forward;
            var side = Vector3.Cross(Vector3.up, fwd).normalized;
            look = Vector3.Lerp(look, Aim(), step == 0 ? 1f : ChestView || HairView ? 0.15f : 0.08f);
            cam.fieldOfView = Close ? 28f : ChestView ? 26f : 30f;
            cam.transform.position = Close
                ? look + (-fwd * 0.8f + side * (0.6f * sideways)).normalized * 1.9f + Vector3.up * 0.1f
                : ChestView ? look + (fwd * 0.9f - side * 0.35f).normalized * 1.45f + Vector3.up * 0.06f
                : HairView ? look + (-fwd * 0.85f + side * 0.45f).normalized * 1.7f + Vector3.up * 0.3f
                : look + (fwd * 0.8f - side * 0.6f).normalized * (FollowView ? 4.2f : 3.6f) + Vector3.up * 0.2f;
            cam.transform.LookAt(look, Vector3.up);
            captureNow = step % 2 == 0;
            step++;
        }

        /// <summary>At the start of PostLateUpdate (Magica has written the bones in PreLateUpdate): the frame, every other step.</summary>
        static void OnCapture()
        {
            if (!running || building || me == null)
                return;
            // the skirt and chains against the game's keys, on the pose Magica left (FighterRig.MeasureKeysLate)
            if (step > 0)
                me.rig.MeasureKeysNow();
            // every step measured (the frame's pose: the step before the counter moved on)
            if (meter != null && step > 0)
            {
                try
                {
                    meter.Sample(PartAt((step - 1) / 60f));
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    meter.Dispose();
                    meter = null;
                }
            }
            if (step > 0)
            {
                try
                {
                    breastMeter?.Sample(PartAt((step - 1) / 60f));
                    if (hairMeter != null && step % 2 == 0)
                        hairMeter.Sample(PartAt((step - 1) / 60f));
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    breastMeter = null;
                    hairMeter?.Dispose();
                    hairMeter = null;
                }
            }
            if (!captureNow)
                return;
            captureNow = false;
            if (current.noFrames)
                return;
            if (shot == 0)
                RoeCapture.Render(game.cam, 240, 180, Path.Combine(current.outDir, "_warm.jpg"), 80);
            RoeCapture.Render(game.cam, current.width, current.height, Path.Combine(dir, $"{shot:D5}.jpg"), 90);
            shot++;
        }

        static (float from, float to, FighterInput input, string what)[] Script => RoeClothDemo.Named(current?.script).script;

        static float Length => RoeClothDemo.Named(current?.script).length;

        static string PartAt(float t)
        {
            string what = Script[0].what;
            foreach (var x in Script)
                if (t >= x.from - 0.5f / 60f)
                    what = x.what;
            return what;
        }


        /// <summary>A part of the script and what was measured in it (the two side steps as one part, then all of it).</summary>
        class Tally
        {
            public string name;
            public int frames, count;
            public double sum, sum2;
            public float max;
            public long extra;

            public static List<Tally> Merged(List<Tally> parts)
            {
                Tally Sum(string name, IEnumerable<Tally> of)
                {
                    var list = of.ToList();
                    return new Tally
                    {
                        name = name, frames = list.Sum(p => p.frames), count = list.Sum(p => p.count), sum = list.Sum(p => p.sum),
                        sum2 = list.Sum(p => p.sum2), max = list.Count > 0 ? list.Max(p => p.max) : 0f, extra = list.Sum(p => p.extra),
                    };
                }
                var merged = parts.GroupBy(p => p.name).Select(g => Sum(g.Key, g)).ToList();
                if (parts.Count > 0)
                    merged.Add(Sum("all", parts));
                return merged;
            }
        }

        /// <summary>
        /// The breasts against the chest, whatever moves them (the solver, the game clip's keys): a point 8 cm in front of each
        /// breast's root, carried by its bone (a DOA6 soft body: the middle of its nodes), in the chest bone's frame; per part of
        /// the script how far it is from where it was at the start of the take (mean and most, cm) and how fast it moves
        /// against the chest (RMS, cm/s).  The breasts' bones: RoeBodyCheck.Breasts.
        /// </summary>
        class BreastMeter
        {
            readonly Transform chest;
            readonly List<(RoeBodyCheck.Breast b, Vector3 local)> breasts = new List<(RoeBodyCheck.Breast, Vector3)>();
            readonly Vector3[] rest, prev;
            readonly List<Tally> parts = new List<Tally>();
            bool first = true;

            public BreastMeter(FighterRig rig)
            {
                var animator = rig.animator;
                chest = animator.GetBoneTransform(HumanBodyBones.UpperChest) ?? animator.GetBoneTransform(HumanBodyBones.Chest) ?? animator.GetBoneTransform(HumanBodyBones.Spine);
                Vector3 Bone(HumanBodyBones h) => (animator.GetBoneTransform(h) ?? animator.GetBoneTransform(HumanBodyBones.Head)).position;
                var right = (Bone(HumanBodyBones.RightUpperArm) - Bone(HumanBodyBones.LeftUpperArm)).normalized;
                var up = (Bone(HumanBodyBones.Neck) - Bone(HumanBodyBones.Spine)).normalized;
                var forward = Vector3.Cross(right, up).normalized;
                foreach (var b in RoeBodyCheck.Breasts(animator, out _))
                    breasts.Add((b, b.moved.Count == 1 ? b.moved[0].InverseTransformPoint(b.pivot + forward * 0.08f) : Vector3.zero));
                rest = new Vector3[breasts.Count];
                prev = new Vector3[breasts.Count];
            }

            Vector3 Tip(int k)
            {
                var (b, local) = breasts[k];
                Vector3 world;
                if (b.moved.Count == 1)
                    world = b.moved[0].TransformPoint(local);
                else
                {
                    world = Vector3.zero;
                    foreach (var t in b.moved)
                        world += t.position;
                    world /= b.moved.Count;
                }
                return Quaternion.Inverse(chest.rotation) * (world - chest.position);
            }

            public void Sample(string partName)
            {
                if (breasts.Count == 0 || chest == null)
                    return;
                var part = parts.LastOrDefault();
                if (part == null || part.name != partName)
                    parts.Add(part = new Tally { name = partName });
                for (int k = 0; k < breasts.Count; k++)
                {
                    var p = Tip(k);
                    if (first)
                        rest[k] = prev[k] = p;
                    float d = (p - rest[k]).magnitude * 100f;
                    float v = (p - prev[k]).magnitude * 100f * 60f;
                    part.sum += d;
                    part.max = Mathf.Max(part.max, d);
                    part.sum2 += v * v;
                    part.count++;
                    prev[k] = p;
                }
                part.frames++;
                first = false;
            }

            public string Report(string prefix)
            {
                var sb = new System.Text.StringBuilder($"\n{prefix}: breasts ({breasts.Count}: {string.Join(", ", breasts.Select(x => x.b.name))}) against the chest - mean cm (most) / speed cm/s:");
                foreach (var p in Tally.Merged(parts))
                {
                    int n = Mathf.Max(1, p.count);
                    sb.Append($"\n{prefix}   {p.name,-9} {p.sum / n,5:F2} ({p.max,5:F2})  {Math.Sqrt(p.sum2 / n),6:F1}");
                }
                return sb.ToString();
            }

            public string Table(string id, string setup)
            {
                var sb = new System.Text.StringBuilder();
                foreach (var p in Tally.Merged(parts))
                {
                    int n = Mathf.Max(1, p.count);
                    sb.Append(string.Join("\t", id, setup, p.name, p.frames.ToString(), (p.sum / n).ToString("F3", CultureInfo.InvariantCulture),
                        p.max.ToString("F2", CultureInfo.InvariantCulture), Math.Sqrt(p.sum2 / n).ToString("F2", CultureInfo.InvariantCulture))).Append('\n');
                }
                return sb.ToString();
            }
        }

        /// <summary>
        /// The hair that hangs (vertices of the hair meshes more than 12 cm from the head bone) against the body and what she
        /// wears (every other skinned mesh but the head's, as drawn - pieces taken off do not count): a hair vertex is inside when
        /// the nearest body or clothes vertex within 3 cm has it more than 1 cm behind its surface (along its normal).  Per part
        /// of the script: the share of hanging hair vertices inside, and the deepest (cm).
        /// </summary>
        class HairMeter : IDisposable
        {
            static readonly System.Text.RegularExpressions.Regex HairName = new System.Text.RegularExpressions.Regex(@"hair|ponytail|bangs",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            static readonly System.Text.RegularExpressions.Regex HeadName = new System.Text.RegularExpressions.Regex(@"head|face",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            readonly List<SkinnedMeshRenderer> hair = new List<SkinnedMeshRenderer>(), body = new List<SkinnedMeshRenderer>();
            readonly Transform head;
            readonly Mesh baked = new Mesh();
            readonly List<Vector3> points = new List<Vector3>(), normals = new List<Vector3>();
            readonly Dictionary<Vector3Int, List<int>> grid = new Dictionary<Vector3Int, List<int>>();
            readonly List<Tally> parts = new List<Tally>();
            const float Cell = 0.03f;

            public HairMeter(FighterRig rig)
            {
                head = rig.animator.GetBoneTransform(HumanBodyBones.Head);
                foreach (var smr in rig.GetComponentsInChildren<SkinnedMeshRenderer>(false).Where(x => x.sharedMesh != null))
                {
                    if (HairName.IsMatch(smr.name))
                        hair.Add(smr);
                    else if (!HeadName.IsMatch(smr.name))
                        body.Add(smr);
                }
            }

            public void Dispose() => Object.DestroyImmediate(baked);

            public void Sample(string partName)
            {
                if (head == null || hair.Count == 0)
                    return;
                points.Clear();
                normals.Clear();
                foreach (var list in grid.Values)
                    list.Clear();
                foreach (var smr in body)
                {
                    if (!smr.enabled || !smr.gameObject.activeInHierarchy)
                        continue;
                    smr.BakeMesh(baked, true);
                    var m = smr.transform.localToWorldMatrix;
                    var v = baked.vertices;
                    var n = baked.normals;
                    for (int i = 0; i < v.Length && i < n.Length; i++)
                    {
                        var p = m.MultiplyPoint3x4(v[i]);
                        var key = Vector3Int.FloorToInt(p / Cell);
                        if (!grid.TryGetValue(key, out var cell))
                            grid[key] = cell = new List<int>();
                        cell.Add(points.Count);
                        points.Add(p);
                        normals.Add(m.MultiplyVector(n[i]).normalized);
                    }
                }
                var part = parts.LastOrDefault();
                if (part == null || part.name != partName)
                    parts.Add(part = new Tally { name = partName });
                int hanging = 0, inside = 0;
                float deepest = 0f;
                var h0 = head.position;
                foreach (var smr in hair)
                {
                    if (!smr.enabled || !smr.gameObject.activeInHierarchy)
                        continue;
                    smr.BakeMesh(baked, true);
                    var m = smr.transform.localToWorldMatrix;
                    var v = baked.vertices;
                    for (int i = 0; i < v.Length; i++)
                    {
                        var p = m.MultiplyPoint3x4(v[i]);
                        if ((p - h0).sqrMagnitude < 0.12f * 0.12f)
                            continue;
                        hanging++;
                        var c = Vector3Int.FloorToInt(p / Cell);
                        int best = -1;
                        float bestD = Cell * Cell;
                        for (int x = -1; x <= 1; x++)
                            for (int y = -1; y <= 1; y++)
                                for (int z = -1; z <= 1; z++)
                                    if (grid.TryGetValue(new Vector3Int(c.x + x, c.y + y, c.z + z), out var cell))
                                        foreach (int k in cell)
                                        {
                                            float d2 = (points[k] - p).sqrMagnitude;
                                            if (d2 < bestD)
                                            {
                                                bestD = d2;
                                                best = k;
                                            }
                                        }
                        if (best < 0)
                            continue;
                        float signed = Vector3.Dot(p - points[best], normals[best]);
                        if (signed < -0.01f)
                        {
                            inside++;
                            deepest = Mathf.Max(deepest, -signed);
                        }
                    }
                }
                part.frames++;
                part.count += hanging;
                part.extra += inside;
                part.sum += hanging > 0 ? (double)inside / hanging : 0.0;
                part.max = Mathf.Max(part.max, deepest * 100f);
            }

            public string Report(string prefix)
            {
                var sb = new System.Text.StringBuilder($"\n{prefix}: hanging hair ({string.Join(", ", hair.Select(h => h.name))}) inside the body or clothes - % (deepest cm):");
                foreach (var p in Tally.Merged(parts))
                    sb.Append($"\n{prefix}   {p.name,-9} {100.0 * p.sum / Mathf.Max(1, p.frames),5:F2} ({p.max,4:F1})  of {p.count / Mathf.Max(1, p.frames)} hanging verts a frame");
                return sb.ToString();
            }

            public string Table(string id, string setup)
            {
                var sb = new System.Text.StringBuilder();
                foreach (var p in Tally.Merged(parts))
                    sb.Append(string.Join("\t", id, setup, p.name, p.frames.ToString(), (p.count / Mathf.Max(1, p.frames)).ToString(),
                        (100.0 * p.sum / Mathf.Max(1, p.frames)).ToString("F2", CultureInfo.InvariantCulture),
                        p.max.ToString("F2", CultureInfo.InvariantCulture))).Append('\n');
                return sb.ToString();
            }
        }

        /// <summary>
        /// The cloth measured the same way whatever moves it: its vertices (the outfit's cloth pieces, RoeClothesBurst;
        /// a DOA6 grid cloth's rebuilt surfaces) every step, per part of the script -
        ///   inside   the share of them more than 1 cm inside the body (RoeBodySurface: the skin of the legs and hips,
        ///            and what is worn on the legs), and the deepest (cm);
        ///   jerk     how far they are from going on as they went (|p - 2 p' + p''| in the hips' frame, mm a step): small
        ///            for a swing, large for jitter and snaps;
        ///   stretch  of the mesh's edges (3 mm and longer) against their length as modelled (a DOA6 surface: at the start
        ///            of the take) - mean of the positive, %; worst: the edge 1 in 100 is above, in the worst step;
        ///   away     from the pose the animation gives the piece (a hidden copy of it on its own mesh and bones, baked each
        ///            step: what "off" shows), mean cm and the worst step's mean;
        ///   turned   the share of its triangles facing more than 90 degrees away from the animation's: cloth turned over
        ///            or twisted (an outfit piece only).
        /// </summary>
        class Meter : IDisposable
        {
            readonly RoeBodySurface body;
            readonly List<(SkinnedMeshRenderer smr, MeshFilter filter, int offset)> sources = new List<(SkinnedMeshRenderer, MeshFilter, int)>();
            readonly List<(SkinnedMeshRenderer copy, int offset, int[] triangles)> anims = new List<(SkinnedMeshRenderer, int, int[])>();
            readonly Transform hips;
            readonly Mesh baked = new Mesh();
            readonly Vector3[] world, animated, q1, q2;
            readonly int[] ea, eb;
            readonly float[] rest, strains, edgeStrain;
            int seen, animatedCount, triangleCount;
            float worstSeen;
            readonly List<(string part, int step, float worst, float jerk)> spikes = new List<(string, int, float, float)>();   // steps whose worst stretch passed 200 %
            string worstAt = "";
            List<string> worstEdges = new List<string>();
            readonly List<(string name, int offset, int count, Transform[] bones, BoneWeight[] weights)> names = new List<(string, int, int, Transform[], BoneWeight[])>();

            class Part
            {
                public string name;
                public int frames;
                public double inside, jerk, stretch, away, turned;
                public float deepest, worstJerk, worstStretch, worstAway;
            }

            readonly List<Part> parts = new List<Part>();

            public Meter(FighterRig rig, IDictionary<SkinnedMeshRenderer, Mesh> own)
            {
                hips = rig.animator.GetBoneTransform(HumanBodyBones.Hips);
                body = new RoeBodySurface(rig.animator);
                int n = 0;
                var edges = new Dictionary<long, float>();       // edge -> its length as modelled (-1: unknown, the first step's)
                void Add(SkinnedMeshRenderer smr, MeshFilter filter, Mesh mesh)
                {
                    if (mesh == null || !mesh.isReadable)
                        return;
                    sources.Add((smr, filter, n));
                    var bones = smr != null ? smr.bones.Take(mesh.bindposes.Length).ToArray() : null;   // (a MeshCloth adds its renderer)
                    names.Add(((smr != null ? (Component)smr : filter).name, n, mesh.vertexCount, bones, smr != null ? mesh.boneWeights : null));
                    var tri = mesh.triangles;
                    var v = smr != null ? mesh.vertices : null;
                    float scale = smr != null ? smr.transform.lossyScale.x : 1f;
                    for (int k = 0; k + 2 < tri.Length; k += 3)
                        for (int e = 0; e < 3; e++)
                        {
                            int i = tri[k + e], j = tri[k + (e + 1) % 3];
                            long a = n + i, b = n + j;
                            edges[a < b ? (a << 32) | b : (b << 32) | a] = v != null ? (v[i] - v[j]).magnitude * scale : -1f;
                        }
                    if (smr != null)
                    {
                        // the piece as the animation poses it: a copy on the same bones, never drawn
                        var go = new GameObject($"meter {smr.name}") { hideFlags = HideFlags.HideAndDontSave };
                        go.transform.SetParent(smr.transform.parent, false);
                        go.transform.localPosition = smr.transform.localPosition;
                        go.transform.localRotation = smr.transform.localRotation;
                        go.transform.localScale = smr.transform.localScale;
                        var copy = go.AddComponent<SkinnedMeshRenderer>();
                        copy.sharedMesh = mesh;
                        copy.bones = bones;
                        copy.rootBone = smr.rootBone;
                        copy.forceRenderingOff = true;
                        anims.Add((copy, n, tri.Select(i => n + i).ToArray()));
                        animatedCount += mesh.vertexCount;
                        triangleCount += tri.Length / 3;
                    }
                    n += mesh.vertexCount;
                }
                if (rig.burst != null)
                    foreach (var p in rig.burst.pieces.Where(p => p.cloth && p.renderer != null))
                        Add(p.renderer, null, own != null && own.TryGetValue(p.renderer, out var m) ? m : p.renderer.sharedMesh);
                var doa = rig.animator.GetComponent<RoeDoaRig>();
                if (doa != null)
                    foreach (var s in doa.surfaces.Where(s => s.filter != null))
                        Add(null, s.filter, s.filter.sharedMesh);
                world = new Vector3[n];
                animated = new Vector3[n];
                q1 = new Vector3[n];
                q2 = new Vector3[n];
                ea = edges.Keys.Select(e => (int)(e >> 32)).ToArray();
                eb = edges.Keys.Select(e => (int)(e & 0xffffffff)).ToArray();
                rest = edges.Values.ToArray();
                strains = new float[ea.Length];
                edgeStrain = new float[ea.Length];
            }

            public void Dispose()
            {
                foreach (var (copy, _, _) in anims)
                    if (copy != null)
                        Object.DestroyImmediate(copy.gameObject);
                anims.Clear();
                Object.DestroyImmediate(baked);
            }

            /// <summary>A cloth vertex as "piece#index:the bone it hangs on most(weight)".</summary>
            string Where(int v)
            {
                foreach (var (name, offset, count, bones, weights) in names)
                    if (v >= offset && v < offset + count)
                    {
                        int i = v - offset;
                        var w = weights != null && i < weights.Length ? weights[i] : default;
                        string bone = bones != null && weights != null && w.boneIndex0 < bones.Length && bones[w.boneIndex0] != null ? bones[w.boneIndex0].name : "?";
                        return $"{name}#{i}:{bone}({w.weight0:F1})";
                    }
                return $"#{v}";
            }

            static void Bake(SkinnedMeshRenderer smr, Mesh into, Vector3[] to, int offset)
            {
                smr.BakeMesh(into, true);
                var v = into.vertices;
                var toWorld = smr.transform.localToWorldMatrix;
                for (int i = 0; i < v.Length && offset + i < to.Length; i++)
                    to[offset + i] = toWorld.MultiplyPoint3x4(v[i]);
            }

            void Read()
            {
                foreach (var (smr, filter, offset) in sources)
                {
                    if (smr != null)
                    {
                        Bake(smr, baked, world, offset);
                        continue;
                    }
                    var v = filter.sharedMesh.vertices;
                    var toWorld = filter.transform.localToWorldMatrix;
                    for (int i = 0; i < v.Length && offset + i < world.Length; i++)
                        world[offset + i] = toWorld.MultiplyPoint3x4(v[i]);
                }
                foreach (var (copy, offset, _) in anims)
                    Bake(copy, baked, animated, offset);
            }

            public void Sample(string partName)
            {
                if (world.Length == 0)
                    return;
                Read();
                body.Pose();
                var part = parts.LastOrDefault();
                if (part == null || part.name != partName)
                    parts.Add(part = new Part { name = partName });
                // inside the body
                int inside = 0;
                float deepest = 0f;
                for (int i = 0; i < world.Length; i++)
                    if (body.Signed(world[i], Vector3.zero, 0f, out float d, out _) && d < -0.01f)
                    {
                        inside++;
                        deepest = Mathf.Max(deepest, -d);
                    }
                // jerk, in the hips' frame
                var toHips = Quaternion.Inverse(hips.rotation);
                double jerk = 0;
                for (int i = 0; i < world.Length; i++)
                {
                    var q = toHips * (world[i] - hips.position);
                    if (seen >= 2)
                        jerk += (q - 2f * q1[i] + q2[i]).magnitude;
                    q2[i] = q1[i];
                    q1[i] = q;
                }
                // stretch of the edges (against the mesh as modelled; a rebuilt DOA6 surface: against its first step)
                if (seen == 0)
                    for (int e = 0; e < ea.Length; e++)
                        if (rest[e] < 0f)
                            rest[e] = (world[ea[e]] - world[eb[e]]).magnitude;
                double stretch = 0;
                float worst = 0f;
                int used = 0;
                for (int e = 0; e < ea.Length; e++)
                {
                    // (edges shorter than 3 mm left out: seams and folds where a millimetre more is "200 %")
                    if (rest[e] < 0.003f)
                        continue;
                    float s = (world[ea[e]] - world[eb[e]]).magnitude / rest[e] - 1f;
                    strains[used++] = s;
                    edgeStrain[e] = s;
                    if (s > 0f)
                        stretch += s;
                }
                // the worst: the edge 1 in 100 is above (a few odd edges - a vertex skinned elsewhere - do not decide it)
                if (used > 0)
                {
                    Array.Sort(strains, 0, used);
                    worst = strains[(int)(0.99f * (used - 1))];
                }
                if (worst > 2f)
                    spikes.Add((partName, seen, worst, seen >= 2 ? (float)(jerk / world.Length) : 0f));
                // where: the most stretched edges of the worst step so far
                if (worst > worstSeen)
                {
                    worstSeen = worst;
                    worstAt = $"{partName} step {seen}";
                    worstEdges = Enumerable.Range(0, ea.Length).Where(e => rest[e] >= 0.003f).OrderByDescending(e => edgeStrain[e]).Take(4)
                        .Select(e => $"{Where(ea[e])}-{Where(eb[e])} {rest[e] * 1000f:F0}->{(world[ea[e]] - world[eb[e]]).magnitude * 1000f:F0} mm").ToList();
                }
                // away from the animation's pose, and turned over against it
                double away = 0;
                int turned = 0;
                foreach (var (_, _, tri) in anims)
                {
                    for (int k = 0; k + 2 < tri.Length; k += 3)
                    {
                        int a = tri[k], b = tri[k + 1], c = tri[k + 2];
                        var ns = Vector3.Cross(world[b] - world[a], world[c] - world[a]);
                        var na = Vector3.Cross(animated[b] - animated[a], animated[c] - animated[a]);
                        if (Vector3.Dot(ns, na) < 0f)
                            turned++;
                    }
                }
                foreach (var (copy, offset, _) in anims)
                {
                    int count = copy.sharedMesh.vertexCount;
                    for (int i = offset; i < offset + count; i++)
                        away += (world[i] - animated[i]).magnitude;
                }
                part.frames++;
                part.inside += (double)inside / world.Length;
                part.deepest = Mathf.Max(part.deepest, deepest);
                if (seen >= 2)
                {
                    float j = (float)(jerk / world.Length);
                    part.jerk += j;
                    part.worstJerk = Mathf.Max(part.worstJerk, j);
                }
                part.stretch += stretch / Mathf.Max(1, used);
                part.worstStretch = Mathf.Max(part.worstStretch, worst);
                if (animatedCount > 0)
                {
                    float a = (float)(away / animatedCount);
                    part.away += a;
                    part.worstAway = Mathf.Max(part.worstAway, a);
                    part.turned += (double)turned / Mathf.Max(1, triangleCount);
                }
                seen++;
            }

            IEnumerable<(string name, int frames, float inside, float deepest, float jerk, float worstJerk, float stretch, float worstStretch,
                         float away, float worstAway, float turned)> Rows()
            {
                Part Sum(string name, IEnumerable<Part> of)
                {
                    var list = of.ToList();
                    return new Part
                    {
                        name = name, frames = list.Sum(p => p.frames), inside = list.Sum(p => p.inside), jerk = list.Sum(p => p.jerk),
                        stretch = list.Sum(p => p.stretch), away = list.Sum(p => p.away), turned = list.Sum(p => p.turned),
                        deepest = list.Max(p => p.deepest), worstJerk = list.Max(p => p.worstJerk), worstStretch = list.Max(p => p.worstStretch),
                        worstAway = list.Max(p => p.worstAway),
                    };
                }
                // the two side steps as one part, then all of it
                var merged = parts.GroupBy(p => p.name).Select(g => Sum(g.Key, g)).ToList();
                if (parts.Count > 0)
                    merged.Add(Sum("all", parts));
                foreach (var p in merged)
                {
                    int f = Mathf.Max(1, p.frames);
                    yield return (p.name, p.frames, (float)(100.0 * p.inside / f), p.deepest * 100f, (float)(1000.0 * p.jerk / f), p.worstJerk * 1000f,
                                  (float)(100.0 * p.stretch / f), p.worstStretch * 100f, (float)(100.0 * p.away / f), p.worstAway * 100f,
                                  (float)(100.0 * p.turned / f));
                }
            }

            public string Report(string prefix)
            {
                var sb = new System.Text.StringBuilder($"\n{prefix}: {world.Length} cloth vertices ({animatedCount} with the animation's pose), {ea.Length} edges, " +
                                                       $"{body.Count} body points; inside % (deepest cm) / jerk mm (worst) / stretch % (worst) / away cm (worst) / turned %:");
                foreach (var r in Rows())
                    sb.Append($"\n{prefix}   {r.name,-9} {r.inside,5:F1} ({r.deepest,4:F1})  {r.jerk,5:F2} ({r.worstJerk,5:F1})  {r.stretch,5:F2} ({r.worstStretch,5:F1})" +
                              $"  {r.away,5:F1} ({r.worstAway,5:F1})  {r.turned,5:F1}");
                sb.Append($"\n{prefix}   most stretched at {worstAt}: {string.Join(", ", worstEdges)}");
                if (spikes.Count > 0)
                    sb.Append($"\n{prefix}   steps with an edge 1 in 100 stretched over 200 %: {spikes.Count} - " +
                              string.Join(", ", spikes.Take(12).Select(x => $"{x.part} {x.step} ({x.worst * 100f:F0} %, jerk {x.jerk * 1000f:F1} mm)")));
                return sb.ToString();
            }

            public string Table(string id, string setup)
            {
                string F(float x, string f) => x.ToString(f, CultureInfo.InvariantCulture);
                var sb = new System.Text.StringBuilder();
                foreach (var r in Rows())
                    sb.Append(string.Join("\t", id, setup, r.name, r.frames.ToString(), F(r.inside, "F2"), F(r.deepest, "F2"), F(r.jerk, "F3"), F(r.worstJerk, "F2"),
                        F(r.stretch, "F3"), F(r.worstStretch, "F2"), F(r.away, "F2"), F(r.worstAway, "F2"), F(r.turned, "F2"))).Append('\n');
                return sb.ToString();
            }
        }
    }
}
