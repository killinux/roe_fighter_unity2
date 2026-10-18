using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RoeFighter.EditorTools
{
    /// <summary>
    /// A Dead or Alive 6 character as a fighter (user 10-04: "doa6中的 Kasumi也加入一个角色").  tools/doa6_fbx.py turns the
    /// assembled .blend of ripper_tpose scripts/doa6 into Assets/DOA/&lt;id&gt;/&lt;id&gt;.fbx (one skeleton, on the floor) +
    /// textures/ + materials.json; this
    ///   - sets the texture importers (the *_kidsnmh maps are tangent-space normal maps),
    ///   - makes URP Lit materials from materials.json (colour maps with _BLEND in the name are cut out by their alpha:
    ///     hair, lashes; hair and ribbons are two-sided) and puts them on the model,
    ///   - saves the Generic prefab Generated/&lt;id&gt;/&lt;id&gt;.prefab and the humanoid fighter Generated/&lt;id&gt;/&lt;id&gt;_fighter.prefab
    ///     (bones mapped by their DOA6 global ids, RoeHumanoid.MapBones; avatar built on a T-posed copy, like the ROE ones).
    ///   -executeMethod RoeFighter.EditorTools.DoaFighter.Build [-roeDoa kas]
    /// </summary>
    public static class DoaFighter
    {
        /// <summary>Her files: Assets/VDF/&lt;id&gt; for a Vindictus character (VdfFighter), Assets/SB/&lt;id&gt; for Stellar Blade's
        /// (SbFighter), else Assets/DOA/&lt;id&gt;.</summary>
        public static string Dir(string id) => Directory.Exists($"Assets/VDF/{id}") ? $"Assets/VDF/{id}"
                                              : Directory.Exists($"Assets/SB/{id}") ? $"Assets/SB/{id}" : $"Assets/DOA/{id}";

        /// <summary>
        /// A character from another game in the fight: tools/doa/&lt;id&gt;.json (DOA6), tools/vdf/&lt;id&gt;.json (Vindictus) or
        /// tools/sb/&lt;id&gt;.json (Stellar Blade) - name,
        /// outfit, her motion pack, which clips are her "game clips".
        /// </summary>
        [System.Serializable]
        public class Definition
        {
            public string id, name, outfit, pack;   // pack: her own stance and strikes (empty: the fight's motion pack, F3)
            public string source = "DOA6";      // the game she comes from (the log)
            /// <summary>Without an own pack: the clip she stands in (FighterRig.stance: soles, bones no clip moves), "pack:role" or a clip path.</summary>
            public string stance;
            /// <summary>"bind": her feet stand as the bind pose has them, the ankle held at its bind angle in every clip (Eve's
            /// platform shoes; FighterRig.bindFeet); empty: as the stance has them.</summary>
            public string feet;
            /// <summary>
            /// The fight's clip names (hurt, die, rip, react_02 intro, idle_02 win, skill_01..03) -> segment names of her pack's
            /// BVH, or a humanoid clip's asset path ("Assets/.../x.anim", "Assets/.../x.fbx:clip").
            /// </summary>
            public List<Pair> game = new List<Pair>();
            /// <summary>Her three specials: when the blows land (s from the start; found from her limbs if empty), how far she slides in.</summary>
            public List<SkillSpec> skills = new List<SkillSpec>();
            /// <summary>Her game's physics words changed for the fight (DoaPhysicsBuilder): the game's are the default, each change says why.</summary>
            public List<Tune> tune = new List<Tune>();
            /// <summary>The pose of her select-screen picture, "&lt;segment of her pack&gt;@&lt;seconds&gt;" (empty: her stance; Mai's low stance hid
            /// her face: "mai_07020_win@1.3").</summary>
            public string portrait;
        }

        /// <summary>One parameter word of one of her DOA6 chains set to another value (Mai's 1.2 m tail: word 8, the pull back to the pose).</summary>
        [System.Serializable]
        public class Tune
        {
            public string chain, why;
            public int word;
            public float value;
        }

        [System.Serializable]
        public class Pair
        {
            public string name, clip;
        }

        [System.Serializable]
        public class SkillSpec
        {
            public string action, clip;
            public float[] hits;
            public float radius = 0.6f;
        }

        public static string DefinitionPath(string id)
        {
            string tools = Path.Combine(Path.GetDirectoryName(Application.dataPath), "tools");
            string vdf = Path.Combine(tools, "vdf", id + ".json"), sb = Path.Combine(tools, "sb", id + ".json");
            return File.Exists(vdf) ? vdf : File.Exists(sb) ? sb : Path.Combine(tools, "doa", id + ".json");
        }

        /// <summary>A character from another game (DOA6, Vindictus) whose fighter prefab is built.</summary>
        public static bool IsDoa(string id) => File.Exists(DefinitionPath(id)) && File.Exists(RoeHumanoid.FighterPath(id));

        public static Definition Load(string id) => JsonUtility.FromJson<Definition>(File.ReadAllText(DefinitionPath(id)));

        /// <summary>
        /// A clip of her pack's converted BVH (RoeMotionPacks: &lt;pack&gt;/bvh/&lt;segment&gt;.anim); or, when the name is an asset path,
        /// that clip ("Assets/.../x.anim", or "Assets/.../x.fbx:clip" for one of a model's clips).
        /// </summary>
        public static AnimationClip PackClip(string pack, string segment)
        {
            if (!segment.StartsWith("Assets/"))
                return AssetDatabase.LoadAssetAtPath<AnimationClip>($"{RoeMotionPacks.Dir}/{pack}/bvh/{segment}.anim");
            int colon = segment.LastIndexOf(':');
            string path = colon > 0 ? segment.Substring(0, colon) : segment;
            string clip = colon > 0 ? segment.Substring(colon + 1) : null;
            return AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview__") && (clip == null || c.name == clip));
        }

        /// <summary>
        /// When the blows of a move land: the peaks of her fastest hand or foot (against her hips, so the move's own travel
        /// does not count), at least 4.5 m/s and 0.15 s apart, played on her fighter prefab at 60 Hz.  With a blade in her
        /// hand (RoeBlade, Fiona's sword) the peaks of its tip instead, at least BladeHitSpeed: her hands move fast in the
        /// wind-ups too (her lunge's blows were found at 0.05 and 0.23 s; the thrust lands at 0.53).
        /// </summary>
        /// <summary>Metres per second of a blade's tip (against her hips) that make a blow (DetectHits).</summary>
        public static float BladeHitSpeed = 20f;

        public static List<float> DetectHits(AnimationClip clip, string id, float minSpeed = 4.5f, float gap = 0.15f)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(RoeHumanoid.FighterPath(id)));
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var an = go.GetComponent<Animator>();
            var limbs = new[] { HumanBodyBones.LeftHand, HumanBodyBones.RightHand, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot }
                .Select(an.GetBoneTransform).Where(t => t != null).ToArray();
            var blade = go.GetComponentInChildren<RoeBlade>(true);
            if (blade != null)
                minSpeed = BladeHitSpeed;
            var hips = an.GetBoneTransform(HumanBodyBones.Hips);
            var speed = new List<float>();
            Vector3[] last = null;
            for (float t = 0f; t <= clip.length; t += 1f / 60f)
            {
                RoeCapture.Pose(go, clip, t);
                var now = blade != null ? new[] { blade.Tip - hips.position } : limbs.Select(l => l.position - hips.position).ToArray();
                speed.Add(last == null ? 0f : now.Select((p, k) => (p - last[k]).magnitude * 60f).Max());
                last = now;
            }
            RoeCapture.EndPosing();
            Object.DestroyImmediate(go);
            var hits = new List<float>();
            for (int i = 1; i + 1 < speed.Count; i++)
            {
                // nothing lands in the first 0.1 s (the first step can jump from the pose before the clip: a blow at 0.02 s
                // left her lunge no time to close in, MakeSkillSheet)
                if (i < 6 || speed[i] < minSpeed || speed[i] < speed[i - 1] || speed[i] < speed[i + 1])
                    continue;
                float time = i / 60f;
                if (hits.Count > 0 && time - hits[hits.Count - 1] < gap)
                {
                    if (speed[i] > speed[Mathf.RoundToInt(hits[hits.Count - 1] * 60f)])
                        hits[hits.Count - 1] = time;
                    continue;
                }
                hits.Add(time);
            }
            return hits;
        }

        /// <summary>
        /// Her skill sheet (Assets/DOA/&lt;id&gt;/skill_sheet_unity.json), the shape the game's units have: per special its clip,
        /// its blows (equal shares) and one "attack" slide that brings her in before the first blow (the moves are played in
        /// place); no effects, sounds or cameras.
        /// </summary>
        public static TextAsset MakeSkillSheet(string id, Definition def, Dictionary<string, AnimationClip> clips)
        {
            var sheet = new RoeSkillSheet { unit = id, id = id, model = id, receiveRadius = 0.8f };
            var notes = new List<string>();
            foreach (var s in def.skills)
            {
                if (!clips.TryGetValue(s.clip, out var clip) || clip == null)
                    continue;
                var hits = s.hits != null && s.hits.Length > 0 ? s.hits.ToList() : DetectHits(clip, id);
                if (hits.Count == 0)
                    hits.Add(clip.length * 0.4f);
                var a = new RoeSkillSheet.Action { name = s.action, timeline = s.action, duration = clip.length };
                a.clips.Add(new RoeSkillSheet.Clip { clip = s.clip, start = 0f, end = clip.length, clipIn = 0f, speed = 1f });
                foreach (var h in hits)
                    a.hits.Add(new RoeSkillSheet.Hit { time = h, ratio = 1f, what = "hurt" });
                a.moves.Add(new RoeSkillSheet.Move { start = 0f, end = Mathf.Max(0.1f, hits[0] * 0.8f), radius = s.radius, kind = "attack", name = "in" });
                sheet.actions.Add(a);
                notes.Add($"{s.action} {s.clip} {clip.length:F2} s, blows at {string.Join(" ", hits.Select(h => h.ToString("F2")))}");
            }
            string path = $"{Dir(id)}/skill_sheet_unity.json";
            File.WriteAllText(path, JsonUtility.ToJson(sheet, true));
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            Debug.Log($"[ROE] {id}: skill sheet {path}: {string.Join("; ", notes)}");
            return AssetDatabase.LoadAssetAtPath<TextAsset>(path);
        }

        [System.Serializable]
        class Sidecar
        {
            public string id, fbx;
        }

        class MaterialInfo
        {
            public string name, albedo, normal;
            public bool alpha;
        }

        /// <summary>materials.json's "materials" object, read by hand (JsonUtility has no dictionaries).</summary>
        static List<MaterialInfo> ReadMaterials(string json)
        {
            var result = new List<MaterialInfo>();
            int start = json.IndexOf("\"materials\"");
            if (start < 0)
                return result;
            int open = json.IndexOf('{', start);
            int depth = 0, i = open;
            for (; i < json.Length; i++)
            {
                if (json[i] == '{') depth++;
                else if (json[i] == '}' && --depth == 0) break;
            }
            string body = json.Substring(open + 1, i - open - 1);
            foreach (var m in System.Text.RegularExpressions.Regex.Matches(body, "\"([^\"]+)\"\\s*:\\s*\\{([^}]*)\\}").Cast<System.Text.RegularExpressions.Match>())
            {
                string Field(string key)
                {
                    var f = System.Text.RegularExpressions.Regex.Match(m.Groups[2].Value, $"\"{key}\"\\s*:\\s*(\"([^\"]*)\"|null|true|false)");
                    return !f.Success ? null : f.Groups[2].Success && f.Groups[1].Value.StartsWith("\"") ? f.Groups[2].Value : f.Groups[1].Value;
                }
                result.Add(new MaterialInfo
                {
                    name = m.Groups[1].Value,
                    albedo = Field("albedo") is string a && a != "null" ? a : null,
                    normal = Field("normal") is string n && n != "null" ? n : null,
                    alpha = Field("alpha") == "true",
                });
            }
            return result;
        }

        /// <summary>
        /// Studio stills of the fighter prefab in its bind pose, like RoeShowcase.Stills: full, face, upper body, back.
        ///   -executeMethod RoeFighter.EditorTools.DoaFighter.Stills [-roeDoa kas] [-roeOut dir]
        /// </summary>
        public static void Stills()
        {
            string outDir = RoeCapture.Arg("-roeOut", Path.Combine(Path.GetDirectoryName(Application.dataPath), "_work", "shots"));
            ShaderUtil.allowAsyncCompilation = false;
            var studio = RoeStudio.Build();
            foreach (var id in RoeCapture.Arg("-roeDoa", "kas").Split(','))
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(RoeHumanoid.FighterPath(id)));
                // the cloth rebuilt from its control points at rest: must look as imported (a check of RebuildSurfaces)
                CheckSurfaces(go);
                var forward = RoeShowcase.Forward(go);
                var bounds = RoeShowcase.WorldBounds(go);
                var map = RoeHumanoid.MapBones(go);
                var headPos = map.TryGetValue("Head", out var head) ? head.position : bounds.center + Vector3.up * bounds.extents.y * 0.8f;
                var chestPos = map.TryGetValue("Chest", out var chest) ? chest.position : bounds.center;
                float height = bounds.max.y;
                studio.LightFrom(forward);
                var body = new Vector3(bounds.center.x, height * 0.52f, bounds.center.z);
                studio.SetFocus(0f, 0f, 0f);
                studio.Aim(body, forward, 18f, 6f, height * 2.75f, 26f);
                string warm = Path.Combine(outDir, "_warmup.png");
                RoeCapture.Render(studio.camera, 320, 320, warm);
                RoeCapture.Render(studio.camera, 320, 320, warm);
                RoeCapture.Render(studio.camera, 1400, 1800, Path.Combine(outDir, $"{id}_1_full.png"));
                studio.Aim(headPos + Vector3.up * 0.06f + forward * 0.02f, forward, 14f, 2f, 0.95f, 17f);
                studio.SetFocus(0.95f, 2.8f, 85f);
                RoeCapture.Render(studio.camera, 1400, 1400, Path.Combine(outDir, $"{id}_2_face.png"));
                studio.Aim(Vector3.Lerp(chestPos, headPos, 0.45f), forward, -28f, 4f, 1.9f, 24f);
                studio.SetFocus(1.9f, 4f, 70f);
                RoeCapture.Render(studio.camera, 1400, 1800, Path.Combine(outDir, $"{id}_3_upper.png"));
                studio.SetFocus(0f, 0f, 0f);
                studio.Aim(body, forward, 160f, 8f, height * 2.75f, 26f);
                RoeCapture.Render(studio.camera, 1400, 1800, Path.Combine(outDir, $"{id}_4_back.png"));
                Object.DestroyImmediate(go);
                Debug.Log($"[ROE] {id}: stills in {outDir} (bounds {bounds.min}..{bounds.max})");
            }
        }

        /// <summary>
        /// Rebuilds her grid-cloth surfaces at rest and measures them against the imported meshes they replace (each source
        /// skinned as it is, hidden): how far the rebuilt vertices are, and how far their normals and tangents turn.
        /// </summary>
        public static void CheckSurfaces(GameObject go)
        {
            var rig = go.GetComponent<RoeDoaRig>();
            if (rig == null || rig.surfaces.Count == 0)
                return;
            var baked = new List<(Vector3[] pos, Vector3[] normal, Vector4[] tangent)>();
            foreach (var s in rig.surfaces)
            {
                var m = new Mesh();
                s.source.BakeMesh(m, true);
                var w = s.source.transform.localToWorldMatrix;
                baked.Add((m.vertices.Select(p => w.MultiplyPoint3x4(p)).ToArray(),
                           m.normals.Select(n => w.MultiplyVector(n).normalized).ToArray(),
                           m.tangents.Select(t => { var v = w.MultiplyVector(new Vector3(t.x, t.y, t.z)).normalized; return new Vector4(v.x, v.y, v.z, t.w); }).ToArray()));
                Object.DestroyImmediate(m);
            }
            rig.RebuildSurfaces();
            for (int si = 0; si < rig.surfaces.Count; si++)
            {
                var s = rig.surfaces[si];
                var mesh = s.filter.sharedMesh;
                var w = s.filter.transform.localToWorldMatrix;
                var pos = mesh.vertices;
                var normal = mesh.normals;
                var tangent = mesh.tangents;
                var bytes = s.binding.bytes;
                int n = System.BitConverter.ToInt32(bytes, 8);
                var (bp, bn, bt) = baked[si];
                int rebuilt = 0, flipped = 0, wFlip = 0;
                double sumD = 0, sumA = 0, sumT = 0;
                float maxD = 0f, maxA = 0f, maxT = 0f;
                int worst = -1;
                for (int v = 0; v < n && v < pos.Length; v++)
                {
                    if (System.BitConverter.ToInt32(bytes, 12 + 4 * v) == 0)
                        continue;
                    rebuilt++;
                    float d = Vector3.Distance(w.MultiplyPoint3x4(pos[v]), bp[v]);
                    float a = Vector3.Angle(w.MultiplyVector(normal[v]), bn[v]);
                    sumD += d;
                    sumA += a;
                    if (d > maxD) { maxD = d; worst = v; }
                    maxA = Mathf.Max(maxA, a);
                    if (a > 90f)
                        flipped++;
                    if (bt.Length == n)
                    {
                        float t = Vector3.Angle(w.MultiplyVector(tangent[v]), bt[v]);
                        sumT += t;
                        maxT = Mathf.Max(maxT, t);
                        if (Mathf.Sign(tangent[v].w) != Mathf.Sign(bt[v].w))
                            wFlip++;
                    }
                }
                Debug.Log($"[ROE] {go.name}: surface {s.name} at rest, {rebuilt}/{n} rebuilt vertices vs the import: position mean " +
                          $"{1000 * sumD / Mathf.Max(1, rebuilt):0.00} max {1000 * maxD:0.00} mm (vertex {worst}), normal mean {sumA / Mathf.Max(1, rebuilt):0.0} " +
                          $"max {maxA:0.0} deg ({flipped} flipped), tangent mean {sumT / Mathf.Max(1, rebuilt):0.0} max {maxT:0.0} deg ({wFlip} handedness flips)");
            }
        }

        [MenuItem("ROE Fighter/Build/DOA6 fighter (Kasumi)")]
        public static void Build()
        {
            foreach (var id in RoeCapture.Arg("-roeDoa", "kas").Split(','))
                Build(id.Trim());
            AssetDatabase.SaveAssets();
        }

        public static GameObject Build(string id)
        {
            string dir = Dir(id);
            string sidecarPath = $"{dir}/materials.json";
            if (!File.Exists(sidecarPath))
            {
                Debug.LogError($"[ROE] {id}: no {sidecarPath} - run tools/doa6_fbx.py first");
                return null;
            }
            string json = File.ReadAllText(sidecarPath);
            var sidecar = JsonUtility.FromJson<Sidecar>(json);
            string fbxPath = $"{dir}/{sidecar.fbx}";
            AssetDatabase.Refresh();

            // textures
            int normals = 0;
            foreach (var path in Directory.GetFiles($"{dir}/textures", "*.png").Select(p => p.Replace('\\', '/')))
            {
                var ti = (TextureImporter)AssetImporter.GetAtPath(path);
                if (ti == null)
                    continue;
                bool normal = path.Contains("kidsnmh");
                var want = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                if (ti.textureType != want || (!normal && !ti.alphaIsTransparency && path.Contains("_BLEND_")))
                {
                    ti.textureType = want;
                    ti.sRGBTexture = !normal;
                    if (!normal)
                        ti.alphaIsTransparency = path.Contains("_BLEND_");
                    ti.SaveAndReimport();
                }
                normals += normal ? 1 : 0;
            }

            // materials
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            string matDir = $"{dir}/materials";
            Directory.CreateDirectory(matDir);
            var made = new Dictionary<string, Material>();
            foreach (var info in ReadMaterials(json))
            {
                string matPath = $"{matDir}/{info.name}.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (mat == null)
                {
                    mat = new Material(shader);
                    AssetDatabase.CreateAsset(mat, matPath);
                }
                mat.shader = shader;
                var albedo = info.albedo != null ? AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/textures/{info.albedo}") : null;
                var normal = info.normal != null ? AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/textures/{info.normal}") : null;
                // a 1x1 map is a placeholder: the eye's glass shell (cornea) has one - drawn as a clear, glossy layer
                // (opaque white it hid the irises)
                bool placeholder = albedo != null && albedo.width <= 1;
                if (placeholder)
                    albedo = null;
                if (normal != null && normal.width <= 1)
                    normal = null;
                mat.SetTexture("_BaseMap", albedo);
                mat.SetTexture("_BumpMap", normal);
                string lower = (info.albedo ?? info.name).ToLowerInvariant();
                bool hair = lower.Contains("hair") || lower.Contains("matuge") || lower.Contains("shitamatu");
                bool thin = hair || lower.Contains("ribbon") || lower.Contains("himo");
                bool glass = placeholder && info.alpha;                  // eyeglass_BLEND
                bool overlay = lower.Contains("_es_blend");              // the shade over the eyeball
                mat.SetColor("_BaseColor", glass ? new Color(1f, 1f, 1f, 0f) : Color.white);
                mat.SetFloat("_Smoothness", glass ? 0.95f : lower.Contains("katana") ? 0.7f : lower.Contains("face") || lower.Contains("body") ? 0.45f : 0.35f);
                mat.SetFloat("_Metallic", 0f);
                mat.SetFloat("_Cull", thin ? 0f : 2f);
                // hair and lashes: cut out by their alpha; cornea and eye shade: blended
                bool blended = glass || overlay;
                bool cut = info.alpha && albedo != null && !blended;
                mat.SetFloat("_Surface", blended ? 1f : 0f);
                mat.SetFloat("_Blend", 0f);
                mat.SetFloat("_BlendModePreserveSpecular", glass ? 1f : 0f);
                mat.SetFloat("_AlphaClip", cut ? 1f : 0f);
                mat.SetFloat("_Cutoff", 0.35f);
                mat.renderQueue = -1;
                BaseShaderGUI.SetMaterialKeywords(mat);
                EditorUtility.SetDirty(mat);
                made[info.name] = mat;
            }

            // the model: our materials on it, readable meshes (the soft bodies and the cloth grids rebuild vertices)
            // (reimported only when a setting changes: her 1373-bone FBX takes minutes, and a new FBX is imported by the Refresh)
            var mi = (ModelImporter)AssetImporter.GetAtPath(fbxPath);
            var remaps = mi.GetExternalObjectMap();
            bool reimport = mi.animationType != ModelImporterAnimationType.Generic || mi.optimizeGameObjects || mi.importAnimation ||
                            mi.importBlendShapes || !mi.isReadable || mi.materialImportMode != ModelImporterMaterialImportMode.ImportStandard ||
                            made.Any(kv => !remaps.TryGetValue(new AssetImporter.SourceAssetIdentifier(typeof(Material), kv.Key), out var o) || o != kv.Value);
            if (reimport)
            {
                mi.animationType = ModelImporterAnimationType.Generic;
                mi.optimizeGameObjects = false;
                mi.importAnimation = false;
                mi.importBlendShapes = false;
                mi.isReadable = true;
                mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                foreach (var kv in made)
                    mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), kv.Key), kv.Value);
                mi.SaveAndReimport();
            }
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (!SavePrefabs(id, model, "DOA6", $"{made.Count} materials ({normals} normal maps)"))
                return null;
            // her game's physics onto the fresh prefab (soft-body nodes, chains, colliders: DoaPhysicsBuilder)
            DoaPhysicsBuilder.Build(id);
            return AssetDatabase.LoadAssetAtPath<GameObject>(RoeHumanoid.FighterPath(id));
        }

        /// <summary>
        /// The Generic prefab (Generated/&lt;id&gt;/&lt;id&gt;.prefab) and the humanoid fighter (&lt;id&gt;_fighter.prefab + its avatar) of an
        /// imported model, the ROE way: bones mapped by name (RoeHumanoid.MapBones), the hierarchy normalized, the avatar built on
        /// a T-posed copy.  Shared by the characters from other games (DOA6 here, Vindictus: VdfFighter).  False if the avatar
        /// is not valid.
        /// </summary>
        public static bool SavePrefabs(string id, GameObject model, string game, string materialsNote, bool heels = false)
        {
            // the Generic prefab
            var go = (GameObject)Object.Instantiate(model);
            go.name = id;
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            if (go.GetComponent<Animator>() == null)
                go.AddComponent<Animator>();
            // clips that carry the body metres from the prefab root (Fiona's own: 2-3 m) must not cull it (as RoeFighterBuilder)
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                smr.updateWhenOffscreen = true;
            string prefabPath = RoeFighterBuilder.PrefabPath(id);
            Directory.CreateDirectory(Path.GetDirectoryName(prefabPath));
            PrefabUtility.SaveAsPrefabAsset(go, prefabPath);

            // the humanoid fighter
            go.name = id + "_fighter";
            var map = RoeHumanoid.MapBones(go);
            int moved = RoeHumanoid.NormalizeHierarchy(map);
            var posed = Object.Instantiate(go);
            posed.name = go.name;
            var posedMap = RoeHumanoid.MapBones(posed);
            var forward = RoeShowcase.Forward(posed);
            float corrected = RoeHumanoid.EnforceTPose(posed, posedMap);
            // shoes with heels on a flat-footed bind pose (Vindictus): the avatar's neutral foot stands in them
            string heelNote = heels ? RoeHumanoid.StandOnHeels(posed, posedMap) : "";
            var avatar = RoeHumanoid.BuildAvatar(posed, posedMap);
            avatar.name = id + "_humanoid";
            bool ok = avatar.isValid && avatar.isHuman;
            Object.DestroyImmediate(posed);
            var bounds = new Bounds(go.transform.position, Vector3.zero);
            foreach (var r in go.GetComponentsInChildren<Renderer>())
                bounds.Encapsulate(r.bounds);
            if (!ok)
            {
                Debug.LogError($"[ROE] {id}: humanoid avatar is NOT valid (valid={avatar.isValid}, human={avatar.isHuman}); mapped {string.Join(" ", map.Keys)}");
                Object.DestroyImmediate(go);
                return false;
            }
            string avatarPath = RoeHumanoid.AvatarPath(id);
            AssetDatabase.DeleteAsset(avatarPath);
            AssetDatabase.CreateAsset(avatar, avatarPath);
            var animator = go.GetComponent<Animator>();
            animator.avatar = avatar;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var saved = PrefabUtility.SaveAsPrefabAsset(go, RoeHumanoid.FighterPath(id));
            int renderers = go.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length;
            int bones = go.GetComponentsInChildren<Transform>(true).Length;
            Object.DestroyImmediate(go);
            Debug.Log($"[ROE] {id}: {game} fighter {RoeHumanoid.FighterPath(id)} + avatar {avatarPath}: {map.Count} human bones, {moved} re-parented, " +
                      $"faces {forward}, largest T-pose correction {corrected:F1} deg{(heelNote.Length > 0 ? ", feet on heels: " + heelNote : "")}; {renderers} skinned renderers, {bones} nodes, " +
                      $"{materialsNote}, size {bounds.size.x:F2} x {bounds.size.y:F2} x {bounds.size.z:F2} m, " +
                      $"lowest {bounds.min.y:F3} m");
            return saved != null;
        }
    }
}
