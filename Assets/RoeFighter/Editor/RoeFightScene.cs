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
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace RoeFighter.EditorTools
{
    /// <summary>
    /// Builds the playable fight scene: one of the game's battle stages, every fighter of the roster (the
    /// select screen picks two; the others wait put away), the fight logic, camera and HUD.  Saved as
    /// Assets/RoeFighter/Scenes/Fight_&lt;stage&gt;.unity (press Play).
    ///   Build   -executeMethod RoeFighter.EditorTools.RoeFightScene.Build [-roeStage e23_steel_s02] [-roeRoster a08,g04,b10,g05,kas]
    ///           [-roeP1 a08] [-roeP2 g04]   (the two picked when the scene starts)
    ///   Record  -executeMethod RoeFighter.EditorTools.RoeFightScene.Record [-roeStage ...] [-roeOut dir] [-roeSeconds 90]
    ///           [-roeSize 1280x720] [-roeSeed 1] [-roeP1 b10] [-roeP2 g05]   CPU against CPU, frames + timeline.json for tools/make_video.py
    ///           [-roeCamAvoid 0] the old camera; [-roeFrames 0] no frames, only the match and how often the stage hid the two
    ///   Player  -executeMethod RoeFighter.EditorTools.RoeFightScene.BuildPlayer [-roeStage ...] [-roeOut dir]
    /// </summary>
    public static class RoeFightScene
    {
        public const string SceneDir = "Assets/RoeFighter/Scenes";
        static string ProjectDir => Path.GetDirectoryName(Application.dataPath);

        public static string ScenePath(string stage) => $"{SceneDir}/Fight_{stage}.unity";

        // (idle_01, the battle stance, is the rig's stance under everything; as a game clip too since 10-11, so a check can show her
        // stance exactly as the game keys it - RoeClothDemo -roeScript stance)
        static readonly string[] GameClips = { "hurt", "die", "rip", "idle_01", "idle_02", "react_01", "react_02", "skill_01", "skill_02", "skill_03" };
        static readonly Dictionary<string, string> DisplayNames = new Dictionary<string, string>
        {
            { "a08", "INASE" }, { "g04", "LUF" }, { "b10", "KART" }, { "g05", "GODDESS LUF" }, { "m02", "AMANO" }, { "b14", "CRANE KART" },
            { "b14_outfit1", "PINK CRANE KART" },
        };
        // a word on each suit for the select screen (b14: a red-crowned crane sits on her belt; she is CRANE KART on the bars, so
        // a match of the two Karts tells them apart, as GODDESS LUF and LUF; b14_outfit1: the same robe in pink and white, her hair
        // white with pink ends)
        static readonly Dictionary<string, string> Outfits = new Dictionary<string, string>
        {
            { "a08", "Valkyrie" }, { "g04", "Porcelain" }, { "b10", "Agent" }, { "g05", "Goddess" }, { "m02", "Abyss" }, { "b14", "Crane" },
            { "b14_outfit1", "Pink Crane" },
        };

        /// <summary>Who the scene holds (user 10-03: "b10，G05也把nude补全，加入战斗"; 10-04: "doa6中的 Kasumi也加入一个角色" -
        /// kas comes in when her DOA6 files are there, DoaFighter.IsDoa; kas011 is her pirate dress, COS_011, whose skirt and
        /// sleeves are the game's grid cloth; "再加入一个角色吧，vindictus里的fiona，用 PCF_005 这个版本" - fio005, VdfFighter, with
        /// her game's KawaiiPhysics and UFE 2's reactions and specials; 10-05 "另外把剑星里的eve的也加个角色进来，物理能用剑星自己的就用
        /// 自己的" - eve09, SbFighter, Stellar Blade's Eve in her Planet Diving Suit (7th) on her game's own physics; "把不知火舞也加入进角色来" -
        /// mai, Mai Shiranui from DOA6 like Kasumi: her stance and strikes are g04's, her game clips and physics her own; "eve有穿jk的衣服么，
        /// 换成这个角色我看下裙子的物理效果" - eve37, Eve in her pleated-skirt school look, Office Style; 10-11 "把m02也加入战斗，战斗动作也加入进来，作为3个技能使用" -
        /// m02, Amano's second suit (suit_amano_2): her game's three battle skills are her skill 1, skill 2 and super; "把kart
        /// 最新的模型加入到游戏中" - b14, Kart's newest suit (suit_kart_14, the white and blue crane robe, 2026-09-12); 10-17 "要"
        /// (her pink and white recolour as a fighter of its own) - b14_outfit1, tools/roe_outfit.py; "eve中有个粉色的衣服的角色有哪些帮我查一下" ->
        /// "Cooling Suit 加入到角色里" - eve_nikke06, Eve in the NIKKE collaboration's pink Cooling Suit (Alice), SbFighter;
        /// 10-18 "把渡鸦加到角色里，动作用游戏内的" - raven, Stellar Blade's Raven: her suit, her long sword, her game's own moves and
        /// physics, her own nude body under the clothes burst).</summary>
        public const string DefaultRoster = "a08,g04,b10,g05,m02,b14,b14_outfit1,kas,kas011,fio005,eve09,eve37,eve_nikke06,raven,mai";

        public static string PortraitPath(string id) => $"{RoeFighterBuilder.OutDir}/{id}/{id}_portrait.png";
        static readonly HashSet<string> Loops = new HashSet<string> { "guard", "walk", "walk_back", "side_left", "side_right", "run", "rip", "idle_01", "idle_02" };

        [MenuItem("ROE Fighter/Fight/Build fight scene")]
        public static void Build()
        {
            string stage = RoeCapture.Arg("-roeStage", "e23_steel_s02");
            string p1 = RoeCapture.Arg("-roeP1", "a08"), p2 = RoeCapture.Arg("-roeP2", "g04");
            BuildScene(stage, p1, p2);
        }

        /// <summary>The fighters the scene holds: -roeRoster, else DefaultRoster; the two picked are always in it.</summary>
        static List<string> Roster(string p1, string p2)
        {
            var ids = RoeCapture.Arg("-roeRoster", DefaultRoster).Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
            foreach (var id in new[] { p1, p2 })
                if (!ids.Contains(id))
                    ids.Add(id);
            return ids.Where(id => File.Exists(RoeHumanoid.FighterPath(id)) && (DoaFighter.IsDoa(id) || File.Exists(RoeSkillSheet.PathOf(id)))).ToList();
        }

        /// <summary>
        /// A Dead or Alive 6 character (user 10-04: "doa6中的 Kasumi也加入一个角色"; DoaFighter, tools/doa/&lt;id&gt;.json): her model
        /// and her own moves from that game - stance and four strikes as her strike pack, hurt / knockdown / lying / intro /
        /// win / three specials as her "game clips" (the specials played in place, an "attack" slide in her skill sheet
        /// instead) - and her game's own physics data for the cloth setups (FighterRig.physicsData).  No game unit: no
        /// timelines, effects or voices.
        /// </summary>
        static FighterRig MakeDoaFighter(string id)
        {
            var def = DoaFighter.Load(id);
            var root = new GameObject($"Fighter {id}");
            var rig = root.AddComponent<FighterRig>();
            rig.id = id;
            rig.displayName = string.IsNullOrEmpty(def.name) ? id.ToUpperInvariant() : def.name;
            rig.outfit = def.outfit ?? "";
            rig.portrait = AssetDatabase.LoadAssetAtPath<Texture2D>(PortraitPath(id));
            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(RoeHumanoid.FighterPath(id)));
            model.transform.SetParent(root.transform, false);
            rig.animator = model.GetComponent<Animator>();
            rig.animator.runtimeAnimatorController = null;
            // the outfit's pieces that can come off (爆衣, Editor/Burst/<id>.json - Eve's, with her nude base under them)
            rig.burst = RoeBurstBuilder.Apply(model, id);
            foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                smr.forceMatrixRecalculationPerRender = false;

            // her stance and strikes (her strike pack), and from the same converted BVH her game clips; without a pack of her
            // own (Vindictus' Fiona) she fights with the fight's pack (F3) and stands in the definition's stance clip
            rig.strikePack = string.IsNullOrEmpty(def.pack) ? null : OwnStrikes(def.pack);
            rig.stance = DoaStance(def, rig.strikePack);
            if (rig.stance == null)
                Debug.LogWarning($"[ROE] {id}: no stance clip ({def.pack} / {def.stance})");
            string feetNote = def.feet == "bind" ? ", feet " + BindFeet(rig, id) : "";
            var clips = new Dictionary<string, AnimationClip>();
            Directory.CreateDirectory($"{DoaFighter.Dir(id)}/clips");
            foreach (var g in def.game)
            {
                var src = DoaFighter.PackClip(def.pack, g.clip);
                if (src == null)
                {
                    Debug.LogWarning($"[ROE] {id}: no clip {g.clip} for {g.name} in {def.pack}");
                    continue;
                }
                bool skill = g.name.StartsWith("skill_");
                bool loop = Loops.Contains(g.name);
                var clip = RoeMotionPacks.Copy(src, $"{DoaFighter.Dir(id)}/clips/{g.name}.anim", loop, inPlace: skill);
                clips[g.name] = clip;
                rig.clips.Add(new FighterRig.NamedClip { name = g.name, clip = clip, loop = loop, game = true });
            }
            rig.skillSheetJson = DoaFighter.MakeSkillSheet(id, def, clips);
            rig.weaponRenderers = new Renderer[0];
            rig.clothOverGameClips = true;      // her clips key none of her loose bones
            rig.audioSource = root.AddComponent<AudioSource>();
            rig.audioSource.playOnAwake = false;
            rig.audioSource.spatialBlend = 0f;
            Debug.Log($"[ROE] fighter {id} ({def.source}): stance {(rig.stance != null ? rig.stance.name : "none")}, own strikes " +
                      $"{(rig.strikePack != null ? string.Join(" ", rig.strikePack.strikes.Select(m => $"{m.button} {m.name}")) : "none")}, " +
                      $"{rig.clips.Count} clips ({string.Join(" ", rig.clips.Select(c => c.name))}), DOA6 physics {(rig.animator.GetComponent<RoeDoaRig>() != null ? "yes" : "none")}{feetNote}, " +
                      $"clothes burst {(rig.burst != null ? rig.burst.Report() : "none")}");
            return rig;
        }

        /// <summary>
        /// A fighter whose bind pose stands her feet as they should stand (FighterRig.bindFeet; Eve's platform shoes): the foot
        /// and toe bones' bind rotations, and planted feet standing as there - against the direction the toes point along the
        /// floor, at the bind pose's toe and ankle heights (flatFeet, nothing turned).  Returns a note for the log.
        /// </summary>
        static string BindFeet(FighterRig rig, string id)
        {
            var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(RoeHumanoid.FighterPath(id)));
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var an = go.GetComponent<Animator>();
            var feet = new[] { an.GetBoneTransform(HumanBodyBones.LeftFoot), an.GetBoneTransform(HumanBodyBones.RightFoot) };
            var toes = new[] { an.GetBoneTransform(HumanBodyBones.LeftToes), an.GetBoneTransform(HumanBodyBones.RightToes) };
            if (feet.Any(f => f == null) || toes.Any(t => t == null))
            {
                Object.DestroyImmediate(go);
                return "as the stance has them (no foot or toe bones)";
            }
            rig.bindFeet = rig.flatFeet = true;
            rig.bindFoot = feet.Select(f => f.localRotation).ToArray();
            rig.bindToe = toes.Select(t => t.localRotation).ToArray();
            rig.flatToe = rig.bindToe.ToArray();
            rig.flatRest = new Quaternion[2];
            var notes = new List<string>();
            for (int k = 0; k < 2; k++)
            {
                var d = toes[k].position - feet[k].position;
                var ahead = new Vector3(d.x, 0f, d.z);
                if (ahead.sqrMagnitude < 1e-6f)
                    ahead = go.transform.forward;
                rig.flatRest[k] = Quaternion.Inverse(Quaternion.LookRotation(ahead.normalized, Vector3.up)) * feet[k].rotation;
                notes.Add($"{(k == 0 ? "L" : "R")} pitch {Mathf.Atan2(-d.y, ahead.magnitude) * Mathf.Rad2Deg:F0} deg");
            }
            rig.flatAxis = new[] { Vector3.right, Vector3.right };
            rig.flatPitch = new[] { 0f, 0f };
            rig.flatSoles = new Vector2((toes[0].position.y + toes[1].position.y) * 0.5f, (feet[0].position.y + feet[1].position.y) * 0.5f);
            Object.DestroyImmediate(go);
            return $"as the bind pose has them ({string.Join(", ", notes)}, toe bone {100f * rig.flatSoles.x:F1} cm and ankle {100f * rig.flatSoles.y:F1} cm up, ankle held)";
        }

        /// <summary>The clip a character from another game stands in: her own pack's guard, else the definition's stance
        /// ("pack:role" or a clip's asset path).</summary>
        internal static AnimationClip DoaStance(DoaFighter.Definition def, MotionPack own = null)
        {
            var pack = own ?? (string.IsNullOrEmpty(def.pack) ? null : RoeMotionPacks.Load(def.pack));
            if (pack != null && pack.Get("guard") != null)
                return pack.Get("guard");
            if (string.IsNullOrEmpty(def.stance))
                return null;
            int colon = def.stance.IndexOf(':');
            return !def.stance.StartsWith("Assets/") && colon > 0
                ? RoeMotionPacks.Load(def.stance.Substring(0, colon))?.Get(def.stance.Substring(colon + 1))
                : DoaFighter.PackClip(def.pack, def.stance);
        }

        public static string BuildScene(string stage, string p1, string p2)
        {
            ShaderUtil.allowAsyncCompilation = false;
            var ids = Roster(p1, p2);
            // the select screen's cards: rendered in the studio first (a scene of their own)
            Portraits(ids);
            var info = RoeStage.Open(stage);
            Debug.Log("[ROE] " + info.report);
            var sight = new RoeStage.Sight();
            var layout = RoeStage.FindLayout(info, sight, false);
            var (centre, radius) = SurveyArena(stage, layout.centre) ?? BestArena(sight, layout.centre);
            var room = MeasureRoom(sight, centre, radius, stage);
            sight.Dispose();
            Debug.Log($"[ROE] fight scene {stage}: arena centre {centre} (fight line centre {layout.centre}), free radius {radius:F1} m, " +
                      $"fighters along {layout.axis}, camera side {layout.normal}");

            var manifest = RoeManifest.Load();
            var roster = ids.Select(id => DoaFighter.IsDoa(id) ? MakeDoaFighter(id) : MakeFighter(manifest.characters.First(c => c.id == id))).ToArray();
            int pick1 = Mathf.Max(0, ids.IndexOf(p1)), pick2 = Mathf.Max(0, ids.IndexOf(p2));
            var rigs = new[] { roster[pick1], roster[pick2] };
            // the ones not in the match wait put away (the select screen brings them out)
            foreach (var r in roster)
                r.gameObject.SetActive(rigs.Contains(r));

            // camera + post-processing
            var camGo = new GameObject("Fight Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 600f;
            cam.fieldOfView = 32f;
            cam.allowHDR = true;
            cam.allowMSAA = true;
            camGo.tag = "MainCamera";
            camGo.AddComponent<AudioListener>();
            var data = camGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.renderShadows = true;
            data.requiresDepthTexture = true;
            data.requiresColorTexture = true;       // the refraction effects read the picture behind them
            camGo.transform.position = centre + layout.normal * 6f + Vector3.up * 1.5f;
            camGo.transform.LookAt(centre + Vector3.up, Vector3.up);
            var volGo = new GameObject("Fight Volume");
            var volume = volGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(RoeProjectSetup.SettingsDir + "/RoeStudioVolume.asset");

            var hudGo = new GameObject("Fight HUD", typeof(RectTransform));
            var hud = hudGo.AddComponent<FightHud>();

            var gameGo = new GameObject("Fight");
            var game = gameGo.AddComponent<FightGame>();
            game.rigs = rigs;
            game.roster = roster;
            game.pick = new[] { pick1, pick2 };
            game.centre = centre;
            game.axis = layout.axis;
            game.arenaRadius = Mathf.Clamp(radius, 3f, 12f);
            game.room = room;
            game.cam = cam;
            game.hud = hud;
            // the basic moves: every motion pack built so far; the fight starts with -roeMotions (default bandai1)
            if (!File.Exists(RoeMotionPacks.PackPath("bandai1")))
                RoeMotionPacks.BuildBandai();
            game.motionPacks = RoeMotionPacks.All();
            string wanted = RoeCapture.Arg("-roeMotions", "bandai1");
            game.motionPack = Mathf.Max(0, game.motionPacks.FindIndex(p => p.name == wanted));
            foreach (var rig in roster)
                rig.UseMotions(game.Pack);
            game.moves = game.Pack.CopyStrikes();
            game.specials = new List<Special>
            {
                new Special { name = "skill1", clip = "skill_01", action = "skill1", damage = 150, range = 6f },
                new Special { name = "skill2", clip = "skill_02", action = "skill2", damage = 190, range = 6f },
                new Special { name = "skill3", clip = "skill_03", action = "skill3", damage = 360, meterCost = 100f, range = 7f },
            };
            (game.effects, game.hitEffect) = Effects(roster);
            // the strikes' hit effects (user 10-05: not g04's skill hit for everything): UFE 2's hit particles by weight when the
            // project has them (RoeHitFx, tools/ufe_extract.py); -roeHitFx game keeps the old spark for every strike
            // Magica Cloth 2's own presets, when the plugin is installed: the "magica_full" setup reads them (RoeMagicaPresets)
            const string magicaPresets = "Assets/MagicaCloth2/Res/Preset";
            if (AssetDatabase.IsValidFolder(magicaPresets))
            {
                var holder = game.gameObject.GetComponent<RoeMagicaPresets>() ?? game.gameObject.AddComponent<RoeMagicaPresets>();
                holder.presets = AssetDatabase.FindAssets("t:TextAsset", new[] { magicaPresets })
                    .Select(g => AssetDatabase.LoadAssetAtPath<TextAsset>(AssetDatabase.GUIDToAssetPath(g))).Where(t => t != null).ToArray();
            }
            game.hitLight = game.hitMedium = game.hitHeavy = game.hitBlock = null;
            var ufeFx = RoeCapture.Arg("-roeHitFx", "ufe") == "ufe" ? RoeHitFx.UfePrefabs() : new List<FightGame.NamedPrefab>();
            if (ufeFx.Count > 0)
            {
                RoeHitFx.Prepare();
                game.effects.AddRange(ufeFx);
                string Has(string n) => ufeFx.Any(x => x.name == n) ? n : null;
                game.hitLight = Has("ufe_light");
                game.hitMedium = Has("ufe_medium");
                game.hitHeavy = Has("ufe_heavy");
                game.hitBlock = Has("ufe_block");
            }
            foreach (var m in game.moves)
                m.hitSound = rigs[0].sounds.Any(s => s.name == "hit") ? "hit" : null;
            // the stage's own battle music
            var musicPath = Directory.Exists($"Assets/ROE/stages/{stage}")
                ? Directory.GetFiles($"Assets/ROE/stages/{stage}", "*.ogg", SearchOption.AllDirectories)
                    .Select(p => p.Replace('\\', '/')).OrderBy(p => Path.GetFileName(p).ToLowerInvariant().Contains("battle") ? 0 : 1).FirstOrDefault()
                : null;
            game.music = musicPath != null ? AssetDatabase.LoadAssetAtPath<AudioClip>(musicPath) : null;

            Directory.CreateDirectory(SceneDir);
            var scene = SceneManager_Active();
            string path = ScenePath(stage);
            EditorSceneManager.SaveScene(scene, path, false);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(path, true) };
            Debug.Log($"[ROE] fight scene saved: {path} (roster {string.Join(" ", ids)}, picked {p1} {p2}; {game.moves.Count} strikes, {game.specials.Count} specials, {game.effects.Count} effect prefabs, hit spark {game.hitEffect}, music {(musicPath ?? "none")}; " +
                      $"motion packs {string.Join(", ", game.motionPacks.Select(p => p.name))}, starting with {game.Pack.name})");
            return path;
        }

        static UnityEngine.SceneManagement.Scene SceneManager_Active() => UnityEngine.SceneManagement.SceneManager.GetActiveScene();

        /// <summary>
        /// A picture per fighter for the select screen's card: upper body and face, standing, in the studio
        /// (Generated/&lt;id&gt;/&lt;id&gt;_portrait.png).  Opens a scene of its own: call it before the stage.
        /// </summary>
        public static void Portraits(IEnumerable<string> ids)
        {
            ShaderUtil.allowAsyncCompilation = false;
            var studio = RoeStudio.Build();
            foreach (var id in ids)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RoeHumanoid.FighterPath(id));
                if (prefab == null)
                    continue;
                // a DOA6 character stands in her own stance (the guard of her strike pack), or the pose her definition names
                var def = DoaFighter.IsDoa(id) ? DoaFighter.Load(id) : null;
                var stance = def != null ? DoaStance(def) : RoeHumanoidClips.Standing(id, out _);
                float at = 0f;
                if (!string.IsNullOrEmpty(def?.portrait))
                {
                    var parts = def.portrait.Split('@');
                    var clip = DoaFighter.PackClip(def.pack, parts[0]);
                    if (clip != null)
                    {
                        stance = clip;
                        at = parts.Length > 1 ? float.Parse(parts[1], CultureInfo.InvariantCulture) : 0f;
                    }
                }
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                var bones = RoeHumanoid.MapBones(go);
                var head = bones.TryGetValue("Head", out var hb) ? hb : null;
                // the bind pose's facing and head, for a portrait pose: the camera faces her face, wherever her toes point
                var bindForward = RoeShowcase.Forward(go);
                var bindHead = head != null ? head.rotation : Quaternion.identity;
                if (stance != null)
                    RoeCapture.Pose(go, stance, at);
                go.GetComponent<RoeDoaRig>()?.RebuildSurfaces();     // a DOA6 skirt on the pose
                foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    smr.forceMatrixRecalculationPerRender = true;
                var forward = RoeShowcase.Forward(go);
                if (!string.IsNullOrEmpty(def?.portrait) && head != null)
                {
                    var face = Vector3.ProjectOnPlane(head.rotation * Quaternion.Inverse(bindHead) * bindForward, Vector3.up);
                    if (face.sqrMagnitude > 1e-6f)
                        forward = face.normalized;
                }
                var chest = bones.TryGetValue("UpperChest", out var cb) ? cb : bones.TryGetValue("Chest", out cb) ? cb : null;
                var target = head != null && chest != null ? Vector3.Lerp(chest.position, head.position, 0.75f) : RoeShowcase.WorldBounds(go).center;
                studio.LightFrom(forward);
                // no depth of field: the studio's volume profile is the fight scene's too (a focus left in it blurred the match)
                studio.Aim(target, forward, 14f, 3f, 1.45f, 24f);
                string file = Path.Combine(ProjectDir, PortraitPath(id));
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                RoeCapture.Render(studio.camera, 120, 160, file);
                RoeCapture.Render(studio.camera, 120, 160, file);
                RoeCapture.Render(studio.camera, 360, 480, file);
                RoeCapture.EndPosing();
                Object.DestroyImmediate(go);
                AssetDatabase.ImportAsset(PortraitPath(id), ImportAssetOptions.ForceUpdate);
            }
        }

        [Serializable]
        class SurveyEntry
        {
            public string scene;
            public float[] centre;
            public float r_best;
            public float[] best_offset;
        }

        /// <summary>
        /// The arena from tools/stage_survey.py (every stage mesh's triangles in a top-down grid: the
        /// most reliable measure, it also sees thin fences).  Null if the stage was not surveyed.
        /// </summary>
        static (Vector3, float)? SurveyArena(string stage, Vector3 fightCentre)
        {
            string dir = Path.Combine(ProjectDir, "_work", "stage_survey");
            string names = Path.Combine(dir, "stages.json"), survey = Path.Combine(dir, "survey.json");
            if (!File.Exists(names) || !File.Exists(survey))
                return null;
            var scene = MiniJson(File.ReadAllText(names), stage);
            if (scene == null)
                return null;
            string text = File.ReadAllText(survey);
            int at = text.IndexOf($"\"{scene}\": {{", StringComparison.Ordinal);
            if (at < 0)
                return null;
            int end = text.IndexOf('}', at);
            var entry = JsonUtility.FromJson<SurveyEntry>(text.Substring(text.IndexOf('{', at), end - text.IndexOf('{', at) + 1));
            var c = new Vector3(entry.centre[0] + entry.best_offset[0], fightCentre.y, entry.centre[2] + entry.best_offset[1]);
            return (c, entry.r_best);
        }

        static string MiniJson(string text, string key)
        {
            int at = text.IndexOf($"\"{key}\"", StringComparison.Ordinal);
            if (at < 0)
                return null;
            int q1 = text.IndexOf('"', text.IndexOf(':', at) + 1);
            int q2 = text.IndexOf('"', q1 + 1);
            return text.Substring(q1 + 1, q2 - q1 - 1);
        }

        /// <summary>The largest circle of free, flat floor near the fight centre (ray tests on the stage meshes).</summary>
        static (Vector3 centre, float radius) BestArena(RoeStage.Sight sight, Vector3 start)
        {
            float Radius(Vector3 c, int directions, float max)
            {
                var radii = new List<float>();
                for (int k = 0; k < directions; k++)
                {
                    var dir = Quaternion.Euler(0f, k * 360f / directions, 0f) * Vector3.forward;
                    float free = 0f;
                    for (float d = 0.25f; d <= max; d += 0.25f)
                    {
                        var p = c + dir * d;
                        if (!sight.Ground(p, 2.2f, out float y) || Mathf.Abs(y - c.y) > 0.2f)
                            break;
                        // fences and railings: thin bars the downward rays slip past
                        if (sight.Blocked(c + Vector3.up * 1.0f, p + Vector3.up * 1.0f) || sight.Blocked(c + Vector3.up * 0.5f, p + Vector3.up * 0.5f))
                            break;
                        free = d;
                    }
                    radii.Add(free);
                }
                radii.Sort();
                return radii[Mathf.Min(2, radii.Count - 1)];    // two thin posts do not count
            }
            var best = (centre: start, radius: Radius(start, 24, 12f));
            for (float x = -3f; x <= 3.01f; x += 0.5f)
                for (float z = -3f; z <= 3.01f; z += 0.5f)
                {
                    var c = start + new Vector3(x, 0f, z);
                    if (!sight.Ground(c, 2.2f, out float y) || Mathf.Abs(y - start.y) > 0.2f)
                        continue;
                    c.y = y;
                    float r = Radius(c, 16, 12f);
                    if (r > best.radius + 0.25f)
                        best = (c, Radius(c, 24, 12f));
                }
            return best;
        }

        /// <summary>
        /// The stage's room for the fight camera (FightGame.room): a grid of 0.25 m cells 48 m across around the arena;
        /// a cell is blocked when solid stage geometry passes through it between 0.3 and 2.6 m above the arena floor
        /// (the camera's and the fighters' heights: walls, props, a ring's ropes).  A map of it goes to
        /// _work/camera_room/&lt;stage&gt;.png (red blocked, grey free - darker nearer the stage, green the arena).
        /// </summary>
        public static CameraRoom MeasureRoom(RoeStage.Sight sight, Vector3 centre, float arenaRadius, string stage)
        {
            var t0 = DateTime.Now;
            var room = new CameraRoom { cell = 0.25f, size = 192 };
            float half = room.size * room.cell * 0.5f;
            room.origin = new Vector3(centre.x - half, centre.y, centre.z - half);
            int n = room.size;
            var blocked = new bool[n * n];
            var box = new Vector3(room.cell * 0.5f, (room.bandHigh - room.bandLow) * 0.5f, room.cell * 0.5f);
            float y = centre.y + (room.bandLow + room.bandHigh) * 0.5f;
            int count = 0;
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                    if (sight.Occupied(new Vector3(room.origin.x + (x + 0.5f) * room.cell, y, room.origin.z + (z + 0.5f) * room.cell), box))
                    {
                        blocked[x + z * n] = true;
                        count++;
                    }
            room.SetBlocked(blocked);

            const int scale = 2;
            var map = new Texture2D(n * scale, n * scale, TextureFormat.RGB24, false);
            var pixels = new Color32[n * n * scale * scale];
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    var p = new Vector3(room.origin.x + (x + 0.5f) * room.cell, 0f, room.origin.z + (z + 0.5f) * room.cell);
                    float c = room.clearance[x + z * n] * CameraRoom.Unit;
                    float r = new Vector2(p.x - centre.x, p.z - centre.z).magnitude;
                    byte grey = (byte)Mathf.Lerp(90f, 230f, Mathf.Clamp01(c / 3f));
                    var colour = blocked[x + z * n] ? new Color32(200, 30, 30, 255)
                        : Mathf.Abs(r - arenaRadius) < room.cell * 0.6f ? new Color32(40, 200, 60, 255)
                        : new Color32(grey, grey, grey, 255);
                    for (int dz = 0; dz < scale; dz++)
                        for (int dx = 0; dx < scale; dx++)
                            pixels[(x * scale + dx) + (z * scale + dz) * n * scale] = colour;
                }
            map.SetPixels32(pixels);
            map.Apply();
            string dir = Path.Combine(ProjectDir, "_work", "camera_room");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, stage + ".png"), map.EncodeToPNG());
            Object.DestroyImmediate(map);
            Debug.Log($"[ROE] camera room {stage}: {n} x {n} cells of {room.cell} m around {centre}, {count} blocked " +
                      $"({room.bandLow}-{room.bandHigh} m over the floor; {sight.SeeThrough} see-through renderers not counted), " +
                      $"stage nearest the arena centre {room.Clearance(centre):F2} m (arena radius {arenaRadius:F1}), " +
                      $"{(DateTime.Now - t0).TotalSeconds:F1} s");
            return room;
        }

        /// <summary>One fighter: root with the rig component, the game's unit prefab (skills, effects), our humanoid model.</summary>
        static FighterRig MakeFighter(RoeManifest.Character c)
        {
            var sheet = RoeSkillSheet.FromJson(File.ReadAllText(RoeSkillSheet.PathOf(c.id)));
            var root = new GameObject($"Fighter {c.id}");
            var rig = root.AddComponent<FighterRig>();
            rig.id = c.id;
            rig.displayName = DisplayNames.TryGetValue(c.id, out var shown) ? shown : c.id.ToUpperInvariant();
            rig.outfit = Outfits.TryGetValue(c.id, out var outfit) ? outfit : "";
            rig.portrait = AssetDatabase.LoadAssetAtPath<Texture2D>(PortraitPath(c.id));

            // the game's unit: timelines + effect anchors on a hidden low-detail body
            var unitPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RoeUnit.RigPath(sheet));
            var unit = (GameObject)PrefabUtility.InstantiatePrefab(unitPrefab);
            PrefabUtility.UnpackPrefabInstance(unit, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            unit.name = sheet.unit;
            unit.transform.SetParent(root.transform, false);
            unit.transform.localPosition = Vector3.zero;
            unit.transform.localRotation = Quaternion.identity;
            int stripped = 0;
            foreach (var t in unit.GetComponentsInChildren<Transform>(true))
                stripped += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
            foreach (var s in unit.GetComponentsInChildren<AudioSource>(true))
                s.enabled = false;
            foreach (var l in unit.GetComponentsInChildren<AudioListener>(true))
                Object.DestroyImmediate(l);
            foreach (var cam in unit.GetComponentsInChildren<Camera>(true))
                cam.enabled = false;
            var ghost = unit.transform.Find(sheet.model);
            string[] characterShaders = { "ROE/Character", "ROE/Skin", "ROE/Hair", "ROE/Eye", "ROE/Eyebrow" };
            int hidden = 0;
            foreach (var r in ghost.GetComponentsInChildren<Renderer>(true))
                if (r.sharedMaterials.Any(m => m != null && m.shader != null && characterShaders.Contains(m.shader.name)))
                {
                    r.enabled = false;
                    hidden++;
                }
            foreach (var a in unit.GetComponentsInChildren<Animator>(true))
            {
                a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                a.applyRootMotion = false;
            }
            foreach (var d in unit.GetComponentsInChildren<PlayableDirector>(true))
            {
                d.playOnAwake = false;
                d.timeUpdateMode = DirectorUpdateMode.Manual;
                rig.directors.Add(new FighterRig.NamedDirector { action = d.gameObject.name.ToLowerInvariant(), director = d });
            }
            rig.unitRoot = unit.transform;

            // our model where the game's body stands
            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(RoeHumanoid.FighterPath(c.id)));
            model.transform.SetParent(ghost.parent, false);
            model.transform.localPosition = ghost.localPosition;
            model.transform.localRotation = ghost.localRotation;
            model.transform.localScale = ghost.localScale;
            rig.animator = model.GetComponent<Animator>();
            rig.animator.runtimeAnimatorController = null;
            // the outfit's pieces that can come off (爆衣): each its own renderer (Editor/Burst/<id>.json)
            rig.burst = RoeBurstBuilder.Apply(model, c.id);
            foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                smr.forceMatrixRecalculationPerRender = false;

            // clips: the game's own here; the basic moves come from a motion pack (BuildScene, FightGame.Setup).  The
            // stance is the battle stance, or a showcase clip that stands when that one floats (g05 sits in the air)
            rig.stance = RoeHumanoidClips.Standing(c.id, out var standing, out float hover);
            // ...and when the battle stance floats, the game's battle clips float as high (g05 23 cm): the fight brings
            // them down to the floor, the skills rise to the game's height (FighterRig.gameHover).  -roeHover g05=0 leaves
            // every clip as high as the game has it; -roeSkillsHover 0 brings the skills down too.
            if (Setting(Table("-roeHover", ""), c.id, out var hoverArg))
                hover = float.Parse(hoverArg, CultureInfo.InvariantCulture);
            rig.gameHover = hover;
            bool skillsHover = RoeCapture.Arg("-roeSkillsHover", "1") != "0";
            foreach (var name in GameClips)
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(RoeHumanoidClips.ClipPath(c.id, name));
                if (clip != null)
                    rig.clips.Add(new FighterRig.NamedClip
                    {
                        name = name, clip = clip, loop = Loops.Contains(name), game = true, hovers = skillsHover && name.StartsWith("skill_"),
                    });
            }
            if (rig.gameHover > 0f)
                standing += $"; the game's clips hover {rig.gameHover * 100f:F0} cm: reactions come down to the floor, " +
                            (skillsHover ? "skills rise to it" : "skills too");
            rig.skillSheetJson = AssetDatabase.LoadAssetAtPath<TextAsset>(RoeSkillSheet.PathOf(c.id));
            // her game's own Magica Cloth 2 - its lobby prefab's, read by tools/roe_mc2_dump.py --fighter <id> - for the "roe"
            // cloth setup (user 10-11 "就完全遵从ROE本身的物理的角度再看看"); without it that setup uses the game's numbers by hand
            rig.gameMagica = AssetDatabase.LoadAssetAtPath<TextAsset>($"Assets/ROE/{c.id}/magica_lobby.json");
            if (rig.gameMagica != null)
                standing += "; her game's Magica Cloth 2 set-up read (magica_lobby.json)";

            // weapons only show during the game's own clips (skills, intro, victory): in the basic moves a08's
            // sword would swing around the hips, and g04 throws her punches and kicks without the giant fan
            // (user 10-02: "小招的时候就不显示扇子了，大招再用扇子")
            rig.weaponRenderers = model.GetComponentsInChildren<Renderer>(true)
                .Where(r => r.sharedMaterials.Any(m => m != null && m.name.StartsWith("wp_"))).ToArray();
            // her own strikes instead of the motion pack's (user 10-03: g04 strikes like Mai Shiranui, from DOA6; 10-05 "inase用
            // mecanim bot的动作": a08 strikes like UFE 2's Mecanim Bot - a08=ufe_bot_inase; since 10-06 "给inase拿着长剑的找一些普通
            // 攻击" her greatsword's, a08_sword; 10-06 "一个拿斧子和枪的上衣是红色的卡地亚": b10 with her axe, b10_axe; 10-17 b14 with
            // her long sword, b14_sword;
            // -roeOwnStrikes g04=doa6_mai,a08=...), and the size of her weapon (user 10-03: a smaller fan; -roeWeaponScale g04=0.7)
            // her planted thighs turned in or out against the pelvis no further than her skin was weighted for (FighterRig.rollFrom
            // : rollKeep : rollFoot; user 10-06 "大腿根的权重还是不对呀": Kart's axe strikes turn her bent thigh in 45-60 degrees,
            // her own game clips at most 36, and her leg stood like a W with the buttock hanging onto it; -roeLegRoll b10=15:0.3:0)
            if (Setting(Table("-roeLegRoll", "b10=15:0.3:0"), c.id, out var legRoll))
            {
                var p = legRoll.Split(':');
                rig.rollFrom = float.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture);
                rig.rollKeep = p.Length > 1 ? float.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture) : 0.3f;
                rig.rollFoot = p.Length > 2 ? int.Parse(p[2]) : 0;
            }
            if (Setting(Table("-roeOwnStrikes", "g04=doa6_mai,g05=doa6_mai,a08=a08_sword,b10=b10_axe,b14=b14_sword"), c.id, out var own))
            {
                rig.strikePack = OwnStrikes(own);
                // strikes with the weapon in her hand need her grip on the fighter prefab (RoeWeaponGrip)
                if (rig.strikePack != null && rig.strikePack.grip && model.GetComponentInChildren<RoeGrips>(true) == null)
                    Debug.LogWarning($"[ROE] {c.id}: her strikes ({own}) hold her weapon, but {RoeHumanoid.FighterPath(c.id)} has no RoeGrips - " +
                                     "run RoeFighter.EditorTools.RoeWeaponGrip.Build");
            }
            // her guard between moves (user 10-11 "luf还原ROE里的姿势吧"; 10-17 "inase的默认动作能换成ROE中的inase的么" - a08 too, her
            // greatsword planted as there): "stance" = her game's battle stance (rig.stance, its
            // humanoid curves; FighterRig.ownGuard) instead of her strike pack's (Mai Shiranui's crouch) or the motion pack's;
            // -roeGuard g04=stance,g05=stance (g05's battle stance floats: rig.stance is her showcase stand there)
            string guardNote = "guard from the motion pack" + (rig.strikePack != null && rig.strikePack.clips.Any(x => x.role == "guard" && x.clip != null) ? $" / {rig.strikePack.name}" : "");
            if (Setting(Table("-roeGuard", "g04=stance,a08=stance"), c.id, out var guardArg) && guardArg == "stance" && rig.stance != null)
            {
                rig.ownGuard = RoeHumanoidClips.HumanOnly(c.id, rig.stance, "guard_stance");
                guardNote = $"guard = her game stance {rig.stance.name} (humanoid curves, -roeGuard {c.id}=stance)";
            }
            if (Setting(Table("-roeWeaponScale", "g04=0.7"), c.id, out var size))
                rig.weaponScale = float.Parse(size, CultureInfo.InvariantCulture);
            // heels or flat feet (user 10-03: "有的角色是有高跟鞋的，有的角色是没有的，得区分一下"): measured on her own standing
            // clips (RoeFeet); -roeFeet g05=flat,b10=heels overrides
            var feetKind = RoeFeet.Measure(c.id);
            if (Setting(Table("-roeFeet", ""), c.id, out var feetArg))
                feetKind.heels = feetArg != "flat";
            rig.flatFeet = !feetKind.heels;
            if (rig.flatFeet)
            {
                rig.flatRest = feetKind.rest;
                rig.flatToe = feetKind.toe;
                rig.flatAxis = feetKind.pitchAxis;
                rig.flatPitch = feetKind.pitchFix;
                rig.flatSoles = new Vector2(feetKind.toeSole, feetKind.ankleSole);
            }
            string feetNote = $", feet {(rig.flatFeet ? "flat" : "in heels")}{(feetArg != null ? $" (-roeFeet {feetArg})" : "")} - {feetKind.note}";

            // sounds the skills use, by clip name; the game's voice lines in Japanese
            var names = new HashSet<string>();
            foreach (var a in sheet.actions)
            {
                foreach (var s in a.sounds)
                    names.Add(s.clip);
                foreach (var v in a.voices)
                    foreach (var vc in v.clips.Where(x => x.language == "2" || x.language == "1"))
                        names.Add(vc.clip);
            }
            foreach (var name in names.Where(n => !string.IsNullOrEmpty(n)))
            {
                var path = FindAudio(c, name);
                if (path != null)
                    rig.sounds.Add(new FighterRig.NamedSound { name = name, clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path) });
            }
            // strikes sound like the shortest of the skills' hit sounds (the first one found was a heavy one)
            var hit = c.sfx.Select(p => (p, n: Path.GetFileNameWithoutExtension(p).ToLowerInvariant()))
                .Where(x => x.n.Contains("hit") || x.n.Contains("hurt"))
                .OrderBy(x => AssetDatabase.LoadAssetAtPath<AudioClip>(x.p) is AudioClip ac ? ac.length : 99f)
                .FirstOrDefault();
            if (hit.p != null)
                rig.sounds.Add(new FighterRig.NamedSound { name = "hit", clip = AssetDatabase.LoadAssetAtPath<AudioClip>(hit.p) });
            string showNote = Showcase(rig, c.id);
            rig.audioSource = root.AddComponent<AudioSource>();
            rig.audioSource.playOnAwake = false;
            rig.audioSource.spatialBlend = 0f;

            Debug.Log($"[ROE] fighter {c.id}: {standing}; unit {sheet.unit} ({stripped} game scripts stripped, {hidden} ghost renderers hidden, " +
                      $"{rig.directors.Count} timelines: {string.Join(" ", rig.directors.Select(d => d.action))}), " +
                      $"{rig.clips.Count} clips, {rig.weaponRenderers.Length} weapon renderers, {rig.sounds.Count} sounds, " +
                      $"clothes burst {(rig.burst != null ? rig.burst.Report() : "none")}, {showNote}, " +
                      $"own strikes {(rig.strikePack != null ? $"{rig.strikePack.name} ({string.Join(" ", rig.strikePack.strikes.Select(m => $"{m.button} {m.name}"))})" : "none")}, " +
                      $"{guardNote}, weapon size {rig.weaponScale:F2}{feetNote} " +
                      $"(hit sound {(hit.p != null ? Path.GetFileName(hit.p) : "none")}; sfx: {string.Join(" ", c.sfx.Take(12).Select(Path.GetFileNameWithoutExtension))})");
            return rig;
        }

        /// <summary>"g04=doa6_mai,a08=x" (the argument, else the fallback) as a table.</summary>
        [Serializable]
        class ShowcaseSpec
        {
            public string id;
            public ShowcaseEntry[] reactions;
        }

        [Serializable]
        class ShowcaseEntry
        {
            public string timeline, clip, voice, audio;
            public float length, voiceAt;
        }

        /// <summary>
        /// Her outfit's showcase reactions - ROE's menus: idle02_react01 / idle02_react02, a react clip and a voice line each
        /// (tools/roe_showcase.py -> tools/roe/&lt;id&gt;_showcase.json, the lines extracted to Assets/ROE/&lt;id&gt;/showcase_voice)
        /// - for the select screen (user 10-06: "选人环节也做个动画，ROE里面标准的动画").  Returns a note for the log.
        /// </summary>
        static string Showcase(FighterRig rig, string id)
        {
            rig.showcase.Clear();
            string path = Path.Combine(ProjectDir, "tools", "roe", id + "_showcase.json");
            if (!File.Exists(path))
                return "no showcase";
            var spec = JsonUtility.FromJson<ShowcaseSpec>(File.ReadAllText(path));
            var notes = new List<string>();
            foreach (var r in spec.reactions ?? new ShowcaseEntry[0])
            {
                // (her clips by name: FighterRig.Has reads an index Init builds when the game runs)
                if (!rig.clips.Any(x => x.name == r.clip && x.clip != null))
                {
                    notes.Add($"{r.timeline}: no clip {r.clip}");
                    continue;
                }
                string sound = "";
                var audio = string.IsNullOrEmpty(r.audio) ? null : AssetDatabase.LoadAssetAtPath<AudioClip>(r.audio);
                if (audio != null)
                {
                    sound = "showcase " + r.timeline;
                    rig.sounds.Add(new FighterRig.NamedSound { name = sound, clip = audio });
                }
                rig.showcase.Add(new FighterRig.ShowReaction { timeline = r.timeline, clip = r.clip, sound = sound, voiceAt = r.voiceAt });
                notes.Add($"{r.timeline} {r.clip}{(audio != null ? $" + {audio.name} at {r.voiceAt:F2} s" : $" (no voice: {r.audio})")}");
            }
            return "showcase " + string.Join(", ", notes);
        }

        /// <summary>A fighter's entry in a per-fighter table, else her suit's when she is a recolour of it (tools/roe_outfit.py:
        /// b14_outfit1 fights with b14's sword strikes).</summary>
        static bool Setting(Dictionary<string, string> table, string id, out string value) =>
            table.TryGetValue(id, out value) || table.TryGetValue(RoeManifest.SuitOf(id), out value);

        static Dictionary<string, string> Table(string arg, string fallback) =>
            RoeCapture.Arg(arg, fallback).Split(',').Select(p => p.Split('='))
                .Where(p => p.Length == 2 && p[0].Trim().Length > 0 && p[1].Trim().Length > 0)
                .ToDictionary(p => p[0].Trim(), p => p[1].Trim());

        /// <summary>A strike pack by name, imported from tools/motionpacks/&lt;name&gt;.json if it was not built yet.</summary>
        static MotionPack OwnStrikes(string name)
        {
            var pack = RoeMotionPacks.Load(name);
            string spec = Path.Combine(ProjectDir, "tools", "motionpacks", name + ".json");
            if (pack == null && File.Exists(spec))
                pack = RoeMotionPacks.ImportSpec(JsonUtility.FromJson<RoeMotionPacks.Spec>(File.ReadAllText(spec)));
            if (pack == null)
                Debug.LogWarning($"[ROE] own strikes '{name}': no pack and no tools/motionpacks/{name}.json");
            return pack;
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

        /// <summary>Every effect the skills spawn on a target, and a small one for the strikes.</summary>
        static (List<FightGame.NamedPrefab> list, string spark) Effects(FighterRig[] rigs)
        {
            var list = new List<FightGame.NamedPrefab>();
            foreach (var rig in rigs)
            {
                var sheet = RoeSkillSheet.FromJson(rig.skillSheetJson.text);
                foreach (var a in sheet.actions)
                    foreach (var s in a.spawns)
                    {
                        if (list.Any(x => x.name == s.prefab))
                            continue;
                        foreach (var guid in AssetDatabase.FindAssets($"{s.prefab} t:Prefab", new[] { "Assets/ROE/skills" }))
                        {
                            var p = AssetDatabase.GUIDToAssetPath(guid);
                            if (Path.GetFileNameWithoutExtension(p) == s.prefab)
                            {
                                list.Add(new FightGame.NamedPrefab { name = s.prefab, prefab = AssetDatabase.LoadAssetAtPath<GameObject>(p) });
                                break;
                            }
                        }
                    }
            }
            // the smallest hit effect of skill 1 serves as the spark for strikes
            var spark = list.Where(x => x.name.ToLowerInvariant().Contains("hit"))
                .OrderBy(x => x.prefab.GetComponentsInChildren<ParticleSystem>(true).Length).FirstOrDefault();
            return (list, spark?.name);
        }

        // ---- filming a CPU match

        [MenuItem("ROE Fighter/Fight/Record CPU match")]
        public static void Record()
        {
            string stage = RoeCapture.Arg("-roeStage", "e23_steel_s02");
            string outDir = RoeCapture.Arg("-roeOut", Path.Combine(ProjectDir, "_work", "fight"));
            float seconds = float.Parse(RoeCapture.Arg("-roeSeconds", "100"), CultureInfo.InvariantCulture);
            var size = RoeCapture.Arg("-roeSize", "1280x720").Split('x');
            int width = int.Parse(size[0]), height = int.Parse(size[1]);
            int seed = int.Parse(RoeCapture.Arg("-roeSeed", "1"));
            int every = int.Parse(RoeCapture.Arg("-roeEvery", "2"));      // 60 steps per second, every 2nd filmed = 30 fps
            bool film = RoeCapture.Arg("-roeFrames", "1") != "0";            // 0: only the match and the camera's numbers
            RoeClothesBurst.Enabled = RoeCapture.Arg("-roeBurst", "1") != "0";

            ShaderUtil.allowAsyncCompilation = false;
            string path = ScenePath(stage);
            if (!File.Exists(path))
                BuildScene(stage, RoeCapture.Arg("-roeP1", "a08"), RoeCapture.Arg("-roeP2", "g04"));
            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var game = Object.FindFirstObjectByType<FightGame>();
            game.PickIds(RoeCapture.Arg("-roeP1", null), RoeCapture.Arg("-roeP2", null));
            game.cpu = new[] { true, true };
            game.seed = seed;
            game.hud.drawnByCamera = true;
            foreach (var rig in game.roster.Length > 0 ? game.roster : game.rigs)
                foreach (var smr in rig.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    smr.forceMatrixRecalculationPerRender = true;     // many renders inside one editor update
            string pack = RoeCapture.Arg("-roeMotions", null);
            if (pack != null)
                game.motionPack = Mathf.Max(0, game.motionPacks.FindIndex(p => p.name == pack));
            game.camAvoid = RoeCapture.Arg("-roeCamAvoid", "1") != "0";
            game.Setup();

            var hide = new HideCounter(game);
            string frameDir = Path.Combine(outDir, "frames");
            Directory.CreateDirectory(outDir);
            if (film)
            {
                if (Directory.Exists(frameDir))
                    Directory.Delete(frameDir, true);
                Directory.CreateDirectory(frameDir);
            }
            string warm = Path.Combine(outDir, "_warmup.jpg");
            int steps = Mathf.RoundToInt(seconds * 60f), shot = 0, endAfter = -1;
            var states = new StringBuilder();
            for (int s = 0; s < steps; s++)
            {
                game.Step(null);
                game.UpdateCamera(FightGame.Dt);
                if (s % every != 0)
                    continue;
                if (film)
                {
                    game.hud.Refresh(game);
                    Canvas.ForceUpdateCanvases();
                    if (shot == 0)
                    {
                        RoeCapture.Render(game.cam, 320, 180, warm, 80);
                        RoeCapture.Render(game.cam, 320, 180, warm, 80);
                    }
                    RoeCapture.Render(game.cam, width, height, Path.Combine(frameDir, $"{shot:D5}.jpg"), 92);
                }
                shot++;
                hide.Look(game, s / 60f);
                if (s % 60 == 0)
                    states.Append($"\n[ROE]   t={s / 60f,5:F1}s {game.phase} {game.message} | " +
                                  string.Join(" | ", game.f.Select(x => $"{x.rig.id} {x.state} hp {x.hp} meter {x.meter:F0} at {x.pos.x:F1},{x.pos.z:F1}")) +
                                  $" | camera {game.camMode} fov {game.cam.fieldOfView:F0} near {game.cam.nearClipPlane:F1}");
                if (game.phase == FightGame.Phase.MatchOver && endAfter < 0)
                    endAfter = s + 240;
                if (endAfter >= 0 && s >= endAfter)
                    break;
            }
            int sounds = WriteTimeline(game, outDir, shot, every);
            hide.Dispose();
            Debug.Log($"[ROE] camera ({(game.camAvoid && game.room != null && game.room.Valid ? "keeps out of the stage" : "the old one")}): {hide.Report(game)}");
            Debug.Log($"[ROE] fight recorded: {shot} frames ({shot * every / 60f:F1} s), {sounds} sounds, rounds won {game.wins[0]}-{game.wins[1]}, " +
                      $"to {outDir}{states}");
        }

        /// <summary>The sounds of a filmed fight for make_video.py, at the frames of the video (every: steps per frame): timeline.json.</summary>
        static int WriteTimeline(FightGame game, string outDir, int shot, int every)
        {
            var json = new StringBuilder();
            var lines = new List<string>();
            foreach (var e in game.soundLog)
            {
                var rig = game.rigs.FirstOrDefault(r => r.id == e.fighter);
                var clip = rig?.sounds.FirstOrDefault(x => x.name == e.name)?.clip;
                if (clip == null)
                    continue;
                lines.Add($"{{\"path\":\"{AssetDatabase.GetAssetPath(clip)}\",\"frame\":{e.frame / every},\"volume\":{e.volume.ToString("F2", CultureInfo.InvariantCulture)}}}");
            }
            string music = game.music != null
                ? $",\"music\":{{\"path\":\"{AssetDatabase.GetAssetPath(game.music)}\",\"volume\":{game.musicVolume.ToString("F2", CultureInfo.InvariantCulture)}}}"
                : "";
            json.Append($"{{\"fps\":{60 / every},\"frames\":{shot}{music},\"sounds\":[\n  {string.Join(",\n  ", lines)}\n]}}\n");
            File.WriteAllText(Path.Combine(outDir, "timeline.json"), json.ToString());
            return lines.Count;
        }

        /// <summary>
        /// Her three game skills one after the other in the real fight (user 10-11: "把m02也加入战斗，战斗动作也加入进来，作为3个技能使用"):
        /// 1P does skill 1, skill 2 and - gauge filled - the super against 2P, who just stands (nobody is the CPU); each
        /// waits until both are back in guard.  Filmed by the fight's camera with the HUD; the sounds in timeline.json for
        /// tools/make_video.py.
        ///   -executeMethod RoeFighter.EditorTools.RoeFightScene.SkillReel [-roeP1 m02] [-roeP2 a08] [-roeSkills 1,2,3]
        ///   [-roeOut _work\skills_m02] [-roeSize 1280x720] [-roeStage e23_steel_s02] [-roeBurst 1]
        /// </summary>
        public static void SkillReel()
        {
            string stage = RoeCapture.Arg("-roeStage", "e23_steel_s02");
            string p1 = RoeCapture.Arg("-roeP1", "m02"), p2 = RoeCapture.Arg("-roeP2", "a08");
            string outDir = RoeCapture.Arg("-roeOut", Path.Combine(ProjectDir, "_work", "skills_" + p1));
            var size = RoeCapture.Arg("-roeSize", "1280x720").Split('x');
            int width = int.Parse(size[0]), height = int.Parse(size[1]);
            var skills = RoeCapture.Arg("-roeSkills", "1,2,3").Split(',').Select(x => int.Parse(x.Trim())).ToList();
            const int every = 2;                                  // 60 steps per second, every 2nd filmed = 30 fps
            RoeClothesBurst.Enabled = RoeCapture.Arg("-roeBurst", "1") != "0";

            ShaderUtil.allowAsyncCompilation = false;
            string path = ScenePath(stage);
            if (!File.Exists(path))
                BuildScene(stage, p1, p2);
            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var game = Object.FindFirstObjectByType<FightGame>();
            game.PickIds(p1, p2);
            game.cpu = new[] { false, false };
            game.seed = 1;
            game.hud.drawnByCamera = true;
            foreach (var rig in game.roster.Length > 0 ? game.roster : game.rigs)
                foreach (var smr in rig.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    smr.forceMatrixRecalculationPerRender = true;     // many renders inside one editor update
            game.Setup();

            string frameDir = Path.Combine(outDir, "frames");
            if (Directory.Exists(frameDir))
                Directory.Delete(frameDir, true);
            Directory.CreateDirectory(frameDir);
            var log = new StringBuilder();
            var segments = new List<string>();                    // reel.json: which frames show which skill (tools/skill_reel_video.py)
            int s = 0, shot = 0, limit = 60 * 90;
            void Advance(FighterInput[] inputs)
            {
                game.Step(inputs);
                game.UpdateCamera(FightGame.Dt);
                if (s % every == 0)
                {
                    game.hud.Refresh(game);
                    Canvas.ForceUpdateCanvases();
                    if (shot == 0)
                        RoeCapture.Render(game.cam, 320, 180, Path.Combine(outDir, "_warmup.jpg"), 80);
                    RoeCapture.Render(game.cam, width, height, Path.Combine(frameDir, $"{shot:D5}.jpg"), 92);
                    shot++;
                }
                s++;
            }
            bool Ready(Fighter x) => x.state == FightState.Idle || x.state == FightState.Walk || x.state == FightState.Side;

            while (game.phase != FightGame.Phase.Fight && s < limit)
                Advance(null);
            foreach (int k in skills)
            {
                // both back in guard, then a short pause
                for (int calm = 0; calm < 40 && s < limit;)
                {
                    Advance(null);
                    calm = Ready(game.f[0]) && Ready(game.f[1]) ? calm + 1 : 0;
                }
                if (k == 3)
                    game.f[0].meter = 100f;
                // a skill that reaches less far than she stands: walk up first, as the CPU does
                string skillName = "skill" + k;
                for (int walk = 0; walk < 240 && s < limit &&
                     Vector3.Distance(game.f[0].pos, game.f[1].pos) > game.f[0].SpecialReach(skillName) - 0.3f; walk++)
                {
                    var toFoe = game.f[1].pos - game.f[0].pos;
                    var step = new FighterInput[2];
                    step[0] = new FighterInput { x = Vector3.Dot(toFoe, game.cam.transform.right) >= 0f ? 1 : -1 };
                    Advance(step);
                }
                var press = new FighterInput[2];
                press[0] = new FighterInput { s1 = k == 1, s2 = k == 2, s3 = k == 3 };
                int from = shot;
                float dist = Vector3.Distance(game.f[0].pos, game.f[1].pos);
                var action = game.f[0].rig.sheet?.Find("skill" + k);
                string reach = action != null
                    ? $"{dist:F1} m apart, reach {FightGame.BlowReach(action, game.f[1]):F1} m ({(action.Dashes ? "dashes" : $"hits {action.HitsAhead:F1} m ahead")}, {action.Blows.Count} blows at {string.Join(" ", action.Blows.Select(h => h.time.ToString("F2", CultureInfo.InvariantCulture)))} s), "
                    : "";
                Advance(press);
                bool started = game.f[0].state == FightState.Special;
                while (game.f[0].state == FightState.Special && s < limit)
                    Advance(null);
                var burst = game.f[1].rig.GetComponentInChildren<RoeClothesBurst>(true);
                segments.Add($"{{\"skill\":{k},\"from\":{from},\"to\":{shot},\"started\":{(started ? "true" : "false")},\"hp\":{game.f[1].hp}}}");
                log.Append($"\n[ROE]   {(k == 3 ? "super" : "skill " + k)}: {(started ? "" : "DID NOT START (out of range or no timeline) ")}" +
                           $"{reach}frames {from}-{shot}, {p2} hp {game.f[1].hp}, her stages off {(burst != null ? burst.Dropped : 0)}");
            }
            for (int i = 0; i < 120; i++)
                Advance(null);
            int sounds = WriteTimeline(game, outDir, shot, every);
            string Name(string id) => DisplayNames.TryGetValue(id, out var n) ? n : id;
            File.WriteAllText(Path.Combine(outDir, "reel.json"),
                $"{{\"p1\":\"{p1}\",\"name\":\"{Name(p1)}\",\"p2\":\"{p2}\",\"foe\":\"{Name(p2)}\",\"fps\":{60 / every},\"frames\":{shot},\"skills\":[\n  {string.Join(",\n  ", segments)}\n]}}\n");
            Debug.Log($"[ROE] skill reel {p1} vs {p2}: {shot} frames ({shot * every / 60f:F1} s), {sounds} sounds, to {outDir}{log}");
        }

        /// <summary>
        /// How often the stage hides part of a fighter from the fight camera: rays from the camera (past its near plane:
        /// what lies closer is not drawn) to both fighters' heads, chests and hips, against the stage's own meshes - not
        /// the camera's room map, so this checks that too.
        /// </summary>
        sealed class HideCounter : IDisposable
        {
            static readonly HumanBodyBones[] Bones = { HumanBodyBones.Head, HumanBodyBones.Chest, HumanBodyBones.Hips };
            readonly RoeStage.Sight sight;
            public int frames, hiddenFrames, rays, hiddenRays;
            readonly List<float> hiddenAt = new List<float>();

            public HideCounter(FightGame game)
            {
                sight = new RoeStage.Sight((game.roster ?? new FighterRig[0]).Concat(game.rigs).Where(r => r != null).Select(r => r.transform).ToList());
            }

            readonly Dictionary<string, int> blockers = new Dictionary<string, int>();
            readonly Dictionary<string, int> modes = new Dictionary<string, int>();

            public void Look(FightGame game, float time)
            {
                frames++;
                int hidden = 0;
                foreach (var x in game.f)
                    foreach (var bone in Bones)
                    {
                        var t = x.rig.animator != null ? x.rig.animator.GetBoneTransform(bone) : null;
                        if (t == null)
                            continue;
                        rays++;
                        var from = game.cam.transform.position;
                        from += (t.position - from).normalized * game.cam.nearClipPlane;
                        var hit = sight.Blocker(from, t.position, out var at);
                        if (hit == null)
                            continue;
                        hidden++;
                        // what was in the way, how high, and where the fighter stood (outside the arena: thrown over the ropes)
                        string key = $"{hit.name} ({at.y - game.centre.y:F1} m up, {Vector3.Distance(Flat(at), Flat(game.centre)):F1} m from the centre; " +
                                     $"{bone} {Vector3.Distance(Flat(t.position), Flat(game.centre)):F1} m)";
                        blockers[key] = blockers.TryGetValue(key, out int c) ? c + 1 : 1;
                    }
                hiddenRays += hidden;
                if (hidden > 0)
                {
                    hiddenFrames++;
                    hiddenAt.Add(time);
                    string m = $"{game.phase}/{game.camMode}";
                    modes[m] = modes.TryGetValue(m, out int c) ? c + 1 : 1;
                }
            }

            static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

            public float Share => 100f * hiddenFrames / Mathf.Max(1, frames);

            /// <summary>The hidden stretches, as from-to seconds.</summary>
            public string Spans()
            {
                var spans = new List<string>();
                for (int i = 0; i < hiddenAt.Count; i++)
                {
                    int j = i;
                    while (j + 1 < hiddenAt.Count && hiddenAt[j + 1] - hiddenAt[j] < 0.2f)
                        j++;
                    spans.Add(j > i ? $"{hiddenAt[i]:F1}-{hiddenAt[j]:F1}" : $"{hiddenAt[i]:F1}");
                    i = j;
                }
                return spans.Count > 0 ? string.Join(" ", spans.Take(40)) : "-";
            }

            public string Report(FightGame game)
            {
                int total = Mathf.Max(1, game.camModeSteps.Sum());
                string Mode(FightGame.CamMode m) => $"{m} {100f * game.camModeSteps[(int)m + 1] / total:F1}%";
                return $"the stage hid part of a fighter in {hiddenFrames} of {frames} frames ({Share:F1}%), {hiddenRays} of {rays} rays; " +
                       $"steps {Mode(FightGame.CamMode.SquareOn)}, {Mode(FightGame.CamMode.SwungRound)}, {Mode(FightGame.CamMode.Closer)}, " +
                       $"{Mode(FightGame.CamMode.OtherSide)}, {Mode(FightGame.CamMode.Cutaway)}, {game.camCuts} cuts, {game.camJumps} jumps; hidden at {Spans()} s; " +
                       $"hidden while {string.Join(", ", modes.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value}"))}; " +
                       $"in the way: {string.Join(", ", blockers.OrderByDescending(kv => kv.Value).Take(6).Select(kv => $"{kv.Key} x{kv.Value}"))}";
            }

            public void Dispose() => sight.Dispose();
        }

        /// <summary>
        /// The old camera against the one that keeps out of the stage, over several CPU matches (nothing filmed; each
        /// match twice, once with each - the matches differ, since the computer's moves follow the screen):
        ///   -executeMethod RoeFighter.EditorTools.RoeFightScene.CameraStats [-roeStage e23_steel_s02] [-roeSeconds 120]
        ///   [-roeMatches fio005:a08:2,a08:g04:1]   (1P:2P:seed; about 2 minutes a match each way)
        /// </summary>
        public static void CameraStats()
        {
            string stage = RoeCapture.Arg("-roeStage", "e23_steel_s02");
            float seconds = float.Parse(RoeCapture.Arg("-roeSeconds", "120"), CultureInfo.InvariantCulture);
            var matches = RoeCapture.Arg("-roeMatches", "fio005:a08:2,a08:g04:1,kas:b10:1,g05:fio005:3").Split(',');
            ShaderUtil.allowAsyncCompilation = false;
            var sb = new StringBuilder($"[ROE] camera stats on {stage}, {seconds} s matches (part of a fighter hidden by the stage, share of frames):");
            float[] sum = new float[2];
            int count = 0;
            foreach (var m in matches)
            {
                var p = m.Split(':');
                var line = new StringBuilder($"\n[ROE]   {p[0]} v {p[1]} seed {p[2]}:");
                for (int avoid = 0; avoid < 2; avoid++)
                {
                    EditorSceneManager.OpenScene(ScenePath(stage), OpenSceneMode.Single);
                    var game = Object.FindFirstObjectByType<FightGame>();
                    game.PickIds(p[0], p[1]);
                    game.cpu = new[] { true, true };
                    game.seed = int.Parse(p[2]);
                    game.camAvoid = avoid == 1;
                    game.Setup();
                    using (var hide = new HideCounter(game))
                    {
                        int steps = Mathf.RoundToInt(seconds * 60f);
                        for (int s = 0; s < steps && game.phase != FightGame.Phase.MatchOver; s++)
                        {
                            game.Step(null);
                            game.UpdateCamera(FightGame.Dt);
                            if (s % 2 == 0)
                                hide.Look(game, s / 60f);
                        }
                        sum[avoid] += hide.Share;
                        string result = $"{(avoid == 1 ? "new" : "old")}: {hide.Report(game)}; rounds {game.wins[0]}-{game.wins[1]}";
                        line.Append($"\n[ROE]     {result}");
                        Debug.Log($"[ROE] camera stats {p[0]} v {p[1]} seed {p[2]} {result}");     // as it comes (a long run may be cut short)
                    }
                }
                count++;
                sb.Append(line);
            }
            sb.Append($"\n[ROE]   mean: old {sum[0] / Mathf.Max(1, count):F1}%, new {sum[1] / Mathf.Max(1, count):F1}%");
            Debug.Log(sb.ToString());
        }

        // ---- filming the select screen

        /// <summary>
        /// The select screen filmed (batch mode has no keys: the cards are moved by script the way the keys move
        /// them): 1P goes from INASE over LUF to KART and confirms, picks GODDESS LUF for the computer, confirms, and
        /// the match begins.  On the cards the fighters turn to the camera in their showcase idle; a confirmed pick plays
        /// her showcase reaction with its line (FightGame.ShowPicked).  Frames and timeline.json (music, the lines) for
        /// tools/make_video.py.
        ///   -executeMethod RoeFighter.EditorTools.RoeFightScene.SelectDemo [-roeOut dir] [-roeSize 1280x720]
        /// </summary>
        [MenuItem("ROE Fighter/Fight/Film the select screen")]
        public static void SelectDemo()
        {
            string stage = RoeCapture.Arg("-roeStage", "e23_steel_s02");
            string outDir = RoeCapture.Arg("-roeOut", Path.Combine(ProjectDir, "_work", "select_demo"));
            var size = RoeCapture.Arg("-roeSize", "1280x720").Split('x');
            int width = int.Parse(size[0]), height = int.Parse(size[1]);
            ShaderUtil.allowAsyncCompilation = false;
            EditorSceneManager.OpenScene(ScenePath(stage), OpenSceneMode.Single);
            var game = Object.FindFirstObjectByType<FightGame>();
            game.hud.drawnByCamera = true;
            game.hud.showHelp = true;
            void Recalc()
            {
                foreach (var rig in game.roster.Concat(game.rigs))
                    foreach (var smr in rig.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                        smr.forceMatrixRecalculationPerRender = true;
            }
            Recalc();
            game.cpu = new[] { false, true };
            game.PickIds("a08", "g04");
            game.EnterSelect();
            string frameDir = Path.Combine(outDir, "frames");
            if (Directory.Exists(frameDir))
                Directory.Delete(frameDir, true);
            Directory.CreateDirectory(frameDir);
            int shot = 0;
            var log = new StringBuilder("[ROE] select demo:");
            // the voices of the picks (the showcase reactions) for the video's sound, by the video frame they start on (the
            // match's start clears the game's own log)
            var sounds = new List<string>();
            int seen = 0;
            void Film(float seconds, string what)
            {
                log.Append($"\n[ROE]   {shot / 30f,5:F1}s {what}");
                int steps = Mathf.RoundToInt(seconds * 60f);
                for (int s = 0; s < steps; s++)
                {
                    game.Step(null);
                    if (game.soundLog.Count < seen)
                        seen = 0;
                    for (; seen < game.soundLog.Count; seen++)
                    {
                        var e = game.soundLog[seen];
                        var clip = game.rigs.FirstOrDefault(r => r.id == e.fighter)?.sounds.FirstOrDefault(x => x.name == e.name)?.clip;
                        if (clip != null)
                            sounds.Add($"{{\"path\":\"{AssetDatabase.GetAssetPath(clip)}\",\"frame\":{shot},\"volume\":{e.volume.ToString("F2", CultureInfo.InvariantCulture)}}}");
                        log.Append($"\n[ROE]   {shot / 30f,5:F1}s sound {e.fighter} {e.name}{(clip == null ? " (no clip)" : "")}");
                    }
                    game.UpdateCamera(FightGame.Dt);
                    if (s % 2 != 0)
                        continue;
                    game.hud.Refresh(game);
                    Canvas.ForceUpdateCanvases();
                    if (shot == 0)
                    {
                        RoeCapture.Render(game.cam, 320, 180, Path.Combine(outDir, "_warm.jpg"), 80);
                        RoeCapture.Render(game.cam, 320, 180, Path.Combine(outDir, "_warm.jpg"), 80);
                    }
                    RoeCapture.Render(game.cam, width, height, Path.Combine(frameDir, $"{shot:D5}.jpg"), 92);
                    shot++;
                }
            }
            void Move(int side, string id)
            {
                var before = new[] { game.pick[0], game.pick[1] };
                game.pick[side] = Array.FindIndex(game.roster, r => r.id == id);
                game.PicksChanged(before);
                Recalc();
            }
            Film(1.6f, "the select screen: 1P on INASE, the computer on LUF");
            Move(0, "g04");
            Film(0.9f, "1P moves to LUF (both on the same card: a mirror match would get a copy)");
            Move(0, "b10");
            Film(1.2f, "1P moves to KART");
            game.picked[0] = true;
            game.choosing = 1;
            Film(3.2f, "1P confirms: her showcase reaction (react_01 and its line); now the computer's fighter");
            Move(1, "a08");
            Film(0.8f, "the computer's card to INASE");
            Move(1, "g05");
            Film(1.2f, "and to GODDESS LUF");
            game.picked[1] = true;
            Film(3.4f, "confirmed: her reaction, READY");
            game.BeginMatch();
            Film(3.2f, "the match begins");
            string music = game.music != null
                ? $",\"music\":{{\"path\":\"{AssetDatabase.GetAssetPath(game.music)}\",\"volume\":{game.musicVolume.ToString("F2", CultureInfo.InvariantCulture)}}}"
                : "";
            File.WriteAllText(Path.Combine(outDir, "timeline.json"), $"{{\"fps\":30,\"frames\":{shot}{music},\"sounds\":[\n  {string.Join(",\n  ", sounds)}\n]}}\n");
            Debug.Log(log.Append($"\n[ROE]   {shot} frames to {frameDir}").ToString());
        }

        // ---- a Windows build to play

        [MenuItem("ROE Fighter/Fight/Build Windows player")]
        public static void BuildPlayer()
        {
            string stage = RoeCapture.Arg("-roeStage", "e23_steel_s02");
            string outDir = RoeCapture.Arg("-roeOut", Path.Combine(ProjectDir, "out", "ROEFighter"));
            string path = ScenePath(stage);
            if (!File.Exists(path))
                BuildScene(stage, "a08", "g04");
            PlayerSettings.productName = "ROE Fighter";
            PlayerSettings.companyName = "ROE Fighter";
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.resizableWindow = true;
            var options = new BuildPlayerOptions
            {
                scenes = new[] { path },
                locationPathName = Path.Combine(outDir, "ROEFighter.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            Debug.Log($"[ROE] player build: {report.summary.result}, {report.summary.totalSize / (1024 * 1024)} MB, " +
                      $"{report.summary.totalTime.TotalMinutes:F1} min, errors {report.summary.totalErrors}, to {options.locationPathName}");
        }
    }
}
