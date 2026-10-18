using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace RoeFighter.EditorTools
{
    /// <summary>
    /// 爆衣 (clothes burst) step 1, the editor half: which pieces of an outfit can come off, and in which
    /// stage.  The rules are a table per character, Assets/RoeFighter/Editor/Burst/&lt;id&gt;.json:
    ///   outfit   the renderers (and submesh, -1 all) that hold the outfit;
    ///   groups   in order, the first that matches a piece decides: name, stage (1 first, 0 stays on),
    ///            cloth (drops and lies down) or armour (flies), together (else one piece per side), and the
    ///            conditions - bone (regex on the bone that carries most of the piece's weight), anyBone (bones
    ///            that together carry at least anyShare, default 0.1), renderer, material (regex on the names of the
    ///            piece's materials: Eve's outfits are one mesh, her suit and her armour told apart by material),
    ///            min/maxTriangles, lowestBelow/Above (m above the floor of its lowest vertex in the bind pose).
    /// A piece is a connected part of the outfit (triangles that share a vertex position, welded at 0.1 mm).
    ///
    /// Apply(model, id) splits a fighter: the pieces of each group and side become a SkinnedMeshRenderer of
    /// their own on the same bones, materials and bounds (so nothing looks different), the rest of the
    /// outfit stays on the original renderer with a mesh that lacks them; the meshes are saved in
    /// Generated/&lt;id&gt;/burst (rewritten in place, so scenes keep their links).  It adds RoeClothesBurst with
    /// the list.  The fight scene calls it for each fighter (RoeFightScene.MakeFighter).
    ///   Check    -executeMethod RoeFighter.EditorTools.RoeBurstBuilder.Check [-roeChars a08,g04] [-roeOut dir]
    ///            pictures in the stance: each stage with the game's materials, and the groups in colours
    ///            (out/clothes_burst/unity; tools/burst_sheet.py puts them on one sheet).
    /// </summary>
    public static class RoeBurstBuilder
    {
        public const string RulesDir = "Assets/RoeFighter/Editor/Burst";
        public static string RulesPath(string id) => $"{RulesDir}/{id}.json";
        public static string OutDir(string id) => $"{RoeFighterBuilder.OutDir}/{id}/burst";
        static string ProjectDir => Path.GetDirectoryName(Application.dataPath);

        [Serializable]
        public class Rules
        {
            public string id, note;
            public Outfit[] outfit;
            public Group[] groups;
            public Nude nude;                       // the family's nude body in place of the suit's skin (RoeNudeBody)
        }

        [Serializable]
        public class Nude
        {
            public string prefab, renderer;         // the nude base and its body renderer
            public int submesh;                     // the body (the base's other submeshes are the head, eyes, lashes)
            public string material;
            public int[] submeshes;                 // or several, each with its material (materials, in the same order): Fiona's
            public string[] materials;              // body and her hands and feet are two textures
            public string attachTo;                 // the suit renderer it goes next to (its space, bones and settings)
            public Outfit[] replace;                // the suit's skin it replaces (renderer + submesh)
            public Fill[] fill;                     // more of the base where the suit has nothing (the neck under a collar)
            public string pose;                     // "bones": carried onto the suit's bind pose by its own weights, bones by name
                                                    // (a base on the suit's own skeleton: Eve's); else where the base has it
            public bool reveal;                     // the body under the outfit's opaque pieces drawn only once they are off (a base
                                                    // fuller than the outfit: Eve's would come out through her suits)
            public bool fit;                        // where pieces that never come off lie (shoes), the base takes the shape of the
                                                    // suit's skin it replaces (her flat bare feet in eve37's heels)
            public string ownWeights;               // regex: where the suit's skin does not lie on it, a vertex the base hangs mostly on
                                                    // such a bone (the suit's, by name) keeps the base's own weights (b14's right upper
                                                    // arm: no skin there under the robe, and the sleeve over it hangs on the forearm)
            public bool ownOverSkin;                // ... where the suit's skin lies on it too (b14's right shoulder: the skin under the
                                                    // robe reaches the joint but hangs on the spine and the clavicle only)
            public bool ownMirror;                  // ... with the suit's skin's weights from her other side, mirrored, in place of
                                                    // the base's own (b14's left shoulder is bare in her game and weighted with care)
        }

        /// <summary>
        /// The base's triangles of another submesh that the suit lacks: the suit's head is the base's head cut off
        /// under its collar (b10's turtleneck, g05's gold collar), so without the collar the neck was a hole.  These
        /// are added with the material of the first suit renderer named (the face's).
        /// </summary>
        [Serializable]
        public class Fill
        {
            public int submesh;                     // the base's submesh (its head, neck included)
            public Outfit[] missingFrom;            // the suit renderers it is compared with: what they have is not taken again
        }

        [Serializable]
        public class Outfit
        {
            public string renderer;
            public int submesh = -1;
            public string material;                 // or the submeshes whose material's name matches (regex): an FBX keeps its own
                                                    // submesh order, the names stay
        }

        /// <summary>The submeshes of r an outfit entry names: by material when it has one, else its submesh, else all.</summary>
        internal static int[] Subs(Outfit o, SkinnedMeshRenderer r)
        {
            int count = r.sharedMesh.subMeshCount;
            if (!string.IsNullOrEmpty(o.material))
            {
                var mats = r.sharedMaterials;
                return Enumerable.Range(0, count).Where(s => s < mats.Length && mats[s] != null && Regex.IsMatch(mats[s].name, o.material)).ToArray();
            }
            return o.submesh >= 0 ? new[] { o.submesh } : Enumerable.Range(0, count).ToArray();
        }

        [Serializable]
        public class Group
        {
            public string name, title;     // title: shown to people (captions, the check picture); the name if empty
            public int stage;
            public bool cloth, together;
            public bool fade;              // paint on the skin (m02's vines are in her skin texture): fades out where it is, nothing flies
            public bool followBody;        // worn tight on the body: its pieces take the weights of the nude body under them (FollowBody)
            public string renderer, bone, anyBone, material;
            public float anyShare;
            public int minTriangles, maxTriangles;
            public float lowestBelow, lowestAbove;
        }

        /// <summary>One connected piece of the outfit.</summary>
        public class Part
        {
            public SkinnedMeshRenderer renderer;
            public readonly Dictionary<int, List<int>> triangles = new Dictionary<int, List<int>>();   // submesh -> triangle numbers in it
            public string materials = "";                        // the names of its materials, space-separated
            public string bone;                                  // carries most of its weight
            public Dictionary<string, float> share;              // bone -> share of its weight
            public Vector3 centre;                               // model space, bind pose
            public float lowest, highest;                        // m above the floor
            public string side;                                  // L, M, R: her left, the middle, her right
            public Group group;                                  // null: no rule matched (stays on)
            public int Triangles => triangles.Values.Sum(l => l.Count);
        }

        public class Analysis
        {
            public Rules rules;
            public readonly List<Part> parts = new List<Part>();
            public float floor;
            public Vector3 pelvis, right;
        }

        public static Rules Load(string id)
        {
            string path = Path.Combine(ProjectDir, RulesPath(id));
            if (!File.Exists(path))
                path = Path.Combine(ProjectDir, RulesPath(RoeManifest.SuitOf(id)));     // a recolour comes off as its suit does
            if (!File.Exists(path))
                return null;
            var rules = JsonUtility.FromJson<Rules>(File.ReadAllText(path));
            if (rules.outfit == null || rules.groups == null)
                throw new Exception($"{path}: needs \"outfit\" and \"groups\"");
            return rules;
        }

        static Matrix4x4 ToModel(SkinnedMeshRenderer r, Transform root) => root.worldToLocalMatrix * r.transform.localToWorldMatrix;

        static bool IsWeapon(Renderer r) => r.sharedMaterials.Any(m => m != null && m.name.StartsWith("wp_"));

        /// <summary>The outfit's pieces and the group each falls into (bind pose, model space: the renderers where the prefab has them).</summary>
        public static Analysis Analyse(GameObject model, Rules rules)
        {
            var a = new Analysis { rules = rules };
            var root = model.transform;
            var renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r => r.sharedMesh != null && !IsWeapon(r)).ToArray();
            // the floor: the lowest vertex of the body (the soles)
            a.floor = float.PositiveInfinity;
            foreach (var r in renderers)
            {
                var m = ToModel(r, root);
                foreach (var v in r.sharedMesh.vertices)
                    a.floor = Mathf.Min(a.floor, m.MultiplyPoint3x4(v).y);
            }
            // the pelvis, and which way is her right (from the thighs), from the bind poses
            Vector3? BindBone(string name)
            {
                foreach (var r in renderers)
                {
                    int i = Array.FindIndex(r.bones, b => b != null && b.name == name);
                    var poses = r.sharedMesh.bindposes;
                    if (i >= 0 && i < poses.Length)
                        return ToModel(r, root).MultiplyPoint3x4(poses[i].inverse.MultiplyPoint3x4(Vector3.zero));
                }
                return null;
            }
            var pelvis = BindBone("Bip001 Pelvis");
            var leftThigh = BindBone("Bip001 L Thigh");
            var rightThigh = BindBone("Bip001 R Thigh");
            a.pelvis = pelvis ?? Vector3.up * (a.floor + 0.95f);
            a.right = leftThigh != null && rightThigh != null ? Vector3.ProjectOnPlane(rightThigh.Value - leftThigh.Value, Vector3.up).normalized : Vector3.right;

            foreach (var o in rules.outfit)
            {
                var r = renderers.FirstOrDefault(x => x.name == o.renderer);
                if (r == null)
                {
                    Debug.LogWarning($"[ROE] burst {rules.id}: no renderer '{o.renderer}'");
                    continue;
                }
                var mesh = r.sharedMesh;
                var toModel = ToModel(r, root);
                var at = mesh.vertices.Select(v => toModel.MultiplyPoint3x4(v)).ToArray();
                // welded by position: the same point of a seam is one vertex
                var weld = new int[at.Length];
                var ids = new Dictionary<Vector3Int, int>();
                for (int i = 0; i < at.Length; i++)
                {
                    var key = Vector3Int.RoundToInt(at[i] * 10000f);
                    if (!ids.TryGetValue(key, out int id))
                        ids[key] = id = ids.Count;
                    weld[i] = id;
                }
                var parent = Enumerable.Range(0, ids.Count).ToArray();
                int Find(int x)
                {
                    while (parent[x] != x)
                        x = parent[x] = parent[parent[x]];
                    return x;
                }
                var subs = Subs(o, r);
                if (subs.Length == 0 || subs.Any(s => s >= mesh.subMeshCount))
                {
                    Debug.LogWarning($"[ROE] burst {rules.id}: '{o.renderer}' has {mesh.subMeshCount} submeshes, no {o.submesh}" +
                                     (string.IsNullOrEmpty(o.material) ? "" : $" / none with a material like '{o.material}'"));
                    continue;
                }
                var tris = subs.ToDictionary(s => s, s => mesh.GetTriangles(s));
                foreach (var t in tris.Values)
                    for (int k = 0; k + 2 < t.Length; k += 3)
                    {
                        int ra = Find(weld[t[k]]), rb = Find(weld[t[k + 1]]), rc = Find(weld[t[k + 2]]);
                        parent[ra] = rb;
                        parent[Find(rc)] = Find(rb);
                    }
                var byRoot = new Dictionary<int, Part>();
                foreach (var kv in tris)
                    for (int k = 0; k + 2 < kv.Value.Length; k += 3)
                    {
                        int root_ = Find(weld[kv.Value[k]]);
                        if (!byRoot.TryGetValue(root_, out var part))
                            byRoot[root_] = part = new Part { renderer = r };
                        if (!part.triangles.TryGetValue(kv.Key, out var list))
                            part.triangles[kv.Key] = list = new List<int>();
                        list.Add(k / 3);
                    }
                // each piece: the bones that carry it, where it is
                var perVertex = mesh.GetBonesPerVertex();
                var weights = mesh.GetAllBoneWeights();
                var start = new int[mesh.vertexCount + 1];
                for (int i = 0; i < mesh.vertexCount && i < perVertex.Length; i++)
                    start[i + 1] = start[i] + perVertex[i];
                var bones = r.bones;
                foreach (var part in byRoot.Values)
                {
                    var used = new HashSet<int>();
                    foreach (var kv in part.triangles)
                        foreach (int t in kv.Value)
                        {
                            used.Add(tris[kv.Key][t * 3]);
                            used.Add(tris[kv.Key][t * 3 + 1]);
                            used.Add(tris[kv.Key][t * 3 + 2]);
                        }
                    var sum = new Dictionary<string, float>();
                    var centre = Vector3.zero;
                    float lo = float.PositiveInfinity, hi = float.NegativeInfinity;
                    foreach (int v in used)
                    {
                        centre += at[v];
                        lo = Mathf.Min(lo, at[v].y);
                        hi = Mathf.Max(hi, at[v].y);
                        if (v >= perVertex.Length)
                            continue;
                        for (int j = start[v]; j < start[v + 1]; j++)
                        {
                            var w = weights[j];
                            string name = w.boneIndex < bones.Length && bones[w.boneIndex] != null ? bones[w.boneIndex].name : $"#{w.boneIndex}";
                            sum[name] = (sum.TryGetValue(name, out float s) ? s : 0f) + w.weight;
                        }
                    }
                    float total = Mathf.Max(1e-6f, sum.Values.Sum());
                    part.share = sum.ToDictionary(kv => kv.Key, kv => kv.Value / total);
                    part.bone = sum.Count > 0 ? sum.OrderByDescending(kv => kv.Value).First().Key : "";
                    part.centre = centre / Mathf.Max(1, used.Count);
                    part.lowest = lo - a.floor;
                    part.highest = hi - a.floor;
                    float across = Vector3.Dot(part.centre - a.pelvis, a.right);
                    part.side = across > 0.03f ? "R" : across < -0.03f ? "L" : "M";
                    var mats = r.sharedMaterials;
                    part.materials = string.Join(" ", part.triangles.Keys.OrderBy(k => k)
                        .Select(k => mats.Length > 0 && mats[Mathf.Min(k, mats.Length - 1)] != null ? mats[Mathf.Min(k, mats.Length - 1)].name : "-"));
                    part.group = rules.groups.FirstOrDefault(g => Matches(g, part));
                    a.parts.Add(part);
                }
            }
            return a;
        }

        static bool Matches(Group g, Part p)
        {
            if (!string.IsNullOrEmpty(g.renderer) && g.renderer != p.renderer.name)
                return false;
            if (!string.IsNullOrEmpty(g.material) && !Regex.IsMatch(p.materials, g.material))
                return false;
            if (!string.IsNullOrEmpty(g.bone) && !Regex.IsMatch(p.bone, g.bone))
                return false;
            if (!string.IsNullOrEmpty(g.anyBone) && p.share.Where(kv => Regex.IsMatch(kv.Key, g.anyBone)).Sum(kv => kv.Value) < (g.anyShare > 0f ? g.anyShare : 0.1f))
                return false;
            if (g.minTriangles > 0 && p.Triangles < g.minTriangles)
                return false;
            if (g.maxTriangles > 0 && p.Triangles > g.maxTriangles)
                return false;
            if (g.lowestBelow > 0f && p.lowest >= g.lowestBelow)
                return false;
            if (g.lowestAbove > 0f && p.lowest <= g.lowestAbove)
                return false;
            return true;
        }

        /// <summary>
        /// Split a fighter's outfit by the rules (see the class comment).  Null when the character has no
        /// rules.  Run it on a fresh instance of the fighter prefab.
        /// </summary>
        public static RoeClothesBurst Apply(GameObject model, string id)
        {
            var rules = Load(id);
            if (rules == null)
                return null;
            var a = Analyse(model, rules);
            string dir = OutDir(id);
            if (!AssetDatabase.IsValidFolder(dir))
            {
                Directory.CreateDirectory(Path.Combine(ProjectDir, dir));
                AssetDatabase.Refresh();
            }
            var burst = model.GetComponent<RoeClothesBurst>();
            if (burst == null)
                burst = model.AddComponent<RoeClothesBurst>();
            burst.pieces.Clear();
            burst.reveals.Clear();
            var log = new StringBuilder($"[ROE] burst {id}: floor {a.floor:F3}, pelvis {a.pelvis}, right {a.right}; {a.parts.Count} pieces in the outfit");
            // the family's nude body under the outfit, in place of the suit's skin (before anything is split)
            var nude = RoeNudeBody.Build(model, id, rules, a, dir, log, burst.reveals);
            var replaced = new Dictionary<SkinnedMeshRenderer, HashSet<int>>();      // renderer -> submeshes gone whole
            if (nude != null)
                foreach (var s in rules.nude.replace ?? new Outfit[0])
                {
                    var r = model.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(x => x.name == s.renderer);
                    if (r == null)
                        continue;
                    if (!replaced.TryGetValue(r, out var set))
                        replaced[r] = set = new HashSet<int>();
                    set.UnionWith(Subs(s, r));
                }

            // the pieces of one group on one side of one renderer come off together
            var units = a.parts.Where(p => p.group != null && p.group.stage > 0)
                .GroupBy(p => (renderer: p.renderer, group: p.group, side: p.group.together ? "" : p.side))
                .OrderBy(g => g.Key.group.stage).ThenBy(g => Array.IndexOf(rules.groups, g.Key.group)).ThenBy(g => g.Key.side)
                .ToList();
            var taken = new Dictionary<SkinnedMeshRenderer, List<Part>>();
            foreach (var u in units)
            {
                var r = u.Key.renderer;
                var src = r.sharedMesh;
                string name = u.Key.group.name + (u.Key.side.Length > 0 ? " " + u.Key.side : "");
                var subs = u.SelectMany(p => p.triangles.Keys).Distinct().OrderBy(s => s).ToArray();
                var lists = subs.Select(s =>
                {
                    var t = src.GetTriangles(s);
                    var list = new List<int>();
                    foreach (var p in u)
                        if (p.triangles.TryGetValue(s, out var nums))
                            foreach (int n in nums)
                                list.AddRange(new[] { t[n * 3], t[n * 3 + 1], t[n * 3 + 2] });
                    return list;
                }).ToArray();
                string piecePath = $"{dir}/{Safe(r.name)}__{Safe(name)}.asset";
                var sub = Subset(src, lists, MeshAsset(piecePath));
                string followed = u.Key.group.followBody && nude != null ? "; " + FollowBody(sub, r, nude, model.transform) : "";
                var mesh = Commit(sub, piecePath);
                var mats = r.sharedMaterials;
                var smr = AddRenderer(r, "burst " + name, mesh, subs.Select(s => mats[Mathf.Min(s, mats.Length - 1)]).ToArray());
                int count = lists.Sum(l => l.Count) / 3;
                burst.pieces.Add(new RoeClothesBurst.Piece
                {
                    name = name, group = u.Key.group.name, title = string.IsNullOrEmpty(u.Key.group.title) ? u.Key.group.name : u.Key.group.title,
                    stage = u.Key.group.stage, cloth = u.Key.group.cloth, fade = u.Key.group.fade, renderer = smr, triangles = count,
                });
                if (!taken.TryGetValue(r, out var list_))
                    taken[r] = list_ = new List<Part>();
                list_.AddRange(u);
                log.Append($"\n[ROE]   stage {u.Key.group.stage} {(u.Key.group.fade ? "fade  " : u.Key.group.cloth ? "cloth " : "armour")} {name,-22} {u.Count(),4} pieces {count,6} triangles  " +
                           $"bones {string.Join(" ", u.Select(p => p.bone).Distinct().Take(6))}{followed}");
            }
            // the shadows that pieces coming off earlier baked into the occlusion of the pieces under them
            string occlusion = CleanOcclusion(model, burst, dir);
            if (occlusion.Length > 0)
                log.Append($"\n[ROE]   occlusion of what lay under earlier pieces lifted: {occlusion}");
            // what stays on each renderer (the suit's skin the nude body replaces goes as well)
            foreach (var r in replaced.Keys)
                if (!taken.ContainsKey(r))
                    taken[r] = new List<Part>();
            foreach (var kv in taken)
            {
                var r = kv.Key;
                var src = r.sharedMesh;
                var gone = new Dictionary<int, HashSet<int>>();
                foreach (var p in kv.Value)
                    foreach (var t in p.triangles)
                    {
                        if (!gone.TryGetValue(t.Key, out var set))
                            gone[t.Key] = set = new HashSet<int>();
                        set.UnionWith(t.Value);
                    }
                var lists = Enumerable.Range(0, src.subMeshCount).Select(s =>
                {
                    var t = src.GetTriangles(s);
                    var list = new List<int>(t.Length);
                    gone.TryGetValue(s, out var set);
                    if (replaced.TryGetValue(r, out var whole) && whole.Contains(s))
                        return list;
                    for (int n = 0; n * 3 + 2 < t.Length; n++)
                        if (set == null || !set.Contains(n))
                            list.AddRange(new[] { t[n * 3], t[n * 3 + 1], t[n * 3 + 2] });
                    return list;
                }).ToArray();
                int before = src.triangles.Length / 3, after = lists.Sum(l => l.Count) / 3;
                string staysPath = $"{dir}/{Safe(r.name)}__stays.asset";
                r.sharedMesh = Commit(Subset(src, lists, MeshAsset(staysPath)), staysPath);
                log.Append($"\n[ROE]   {r.name}: {before} -> {after} triangles stay on it ({src.name} -> {r.sharedMesh.name})");
            }
            // what stays on, and pieces no rule took
            foreach (var g in a.parts.Where(p => p.group != null && p.group.stage <= 0).GroupBy(p => p.group.name))
                log.Append($"\n[ROE]   stays on (rule): {g.Key}, {g.Count()} pieces, {g.Sum(p => p.Triangles)} triangles");
            var free = a.parts.Where(p => p.group == null).ToList();
            if (free.Count > 0)
                log.Append($"\n[ROE]   stays on (no rule): {free.Count} pieces, {free.Sum(p => p.Triangles)} triangles - " +
                           string.Join(", ", free.GroupBy(p => p.bone).OrderByDescending(g => g.Sum(p => p.Triangles))
                               .Select(g => $"{g.Key} x{g.Count()} ({g.Sum(p => p.Triangles)})")));
            burst.stages = burst.pieces.Count > 0 ? burst.pieces.Max(p => p.stage) : 0;
            AssetDatabase.SaveAssets();
            Debug.Log(log.ToString());
            return burst;
        }

        internal static string Safe(string s) => Regex.Replace(s, @"[^A-Za-z0-9_\-]+", "_");

        // ---- the occlusion a piece casts on what lies under it

        const float UnderReach = 0.03f;     // a piece's triangle this far along its normal (either way) lies over a vertex of another
        const float UnderAside = 0.004f;    //   when the vertex is this close to right under it
        const float ShadowSpread = 0.01f;   // a baked shadow reaches this far round what casts it (m, in texels by the triangles' density)
        const float RingWidth = 0.02f;      // the occlusion round it is read from a ring this wide (m)

        /// <summary>
        /// Game artists bake the shadow a piece casts on what lies under it into the ambient occlusion of what is under: eve37's
        /// tie lies on her shirt's placket, and the placket's occlusion holds a tie-shaped shadow (0.1-0.4 against 0.6 round
        /// it) that Stellar Blade never shows - in the fight, lit from above by the stage's spotlights and so mostly by the
        /// ambient light the occlusion darkens, it was a black tie on the shirt once the tie was off.  So for each piece, the
        /// texels of its triangles under an opaque piece that comes off earlier (a corner within 4 mm of right under one of its
        /// triangles, 3 cm along its normal, facing the same way), spread by the shadow's soft edge (1 cm), are raised to the
        /// median occlusion of a 2 cm ring round them (texels of the piece's own triangles); the piece gets a copy of its material with
        /// that occlusion map (Assets/RoeFighter/Generated/&lt;id&gt;/burst/&lt;renderer&gt;__&lt;material&gt;.mat, the map next to it).
        /// While the piece over it is on, nothing of this shows.  Returns a note for the log ("" when nothing was lifted).
        /// </summary>
        static string CleanOcclusion(GameObject model, RoeClothesBurst burst, string dir)
        {
            var root = model.transform;
            Matrix4x4 ToModel(Transform t) => root.worldToLocalMatrix * t.localToWorldMatrix;
            var notes = new List<string>();
            foreach (var p in burst.pieces)
            {
                var r = p.renderer;
                if (r == null || r.sharedMesh == null)
                    continue;
                // the opaque triangles of the pieces that come off before it, model space as modelled (lace and see-through
                // plastic cast no solid shadow, and what is under them shows through them while they are on; of a cut-out
                // material the triangles that are solid: RoeNudeBody.SolidTriangles)
                var over = new List<(Vector3 a, Vector3 b, Vector3 c, Vector3 n, int stage)>();
                foreach (var q in burst.pieces.Where(x => x.stage < p.stage && x.renderer != null && x.renderer.sharedMesh != null))
                {
                    var qm = q.renderer.sharedMesh;
                    var qv = qm.vertices;
                    var qn = qm.normals;
                    var qmats = q.renderer.sharedMaterials;
                    var toModelQ = ToModel(q.renderer.transform);
                    for (int qs = 0; qs < qm.subMeshCount; qs++)
                    {
                        var solid = RoeNudeBody.SolidTriangles(qmats[Mathf.Min(qs, qmats.Length - 1)], qm, qs);
                        if (solid == null)
                            continue;
                        var qt = qm.GetTriangles(qs);
                        for (int k = 0; k + 2 < qt.Length; k += 3)
                        {
                            if (!solid[k / 3])
                                continue;
                            Vector3 a = toModelQ.MultiplyPoint3x4(qv[qt[k]]), b = toModelQ.MultiplyPoint3x4(qv[qt[k + 1]]), c = toModelQ.MultiplyPoint3x4(qv[qt[k + 2]]);
                            var fn = Vector3.Cross(b - a, c - a);
                            if (fn.sqrMagnitude < 1e-14f)
                                continue;
                            fn.Normalize();
                            if (qn.Length == qv.Length && Vector3.Dot(fn, toModelQ.MultiplyVector(qn[qt[k]] + qn[qt[k + 1]] + qn[qt[k + 2]])) < 0f)
                                fn = -fn;
                            over.Add((a, b, c, fn, q.stage));
                        }
                    }
                }
                if (over.Count == 0)
                    continue;
                var cover = new RoeNudeBody.Cover(over);
                var mesh = r.sharedMesh;
                var v = mesh.vertices;
                var vn = mesh.normals;
                var uv = mesh.uv;
                if (uv.Length != v.Length)
                    continue;
                var toModel = ToModel(r.transform);
                var pos = v.Select(x => toModel.MultiplyPoint3x4(x)).ToArray();
                var under = new bool[v.Length];
                for (int i = 0; i < v.Length; i++)
                    under[i] = cover.Stage(pos[i], vn.Length == v.Length ? toModel.MultiplyVector(vn[i]).normalized : Vector3.zero, UnderReach, UnderAside) > 0;
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int sub = 0; sub < mesh.subMeshCount && sub < mats.Length; sub++)
                {
                    var mat = mats[sub];
                    var occ = mat != null && mat.HasProperty("_OcclusionMap") ? mat.GetTexture("_OcclusionMap") as Texture2D : null;
                    if (occ == null)
                        continue;
                    var tris = mesh.GetTriangles(sub);
                    var footprint = new List<int>();
                    for (int k = 0; k + 2 < tris.Length; k += 3)
                        if (under[tris[k]] || under[tris[k + 1]] || under[tris[k + 2]])
                            footprint.AddRange(new[] { tris[k], tris[k + 1], tris[k + 2] });
                    if (footprint.Count == 0)
                        continue;
                    string baseName = $"{dir}/{Safe(r.name)}__{Safe(mat.name)}";
                    var lifted = LiftOcclusion(occ, uv, pos, tris, footprint, baseName + "_occlusion.png", out string note);
                    if (lifted == null)
                    {
                        notes.Add($"{p.name}: {note}");
                        continue;
                    }
                    string matPath = baseName + ".mat";
                    var copy = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                    if (copy == null)
                    {
                        copy = new Material(mat);
                        AssetDatabase.CreateAsset(copy, matPath);
                    }
                    else
                        copy.CopyPropertiesFromMaterial(mat);
                    copy.shader = mat.shader;
                    copy.shaderKeywords = mat.shaderKeywords;
                    copy.SetTexture("_OcclusionMap", lifted);
                    EditorUtility.SetDirty(copy);
                    mats[sub] = copy;
                    changed = true;
                    notes.Add($"{p.name} ({mat.name}): {footprint.Count / 3} triangles under earlier pieces, {note}");
                }
                if (changed)
                    r.sharedMaterials = mats;
            }
            return string.Join("; ", notes);
        }

        /// <summary>
        /// The occlusion map (green, as URP reads it) with the texels of the footprint triangles, spread by ShadowSpread, raised
        /// to at least the median of a RingWidth ring round each connected part of them (texels of the island's triangles
        /// only), written as a png next to the burst's meshes with the original's import settings.  Distances become texels by
        /// the footprint's own density (its texels per metre: UV area against area as modelled).  Null when nothing changed.
        /// </summary>
        static Texture2D LiftOcclusion(Texture2D occ, Vector2[] uv, Vector3[] pos, int[] island, List<int> footprint, string path, out string note)
        {
            note = "";
            string src = AssetDatabase.GetAssetPath(occ);
            string ext = Path.GetExtension(src).ToLowerInvariant();
            if (ext != ".png" && ext != ".jpg" && ext != ".jpeg")
            {
                note = $"{occ.name} is no png or jpg - left as it is";
                return null;
            }
            var img = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            if (!img.LoadImage(File.ReadAllBytes(Path.Combine(ProjectDir, src))))
            {
                note = $"{src} unreadable - left as it is";
                return null;
            }
            int w = img.width, h = img.height;
            var px = img.GetPixels32();
            var own = new bool[w * h];
            Raster(island, uv, w, h, own);
            var lift = new bool[w * h];
            Raster(footprint, uv, w, h, lift);
            double uvArea = 0, worldArea = 0;
            for (int k = 0; k + 2 < footprint.Count; k += 3)
            {
                Vector2 a = Vector2.Scale(uv[footprint[k]], new Vector2(w, h)), b = Vector2.Scale(uv[footprint[k + 1]], new Vector2(w, h)),
                        c = Vector2.Scale(uv[footprint[k + 2]], new Vector2(w, h));
                uvArea += Mathf.Abs((b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y)) * 0.5;
                worldArea += Vector3.Cross(pos[footprint[k + 1]] - pos[footprint[k]], pos[footprint[k + 2]] - pos[footprint[k]]).magnitude * 0.5;
            }
            float density = worldArea > 1e-9 ? (float)System.Math.Sqrt(uvArea / worldArea) : Mathf.Max(w, h);   // texels per metre
            int spread = Mathf.Clamp(Mathf.RoundToInt(ShadowSpread * density), 1, 64);
            int ringWidth = Mathf.Clamp(Mathf.RoundToInt(RingWidth * density), 2, 128);
            // the footprint and the soft edge of its shadow (own texels only), its parts labelled
            var label = new int[w * h];
            var queue = new Queue<int>();
            var dist = new int[w * h];
            for (int i = 0; i < w * h; i++)
            {
                dist[i] = -1;
                if (lift[i])
                {
                    dist[i] = 0;
                    queue.Enqueue(i);
                }
            }
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                if (dist[i] >= spread)
                    continue;
                int x = i % w, y = i / w;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h)
                            continue;
                        int j = ny * w + nx;
                        if (dist[j] >= 0 || !own[j])
                            continue;
                        dist[j] = dist[i] + 1;
                        lift[j] = true;
                        queue.Enqueue(j);
                    }
            }
            int parts = 0;
            for (int i = 0; i < w * h; i++)
            {
                if (!lift[i] || label[i] != 0)
                    continue;
                label[i] = ++parts;
                queue.Enqueue(i);
                while (queue.Count > 0)
                {
                    int k = queue.Dequeue(), x = k % w, y = k / w;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || ny < 0 || nx >= w || ny >= h)
                                continue;
                            int j = ny * w + nx;
                            if (lift[j] && label[j] == 0)
                            {
                                label[j] = parts;
                                queue.Enqueue(j);
                            }
                        }
                }
            }
            if (parts == 0)
            {
                note = "no texels under them";
                return null;
            }
            // the ring round each part: own texels outside the parts, labelled by the nearest part
            var ringOf = new int[w * h];
            var ringDist = new int[w * h];
            for (int i = 0; i < w * h; i++)
            {
                ringDist[i] = -1;
                if (label[i] != 0)
                {
                    ringDist[i] = 0;
                    ringOf[i] = label[i];
                    queue.Enqueue(i);
                }
            }
            var samples = new List<byte>[parts + 1];
            for (int k = 1; k <= parts; k++)
                samples[k] = new List<byte>();
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                if (ringDist[i] >= ringWidth)
                    continue;
                int x = i % w, y = i / w;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h)
                            continue;
                        int j = ny * w + nx;
                        if (ringDist[j] >= 0)
                            continue;
                        ringDist[j] = ringDist[i] + 1;
                        ringOf[j] = ringOf[i];
                        queue.Enqueue(j);
                        if (own[j])
                            samples[ringOf[j]].Add(px[j].g);
                    }
            }
            var level = new byte[parts + 1];
            int raised = 0;
            float before = 0f, after = 0f;
            for (int k = 1; k <= parts; k++)
            {
                var s = samples[k];
                s.Sort();
                level[k] = s.Count > 0 ? s[s.Count / 2] : (byte)255;
            }
            for (int i = 0; i < w * h; i++)
            {
                if (label[i] == 0 || px[i].g >= level[label[i]])
                    continue;
                before += px[i].g;
                px[i].g = level[label[i]];
                after += px[i].g;
                raised++;
            }
            if (raised == 0)
            {
                note = $"{occ.name} already as light there";
                return null;
            }
            img.SetPixels32(px);
            File.WriteAllBytes(Path.Combine(ProjectDir, path), img.EncodeToPNG());
            Object.DestroyImmediate(img);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            if (AssetImporter.GetAtPath(src) is TextureImporter from && AssetImporter.GetAtPath(path) is TextureImporter to)
            {
                var settings = new TextureImporterSettings();
                from.ReadTextureSettings(settings);
                to.SetTextureSettings(settings);
                to.SetPlatformTextureSettings(from.GetDefaultPlatformTextureSettings());
                to.SaveAndReimport();
            }
            note = $"{raised} texels of {occ.name} raised from {before / raised / 255f:F2} to {after / raised / 255f:F2} on average " +
                   $"({parts} part{(parts > 1 ? "s" : "")}, {density / 100f:F0} texels/cm: spread {spread} px, ring {ringWidth} px)";
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Marks the texels whose centres lie in the triangles' UVs (each triangle moved by whole tiles into 0..1).</summary>
        static void Raster(IList<int> tris, Vector2[] uv, int w, int h, bool[] into)
        {
            for (int k = 0; k + 2 < tris.Count; k += 3)
            {
                Vector2 a = uv[tris[k]], b = uv[tris[k + 1]], c = uv[tris[k + 2]];
                var tile = new Vector2(Mathf.Floor(a.x), Mathf.Floor(a.y));
                a = Vector2.Scale(a - tile, new Vector2(w, h));
                b = Vector2.Scale(b - tile, new Vector2(w, h));
                c = Vector2.Scale(c - tile, new Vector2(w, h));
                float area = (b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y);
                if (Mathf.Abs(area) < 1e-9f)
                    continue;
                int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x)))), x1 = Mathf.Min(w - 1, Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))));
                int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y)))), y1 = Mathf.Min(h - 1, Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))));
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float px = x + 0.5f, py = y + 0.5f;
                        float w0 = ((b.x - px) * (c.y - py) - (c.x - px) * (b.y - py)) / area;
                        float w1 = ((c.x - px) * (a.y - py) - (a.x - px) * (c.y - py)) / area;
                        float w2 = 1f - w0 - w1;
                        if (w0 >= -1e-4f && w1 >= -1e-4f && w2 >= -1e-4f)
                            into[y * w + x] = true;
                    }
            }
        }

        /// <summary>
        /// A piece worn tight on the body takes the weights of the nude body under it (a group's "followBody"), so that it
        /// moves exactly as the skin it lies on, whatever moves the bones: a breast spring moving the end of the chain alone
        /// pushed the skin up to 12 mm out through a08's bra armour, which the game weighted for its own hand-keyed bounce
        /// (RoeBodyCheck, 10-05).  Within near of the body all of the body's weights, from near to far blended back to the
        /// piece's own, further out its own; bones the piece's renderer does not have are left out.  The body's weights at a
        /// point: its 6 nearest vertices, by inverse square distance (a breast's weights change fast across it: one nearest
        /// vertex left the cup up to 2 cm off the skin under it when the end of the chain moved 3 cm).
        /// </summary>
        static string FollowBody(Mesh piece, SkinnedMeshRenderer source, SkinnedMeshRenderer nude, Transform root, float near = 0.02f, float far = 0.04f)
        {
            var body = nude.sharedMesh;
            var toBody = ToModel(nude, root);
            var bv = body.vertices;
            var bodyAt = new Vector3[bv.Length];
            for (int i = 0; i < bv.Length; i++)
                bodyAt[i] = toBody.MultiplyPoint3x4(bv[i]);
            var bodyCounts = body.GetBonesPerVertex();
            var bodyWeights = body.GetAllBoneWeights();
            var bodyStart = new int[bv.Length + 1];
            for (int i = 0; i < bv.Length; i++)
                bodyStart[i + 1] = bodyStart[i] + bodyCounts[i];
            var bodyBones = nude.bones;
            var grid = new Dictionary<Vector3Int, List<int>>();
            for (int i = 0; i < bodyAt.Length; i++)
            {
                var key = Vector3Int.FloorToInt(bodyAt[i] / far);
                if (!grid.TryGetValue(key, out var list))
                    grid[key] = list = new List<int>();
                list.Add(i);
            }
            var index = new Dictionary<Transform, int>();
            var bones = source.bones;
            for (int k = 0; k < bones.Length; k++)
                if (bones[k] != null && !index.ContainsKey(bones[k]))
                    index[bones[k]] = k;
            var toPiece = ToModel(source, root);
            var pv = piece.vertices;
            var counts = piece.GetBonesPerVertex();
            var weights = piece.GetAllBoneWeights();
            var newCounts = new NativeArray<byte>(pv.Length, Allocator.Temp);
            var flat = new List<BoneWeight1>();
            int whole = 0, blended = 0, own = 0;
            float lost = 0f;
            int at = 0;
            var sum = new Dictionary<int, float>();
            var nearest = new List<(int j, float d2)>();
            for (int i = 0; i < pv.Length; i++)
            {
                var p = toPiece.MultiplyPoint3x4(pv[i]);
                var c = Vector3Int.FloorToInt(p / far);
                nearest.Clear();
                for (int x = -1; x <= 1; x++)
                    for (int y = -1; y <= 1; y++)
                        for (int z = -1; z <= 1; z++)
                            if (grid.TryGetValue(new Vector3Int(c.x + x, c.y + y, c.z + z), out var list))
                                foreach (int j in list)
                                {
                                    float d2 = (bodyAt[j] - p).sqrMagnitude;
                                    if (d2 < far * far)
                                        nearest.Add((j, d2));
                                }
                nearest.Sort((a, b) => a.d2.CompareTo(b.d2));
                if (nearest.Count > 6)
                    nearest.RemoveRange(6, nearest.Count - 6);
                int best = nearest.Count > 0 ? nearest[0].j : -1;
                float bestD = nearest.Count > 0 ? nearest[0].d2 : far * far;
                sum.Clear();
                float t = best < 0 ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(near, far, Mathf.Sqrt(bestD)));
                for (int k = 0; k < counts[i]; k++)
                {
                    var w = weights[at + k];
                    sum.TryGetValue(w.boneIndex, out float v);
                    sum[w.boneIndex] = v + w.weight * t;
                }
                at += counts[i];
                if (best >= 0 && t < 1f)
                {
                    float idw = 0f;
                    foreach (var (j, d2) in nearest)
                        idw += 1f / Mathf.Max(d2, 1e-8f);
                    foreach (var (j, d2) in nearest)
                    {
                        float share = (1f - t) / Mathf.Max(d2, 1e-8f) / idw;
                        for (int k = bodyStart[j]; k < bodyStart[j + 1]; k++)
                        {
                            var w = bodyWeights[k];
                            var bone = w.boneIndex >= 0 && w.boneIndex < bodyBones.Length ? bodyBones[w.boneIndex] : null;
                            if (bone == null || !index.TryGetValue(bone, out int bi))
                            {
                                lost += w.weight * share;
                                continue;
                            }
                            sum.TryGetValue(bi, out float v);
                            sum[bi] = v + w.weight * share;
                        }
                    }
                    if (t <= 0f)
                        whole++;
                    else
                        blended++;
                }
                else
                    own++;
                var top = sum.Where(kv => kv.Value > 1e-4f).OrderByDescending(kv => kv.Value).Take(4).ToList();
                float total = top.Sum(kv => kv.Value);
                newCounts[i] = (byte)top.Count;
                foreach (var kv in top)
                    flat.Add(new BoneWeight1 { boneIndex = kv.Key, weight = kv.Value / Mathf.Max(total, 1e-6f) });
            }
            var na = new NativeArray<BoneWeight1>(flat.ToArray(), Allocator.Temp);
            piece.SetBoneWeights(newCounts, na);
            newCounts.Dispose();
            na.Dispose();
            return $"follows the body: {whole} vertices take its weights, {blended} blended, {own} keep their own" +
                   (lost > 0f ? $" ({lost:F1} vertex-weights on bones its renderer lacks left out)" : "");
        }

        /// <summary>
        /// Some triangles of a mesh as a mesh of their own: only the vertices they use, every vertex
        /// stream, every bone influence, the same bind poses; one submesh per list.
        /// </summary>
        internal static Mesh Subset(Mesh src, List<int>[] indices, Mesh m)
        {
            var map = new Dictionary<int, int>();
            var order = new List<int>();
            foreach (var list in indices)
                foreach (int i in list)
                    if (!map.ContainsKey(i))
                    {
                        map[i] = order.Count;
                        order.Add(i);
                    }
            T[] Pick<T>(T[] all)
            {
                var r = new T[order.Count];
                for (int k = 0; k < r.Length; k++)
                    r[k] = all[order[k]];
                return r;
            }
            m.indexFormat = order.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            m.vertices = Pick(src.vertices);
            if (src.HasVertexAttribute(VertexAttribute.Normal))
                m.normals = Pick(src.normals);
            if (src.HasVertexAttribute(VertexAttribute.Tangent))
                m.tangents = Pick(src.tangents);
            if (src.HasVertexAttribute(VertexAttribute.Color))
                m.colors32 = Pick(src.colors32);
            for (int ch = 0; ch < 8; ch++)
            {
                var attr = VertexAttribute.TexCoord0 + ch;
                if (!src.HasVertexAttribute(attr))
                    continue;
                int dim = src.GetVertexAttributeDimension(attr);
                if (dim <= 2)
                {
                    var l = new List<Vector2>();
                    src.GetUVs(ch, l);
                    m.SetUVs(ch, Pick(l.ToArray()));
                }
                else if (dim == 3)
                {
                    var l = new List<Vector3>();
                    src.GetUVs(ch, l);
                    m.SetUVs(ch, Pick(l.ToArray()));
                }
                else
                {
                    var l = new List<Vector4>();
                    src.GetUVs(ch, l);
                    m.SetUVs(ch, Pick(l.ToArray()));
                }
            }
            var perVertex = src.GetBonesPerVertex();
            if (perVertex.Length == src.vertexCount)
            {
                var weights = src.GetAllBoneWeights();
                var start = new int[src.vertexCount + 1];
                for (int i = 0; i < src.vertexCount; i++)
                    start[i + 1] = start[i] + perVertex[i];
                int total = order.Sum(i => (int)perVertex[i]);
                var counts = new NativeArray<byte>(order.Count, Allocator.Temp);
                var picked = new NativeArray<BoneWeight1>(total, Allocator.Temp);
                int at = 0;
                for (int k = 0; k < order.Count; k++)
                {
                    int i = order[k];
                    counts[k] = perVertex[i];
                    for (int j = start[i]; j < start[i + 1]; j++)
                        picked[at++] = weights[j];
                }
                m.bindposes = src.bindposes;
                m.SetBoneWeights(counts, picked);
                counts.Dispose();
                picked.Dispose();
            }
            if (src.blendShapeCount > 0)
                Debug.LogWarning($"[ROE] burst: {src.name} has {src.blendShapeCount} blend shapes; the split meshes leave them out");
            m.subMeshCount = indices.Length;
            for (int s = 0; s < indices.Length; s++)
                m.SetTriangles(indices[s].Select(i => map[i]).ToArray(), s, false);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>
        /// The mesh asset at a path, emptied to be filled anew - or a new mesh, saved by Commit.  An existing asset is
        /// rewritten in place (scenes that use it keep the link) through the mesh API: copying another mesh over it
        /// (CopySerialized) left its drawable copy stale, and renderers drew nothing until the editor restarted.
        /// </summary>
        internal static Mesh MeshAsset(string path)
        {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null)
                return new Mesh { name = Path.GetFileNameWithoutExtension(path) };
            mesh.Clear(false);
            mesh.name = Path.GetFileNameWithoutExtension(path);
            return mesh;
        }

        internal static Mesh Commit(Mesh mesh, string path)
        {
            if (AssetDatabase.Contains(mesh))
                EditorUtility.SetDirty(mesh);
            else
                AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        /// <summary>A renderer next to the original, on its bones, with its settings.</summary>
        static SkinnedMeshRenderer AddRenderer(SkinnedMeshRenderer src, string name, Mesh mesh, Material[] materials)
        {
            var go = new GameObject(name) { layer = src.gameObject.layer };
            go.transform.SetParent(src.transform.parent, false);
            go.transform.localPosition = src.transform.localPosition;
            go.transform.localRotation = src.transform.localRotation;
            go.transform.localScale = src.transform.localScale;
            var r = go.AddComponent<SkinnedMeshRenderer>();
            r.sharedMesh = mesh;
            r.bones = src.bones;
            r.rootBone = src.rootBone;
            r.sharedMaterials = materials;
            r.localBounds = src.localBounds;
            r.updateWhenOffscreen = src.updateWhenOffscreen;
            r.quality = src.quality;
            r.skinnedMotionVectors = src.skinnedMotionVectors;
            r.shadowCastingMode = src.shadowCastingMode;
            r.receiveShadows = src.receiveShadows;
            r.lightProbeUsage = src.lightProbeUsage;
            r.reflectionProbeUsage = src.reflectionProbeUsage;
            r.probeAnchor = src.probeAnchor;
            r.forceMatrixRecalculationPerRender = src.forceMatrixRecalculationPerRender;
            r.allowOcclusionWhenDynamic = src.allowOcclusionWhenDynamic;
            r.renderingLayerMask = src.renderingLayerMask;
            return r;
        }

        // ---- check pictures

        static readonly Color[] Palette =
        {
            new Color(0.90f, 0.20f, 0.25f), new Color(0.20f, 0.55f, 0.95f), new Color(0.95f, 0.75f, 0.10f), new Color(0.25f, 0.75f, 0.35f),
            new Color(0.65f, 0.30f, 0.85f), new Color(0.95f, 0.50f, 0.15f), new Color(0.15f, 0.80f, 0.80f), new Color(0.95f, 0.45f, 0.75f),
            new Color(0.55f, 0.40f, 0.20f), new Color(0.60f, 0.85f, 0.20f), new Color(0.10f, 0.35f, 0.55f), new Color(0.80f, 0.80f, 0.80f),
        };

        [MenuItem("ROE Fighter/Burst/Check pictures")]
        public static void Check()
        {
            var ids = RoeCapture.Arg("-roeChars", "a08,g04").Split(',');
            string outDir = RoeCapture.Arg("-roeOut", Path.Combine(ProjectDir, "out", "clothes_burst", "unity"));
            Directory.CreateDirectory(outDir);
            ShaderUtil.allowAsyncCompilation = false;
            var studio = RoeStudio.Build();
            // -roeKeyPitch 88: the key light from straight above, as the fight stage's spotlights (what the arms shade)
            studio.keyPitch = float.Parse(RoeCapture.Arg("-roeKeyPitch", studio.keyPitch.ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture);
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            foreach (var id in ids)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RoeHumanoid.FighterPath(id));
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                if (Load(id) == null)
                {
                    Debug.LogWarning($"[ROE] burst {id}: no rules at {RulesPath(id)}");
                    Object.DestroyImmediate(go);
                    continue;
                }
                var stance = CheckPose(id, RoeCapture.Arg("-roePose", ""), out float poseTime, out var standing);
                Debug.Log($"[ROE] burst {id}: {standing}");
                if (stance != null)
                    RoeCapture.Pose(go, stance, poseTime);
                foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    smr.forceMatrixRecalculationPerRender = true;
                var forward = RoeShowcase.Forward(go);
                var bounds = RoeShowcase.WorldBounds(go);
                float height = bounds.max.y - go.transform.position.y;
                var body = new Vector3(bounds.center.x, go.transform.position.y + height * 0.5f, bounds.center.z);
                studio.LightFrom(forward);
                studio.SetFocus(0f, 0f, 0f);
                void Shot(string file, float yaw)
                {
                    studio.Aim(body, forward, yaw, 6f, height * 2.6f, 26f);
                    RoeCapture.Render(studio.camera, 900, 1300, Path.Combine(outDir, file));
                }
                // the first frames with new materials come out with placeholder shading
                studio.Aim(body, forward, 18f, 6f, height * 2.6f, 26f);
                RoeCapture.Render(studio.camera, 200, 200, Path.Combine(outDir, "_warm.png"));
                RoeCapture.Render(studio.camera, 200, 200, Path.Combine(outDir, "_warm.png"));
                // every piece of the outfit and the rule it falls under (for writing the rules)
                var analysis = Analyse(go, Load(id));
                File.WriteAllText(Path.Combine(outDir, $"{id}_pieces.txt"), "bone\tshares\ttriangles\tside\tlowest\thighest\tcentre\tmaterials\tgroup\n" +
                    string.Join("\n", analysis.parts.OrderBy(p => p.bone).ThenByDescending(p => p.Triangles).Select(p =>
                        $"{p.bone}\t{string.Join(" ", p.share.OrderByDescending(kv => kv.Value).Take(3).Select(kv => $"{kv.Key}:{kv.Value:F2}"))}\t" +
                        $"{p.Triangles}\t{p.side}\t{p.lowest:F2}\t{p.highest:F2}\t{p.centre.x:F2},{p.centre.y:F2},{p.centre.z:F2}\t{p.materials}\t" +
                        $"{(p.group != null ? $"{p.group.name} (stage {p.group.stage})" : "-")}")) + "\n");
                // as the prefab has it, then split: the same picture (tools/burst_sheet.py compares them)
                Shot($"{id}_unsplit_front.png", 15f);
                Shot($"{id}_unsplit_back.png", 165f);
                // split out of the posing (a renderer that gets another mesh while posed draws nothing), then posed again
                RoeCapture.EndPosing();
                var burst = Apply(go, id);
                if (stance != null)
                    RoeCapture.Pose(go, stance, poseTime);
                foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    smr.forceMatrixRecalculationPerRender = true;
                for (int stage = 0; stage <= burst.stages; stage++)
                {
                    burst.Show(stage);
                    Shot($"{id}_stage{stage}_front.png", 15f);
                    Shot($"{id}_stage{stage}_back.png", 165f);
                }
                string facesArg = RoeCapture.Arg("-roeFaces", "0");
                if (facesArg != "0")
                {
                    // which side of the triangles shows (Hidden/ROE/FaceSide: grey front, magenta back, yellow a front whose
                    // normal looks away), each stage whole and the chest close, next to the chest as drawn; "-roeFaces 1" in
                    // the pose above, or in each of a list of poses as -roePose takes them ("stance@0,react_02@0.5")
                    var side = new Material(Shader.Find("Hidden/ROE/FaceSide"));
                    var kept = go.GetComponentsInChildren<Renderer>(true).ToDictionary(r => r, r => r.sharedMaterials);
                    var animator = go.GetComponent<Animator>();
                    var chestBone = animator != null && animator.isHuman
                        ? animator.GetBoneTransform(HumanBodyBones.UpperChest) ?? animator.GetBoneTransform(HumanBodyBones.Chest) : null;
                    void Close(string file)
                    {
                        // the chest as posed, from the way it faces now (a battle stance stands her side on)
                        var facing = forward;
                        var shoulderL = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.LeftUpperArm) : null;
                        var shoulderR = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.RightUpperArm) : null;
                        if (shoulderL != null && shoulderR != null)
                        {
                            var across = Vector3.Cross(shoulderR.position - shoulderL.position, Vector3.up);
                            if (across.sqrMagnitude > 1e-6f)
                                facing = across.normalized;
                        }
                        var chest = chestBone != null ? chestBone.position + facing * 0.05f
                                                      : new Vector3(bounds.center.x, go.transform.position.y + height * 0.74f, bounds.center.z);
                        studio.LightFrom(facing);
                        studio.Aim(chest, facing, 15f, 6f, height * 0.7f, 26f);
                        RoeCapture.Render(studio.camera, 900, 900, Path.Combine(outDir, file));
                        studio.LightFrom(forward);
                    }
                    var looks = facesArg == "1" ? new[] { (clip: stance, time: poseTime, tag: "") }
                        : facesArg.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).Select(x =>
                        {
                            var clip = CheckPose(id, x, out float t, out var note);
                            Debug.Log($"[ROE] burst {id} faces: {note}");
                            return (clip, time: t, tag: Safe(x.Replace("@", "_at_")) + "_");
                        }).ToArray();
                    foreach (var look in looks)
                    {
                        if (look.clip != null)
                            RoeCapture.Pose(go, look.clip, look.time);
                        for (int stage = 0; stage <= burst.stages; stage++)
                        {
                            burst.Show(stage);
                            Close($"{id}_{look.tag}stage{stage}_chest.png");
                            foreach (var kv in kept)
                                kv.Key.sharedMaterials = Enumerable.Repeat(side, kv.Value.Length).ToArray();
                            Shot($"{id}_{look.tag}stage{stage}_faces_front.png", 15f);
                            Close($"{id}_{look.tag}stage{stage}_faces_chest.png");
                            foreach (var kv in kept)
                                kv.Key.sharedMaterials = kv.Value;
                        }
                    }
                    Object.DestroyImmediate(side);
                    if (stance != null)
                        RoeCapture.Pose(go, stance, poseTime);
                    else
                        RoeCapture.EndPosing();
                }
                // the groups in colours on a grey body
                burst.Show(0);
                var grey = new Material(lit);
                grey.SetColor("_BaseColor", new Color(0.62f, 0.62f, 0.64f));
                var colours = new Dictionary<string, Material>();
                var legend = new StringBuilder();
                foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var piece = burst.pieces.FirstOrDefault(p => p.renderer == r);
                    if (piece == null)
                    {
                        r.sharedMaterials = Enumerable.Repeat(grey, r.sharedMaterials.Length).ToArray();
                        continue;
                    }
                    if (!colours.TryGetValue(piece.group, out var mat))
                    {
                        var c = Palette[colours.Count % Palette.Length];
                        mat = new Material(lit);
                        mat.SetColor("_BaseColor", c);
                        mat.SetFloat("_Smoothness", 0.3f);
                        colours[piece.group] = mat;
                        legend.Append($"{piece.title}\t#{ColorUtility.ToHtmlStringRGB(c)}\t{piece.stage}\t{(piece.cloth ? "cloth" : "armour")}\t" +
                                      $"{burst.pieces.Where(x => x.group == piece.group).Sum(x => x.triangles)}\n");
                    }
                    r.sharedMaterials = Enumerable.Repeat(mat, r.sharedMaterials.Length).ToArray();
                }
                File.WriteAllText(Path.Combine(outDir, $"{id}_legend.txt"), legend.ToString());
                Shot($"{id}_groups_front.png", 15f);
                Shot($"{id}_groups_back.png", 165f);
                RoeCapture.EndPosing();
                Debug.Log($"[ROE] burst {id}: {burst.Report()}");
                Object.DestroyImmediate(go);
            }
            Debug.Log($"[ROE] burst check pictures in {outDir}");
        }

        /// <summary>
        /// The pose of the check pictures: -roePose name[@seconds], one of her clips (a fighter from another game:
        /// Assets/&lt;game&gt;/&lt;id&gt;/clips; ROE: the humanoid clips); else the stance the fight stands a ROE fighter in, and the
        /// bind pose for one from another game ("stance": her battle stance as the fight has it).  Eve's bind pose (arms
        /// out) hides what her poses do to her clothes: eve37's shirt went dark where the tie had been only in the fight.
        /// </summary>
        static AnimationClip CheckPose(string id, string arg, out float time, out string note)
        {
            time = 0f;
            if (string.IsNullOrEmpty(arg))
                return RoeHumanoidClips.Standing(id, out note);
            int at = arg.IndexOf('@');
            string name = at >= 0 ? arg.Substring(0, at) : arg;
            if (at >= 0)
                time = float.Parse(arg.Substring(at + 1), CultureInfo.InvariantCulture);
            var clip = name == "stance" && DoaFighter.IsDoa(id) ? RoeFightScene.DoaStance(DoaFighter.Load(id))
                     : AssetDatabase.LoadAssetAtPath<AnimationClip>($"{DoaFighter.Dir(id)}/clips/{name}.anim")
                       ?? AssetDatabase.LoadAssetAtPath<AnimationClip>(RoeHumanoidClips.ClipPath(id, name));
            note = clip != null ? $"stands in {name} ({clip.name}) at {time:F2} s (-roePose)" : $"no clip {name} (-roePose): bind pose";
            return clip;
        }
    }
}
