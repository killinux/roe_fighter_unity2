using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace RoeFighter.EditorTools
{
    /// <summary>
    /// A choreographed exchange on one of the game's stages, filmed like a fighting game.  The two
    /// fighters take turns with their three skills.  Everything about a skill is the game's own:
    ///   - the unit prefab and its timeline play the animation and the effects (RoeUnit);
    ///   - the skill sheet (tools/skill_sheet.py) says when the attacker slides up to the opponent
    ///     and back, when the hits land, which effects appear on the opponent, when the camera
    ///     shakes, and which sounds and voice lines play.
    /// The last skill knocks the opponent out.
    /// This is NOT gameplay (no input, no hit detection) - it shows how characters, stage, camera
    /// and skills will look together.  Writes frames + timeline.json for tools/make_video.py.
    /// Batch mode: -executeMethod RoeFighter.EditorTools.RoeDuel.Run [-roeStage s01] [-roeOut dir]
    ///             [-roeSize 1920x1080] [-roeGap 4.4] [-roeVoice 2] (voice language: 2 = Japanese, 1 = English)
    ///             [-roeFrames 0-600] (render only this range, for a quick look)
    /// </summary>
    public static class RoeDuel
    {
        const int Fps = 60;

        class Fighter
        {
            public RoeUnit unit;
            public Vector3 home, facing;
            public float idleLength, hurtLength, dieLength, ripLength;
            public int dieStart = -1;
            public readonly List<string> action = new List<string>();
            public readonly List<float> time = new List<float>();
            public readonly List<float> advance = new List<float>();    // metres from home towards the opponent
            public Transform[] body, all, key;

            public RoeManifest.Character info => unit.info;
            public RoeSkillSheet sheet => unit.sheet;

            public float Length(string action)
            {
                var a = sheet.Find(action);
                return a?.Body == null ? 0f : a.Body.end - a.Body.start;
            }
        }

        struct ShakeEvent
        {
            public int frame;
            public RoeSkillSheet.Shake shake;
        }

        /// <summary>An effect the game spawns at a target: a prefab at a spot, alive for a while.</summary>
        class SpawnedEffect
        {
            public int frame, frames;
            public GameObject prefab;
            public Fighter at, from;
            public string anchor;
            public GameObject instance;
            public PlayableDirector director;
            public ParticleSystem[] roots;
            public float simulated;
        }

        static string FindAudio(RoeManifest.Character c, string clipName)
        {
            var path = c.sfx.FirstOrDefault(p => Path.GetFileNameWithoutExtension(p) == clipName);
            if (path != null)
                return path;
            foreach (var guid in AssetDatabase.FindAssets($"{clipName} t:AudioClip", new[] { "Assets/ROE" }))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(p) == clipName)
                    return p;
            }
            return null;
        }

        static GameObject FindEffect(string prefabName)
        {
            foreach (var guid in AssetDatabase.FindAssets($"{prefabName} t:Prefab", new[] { "Assets/ROE/skills" }))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(p) == prefabName)
                    return AssetDatabase.LoadAssetAtPath<GameObject>(p);
            }
            return null;
        }

        /// <summary>How long a spawned effect lives: its own timeline if it has one, else its particle systems.</summary>
        static float EffectSeconds(GameObject prefab)
        {
            var director = prefab.GetComponentInChildren<PlayableDirector>(true);
            if (director != null && director.playableAsset != null && director.playableAsset.duration > 0.0)
                return (float)director.playableAsset.duration;
            float longest = 0f;
            foreach (var ps in prefab.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                longest = Mathf.Max(longest, main.startDelay.constantMax + main.duration + main.startLifetime.constantMax);
            }
            return Mathf.Clamp(longest, 0.5f, 6f);
        }

        [MenuItem("ROE Fighter/Showcase/Stage duel video frames")]
        public static void Run()
        {
            string stage = RoeCapture.Arg("-roeStage", "s01");
            string outDir = RoeCapture.Arg("-roeOut", Path.Combine(Path.GetDirectoryName(Application.dataPath), "_work", "duel"));
            var size = RoeCapture.Arg("-roeSize", "1920x1080").Split('x');
            int width = int.Parse(size[0]), height = int.Parse(size[1]);
            float gap = float.Parse(RoeCapture.Arg("-roeGap", "4.4"), CultureInfo.InvariantCulture);
            string voiceLanguage = RoeCapture.Arg("-roeVoice", "2");
            var range = RoeCapture.Arg("-roeFrames", "").Split('-');

            ShaderUtil.allowAsyncCompilation = false;
            var info = RoeStage.Open(stage);
            Debug.Log("[ROE] " + info.report);
            var sight = new RoeStage.Sight();
            var layout = RoeStage.FindLayout(info, sight);
            Debug.Log($"[ROE] duel layout: {layout}");
            if (gap > layout.halfLength * 2f - 1.2f)
            {
                gap = layout.halfLength * 2f - 1.2f;
                Debug.Log($"[ROE] duel: the free fight line is {layout.halfLength * 2f:F1} m, fighters moved together to {gap:F1} m");
            }

            // ---- the two fighters
            var manifest = RoeManifest.Load();
            var fighters = manifest.WithModels.Take(2).Select(c => new Fighter { unit = RoeUnit.Create(c) }).ToArray();
            for (int i = 0; i < 2; i++)
            {
                var f = fighters[i];
                f.idleLength = f.Length("idle_01");
                f.hurtLength = f.Length("hurt");
                f.dieLength = f.Length("die");
                f.ripLength = f.Length("rip");
                f.unit.Show("idle_01", 0f);
                float side = i == 0 ? -1f : 1f;
                f.home = layout.centre + layout.axis * (gap * 0.5f * side);
                f.facing = -layout.axis * side;
                RoeStage.Place(f.unit.rig, f.home, f.facing, layout.normal);
                var all = f.unit.model.GetComponentsInChildren<SkinnedMeshRenderer>(true).SelectMany(r => r.bones).Where(b => b != null).Distinct().ToArray();
                f.all = all;
                f.body = all.Where(b => b.name.StartsWith("Bip001") && !b.name.Contains("Prop")).ToArray();
                f.key = new[] { "Bip001 Head", "Bip001 Spine1", "Bip001 Pelvis", "Bip001 L Hand", "Bip001 R Hand", "Bip001 L Foot", "Bip001 R Foot" }
                    .Select(n => RoeShowcase.FindBone(f.unit.model, n)).Where(b => b != null).ToArray();
            }

            // ---- schedule: per frame, which action and time each fighter shows and how far it has moved
            int total = 0;
            var shakes = new List<ShakeEvent>();
            var sounds = new List<string>();
            var spawns = new List<SpawnedEffect>();
            var missing = new HashSet<string>();
            void Sound(Fighter f, string clipName, float seconds, float volume)
            {
                var path = string.IsNullOrEmpty(clipName) ? null : FindAudio(f.info, clipName);
                if (path == null)
                {
                    if (!string.IsNullOrEmpty(clipName))
                        missing.Add(clipName);
                    return;
                }
                sounds.Add($"{{\"path\":\"{path}\",\"frame\":{total + Mathf.RoundToInt(seconds * Fps)},\"volume\":{volume.ToString("F2", CultureInfo.InvariantCulture)}}}");
            }
            void ActionSounds(Fighter f, RoeSkillSheet.Action action, float offset)
            {
                foreach (var s in action.sounds.Where(s => !s.muted))
                    Sound(f, s.clip, offset + s.start, s.volume);
                foreach (var v in action.voices.Where(v => !v.muted))
                    Sound(f, v.clips.FirstOrDefault(c => c.language == voiceLanguage)?.clip, offset + v.start, 1f);
            }
            void Push(Fighter f, string action, float time, float advance)
            {
                f.action.Add(action);
                f.time.Add(time);
                f.advance.Add(advance);
            }
            void RestFrame(Fighter f, int frame)
            {
                if (f.dieStart >= 0)
                {
                    float t = (frame - f.dieStart) / (float)Fps;
                    if (t < f.dieLength)
                        Push(f, "die", t, 0f);
                    else if (f.ripLength > 0f)
                        Push(f, "rip", (t - f.dieLength) % f.ripLength, 0f);
                    else
                        Push(f, "die", f.dieLength - 0.001f, 0f);
                }
                else
                {
                    Push(f, "idle_01", (frame / (float)Fps) % f.idleLength, 0f);
                }
            }
            void Rest(int frames)
            {
                for (int k = 0; k < frames; k++)
                    foreach (var f in fighters)
                        RestFrame(f, total + k);
                total += frames;
            }
            void Attack(int attacker, string skill, bool finisher)
            {
                var atk = fighters[attacker];
                var vic = fighters[1 - attacker];
                var action = atk.sheet.Find(skill);
                var body = action?.Body;
                if (body == null || !atk.unit.Has(skill))
                {
                    Debug.LogWarning($"[ROE] duel: {atk.info.id} has no {skill}");
                    return;
                }
                int frames = Mathf.RoundToInt(action.BodyEnd * Fps);
                var moves = action.moves.OrderBy(m => m.start).ToList();
                float AdvanceAt(float t)
                {
                    float value = 0f;
                    foreach (var m in moves)
                    {
                        // "attack": the game puts the unit in front of its target, its own attack radius plus the target's receive radius away
                        float target = m.kind == "attack" ? Mathf.Max(0f, gap - (vic.sheet.receiveRadius + m.radius))
                                     : m.kind == "return" ? 0f : value;
                        if (t >= m.end)
                            value = target;
                        else if (t > m.start)
                            return Mathf.Lerp(value, target, Mathf.SmoothStep(0f, 1f, (t - m.start) / Mathf.Max(1e-4f, m.end - m.start)));
                        else
                            break;
                    }
                    return value;
                }
                var damage = action.DamageHits.Select(h => h.time).OrderBy(t => t).ToList();
                var flinches = new List<float>();       // hits in quick succession are one flinch
                foreach (float h in damage)
                    if (flinches.Count == 0 || h - flinches[flinches.Count - 1] >= 0.3f)
                        flinches.Add(h);
                float dieAt = finisher && damage.Count > 0 ? damage[damage.Count - 1] : -1f;
                for (int f = 0; f < frames; f++)
                {
                    float t = f / (float)Fps;
                    Push(atk, skill, t, AdvanceAt(t));
                    if (dieAt >= 0f && t >= dieAt)
                    {
                        if (vic.dieStart < 0)
                        {
                            vic.dieStart = total + f;
                            var dieAction = vic.sheet.Find("die");
                            if (dieAction != null)
                                ActionSounds(vic, dieAction, t);
                        }
                        RestFrame(vic, total + f);
                        continue;
                    }
                    float flinch = -1f;
                    foreach (float h in flinches)
                        if (h <= t)
                            flinch = h;
                    if (flinch >= 0f && vic.hurtLength > 0f && t - flinch < vic.hurtLength)
                        Push(vic, "hurt", t - flinch, 0f);
                    else
                        RestFrame(vic, total + f);
                }
                foreach (var s in action.shakes)
                    shakes.Add(new ShakeEvent { frame = total + Mathf.RoundToInt(s.time * Fps), shake = s });
                foreach (var s in action.spawns)
                {
                    var prefab = FindEffect(s.prefab);
                    if (prefab == null)
                    {
                        missing.Add(s.prefab);
                        continue;
                    }
                    spawns.Add(new SpawnedEffect
                    {
                        frame = total + Mathf.RoundToInt(s.time * Fps), frames = Mathf.CeilToInt(EffectSeconds(prefab) * Fps),
                        prefab = prefab, at = s.kind == "target" ? vic : atk, from = atk, anchor = s.kind == "target" ? s.name : "center",
                    });
                }
                ActionSounds(atk, action, 0f);
                Debug.Log($"[ROE] duel: {atk.info.id} {skill} = clip {body.clip}, {frames} frames at {total}, hits at " +
                          $"{string.Join(" ", damage.Select(h => h.ToString("F2", CultureInfo.InvariantCulture)))} s, " +
                          $"moves {string.Join(" ", moves.Select(m => $"{m.kind}@{m.start:F2}"))}, {action.shakes.Count} shakes, " +
                          $"{action.effects.Count(e => !e.muted)} effects on the timeline, {action.spawns.Count} spawned at the target" +
                          (dieAt >= 0f ? $", knock-out at {dieAt:F2} s" : ""));
                total += frames;
            }

            Rest(75);
            var order = new[] { "skill1", "skill2", "skill3" };
            for (int k = 0; k < order.Length; k++)
            {
                Attack(0, order[k], false);
                Rest(30);
                Attack(1, order[k], k == order.Length - 1);
                Rest(k == order.Length - 1 ? 150 : 30);
            }
            if (missing.Count > 0)
                Debug.Log($"[ROE] duel: not in the project (skipped): {string.Join(", ", missing.OrderBy(s => s))}");

            // ---- showing a frame
            Transform Anchor(Fighter f, string name)
            {
                var node = f.sheet.anchors.FirstOrDefault(a => a.name == name)?.node;
                var t = node == null || string.IsNullOrEmpty(node.path) ? null : f.unit.rig.transform.Find(node.path);
                return t != null ? t : f.unit.rig.transform;
            }
            void Pose(int f, bool effects)
            {
                foreach (var x in fighters)
                {
                    x.unit.rig.transform.position = x.home + x.facing * x.advance[f];
                    x.unit.Show(x.action[f], x.time[f]);
                }
                if (!effects)
                    return;
                foreach (var s in spawns)
                {
                    bool alive = f >= s.frame && f < s.frame + s.frames;
                    if (!alive)
                    {
                        if (s.instance != null)
                            Object.DestroyImmediate(s.instance);
                        s.instance = null;
                        continue;
                    }
                    float t = (f - s.frame) / (float)Fps;
                    if (s.instance == null)
                    {
                        // the game spawns at a position; the effect looks the way the attacker looks
                        s.instance = Object.Instantiate(s.prefab, Anchor(s.at, s.anchor).position, s.from.unit.rig.transform.rotation);
                        s.instance.hideFlags = HideFlags.DontSave;
                        s.director = s.instance.GetComponentInChildren<PlayableDirector>(true);
                        if (s.director != null)
                        {
                            s.director.playOnAwake = false;
                            s.director.timeUpdateMode = DirectorUpdateMode.Manual;
                            s.director.RebuildGraph();
                        }
                        s.roots = s.instance.GetComponentsInChildren<ParticleSystem>(true)
                            .Where(p => p.transform.parent == null || p.transform.parent.GetComponentInParent<ParticleSystem>() == null).ToArray();
                        foreach (var p in s.instance.GetComponentsInChildren<ParticleSystem>(true))
                        {
                            p.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                            p.useAutoRandomSeed = false;
                        }
                        s.simulated = -1f;
                    }
                    if (s.director != null)
                    {
                        s.director.time = t;
                        s.director.Evaluate();
                    }
                    else
                    {
                        foreach (var p in s.roots)
                        {
                            if (s.simulated < 0f)
                                p.Simulate(0f, true, true, false);
                            p.Simulate(t - Mathf.Max(0f, s.simulated), true, false, true);
                        }
                        s.simulated = t;
                    }
                }
            }

            // ---- camera
            var camGo = new GameObject("Duel Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 500f;
            cam.allowHDR = true;
            cam.allowMSAA = true;
            const float fov = 30f;
            cam.fieldOfView = fov;
            var data = camGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.renderShadows = true;
            data.requiresDepthTexture = true;
            data.requiresColorTexture = true;       // the refraction effects read the picture behind them
            var volGo = new GameObject("Duel Volume");
            var volume = volGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(RoeProjectSetup.SettingsDir + "/RoeStudioVolume.asset");

            // pass 1: where the fighters are, frame by frame, along the fight line and in height.  The
            // camera frames the bodies; a weapon may pull the frame out by 1.2 m, a thrown one flies out of it.
            var mid = new float[total];
            var midY = new float[total];
            var dist = new float[total];
            float tanHalf = Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
            float aspect = width / (float)height;
            const float weaponRoom = 1.2f;
            var keyPositions = new Vector3[total][];
            for (int f = 0; f < total; f++)
            {
                Pose(f, false);
                float lo = float.MaxValue, hi = float.MinValue, top = 0f, loAll = float.MaxValue, hiAll = float.MinValue, topAll = 0f;
                foreach (var x in fighters)
                {
                    foreach (var b in x.body)
                    {
                        float u = Vector3.Dot(b.position - layout.centre, layout.axis);
                        lo = Mathf.Min(lo, u);
                        hi = Mathf.Max(hi, u);
                        top = Mathf.Max(top, b.position.y - layout.centre.y);
                    }
                    foreach (var b in x.all)
                    {
                        float u = Vector3.Dot(b.position - layout.centre, layout.axis);
                        loAll = Mathf.Min(loAll, u);
                        hiAll = Mathf.Max(hiAll, u);
                        topAll = Mathf.Max(topAll, b.position.y - layout.centre.y);
                    }
                }
                lo = Mathf.Max(loAll, lo - weaponRoom);
                hi = Mathf.Min(hiAll, hi + weaponRoom);
                top = Mathf.Min(topAll, top + weaponRoom);
                mid[f] = (lo + hi) * 0.5f;
                float halfWidth = (hi - lo) * 0.5f + 0.7f;
                float halfHeight = Mathf.Max(1.25f, (top + 0.45f) * 0.5f);
                midY[f] = halfHeight - 0.12f;
                dist[f] = Mathf.Max(halfWidth / aspect, halfHeight) / tanHalf;
                keyPositions[f] = fighters.SelectMany(x => x.key).Select(b => b.position).ToArray();
            }
            int r = Mathf.RoundToInt(Fps * 0.45f);
            mid = RoeShowcase.Smooth(RoeShowcase.Smooth(mid, r), r);
            midY = RoeShowcase.Smooth(RoeShowcase.Smooth(midY, r), r);
            dist = RoeShowcase.Smooth(RoeShowcase.Smooth(RoeShowcase.SlidingMax(dist, r), r), r);
            Vector3 Target(int f) => layout.centre + layout.axis * mid[f] + Vector3.up * midY[f];
            Vector3 Eye(int f) => Target(f) + layout.normal * dist[f] + Vector3.up * 0.25f;
            Vector3 ShakeAt(int f)
            {
                var offset = Vector3.zero;
                foreach (var s in shakes)
                {
                    float t = (f - s.frame) / (float)Fps;
                    if (t < 0f || t >= s.shake.duration)
                        continue;
                    float fade = 1f - t / s.shake.duration;
                    float a = s.shake.amplitude * 0.045f * fade * fade;
                    float w = 2f * Mathf.PI * 14f * t;
                    offset += layout.axis * (a * Mathf.Sin(w + s.frame)) + Vector3.up * (a * Mathf.Sin(w * 1.37f + 1.3f + s.frame));
                }
                return offset;
            }

            // sight check on the real camera path: is a stage prop ever between the camera and a fighter?
            int hiddenFrames = 0, hiddenParts = 0, longest = 0, run = 0, keyCount = keyPositions[0].Length;
            for (int f = 0; f < total; f++)
            {
                int hidden = keyPositions[f].Count(p => p.y > layout.centre.y + 0.05f && sight.Blocked(Eye(f), p));
                hiddenParts += hidden;
                run = hidden > 0 ? run + 1 : 0;
                longest = Mathf.Max(longest, run);
                if (hidden > 0)
                    hiddenFrames++;
            }
            sight.Dispose();
            string verdict = $"[ROE] duel sight check: a stage prop hides part of a fighter in {hiddenFrames} of {total} frames " +
                             $"({hiddenParts} of {total * keyCount} body-part samples), longest stretch {longest} frames; " +
                             $"camera distance {dist.Min():F1} .. {dist.Max():F1} m";
            if (hiddenFrames > total / 50)
                Debug.LogWarning(verdict);
            else
                Debug.Log(verdict);

            // pass 2: render.  Effects need every frame from the start (particles are stepped), so a
            // partial range still walks through the frames before it.
            int first = range.Length == 2 ? int.Parse(range[0]) : 0;
            int last = range.Length == 2 ? Mathf.Min(total - 1, int.Parse(range[1])) : total - 1;
            string frameDir = Path.Combine(outDir, "frames");
            if (Directory.Exists(frameDir))
                Directory.Delete(frameDir, true);
            string warm = Path.Combine(outDir, "_warmup.jpg");
            foreach (var x in fighters)
                x.unit.Stop();
            string probe = RoeCapture.Arg("-roeProbe", "");
            for (int f = 0; f <= last; f++)
            {
                Pose(f, true);
                if (probe != "" && probe.Split(',').Contains(f.ToString()))
                    foreach (var x in fighters)
                    {
                        var gp = RoeShowcase.FindBone(x.unit.ghost, "Bip001 Pelvis");
                        var gh = RoeShowcase.FindBone(x.unit.ghost, "Bip001 Head");
                        var mp = RoeShowcase.FindBone(x.unit.model, "Bip001 Pelvis");
                        var mh = RoeShowcase.FindBone(x.unit.model, "Bip001 Head");
                        Debug.Log($"[ROE] probe frame {f} {x.info.id} {x.action[f]}@{x.time[f]:F3}: ghost pelvis {gp.position:F3} head {gh.position:F3} | " +
                                  $"model pelvis {mp.position:F3} head {mh.position:F3}");
                    }
                if (f < first)
                    continue;
                var shake = ShakeAt(f);
                var target = Target(f) + shake;
                cam.transform.position = Eye(f) + shake;
                cam.transform.LookAt(target, Vector3.up);
                if (f == first)
                {
                    RoeCapture.Render(cam, 320, 180, warm, 80);
                    RoeCapture.Render(cam, 320, 180, warm, 80);
                }
                RoeCapture.Render(cam, width, height, Path.Combine(frameDir, $"{f - first:D5}.jpg"), 95);
                if (probe != "" && probe.Split(',').Contains(f.ToString()))
                    foreach (var x in fighters)
                    {
                        var mp = RoeShowcase.FindBone(x.unit.model, "Bip001 Pelvis");
                        var mh = RoeShowcase.FindBone(x.unit.model, "Bip001 Head");
                        var vh = cam.WorldToViewportPoint(mh.position);
                        var vp = cam.WorldToViewportPoint(mp.position);
                        Debug.Log($"[ROE] probe frame {f} {x.info.id} after the render: model pelvis {mp.position:F3} head {mh.position:F3}; " +
                                  $"in the picture (0..1 from the left, 0..1 from the top): head ({vh.x:F3}, {1f - vh.y:F3}) pelvis ({vp.x:F3}, {1f - vp.y:F3})");
                    }
            }
            foreach (var x in fighters)
                x.unit.Stop();
            foreach (var s in spawns)
                if (s.instance != null)
                    Object.DestroyImmediate(s.instance);
            var json = new StringBuilder();
            json.Append($"{{\"fps\":{Fps},\"frames\":{last - first + 1},\"sounds\":[\n  ");
            json.Append(string.Join(",\n  ", first == 0 ? sounds : new List<string>()));
            json.Append("\n]}\n");
            Directory.CreateDirectory(outDir);
            File.WriteAllText(Path.Combine(outDir, "timeline.json"), json.ToString());
            Debug.Log($"[ROE] duel: frames {first}..{last} of {total}, {sounds.Count} sounds, {spawns.Count} effects spawned at targets, written to {outDir}");
        }
    }
}
