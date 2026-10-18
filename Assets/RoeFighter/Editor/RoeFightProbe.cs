using System.Collections.Generic;
using System.Linq;
using RoeFighter.Fight;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RoeFighter.EditorTools
{
    /// <summary>Checks on the saved fight scene.</summary>
    public static class RoeFightProbe
    {
        /// <summary>The skirt rig's switches from the command line: -roeSkirtRest stance|guard, -roeDrape 0..1 (RoeSkirtRig.Drape), -roeClearance m, -roeTwist 0..1.</summary>
        static void SkirtArgs()
        {
            RoeSkirtRig.RestOnStance = RoeCapture.Arg("-roeSkirtRest", "stance") == "stance";
            RoeSkirtRig.Drape = float.Parse(RoeCapture.Arg("-roeDrape", "1"), System.Globalization.CultureInfo.InvariantCulture);
            RoeSkirtRig.DrapeClearance = float.Parse(RoeCapture.Arg("-roeClearance", "0.012"), System.Globalization.CultureInfo.InvariantCulture);
            RoeSkirtRig.DrapeTwist = float.Parse(RoeCapture.Arg("-roeTwist", "1"), System.Globalization.CultureInfo.InvariantCulture);
            RoeSkirtRig.DrapeMeshClearance = float.Parse(RoeCapture.Arg("-roeMeshClearance", "0.003"), System.Globalization.CultureInfo.InvariantCulture);
            RoeSkirtRig.DrapeFacing = float.Parse(RoeCapture.Arg("-roeFacing", RoeSkirtRig.DrapeFacing.ToString(System.Globalization.CultureInfo.InvariantCulture)), System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>Lists what each fighter draws (renderer, enabled, materials/shaders).</summary>
        public static void Renderers()
        {
            string stage = RoeCapture.Arg("-roeStage", "e23_steel_s02");
            EditorSceneManager.OpenScene(RoeFightScene.ScenePath(stage), OpenSceneMode.Single);
            foreach (var rig in Object.FindObjectsByType<FighterRig>(FindObjectsSortMode.None))
                foreach (var r in rig.GetComponentsInChildren<Renderer>(true))
                    Debug.Log($"[ROE] {rig.id} {RoeHumanoidClips.PathOf(r.transform, rig.transform)} enabled={r.enabled} active={r.gameObject.activeInHierarchy} " +
                              $"{r.GetType().Name} mats: {string.Join(", ", r.sharedMaterials.Select(m => m == null ? "null" : $"{m.name}<{m.shader.name}>"))}");
        }

        /// <summary>
        /// The same clip, the same time: posed by sampling the clip alone, and by the fight's playable graph.
        /// Prints the bones that end up farthest apart.  -roeChar g04 -roeClip skill_03 -roeTimes 1,2,3
        /// </summary>
        public static void Pose()
        {
            string stage = RoeCapture.Arg("-roeStage", "e23_steel_s02");
            string id = RoeCapture.Arg("-roeChar", "g04");
            string clipName = RoeCapture.Arg("-roeClip", "skill_03");
            var times = RoeCapture.Arg("-roeTimes", "0.5,1,1.5,2,2.5,3").Split(',').Select(float.Parse).ToArray();
            EditorSceneManager.OpenScene(RoeFightScene.ScenePath(stage), OpenSceneMode.Single);
            var rig = Object.FindObjectsByType<FighterRig>(FindObjectsSortMode.None).First(r => r.id == id);
            var clip = rig.clips.First(c => c.name == clipName).clip;
            var bones = rig.animator.GetComponentsInChildren<Transform>(true);
            foreach (float time in times)
            {
                rig.Init();
                rig.Play(clipName, 1f, 0f);
                float t = 0f;
                const float dt = 1f / 60f;
                while (t + dt <= time)
                {
                    rig.Tick(dt);
                    t += dt;
                }
                var graphPose = bones.ToDictionary(b => b, b => b.position);
                RoeCapture.Pose(rig.animator.gameObject, clip, t);
                var worst = bones.Select(b => (b, d: Vector3.Distance(graphPose[b], b.position))).OrderByDescending(x => x.d).Take(8).ToList();
                var hips = rig.animator.GetBoneTransform(HumanBodyBones.Hips);
                Debug.Log($"[ROE] pose check {id} {clipName} t={t:F2}: hips graph {graphPose[hips]:F2} sampled {hips.position:F2}; farthest apart: " +
                          string.Join(", ", worst.Select(x => $"{x.b.name} {x.d:F2} m")));
                RoeCapture.EndPosing();
            }
        }
        /// <summary>Everything drawn while a skill runs (timeline-made objects included): -roeChar g04 -roeAction skill3 -roeTimes 1.5,2</summary>
        public static void SkillRenderers()
        {
            string stage = RoeCapture.Arg("-roeStage", "e23_steel_s02");
            string id = RoeCapture.Arg("-roeChar", "g04");
            string action = RoeCapture.Arg("-roeAction", "skill3");
            string clipName = "skill_0" + action.Substring(action.Length - 1);
            var times = RoeCapture.Arg("-roeTimes", "1.5,2").Split(',').Select(float.Parse).ToArray();
            EditorSceneManager.OpenScene(RoeFightScene.ScenePath(stage), OpenSceneMode.Single);
            var rig = Object.FindObjectsByType<FighterRig>(FindObjectsSortMode.None).First(r => r.id == id);
            rig.Init();
            rig.Play(clipName, 1f, 0f);
            float t = 0f;
            const float dt = 1f / 60f;
            foreach (float time in times)
            {
                while (t + dt <= time)
                {
                    t += dt;
                    rig.Tick(dt);
                    rig.ShowTimeline(action, t);
                }
                var hips = rig.animator.GetBoneTransform(HumanBodyBones.Hips);
                Debug.Log($"[ROE] skill renderers {id} {action} t={t:F2}: our hips {hips.position:F2}");
                foreach (var r in Resources.FindObjectsOfTypeAll<Renderer>())
                {
                    if (!r.enabled || !r.gameObject.activeInHierarchy || EditorUtility.IsPersistent(r) || r is ParticleSystemRenderer)
                        continue;
                    if (!r.transform.IsChildOf(rig.transform) && r.gameObject.scene != rig.gameObject.scene)
                        continue;
                    if (!r.transform.IsChildOf(rig.transform))
                        continue;
                    if (r.transform.IsChildOf(rig.animator.transform))
                        continue;
                    var smr = r as SkinnedMeshRenderer;
                    Debug.Log($"[ROE]   {RoeHumanoidClips.PathOf(r.transform, rig.transform)} {r.GetType().Name} bounds {r.bounds.center:F2} size {r.bounds.size:F2} " +
                              $"mats {string.Join(",", r.sharedMaterials.Select(m => m == null ? "null" : m.name + "<" + m.shader.name + ">"))}" +
                              (smr != null && smr.rootBone != null ? $" rootBone {RoeHumanoidClips.PathOf(smr.rootBone, rig.transform)}" : ""));
                }
            }
        }
        /// <summary>
        /// The real fight logic, g04 starting her super as soon as the fight begins: hips and feet
        /// heights every 10 steps, and two rendered frames.  -roeOut dir
        /// </summary>
        public static void SuperInGame()
        {
            string stage = RoeCapture.Arg("-roeStage", "e23_steel_s02");
            string outDir = RoeCapture.Arg("-roeOut", System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "_work", "probe_super"));
            EditorSceneManager.OpenScene(RoeFightScene.ScenePath(stage), OpenSceneMode.Single);
            var game = Object.FindFirstObjectByType<FightGame>();
            game.cpu = new[] { false, false };
            game.hud.drawnByCamera = true;
            foreach (var rig in game.rigs)
                foreach (var smr in rig.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    smr.forceMatrixRecalculationPerRender = true;
            game.Setup();
            var g = game.f[1];
            g.meter = 100f;
            var a = g.rig.animator;
            var hips = a.GetBoneTransform(HumanBodyBones.Hips);
            var lf = a.GetBoneTransform(HumanBodyBones.LeftFoot);
            var sb = new System.Text.StringBuilder();
            bool started = false;
            for (int s = 0; s < 60 * 9; s++)
            {
                var inputs = new FighterInput[2];
                if (game.phase == FightGame.Phase.Fight && !started)
                {
                    inputs[1].s3 = true;
                    started = true;
                }
                game.Step(inputs);
                game.UpdateCamera(FightGame.Dt);
                if (g.state == FightState.Special && s % 10 == 0)
                    sb.Append($"\n[ROE]   step {s} special t={g.t:F2} rig clip {g.rig.Current}@{g.rig.CurrentTime:F2}: hips y {hips.position.y:F2} left foot y {lf.position.y:F2}");
                if (g.state == FightState.Special && (Mathf.Abs(g.t - 2.0f) < 0.009f || Mathf.Abs(g.t - 1.5f) < 0.009f))
                {
                    game.hud.Refresh(game);
                    Canvas.ForceUpdateCanvases();
                    RoeCapture.Render(game.cam, 320, 180, System.IO.Path.Combine(outDir, "_warm.jpg"), 80);
                    RoeCapture.Render(game.cam, 1280, 720, System.IO.Path.Combine(outDir, $"super_{g.t:F2}.jpg"), 90);
                }
            }
            Debug.Log("[ROE] super in game:" + sb);
        }

        /// <summary>
        /// The real fight logic, g04's super: every 10 steps, the skinning bones of her meshes that are
        /// lower than her feet (left behind on the floor while she jumps), and which meshes use them.
        /// </summary>
        public static void FloorBones()
        {
            string stage = RoeCapture.Arg("-roeStage", "e23_steel_s02");
            int who = int.Parse(RoeCapture.Arg("-roeFighter", "1"));
            string action = RoeCapture.Arg("-roeAction", "skill3");
            EditorSceneManager.OpenScene(RoeFightScene.ScenePath(stage), OpenSceneMode.Single);
            var game = Object.FindFirstObjectByType<FightGame>();
            game.cpu = new[] { false, false };
            game.Setup();
            var g = game.f[who];
            g.meter = 100f;
            var a = g.rig.animator;
            var hips = a.GetBoneTransform(HumanBodyBones.Hips);
            var feet = new[] { a.GetBoneTransform(HumanBodyBones.LeftFoot), a.GetBoneTransform(HumanBodyBones.RightFoot) };
            var smrs = g.rig.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r => r.transform.IsChildOf(a.transform)).ToArray();
            var sb = new System.Text.StringBuilder();
            bool started = false;
            for (int s = 0; s < 60 * 9; s++)
            {
                var inputs = new FighterInput[2];
                if (game.phase == FightGame.Phase.Fight && !started)
                {
                    if (action == "skill3") inputs[who].s3 = true;
                    else if (action == "skill2") inputs[who].s2 = true;
                    else inputs[who].s1 = true;
                    started = true;
                }
                game.Step(inputs);
                if (g.state != FightState.Special || s % 10 != 0)
                    continue;
                float floor = Mathf.Min(feet[0].position.y, feet[1].position.y) - 0.15f;
                sb.Append($"\n[ROE]   t={g.t:F2} hips y {hips.position.y:F2} feet y {feet[0].position.y:F2}/{feet[1].position.y:F2}");
                var low = new Dictionary<Transform, List<string>>();
                foreach (var r in smrs)
                    foreach (var b in r.bones)
                        if (b != null && b.position.y < floor)
                        {
                            if (!low.TryGetValue(b, out var users))
                                low[b] = users = new List<string>();
                            users.Add(r.name);
                        }
                foreach (var kv in low.OrderBy(kv => kv.Key.position.y).Take(12))
                    sb.Append($"\n[ROE]     low bone {RoeHumanoidClips.PathOf(kv.Key, a.transform)} y {kv.Key.position.y:F2} local {kv.Key.localPosition:F3} used by {string.Join(",", kv.Value.Distinct())}");
            }
            Debug.Log($"[ROE] floor bones of {g.rig.id} in {action}:" + sb);
        }

        /// <summary>
        /// The real fight logic, a fighter's skill up to a time (-roeTime 2.1): every mesh under the
        /// fighter skinned on the CPU (BakeMesh), the vertices that end up below the feet, the bones
        /// they hang from; then pictures of the legs: everything, our model only, the game's unit only.
        /// </summary>
        public static void Spikes()
        {
            string stage = RoeCapture.Arg("-roeStage", "e23_steel_s02");
            int who = int.Parse(RoeCapture.Arg("-roeFighter", "1"));
            string action = RoeCapture.Arg("-roeAction", "skill3");
            var times = RoeCapture.Arg("-roeTimes", "2.1").Split(',').Select(x => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            string outDir = RoeCapture.Arg("-roeOut", System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "_work", "probe_spikes"));
            System.IO.Directory.CreateDirectory(outDir);
            EditorSceneManager.OpenScene(RoeFightScene.ScenePath(stage), OpenSceneMode.Single);
            var game = Object.FindFirstObjectByType<FightGame>();
            game.cpu = new[] { false, false };
            game.hud.drawnByCamera = true;
            foreach (var rig in game.rigs)
                foreach (var smr in rig.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    smr.forceMatrixRecalculationPerRender = true;
            game.Setup();
            var g = game.f[who];
            g.meter = 100f;
            bool started = false;
            for (int s = 0; s < 60 * 12; s++)
            {
                var inputs = new FighterInput[2];
                if (game.phase == FightGame.Phase.Fight && !started)
                {
                    if (action == "skill3") inputs[who].s3 = true;
                    else if (action == "skill2") inputs[who].s2 = true;
                    else inputs[who].s1 = true;
                    started = true;
                }
                game.Step(inputs);
                if (started && g.state == FightState.Special && times.Length > 0 && g.t >= times[0])
                {
                    SpikesAt(game, g, action, outDir);
                    times = times.Skip(1).ToArray();
                    if (times.Length == 0)
                        break;
                }
            }
        }

        /// <summary>
        /// The recorded CPU match again (same seed, same steps as RoeFightScene.Record, nothing
        /// filmed) up to given steps (-roeSteps 5280,5340), then the Spikes check there, plus the
        /// game camera's own view and every bone of the fighter that is far from where its
        /// skinning expects it.
        /// </summary>
        public static void MatchAt()
        {
            string stage = RoeCapture.Arg("-roeStage", "e23_steel_s02");
            int who = int.Parse(RoeCapture.Arg("-roeFighter", "1"));
            int seed = int.Parse(RoeCapture.Arg("-roeSeed", "1"));
            var steps = RoeCapture.Arg("-roeSteps", "5280").Split(',').Select(int.Parse).ToList();
            string outDir = RoeCapture.Arg("-roeOut", System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "_work", "probe_match"));
            System.IO.Directory.CreateDirectory(outDir);
            EditorSceneManager.OpenScene(RoeFightScene.ScenePath(stage), OpenSceneMode.Single);
            var game = Object.FindFirstObjectByType<FightGame>();
            game.cpu = new[] { true, true };
            game.seed = seed;
            game.hud.drawnByCamera = true;
            foreach (var rig in game.rigs)
                foreach (var smr in rig.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    smr.forceMatrixRecalculationPerRender = true;
            game.Setup();
            var g = game.f[who];
            var a = g.rig.animator;
            var hips = a.GetBoneTransform(HumanBodyBones.Hips);
            var lf = a.GetBoneTransform(HumanBodyBones.LeftFoot);
            var history = new System.Text.StringBuilder();
            int last = steps.Max();
            for (int s = 0; s <= last; s++)
            {
                game.Step(null);
                game.UpdateCamera(FightGame.Dt);
                if (s % 30 == 0 && s >= steps.Min() - 600)
                    history.Append($"\n[ROE]   step {s}: {g.state} t={g.t:F2} clip {g.rig.Current}@{g.rig.CurrentTime:F2} hips y {hips.position.y:F2} left foot y {lf.position.y:F2}");
                if (!steps.Contains(s))
                    continue;
                string Size(FighterRig rig)
                {
                    var sizes = new List<string>();
                    foreach (var r in rig.animator.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r => r.name.StartsWith("wp_")))
                    {
                        var m = new Mesh();
                        r.BakeMesh(m, true);
                        m.RecalculateBounds();
                        sizes.Add($"{r.name} {m.bounds.size:F2} enabled {r.enabled}");
                        Object.DestroyImmediate(m);
                    }
                    return string.Join("; ", sizes);
                }
                Debug.Log($"[ROE] match at step {s} ({s / 60f:F2} s): {g.rig.id} {g.state} special {g.special?.name} t={g.t:F2} clip {g.rig.Current}@{g.rig.CurrentTime:F2}" + history +
                          string.Concat(game.f.Select(x => $"\n[ROE]   mixers {x.rig.id} {x.state} t={x.t:F2}: {x.rig.MixerWeights()}; weapon mesh {Size(x.rig)}")));
                history.Clear();
                game.hud.Refresh(game);
                Canvas.ForceUpdateCanvases();
                RoeCapture.Render(game.cam, 320, 180, System.IO.Path.Combine(outDir, "_warm.jpg"), 80);
                RoeCapture.Render(game.cam, 1280, 720, System.IO.Path.Combine(outDir, $"step{s}_game.jpg"), 90);
                SpikesAt(game, g, g.special?.name ?? g.state.ToString(), outDir);
            }
        }

        static void SpikesAt(FightGame game, Fighter g, string action, string outDir)
        {
            var a = g.rig.animator;
            var feet = new[] { a.GetBoneTransform(HumanBodyBones.LeftFoot), a.GetBoneTransform(HumanBodyBones.RightFoot) };
            float floor = Mathf.Min(feet[0].position.y, feet[1].position.y) - 0.2f;
            var sb = new System.Text.StringBuilder($"[ROE] spikes {g.rig.id} {action} t={g.t:F2} state {g.state}: hips y {a.GetBoneTransform(HumanBodyBones.Hips).position.y:F2}, feet y {feet[0].position.y:F2}/{feet[1].position.y:F2}, root y {g.rig.transform.position.y:F2}");
            var baked = new Mesh();
            foreach (var r in g.rig.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (r.sharedMesh == null)
                    continue;
                r.BakeMesh(baked, true);
                var v = baked.vertices;
                var m = Matrix4x4.TRS(r.transform.position, r.transform.rotation, Vector3.one);
                var low = new List<int>();
                for (int i = 0; i < v.Length; i++)
                    if (m.MultiplyPoint3x4(v[i]).y < floor)
                        low.Add(i);
                bool ours = r.transform.IsChildOf(a.transform);
                sb.Append($"\n[ROE]   {(ours ? "model" : "unit ")} {r.name} enabled={r.enabled && r.gameObject.activeInHierarchy} verts {v.Length}, below the feet {low.Count}");
                if (low.Count == 0)
                    continue;
                var bw = r.sharedMesh.boneWeights;
                var bones = r.bones;
                string Name(int k) => k < bones.Length ? (bones[k] != null ? bones[k].name : "null") : $"#{k} out of {bones.Length}";
                var byBone = new Dictionary<string, int>();
                float lowest = float.MaxValue;
                foreach (int i in low)
                {
                    lowest = Mathf.Min(lowest, m.MultiplyPoint3x4(v[i]).y);
                    if (i >= bw.Length)
                        continue;
                    var w = bw[i];
                    string key = $"{Name(w.boneIndex0)} {w.weight0:F2}" + (w.weight1 > 0 ? $" + {Name(w.boneIndex1)} {w.weight1:F2}" : "") +
                                 (w.weight2 > 0 ? $" + {Name(w.boneIndex2)} {w.weight2:F2}" : "") + (w.weight3 > 0 ? $" + {Name(w.boneIndex3)} {w.weight3:F2}" : "") +
                                 $" (sum {w.weight0 + w.weight1 + w.weight2 + w.weight3:F2})";
                    byBone[key] = byBone.TryGetValue(key, out int n) ? n + 1 : 1;
                }
                sb.Append($", lowest y {lowest:F2}; root bone {(r.rootBone ? r.rootBone.name : "none")}");
                foreach (var kv in byBone.OrderByDescending(kv => kv.Value).Take(10))
                    sb.Append($"\n[ROE]     {kv.Value} verts: {kv.Key}");
            }
            Object.DestroyImmediate(baked);
            Debug.Log(sb.ToString());

            // pictures of the legs from the side, at the height of the feet
            var cam = game.cam;
            var camWas = (cam.transform.position, cam.transform.rotation);
            string tag = g.t.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
            var mid = (feet[0].position + feet[1].position) * 0.5f;
            var side = Vector3.Cross(Vector3.up, g.Forward).normalized;
            cam.transform.position = mid + side * 4.5f + Vector3.down * 0.2f;
            cam.transform.LookAt(mid + Vector3.down * 0.4f);
            game.hud.gameObject.SetActive(false);
            var ourRenderers = a.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled).ToArray();
            var unitRenderers = g.rig.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled && !r.transform.IsChildOf(a.transform)).ToArray();
            RoeCapture.Render(cam, 320, 180, System.IO.Path.Combine(outDir, "_warm.jpg"), 80);
            RoeCapture.Render(cam, 960, 720, System.IO.Path.Combine(outDir, $"{tag}_all.jpg"), 90);
            foreach (var r in unitRenderers) r.enabled = false;
            RoeCapture.Render(cam, 960, 720, System.IO.Path.Combine(outDir, $"{tag}_model.jpg"), 90);
            foreach (var r in unitRenderers) r.enabled = true;
            foreach (var r in ourRenderers) r.enabled = false;
            RoeCapture.Render(cam, 960, 720, System.IO.Path.Combine(outDir, $"{tag}_unit.jpg"), 90);
            foreach (var r in ourRenderers) r.enabled = true;
            game.hud.gameObject.SetActive(true);
            cam.transform.SetPositionAndRotation(camWas.Item1, camWas.Item2);
            Debug.Log($"[ROE] spikes pictures t={tag} in {outDir}: {ourRenderers.Length} model renderers, {unitRenderers.Length} unit renderers ({string.Join(", ", unitRenderers.Select(r => r.name).Distinct().Take(20))})");
        }

        /// <summary>
        /// Close-ups of each fighter in each basic move, posed by the fight's own graph (all three
        /// layers), from the side and from the front: _work/probe_moves/&lt;id&gt;_&lt;view&gt;_&lt;clip&gt;_&lt;k&gt;.jpg
        /// (strikes at start, hit start, hit peak, hit end, recovery).  -roeClips guard,jab
        /// </summary>
        public static void MovesSheet()
        {
            string stage = RoeCapture.Arg("-roeStage", "e23_steel_s02");
            string outDir = RoeCapture.Arg("-roeOut", System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "_work", "probe_moves"));
            var clipNames = RoeCapture.Arg("-roeClips", "guard,walk,walk_back,side_left,side_right,jab,cross,kick,slash,hurt").Split(',');
            int width = int.Parse(RoeCapture.Arg("-roeWidth", "400")), height = int.Parse(RoeCapture.Arg("-roeHeight", "560"));
            System.IO.Directory.CreateDirectory(outDir);
            EditorSceneManager.OpenScene(RoeFightScene.ScenePath(stage), OpenSceneMode.Single);
            var game = Object.FindFirstObjectByType<FightGame>();
            foreach (var rig in game.rigs)
                foreach (var smr in rig.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    smr.forceMatrixRecalculationPerRender = true;
            game.hud.gameObject.SetActive(false);
            var cam = game.cam;
            cam.fieldOfView = 30f;
            var facing = Quaternion.LookRotation(game.axis, Vector3.up);
            bool warmed = false;
            var sb = new System.Text.StringBuilder("[ROE] moves sheet:");
            foreach (var rig in game.rigs)
            {
                // the other one out of the way
                foreach (var other in game.rigs)
                    other.transform.position = other == rig ? game.centre : game.centre + Vector3.down * 50f;
                rig.transform.rotation = facing;
                rig.Init();
                foreach (var name in clipNames)
                {
                    if (!rig.Has(name))
                        continue;
                    var move = game.moves.FirstOrDefault(m => m.clip == name);
                    float len = rig.Length(name);
                    float[] times = move != null
                        ? new[] { 0f, move.hitStart, (move.hitStart + move.hitEnd) * 0.5f, move.hitEnd, Mathf.Min(len, move.length * move.recover) }
                        : Enumerable.Range(0, 5).Select(k => len * k / 5f).ToArray();
                    for (int k = 0; k < times.Length; k++)
                    {
                        rig.Init();
                        rig.Play(name, 1f, 0f);
                        for (float t = 0f; t + 1f / 120f < times[k]; t += 1f / 60f)
                            rig.Tick(1f / 60f);
                        rig.Tick(0f);
                        var a = rig.animator;
                        var hips = a.GetBoneTransform(HumanBodyBones.Hips).position;
                        var target = new Vector3(rig.transform.position.x, hips.y * 0.55f + 0.42f, rig.transform.position.z);
                        var fwd = rig.transform.forward;
                        var left = -rig.transform.right;
                        var views = new (string tag, Vector3 dir)[] { ("side", left), ("front", (fwd * 0.85f - left * 0.53f).normalized) };
                        foreach (var (tag, dir) in views)
                        {
                            cam.transform.position = target + dir * 4.1f + Vector3.up * 0.25f;
                            cam.transform.LookAt(target, Vector3.up);
                            if (!warmed)
                            {
                                RoeCapture.Render(cam, 200, 280, System.IO.Path.Combine(outDir, "_warm.jpg"), 80);
                                RoeCapture.Render(cam, 200, 280, System.IO.Path.Combine(outDir, "_warm.jpg"), 80);
                                warmed = true;
                            }
                            RoeCapture.Render(cam, width, height, System.IO.Path.Combine(outDir, $"{rig.id}_{tag}_{name}_{k}.jpg"), 90);
                        }
                        var lf = a.GetBoneTransform(HumanBodyBones.LeftFoot);
                        var rf = a.GetBoneTransform(HumanBodyBones.RightFoot);
                        var lt = a.GetBoneTransform(HumanBodyBones.LeftToes);
                        var rt = a.GetBoneTransform(HumanBodyBones.RightToes);
                        sb.Append($"\n[ROE]   {rig.id} {name} t={times[k]:F2}: hips y {hips.y:F2}, feet y {lf.position.y:F2}/{rf.position.y:F2}, toes y {(lt ? lt.position.y : 0f):F2}/{(rt ? rt.position.y : 0f):F2}");
                    }
                }
            }
            Debug.Log(sb.ToString());
        }

        /// <summary>
        /// Foot sliding in the real fight logic: each fighter walks forward, walks back and steps to
        /// each side for 3 s; while a foot is planted (its sole within 2 cm of the floor) it should
        /// not move.  Prints the body speed and the planted feet's mean speed per locomotion.
        /// </summary>
        public static void FootSlide()
        {
            string stage = RoeCapture.Arg("-roeStage", "e23_steel_s02");
            EditorSceneManager.OpenScene(RoeFightScene.ScenePath(stage), OpenSceneMode.Single);
            var game = Object.FindFirstObjectByType<FightGame>();
            game.cpu = new[] { false, false };
            string pack = RoeCapture.Arg("-roeMotions", null);
            if (pack != null)
                game.motionPack = Mathf.Max(0, game.motionPacks.FindIndex(p => p.name == pack));
            var sb = new System.Text.StringBuilder($"[ROE] foot slide ({game.Pack?.name}):");
            var tests = new (string name, int x, int y)[] { ("forward", 1, 0), ("back", -1, 0), ("side up", 0, 1), ("side down", 0, -1) };
            for (int who = 0; who < 2; who++)
            {
                foreach (var test in tests)
                {
                    game.Setup();
                    while (game.phase != FightGame.Phase.Fight)
                        game.Step(new FighterInput[2]);
                    var me = game.f[who];
                    // the opponent far enough not to be walked into
                    me.foe.pos = me.pos + (me.foe.pos - me.pos).normalized * 6.5f;
                    me.foe.Place();
                    var a = me.rig.animator;
                    var feet = new[] { a.GetBoneTransform(HumanBodyBones.LeftFoot), a.GetBoneTransform(HumanBodyBones.RightFoot),
                                       a.GetBoneTransform(HumanBodyBones.LeftToes), a.GetBoneTransform(HumanBodyBones.RightToes) };
                    // which screen direction is "forward" for this fighter
                    var camRight = game.cam.transform.right;
                    camRight.y = 0f;
                    int towards = Vector3.Dot(me.foe.pos - me.pos, camRight) >= 0f ? 1 : -1;
                    var floorY = a.transform.position.y;
                    var last = feet.Select(f => f.position).ToArray();
                    // the lowest of each bone in the first second = its planted height
                    var planted = feet.Select(f => float.MaxValue).ToArray();
                    // the contact: each step, the bone (ankle or ball of a foot) nearest its planted height;
                    // while it is within 2 cm of it and stays the same bone, it should not move
                    float slid = 0f, contactTime = 0f, travelled = 0f;
                    var drift = Vector3.zero;
                    int lastContact = -1;
                    var start = me.pos;
                    for (int s = 0; s < 180; s++)
                    {
                        var inputs = new FighterInput[2];
                        inputs[who] = new FighterInput { x = test.x * towards, y = test.y };
                        game.Step(inputs);
                        game.UpdateCamera(FightGame.Dt);
                        int contact = -1;
                        float lowest = float.MaxValue;
                        for (int k = 0; k < feet.Length; k++)
                        {
                            float h = feet[k].position.y - a.transform.position.y;
                            if (s < 60)
                                planted[k] = Mathf.Min(planted[k], h);
                            else if (h - planted[k] < lowest)
                            {
                                lowest = h - planted[k];
                                contact = k;
                            }
                        }
                        if (s >= 60 && lowest < 0.02f && contact == lastContact)
                        {
                            var d = feet[contact].position - last[contact];
                            d.y = 0f;
                            slid += d.magnitude;
                            drift += d;
                            contactTime += FightGame.Dt;
                        }
                        lastContact = lowest < 0.02f ? contact : -1;
                        for (int k = 0; k < feet.Length; k++)
                            last[k] = feet[k].position;
                    }
                    travelled = Vector3.Distance(Flat(start), Flat(me.pos));
                    var v = contactTime > 0f ? drift / contactTime : Vector3.zero;
                    sb.Append($"\n[ROE]   {me.rig.id} {test.name} (strides {me.rig.StrideReport()}): state {me.state} clip {me.rig.Current} rate {me.rig.CurrentRate:F2}, body {travelled / 3f:F2} m/s; " +
                              $"the contact slides {(contactTime > 0f ? slid / contactTime : 0f):F2} m/s ({contactTime / 2f * 100f:F0}% of the time), " +
                              $"drifting {Vector3.Dot(v, me.Forward):F2} m/s forward, {Vector3.Dot(v, Quaternion.Euler(0f, me.yaw, 0f) * Vector3.right):F2} m/s right");
                }
            }
            Debug.Log(sb.ToString());

            Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
        }

        /// <summary>
        /// g04's fan over her battle stance (layer 0 of the graph, under the basic moves): the distance
        /// between the two outer ribs' tips and the fan's size, every 0.25 s of the stance clip.
        /// </summary>
        public static void Fan()
        {
            string stage = RoeCapture.Arg("-roeStage", "e23_steel_s02");
            EditorSceneManager.OpenScene(RoeFightScene.ScenePath(stage), OpenSceneMode.Single);
            var rig = Object.FindObjectsByType<FighterRig>(FindObjectsSortMode.None).First(r => r.id == "g04");
            rig.Init();
            rig.Play("guard", 1f, 0f);
            var bones = rig.animator.GetComponentsInChildren<Transform>(true);
            var left = bones.FirstOrDefault(b => b.name == "Left_fan_03");
            var right = bones.FirstOrDefault(b => b.name == "Right_fan_03");
            var root = bones.FirstOrDefault(b => b.name == "fan_01");
            var sb = new System.Text.StringBuilder($"[ROE] g04 fan over the stance ({rig.stance.name}, {rig.stance.length:F2} s):");
            for (int s = 0; s <= Mathf.CeilToInt(rig.stance.length * 60f) + 30; s++)
            {
                rig.Tick(1f / 60f);
                if (s % 15 == 0)
                    sb.Append($"\n[ROE]   t={s / 60f:F2}: outer ribs {Vector3.Distance(left.position, right.position):F2} m apart, fan scale {root.lossyScale.x:F2}");
            }
            Debug.Log(sb.ToString());

            // the same in the fight: side steps, then walking, then standing
            var game = Object.FindFirstObjectByType<FightGame>();
            game.cpu = new[] { false, false };
            game.Setup();
            while (game.phase != FightGame.Phase.Fight)
                game.Step(new FighterInput[2]);
            var g = game.f[1];
            g.foe.pos = g.pos + (g.foe.pos - g.pos).normalized * 6.5f;
            sb.Clear();
            sb.Append("[ROE] g04 fan in the fight:");
            for (int s = 0; s < 360; s++)
            {
                var inputs = new FighterInput[2];
                inputs[1] = s < 180 ? new FighterInput { y = 1 } : s < 300 ? new FighterInput { x = -1 } : default;
                game.Step(inputs);
                if (s % 15 == 0)
                    sb.Append($"\n[ROE]   step {s} {g.state} clip {g.rig.Current}: outer ribs {Vector3.Distance(left.position, right.position):F2} m apart; {g.rig.MixerWeights()}");
            }
            Debug.Log(sb.ToString());
        }

        /// <summary>
        /// One cycle of a locomotion clip in place (-roeChar g04 -roeClip walk): the ankles' and balls'
        /// heights and positions along the body's forward axis every 1/30 s, at rate 1.
        /// </summary>
        public static void Cycle()
        {
            string stage = RoeCapture.Arg("-roeStage", "e23_steel_s02");
            string id = RoeCapture.Arg("-roeChar", "g04");
            string clipName = RoeCapture.Arg("-roeClip", "walk");
            EditorSceneManager.OpenScene(RoeFightScene.ScenePath(stage), OpenSceneMode.Single);
            var rig = Object.FindObjectsByType<FighterRig>(FindObjectsSortMode.None).First(r => r.id == id);
            rig.Init();
            rig.Play(clipName, 1f, 0f);
            var a = rig.animator;
            var bones = new[] { HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes, HumanBodyBones.RightFoot, HumanBodyBones.RightToes, HumanBodyBones.Hips }
                .Select(a.GetBoneTransform).ToArray();
            float len = rig.Length(clipName);
            var sb = new System.Text.StringBuilder($"[ROE] {id} {clipName} ({len:F2} s) in place, forward z / height y of: left ankle, left ball, right ankle, right ball, hips");
            for (int s = 0; s <= Mathf.CeilToInt(len * 30f) + 3; s++)
            {
                rig.Tick(s == 0 ? 0f : 1f / 30f);
                sb.Append($"\n[ROE]   t={rig.CurrentTime:F3}: " + string.Join("  ", bones.Select(b =>
                {
                    var p = rig.transform.InverseTransformPoint(b.position);
                    return $"{p.z,6:F3}/{p.y,5:F3}";
                })));
            }
            Debug.Log(sb.ToString());
        }

        /// <summary>
        /// The bones a cloth simulation could move, per fighter: every non-human bone that is not a
        /// limb helper, grouped by the bone its chain hangs on and its name without the trailing number,
        /// with the chain depth, how much skin the group carries and whether the battle stance animates it.
        /// </summary>
        public static void ClothBones()
        {
            string stage = RoeCapture.Arg("-roeStage", "e23_steel_s02");
            EditorSceneManager.OpenScene(RoeFightScene.ScenePath(stage), OpenSceneMode.Single);
            foreach (var rig in Object.FindObjectsByType<FighterRig>(FindObjectsSortMode.None).OrderBy(r => r.id))
            {
                var a = rig.animator;
                var human = new HashSet<Transform>();
                foreach (HumanBodyBones hb in System.Enum.GetValues(typeof(HumanBodyBones)))
                    if (hb != HumanBodyBones.LastBone && a.GetBoneTransform(hb) != null)
                        human.Add(a.GetBoneTransform(hb));
                var helperRig = a.GetComponent<RoeHelperRig>();
                var helpers = new HashSet<Transform>(helperRig != null ? helperRig.drives.Select(d => d.helper) : Enumerable.Empty<Transform>());
                var skin = new Dictionary<Transform, int>();
                foreach (var r in a.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var bw = r.sharedMesh.boneWeights;
                    foreach (var w in bw)
                        if (w.weight0 > 0.3f && r.bones[w.boneIndex0] != null)
                            skin[r.bones[w.boneIndex0]] = skin.TryGetValue(r.bones[w.boneIndex0], out int n) ? n + 1 : 1;
                }
                var animated = new HashSet<string>(AnimationUtility.GetCurveBindings(rig.stance).Select(b => b.path.Split('/').Last()));
                string Prefix(string name) => System.Text.RegularExpressions.Regex.Replace(name, @"[._\-\s]*\d+$", "");
                var groups = new Dictionary<string, List<Transform>>();
                foreach (var t in a.GetComponentsInChildren<Transform>(true))
                {
                    if (t == a.transform || human.Contains(t) || helpers.Contains(t) || t.GetComponent<Renderer>() != null)
                        continue;
                    var top = t;
                    while (top.parent != null && !human.Contains(top.parent) && Prefix(top.parent.name) == Prefix(t.name))
                        top = top.parent;
                    string key = $"{Prefix(t.name)} (on {(top.parent != null ? top.parent.name : "-")})";
                    if (!groups.TryGetValue(key, out var list))
                        groups[key] = list = new List<Transform>();
                    list.Add(t);
                }
                var sb = new System.Text.StringBuilder($"[ROE] cloth candidates of {rig.id}:");
                foreach (var kv in groups.OrderByDescending(kv => kv.Value.Sum(t => skin.TryGetValue(t, out int n) ? n : 0)))
                {
                    int verts = kv.Value.Sum(t => skin.TryGetValue(t, out int n) ? n : 0);
                    if (verts == 0)
                        continue;
                    int keyed = kv.Value.Count(t => animated.Contains(t.name));
                    float span = kv.Value.Count > 1 ? kv.Value.Max(t => Vector3.Distance(t.position, kv.Value[0].position)) : 0f;
                    sb.Append($"\n[ROE]   {kv.Key}: {kv.Value.Count} bones, {verts} verts, {keyed} keyed in the stance, span {span:F2} m: " +
                              string.Join(" ", kv.Value.Take(8).Select(t => t.name)) + (kv.Value.Count > 8 ? " ..." : ""));
                }
                Debug.Log(sb.ToString());
            }
        }

        /// <summary>Animators under each fighter and what the skill timelines are bound to.</summary>
        public static void Bindings()
        {
            string stage = RoeCapture.Arg("-roeStage", "e23_steel_s02");
            EditorSceneManager.OpenScene(RoeFightScene.ScenePath(stage), OpenSceneMode.Single);
            foreach (var rig in Object.FindObjectsByType<FighterRig>(FindObjectsSortMode.None))
            {
                foreach (var a in rig.GetComponentsInChildren<Animator>(true))
                    Debug.Log($"[ROE] {rig.id} animator at {RoeHumanoidClips.PathOf(a.transform, rig.transform)} enabled={a.enabled} avatar={(a.avatar ? a.avatar.name : "none")} controller={(a.runtimeAnimatorController ? a.runtimeAnimatorController.name : "none")}");
                foreach (var nd in rig.directors.Where(d => d.action.StartsWith("skill")))
                {
                    var asset = nd.director.playableAsset as UnityEngine.Timeline.TimelineAsset;
                    if (asset == null)
                        continue;
                    foreach (var track in asset.GetOutputTracks())
                    {
                        if (!(track is UnityEngine.Timeline.AnimationTrack))
                            continue;
                        var bound = nd.director.GetGenericBinding(track);
                        string where = bound is Component c ? RoeHumanoidClips.PathOf(c.transform, rig.transform) : bound is GameObject g ? RoeHumanoidClips.PathOf(g.transform, rig.transform) : "nothing";
                        Debug.Log($"[ROE] {rig.id} {nd.action} track '{track.name}' bound to {bound?.GetType().Name} at {where}");
                    }
                }
            }
        }

        /// <summary>Leg length (hips to foot) and the largest bone scale over a whole clip, by the fight's graph.</summary>
        public static void Legs()
        {
            string stage = RoeCapture.Arg("-roeStage", "e23_steel_s02");
            string id = RoeCapture.Arg("-roeChar", "g04");
            string clipName = RoeCapture.Arg("-roeClip", "skill_03");
            EditorSceneManager.OpenScene(RoeFightScene.ScenePath(stage), OpenSceneMode.Single);
            var rig = Object.FindObjectsByType<FighterRig>(FindObjectsSortMode.None).First(r => r.id == id);
            rig.Init();
            rig.Play(clipName, 1f, 0f);
            var a = rig.animator;
            var hips = a.GetBoneTransform(HumanBodyBones.Hips);
            var lf = a.GetBoneTransform(HumanBodyBones.LeftFoot);
            var rf = a.GetBoneTransform(HumanBodyBones.RightFoot);
            var bones = a.GetComponentsInChildren<Transform>(true);
            float len = rig.Length(clipName);
            const float dt = 1f / 60f;
            var sb = new System.Text.StringBuilder();
            for (float t = 0f; t < len; t += dt)
            {
                rig.Tick(dt);
                if (Mathf.RoundToInt(t / dt) % 15 != 0)
                    continue;
                float legL = Vector3.Distance(hips.position, lf.position), legR = Vector3.Distance(hips.position, rf.position);
                var big = bones.Select(b => (b, s: b.lossyScale.magnitude / Mathf.Sqrt(3f))).OrderByDescending(x => x.s).First();
                sb.Append($"\n[ROE]   t={t:F2} hips y {hips.position.y:F2} feet y {lf.position.y:F2}/{rf.position.y:F2} legs {legL:F2}/{legR:F2} m, " +
                          $"largest scale {big.b.name} {big.s:F2}, hips scale {hips.lossyScale.y:F2}");
            }
            Debug.Log($"[ROE] legs {id} {clipName} ({len:F2} s):" + sb);
        }

        /// <summary>
        /// What the skin round the tops of the thighs hangs on, by the bone with the largest weight, front and back (the
        /// buttock) of each hip: the family's nude base as the game weights it (its own prefab) beside the body the fight
        /// draws (RoeNudeBody: weights taken from the suit and the outfit over it) - user 10-06: "大腿根的权重还是不对呀".
        ///   -executeMethod RoeFighter.EditorTools.RoeFightProbe.HipWeights -roeChar b10
        /// </summary>
        public static void HipWeights()
        {
            string id = RoeCapture.Arg("-roeChar", "b10");
            string stage = RoeCapture.Arg("-roeStage", "e23_steel_s02");
            EditorSceneManager.OpenScene(RoeFightScene.ScenePath(stage), OpenSceneMode.Single);
            var rig = Object.FindObjectsByType<FighterRig>(FindObjectsInactive.Include, FindObjectsSortMode.None).First(r => r.id == id);
            var spec = RoeBurstBuilder.Load(id);
            var bodies = new List<(string what, SkinnedMeshRenderer smr)>();
            var fightBody = rig.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(r => r.name == "nude body");
            if (fightBody != null)
                bodies.Add(("the fight's body", fightBody));
            GameObject baseGo = null;
            if (spec?.nude != null && !string.IsNullOrEmpty(spec.nude.prefab))
            {
                baseGo = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(spec.nude.prefab));
                var baseBody = baseGo.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(r => r.name == spec.nude.renderer);
                if (baseBody != null)
                    bodies.Add(("the base's own", baseBody));
            }
            foreach (var (what, smr) in bodies)
            {
                var mesh = smr.sharedMesh;
                var binds = mesh.bindposes;
                var bw = mesh.boneWeights;
                var verts = mesh.vertices;
                var sb = new System.Text.StringBuilder($"[ROE] hip weights {id}, {what} ({smr.name}, {smr.bones.Length} bones):");
                foreach (var side in new[] { "L", "R" })
                {
                    // the hip joint where the thigh (or its upper twist bone) starts, the knee where the calf does
                    int hi = System.Array.FindIndex(smr.bones, b => b != null && (b.name == $"Bip001 {side} Thigh" || b.name == $"Bip001 {side}ThighTwist"));
                    int ki = System.Array.FindIndex(smr.bones, b => b != null && b.name == $"Bip001 {side} Calf");
                    if (hi < 0 || ki < 0)
                    {
                        sb.Append($" {side}: no thigh / calf among its bones;");
                        continue;
                    }
                    Vector3 hip = binds[hi].inverse.MultiplyPoint3x4(Vector3.zero), knee = binds[ki].inverse.MultiplyPoint3x4(Vector3.zero);
                    var seg = knee - hip;
                    // the body faces where the two hips' cross with up points (mesh space: y up)
                    var counts = new Dictionary<string, Dictionary<string, float>> { { "front", new Dictionary<string, float>() }, { "back", new Dictionary<string, float>() } };
                    int other = System.Array.FindIndex(smr.bones, b => b != null && (b.name == $"Bip001 {(side == "L" ? "R" : "L")} Thigh" || b.name == $"Bip001 {(side == "L" ? "R" : "L")}ThighTwist"));
                    var across = other >= 0 ? binds[other].inverse.MultiplyPoint3x4(Vector3.zero) - hip : Vector3.right;
                    var forward = Vector3.Cross(side == "L" ? -across : across, Vector3.up).normalized;
                    for (int v = 0; v < verts.Length; v++)
                    {
                        float s = Vector3.Dot(verts[v] - hip, seg) / seg.sqrMagnitude;
                        if (s < -0.25f || s > 0.3f || Vector3.Distance(verts[v], hip + seg * Mathf.Max(0f, s)) > 0.17f)
                            continue;
                        var w = bw[v];
                        string where = Vector3.Dot(verts[v] - hip, forward) >= 0f ? "front" : "back";
                        foreach (var (bi, wt) in new[] { (w.boneIndex0, w.weight0), (w.boneIndex1, w.weight1), (w.boneIndex2, w.weight2), (w.boneIndex3, w.weight3) })
                        {
                            if (wt <= 0f || bi >= smr.bones.Length || smr.bones[bi] == null)
                                continue;
                            string name = smr.bones[bi].name;
                            counts[where][name] = (counts[where].TryGetValue(name, out float c) ? c : 0f) + wt;
                        }
                    }
                    foreach (var kv in counts)
                    {
                        float total = kv.Value.Values.Sum();
                        if (total > 0f)
                            sb.Append($" {side} {kv.Key}: " + string.Join(", ", kv.Value.OrderByDescending(x => x.Value).Take(6).Select(x => $"{x.Key} {x.Value / total * 100f:F0}%")) + ";");
                    }
                }
                Debug.Log(sb.ToString());
            }
            if (baseGo != null)
                Object.DestroyImmediate(baseGo);
        }

        /// <summary>-roeLegRoll "from:keep" (FighterRig.rollFrom / rollKeep) on the probed fighter, for trying values without a rebuild.</summary>
        static void LegRollArg(FighterRig rig)
        {
            var spec = RoeCapture.Arg("-roeLegRoll", null);
            if (string.IsNullOrEmpty(spec))
                return;
            var p = spec.Split(':');
            rig.rollFrom = float.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture);
            rig.rollKeep = p.Length > 1 ? float.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture) : 0.3f;
            if (p.Length > 2)
                rig.rollFoot = int.Parse(p[2]);
            Debug.Log($"[ROE] {rig.id}: planted thighs turned past {rig.rollFrom:F0} deg keep {rig.rollKeep:F2} of it (-roeLegRoll)");
        }

        /// <summary>Which leg a bone belongs to by its name: 1 left, 2 right, 0 none (the trunk, the arms, the head).</summary>
        static int LegSide(string name)
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(name, "Thigh|Calf|Knee|knee|Foot|Toe|Leg|leg"))
                return 0;
            var m = System.Text.RegularExpressions.Regex.Match(name, @"^Bip001 ([LR])(?: |[A-Z])");
            if (m.Success)
                return m.Groups[1].Value == "L" ? 1 : 2;
            if (System.Text.RegularExpressions.Regex.IsMatch(name, @"(_L|_LT|\.L|Left)\b|^L_|\bL\b"))
                return 1;
            if (System.Text.RegularExpressions.Regex.IsMatch(name, @"(_R|_RT|\.R|Right)\b|^R_|\bR\b"))
                return 2;
            return 0;
        }

        /// <summary>
        /// The hips at moments of a clip, four ways (user 10-06: "大腿根的权重还是不对呀"): "fight" the body the fight draws;
        /// "weights" the same body coloured by what its skin hangs on (red the trunk, green the left leg, blue the right leg,
        /// mixed where it is shared); "base" the family's nude base with the game's OWN weights, posed bone for bone like her
        /// (bones of the base she lacks - buttocks, muscle strands, its own twist bones - follow their parents); "diff" the
        /// fight's body coloured by how far each vertex stands from where the base's own weights put it (white 0, red 4 cm).
        /// Same cameras as LegShots -roeAim hips.  Frames to out/hip_look/&lt;id&gt;_&lt;clip&gt;_&lt;t&gt;_&lt;way&gt;_&lt;view&gt;.png.
        ///   -executeMethod RoeFighter.EditorTools.RoeFightProbe.HipLook -roeChar b10 -roeClip ax_lunge -roeTimes 0.43 -roeSpeed 1.1
        /// </summary>
        public static void HipLook()
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            string stage = RoeCapture.Arg("-roeStage", "e23_steel_s02");
            string id = RoeCapture.Arg("-roeChar", "b10");
            string clipName = RoeCapture.Arg("-roeClip", "ax_lunge");
            float speed = float.Parse(RoeCapture.Arg("-roeSpeed", "1"), inv);
            var times = RoeCapture.Arg("-roeTimes", "0.43").Split(',').Select(s => float.Parse(s, inv)).ToArray();
            string outDir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "out", "hip_look");
            System.IO.Directory.CreateDirectory(outDir);
            ShaderUtil.allowAsyncCompilation = false;
            EditorSceneManager.OpenScene(RoeFightScene.ScenePath(stage), OpenSceneMode.Single);
            var rig = Object.FindObjectsByType<FighterRig>(FindObjectsInactive.Include, FindObjectsSortMode.None).First(r => r.id == id);
            rig.gameObject.SetActive(true);
            LegRollArg(rig);
            foreach (var other in Object.FindObjectsByType<FighterRig>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(r => r != rig))
                other.gameObject.SetActive(false);
            var hud = Object.FindFirstObjectByType<Canvas>();
            if (hud != null)
                hud.gameObject.SetActive(false);
            var all = rig.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled && r.gameObject.activeInHierarchy).ToList();
            foreach (var smr in all.OfType<SkinnedMeshRenderer>())
                smr.forceMatrixRecalculationPerRender = true;
            var weapons = new HashSet<Renderer>(rig.weaponRenderers ?? new Renderer[0]);
            var outfit = new HashSet<Renderer>(rig.burst != null ? rig.burst.pieces.Where(p => p.renderer != null).Select(p => (Renderer)p.renderer) : new Renderer[0]);
            foreach (var r in all.Where(r => r.name.Contains("body2") || r.name.Contains("__stays")))
                outfit.Add(r);
            var shown = all.Where(r => !outfit.Contains(r) && !weapons.Contains(r)).ToList();
            var body = all.OfType<SkinnedMeshRenderer>().First(r => r.name == "nude body");
            var bones = body.bones;
            var bodyMesh = body.sharedMesh;
            var bodyMaterials = body.sharedMaterials;

            // the body coloured by its weights
            var painted = Object.Instantiate(bodyMesh);
            var bw = bodyMesh.boneWeights;
            var paint = new Color[bodyMesh.vertexCount];
            Color[] sideColour = { new Color(1f, 0.25f, 0.2f), new Color(0.25f, 1f, 0.25f), new Color(0.25f, 0.45f, 1f) };
            int[] sideOf = bones.Select(b => b != null ? LegSide(b.name) : 0).ToArray();
            for (int v = 0; v < bw.Length; v++)
            {
                var w = bw[v];
                paint[v] = sideColour[sideOf[w.boneIndex0]] * w.weight0 + sideColour[sideOf[w.boneIndex1]] * w.weight1
                         + sideColour[sideOf[w.boneIndex2]] * w.weight2 + sideColour[sideOf[w.boneIndex3]] * w.weight3;
                paint[v].a = 1f;
            }
            painted.colors = paint;
            var paintMaterial = new Material(Shader.Find("Universal Render Pipeline/Particles/Simple Lit")) { hideFlags = HideFlags.DontSave };
            paintMaterial.SetColor("_BaseColor", Color.white);
            var paintMaterials = Enumerable.Repeat(paintMaterial, bodyMaterials.Length).ToArray();

            // the base, its body only, placed where its skin lies on hers: its root on her model's root, as RoeNudeBody laid it
            // (the suit's skin is cut out of this very body - b10: gap 0 mm); its skeleton is NOT hers (b01's spine stands 8 cm
            // from b10's), so each of its bones moves with her namesake's turn from her bind pose
            var spec = RoeBurstBuilder.Load(id);
            var baseGo = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(spec.nude.prefab));
            var baseBody = baseGo.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(r => r.name == spec.nude.renderer);
            foreach (var r in baseGo.GetComponentsInChildren<Renderer>(true))
                r.enabled = false;
            // the prefab carries no materials (the game sets them): hers - the base's body and head submeshes
            baseBody.sharedMaterials = Enumerable.Range(0, baseBody.sharedMesh.subMeshCount).Select(k => bodyMaterials[Mathf.Min(k, bodyMaterials.Length - 1)]).ToArray();
            baseBody.forceMatrixRecalculationPerRender = true;
            baseBody.updateWhenOffscreen = true;
            var baseBones = baseBody.bones;
            var baseBind = baseBody.sharedMesh.bindposes;
            // her bones' bind frames, world, from every skinned renderer she has
            var bindF = new Dictionary<string, Matrix4x4>();
            foreach (var smr in rig.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r => r.sharedMesh != null).OrderBy(r => r == body ? 0 : 1))
            {
                var bp = smr.sharedMesh.bindposes;
                for (int b = 0; b < smr.bones.Length && b < bp.Length; b++)
                    if (smr.bones[b] != null && !bindF.ContainsKey(smr.bones[b].name))
                        bindF[smr.bones[b].name] = smr.transform.localToWorldMatrix * bp[b].inverse;
            }
            var modelRoot = rig.animator.transform;
            baseGo.transform.SetPositionAndRotation(modelRoot.position, modelRoot.rotation);
            baseGo.transform.localScale = modelRoot.lossyScale;
            var P = baseBody.transform.localToWorldMatrix;
            Debug.Log($"[ROE] hip look {id}: model root {modelRoot.name} scale {modelRoot.lossyScale}, her body's scale {body.transform.lossyScale}, the base body's {baseBody.transform.lossyScale}");
            var bindB = new Dictionary<string, Matrix4x4>();
            for (int b = 0; b < baseBones.Length; b++)
                if (baseBones[b] != null)
                    bindB[baseBones[b].name] = P * baseBind[b].inverse;
            // how far apart the two binds stand, bone by bone (they should not)
            var apart = bindB.Where(kv => bindF.ContainsKey(kv.Key)).Select(kv => (kv.Key, d: Vector3.Distance(kv.Value.GetColumn(3), bindF[kv.Key].GetColumn(3)),
                                                                                    a: Quaternion.Angle(kv.Value.rotation, bindF[kv.Key].rotation))).OrderByDescending(x => x.d + x.a * 0.001f).ToList();
            Debug.Log($"[ROE] hip look {id}: base {baseBody.name} {baseBones.Length} bones, {apart.Count} also hers; binds apart at most " +
                      string.Join(", ", apart.Take(5).Select(x => $"{x.Key} {x.d * 100f:F2} cm {x.a:F1} deg")));
            // her bones by name: those her renderers are skinned to first (the fighter carries other skeletons with the same
            // names that do not move), then the rest of her model
            var fightBones = new Dictionary<string, Transform>();
            foreach (var x in rig.GetComponentsInChildren<SkinnedMeshRenderer>(true).OrderBy(r => r == body ? 0 : 1).SelectMany(r => r.bones)
                                 .Concat(rig.animator.GetComponentsInChildren<Transform>(true)))
                if (x != null && !fightBones.ContainsKey(x.name))
                    fightBones[x.name] = x;
            var baseAll = baseGo.GetComponentsInChildren<Transform>(true);
            var unmatched = baseBones.Where(b => b != null && !fightBones.ContainsKey(b.name)).ToList();
            Debug.Log($"[ROE] hip look {id}: base bones she lacks ({unmatched.Count}): " + string.Join(", ", unmatched.Select(b => $"{b.name} <- {b.parent?.name}")));
            // each base vertex's place in her bind pose and the vertex of her body there
            var baseVerts = baseBody.sharedMesh.vertices;
            var bodyVerts = bodyMesh.vertices;
            var cells = new Dictionary<Vector3Int, List<int>>();
            for (int i = 0; i < baseVerts.Length; i++)
            {
                var key = Vector3Int.FloorToInt(P.MultiplyPoint3x4(baseVerts[i]) / 0.002f);
                if (!cells.TryGetValue(key, out var list))
                    cells[key] = list = new List<int>();
                list.Add(i);
            }
            var twin = new int[bodyVerts.Length];
            int found = 0;
            for (int v = 0; v < bodyVerts.Length; v++)
            {
                var q = body.transform.localToWorldMatrix.MultiplyPoint3x4(bodyVerts[v]);
                var c = Vector3Int.FloorToInt(q / 0.002f);
                twin[v] = -1;
                float best = 0.0005f;
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dz = -1; dz <= 1; dz++)
                            if (cells.TryGetValue(c + new Vector3Int(dx, dy, dz), out var list))
                                foreach (int i in list)
                                {
                                    float d = Vector3.Distance(P.MultiplyPoint3x4(baseVerts[i]), q);
                                    if (d < best)
                                    {
                                        best = d;
                                        twin[v] = i;
                                    }
                                }
                if (twin[v] >= 0)
                    found++;
            }
            Debug.Log($"[ROE] hip look {id}: {found} of {bodyVerts.Length} vertices of her body found on the base within 0.5 mm");

            // the base's own limb helpers round the hips she lacks, driven as the ROE PMX export drives them (ripper_tpose
            // plan_joint_helper_moves + skin_rotation_share + apply_helper_grants): by name (twist / muscle strand), on the
            // thigh their skin lies on, a share of that thigh's turn against the pelvis - where the skin lies along the thigh,
            // 0.5 + t / 0.7 clamped, weighted mean - in place on the pelvis.  -roeStrands 0: they follow their parents.
            bool strandsOn = RoeCapture.Arg("-roeStrands", "1") != "0";
            var strands = new List<(Transform bone, Transform thigh, float share)>();
            Transform BaseBone(string n) => baseAll.FirstOrDefault(x => x.name == n);
            var basePelvis = BaseBone("Bip001 Pelvis");
            if (strandsOn)
            {
                var bbw = baseBody.sharedMesh.boneWeights;
                foreach (var (b, i) in baseBones.Select((b, i) => (b, i)))
                {
                    if (b == null || fightBones.ContainsKey(b.name) || !System.Text.RegularExpressions.Regex.IsMatch(b.name, "twist|muscle strand", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                        continue;
                    var island = Enumerable.Range(0, bbw.Length).Select(v => (v, w: (bbw[v].boneIndex0 == i ? bbw[v].weight0 : 0f) + (bbw[v].boneIndex1 == i ? bbw[v].weight1 : 0f)
                                                                                    + (bbw[v].boneIndex2 == i ? bbw[v].weight2 : 0f) + (bbw[v].boneIndex3 == i ? bbw[v].weight3 : 0f)))
                        .Where(x => x.w > 0.05f).ToList();
                    if (island.Count == 0)
                        continue;
                    var centroid = island.Aggregate(Vector3.zero, (s, x) => s + P.MultiplyPoint3x4(baseVerts[x.v]) * x.w) / island.Sum(x => x.w);
                    // the nearer thigh, by its segment from hip to knee in the bind pose
                    (Transform thigh, float t, float d) best = (null, 0f, float.MaxValue);
                    foreach (var s in new[] { "L", "R" })
                    {
                        string thighName = $"Bip001 {s} Thigh", calfName = $"Bip001 {s} Calf";
                        if (!bindB.ContainsKey(thighName) || !bindB.ContainsKey(calfName))
                            continue;
                        Vector3 hip = bindB[thighName].GetColumn(3), knee = bindB[calfName].GetColumn(3), seg = knee - hip;
                        float t = Vector3.Dot(centroid - hip, seg) / seg.sqrMagnitude;
                        float d = Vector3.Distance(centroid, hip + seg * Mathf.Clamp01(t));
                        if (d < best.d)
                            best = (BaseBone(thighName), t, d);
                    }
                    if (best.thigh == null || best.t < -0.5f || best.t > 0.6f || best.d > 0.2f)
                        continue;
                    Vector3 h0 = bindB[best.thigh.name].GetColumn(3), k0 = bindB[best.thigh.name.Replace("Thigh", "Calf")].GetColumn(3), sg = k0 - h0;
                    float share = island.Sum(x => Mathf.Clamp01(0.5f + Vector3.Dot(P.MultiplyPoint3x4(baseVerts[x.v]) - h0, sg) / sg.sqrMagnitude / 0.7f) * x.w) / island.Sum(x => x.w);
                    // (the PMX export leaves one that takes 97 % or more on the thigh as it is)
                    if (share < 0.97f)
                        strands.Add((b, best.thigh, share));
                    else
                        Debug.Log($"[ROE] hip look {id}: {b.name} on {best.thigh.name} x{share:F2} - stays on its parent");
                }
                Debug.Log($"[ROE] hip look {id}: the base's own hip helpers driven PMX-style: " +
                          string.Join(", ", strands.Select(s => $"{s.bone.name} on {s.thigh.name} x{s.share:F2}")));
            }

            var camGo = new GameObject("Hip Camera") { hideFlags = HideFlags.DontSave };
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 34f;
            cam.nearClipPlane = 0.05f;
            var a = rig.animator;
            var bakedF = new Mesh();
            var bakedB = new Mesh();
            foreach (float time in times)
            {
                rig.Init();
                rig.Play(clipName, speed, 0f);
                float t = 0f;
                const float dt = 1f / 60f;
                while (t + dt <= time)
                {
                    rig.Tick(dt);
                    t += dt;
                }
                // the base's bones: each of hers moves its namesake as it moved from her bind; the rest follow their parents
                foreach (var tb in baseAll)
                {
                    if (!fightBones.TryGetValue(tb.name, out var f) || tb == baseGo.transform)
                        continue;
                    var m = bindF.TryGetValue(tb.name, out var bf) && bindB.TryGetValue(tb.name, out var bb)
                        ? f.localToWorldMatrix * bf.inverse * bb
                        : f.localToWorldMatrix;
                    tb.SetPositionAndRotation(m.GetColumn(3), m.rotation);
                }
                foreach (var (bone, thigh, share) in strands)
                {
                    var dP = basePelvis.localToWorldMatrix * bindB[basePelvis.name].inverse;
                    var dT = thigh.localToWorldMatrix * bindB[thigh.name].inverse;
                    var bind = bindB[bone.name];
                    bone.SetPositionAndRotation(dP.MultiplyPoint3x4(bind.GetColumn(3)), Quaternion.Slerp(dP.rotation, dT.rotation, share) * bind.rotation);
                }
                foreach (var name in new[] { "Bip001 Pelvis", "Bip001 L Thigh", "Bip001 L Calf" })
                {
                    var tb = baseAll.FirstOrDefault(x => x.name == name);
                    Debug.Log($"[ROE] hip look {id}: {name} base at {tb?.position} (in its body's bones {System.Array.IndexOf(baseBones, tb)}), hers at {(fightBones.TryGetValue(name, out var f) ? f.position.ToString() : "-")}");
                }
                Debug.Log($"[ROE] hip look {id}: base root bone {baseBody.rootBone?.name}, materials {string.Join(", ", baseBody.sharedMaterials.Select(m => m != null ? m.name + " " + m.shader.name : "null"))}; " +
                          $"animator {baseGo.GetComponentInChildren<Animator>(true)?.name} {baseGo.GetComponentInChildren<Animator>(true)?.hasTransformHierarchy}");
                // how far her body stands from the base's own skinning, vertex by vertex
                body.BakeMesh(bakedF, true);
                baseBody.BakeMesh(bakedB, true);
                var pf = bakedF.vertices;
                var pb = bakedB.vertices;
                Matrix4x4 Rigid(Transform x) => Matrix4x4.TRS(x.position, x.rotation, Vector3.one);
                var toWorldF = Rigid(body.transform);
                var toWorldB = Rigid(baseBody.transform);
                var heat = new Color[pf.Length];
                var far = new List<float>();
                for (int v = 0; v < pf.Length; v++)
                {
                    if (twin[v] < 0)
                    {
                        heat[v] = new Color(0.3f, 0.3f, 0.3f, 1f);
                        continue;
                    }
                    float d = Vector3.Distance(toWorldF.MultiplyPoint3x4(pf[v]), toWorldB.MultiplyPoint3x4(pb[twin[v]]));
                    far.Add(d);
                    float k = Mathf.Clamp01(d / 0.04f);
                    heat[v] = Color.Lerp(Color.white, Color.red, k);
                    heat[v].a = 1f;
                }
                far.Sort();
                if (far.Count > 0)
                    Debug.Log($"[ROE] hip look {id} {clipName} t={t:F2}: her body from the base's own skinning: median {far[far.Count / 2] * 100f:F2} cm, " +
                              $"95% {far[(int)(far.Count * 0.95f)] * 100f:F2} cm, 99% {far[(int)(far.Count * 0.99f)] * 100f:F2} cm, most {far[far.Count - 1] * 100f:F2} cm");
                var heated = Object.Instantiate(bodyMesh);
                heated.colors = heat;

                var knees = (a.GetBoneTransform(HumanBodyBones.LeftLowerLeg).position + a.GetBoneTransform(HumanBodyBones.RightLowerLeg).position) * 0.5f;
                var hips = a.GetBoneTransform(HumanBodyBones.Hips).position;
                var look = Vector3.Lerp(hips, knees, 0.2f);
                var fwd = rig.transform.forward;
                var right = rig.transform.right;
                foreach (var way in new[] { "fight", "weights", "base", "diff" })
                {
                    foreach (var r in all)
                        r.enabled = way != "base" && shown.Contains(r);
                    baseBody.enabled = way == "base";
                    body.sharedMesh = way == "weights" ? painted : way == "diff" ? heated : bodyMesh;
                    body.sharedMaterials = way == "weights" || way == "diff" ? paintMaterials : bodyMaterials;
                    foreach (var (view, dir) in new[] { ("right", right), ("front", fwd), ("left", -right), ("back", -fwd) })
                    {
                        camGo.transform.position = look + dir * 1.5f + Vector3.up * 0.1f;
                        camGo.transform.LookAt(look, Vector3.up);
                        string file = System.IO.Path.Combine(outDir, $"{id}_{clipName}_{t:F2}_{way}_{view}.png");
                        RoeCapture.Render(cam, 640, 720, file);
                        RoeCapture.Render(cam, 640, 720, file);
                    }
                }
                body.sharedMesh = bodyMesh;
                body.sharedMaterials = bodyMaterials;
                foreach (var r in all)
                    r.enabled = true;
                baseBody.enabled = false;
                Object.DestroyImmediate(heated);
            }
            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(baseGo);
            Object.DestroyImmediate(painted);
            Debug.Log($"[ROE] hip look {id} {clipName}: {times.Length} moments to {outDir}");
        }

        /// <summary>
        /// How far each thigh turns from the pelvis in a fighter's clips (user 10-06: "大腿根的权重还是不对呀" - which of her own game
        /// clips come near the lunge): per clip, every 1/30 s, each thigh's direction in the pelvis' frame (forward, out to its
        /// side, down; from the bind pose's) and the angle it turned through; the clip's widest moments (most out to the side
        /// while turned 50+ degrees) and its most turned.  Thighs: the lower twist bones (they follow the thigh's swing).
        ///   -executeMethod RoeFighter.EditorTools.RoeFightProbe.HipScan -roeChar b10 [-roeClips skill_01,skill_02,ax_lunge] [-roeSpeed 1]
        /// </summary>
        public static void HipScan()
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            string stage = RoeCapture.Arg("-roeStage", "e23_steel_s02");
            string id = RoeCapture.Arg("-roeChar", "b10");
            var clipNames = RoeCapture.Arg("-roeClips", "idle_01,idle_02,react_01,react_02,skill_01,skill_02,skill_03,hurt,die,rip,ax_sweep,ax_chop,ax_lunge,ax_slam").Split(',');
            EditorSceneManager.OpenScene(RoeFightScene.ScenePath(stage), OpenSceneMode.Single);
            var rig = Object.FindObjectsByType<FighterRig>(FindObjectsInactive.Include, FindObjectsSortMode.None).First(r => r.id == id);
            rig.gameObject.SetActive(true);
            LegRollArg(rig);
            var body = rig.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(r => r.name == "nude body");
            Matrix4x4 BindOf(string boneName)
            {
                int i = System.Array.FindIndex(body.bones, b => b != null && b.name == boneName);
                return body.transform.localToWorldMatrix * body.sharedMesh.bindposes[i].inverse;
            }
            var pelvis = body.bones.First(b => b != null && b.name == "Bip001 Pelvis");
            var pelvisBind = BindOf("Bip001 Pelvis");
            var root = rig.animator.transform;
            // the pelvis' frame at bind: forward and up from her model root, out = to each side
            Vector3 fwd0 = root.forward, up0 = root.up, right0 = root.right;
            var legs = new[] { ("L", -1f), ("R", 1f) }.Select(x => (side: x.Item1, sign: x.Item2,
                bone: body.bones.First(b => b != null && System.Text.RegularExpressions.Regex.IsMatch(b.name, $@"^Bip001 {x.Item1} ?ThighTwist1$")),
                calf: rig.animator.GetBoneTransform(x.Item1 == "L" ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg))).ToList();
            // each thigh's roll against the pelvis, as the lower twist bone's drive measures it (thigh axis, pelvis rest)
            var helperRig = rig.GetComponentInChildren<RoeHelperRig>(true);
            var rollDrive = legs.ToDictionary(l => l.side, l => helperRig?.drives.FirstOrDefault(d => d.helper == l.bone && d.twistSource != null));
            float Roll(string side, Transform thigh)
            {
                var d = rollDrive[side];
                return d == null ? 0f : -RoeHelperRig.TwistAngle(Quaternion.Inverse(thigh.rotation) * d.twistSource.rotation * Quaternion.Inverse(d.twistRest), d.twistAxis);
            }
            var sb = new System.Text.StringBuilder($"[ROE] hip scan {id}:");
            foreach (var clipName in clipNames)
            {
                rig.Init();
                float speed = float.Parse(RoeCapture.Arg("-roeSpeed", "1"), inv);
                float length = rig.Length(clipName) / speed;
                if (length <= 0f)
                {
                    sb.Append($"\n  {clipName}: not hers");
                    continue;
                }
                rig.Play(clipName, speed, 0f);
                (float t, string side, float angle, Vector3 dir) widest = (0f, "", 0f, Vector3.zero), most = widest;
                float widestOut = -1f;
                var rollMin = new Dictionary<string, (float roll, float t)> { { "L", (float.MaxValue, 0f) }, { "R", (float.MaxValue, 0f) } };
                var rollMax = new Dictionary<string, (float roll, float t)> { { "L", (float.MinValue, 0f) }, { "R", (float.MinValue, 0f) } };
                const float dt = 1f / 30f;
                for (float t = 0f; t <= length; t += dt)
                {
                    // the pelvis' turn from its bind pose; the thigh's direction (hip -> knee) in the pelvis' bind frame
                    var dP = (pelvis.localToWorldMatrix * pelvisBind.inverse).rotation;
                    foreach (var (side, sign, bone, calf) in legs)
                    {
                        var thighBone = rig.animator.GetBoneTransform(side == "L" ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg);
                        float roll = Roll(side, thighBone);
                        if (roll < rollMin[side].roll)
                            rollMin[side] = (roll, t);
                        if (roll > rollMax[side].roll)
                            rollMax[side] = (roll, t);
                        var dir = Quaternion.Inverse(dP) * (calf.position - bone.position).normalized;
                        var local = new Vector3(Vector3.Dot(dir, fwd0), Vector3.Dot(dir, right0) * sign, -Vector3.Dot(dir, up0));
                        float angle = Vector3.Angle(local, Vector3.forward * 0f + new Vector3(0f, 0f, 1f));
                        // angle from straight down (0, 0, 1 in (forward, out, down))
                        if (angle > most.angle)
                            most = (t, side, angle, local);
                        if (angle >= 50f && local.y > widestOut)
                        {
                            widestOut = local.y;
                            widest = (t, side, angle, local);
                        }
                    }
                    rig.Tick(dt);
                }
                string D((float t, string side, float angle, Vector3 dir) x) =>
                    $"t {x.t:F2} {x.side} thigh {x.angle:F0} deg from down (forward {x.dir.x:F2}, out {x.dir.y:F2}, down {x.dir.z:F2})";
                sb.Append($"\n  {clipName} ({length:F2} s): most turned {D(most)}; widest {(widestOut >= 0f ? D(widest) : "-")}; " +
                          $"roll L {rollMin["L"].roll:F0} (t {rollMin["L"].t:F2}) .. {rollMax["L"].roll:F0} (t {rollMax["L"].t:F2}), " +
                          $"R {rollMin["R"].roll:F0} (t {rollMin["R"].t:F2}) .. {rollMax["R"].roll:F0} (t {rollMax["R"].t:F2})");
            }
            Debug.Log(sb.ToString());
        }

        /// <summary>
        /// Her body's vertices for a look outside Unity: bind place and posed place (world, at one moment of a clip) and the
        /// four weights by bone name, to _work/hip_dump/&lt;id&gt;_body.tsv; the joints (each bone's bind and posed head, from
        /// every skinned renderer she has, and the family base's bind heads placed as RoeNudeBody laid the base) to
        /// &lt;id&gt;_joints.tsv; her model root's axes at bind to &lt;id&gt;_axes.tsv.
        ///   -executeMethod RoeFighter.EditorTools.RoeFightProbe.BodyDump -roeChar b10 -roeClip ax_lunge -roeTimes 0.43 -roeSpeed 1.1
        /// </summary>
        public static void BodyDump()
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            string stage = RoeCapture.Arg("-roeStage", "e23_steel_s02");
            string id = RoeCapture.Arg("-roeChar", "b10");
            string clipName = RoeCapture.Arg("-roeClip", "ax_lunge");
            float speed = float.Parse(RoeCapture.Arg("-roeSpeed", "1"), inv);
            float time = float.Parse(RoeCapture.Arg("-roeTimes", "0.43").Split(',')[0], inv);
            string outDir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "_work", "hip_dump");
            System.IO.Directory.CreateDirectory(outDir);
            EditorSceneManager.OpenScene(RoeFightScene.ScenePath(stage), OpenSceneMode.Single);
            var rig = Object.FindObjectsByType<FighterRig>(FindObjectsInactive.Include, FindObjectsSortMode.None).First(r => r.id == id);
            rig.gameObject.SetActive(true);
            LegRollArg(rig);
            var body = rig.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(r => r.name == "nude body");
            var root = rig.animator.transform;
            // everything in her model root's frame, so bind and pose compare whatever the root does in the clip
            Matrix4x4 bindToRoot = root.worldToLocalMatrix * body.transform.localToWorldMatrix;
            var mesh = body.sharedMesh;
            var verts = mesh.vertices;
            var bw = mesh.boneWeights;
            var bones = body.bones;
            var joints = new System.Text.StringBuilder("who\tbone\tparent\tbx\tby\tbz\tpx\tpy\tpz\n");
            var seen = new HashSet<string>();
            var bindHead = new Dictionary<Transform, Vector3>();
            foreach (var smr in rig.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r => r.sharedMesh != null).OrderBy(r => r == body ? 0 : 1))
            {
                var bp = smr.sharedMesh.bindposes;
                var toRoot = root.worldToLocalMatrix * smr.transform.localToWorldMatrix;
                for (int b = 0; b < smr.bones.Length && b < bp.Length; b++)
                    if (smr.bones[b] != null && seen.Add(smr.bones[b].name))
                        bindHead[smr.bones[b]] = toRoot.MultiplyPoint3x4(bp[b].inverse.MultiplyPoint3x4(Vector3.zero));
            }
            // the base's bind heads where RoeNudeBody laid the base (its root on her model root)
            var spec = RoeBurstBuilder.Load(id);
            var baseGo = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(spec.nude.prefab));
            baseGo.transform.SetPositionAndRotation(root.position, root.rotation);
            var baseBody = baseGo.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(r => r.name == spec.nude.renderer);
            var baseToRoot = root.worldToLocalMatrix * baseBody.transform.localToWorldMatrix;
            var baseBind = baseBody.sharedMesh.bindposes;
            for (int b = 0; b < baseBody.bones.Length; b++)
                if (baseBody.bones[b] != null)
                {
                    var h = baseToRoot.MultiplyPoint3x4(baseBind[b].inverse.MultiplyPoint3x4(Vector3.zero));
                    joints.Append($"base\t{baseBody.bones[b].name}\t{(baseBody.bones[b].parent != null ? baseBody.bones[b].parent.name : "")}\t{h.x.ToString("F4", inv)}\t{h.y.ToString("F4", inv)}\t{h.z.ToString("F4", inv)}\t\t\t\n");
                }
            Object.DestroyImmediate(baseGo);
            var axes = $"right\t{root.InverseTransformDirection(rig.transform.right)}\nforward\t{root.InverseTransformDirection(rig.transform.forward)}\nup\t{root.InverseTransformDirection(Vector3.up)}\n";

            rig.Init();
            rig.Play(clipName, speed, 0f);
            float t = 0f;
            const float dt = 1f / 60f;
            while (t + dt <= time)
            {
                rig.Tick(dt);
                t += dt;
            }
            var baked = new Mesh();
            body.BakeMesh(baked, true);
            var posed = baked.vertices;
            var poseToRoot = root.worldToLocalMatrix * Matrix4x4.TRS(body.transform.position, body.transform.rotation, Vector3.one);
            foreach (var kv in bindHead)
            {
                var p = root.InverseTransformPoint(kv.Key.position);
                var h = kv.Value;
                joints.Append($"her\t{kv.Key.name}\t{(kv.Key.parent != null ? kv.Key.parent.name : "")}\t{h.x.ToString("F4", inv)}\t{h.y.ToString("F4", inv)}\t{h.z.ToString("F4", inv)}\t{p.x.ToString("F4", inv)}\t{p.y.ToString("F4", inv)}\t{p.z.ToString("F4", inv)}\n");
            }
            // the human bones too (her thighs may carry no skin, so no bind head: their posed head only)
            foreach (HumanBodyBones hb in new[] { HumanBodyBones.Hips, HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.Spine })
            {
                var x = rig.animator.GetBoneTransform(hb);
                var p = root.InverseTransformPoint(x.position);
                joints.Append($"human\t{x.name}\t{hb}\t\t\t\t{p.x.ToString("F4", inv)}\t{p.y.ToString("F4", inv)}\t{p.z.ToString("F4", inv)}\n");
            }
            var sb = new System.Text.StringBuilder("v\tbx\tby\tbz\tpx\tpy\tpz\tb0\tw0\tb1\tw1\tb2\tw2\tb3\tw3\n");
            string Bn(int i) => i < bones.Length && bones[i] != null ? bones[i].name : "?";
            for (int v = 0; v < verts.Length; v++)
            {
                var b = bindToRoot.MultiplyPoint3x4(verts[v]);
                var p = poseToRoot.MultiplyPoint3x4(posed[v]);
                var w = bw[v];
                sb.Append($"{v}\t{b.x.ToString("F4", inv)}\t{b.y.ToString("F4", inv)}\t{b.z.ToString("F4", inv)}\t{p.x.ToString("F4", inv)}\t{p.y.ToString("F4", inv)}\t{p.z.ToString("F4", inv)}\t" +
                          $"{Bn(w.boneIndex0)}\t{w.weight0.ToString("F3", inv)}\t{Bn(w.boneIndex1)}\t{w.weight1.ToString("F3", inv)}\t{Bn(w.boneIndex2)}\t{w.weight2.ToString("F3", inv)}\t{Bn(w.boneIndex3)}\t{w.weight3.ToString("F3", inv)}\n");
            }
            System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, $"{id}_body.tsv"), sb.ToString());
            System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, $"{id}_joints.tsv"), joints.ToString());
            System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, $"{id}_axes.tsv"), axes);
            // the triangles, to measure stretch
            System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, $"{id}_tris.txt"), string.Join(" ", mesh.triangles));
            Debug.Log($"[ROE] body dump {id} {clipName} t={t:F2}: {verts.Length} vertices, {bindHead.Count} bind heads to {outDir}");
        }

        /// <summary>
        /// The thigh roots graded several ways (RoeThighRoot), drawn at moments of a clip - tried, not guessed (user 10-06:
        /// "大腿根的权重还是不对呀").  -roeTry is a "|" list of ways "name" or "name:settings" (RoeThighRoot.Set, e.g.
        /// "pmx:center=0,blend=0.35"); "now" draws her as the fight does.  Every skinned renderer of hers but the weapons is
        /// graded (her body and the outfit over it move alike), with the legs of her body.  Two layers: her body alone and
        /// all of her.  Frames to out/hip_try/&lt;id&gt;_&lt;clip&gt;_&lt;t&gt;_&lt;way&gt;_&lt;layer&gt;_&lt;view&gt;.png; the log says what each way changed.
        ///   -executeMethod RoeFighter.EditorTools.RoeFightProbe.HipTry -roeChar b10 -roeClip ax_lunge -roeTimes 0.43 -roeSpeed 1.1
        /// </summary>
        public static void HipTry()
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            string stage = RoeCapture.Arg("-roeStage", "e23_steel_s02");
            string id = RoeCapture.Arg("-roeChar", "b10");
            string clipName = RoeCapture.Arg("-roeClip", "ax_lunge");
            float speed = float.Parse(RoeCapture.Arg("-roeSpeed", "1"), inv);
            var times = RoeCapture.Arg("-roeTimes", "0.43").Split(',').Select(s => float.Parse(s, inv)).ToArray();
            var ways = RoeCapture.Arg("-roeTry", "now|pmx:center=0,blend=0.35|mid:center=0.1,blend=0.25|late:center=0.15,blend=0.15|island:island=1")
                .Split('|').Select(w => w.Split(new[] { ':' }, 2)).Select(p => (name: p[0], settings: p.Length > 1 ? p[1] : null)).ToList();
            var views = RoeCapture.Arg("-roeViews", "right,front,left,back").Split(',');
            var layers = RoeCapture.Arg("-roeLayers", "body,all").Split(',');
            string outDir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "out", "hip_try");
            System.IO.Directory.CreateDirectory(outDir);
            ShaderUtil.allowAsyncCompilation = false;
            EditorSceneManager.OpenScene(RoeFightScene.ScenePath(stage), OpenSceneMode.Single);
            var rig = Object.FindObjectsByType<FighterRig>(FindObjectsInactive.Include, FindObjectsSortMode.None).First(r => r.id == id);
            rig.gameObject.SetActive(true);
            LegRollArg(rig);
            foreach (var other in Object.FindObjectsByType<FighterRig>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(r => r != rig))
                other.gameObject.SetActive(false);
            var hud = Object.FindFirstObjectByType<Canvas>();
            if (hud != null)
                hud.gameObject.SetActive(false);
            var all = rig.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled && r.gameObject.activeInHierarchy).ToList();
            foreach (var smr in all.OfType<SkinnedMeshRenderer>())
                smr.forceMatrixRecalculationPerRender = true;
            var weapons = new HashSet<Renderer>(rig.weaponRenderers ?? new Renderer[0]);
            var outfit = new HashSet<Renderer>(rig.burst != null ? rig.burst.pieces.Where(p => p.renderer != null).Select(p => (Renderer)p.renderer) : new Renderer[0]);
            foreach (var r in all.Where(r => r.name.Contains("body2") || r.name.Contains("__stays")))
                outfit.Add(r);
            var bodyLayer = all.Where(r => !outfit.Contains(r) && !weapons.Contains(r)).ToList();
            var body = all.OfType<SkinnedMeshRenderer>().First(r => r.name == "nude body");
            var a = rig.animator;
            var hipsBone = a.GetBoneTransform(HumanBodyBones.Hips);
            string N(Object o) => o != null ? o.name : "-";      // (Unity's missing references are not C# null)
            var helperRig = rig.GetComponentInChildren<RoeHelperRig>(true);
            if (helperRig != null)
                foreach (var d in helperRig.drives.Where(d => d.helper != null && d.helper.name.Contains("ThighTwist")))
                    Debug.Log($"[ROE] hip try {id}: drive {d.helper.name} <- {N(d.driver)}{(d.driver2 != null ? $" / {d.driver2.name} blend {d.blend:F2}" : "")}, " +
                              $"twist {N(d.twistSource)} x{d.twistShare:F2} + {d.twistOffset:F1}, back {N(d.twistSource2)} x{d.twistShare2:F2}, at {N(d.positionBone)}, fit {d.fitError:F1} deg");

            // the hip helpers ("helper=1"): one per leg at the hip joint, lying like the pelvis in the bind pose, turned each
            // frame half way between what the pelvis and the leg's upper twist bone turned from their bind poses
            var legs = RoeThighRoot.Legs(body, rig.animator.transform);
            Debug.Log($"[ROE] hip try {id}: legs " + string.Join("; ", legs.Select(l => $"{l.side} hip {l.hip} knee {l.knee} upper {l.upper}")));
            Matrix4x4 BindOf(string boneName)
            {
                int i = System.Array.FindIndex(body.bones, b => b != null && b.name == boneName);
                return body.transform.localToWorldMatrix * body.sharedMesh.bindposes[i].inverse;
            }
            var pelvisBone = body.bones.First(b => b != null && b.name == "Bip001 Pelvis");
            var pelvisBind = BindOf("Bip001 Pelvis");
            Vector3 bindUp = rig.animator.transform.up, bindRight = rig.animator.transform.right, bindForward = rig.animator.transform.forward;
            var hipHelpers = new List<(int side, Transform helper, Transform twist, Matrix4x4 twistBind, Matrix4x4 bind)>();
            foreach (var leg in legs)
            {
                var twist = body.bones.First(b => b != null && b.name == leg.upper);
                var twistBind = BindOf(leg.upper);
                var go = new GameObject($"Bip001 {(leg.side == 1 ? "L" : "R")} HipHelper") { hideFlags = HideFlags.DontSave };
                go.transform.SetParent(rig.transform, true);
                var bind = Matrix4x4.TRS(twistBind.GetColumn(3), pelvisBind.rotation, Vector3.one);
                hipHelpers.Add((leg.side, go.transform, twist, twistBind, bind));
            }
            void DriveHelpers()
            {
                foreach (var (_, helper, twist, twistBind, bind) in hipHelpers)
                {
                    var dp = (pelvisBone.localToWorldMatrix * pelvisBind.inverse).rotation;
                    var dt = (twist.localToWorldMatrix * twistBind.inverse).rotation;
                    helper.SetPositionAndRotation(twist.position, Quaternion.Slerp(dp, dt, 0.5f) * bind.rotation);
                }
            }

            // "roll=k": the upper twist bones follow the thigh but give back k of its roll against the pelvis (Biped's twist
            // links: the roll spreads down the thigh) - a drive built like the lower twist bone's (same thigh axis, same
            // pelvis rest), its rest turn in the thigh's frame from the bind poses (the thigh carries no skin: its bind
            // turn from the lower twist bone's and that one's drive)
            var rollDrives = new List<(Transform upper, RoeHelperRig.Drive drive)>();
            if (helperRig != null)
                foreach (var leg in legs)
                {
                    string s = leg.side == 1 ? "L" : "R";
                    var lowerName = leg.upper + "1";
                    var d1 = helperRig.drives.FirstOrDefault(d => d.helper != null && d.helper.name == lowerName);
                    var upper = body.bones.First(b => b != null && b.name == leg.upper);
                    if (d1 == null || d1.twistSource == null || !body.bones.Any(b => b != null && b.name == lowerName))
                    {
                        Debug.Log($"[ROE] hip try {id}: no lower twist drive for {leg.upper} - roll ways leave it as driven");
                        continue;
                    }
                    var thighBind = BindOf(lowerName).rotation * Quaternion.Inverse(d1.rotation) * Quaternion.AngleAxis(-d1.twistOffset, d1.twistAxis);
                    rollDrives.Add((upper, new RoeHelperRig.Drive
                    {
                        helper = upper, driver = d1.driver, rotation = Quaternion.Inverse(thighBind) * BindOf(leg.upper).rotation,
                        twistAxis = d1.twistAxis, twistSource = d1.twistSource, twistRest = d1.twistRest,
                    }));
                    Debug.Log($"[ROE] hip try {id}: roll drive for {leg.upper} from {lowerName}'s ({N(d1.driver)}, axis {d1.twistAxis}, back from {N(d1.twistSource)})");
                }

            // each way's meshes (and bones, with the hip helpers): every skinned renderer of hers but the weapons and the
            // rigid pieces, graded with her body's legs
            var skinned = all.OfType<SkinnedMeshRenderer>().Where(r => !weapons.Contains(r) && r.sharedMesh != null).ToList();
            var own = skinned.ToDictionary(r => r, r => (mesh: r.sharedMesh, bones: r.bones));
            var meshes = new Dictionary<string, Dictionary<SkinnedMeshRenderer, (Mesh mesh, Transform[] bones)>>();
            var rollOf = new Dictionary<string, float>();
            var driveOf = new HashSet<string>();
            var spreadOf = new Dictionary<string, (float share, float from)>();
            var unrollOf = new Dictionary<string, float>();
            var thighs = new[] { (a.GetBoneTransform(HumanBodyBones.LeftUpperLeg), a.GetBoneTransform(HumanBodyBones.LeftLowerLeg), -1f),
                                 (a.GetBoneTransform(HumanBodyBones.RightUpperLeg), a.GetBoneTransform(HumanBodyBones.RightLowerLeg), 1f) };
            foreach (var (name, settings) in ways)
            {
                if (settings == null)
                    continue;
                RoeThighRoot.Reset();
                string described = RoeThighRoot.Set(settings);
                rollOf[name] = RoeThighRoot.RollBack;
                if (RoeThighRoot.ForceDrive)
                    driveOf.Add(name);
                if (RoeThighRoot.Spread < 1f)
                    spreadOf[name] = (RoeThighRoot.Spread, RoeThighRoot.SpreadFrom);
                if (RoeThighRoot.Unroll > 0f)
                    unrollOf[name] = RoeThighRoot.Unroll;
                if (!RoeThighRoot.GradeOn && !RoeThighRoot.Helper && !RoeThighRoot.ButtOn)
                {
                    Debug.Log($"[ROE] hip try {id} {name}: weights as they are, roll given back {RoeThighRoot.RollBack:F2}");
                    continue;
                }
                var set = new Dictionary<SkinnedMeshRenderer, (Mesh, Transform[])>();
                var notes = new List<string>();
                foreach (var r in skinned)
                {
                    var legsHere = RoeThighRoot.Moved(legs, body, r);
                    if (r != body && !RoeThighRoot.IsSoft(own[r].mesh, own[r].bones, legsHere, out float soft))
                    {
                        if (soft > 0f)
                            notes.Add($"{r.name}: rigid ({soft:P0} of it round the hips partly on a leg), left alone");
                        continue;
                    }
                    var copy = Object.Instantiate(own[r].mesh);
                    var bones = own[r].bones;
                    int n = 0;
                    var note = new System.Text.StringBuilder();
                    if (RoeThighRoot.GradeOn)
                    {
                        n += RoeThighRoot.Grade(copy, bones, legsHere, out string graded);
                        note.Append(graded);
                    }
                    if (RoeThighRoot.ButtOn)
                    {
                        n += RoeThighRoot.Butt(copy, bones, legsHere, out string butt);
                        note.Append((note.Length > 0 ? ", " : "") + butt);
                    }
                    if (RoeThighRoot.Helper)
                    {
                        var rootIndex = new Dictionary<int, int>();
                        var bindposes = copy.bindposes.ToList();
                        var list = bones.ToList();
                        foreach (var (side, helper, _, _, bind) in hipHelpers)
                        {
                            rootIndex[side] = list.Count;
                            list.Add(helper);
                            bindposes.Add(bind.inverse * r.transform.localToWorldMatrix);
                        }
                        copy.bindposes = bindposes.ToArray();
                        int split = RoeThighRoot.Split(copy, list.ToArray(), legsHere, rootIndex, out string hung);
                        if (split > 0)
                        {
                            bones = list.ToArray();
                            n += split;
                            note.Append((note.Length > 0 ? ", " : "") + hung);
                        }
                        else
                            copy.bindposes = own[r].mesh.bindposes;
                    }
                    if (n > 0)
                    {
                        set[r] = (copy, bones);
                        notes.Add($"{r.name}: {note}");
                    }
                    else
                        Object.DestroyImmediate(copy);
                }
                meshes[name] = set;
                Debug.Log($"[ROE] hip try {id} {name} ({described}): " + string.Join("; ", notes));
            }
            RoeThighRoot.Reset();

            var camGo = new GameObject("Hip Camera") { hideFlags = HideFlags.DontSave };
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 34f;
            cam.nearClipPlane = 0.05f;
            foreach (float time in times)
            {
                rig.Init();
                rig.Play(clipName, speed, 0f);
                float t = 0f;
                const float dt = 1f / 60f;
                while (t + dt <= time)
                {
                    rig.Tick(dt);
                    t += dt;
                }
                Debug.Log($"[ROE] hip try {id} t={t:F2}: thighs turned against the pelvis L {rig.Roll[0].x:F0} -> {rig.Roll[0].y:F0} deg, R {rig.Roll[1].x:F0} -> {rig.Roll[1].y:F0} deg");
                {
                    // each thigh's direction in the pelvis' frame: forward (her model root's at bind, turned as the pelvis turned),
                    // out to its side (hip to hip), down
                    var dP = (pelvisBone.localToWorldMatrix * pelvisBind.inverse).rotation;
                    var sbDir = new System.Text.StringBuilder($"[ROE] hip try {id} t={t:F2}: thigh directions in the pelvis' frame");
                    foreach (var (thigh, calf, sign) in thighs)
                    {
                        var other = thighs.First(x => x.Item1 != thigh).Item1;
                        var sideAxis = (thigh.position - other.position).normalized;
                        var fwdAxis = (dP * bindForward).normalized;
                        var downAxis = (dP * -bindUp).normalized;
                        var dd = (calf.position - thigh.position).normalized;
                        sbDir.Append($"; {thigh.name}: forward {Vector3.Dot(dd, fwdAxis):F2}, out {Vector3.Dot(dd, sideAxis):F2}, down {Vector3.Dot(dd, downAxis):F2}");
                    }
                    Debug.Log(sbDir.ToString());
                }
                var asDriven = rollDrives.ToDictionary(x => x.upper, x => x.upper.rotation);
                foreach (var (upper, drive) in rollDrives)
                {
                    foreach (float k in new[] { 0f, 1f })
                    {
                        drive.twistShare = k;
                        Debug.Log($"[ROE] hip try {id} t={t:F2}: {upper.name} as driven is {Quaternion.Angle(asDriven[upper], RoeHelperRig.Rotation(drive)):F0} deg from following the thigh with {(k == 0f ? "all" : "none")} of its roll");
                    }
                }
                DriveHelpers();
                foreach (var (side, helper, twist, _, _) in hipHelpers)
                    Debug.Log($"[ROE] hip try {id} t={t:F2}: hip helper {side} {Quaternion.Angle(helper.rotation, pelvisBone.rotation * Quaternion.Inverse(pelvisBind.rotation) * hipHelpers.First(h => h.side == side).bind.rotation):F0} deg off the pelvis, " +
                              $"{Quaternion.Angle((pelvisBone.localToWorldMatrix * pelvisBind.inverse).rotation, (twist.localToWorldMatrix * hipHelpers.First(h => h.side == side).twistBind.inverse).rotation):F0} deg between the pelvis and the leg");
                var knees = (a.GetBoneTransform(HumanBodyBones.LeftLowerLeg).position + a.GetBoneTransform(HumanBodyBones.RightLowerLeg).position) * 0.5f;
                var look = Vector3.Lerp(hipsBone.position, knees, 0.2f);
                var fwd = rig.transform.forward;
                var right = rig.transform.right;
                var dirs = new Dictionary<string, Vector3> { { "right", right }, { "front", fwd }, { "left", -right }, { "back", -fwd } };
                // every helper as the clip left it (a game clip keys them), to start each way from
                var keyed = helperRig != null ? helperRig.drives.Where(d => d.helper != null).Select(d => (d.helper, d.helper.position, d.helper.rotation)).ToList()
                                              : new List<(Transform, Vector3, Quaternion)>();
                var thighPose = thighs.Select(x => x.Item1.rotation).ToArray();
                foreach (var (name, _) in ways)
                {
                    for (int k = 0; k < thighs.Length; k++)
                        thighs[k].Item1.rotation = thighPose[k];
                    foreach (var (h, p, q) in keyed)
                        h.SetPositionAndRotation(p, q);
                    // "spread=k": a thigh's swing out to its side past "spreadfrom" degrees from down (in the pelvis' front
                    // plane) kept at k; its twist bones follow (the helper rig)
                    // "unroll=k": the thigh (and so the leg below it) turned back about its own axis by k of its roll against
                    // the pelvis (the lower twist bone's drive measures it: thigh axis, pelvis rest)
                    bool unroll = unrollOf.TryGetValue(name, out float uk);
                    if (unroll)
                    {
                        foreach (var (_, drive) in rollDrives)
                        {
                            var thigh = drive.driver;
                            float tau = RoeHelperRig.TwistAngle(Quaternion.Inverse(thigh.rotation) * drive.twistSource.rotation * Quaternion.Inverse(drive.twistRest), drive.twistAxis);
                            thigh.rotation = thigh.rotation * Quaternion.AngleAxis(uk * tau, drive.twistAxis);
                            Debug.Log($"[ROE] hip try {id} t={t:F2} {name}: {thigh.name} rolled {-tau:F0} deg against the pelvis, {-(1f - uk) * tau:F0} left");
                        }
                        helperRig?.Apply(1f);
                    }
                    bool spread = spreadOf.TryGetValue(name, out var sp);
                    if (spread)
                    {
                        var dP = (pelvisBone.localToWorldMatrix * pelvisBind.inverse).rotation;
                        foreach (var (thigh, calf, sign) in thighs)
                        {
                            // down and out to this side in the bind pose (her model root's, when the bind frames were read),
                            // turned as the pelvis turned since
                            Vector3 down = (dP * -bindUp).normalized, outw = (dP * (bindRight * sign)).normalized;
                            var d = (calf.position - thigh.position).normalized;
                            float alpha = Mathf.Atan2(Vector3.Dot(d, outw), Vector3.Dot(d, down)) * Mathf.Rad2Deg;
                            if (alpha <= sp.from)
                                continue;
                            float target = sp.from + (alpha - sp.from) * sp.share;
                            Vector3 Front(float deg) => down * Mathf.Cos(deg * Mathf.Deg2Rad) + outw * Mathf.Sin(deg * Mathf.Deg2Rad);
                            thigh.rotation = Quaternion.FromToRotation(Front(alpha), Front(target)) * thigh.rotation;
                            Debug.Log($"[ROE] hip try {id} t={t:F2} {name}: {thigh.name} out {alpha:F0} -> {target:F0} deg");
                        }
                        helperRig?.Apply(1f);
                    }
                    // "drive=1": the helper rig drives them as it does under motion capture
                    if ((driveOf.Contains(name) || spread || unroll) && helperRig != null)
                    {
                        helperRig.Apply(1f);
                        foreach (var (upper, _) in rollDrives)
                            asDriven[upper] = upper.rotation;
                    }
                    else
                        foreach (var (upper, _) in rollDrives)
                            asDriven[upper] = keyed.First(x => x.Item1 == upper).Item3;
                    // the upper twist bones as driven, or giving back part of the roll; then the hip helpers from them
                    foreach (var (upper, drive) in rollDrives)
                    {
                        upper.rotation = asDriven[upper];
                        if (rollOf.TryGetValue(name, out float k) && k >= 0f)
                        {
                            drive.twistShare = k;
                            upper.rotation = RoeHelperRig.Rotation(drive);
                        }
                    }
                    DriveHelpers();
                    foreach (var r in skinned)
                    {
                        var (m, bones) = meshes.TryGetValue(name, out var set) && set.TryGetValue(r, out var x) ? x : own[r];
                        r.sharedMesh = m;
                        r.bones = bones;
                    }
                    foreach (var layer in layers)
                    {
                        foreach (var r in all)
                            r.enabled = layer == "all" ? !weapons.Contains(r) : bodyLayer.Contains(r);
                        foreach (var view in views.Where(dirs.ContainsKey))
                        {
                            camGo.transform.position = look + dirs[view] * 1.5f + Vector3.up * 0.1f;
                            camGo.transform.LookAt(look, Vector3.up);
                            string file = System.IO.Path.Combine(outDir, $"{id}_{clipName}_{t:F2}_{name}_{layer}_{view}.png");
                            RoeCapture.Render(cam, 640, 720, file);
                            RoeCapture.Render(cam, 640, 720, file);
                        }
                    }
                    foreach (var r in all)
                        r.enabled = true;
                }
                foreach (var r in skinned)
                {
                    r.sharedMesh = own[r].mesh;
                    r.bones = own[r].bones;
                }
            }
            Object.DestroyImmediate(camGo);
            foreach (var h in hipHelpers)
                Object.DestroyImmediate(h.helper.gameObject);
            foreach (var set in meshes.Values)
                foreach (var m in set.Values)
                    Object.DestroyImmediate(m.mesh);
            Debug.Log($"[ROE] hip try {id} {clipName}: {times.Length} moments x {ways.Count} ways to {outDir}");
        }

        /// <summary>
        /// Close views of a fighter's legs at moments of a clip as the fight drives her (motion capture, helper rig, grips): all
        /// of her, her body alone (skin, nude base, head), her outfit alone (the burst's pieces and what stays); from her right,
        /// the front and her left (user 10-06: "卡地亚的膝盖和小腿是不是有问题").  Frames to out/leg_shots/&lt;id&gt;_&lt;clip&gt;_&lt;t&gt;_&lt;layer&gt;_&lt;view&gt;.png;
        /// the log says which renderer went to which layer and, per leg, the knee bend and the helper bones' turn off the calf.
        ///   -executeMethod RoeFighter.EditorTools.RoeFightProbe.LegShots -roeChar b10 -roeClip ax_lunge -roeTimes 0.45 [-roeSpeed 1.1]
        /// </summary>
        public static void LegShots()
        {
            string stage = RoeCapture.Arg("-roeStage", "e23_steel_s02");
            string id = RoeCapture.Arg("-roeChar", "b10");
            string clipName = RoeCapture.Arg("-roeClip", "ax_lunge");
            float speed = float.Parse(RoeCapture.Arg("-roeSpeed", "1"), System.Globalization.CultureInfo.InvariantCulture);
            var times = RoeCapture.Arg("-roeTimes", "0.45").Split(',').Select(s => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            string outDir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "out", "leg_shots");
            System.IO.Directory.CreateDirectory(outDir);
            ShaderUtil.allowAsyncCompilation = false;
            EditorSceneManager.OpenScene(RoeFightScene.ScenePath(stage), OpenSceneMode.Single);
            var rig = Object.FindObjectsByType<FighterRig>(FindObjectsInactive.Include, FindObjectsSortMode.None).First(r => r.id == id);
            rig.gameObject.SetActive(true);
            LegRollArg(rig);
            foreach (var other in Object.FindObjectsByType<FighterRig>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(r => r != rig))
                other.gameObject.SetActive(false);
            var hud = Object.FindFirstObjectByType<Canvas>();
            if (hud != null)
                hud.gameObject.SetActive(false);
            var all = rig.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled && r.gameObject.activeInHierarchy).ToList();
            foreach (var smr in all.OfType<SkinnedMeshRenderer>())
                smr.forceMatrixRecalculationPerRender = true;
            var weapons = new HashSet<Renderer>(rig.weaponRenderers ?? new Renderer[0]);
            var outfit = new HashSet<Renderer>(rig.burst != null ? rig.burst.pieces.Where(p => p.renderer != null).Select(p => (Renderer)p.renderer) : new Renderer[0]);
            foreach (var r in all.Where(r => r.name.Contains("body2") || r.name.Contains("__stays")))
                outfit.Add(r);
            var body = all.Where(r => !outfit.Contains(r) && !weapons.Contains(r)).ToList();
            Debug.Log($"[ROE] leg shots {id}: body {string.Join(", ", body.Select(r => r.name))}; outfit {outfit.Count} renderers " +
                      $"({string.Join(", ", outfit.Select(r => r.name).Take(12))}); weapons {string.Join(", ", weapons.Where(w => w != null).Select(r => r.name))}");
            // what the skin of each lower leg hangs on: per renderer, the vertices round the middle of the shin (bind pose, in the
            // mesh's own space: the knee and the ankle are where the bind poses put the calf's and the foot's bones), by the bone
            // with the largest weight
            foreach (var smr in all.OfType<SkinnedMeshRenderer>().Where(r => r.sharedMesh != null && !weapons.Contains(r)))
            {
                var mesh = smr.sharedMesh;
                var binds = mesh.bindposes;
                var bw = mesh.boneWeights;
                if (bw.Length != mesh.vertexCount || binds.Length != smr.bones.Length)
                    continue;
                var report = new System.Text.StringBuilder();
                foreach (var (calfBone, footBone, side, s0, s1) in new[] {
                             (HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, "L thigh", 0.25f, 0.75f), (HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, "R thigh", 0.25f, 0.75f),
                             (HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, "L knee", -0.2f, 0.15f), (HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, "R knee", -0.2f, 0.15f),
                             (HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, "L shin", 0.25f, 0.75f), (HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, "R shin", 0.25f, 0.75f) })
                {
                    int ci = System.Array.IndexOf(smr.bones, rig.animator.GetBoneTransform(calfBone));
                    int fi = System.Array.IndexOf(smr.bones, rig.animator.GetBoneTransform(footBone));
                    if (ci < 0 || fi < 0)
                        continue;
                    Vector3 knee = binds[ci].inverse.MultiplyPoint3x4(Vector3.zero), ankle = binds[fi].inverse.MultiplyPoint3x4(Vector3.zero);
                    var seg = ankle - knee;
                    var counts = new Dictionary<string, int>();
                    var verts = mesh.vertices;
                    for (int v = 0; v < verts.Length; v++)
                    {
                        float s = Vector3.Dot(verts[v] - knee, seg) / seg.sqrMagnitude;
                        if (s < s0 || s > s1 || Vector3.Distance(verts[v], knee + seg * s) > 0.09f)
                            continue;
                        var w = bw[v];
                        int top = w.weight0 >= w.weight1 && w.weight0 >= w.weight2 && w.weight0 >= w.weight3 ? w.boneIndex0
                                : w.weight1 >= w.weight2 && w.weight1 >= w.weight3 ? w.boneIndex1 : w.weight2 >= w.weight3 ? w.boneIndex2 : w.boneIndex3;
                        string name = top < smr.bones.Length && smr.bones[top] != null ? smr.bones[top].name : "?";
                        counts[name] = (counts.TryGetValue(name, out int c) ? c : 0) + 1;
                    }
                    if (counts.Count > 0)
                        report.Append($" {side}: " + string.Join(", ", counts.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value}")) + ";");
                }
                if (report.Length > 0)
                    Debug.Log($"[ROE] leg shots {id} {smr.name}:{report}");
                // how much of the hip each leg twist bone's skin should take, the way the ROE PMX export grades it
                // (ripper_tpose export_character_model_blender.skin_rotation_share: each vertex projected on the thigh,
                // 0.5 + t / 0.7 clamped to 0..1, weighted mean)
                var shares = new System.Text.StringBuilder();
                for (int b = 0; b < smr.bones.Length; b++)
                {
                    var bone = smr.bones[b];
                    if (bone == null || !System.Text.RegularExpressions.Regex.IsMatch(bone.name, "ThighTwist"))
                        continue;
                    bool left = bone.name.Contains(" L") || bone.name.Contains("LThigh");
                    // the thigh bone itself may carry no skin (b10: the whole thigh is on its two twist bones), so the hip is
                    // where the upper twist bone starts (on the hip joint) and the knee where the calf does
                    string upperName = System.Text.RegularExpressions.Regex.Replace(bone.name, @"1$", "");
                    int ui = System.Array.FindIndex(smr.bones, x => x != null && x.name == upperName);
                    int li = System.Array.IndexOf(smr.bones, rig.animator.GetBoneTransform(left ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg));
                    if (ui < 0 || li < 0)
                        continue;
                    Vector3 hip = binds[ui].inverse.MultiplyPoint3x4(Vector3.zero), knee = binds[li].inverse.MultiplyPoint3x4(Vector3.zero);
                    var seg = knee - hip;
                    float taken = 0f, total = 0f;
                    int n = 0, above = 0;
                    var verts = mesh.vertices;
                    for (int v = 0; v < bw.Length; v++)
                    {
                        var w = bw[v];
                        float wt = (w.boneIndex0 == b ? w.weight0 : 0f) + (w.boneIndex1 == b ? w.weight1 : 0f) + (w.boneIndex2 == b ? w.weight2 : 0f) + (w.boneIndex3 == b ? w.weight3 : 0f);
                        if (wt <= 0.05f)
                            continue;
                        float t = Vector3.Dot(verts[v] - hip, seg) / seg.sqrMagnitude;
                        taken += Mathf.Clamp01(0.5f + t / 0.7f) * wt;
                        total += wt;
                        n++;
                        if (t < 0.15f)
                            above++;
                    }
                    if (n > 0)
                        shares.Append($" {bone.name}: {n} vertices, share {taken / total:F2}, {above} of them within 0.15 of the hip;");
                }
                if (shares.Length > 0)
                    Debug.Log($"[ROE] leg shots {id} {smr.name} twist shares:{shares}");
            }
            var camGo = new GameObject("Leg Camera") { hideFlags = HideFlags.DontSave };
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 34f;
            cam.nearClipPlane = 0.05f;
            var a = rig.animator;
            foreach (float time in times)
            {
                rig.Init();
                rig.Play(clipName, speed, 0f);
                float t = 0f;
                const float dt = 1f / 60f;
                while (t + dt <= time)
                {
                    rig.Tick(dt);
                    t += dt;
                }
                var log = new System.Text.StringBuilder($"[ROE] leg shots {id} {clipName} t={t:F2}:");
                foreach (var (upper, lower, foot, side) in new[] { (HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, "L"),
                                                                   (HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, "R") })
                {
                    Transform u = a.GetBoneTransform(upper), l = a.GetBoneTransform(lower), f = a.GetBoneTransform(foot);
                    float bend = Vector3.Angle(l.position - u.position, f.position - l.position);
                    log.Append($" {side} knee bent {bend:F0} deg;");
                    foreach (var helper in u.GetComponentsInChildren<Transform>(true).Where(x => x != u && x != l && !x.IsChildOf(l) && x.parent == u))
                        log.Append($" {helper.name} {Quaternion.Angle(helper.rotation, l.rotation):F0} deg off the calf, {Quaternion.Angle(helper.rotation, u.rotation):F0} off the thigh,");
                }
                Debug.Log(log.ToString());
                var knees = (a.GetBoneTransform(HumanBodyBones.LeftLowerLeg).position + a.GetBoneTransform(HumanBodyBones.RightLowerLeg).position) * 0.5f;
                var hips = a.GetBoneTransform(HumanBodyBones.Hips).position;
                // -roeAim hips: the tops of the thighs and the buttocks, closer
                bool atHips = RoeCapture.Arg("-roeAim", "knees") == "hips";
                var look = atHips ? Vector3.Lerp(hips, knees, 0.2f) : Vector3.Lerp(knees, hips, 0.25f);
                float distance = atHips ? 1.5f : 2.2f;
                var fwd = rig.transform.forward;
                var right = rig.transform.right;
                foreach (var (layer, show) in new[] { ("all", all), ("body", body), ("outfit", all.Where(r => outfit.Contains(r)).ToList()) })
                {
                    foreach (var r in all)
                        r.enabled = show.Contains(r);
                    foreach (var (view, dir) in atHips ? new[] { ("right", right), ("front", fwd), ("left", -right), ("back", -fwd) }
                                                       : new[] { ("right", right), ("front", fwd), ("left", -right) })
                    {
                        camGo.transform.position = look + dir * distance + Vector3.up * (atHips ? 0.1f : 0.25f);
                        camGo.transform.LookAt(look, Vector3.up);
                        string file = System.IO.Path.Combine(outDir, $"{id}_{clipName}_{t:F2}_{layer}_{view}{(atHips ? "_hips" : "")}.png");
                        RoeCapture.Render(cam, 640, 720, file);
                        RoeCapture.Render(cam, 640, 720, file);
                    }
                }
                foreach (var r in all)
                    r.enabled = true;
            }
            Object.DestroyImmediate(camGo);
            Debug.Log($"[ROE] leg shots {id} {clipName}: {times.Length} moments to {outDir}");
        }
            /// <summary>
        /// How much the skirt swings, in numbers: one fighter goes through guard, walking on and back, a
        /// side step and the four strikes in the real fight logic; for every skirt chain tip, per part,
        /// the angle between the simulated and the animated tip seen from where the chain hangs (rms and
        /// max) and the largest turn of the simulated tip in one step.
        /// Also, per part: how deep the cloth goes into the legs beyond the stance (bone segments / cross
        /// links, cm, the largest) and how far the cross links stretch or squeeze (%).
        ///   -executeMethod RoeFighter.EditorTools.RoeFightProbe.SkirtSwing [-roeChar g04] [-roeMotions accad_male2] [-roeKind skirt] [-roeCloth legacy]
        /// </summary>
        public static void SkirtSwing()
        {
            SkirtArgs();
            string stage = RoeCapture.Arg("-roeStage", "e23_steel_s02");
            string id = RoeCapture.Arg("-roeChar", "g04");
            string kind = RoeCapture.Arg("-roeKind", "skirt");
            string variant = RoeCapture.Arg("-roeVariant", "");
            FighterRig.ClothBackend = RoeCapture.Arg("-roeCloth", "magica_style");
            variant = (FighterRig.ClothBackend + " " + variant).Trim();
            RoeBoneCloth.NoColliders = variant.Contains("nocoll");
            RoeBoneCloth.NoBackstop = variant.Contains("noback");
            FighterRig.NoHipCloth = variant.Contains("nohip");
            RoeBoneCloth.SkirtTuning = RoeCapture.Arg("-roeSkirt", "");
            if (RoeBoneCloth.SkirtTuning.Length > 0)
                variant += " " + RoeBoneCloth.SkirtTuning;
            // the DOA5LR-style net: "nolong" without its long-range springs, -roeDoa5K the stiffness scale
            RoeBoneCloth.Doa5LongRange = !variant.Contains("nolong");
            RoeBoneCloth.Doa5AngleLimit = !variant.Contains("nolimit");
            RoeBoneCloth.Doa5K = float.Parse(RoeCapture.Arg("-roeDoa5K", "2"), System.Globalization.CultureInfo.InvariantCulture);
            if (RoeBoneCloth.Doa5K != 2f)
                variant += $" K {RoeBoneCloth.Doa5K}";
            EditorSceneManager.OpenScene(RoeFightScene.ScenePath(stage), OpenSceneMode.Single);
            var game = Object.FindFirstObjectByType<FightGame>();
            game.cpu = new[] { false, false };
            string pack = RoeCapture.Arg("-roeMotions", null);
            if (pack != null)
                game.motionPack = Mathf.Max(0, game.motionPacks.FindIndex(p => p.name == pack));
            if (game.roster.Length > 0 && game.roster[game.pick[0]].id != id && game.roster[game.pick[1]].id != id)
                game.PickIds(null, id);
            game.Setup();
            int who = game.f[0].rig.id == id ? 0 : 1;
            var me = game.f[who];
            while (game.phase != FightGame.Phase.Fight)
            {
                game.UpdateCamera(FightGame.Dt);
                game.Step(new FighterInput[2]);
            }
            me.foe.pos = me.pos + (me.foe.pos - me.pos).normalized * 6f;
            me.foe.Place();
            var parts = new (string name, float seconds, FighterInput input)[]
            {
                ("guard", 1.5f, default), ("walk", 3f, new FighterInput { x = 1 }), ("back", 2f, new FighterInput { x = -1 }),
                ("side", 2f, new FighterInput { y = 1 }), ("guard2", 1f, default),
                ("A", 0.9f, new FighterInput { a = true }), ("C", 1.1f, new FighterInput { c = true }),
                ("D", 1.5f, new FighterInput { d = true }), ("B", 1.4f, new FighterInput { b = true }),
            };
            var sb = new System.Text.StringBuilder($"[ROE] {kind} swing {id} ({game.Pack?.name}{(variant.Length > 0 ? " " + variant : "")}), degrees: rms / max away from the animation, max turn in one step");
            var lastDir = new Dictionary<string, Vector3>();
            var lastAnim = new Dictionary<string, Vector3>();
            foreach (var (name, seconds, inp) in parts)
            {
                var sum = new Dictionary<string, float>();
                var max = new Dictionary<string, float>();
                var jump = new Dictionary<string, float>();
                var animJump = new Dictionary<string, float>();
                float boneDepth = 0f, linkDepth = 0f, strain = 0f, baseDepth = 0f;
                int links = 0;
                int steps = Mathf.RoundToInt(seconds * 60f), n = 0;
                for (int s = 0; s < steps; s++)
                {
                    game.UpdateCamera(FightGame.Dt);
                    var input = inp.a || inp.b || inp.c || inp.d ? (s == 0 ? inp : default) : inp;
                    var camRight = game.cam.transform.right;
                    camRight.y = 0f;
                    input.x *= Vector3.Dot(me.foe.pos - me.pos, camRight) >= 0f ? 1 : -1;
                    var inputs = new FighterInput[2];
                    inputs[who] = input;
                    game.Step(inputs);
                    if (me.rig.Cloth == null)
                        break;
                    n++;
                    var measured = me.rig.Cloth.Measures(kind);
                    boneDepth = Mathf.Max(boneDepth, measured.boneDepth);
                    linkDepth = Mathf.Max(linkDepth, measured.linkDepth);
                    strain = Mathf.Max(strain, measured.strain);
                    baseDepth = Mathf.Max(baseDepth, measured.baseDepth);
                    links = measured.links;
                    foreach (var (set, tip, angle, dir, animated) in me.rig.Cloth.TipDeviations(kind))
                    {
                        sum[tip] = (sum.TryGetValue(tip, out var v) ? v : 0f) + angle * angle;
                        max[tip] = Mathf.Max(max.TryGetValue(tip, out var m) ? m : 0f, angle);
                        if (lastDir.TryGetValue(tip, out var last))
                            jump[tip] = Mathf.Max(jump.TryGetValue(tip, out var j) ? j : 0f, Vector3.Angle(last, dir));
                        if (lastAnim.TryGetValue(tip, out var lastA))
                            animJump[tip] = Mathf.Max(animJump.TryGetValue(tip, out var j) ? j : 0f, Vector3.Angle(lastA, animated));
                        lastDir[tip] = dir;
                        lastAnim[tip] = animated;
                    }
                }
                if (n == 0 || sum.Count == 0)
                    continue;
                var rms = sum.ToDictionary(kv => kv.Key, kv => Mathf.Sqrt(kv.Value / n));
                sb.Append($"\n[ROE]   {name,-6}: in the legs {boneDepth * 100f:F1} cm (links {linkDepth * 100f:F1}, base pose {baseDepth * 100f:F1}), links stretch {strain * 100f:F0}% ({links} links); " +
                          $"all tips rms {Mathf.Sqrt(rms.Values.Select(r => r * r).Average()):F1}, max {max.Values.Max():F0}, step {jump.Values.DefaultIfEmpty(0f).Max():F0} " +
                          $"(animation step {animJump.Values.DefaultIfEmpty(0f).Max():F0}: {string.Join(" ", animJump.OrderByDescending(kv => kv.Value).Take(2).Select(kv => $"{kv.Key} {kv.Value:F0}"))}); " +
                          string.Join(", ", rms.OrderByDescending(kv => kv.Value).Take(4).Select(kv => $"{kv.Key} {kv.Value:F0}/{max[kv.Key]:F0}/{(jump.TryGetValue(kv.Key, out var j) ? j : 0f):F0}")));
            }
            Debug.Log(sb.ToString());
        }

        /// <summary>
        /// The skirt rig's two rests side by side (RoeSkirtRig.RestOnStance): per driver, how far the game's
        /// stance is from the motion-capture guard (against the hips' heading); per skirt bone, the angle
        /// between the directions the two rests give it, in the guard, walking on and walking back - the
        /// largest bones with their weights (h heading, p pelvis, lt/rt thighs, lc/rc calves).
        ///   -executeMethod RoeFighter.EditorTools.RoeFightProbe.SkirtRests [-roeMotions bandai1]
        /// </summary>
        public static void SkirtRests()
        {
            string stage = RoeCapture.Arg("-roeStage", "e23_steel_s02");
            EditorSceneManager.OpenScene(RoeFightScene.ScenePath(stage), OpenSceneMode.Single);
            var game = Object.FindFirstObjectByType<FightGame>();
            game.cpu = new[] { false, false };
            string pack = RoeCapture.Arg("-roeMotions", null);
            if (pack != null)
                game.motionPack = Mathf.Max(0, game.motionPacks.FindIndex(p => p.name == pack));
            FighterRig.ClothBackend = "magica_style";
            game.Setup();
            while (game.phase != FightGame.Phase.Fight)
            {
                game.UpdateCamera(FightGame.Dt);
                game.Step(new FighterInput[2]);
            }
            var mid = (game.f[0].pos + game.f[1].pos) * 0.5f;
            for (int i = 0; i < 2; i++)
            {
                game.f[i].pos = mid + (game.f[i].pos - mid).normalized * 3f;
                game.f[i].Place();
            }
            var sb = new System.Text.StringBuilder($"[ROE] skirt rests, game stance vs motion-capture guard ({game.Pack?.name}):");
            for (int i = 0; i < 2; i++)
            {
                var sr = game.f[i].rig.animator.GetComponent<RoeSkirtRig>();
                sb.Append($"\n[ROE]   {game.f[i].rig.id} drivers' stance vs guard: {(sr != null ? sr.RestReport() : "no skirt rig")}");
            }
            string[] shortNames = { "h", "p", "lt", "rt", "lc", "rc" };
            foreach (var (moment, seconds, x) in new (string, float, int)[] { ("guard", 1.0f, 0), ("walk", 1.2f, 1), ("back", 1.2f, -1) })
            {
                for (float t = 0f; t < seconds; t += FightGame.Dt)
                {
                    game.UpdateCamera(FightGame.Dt);
                    var camRight = game.cam.transform.right;
                    camRight.y = 0f;
                    var inputs = new FighterInput[2];
                    for (int i = 0; i < 2; i++)
                        inputs[i] = new FighterInput { x = x * (Vector3.Dot(game.f[i].foe.pos - game.f[i].pos, camRight) >= 0f ? 1 : -1) };
                    game.Step(inputs);
                }
                for (int i = 0; i < 2; i++)
                {
                    var rig = game.f[i].rig;
                    var sr = rig.animator.GetComponent<RoeSkirtRig>();
                    if (sr == null || !sr.Ready)
                        continue;
                    var hips = rig.animator.GetBoneTransform(HumanBodyBones.Hips);
                    int n = sr.joints.Count;
                    Vector3[] tS = new Vector3[n], hS = new Vector3[n], tG = new Vector3[n], hG = new Vector3[n];
                    sr.Predict(hips.position, rig.HipHeading(), true, tS, hS);
                    sr.Predict(hips.position, rig.HipHeading(), false, tG, hG);
                    var angles = Enumerable.Range(0, n).Select(j => (j, a: Vector3.Angle(tS[j] - hS[j], tG[j] - hG[j]), d: (tS[j] - tG[j]).magnitude)).ToList();
                    sb.Append($"\n[ROE]   {rig.id} {moment,-5}: mean {angles.Average(a => a.a):F1} deg, max {angles.Max(a => a.a):F1}; tails apart mean {angles.Average(a => a.d) * 100f:F1} cm; ");
                    sb.Append(string.Join(", ", angles.OrderByDescending(a => a.a).Take(6).Select(a =>
                        $"{sr.joints[a.j].bone.name} {a.a:F0} deg ({string.Join(" ", Enumerable.Range(0, RoeSkirtRig.Drivers).Where(k => sr.joints[a.j].weights[k] >= 0.05f).Select(k => $"{shortNames[k]}{sr.joints[a.j].weights[k]:F2}"))})")));
                }
            }
            Debug.Log(sb.ToString());
        }

        /// <summary>
        /// Does the skirt hang?  Per skirt chain of each fighter, per bone: the angle from straight down
        /// and how far its tail is off the skin (RoeBodySurface, cm, negative inside) - for the game's own
        /// stance (its keys), then in the guard, walking on and walking back, once per RoeSkirtRig.Drape
        /// value (-roeDrapes, default 0,1), the animation pose (cloth weight 0) and the cloth on it.  In
        /// the summary "free" bones are those more than 3 cm off the skin: hanging cloth has them near 0.
        ///   -executeMethod RoeFighter.EditorTools.RoeFightProbe.SkirtHang [-roeMotions bandai1] [-roeDrapes 0,1] [-roeChains]
        /// </summary>
        public static void SkirtHang()
        {
            string stage = RoeCapture.Arg("-roeStage", "e23_steel_s02");
            // -roeDrapeByLevel 0,0.5: how much each chain's top bones drape (RoeSkirtRig.DrapeByLevel)
            string byLevel = RoeCapture.Arg("-roeDrapeByLevel", null);
            if (byLevel != null)
                RoeSkirtRig.DrapeByLevel = byLevel.Split(',').Select(x => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            bool chains = System.Environment.GetCommandLineArgs().Contains("-roeChains");
            string variant = RoeCapture.Arg("-roeVariant", "");
            RoeBoneCloth.NoColliders = variant.Contains("nocoll");
            RoeBoneCloth.NoBackstop = variant.Contains("noback");
            RoeBoneCloth.SkirtTuning = RoeCapture.Arg("-roeSkirt", "");
            RoeSkirtRig.DrapeClearance = float.Parse(RoeCapture.Arg("-roeClearance", "0.012"), System.Globalization.CultureInfo.InvariantCulture);
            EditorSceneManager.OpenScene(RoeFightScene.ScenePath(stage), OpenSceneMode.Single);
            var game = Object.FindFirstObjectByType<FightGame>();
            game.cpu = new[] { false, false };
            string pack = RoeCapture.Arg("-roeMotions", null);
            if (pack != null)
                game.motionPack = Mathf.Max(0, game.motionPacks.FindIndex(p => p.name == pack));
            FighterRig.ClothBackend = "magica_style";
            var sb = new System.Text.StringBuilder($"[ROE] skirt hang ({game.Pack?.name}{(variant.Length > 0 ? " " + variant : "")}{(RoeBoneCloth.SkirtTuning.Length > 0 ? " " + RoeBoneCloth.SkirtTuning : "")}): per moment, bones more than 3 cm off the skin ('free') - mean / max degrees from straight down; bones nearer - mean / least cm off the skin");

            string Measure(FighterRig rig, string label)
            {
                var sr = rig.animator.GetComponent<RoeSkirtRig>();
                var body = sr?.Body;
                if (sr == null || body == null)
                    return $"\n[ROE]   {rig.id} {label}: no skirt rig";
                body.Pose();
                var free = new List<float>();
                var near = new List<float>();
                var lines = new System.Text.StringBuilder();
                int RootOf(int j) { while (sr.joints[j].parent >= 0) j = sr.joints[j].parent; return j; }
                // chain by chain, root to tip (the joints come level by level)
                var order = Enumerable.Range(0, sr.joints.Count).OrderBy(j => RootOf(j)).ThenBy(j => j).ToList();
                foreach (int j in order)
                {
                    var jt = sr.joints[j];
                    if (jt.bone == null)
                        continue;
                    var tail = jt.bone.TransformPoint(jt.tailLocal);
                    float angle = Vector3.Angle(tail - jt.bone.position, Vector3.down);
                    float off = body.Nearest(tail, out var p, out var n) ? Vector3.Dot(tail - p, n) : 1f;
                    if (off > 0.03f)
                        free.Add(angle);
                    else
                        near.Add(off);
                    if (chains)
                    {
                        if (jt.parent < 0)
                            lines.Append($"\n[ROE]       {jt.bone.name}:");
                        lines.Append($" {angle:F0}/{off * 100f:F1}");
                    }
                }
                return $"\n[ROE]   {rig.id} {label,-18}: free {free.Count,2} bones {(free.Count > 0 ? free.Average() : 0f),4:F1} / {(free.Count > 0 ? free.Max() : 0f),3:F0} deg; " +
                       $"near {near.Count,2} bones {(near.Count > 0 ? near.Average() * 100f : 0f),4:F1} / {(near.Count > 0 ? near.Min() * 100f : 0f),5:F1} cm" + lines;
            }

            // the game's own stance, as its keys have it
            FighterRig.ClothBackend = "magica_style";
            RoeSkirtRig.Drape = 0f;
            game.Setup();
            foreach (var f in game.f)
            {
                f.rig.stance.SampleAnimation(f.rig.animator.gameObject, 0f);
                sb.Append(Measure(f.rig, "game stance"));
            }
            foreach (var drapeText in RoeCapture.Arg("-roeDrapes", "0,1").Split(','))
            {
                RoeSkirtRig.Drape = float.Parse(drapeText, System.Globalization.CultureInfo.InvariantCulture);
                game.Setup();
                while (game.phase != FightGame.Phase.Fight)
                {
                    game.UpdateCamera(FightGame.Dt);
                    game.Step(new FighterInput[2]);
                }
                var mid = (game.f[0].pos + game.f[1].pos) * 0.5f;
                for (int i = 0; i < 2; i++)
                {
                    game.f[i].pos = mid + (game.f[i].pos - mid).normalized * 3f;
                    game.f[i].Place();
                }
                foreach (var (moment, seconds, x) in new (string, float, int)[] { ("guard", 1.0f, 0), ("walk", 1.2f, 1), ("back", 1.2f, -1) })
                {
                    for (float t = 0f; t < seconds; t += FightGame.Dt)
                    {
                        game.UpdateCamera(FightGame.Dt);
                        var camRight = game.cam.transform.right;
                        camRight.y = 0f;
                        var inputs = new FighterInput[2];
                        for (int i = 0; i < 2; i++)
                            inputs[i] = new FighterInput { x = x * (Vector3.Dot(game.f[i].foe.pos - game.f[i].pos, camRight) >= 0f ? 1 : -1) };
                        game.Step(inputs);
                    }
                    foreach (var f in game.f)
                    {
                        sb.Append(Measure(f.rig, $"drape {drapeText} {moment} cloth"));
                        var sr = f.rig.animator.GetComponent<RoeSkirtRig>();
                        var hips = f.rig.animator.GetBoneTransform(HumanBodyBones.Hips);
                        sr.Apply(hips.position, f.rig.HipHeading(), 1f);       // the animation pose alone
                        sb.Append(Measure(f.rig, $"drape {drapeText} {moment} pose"));
                    }
                }
            }
            RoeSkirtRig.Drape = 1f;
            Debug.Log(sb.ToString());
        }

        /// <summary>
        /// Does the skirt's animation pose jump?  One fighter plays the basic moves with the cloth weight at
        /// 0 (the bones show RoeSkirtRig's pose alone); per skirt bone the largest turn between two steps,
        /// the worst bones with the move and time.
        ///   -executeMethod RoeFighter.EditorTools.RoeFightProbe.SkirtJumps [-roeChar g04] [-roeMotions bandai1] [-roeDrape 1]
        /// </summary>
        public static void SkirtJumps()
        {
            SkirtArgs();
            string stage = RoeCapture.Arg("-roeStage", "e23_steel_s02");
            string id = RoeCapture.Arg("-roeChar", "g04");
            EditorSceneManager.OpenScene(RoeFightScene.ScenePath(stage), OpenSceneMode.Single);
            var game = Object.FindFirstObjectByType<FightGame>();
            game.cpu = new[] { false, false };
            string pack = RoeCapture.Arg("-roeMotions", null);
            if (pack != null)
                game.motionPack = Mathf.Max(0, game.motionPacks.FindIndex(p => p.name == pack));
            FighterRig.ClothBackend = "magica_style";
            game.Setup();
            int who = game.f[0].rig.id == id ? 0 : 1;
            var me = game.f[who];
            me.rig.clothWeight = 0f;
            while (game.phase != FightGame.Phase.Fight)
            {
                game.UpdateCamera(FightGame.Dt);
                game.Step(new FighterInput[2]);
            }
            me.foe.pos = me.pos + (me.foe.pos - me.pos).normalized * 6f;
            me.foe.Place();
            var sr = me.rig.animator.GetComponent<RoeSkirtRig>();
            var last = new Vector3[sr.joints.Count];
            var worst = new List<(float angle, string bone, string move, float t)>();
            float time = 0f;
            foreach (var (name, seconds, inp) in new (string, float, FighterInput)[]
            {
                ("guard", 1.5f, default), ("walk", 2f, new FighterInput { x = 1 }), ("back", 2f, new FighterInput { x = -1 }),
                ("side", 2f, new FighterInput { y = 1 }), ("A", 0.9f, new FighterInput { a = true }), ("C", 1.1f, new FighterInput { c = true }),
                ("D", 1.5f, new FighterInput { d = true }), ("B", 1.4f, new FighterInput { b = true }),
            })
            {
                int steps = Mathf.RoundToInt(seconds * 60f);
                for (int s = 0; s < steps; s++)
                {
                    game.UpdateCamera(FightGame.Dt);
                    var input = inp.a || inp.b || inp.c || inp.d ? (s == 0 ? inp : default) : inp;
                    var camRight = game.cam.transform.right;
                    camRight.y = 0f;
                    input.x *= Vector3.Dot(me.foe.pos - me.pos, camRight) >= 0f ? 1 : -1;
                    var inputs = new FighterInput[2];
                    inputs[who] = input;
                    game.Step(inputs);
                    time += FightGame.Dt;
                    for (int j = 0; j < sr.joints.Count; j++)
                    {
                        var jt = sr.joints[j];
                        var dir = (jt.bone.TransformPoint(jt.tailLocal) - jt.bone.position).normalized;
                        if (last[j] != Vector3.zero && s > 0)
                            worst.Add((Vector3.Angle(last[j], dir), jt.bone.name, name, time));
                        last[j] = dir;
                    }
                }
            }
            var sb = new System.Text.StringBuilder($"[ROE] skirt pose jumps {id} ({game.Pack?.name}, drape {RoeSkirtRig.Drape}): largest turns of a bone between two steps");
            // what the skirt rig costs a step (the skin posed, every bone hung)
            var hipsBone = me.rig.animator.GetBoneTransform(HumanBodyBones.Hips);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            for (int k = 0; k < 300; k++)
                sr.Apply(hipsBone.position, me.rig.HipHeading(), 1f);
            sb.Append($"\n[ROE]   cost: {watch.Elapsed.TotalMilliseconds / 300.0:F3} ms a step, {sr.joints.Count} bones, {(sr.Body != null ? sr.Body.Count : 0)} skin points");
            foreach (var g in worst.GroupBy(w => w.move))
                sb.Append($"\n[ROE]   {g.Key,-6}: " + string.Join(", ", g.OrderByDescending(w => w.angle).Take(4).Select(w => $"{w.bone} {w.angle:F0} deg at {w.t:F2} s")));
            Debug.Log(sb.ToString());
        }

        /// <summary>
        /// Does the skirt stand off the buttocks?  On the baked skin, in the hips' heading frame, at heights
        /// below the hip joints: the gap between the rearmost point of the body and the inner face of the
        /// back panel (|x| under 7 cm), and between the body's left side and the left sash - positive = air
        /// between them, negative = the cloth inside the body.  Conditions: the game's own battle stance
        /// (idle_01, its keys), then the motion-capture guard and a few steps for each cloth backend.
        /// Side and back views (left, back, back-left, right, back-right) to _work/butt/&lt;condition&gt;_&lt;moment&gt;_&lt;view&gt;.jpg.
        ///   -executeMethod RoeFighter.EditorTools.RoeFightProbe.ButtGap [-roeChar g04] [-roeMotions bandai1] [-roeCloths off,legacy,magica_style] [-roeSkirtRest stance|guard] [-roeOut dir]
        /// </summary>
        public static void ButtGap()
        {
            SkirtArgs();
            string stage = RoeCapture.Arg("-roeStage", "e23_steel_s02");
            string id = RoeCapture.Arg("-roeChar", "g04");
            string outDir = RoeCapture.Arg("-roeOut", System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "_work", "butt"));
            if (System.IO.Directory.Exists(outDir))
                System.IO.Directory.Delete(outDir, true);
            System.IO.Directory.CreateDirectory(outDir);
            ShaderUtil.allowAsyncCompilation = false;
            EditorSceneManager.OpenScene(RoeFightScene.ScenePath(stage), OpenSceneMode.Single);
            var game = Object.FindFirstObjectByType<FightGame>();
            game.cpu = new[] { false, false };
            game.hud.gameObject.SetActive(false);
            string pack = RoeCapture.Arg("-roeMotions", null);
            if (pack != null)
                game.motionPack = Mathf.Max(0, game.motionPacks.FindIndex(p => p.name == pack));
            foreach (var rig in game.rigs)
                foreach (var smr in rig.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    smr.forceMatrixRecalculationPerRender = true;
            var sb = new System.Text.StringBuilder($"[ROE] skirt against the buttocks, {id} ({game.Pack?.name}), cm at 5/10/15/20/25 cm below the hip joints - back panel gap | left sash gap:");
            float lookY = float.Parse(RoeCapture.Arg("-roeLookY", "-0.15"), System.Globalization.CultureInfo.InvariantCulture);       // camera aim against the hips (m)
            float clothWeight = float.Parse(RoeCapture.Arg("-roeClothWeight", "1"), System.Globalization.CultureInfo.InvariantCulture); // 0: the animation pose alone
            bool warmed = false;
            Fighter me = null;

            string Measure(FighterRig rig)
            {
                var a = rig.animator;
                var l = a.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
                var r = a.GetBoneTransform(HumanBodyBones.RightUpperLeg);
                var fwd = Vector3.Cross(r.position - l.position, Vector3.up);
                fwd.y = 0f;
                var frame = Quaternion.LookRotation(fwd.normalized, Vector3.up);
                var origin = (l.position + r.position) * 0.5f;
                var inv = Quaternion.Inverse(frame);
                var body = new List<Vector3>();
                var back = new List<Vector3>();
                var sash = new List<Vector3>();
                var mesh = new Mesh();
                // the skirt's vertices against the skin (RoeBodySurface, any facing): how many are inside
                var skin = a.GetComponent<RoeSkirtRig>()?.Body;
                skin?.Pose();
                int skirtVerts = 0, skirtIn = 0, backVerts = 0, backIn = 0;
                float deepest = 0f, backDeepest = 0f;
                var perBone = new Dictionary<string, (int n, int inside, float deepest)>();
                foreach (var smr in a.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    if (smr.sharedMesh == null || smr.sharedMaterials.Any(m => m != null && m.name.StartsWith("wp_")))
                        continue;
                    smr.BakeMesh(mesh, true);
                    var v = mesh.vertices;
                    var w = smr.sharedMesh.boneWeights;
                    var m4 = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
                    for (int i = 0; i < v.Length && i < w.Length; i++)
                    {
                        var bone = w[i].boneIndex0 < smr.bones.Length ? smr.bones[w[i].boneIndex0] : null;
                        if (bone == null || w[i].weight0 < 0.4f)
                            continue;
                        var world = m4.MultiplyPoint3x4(v[i]);
                        if (skin != null && bone.name.StartsWith("Skirt_") && !bone.name.EndsWith("_00"))
                        {
                            bool isBack = bone.name.StartsWith("Skirt_Back") || bone.name.StartsWith("Skirt_M_01");
                            skirtVerts++;
                            backVerts += isBack ? 1 : 0;
                            var tally = perBone.TryGetValue(bone.name, out var t0) ? t0 : (n: 0, inside: 0, deepest: 0f);
                            tally.n++;
                            if (skin.Signed(world, Vector3.zero, -2f, out float d, out _) && d < -0.003f)
                            {
                                tally.inside++;
                                tally.deepest = Mathf.Min(tally.deepest, d);
                                skirtIn++;
                                deepest = Mathf.Min(deepest, d);
                                if (isBack)
                                {
                                    backIn++;
                                    backDeepest = Mathf.Min(backDeepest, d);
                                }
                            }
                            perBone[bone.name] = tally;
                        }
                        var p = inv * (world - origin);
                        if (bone.name.StartsWith("Skirt_Back"))
                            back.Add(p);
                        else if (bone.name.StartsWith("Skirt_Left"))
                            sash.Add(p);
                        else if (!bone.name.Contains("Skirt") && !bone.name.ToLowerInvariant().Contains("hair"))
                            body.Add(p);
                    }
                }
                Object.DestroyImmediate(mesh);
                var parts = new List<string>();
                foreach (float below in new[] { 0.05f, 0.10f, 0.15f, 0.20f, 0.25f })
                {
                    float y = -below;
                    bool Slice(Vector3 p) => Mathf.Abs(p.y - y) < 0.012f;
                    var bodyBack = body.Where(p => Slice(p) && Mathf.Abs(p.x) < 0.07f).Select(p => p.z).DefaultIfEmpty(float.NaN).Min();
                    var panelFront = back.Where(p => Slice(p) && Mathf.Abs(p.x) < 0.07f).Select(p => p.z).DefaultIfEmpty(float.NaN).Max();
                    var bodyLeft = body.Where(p => Slice(p) && Mathf.Abs(p.z) < 0.08f).Select(p => p.x).DefaultIfEmpty(float.NaN).Min();
                    var sashInner = sash.Where(p => Slice(p) && Mathf.Abs(p.z) < 0.08f).Select(p => p.x).DefaultIfEmpty(float.NaN).Max();
                    parts.Add($"{(bodyBack - panelFront) * 100f:F1}|{(bodyLeft - sashInner) * 100f:F1}");
                }
                var inside = skin == null ? "" : $";  skirt vertices in the skin {100f * skirtIn / Mathf.Max(1, skirtVerts):F1}% (deepest {deepest * 100f:F1} cm), " +
                    $"back panel {100f * backIn / Mathf.Max(1, backVerts):F1}% ({backDeepest * 100f:F1} cm); most in: " +
                    string.Join(", ", perBone.Where(kv => kv.Value.inside > 0).OrderByDescending(kv => kv.Value.inside).Take(4)
                                             .Select(kv => $"{kv.Key} {kv.Value.inside}/{kv.Value.n} ({kv.Value.deepest * 100f:F1})"));
                return string.Join("  ", parts) + inside;
            }

            void Shoot(string name)
            {
                var a = me.rig.animator;
                var hips = a.GetBoneTransform(HumanBodyBones.Hips).position;
                var fwd = me.Forward;
                var right = Vector3.Cross(Vector3.up, fwd).normalized;
                var look = new Vector3(hips.x, hips.y + lookY, hips.z);
                var cam = game.cam;
                cam.fieldOfView = 26f;
                foreach (var (view, dir) in new[] { ("left", -right), ("back", -fwd), ("backleft", (-fwd - right).normalized), ("right", right), ("backright", (-fwd + right).normalized) })
                {
                    cam.transform.position = look + dir * 1.9f + Vector3.up * 0.05f;
                    cam.transform.LookAt(look, Vector3.up);
                    if (!warmed)
                    {
                        RoeCapture.Render(cam, 320, 180, System.IO.Path.Combine(outDir, "_warm.jpg"), 80);
                        RoeCapture.Render(cam, 320, 180, System.IO.Path.Combine(outDir, "_warm.jpg"), 80);
                        warmed = true;
                    }
                    RoeCapture.Render(cam, 420, 480, System.IO.Path.Combine(outDir, $"{name}_{view}.jpg"), 90);
                }
            }

            // the game's own battle stance: the stance clip sampled on the model (its skirt keys, its pelvis)
            FighterRig.ClothBackend = "off";
            game.Setup();
            int who = game.f[0].rig.id == id ? 0 : 1;
            me = game.f[who];
            me.foe.pos = me.pos + (me.foe.pos - me.pos).normalized * 6f;
            me.foe.Place();
            game.UpdateCamera(FightGame.Dt);
            foreach (float t in new[] { 0f, 0.5f })
            {
                me.rig.stance.SampleAnimation(me.rig.animator.gameObject, t);
                sb.Append($"\n[ROE]   game stance idle_01 t={t:F1}: {Measure(me.rig)}");
                if (t == 0f)
                    Shoot("game_stance");
            }
            foreach (var cloth in RoeCapture.Arg("-roeCloths", "off,legacy,magica_style").Split(','))
            {
                FighterRig.ClothBackend = cloth;
                game.Setup();
                me = game.f[who];
                me.rig.clothWeight = clothWeight;
                while (game.phase != FightGame.Phase.Fight)
                {
                    game.UpdateCamera(FightGame.Dt);
                    game.Step(new FighterInput[2]);
                }
                me.foe.pos = me.pos + (me.foe.pos - me.pos).normalized * 6f;
                me.foe.Place();
                foreach (var (moment, seconds, inp) in new (string, float, FighterInput)[] { ("guard", 1.0f, default), ("walk", 1.2f, new FighterInput { x = 1 }), ("back", 1.2f, new FighterInput { x = -1 }) })
                {
                    for (float t = 0f; t < seconds; t += FightGame.Dt)
                    {
                        game.UpdateCamera(FightGame.Dt);
                        var input = inp;
                        var camRight = game.cam.transform.right;
                        camRight.y = 0f;
                        input.x *= Vector3.Dot(me.foe.pos - me.pos, camRight) >= 0f ? 1 : -1;
                        var inputs = new FighterInput[2];
                        inputs[who] = input;
                        game.Step(inputs);
                    }
                    sb.Append($"\n[ROE]   {cloth,-12} {moment,-5}: {Measure(me.rig)}");
                    if (System.Environment.GetCommandLineArgs().Contains("-roeHangReport"))
                        sb.Append($"\n[ROE]     hang: {me.rig.animator.GetComponent<RoeSkirtRig>()?.HangReport(RoeCapture.Arg("-roeHangReport", "Skirt_Back"))}");
                    Shoot($"{cloth}_{moment}");
                }
            }
            Debug.Log(sb.ToString());
        }

        /// <summary>
        /// How deep the skirt's animated pose goes into the legs (the leg capsules, beyond the battle
        /// stance; RoeBoneCloth.Measures), clip by clip on the rig itself: the game's own clips (the
        /// animators' keys - the yardstick), then the basic moves with the skirt pinned to the pelvis
        /// (legacy) and with the skirt that follows the legs (magica_style).  Max and mean over the clip.
        ///   -executeMethod RoeFighter.EditorTools.RoeFightProbe.SkirtDepth [-roeChars g04,a08] [-roeMotions bandai1]
        /// </summary>
        public static void SkirtDepth()
        {
            SkirtArgs();
            string stage = RoeCapture.Arg("-roeStage", "e23_steel_s02");
            EditorSceneManager.OpenScene(RoeFightScene.ScenePath(stage), OpenSceneMode.Single);
            var game = Object.FindFirstObjectByType<FightGame>();
            string pack = RoeCapture.Arg("-roeMotions", null);
            if (pack != null)
                game.motionPack = Mathf.Max(0, game.motionPacks.FindIndex(p => p.name == pack));
            var sb = new System.Text.StringBuilder($"[ROE] skirt base pose in the legs, cm max / mean ({game.Pack?.name}):");
            foreach (var id in RoeCapture.Arg("-roeChars", "g04,a08").Split(','))
            {
                var rig = game.rigs.FirstOrDefault(r => r.id == id);
                if (rig == null)
                    continue;
                foreach (var backend in new[] { "legacy", "magica_style" })
                {
                    FighterRig.ClothBackend = backend;
                    rig.useCloth = true;
                    rig.UseMotions(game.Pack);
                    rig.Init();
                    var clipNames = rig.clips.Select(c => c.name).ToList();
                    if (backend == "magica_style" && rig.Cloth != null)
                        sb.Append($"\n[ROE]   {id} pieces (what RoeMagicaCloth would build): " + string.Join("; ", rig.Cloth.Pieces().Select(p =>
                            $"{p.kind} {string.Join("+", p.roots.Select(r => r.name))}{(p.mesh ? " (mesh)" : "")}")));
                    sb.Append($"\n[ROE]   {id} {backend}:");
                    foreach (var name in clipNames)
                    {
                        var info = rig.clips.First(c => c.name == name);
                        if (info.game && backend != "legacy")
                            continue;               // the game's keys are the same under either backend
                        if (info.game && !new[] { "idle_01", "skill_01", "skill_02", "skill_03", "hurt", "react_01" }.Contains(name))
                            continue;
                        rig.Play(name, 1f, 0f);
                        rig.ResetCloth();
                        float len = Mathf.Min(rig.Length(name), 6f), max = 0f, sum = 0f;
                        int n = 0;
                        for (float t = 0f; t < len; t += 1f / 60f)
                        {
                            rig.Tick(t == 0f ? 0f : 1f / 60f);
                            var m = rig.Cloth?.Measures("skirt");
                            if (m == null)
                                break;
                            max = Mathf.Max(max, m.Value.baseDepth);
                            sum += m.Value.baseDepth;
                            n++;
                        }
                        sb.Append($" {(info.game ? "game " : "")}{name} {max * 100f:F1}/{(n > 0 ? sum / n * 100f : 0f):F1}");
                    }
                }
            }
            Debug.Log(sb.ToString());
        }

        /// <summary>
        /// The skirt in the real fight logic: one fighter's hips and legs filmed from the front, her left
        /// and behind at moments of the game's own pose (the intro) and the basic moves of each motion
        /// pack, once per cloth backend (default: none, the 10-02 one, Magica-style).  Frames to
        /// _work/skirt/&lt;pack&gt;_&lt;cloth&gt;_&lt;moment&gt;_&lt;view&gt;.jpg; the renderers of both fighters (materials, root
        /// bone, weapon or not) go to the log.
        ///   -executeMethod RoeFighter.EditorTools.RoeFightProbe.Skirt [-roeChar a08] [-roePacks bandai1,accad_male2] [-roeCloths off,legacy,magica_style]
        /// </summary>
        public static void Skirt()
        {
            SkirtArgs();
            string stage = RoeCapture.Arg("-roeStage", "e23_steel_s02");
            string id = RoeCapture.Arg("-roeChar", "a08");
            string outDir = RoeCapture.Arg("-roeOut", System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "_work", "skirt"));
            var packs = RoeCapture.Arg("-roePacks", "bandai1,accad_male2").Split(',');
            var cloths = RoeCapture.Arg("-roeCloths", "off,legacy,magica_style").Split(',');
            if (System.IO.Directory.Exists(outDir))
                System.IO.Directory.Delete(outDir, true);
            System.IO.Directory.CreateDirectory(outDir);
            ShaderUtil.allowAsyncCompilation = false;
            EditorSceneManager.OpenScene(RoeFightScene.ScenePath(stage), OpenSceneMode.Single);
            var game = Object.FindFirstObjectByType<FightGame>();
            game.cpu = new[] { false, false };
            game.hud.gameObject.SetActive(false);
            var sb = new System.Text.StringBuilder("[ROE] renderers:");
            foreach (var rig in game.rigs)
            {
                var weapons = new HashSet<Renderer>(rig.weaponRenderers ?? new Renderer[0]);
                foreach (var r in rig.animator.GetComponentsInChildren<Renderer>(true))
                {
                    var smr = r as SkinnedMeshRenderer;
                    sb.Append($"\n[ROE]   {rig.id} {r.name}: {(smr != null && smr.sharedMesh != null ? smr.sharedMesh.vertexCount : 0)} verts, root {(smr != null && smr.rootBone != null ? smr.rootBone.name : "-")}, " +
                              $"materials {string.Join(" ", r.sharedMaterials.Select(m => m != null ? m.name : "null"))}{(weapons.Contains(r) ? " [weapon]" : "")}");
                }
                foreach (var smr in rig.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    smr.forceMatrixRecalculationPerRender = true;
            }
            Debug.Log(sb.ToString());
            var moments = new (float at, FighterInput input, string name)[]
            {
                (0.6f, default, "guard"),
                (1.6f, new FighterInput { x = 1 }, "walk"),
                (2.8f, new FighterInput { x = -1 }, "back"),
                (4.0f, new FighterInput { y = 1 }, "side"),
                (5.0f, default, "guard2"),
                (5.35f, new FighterInput { d = true }, "kick"),
                (6.6f, default, "guard3"),
                (6.85f, new FighterInput { c = true }, "cross"),
            };
            bool warmed = false;
            foreach (var packName in packs)
                foreach (var cloth in cloths)
                {
                    game.motionPack = Mathf.Max(0, game.motionPacks.FindIndex(p => p.name == packName));
                    FighterRig.ClothBackend = cloth;
                    foreach (var rig in game.rigs)
                        rig.useCloth = true;
                    game.Setup();
                    int who = game.f[0].rig.id == id ? 0 : 1;
                    var me = game.f[who];
                    void Shoot(string moment)
                    {
                        var fwd = me.Forward;
                        var right = Vector3.Cross(Vector3.up, fwd).normalized;
                        var look = me.pos + Vector3.up * 0.62f;
                        var cam = game.cam;
                        cam.fieldOfView = 34f;
                        foreach (var (view, dir) in new[] { ("front", fwd), ("left", -right), ("back", -fwd) })
                        {
                            cam.transform.position = look + dir * 2.4f + Vector3.up * 0.15f;
                            cam.transform.LookAt(look, Vector3.up);
                            if (!warmed)
                            {
                                RoeCapture.Render(cam, 320, 180, System.IO.Path.Combine(outDir, "_warm.jpg"), 80);
                                RoeCapture.Render(cam, 320, 180, System.IO.Path.Combine(outDir, "_warm.jpg"), 80);
                                warmed = true;
                            }
                            RoeCapture.Render(cam, 420, 480, System.IO.Path.Combine(outDir, $"{packName}_{cloth}_{moment}_{view}.jpg"), 90);
                        }
                    }
                    for (int s = 0; game.phase != FightGame.Phase.Fight; s++)
                    {
                        game.UpdateCamera(FightGame.Dt);
                        game.Step(new FighterInput[2]);
                        if (s == 45)
                            Shoot("intro");
                    }
                    me.foe.pos = me.pos + (me.foe.pos - me.pos).normalized * 6f;
                    me.foe.Place();
                    float t = 0f;
                    foreach (var (at, inp, name) in moments)
                    {
                        bool press = inp.a || inp.b || inp.c || inp.d, first = true;
                        while (t < at)
                        {
                            game.UpdateCamera(FightGame.Dt);
                            var input = press && !first ? default : inp;     // a button is pressed once
                            first = false;
                            var camRight = game.cam.transform.right;
                            camRight.y = 0f;
                            input.x *= Vector3.Dot(me.foe.pos - me.pos, camRight) >= 0f ? 1 : -1;
                            var inputs = new FighterInput[2];
                            inputs[who] = input;
                            game.Step(inputs);
                            t += FightGame.Dt;
                        }
                        if (!name.StartsWith("guard") || name == "guard")
                            Shoot(name);
                    }
                    Debug.Log($"[ROE] skirt {id} {packName} cloth {cloth}: {me.rig.ClothTitle}; {me.rig.ClothReport}");
                }
        }

        /// <summary>
        /// The feet up close, in the real fight logic: one fighter's feet filmed from her side at floor
        /// level and measured - per foot the pitch of the foot (ankle to toes against the floor), the
        /// ankle and toe bone heights, and from the skinned shoe the lowest point under the heel half and
        /// under the toe half (both 0 when standing flat on the floor in heels; below 0 = in the floor).
        /// First the game's own pose (the intro), then guard, walking on and back, side steps and the four
        /// strikes of each motion pack.  Frames to _work/feet/&lt;pack&gt;_&lt;n&gt;.jpg, numbers to the log and feet.txt.
        /// Per heel also the lowest heel skinned to its strongest bone alone (heel-rigid: without the share of the
        /// helpers), the shin-to-foot angle and the bones of the lowest heel vertex.
        ///   -executeMethod RoeFighter.EditorTools.RoeFightProbe.Feet [-roeChar a08] [-roePacks bandai1,accad_male2] [-roeSides both]
        /// </summary>
        public static void Feet()
        {
            string stage = RoeCapture.Arg("-roeStage", "e23_steel_s02");
            string id = RoeCapture.Arg("-roeChar", "a08");
            string outDir = RoeCapture.Arg("-roeOut", System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "_work", "feet"));
            var packs = RoeCapture.Arg("-roePacks", "bandai1,accad_male2").Split(',');
            if (System.IO.Directory.Exists(outDir))
                System.IO.Directory.Delete(outDir, true);
            System.IO.Directory.CreateDirectory(outDir);
            ShaderUtil.allowAsyncCompilation = false;
            EditorSceneManager.OpenScene(RoeFightScene.ScenePath(stage), OpenSceneMode.Single);
            var game = Object.FindFirstObjectByType<FightGame>();
            game.cpu = new[] { false, false };
            game.hud.gameObject.SetActive(false);
            if (game.rigs.All(r => r.id != id))
                game.PickIds(null, id);      // she takes 2P's place
            foreach (var rig in game.roster.Length > 0 ? game.roster : game.rigs)
                foreach (var smr in rig.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    smr.forceMatrixRecalculationPerRender = true;
            var table = new System.Text.StringBuilder();
            var baked = new Mesh();
            bool warmed = false;
            // (from, to, input, name) in seconds of the fight phase, shots every `every` seconds in between
            var script = new (float from, float to, FighterInput input, string name, float every)[]
            {
                (0.0f, 1.0f, default, "guard", 0.25f),
                (1.0f, 2.6f, new FighterInput { x = 1 }, "walk", 0.2f),
                (2.6f, 3.8f, new FighterInput { x = -1 }, "back", 0.2f),
                (3.8f, 5.3f, new FighterInput { y = 1 }, "side", 0.25f),
                (5.6f, 6.6f, new FighterInput { a = true }, "A", 0.1f),
                (6.6f, 7.8f, new FighterInput { c = true }, "C", 0.12f),
                (7.8f, 9.4f, new FighterInput { d = true }, "D", 0.15f),
                (9.4f, 10.8f, new FighterInput { b = true }, "B", 0.15f),
            };
            foreach (var packName in packs)
            {
                game.motionPack = Mathf.Max(0, game.motionPacks.FindIndex(p => p.name == packName));
                game.Setup();
                int who = game.f[0].rig.id == id ? 0 : 1;
                var me = game.f[who];
                var a = me.rig.animator;
                var feet = new[] { a.GetBoneTransform(HumanBodyBones.LeftFoot), a.GetBoneTransform(HumanBodyBones.RightFoot) };
                var toes = new[] { a.GetBoneTransform(HumanBodyBones.LeftToes), a.GetBoneTransform(HumanBodyBones.RightToes) };
                // the shoe and foot vertices of each side: their strongest bone is the foot or under it
                var renderers = a.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r => r.sharedMesh != null && r.enabled && r.gameObject.activeInHierarchy).ToArray();
                var sides = renderers.Select(r =>
                {
                    var bw = r.sharedMesh.boneWeights;
                    var bones = r.bones;
                    var lists = new[] { new List<int>(), new List<int>() };
                    for (int i = 0; i < bw.Length; i++)
                    {
                        int b = bw[i].boneIndex0;
                        if (b >= bones.Length || bones[b] == null)
                            continue;
                        for (int k = 0; k < 2; k++)
                            if (bones[b].IsChildOf(feet[k]))
                                lists[k].Add(i);
                    }
                    return lists;
                }).ToArray();
                // the same vertices skinned to their strongest bone alone (how the heel would sit without the helpers' share)
                var rigid = renderers.Select(r => (verts: r.sharedMesh.vertices, bind: r.sharedMesh.bindposes, bw: r.sharedMesh.boneWeights)).ToArray();
                var knees = new[] { a.GetBoneTransform(HumanBodyBones.LeftLowerLeg), a.GetBoneTransform(HumanBodyBones.RightLowerLeg) };
                bool bothSides = RoeCapture.Arg("-roeSides", "left") == "both";
                // the foot against the shin, in the shin's frame: at the intro (the game's own pose) and now
                var introToe = new Vector3[2];
                var introUp = new Vector3[2];
                var introRel = new Quaternion[2];       // the calf against the thigh at the intro
                int shot = 0;
                void Shoot(string what)
                {
                    float floor = a.transform.position.y;
                    var fwd = me.Forward;
                    var line = new System.Text.StringBuilder($"{packName}\t{shot}\t{what}\t{me.state}\t{me.rig.Current}");
                    var low = new[] { new Vector2(float.MaxValue, float.MaxValue), new Vector2(float.MaxValue, float.MaxValue) };   // (heel half, toe half)
                    var heelAt = new[] { (-1, -1), (-1, -1) };      // the lowest heel vertex: renderer, vertex
                    var rigidHeel = new[] { float.MaxValue, float.MaxValue };
                    var rigidAt = new[] { "", "" };
                    for (int ri = 0; ri < renderers.Length; ri++)
                    {
                        if (sides[ri][0].Count == 0 && sides[ri][1].Count == 0)
                            continue;
                        var r = renderers[ri];
                        r.BakeMesh(baked, true);
                        var v = baked.vertices;
                        var m = Matrix4x4.TRS(r.transform.position, r.transform.rotation, Vector3.one);
                        for (int k = 0; k < 2; k++)
                        {
                            var footFwd = toes[k].position - feet[k].position;
                            footFwd.y = 0f;
                            footFwd = footFwd.sqrMagnitude > 1e-6f ? footFwd.normalized : fwd;
                            foreach (int i in sides[ri][k])
                            {
                                var p = m.MultiplyPoint3x4(v[i]);
                                float along = Vector3.Dot(p - feet[k].position, footFwd);
                                if (along < 0f)
                                {
                                    if (p.y - floor < low[k].x)
                                        heelAt[k] = (ri, i);
                                    low[k].x = Mathf.Min(low[k].x, p.y - floor);
                                    int b = rigid[ri].bw[i].boneIndex0;
                                    var q = r.bones[b].localToWorldMatrix.MultiplyPoint3x4(rigid[ri].bind[b].MultiplyPoint3x4(rigid[ri].verts[i]));
                                    if (q.y - floor < rigidHeel[k])
                                        rigidAt[k] = $"{r.name}#{i}:{r.bones[b].name}";
                                    rigidHeel[k] = Mathf.Min(rigidHeel[k], q.y - floor);
                                }
                                else
                                    low[k].y = Mathf.Min(low[k].y, p.y - floor);
                            }
                        }
                    }
                    for (int k = 0; k < 2; k++)
                    {
                        var d = toes[k].position - feet[k].position;
                        float pitch = Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
                        line.Append($"\t{(k == 0 ? "L" : "R")} pitch {pitch:F0} ankle {feet[k].position.y - floor:F3} toe {toes[k].position.y - floor:F3} " +
                                    $"heel-half low {low[k].x:F3} toe-half low {low[k].y:F3}");
                        // the heel skinned to its strongest bone alone, and the angle between shin and foot (knee-ankle-toe)
                        line.Append($" heel-rigid {rigidHeel[k]:F3} ({rigidAt[k]}) ankle-angle {Vector3.Angle(knees[k].position - feet[k].position, toes[k].position - feet[k].position):F0}");
                        // one heel vertex followed through the shots (-roeTrack nude_body#6537,nude_body#9396 for L,R)
                        var track = RoeCapture.Arg("-roeTrack", "").Split(',');
                        if (track.Length == 2 && track[k].Contains("#"))
                        {
                            var parts = track[k].Split('#');
                            int ri = System.Array.FindIndex(renderers, x => x.name.Replace(' ', '_') == parts[0]);     // spaces as _ on the command line
                            int vi = int.Parse(parts[1]);
                            if (ri >= 0)
                            {
                                var r = renderers[ri];
                                r.BakeMesh(baked, true);
                                var pb = Matrix4x4.TRS(r.transform.position, r.transform.rotation, Vector3.one).MultiplyPoint3x4(baked.vertices[vi]);
                                int b = rigid[ri].bw[vi].boneIndex0;
                                var pr = r.bones[b].localToWorldMatrix.MultiplyPoint3x4(rigid[ri].bind[b].MultiplyPoint3x4(rigid[ri].verts[vi]));
                                var fl = feet[k].InverseTransformPoint(pr);
                                line.Append($" track#{vi} baked y {pb.y - floor:F3} along {Vector3.Dot(pb - feet[k].position, Vector3.ProjectOnPlane(toes[k].position - feet[k].position, Vector3.up).normalized):F3}" +
                                            $" rigid y {pr.y - floor:F3} foot-local ({fl.x:F3},{fl.y:F3},{fl.z:F3}) baked-rigid {(pb - pr).magnitude:F3}");
                            }
                        }
                        // the foot turned against the shin since the intro (the game's own pose): about the shin (twist), towards it
                        // (flex), and the sole tipped sideways about the foot's length (roll)
                        {
                            var shin = knees[k];
                            var axis = shin.InverseTransformDirection(feet[k].position - shin.position).normalized;
                            var toeDir = shin.InverseTransformDirection(toes[k].position - feet[k].position).normalized;
                            if (shot == 0)
                            {
                                introToe[k] = toeDir;
                                introUp[k] = feet[k].InverseTransformDirection(Vector3.up);     // the sole's up, in the foot's frame
                            }
                            float twist = Vector3.SignedAngle(Vector3.ProjectOnPlane(introToe[k], axis), Vector3.ProjectOnPlane(toeDir, axis), axis);
                            float flex = Vector3.Angle(toeDir, axis) - Vector3.Angle(introToe[k], axis);
                            // roll: the sole's up against the plane of shin and foot, now and at the intro
                            var up = shin.InverseTransformDirection(feet[k].TransformDirection(introUp[k]));
                            var across = Vector3.Cross(axis, toeDir).normalized;
                            float roll = Mathf.Asin(Mathf.Clamp(Vector3.Dot(up.normalized, across), -1f, 1f)) * Mathf.Rad2Deg;
                            line.Append($" vs-intro twist {twist:F0} flex {flex:F0} roll {roll:F0}");
                            // the shin's own roll: the calf against the thigh, about the calf's length, since the intro
                            var thigh = shin.parent;
                            if (thigh != null)
                            {
                                var rel = Quaternion.Inverse(thigh.rotation) * shin.rotation;
                                if (shot == 0)
                                    introRel[k] = rel;
                                var boneAxis = shin.InverseTransformDirection(feet[k].position - shin.position).normalized;
                                line.Append($" calf-roll {RoeHelperRig.TwistAngle(Quaternion.Inverse(introRel[k]) * rel, boneAxis):F0}");
                                // from the positions alone: the way the knee sticks out (off the hip-ankle line) against the toes, along the floor
                                var hip = thigh.position;
                                var line3 = feet[k].position - hip;
                                var off = shin.position - (hip + line3 * Mathf.Clamp01(Vector3.Dot(shin.position - hip, line3) / line3.sqrMagnitude));
                                var kneeFlat = Vector3.ProjectOnPlane(off, Vector3.up);
                                var toeFlat = Vector3.ProjectOnPlane(toes[k].position - feet[k].position, Vector3.up);
                                if (off.magnitude > 0.01f && kneeFlat.sqrMagnitude > 1e-6f && toeFlat.sqrMagnitude > 1e-6f)
                                    line.Append($" toes-vs-knee {Vector3.SignedAngle(kneeFlat, toeFlat, Vector3.up):F0} (knee bent {off.magnitude * 100f:F1} cm)");
                                // hip, knee, ankle, toe in her own frame (x right, y up, z ahead; cm from the point under her hips)
                                var frame = Quaternion.LookRotation(Vector3.ProjectOnPlane(fwd, Vector3.up).normalized, Vector3.up);
                                var origin = Vector3.ProjectOnPlane(me.rig.animator.GetBoneTransform(HumanBodyBones.Hips).position, Vector3.up) + Vector3.up * floor;
                                string P(Vector3 p) { var q = Quaternion.Inverse(frame) * (p - origin) * 100f; return $"({q.x:F0},{q.y:F0},{q.z:F0})"; }
                                line.Append($" legs hip{P(hip)} knee{P(shin.position)} ankle{P(feet[k].position)} toe{P(toes[k].position)}");
                                // the toes before the foot was turned to stand, and the heading it stood in (against the knee)
                                var before = me.rig.FootToes[k];
                                var used = me.rig.FootAhead[k];
                                if (kneeFlat.sqrMagnitude > 1e-6f && before.sqrMagnitude > 1e-8f && used.sqrMagnitude > 1e-8f)
                                    line.Append($" toes-before-vs-knee {Vector3.SignedAngle(kneeFlat, Vector3.ProjectOnPlane(before, Vector3.up), Vector3.up):F0} heading-vs-knee {Vector3.SignedAngle(kneeFlat, used, Vector3.up):F0}" +
                                                $" before: pitch {Mathf.Atan2(-before.y, new Vector2(before.x, before.z).magnitude) * Mathf.Rad2Deg:F0} along-heading {Vector3.Dot(before, used.normalized) * 100f:F1} cm, stand weight {me.rig.FootWeight[k]:F2}");
                            }
                        }
                        // flat feet: how far the foot is from the stand it should take (against the direction it points along the floor)
                        if (me.rig.flatFeet)
                        {
                            var ahead = me.rig.Heading(k);
                            if (ahead.sqrMagnitude > 1e-6f)
                                line.Append($" off-stand {Quaternion.Angle(Quaternion.Inverse(Quaternion.LookRotation(ahead.normalized, Vector3.up)) * feet[k].rotation, me.rig.flatRest[k]):F0}");
                        }
                        // which bones carry the lowest heel vertex (a helper that the motion capture moves differently?)
                        if (heelAt[k].Item1 >= 0)
                        {
                            var r = renderers[heelAt[k].Item1];
                            var w = rigid[heelAt[k].Item1].bw[heelAt[k].Item2];
                            string B(int b) => b < r.bones.Length && r.bones[b] != null ? r.bones[b].name : "?";
                            line.Append($" heel@{B(w.boneIndex0)}:{w.weight0:F2}/{B(w.boneIndex1)}:{w.weight1:F2}/{B(w.boneIndex2)}:{w.weight2:F2}");
                        }
                    }
                    table.AppendLine(line.ToString());
                    // side view at floor level, from her left, a little in front (and from her right: -roeSides both)
                    var mid = (feet[0].position + feet[1].position) * 0.5f;
                    mid.y = floor + 0.14f;
                    var side = Vector3.Cross(Vector3.up, fwd).normalized;     // her right
                    var cam = game.cam;
                    cam.fieldOfView = 30f;
                    cam.transform.position = mid - side * 1.5f + fwd * 0.35f + Vector3.up * 0.05f;
                    cam.transform.LookAt(mid, Vector3.up);
                    if (!warmed)
                    {
                        RoeCapture.Render(cam, 320, 180, System.IO.Path.Combine(outDir, "_warm.jpg"), 80);
                        RoeCapture.Render(cam, 320, 180, System.IO.Path.Combine(outDir, "_warm.jpg"), 80);
                        warmed = true;
                    }
                    RoeCapture.Render(cam, 640, 400, System.IO.Path.Combine(outDir, $"{packName}_{shot:D3}.jpg"), 90);
                    if (bothSides)
                    {
                        cam.transform.position = mid + side * 1.5f + fwd * 0.35f + Vector3.up * 0.05f;
                        cam.transform.LookAt(mid, Vector3.up);
                        RoeCapture.Render(cam, 640, 400, System.IO.Path.Combine(outDir, $"{packName}_{shot:D3}_r.jpg"), 90);
                        // and from the front at knee height: which way the knees and the feet face
                        var legs = mid + Vector3.up * 0.2f;
                        cam.transform.position = legs + fwd * 2.2f + Vector3.up * 0.15f;
                        cam.transform.LookAt(legs, Vector3.up);
                        RoeCapture.Render(cam, 640, 400, System.IO.Path.Combine(outDir, $"{packName}_{shot:D3}_f.jpg"), 90);
                    }
                    shot++;
                }
                // the game's own pose while the round is introduced
                for (int s = 0; game.phase != FightGame.Phase.Fight; s++)
                {
                    game.UpdateCamera(FightGame.Dt);
                    game.Step(new FighterInput[2]);
                    if (s == 45)
                        Shoot("intro (game clip)");
                }
                me.foe.pos = me.pos + (me.foe.pos - me.pos).normalized * 6f;
                me.foe.Place();
                float next = 0f;
                for (int s = 0; s < 10.8f * 60f; s++)
                {
                    float t = s / 60f;
                    var input = default(FighterInput);
                    string what = "";
                    float every = 0.25f;
                    foreach (var (from, to, inp, name, ev) in script)
                        if (t >= from && t < to)
                        {
                            input = inp.a || inp.b || inp.c || inp.d ? (s == Mathf.RoundToInt(from * 60f) ? inp : default) : inp;
                            what = name;
                            every = ev;
                        }
                    // the fight's camera back in place (the shots move it): the game reads the input in its terms
                    game.UpdateCamera(FightGame.Dt);
                    // walking: towards the opponent on the screen
                    var camRight = game.cam.transform.right;
                    camRight.y = 0f;
                    input.x *= Vector3.Dot(me.foe.pos - me.pos, camRight) >= 0f ? 1 : -1;
                    var inputs = new FighterInput[2];
                    inputs[who] = input;
                    game.Step(inputs);
                    if (what.Length > 0 && t >= next)
                    {
                        Shoot($"{what} {t:F2}s");
                        next = t + every;
                    }
                }
                Debug.Log($"[ROE] feet {id} {packName}: {shot} shots");
            }
            Object.DestroyImmediate(baked);
            System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "feet.txt"), table.ToString());
            Debug.Log("[ROE] feet:\n" + table);
        }

        /// <summary>
        /// How well the limb helpers' fit (RoeHelperRig) keeps the skin of the heels where the game's own keys have it.
        /// Every frame of the game's clips on the fighter prefab: the heel skin (vertices of the foot behind and below
        /// the ankle in the bind pose) as the game keys the helpers, then with the helpers driven by the fit; the
        /// difference in the foot's frame, by the angle knee-ankle-toe (g05's front foot in the motion capture's guard:
        /// 152 degrees, her stands 112-114, the bind pose 162).  Also how far the skinned heel lies from where the foot
        /// bone alone would put it (the share of the helpers), keyed and fitted.
        ///   -executeMethod RoeFighter.EditorTools.RoeFightProbe.HelperHeel [-roeChar g05]
        /// </summary>
        public static void HelperHeel()
        {
            string id = RoeCapture.Arg("-roeChar", "g05");
            var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(RoeHumanoid.FighterPath(id)));
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var an = go.GetComponent<Animator>();
            var rig = go.GetComponent<RoeHelperRig>();
            var feet = new[] { an.GetBoneTransform(HumanBodyBones.LeftFoot), an.GetBoneTransform(HumanBodyBones.RightFoot) };
            var toes = new[] { an.GetBoneTransform(HumanBodyBones.LeftToes), an.GetBoneTransform(HumanBodyBones.RightToes) };
            var knees = new[] { an.GetBoneTransform(HumanBodyBones.LeftLowerLeg), an.GetBoneTransform(HumanBodyBones.RightLowerLeg) };
            // the heel skin: vertices whose strongest bone is the foot, behind the ankle (along the floor) and 2 cm or more
            // below it while she stands (the bind pose points her bare feet down)
            var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r => r.sharedMesh != null && r.enabled && r.gameObject.activeInHierarchy).ToArray();
            var standClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(RoeHumanoidClips.ClipPath(id, "react_02"))
                            ?? AssetDatabase.LoadAssetAtPath<AnimationClip>(RoeHumanoidClips.ClipPath(id, "idle_01"));
            RoeCapture.Pose(go, standClip, 0f);
            var heel = smrs.Select(r =>
            {
                var bw = r.sharedMesh.boneWeights;
                var bind = r.sharedMesh.bindposes;
                var verts = r.sharedMesh.vertices;
                var lists = new[] { new List<int>(), new List<int>() };
                for (int k = 0; k < 2; k++)
                {
                    var ahead = Vector3.ProjectOnPlane(toes[k].position - feet[k].position, Vector3.up).normalized;
                    for (int i = 0; i < bw.Length; i++)
                    {
                        int b = bw[i].boneIndex0;
                        if (b >= r.bones.Length || r.bones[b] != feet[k])
                            continue;
                        var p = feet[k].TransformPoint(bind[b].MultiplyPoint3x4(verts[i]));
                        if (Vector3.Dot(p - feet[k].position, ahead) < 0f && p.y < feet[k].position.y - 0.02f)
                            lists[k].Add(i);
                    }
                }
                return lists;
            }).ToArray();
            var rigidAt = smrs.Select(r => (bind: r.sharedMesh.bindposes, verts: r.sharedMesh.vertices, bw: r.sharedMesh.boneWeights)).ToArray();
            var mesh = new Mesh();
            Vector3[] Skin(int k)
            {
                var all = new List<Vector3>();
                for (int ri = 0; ri < smrs.Length; ri++)
                {
                    if (heel[ri][k].Count == 0)
                        continue;
                    smrs[ri].BakeMesh(mesh, true);
                    var v = mesh.vertices;
                    var m = Matrix4x4.TRS(smrs[ri].transform.position, smrs[ri].transform.rotation, Vector3.one);
                    foreach (int i in heel[ri][k])
                        all.Add(feet[k].InverseTransformPoint(m.MultiplyPoint3x4(v[i])));
                }
                return all.ToArray();
            }
            Vector3[] Rigid(int k)
            {
                var all = new List<Vector3>();
                for (int ri = 0; ri < smrs.Length; ri++)
                    foreach (int i in heel[ri][k])
                    {
                        int b = rigidAt[ri].bw[i].boneIndex0;
                        all.Add(rigidAt[ri].bind[b].MultiplyPoint3x4(rigidAt[ri].verts[i]));
                    }
                return all.ToArray();
            }
            var rigid = new[] { Rigid(0), Rigid(1) };
            var bins = new[] { 100f, 120f, 140f, 160f, 999f };
            var stats = new Dictionary<int, List<(float fitErr, float keyedOff, float fitOff)>>();
            var sb = new System.Text.StringBuilder($"[ROE] helper heels of {id}: {heel.Sum(h => h[0].Count)}/{heel.Sum(h => h[1].Count)} heel vertices (L/R), " +
                                                   $"helpers {(rig != null ? rig.drives.Count : 0)}");
            var worst = new List<(float err, string where)>();
            foreach (var name in new[] { "idle_01", "idle_02", "react_01", "react_02", "hurt", "die", "rip", "skill_01", "skill_02", "skill_03" })
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(RoeHumanoidClips.ClipPath(id, name));
                if (clip == null)
                    continue;
                for (float t = 0f; t <= clip.length + 1e-4f; t += 1f / 15f)
                {
                    RoeCapture.Pose(go, clip, t);
                    var keyed = new[] { Skin(0), Skin(1) };
                    var angle = Enumerable.Range(0, 2).Select(k => Vector3.Angle(knees[k].position - feet[k].position, toes[k].position - feet[k].position)).ToArray();
                    rig?.Apply(1f);
                    var fitted = new[] { Skin(0), Skin(1) };
                    for (int k = 0; k < 2; k++)
                    {
                        float err = 0f, keyedOff = 0f, fitOff = 0f;
                        for (int j = 0; j < keyed[k].Length; j++)
                        {
                            err += (keyed[k][j] - fitted[k][j]).magnitude;
                            keyedOff += (keyed[k][j] - rigid[k][j]).magnitude;
                            fitOff += (fitted[k][j] - rigid[k][j]).magnitude;
                        }
                        int n = Mathf.Max(1, keyed[k].Length);
                        int bin = System.Array.FindIndex(bins, b => angle[k] < b);
                        if (!stats.TryGetValue(bin, out var list))
                            stats[bin] = list = new List<(float, float, float)>();
                        list.Add((err / n, keyedOff / n, fitOff / n));
                        worst.Add((err / n, $"{name} {t:F2}s {(k == 0 ? "L" : "R")} angle {angle[k]:F0}"));
                    }
                }
            }
            RoeCapture.EndPosing();
            foreach (var kv in stats.OrderBy(x => x.Key))
            {
                string range = kv.Key == 0 ? $"< {bins[0]:F0}" : kv.Key < bins.Length - 1 ? $"{bins[kv.Key - 1]:F0}-{bins[kv.Key]:F0}" : $">= {bins[kv.Key - 1]:F0}";
                var l = kv.Value;
                sb.Append($"\n[ROE]   knee-ankle-toe {range,-8} deg: {l.Count,4} feet, heel skin fit vs keys {l.Average(x => x.fitErr) * 100f:F1} cm (max {l.Max(x => x.fitErr) * 100f:F1}), " +
                          $"off the foot bone keyed {l.Average(x => x.keyedOff) * 100f:F1} cm, fitted {l.Average(x => x.fitOff) * 100f:F1} cm");
            }
            foreach (var w in worst.OrderByDescending(x => x.err).Take(6))
                sb.Append($"\n[ROE]   worst: {w.err * 100f:F1} cm in {w.where}");
            Object.DestroyImmediate(mesh);
            Object.DestroyImmediate(go);
            Debug.Log(sb.ToString());
        }

        /// <summary>
        /// The same in the fight: a fighter standing in her guard (real fight logic, no input), every 0.25 s the
        /// hips-to-neck line against vertical (+ forward, towards where she faces), the hips' height, where the ankles
        /// are against the hips (+ ahead), and the mixer weights.
        ///   -executeMethod RoeFighter.EditorTools.RoeFightProbe.FightLean -roeChars g04,g05 [-roeMotions bandai1]
        /// </summary>
        public static void FightLean()
        {
            string stage = RoeCapture.Arg("-roeStage", "e23_steel_s02");
            EditorSceneManager.OpenScene(RoeFightScene.ScenePath(stage), OpenSceneMode.Single);
            var game = Object.FindFirstObjectByType<FightGame>();
            string pack = RoeCapture.Arg("-roeMotions", null);
            if (pack != null)
                game.motionPack = Mathf.Max(0, game.motionPacks.FindIndex(p => p.name == pack));
            var sb = new System.Text.StringBuilder("[ROE] lean in the fight:");
            if (RoeCapture.Arg("-roeOwn", "1") == "0")
                foreach (var r in game.roster)
                    r.strikePack = null;        // the motion pack's guard and strikes for everyone
            foreach (var id in RoeCapture.Arg("-roeChars", "g04,g05").Split(','))
            {
                if (game.rigs.All(r => r.id != id))
                    game.PickIds(null, id);
                game.cpu = new[] { false, false };
                game.Setup();
                while (game.phase != FightGame.Phase.Fight)
                    game.Step(new FighterInput[2]);
                var me = game.f[game.f[0].rig.id == id ? 0 : 1];
                var an = me.rig.animator;
                var hips = an.GetBoneTransform(HumanBodyBones.Hips);
                var neck = an.GetBoneTransform(HumanBodyBones.Neck) ?? an.GetBoneTransform(HumanBodyBones.Head);
                var feet = new[] { an.GetBoneTransform(HumanBodyBones.LeftFoot), an.GetBoneTransform(HumanBodyBones.RightFoot) };
                sb.Append($"\n[ROE]   {id} ({me.rig.Current}):");
                for (int s = 0; s <= 90; s++)
                {
                    game.Step(new FighterInput[2]);
                    if (s % 15 != 0)
                        continue;
                    var fwd = me.Forward;
                    var up = neck.position - hips.position;
                    float lean = Mathf.Atan2(Vector3.Dot(up, fwd), up.y) * Mathf.Rad2Deg;
                    float ahead = feet.Average(f => Vector3.Dot(f.position - hips.position, fwd));
                    sb.Append($"\n[ROE]     t{s / 60f:F2} lean {lean:F0} hips {hips.position.y - me.pos.y:F2} m ankles ahead {ahead:F2} m | {me.rig.MixerWeights()}");
                }
            }
            Debug.Log(sb.ToString());
        }

        /// <summary>
        /// How high the game's own clips hold a fighter (g05 hovers in hers): each game clip played on its own in the
        /// fight's rig (real Init, Tick at 30 Hz, the opponent far away), per clip the lowest sole above the floor
        /// (toe and ankle bones against their standing heights) at the start, its lowest and highest, the hips'
        /// height and the lowest humanoid bone; side views of the chosen fighters every 0.4 s.
        ///   -executeMethod RoeFighter.EditorTools.RoeFightProbe.Hover [-roeChars a08,g04,b10,g05] [-roeShots g05] [-roeOut dir]
        /// </summary>
        public static void Hover()
        {
            string stage = RoeCapture.Arg("-roeStage", "e23_steel_s02");
            var ids = RoeCapture.Arg("-roeChars", "a08,g04,b10,g05").Split(',');
            var shots = new HashSet<string>(RoeCapture.Arg("-roeShots", "g05").Split(','));
            string outDir = RoeCapture.Arg("-roeOut", System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "_work", "hover"));
            if (System.IO.Directory.Exists(outDir))
                System.IO.Directory.Delete(outDir, true);
            System.IO.Directory.CreateDirectory(outDir);
            ShaderUtil.allowAsyncCompilation = false;
            EditorSceneManager.OpenScene(RoeFightScene.ScenePath(stage), OpenSceneMode.Single);
            var game = Object.FindFirstObjectByType<FightGame>();
            game.cpu = new[] { false, false };
            game.hud.gameObject.SetActive(false);
            foreach (var rig in game.roster.Length > 0 ? game.roster : game.rigs)
                foreach (var smr in rig.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    smr.forceMatrixRecalculationPerRender = true;
            var sb = new System.Text.StringBuilder("[ROE] hover (cm; sole = lowest sole above the floor):");
            bool warmed = false;
            foreach (var id in ids)
            {
                if (game.rigs.All(r => r.id != id))
                    game.PickIds(null, id);
                game.Setup();
                while (game.phase != FightGame.Phase.Fight)
                    game.Step(new FighterInput[2]);
                var me = game.f[game.f[0].rig.id == id ? 0 : 1];
                me.foe.pos = me.pos + (me.foe.pos - me.pos).normalized * 6f;
                me.foe.Place();
                var rig = me.rig;
                var an = rig.animator;
                var hips = an.GetBoneTransform(HumanBodyBones.Hips);
                var human = new List<Transform>();
                foreach (HumanBodyBones b in System.Enum.GetValues(typeof(HumanBodyBones)))
                    if (b != HumanBodyBones.LastBone && an.GetBoneTransform(b) != null)
                        human.Add(an.GetBoneTransform(b));
                sb.Append($"\n[ROE]   {id} (stance {rig.stance?.name}, feet {(rig.flatFeet ? "flat" : "heels")}):");
                foreach (var c in rig.clips.Where(c => c.game))
                {
                    rig.Play(c.name, 1f, 0f);
                    float length = Mathf.Min(c.clip.length, 6f);
                    var soles = new List<float>();
                    float hipsLow = float.MaxValue, hipsHigh = float.MinValue, boneLow = float.MaxValue;
                    float startSole = 0f;
                    int n = 0;
                    float nextShot = 0f;
                    for (float t = 0f; t <= length + 1e-4f; t += 1f / 30f, n++)
                    {
                        rig.Tick(n == 0 ? 0f : 1f / 30f);
                        float floor = an.transform.position.y;
                        float sole = rig.SoleHeight;
                        if (n == 0)
                            startSole = sole;
                        soles.Add(sole);
                        hipsLow = Mathf.Min(hipsLow, hips.position.y - floor);
                        hipsHigh = Mathf.Max(hipsHigh, hips.position.y - floor);
                        boneLow = Mathf.Min(boneLow, human.Min(b => b.position.y) - floor);
                        if (shots.Contains(id) && t >= nextShot)
                        {
                            var fwd = me.Forward;
                            var side = Vector3.Cross(Vector3.up, fwd).normalized;
                            var mid = new Vector3(hips.position.x, floor + 0.85f, hips.position.z);
                            var cam = game.cam;
                            cam.fieldOfView = 35f;
                            cam.transform.position = mid - side * 4.2f + fwd * 0.6f;
                            cam.transform.LookAt(mid, Vector3.up);
                            if (!warmed)
                            {
                                RoeCapture.Render(cam, 320, 180, System.IO.Path.Combine(outDir, "_warm.jpg"), 80);
                                RoeCapture.Render(cam, 320, 180, System.IO.Path.Combine(outDir, "_warm.jpg"), 80);
                                warmed = true;
                            }
                            RoeCapture.Render(cam, 360, 480, System.IO.Path.Combine(outDir, $"{id}_{c.name}_{Mathf.RoundToInt(t * 10f):D3}.jpg"), 88);
                            nextShot = t + 0.4f;
                        }
                    }
                    var sorted = soles.OrderBy(x => x).ToList();
                    sb.Append($"\n[ROE]     {c.name,-9} {c.clip.length,5:F2}s sole start {startSole * 100f,5:F1} low {sorted[0] * 100f,5:F1} " +
                              $"median {sorted[sorted.Count / 2] * 100f,5:F1} high {sorted[sorted.Count - 1] * 100f,5:F1} | hips {hipsLow * 100f:F0}..{hipsHigh * 100f:F0} " +
                              $"| lowest bone {boneLow * 100f:F1}");
                }
            }
            System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "hover.txt"), sb.ToString());
            Debug.Log(sb.ToString());
        }

        /// <summary>
        /// How far forward the body leans in humanoid clips: each clip posed on each fighter (prefab, no fight logic)
        /// at a few times - hips height, the hips-to-neck line from vertical, the hips' pitch.  For checking a
        /// retarget against the source (DOA6 Mai's stance leans 36-43 degrees in the BVH).
        ///   -executeMethod RoeFighter.EditorTools.RoeFightProbe.Lean -roeChars g04,g05 -roeClips path1,path2
        /// </summary>
        public static void Lean()
        {
            var ids = RoeCapture.Arg("-roeChars", "g04").Split(',');
            var clips = RoeCapture.Arg("-roeClips", "").Split(',').Where(x => x.Length > 0).ToArray();
            var sb = new System.Text.StringBuilder("[ROE] lean:");
            foreach (var id in ids)
            {
                var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(RoeHumanoid.FighterPath(id)));
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var an = go.GetComponent<Animator>();
                var hips = an.GetBoneTransform(HumanBodyBones.Hips);
                var neck = an.GetBoneTransform(HumanBodyBones.Neck) ?? an.GetBoneTransform(HumanBodyBones.Head);
                foreach (var path in clips)
                {
                    var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                    if (clip == null)
                        continue;
                    sb.Append($"\n[ROE]   {id} {clip.name}:");
                    for (float t = 0f; t <= clip.length + 1e-4f; t += Mathf.Max(0.1f, clip.length / 6f))
                    {
                        RoeCapture.Pose(go, clip, t);
                        var up = neck.position - hips.position;
                        float lean = Vector3.Angle(up, Vector3.up);
                        sb.Append($" t{t:F1} hips {hips.position.y:F2} m lean {lean:F0}");
                    }
                }
                RoeCapture.EndPosing();
                Object.DestroyImmediate(go);
            }
            Debug.Log(sb.ToString());
        }

        /// <summary>
        /// Where the pelvis points in humanoid clips: each clip posed on a fighter at a few times, the
        /// pelvis bone's axes, the spine and thighs relative to world up, and the skirt's first bone.
        ///   -executeMethod RoeFighter.EditorTools.RoeFightProbe.PelvisCheck [-roeChars a08] [-roeClips path1,path2]
        /// </summary>
        public static void PelvisCheck()
        {
            var ids = RoeCapture.Arg("-roeChars", "a08").Split(',');
            var clips = RoeCapture.Arg("-roeClips", "Assets/RoeFighter/Generated/mocap/mc_guard.anim," +
                                       "Assets/RoeFighter/Generated/motionpacks/accad_male2/guard.anim," +
                                       "Assets/RoeFighter/Generated/motionpacks/accad_male2/walk.anim").Split(',');
            foreach (var id in ids)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(RoeHumanoid.FighterPath(id)));
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var a = go.GetComponent<Animator>();
                var pelvis = a.GetBoneTransform(HumanBodyBones.Hips);
                var spine = a.GetBoneTransform(HumanBodyBones.Spine);
                var thighL = a.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
                var thighR = a.GetBoneTransform(HumanBodyBones.RightUpperLeg);
                var kneeL = a.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
                var kneeR = a.GetBoneTransform(HumanBodyBones.RightLowerLeg);
                var skirt = go.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Skirt_L_00" || t.name == "Skirt_Back_01");
                string Dir(Vector3 v) => $"({v.x:+0.00;-0.00} {v.y:+0.00;-0.00} {v.z:+0.00;-0.00})";
                float Tilt(Vector3 v) => Vector3.Angle(v, Vector3.up);
                var sb = new System.Text.StringBuilder($"[ROE] pelvis check {id}:");
                foreach (var path in clips)
                {
                    var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                    if (clip == null)
                        continue;
                    foreach (float t in new[] { 0f, clip.length * 0.5f })
                    {
                        RoeCapture.Pose(go, clip, t);
                        var sp = spine.position - pelvis.position;
                        var hipsAxis = thighR.position - thighL.position;
                        sb.Append($"\n[ROE]   {System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path))}/{clip.name} t={t:F2}: pelvis x{Dir(pelvis.right)} y{Dir(pelvis.up)} z{Dir(pelvis.forward)}; " +
                                  $"spine tilt {Tilt(sp):F0} deg, hip axis {Dir(hipsAxis.normalized)}, thighs L{Dir((kneeL.position - thighL.position).normalized)} R{Dir((kneeR.position - thighR.position).normalized)}" +
                                  (skirt != null && skirt.childCount > 0 ? $", {skirt.name} {Dir((skirt.GetChild(0).position - skirt.position).normalized)} local {skirt.localEulerAngles}" : ""));
                    }
                }
                RoeCapture.EndPosing();
                Object.DestroyImmediate(go);
                Debug.Log(sb.ToString());
            }
        }
    }
}
