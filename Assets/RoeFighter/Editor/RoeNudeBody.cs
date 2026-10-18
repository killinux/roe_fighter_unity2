using System;
using System.Collections.Generic;
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
    /// 爆衣 step 2, the whole body under the outfit (user 10-03: "做个inase结合nude的模型，让爆衣都爆掉"): the game
    /// deleted the skin under the battle suits, so pieces with nothing under them could not come off.  The family's
    /// nude base (a01 for Inase, g01 for Luf; the game's own body for the H scenes, Assets/ROE/&lt;family&gt;01) lies on the
    /// suit's skin within a millimetre (docs/clothes-burst.md section 2), so it replaces the suit's skin as a whole -
    /// no seam where the two meet, the same skin everywhere.
    ///
    /// Its own skeleton has dozens of bones the suit's lacks, so it is not bound by bone names: every vertex takes
    /// the bone weights of what lies on it in the bind pose (weight transfer) - the suit's skin within 1.5 cm, else,
    /// where the game cut the skin away, whatever is nearest: the tight piece over it (bra armour, shin guard, glove).
    /// The body then moves exactly like the skin and armour it replaces.  Triangles that lie only under pieces that
    /// never come off (g04's shoes: her feet stand at another heel angle than the base's) are left out, so nothing
    /// pokes through them.
    ///
    /// Rules: the "nude" block of Editor/Burst/&lt;id&gt;.json (prefab, renderer, submesh, material of the base; the suit
    /// renderer it goes next to; the suit's skin it replaces).  Called by RoeBurstBuilder.Apply before the outfit is
    /// split.
    ///
    /// Stellar Blade's Eve (10-05): her only nude base is a mod's (EveOriginalProportions), on her own skeleton but fuller than
    /// her outfits - up to 2.8 cm out through the suit at the hips and chest.  Two more words in the rules for her:
    ///   pose "bones"  the base is carried onto the suit's bind pose by its own weights, bones by name (one the suit lacks
    ///                 follows its nearest ancestor the suit has): eve37's hips stand 1.1 cm otherwise than the base's;
    ///   reveal        the body under the outfit's opaque pieces (lace and see-through plastic hide nothing) is drawn only
    ///                 once they are off: its triangles go to a renderer per stage (RoeClothesBurst.reveals), switched on as
    ///                 that stage comes off - so the base keeps its own shape and nothing comes out through the clothes.
    ///                 A vertex is under a piece when a triangle of it lies over the vertex (within 4 mm of right over it,
    ///                 4 cm along its normal either way, facing the way the body does) and the piece lies on the body or
    ///                 never comes off (thick soles and platform shoes lie further from the foot than 2.5 cm); a
    ///                 triangle is drawn from the earliest stage any corner needs (no gap at a piece's edge).  A vertex
    ///                 under no piece and further than 2.5 cm from the suit's skin that shows (skin under an opaque
    ///                 piece does not, by the same test: eve09's suit keeps skin inside its boots) comes out where the dressed outfit shows
    ///                 no skin - her flat bare toes out of the suit's heeled feet: it waits for the pieces nearest to it,
    ///                 however far (those within 3 cm of the nearest: the last of their stages; never when only pieces
    ///                 that stay on are there).
    ///   fit           where pieces that never come off lie (eve37's strappy heels), the base takes the shape of the suit's
    ///                 own skin it replaces: within 2 cm of such a piece wholly (the nearest point of that skin, 5 cm at
    ///                 most), blended back to its own shape at 4 cm.  The suit's feet were made for its shoes, the base's
    ///                 flat bare feet were not: its toes came out through the shoes and between the straps its ankles
    ///                 were either through them or (left out) missing.
    ///
    /// Kart's crane robe (b14, 10-11): her suit has no skin on her right upper arm (the robe covers it), and what lies over it
    /// is the sheer sleeve, whose top the game hangs on the forearm's twist bone, and the shoulder piece on its own bone - the
    /// body took those and its upper arm stayed out sideways in the bind pose like a board.  One more word:
    ///   ownWeights    a regex of the suit's bones: where the suit's skin is not within SkinReach, a vertex the base hangs
    ///                 mostly on such a bone keeps the base's own weights, bones by name (one the suit lacks - b01's ArmTwist_R,
    ///                 toes - its nearest ancestor the suit has).  Only there: all over (tried first) her thighs creased where the
    ///                 base's own weights (the thigh) met the suit's skin (its twist bones, which hang on the spine).  At the edge
    ///                 of the region it blends: by the share of the base's weight on such bones (OwnFrom to OwnTo) the base's
    ///                 own weights mix with what lies over the vertex - switched there at once (its largest weight on such a
    ///                 bone or not), the top of her right shoulder showed a small step in the nude stage (10-17).
    ///   ownOverSkin   ownWeights where the suit's skin lies on the body too.  The step was not that edge: under the robe her
    ///                 suit's skin reaches her right shoulder joint, hung on the spine and the clavicle only (the game never
    ///                 shows it), and the body copying it there stayed put while the arm beside it went down - a corner on top
    ///                 of the shoulder.  Within the region the base's own weights now win over that skin, blended by the same
    ///                 share at the region's edge.
    ///   ownMirror     the region's weights are not the base's own but the suit's skin's on her other side, mirrored across
    ///                 the plane halfway between her clavicles, its bones named for this side (Bip001 L .. / Bip001 R ..,
    ///                 .._L / .._R): her left shoulder is bare in her game and weighted for it, with the suit's own twist and
    ///                 shoulder bones, which the fight drives (RoeHelperFit); the base's own shoulder helpers (GeoSphere002,
    ///                 Muscle Strand ..) are not in the suit, so its own weights fold the top of a lowered arm's shoulder too.
    ///                 Where the other side has no skin within MirrorReach, a tight piece of the outfit lying there within
    ///                 MirrorCover (b14's left arm cover: the game cut the skin under it); with neither, or a bone of theirs
    ///                 not bound on this side, the base's own weights stay.  Then the region's weights are smoothed over the
    ///                 surface (MirrorSmooth passes, each vertex the mean of itself and its neighbours, the ones at one place
    ///                 as one): where her left arm's skin meets its cover their weights step - the game hides that edge under
    ///                 the cover's - and mirrored onto her bare right arm it tore the upper arm across.
    ///
    /// Vindictus' Fiona (10-06): her base is the game's own SM_Fiona_Body01 laid on PCF_005's skin in Blender (tools/vdf_nude.py),
    /// with two textures - body, and hands and feet: "submeshes" + "materials" in place of "submesh" + "material".  All of her
    /// outfit is cut out by its alpha, so what hides the body is read per triangle (SolidTriangles: at least 90 % of its texels
    /// solid): her dress does, the net of her lace cuffs does not.
    /// </summary>
    public static class RoeNudeBody
    {
        public const float SkinReach = 0.015f;       // the suit's skin this close is what the body copies
        const float MirrorReach = 0.02f;             // ownMirror: the suit's skin this close to the mirrored point
        const float MirrorCover = 0.03f;             // ... or else a tight piece of the outfit this close
        const int MirrorSmooth = 8;                  // ... and then its weights smoothed over the surface this many times
        const float OwnFrom = 0.25f, OwnTo = 0.75f;  // ownWeights: a vertex the base hangs on the region's bones by this share
                                                     // or less copies what lies over it, by OwnTo or more keeps its own
        const float MaxFill = 0.3f;                  // a fill that would add more than this share of a submesh is not one
        const float Cell = 0.02f;
        const float CoverReach = 0.04f;              // reveal: a piece's triangle this far along its normal (either way) covers a vertex
        const float CoverAside = 0.004f;             //   when the vertex is this close to right under it
        const float CoverFacing = 0.3f;              //   and the triangle faces the way the body does (cos)
        const float NearSkin = 0.025f;               // reveal: a vertex this close to the suit's skin is where the outfit shows skin
        const float SkinAside = 0.0005f;             //   the suit's skin is hidden only right under a piece's triangle
        const float FitInner = 0.02f;                // fit: within this of a piece that never comes off, the suit skin's shape wholly
        const float FitOuter = 0.04f;                //   blended back to the base's own by here
        const float FitReach = 0.05f;                //   the suit's skin at most this far (eve09's feet have none: left alone)
        const float AroundPieces = 0.06f;            //   one further from it and under no piece waits for the nearest pieces (looked for this far, then 2x, 4x)

        /// <summary>A bone of loose cloth (RoeBoneCloth.RoeKindOf: skirt, sash, hair, chain) - the body takes no weight from it.</summary>
        static bool Loose(Transform bone)
        {
            string kind = bone != null ? RoeBoneCloth.RoeKindOf(bone.name) : null;
            return kind == "skirt" || kind == "ribbon" || kind == "hair" || kind == "chain";
        }

        class Grid
        {
            readonly Dictionary<Vector3Int, List<int>> cells = new Dictionary<Vector3Int, List<int>>();
            readonly List<Vector3> points;

            public Grid(List<Vector3> points, IEnumerable<int> which)
            {
                this.points = points;
                foreach (int i in which)
                {
                    var key = Key(points[i]);
                    if (!cells.TryGetValue(key, out var list))
                        cells[key] = list = new List<int>();
                    list.Add(i);
                }
            }

            static Vector3Int Key(Vector3 p) => Vector3Int.FloorToInt(p / Cell);

            /// <summary>Points within radius, with their distance.</summary>
            public void Near(Vector3 p, float radius, List<(int i, float d)> into)
            {
                into.Clear();
                int k = Mathf.CeilToInt(radius / Cell);
                var c = Key(p);
                float r2 = radius * radius;
                for (int x = -k; x <= k; x++)
                    for (int y = -k; y <= k; y++)
                        for (int z = -k; z <= k; z++)
                            if (cells.TryGetValue(new Vector3Int(c.x + x, c.y + y, c.z + z), out var list))
                                foreach (int i in list)
                                {
                                    float d2 = (points[i] - p).sqrMagnitude;
                                    if (d2 <= r2)
                                        into.Add((i, Mathf.Sqrt(d2)));
                                }
            }
        }

        /// <summary>Points welded by place: the same place (within 0.3 mm) gets the same id.</summary>
        class Places
        {
            readonly Dictionary<Vector3Int, List<(Vector3 p, int id)>> cells = new Dictionary<Vector3Int, List<(Vector3 p, int id)>>();
            const float Tolerance = 0.0003f;
            int count;

            static Vector3Int Key(Vector3 p) => Vector3Int.FloorToInt(p / 0.001f);

            public int Find(Vector3 p)
            {
                var c = Key(p);
                for (int x = -1; x <= 1; x++)
                    for (int y = -1; y <= 1; y++)
                        for (int z = -1; z <= 1; z++)
                            if (cells.TryGetValue(new Vector3Int(c.x + x, c.y + y, c.z + z), out var list))
                                foreach (var (q, id) in list)
                                    if ((q - p).sqrMagnitude <= Tolerance * Tolerance)
                                        return id;
                return -1;
            }

            public int Add(Vector3 p)
            {
                int id = Find(p);
                if (id >= 0)
                    return id;
                var key = Key(p);
                if (!cells.TryGetValue(key, out var list))
                    cells[key] = list = new List<(Vector3 p, int id)>();
                list.Add((p, id = count++));
                return id;
            }
        }

        static (int, int, int) Sorted(int a, int b, int c)
        {
            if (a > b) (a, b) = (b, a);
            if (b > c) (b, c) = (c, b);
            if (a > b) (a, b) = (b, a);
            return (a, b, c);
        }

        /// <summary>
        /// The nude body as a renderer next to rules.nude.attachTo, on the suit's bones.  Null when the character has
        /// no "nude" block or something is missing (logged).
        /// </summary>
        public static SkinnedMeshRenderer Build(GameObject model, string id, RoeBurstBuilder.Rules rules, RoeBurstBuilder.Analysis analysis,
                                                string dir, StringBuilder log, List<RoeClothesBurst.Reveal> reveals = null)
        {
            var n = rules.nude;
            if (n == null || string.IsNullOrEmpty(n.prefab))
                return null;
            var root = model.transform;
            var renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r => r.sharedMesh != null).ToArray();
            var target = renderers.FirstOrDefault(r => r.name == n.attachTo);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(n.prefab);
            var nudeRenderer = prefab != null ? prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(r => r.name == n.renderer) : null;
            // the body's submeshes and their materials: one ("submesh", "material") or several ("submeshes", "materials")
            var bodySubs = n.submeshes != null && n.submeshes.Length > 0 ? n.submeshes : new[] { n.submesh };
            var matPaths = n.materials != null && n.materials.Length > 0 ? n.materials : new[] { n.material };
            var materials = matPaths.Select(p => AssetDatabase.LoadAssetAtPath<Material>(p)).ToArray();
            bool subsOk = nudeRenderer != null && nudeRenderer.sharedMesh != null && bodySubs.All(s => s >= 0 && s < nudeRenderer.sharedMesh.subMeshCount);
            if (target == null || nudeRenderer == null || nudeRenderer.sharedMesh == null || !subsOk || materials.Length != bodySubs.Length || materials.Any(m => m == null))
            {
                Debug.LogWarning($"[ROE] nude body {id}: missing - suit renderer '{n.attachTo}' {(target != null)}, base prefab {n.prefab} {(prefab != null)}, " +
                                 $"its renderer '{n.renderer}' {(nudeRenderer != null)}, submeshes {string.Join(",", bodySubs)} {subsOk}, " +
                                 $"materials {string.Join(", ", matPaths.Zip(materials, (p, m) => $"{p} {(m != null)}"))} ({materials.Length} for {bodySubs.Length} submeshes)");
                return null;
            }
            var own = nudeRenderer.sharedMaterials;
            for (int k = 0; k < bodySubs.Length; k++)
                if (bodySubs[k] < own.Length && own[bodySubs[k]] != null && own[bodySubs[k]].name != materials[k].name)
                    Debug.LogWarning($"[ROE] nude body {id}: the base's submesh {bodySubs[k]} has {own[bodySubs[k]].name}, the rules put {materials[k].name} on it");
            var mesh = nudeRenderer.sharedMesh;
            Matrix4x4 ToModel(Transform t) => root.worldToLocalMatrix * t.localToWorldMatrix;
            var nudeToModel = prefab.transform.worldToLocalMatrix * nudeRenderer.transform.localToWorldMatrix;
            var modelToTarget = ToModel(target.transform).inverse;

            // ---- what the body copies its weights from: the suit's skin it replaces, and the outfit
            var points = new List<Vector3>();
            var skin = new List<bool>();
            var permanent = new List<bool>();
            var tight = new List<bool>();             // outfit points of pieces that lie on the body
            var follow = new List<bool>();            // ... of a group that follows the body (followBody): not a source of weights
            var influence = new List<(Transform bone, float weight)[]>();
            var pointStage = new List<int>();         // the stage of the piece an outfit point is on (int.MaxValue: stays on; 0: skin)
            var pointNormal = new List<Vector3>();    // its normal, model space (reveal: which way the skin faces)
            var bindpose = new Dictionary<Transform, Matrix4x4>();
            void Add(SkinnedMeshRenderer r, IEnumerable<int> vertices, bool isSkin, Func<int, bool> stays, Func<int, bool> lies = null,
                     Func<int, bool> follows = null, Func<int, int> stageOf = null)
            {
                var m = r.sharedMesh;
                var v = m.vertices;
                var vn = m.normals;
                var perVertex = m.GetBonesPerVertex();
                var all = m.GetAllBoneWeights();
                var start = new int[m.vertexCount + 1];
                for (int i = 0; i < m.vertexCount && i < perVertex.Length; i++)
                    start[i + 1] = start[i] + perVertex[i];
                var poses = m.bindposes;
                var toTargetSpace = r.transform.worldToLocalMatrix * target.transform.localToWorldMatrix;
                var bones = r.bones;
                for (int b = 0; b < bones.Length && b < poses.Length; b++)
                    if (bones[b] != null && !bindpose.ContainsKey(bones[b]))
                        bindpose[bones[b]] = poses[b] * toTargetSpace;
                var toModel = ToModel(r.transform);
                foreach (int i in vertices)
                {
                    if (i >= perVertex.Length)
                        continue;
                    var list = new List<(Transform, float)>();
                    for (int j = start[i]; j < start[i + 1]; j++)
                        if (all[j].boneIndex < bones.Length && bones[all[j].boneIndex] != null && all[j].weight > 0f)
                            list.Add((bones[all[j].boneIndex], all[j].weight));
                    if (list.Count == 0)
                        continue;
                    points.Add(toModel.MultiplyPoint3x4(v[i]));
                    skin.Add(isSkin);
                    permanent.Add(stays(i));
                    tight.Add(lies != null && lies(i));
                    follow.Add(follows != null && follows(i));
                    pointStage.Add(stageOf != null ? stageOf(i) : 0);
                    pointNormal.Add(i < vn.Length ? toModel.MultiplyVector(vn[i]).normalized : Vector3.zero);
                    influence.Add(list.ToArray());
                }
            }
            foreach (var s in n.replace ?? new RoeBurstBuilder.Outfit[0])
            {
                var r = renderers.FirstOrDefault(x => x.name == s.renderer);
                if (r == null)
                    continue;
                Add(r, RoeBurstBuilder.Subs(s, r).SelectMany(k => r.sharedMesh.GetIndices(k)).Distinct(), true, _ => false);
            }
            var fills = (n.fill ?? new RoeBurstBuilder.Fill[0]).Where(f => f.missingFrom != null && f.missingFrom.Length > 0).ToList();
            foreach (var o in fills.SelectMany(f => f.missingFrom))
            {
                // the skin the filled part continues (the suit's head above the cut neck): weights for it too
                var r = renderers.FirstOrDefault(x => x.name == o.renderer);
                if (r == null)
                    continue;
                Add(r, RoeBurstBuilder.Subs(o, r).SelectMany(k => r.sharedMesh.GetIndices(k)).Distinct(), true, _ => false);
            }
            // ---- the body's own vertices (the base's body submeshes; triSub: which of them each triangle is from)
            var triList = new List<int>();
            var triSub = new List<int>();
            for (int k = 0; k < bodySubs.Length; k++)
            {
                var t = mesh.GetTriangles(bodySubs[k]);
                triList.AddRange(t);
                triSub.AddRange(Enumerable.Repeat(k, t.Length / 3));
            }
            var tris = triList.ToArray();
            var nv = mesh.vertices;
            var nn0 = mesh.HasVertexAttribute(VertexAttribute.Normal) ? mesh.normals : new Vector3[nv.Length];
            // ---- more of the base where the suit has nothing (Fill): the triangles of another base submesh that the
            // named suit renderers do not have, compared by the places of their corners (the suit's head is the base's
            // head, cut under the collar)
            var fillTris = new List<int>();
            Material fillMaterial = null;
            var fillNote = new StringBuilder();
            foreach (var f in fills)
            {
                if (f.submesh < 0 || f.submesh >= mesh.subMeshCount)
                    continue;
                var places = new Places();
                var suitTris = new HashSet<(int, int, int)>();
                foreach (var o in f.missingFrom)
                {
                    var r = renderers.FirstOrDefault(x => x.name == o.renderer);
                    if (r == null)
                    {
                        fillNote.Append($" (no renderer {o.renderer})");
                        continue;
                    }
                    var toModel = ToModel(r.transform);
                    var rv = r.sharedMesh.vertices;
                    var subs = RoeBurstBuilder.Subs(o, r);
                    if (subs.Length == 0)
                        continue;
                    if (fillMaterial == null)
                        fillMaterial = r.sharedMaterials[Mathf.Clamp(subs[0], 0, r.sharedMaterials.Length - 1)];
                    foreach (int sub in subs)
                    {
                        var t = r.sharedMesh.GetTriangles(sub);
                        for (int k = 0; k + 2 < t.Length; k += 3)
                            suitTris.Add(Sorted(places.Add(toModel.MultiplyPoint3x4(rv[t[k]])), places.Add(toModel.MultiplyPoint3x4(rv[t[k + 1]])),
                                                places.Add(toModel.MultiplyPoint3x4(rv[t[k + 2]]))));
                    }
                }
                var baseTris = mesh.GetTriangles(f.submesh);
                var missing = new List<int>();
                for (int k = 0; k + 2 < baseTris.Length; k += 3)
                {
                    int a = places.Find(nudeToModel.MultiplyPoint3x4(nv[baseTris[k]]));
                    int b = places.Find(nudeToModel.MultiplyPoint3x4(nv[baseTris[k + 1]]));
                    int c = places.Find(nudeToModel.MultiplyPoint3x4(nv[baseTris[k + 2]]));
                    if (a < 0 || b < 0 || c < 0 || !suitTris.Contains(Sorted(a, b, c)))
                        missing.AddRange(new[] { baseTris[k], baseTris[k + 1], baseTris[k + 2] });
                }
                int total = baseTris.Length / 3, lacking = missing.Count / 3;
                // a suit whose head is not cut out of this base would get the whole head twice
                if (lacking > total * MaxFill)
                {
                    fillNote.Append($" submesh {f.submesh}: {lacking} of {total} triangles not in the suit - more than {MaxFill:P0}, NOT filled (the suit's is another mesh)");
                    continue;
                }
                fillTris.AddRange(missing);
                fillNote.Append($" submesh {f.submesh}: {lacking} of {total} triangles the suit lacks");
            }
            var used = tris.Concat(fillTris).Distinct().OrderBy(i => i).ToList();
            // where each vertex lies: where the base has it, or ("pose": "bones") carried from the base's bind pose to the suit's
            // by its own weights
            string placeNote = "";
            var place = n.pose == "bones" ? PlaceByBones(model, renderers, prefab, nudeRenderer, nudeToModel, used, out placeNote) : null;
            Vector3 Placed(int i, Vector3 v) => place != null ? place[i].MultiplyPoint3x4(nudeToModel.MultiplyPoint3x4(v)) : nudeToModel.MultiplyPoint3x4(v);
            Vector3 PlacedDir(int i, Vector3 d) => place != null ? place[i].MultiplyVector(nudeToModel.MultiplyVector(d)) : nudeToModel.MultiplyVector(d);
            var position = new Dictionary<int, Vector3>();
            foreach (int i in used)
                position[i] = Placed(i, nv[i]);
            var normalOf = new Dictionary<int, Vector3>();
            foreach (int i in used)
                normalOf[i] = PlacedDir(i, nn0[i]).normalized;
            string fitNote = n.fit ? Fit(model, renderers, n, analysis, used, position, normalOf) : "";
            var bodyPoints = used.Select(i => position[i]).ToList();
            var bodyGrid = new Grid(bodyPoints, Enumerable.Range(0, bodyPoints.Count));
            var near = new List<(int i, float d)>();
            float ToBody(Vector3 q, float limit)
            {
                bodyGrid.Near(q, limit, near);
                return near.Count > 0 ? near.Min(x => x.d) : limit;
            }

            // the outfit: a piece is tight when it lies on the body (median distance of its vertices at most 2.5 cm) -
            // armour, bra, gloves, shoes; skirt panels, chains and ribbons hang away from it and give no weights
            int tightParts = 0, looseParts = 0;
            var cover = new List<(Vector3 a, Vector3 b, Vector3 c, Vector3 n, int stage)>();     // reveal: opaque triangles lying on the body
            foreach (var g in analysis.parts.GroupBy(x => x.renderer))
            {
                var r = g.Key;
                var stays = new Dictionary<int, bool>();
                var onBody = new Dictionary<int, bool>();
                var follows = new Dictionary<int, bool>();
                var stageOfV = new Dictionary<int, int>();
                var toModel = ToModel(r.transform);
                var rv = r.sharedMesh.vertices;
                var rn = r.sharedMesh.normals;
                var mats = r.sharedMaterials;
                var subTris = new Dictionary<int, int[]>();
                int[] Tris(int sub) => subTris.TryGetValue(sub, out var t) ? t : subTris[sub] = r.sharedMesh.GetTriangles(sub);
                var subSolid = new Dictionary<int, bool[]>();
                bool[] Solid(int sub) => subSolid.TryGetValue(sub, out var s) ? s
                    : subSolid[sub] = SolidTriangles(mats[Mathf.Clamp(sub, 0, mats.Length - 1)], r.sharedMesh, sub);
                foreach (var part in g)
                {
                    bool forever = part.group == null || part.group.stage <= 0;
                    var verts = new HashSet<int>();
                    foreach (var kv in part.triangles)
                    {
                        var t = Tris(kv.Key);
                        foreach (int tri in kv.Value)
                            for (int c = 0; c < 3; c++)
                                verts.Add(t[tri * 3 + c]);
                    }
                    var dist = verts.Select(v => ToBody(toModel.MultiplyPoint3x4(rv[v]), 0.05f)).OrderBy(x => x).ToList();
                    bool lies = dist.Count > 0 && dist[dist.Count / 2] <= 0.025f;
                    if (lies)
                        tightParts++;
                    else
                        looseParts++;
                    if (n.reveal && (lies || forever))      // shoes and soles hide the foot however thick they are; a loose
                                                            // piece (a skirt) does not hide what shows under its hem
                        foreach (var kv in part.triangles)
                        {
                            var solid = Solid(kv.Key);
                            if (solid == null)
                                continue;
                            var t = Tris(kv.Key);
                            foreach (int tri in kv.Value)
                            {
                                if (!solid[tri])
                                    continue;
                                int i0 = t[tri * 3], i1 = t[tri * 3 + 1], i2 = t[tri * 3 + 2];
                                Vector3 a = toModel.MultiplyPoint3x4(rv[i0]), b = toModel.MultiplyPoint3x4(rv[i1]), c = toModel.MultiplyPoint3x4(rv[i2]);
                                var fn = Vector3.Cross(b - a, c - a);
                                if (fn.sqrMagnitude < 1e-14f)
                                    continue;
                                fn.Normalize();
                                if (rn.Length == rv.Length && Vector3.Dot(fn, toModel.MultiplyVector(rn[i0] + rn[i1] + rn[i2])) < 0f)
                                    fn = -fn;
                                cover.Add((a, b, c, fn, forever ? int.MaxValue : part.group.stage));
                            }
                        }
                    foreach (int v in verts)
                    {
                        stays[v] = forever;
                        onBody[v] = lies;
                        follows[v] = part.group != null && part.group.followBody;
                        stageOfV[v] = forever ? int.MaxValue : part.group.stage;
                    }
                }
                Add(r, stays.Keys, false, i => stays[i], i => onBody[i], i => follows[i], i => stageOfV[i]);
            }
            int skinCount = skin.Count(x => x);
            var all = Enumerable.Range(0, points.Count).ToList();
            var skinGrid = new Grid(points, all.Where(i => skin[i]));
            var tightGrid = new Grid(points, all.Where(i => !skin[i] && tight[i] && !follow[i]));
            var followGrid = new Grid(points, all.Where(i => !skin[i] && follow[i]));
            int fromRound = 0;
            var foreverGrid = new Grid(points, all.Where(i => !skin[i] && permanent[i]));
            var allGrid = new Grid(points, all);

            // ---- each vertex takes the weights of what lies on it: the suit's skin within 1.5 cm; else a tight piece
            // clearly nearer than any skin (where the game cut the skin away); else the nearest skin
            var weightsOf = new Dictionary<int, (Transform bone, float weight)[]>();
            var under = new HashSet<int>();         // only under pieces that never come off
            int fromSkin = 0, fromOutfit = 0, lost = 0, unhooked = 0, fromOwn = 0, ownMixed = 0;
            string ownNote = "";
            var ownOf = !string.IsNullOrEmpty(n.ownWeights)
                ? OwnWeights(model, prefab, nudeRenderer, nudeToModel, ToModel(target.transform), bindpose, used, new Regex(n.ownWeights), out ownNote)
                : null;
            Dictionary<Transform, float> Gather(List<(int i, float d)> sources)
            {
                var sum = new Dictionary<Transform, float>();
                foreach (var (k, d) in sources)
                {
                    float w = 1f / ((d + 0.002f) * (d + 0.002f));
                    foreach (var (bone, weight) in influence[k])
                        sum[bone] = (sum.TryGetValue(bone, out float s) ? s : 0f) + w * weight;
                }
                return sum;
            }
            var skinGap = new List<float>();
            (float d, List<(int i, float d)> list) Nearest(Grid grid, Vector3 q, float radius, float band)
            {
                grid.Near(q, radius, near);
                if (near.Count == 0)
                    return (float.PositiveInfinity, null);
                float d0 = near.Min(x => x.d);
                return (d0, near.Where(x => x.d <= d0 + band).OrderBy(x => x.d).Take(8).ToList());
            }
            // "ownMirror": the region's weights from the suit's skin on her other side
            Dictionary<int, (Transform bone, float weight)[]> mirrored = null;
            if (ownOf != null && n.ownMirror)
            {
                mirrored = new Dictionary<int, (Transform bone, float weight)[]>();
                var mirName = new Dictionary<string, Transform>();
                foreach (var t in model.GetComponentsInChildren<Transform>(true))
                    if (!mirName.ContainsKey(t.name))
                        mirName[t.name] = t;
                if (mirName.TryGetValue("Bip001 L Clavicle", out var mirL) && mirName.TryGetValue("Bip001 R Clavicle", out var mirR))
                {
                    Vector3 pL = ToModel(mirL).MultiplyPoint3x4(Vector3.zero), pR = ToModel(mirR).MultiplyPoint3x4(Vector3.zero);
                    Vector3 mid = (pL + pR) * 0.5f, across = (pR - pL).normalized;
                    int far = 0, unbound = 0, covered = 0;
                    foreach (int i in ownOf.Keys)
                    {
                        if (!position.TryGetValue(i, out var p))
                            continue;
                        var q = p - 2f * Vector3.Dot(p - mid, across) * across;
                        var (dq, onOther) = Nearest(skinGrid, q, 0.08f, 0.005f);
                        if (onOther == null || dq > MirrorReach)
                        {
                            var (dc, onCover) = Nearest(tightGrid, q, 0.08f, 0.005f);
                            if (onCover == null || dc > MirrorCover)
                            {
                                far++;
                                continue;
                            }
                            onOther = onCover;
                            covered++;
                        }
                        var swapped = new Dictionary<Transform, float>();
                        bool bound = true;
                        foreach (var kv in Gather(onOther))
                        {
                            if (Loose(kv.Key))
                                continue;
                            string other = SideSwap(kv.Key.name);
                            var bone = other != kv.Key.name && mirName.TryGetValue(other, out var o) ? o : kv.Key;
                            if (!bindpose.ContainsKey(bone))
                            {
                                bound = false;
                                break;
                            }
                            swapped[bone] = (swapped.TryGetValue(bone, out float had) ? had : 0f) + kv.Value;
                        }
                        if (!bound || swapped.Count == 0)
                        {
                            unbound++;
                            continue;
                        }
                        var top = swapped.OrderByDescending(kv => kv.Value).Take(4).ToArray();
                        float total = top.Sum(kv => kv.Value);
                        mirrored[i] = top.Select(kv => (kv.Key, kv.Value / total)).ToArray();
                    }
                    ownNote += $"; mirrored from her other side: {mirrored.Count} ({covered} from a piece lying there), {far} with no skin there, " +
                               $"{unbound} on a bone not bound on this side";
                }
                else
                    ownNote += "; ownMirror: no Bip001 L / R Clavicle to mirror across";
            }
            foreach (int i in used)
            {
                var p = position[i];
                List<(int i, float d)> pick = null;
                var (ds, onSkin) = Nearest(skinGrid, p, 0.08f, 0.005f);
                // "ownWeights": no suit skin here (or ownOverSkin) - the base's own weights, not what lies over it, as far
                // as the base hangs the vertex on the region's bones (blended with that across the region's edge)
                (Transform bone, float weight)[] mine = null;
                float ownShare = 0f;
                if ((ds > SkinReach || n.ownOverSkin) && ownOf != null && ownOf.TryGetValue(i, out var ownEntry))
                {
                    mine = mirrored != null && mirrored.TryGetValue(i, out var fromOther) ? fromOther : ownEntry.weights;
                    ownShare = Mathf.Clamp01((ownEntry.share - OwnFrom) / (OwnTo - OwnFrom));
                }
                if (ds <= SkinReach)
                {
                    skinGap.Add(ds);
                    pick = onSkin;
                    fromSkin++;
                }
                else if (mine != null && ownShare >= 1f)
                {
                    weightsOf[i] = mine;
                    fromOwn++;
                    foreverGrid.Near(p, 0.025f, near);
                    if (near.Count > 0)
                        under.Add(i);
                    continue;
                }
                else
                {
                    var (dt, onTight) = Nearest(tightGrid, p, 0.08f, 0.01f);
                    followGrid.Near(p, 0.025f, near);
                    bool underFollower = near.Count > 0 && near.Min(x => x.d) < dt;
                    if (underFollower && onSkin != null)
                    {
                        // under a piece that follows the body: the skin round the hole it covers, from all sides
                        skinGrid.Near(p, 0.12f, near);
                        float d0 = near.Min(x => x.d);
                        pick = near.Where(x => x.d <= d0 + 0.04f).OrderBy(x => x.d).Take(16).ToList();
                        fromRound++;
                    }
                    else if (onTight != null && dt < ds - 0.005f)
                    {
                        pick = onTight;
                        fromOutfit++;
                    }
                    else if (onSkin != null)
                    {
                        pick = onSkin;
                        fromSkin++;
                    }
                    else
                        foreach (float radius in new[] { 0.16f, 0.32f })
                        {
                            var (_, any) = Nearest(allGrid, p, radius, 0.01f);
                            if (any == null)
                                continue;
                            pick = any;
                            fromOutfit++;
                            break;
                        }
                    // under a piece that never comes off (g04's shoes): left out, it would only poke through
                    foreverGrid.Near(p, 0.025f, near);
                    if (near.Count > 0)
                        under.Add(i);
                }
                if (pick == null && mine != null && ownShare > 0f)
                {
                    weightsOf[i] = mine;
                    fromOwn++;
                    continue;
                }
                if (pick == null)
                {
                    lost++;
                    continue;
                }
                var sum = Gather(pick);
                // a body never hangs on loose cloth (user 10-11: "luf的腿部权重是否有问题，似乎和裙子有粘连" - six of g04's
                // thigh vertices, where the game cut her skin away under a skirt piece lying on the thigh, took that piece's
                // skirt bones whole, and the swinging skirt pulled the skin with it): the skirt's, sashes', hair's and
                // chains' bones give no weight; a vertex left with none takes the nearest skin's
                int before = sum.Count;
                foreach (var bone in sum.Keys.Where(Loose).ToList())
                    sum.Remove(bone);
                if (sum.Count < before)
                    unhooked++;
                if (sum.Count == 0 && onSkin != null)
                {
                    sum = Gather(onSkin);
                    foreach (var bone in sum.Keys.Where(Loose).ToList())
                        sum.Remove(bone);
                }
                if (mine != null && ownShare > 0f)
                {
                    // across the region's edge: its own weights by the share, what lies over it by the rest
                    float copiedSum = sum.Values.Sum();
                    foreach (var bone in sum.Keys.ToList())
                        sum[bone] *= copiedSum > 0f ? (1f - ownShare) / copiedSum : 0f;
                    foreach (var (bone, weight) in mine)
                        sum[bone] = (sum.TryGetValue(bone, out float had) ? had : 0f) + ownShare * weight;
                    ownMixed++;
                }
                if (sum.Count == 0)
                {
                    lost++;
                    continue;
                }
                var top = sum.OrderByDescending(kv => kv.Value).Take(4).ToArray();
                float total = top.Sum(kv => kv.Value);
                weightsOf[i] = top.Select(kv => (kv.Key, kv.Value / total)).ToArray();
            }

            // ---- ownMirror: the region's weights smoothed over the surface, vertices at one place as one (UV seams split them)
            if (mirrored != null && mirrored.Count > 0)
            {
                var smGroup = new Dictionary<int, int>();
                var smKey = new Dictionary<Vector3Int, int>();
                foreach (int i in used)
                {
                    var at = position[i];
                    var cell = new Vector3Int(Mathf.RoundToInt(at.x * 10000f), Mathf.RoundToInt(at.y * 10000f), Mathf.RoundToInt(at.z * 10000f));
                    if (!smKey.TryGetValue(cell, out int g))
                        smKey[cell] = g = smKey.Count;
                    smGroup[i] = g;
                }
                var smMembers = new List<int>[smKey.Count];
                var smNext = new HashSet<int>[smKey.Count];
                for (int g = 0; g < smKey.Count; g++)
                {
                    smMembers[g] = new List<int>();
                    smNext[g] = new HashSet<int>();
                }
                foreach (var kv in smGroup)
                    smMembers[kv.Value].Add(kv.Key);
                for (int t = 0; t + 2 < tris.Length; t += 3)
                    for (int e = 0; e < 3; e++)
                        if (smGroup.TryGetValue(tris[t + e], out int ga) && smGroup.TryGetValue(tris[t + (e + 1) % 3], out int gb) && ga != gb)
                        {
                            smNext[ga].Add(gb);
                            smNext[gb].Add(ga);
                        }
                // each group's weights: its members' together
                var smW = new Dictionary<Transform, float>[smKey.Count];
                for (int g = 0; g < smKey.Count; g++)
                {
                    var sum = new Dictionary<Transform, float>();
                    foreach (int m in smMembers[g])
                        if (weightsOf.TryGetValue(m, out var w))
                            foreach (var (bone, weight) in w)
                                sum[bone] = (sum.TryGetValue(bone, out float had) ? had : 0f) + weight;
                    float total = sum.Values.Sum();
                    smW[g] = total > 0f ? sum.ToDictionary(kv => kv.Key, kv => kv.Value / total) : null;
                }
                var smRegion = ownOf.Keys.Where(smGroup.ContainsKey).Select(i => smGroup[i]).Distinct().Where(g => smW[g] != null).ToList();
                for (int pass = 0; pass < MirrorSmooth; pass++)
                {
                    var next = new Dictionary<int, Dictionary<Transform, float>>();
                    foreach (int g in smRegion)
                    {
                        var sum = new Dictionary<Transform, float>(smW[g]);
                        int count = 1;
                        foreach (int h in smNext[g])
                        {
                            if (smW[h] == null)
                                continue;
                            foreach (var kv in smW[h])
                                sum[kv.Key] = (sum.TryGetValue(kv.Key, out float had) ? had : 0f) + kv.Value;
                            count++;
                        }
                        next[g] = sum.ToDictionary(kv => kv.Key, kv => kv.Value / count);
                    }
                    foreach (var kv in next)
                        smW[kv.Key] = kv.Value;
                }
                foreach (int g in smRegion)
                {
                    var top = smW[g].OrderByDescending(kv => kv.Value).Take(4).ToArray();
                    float total = top.Sum(kv => kv.Value);
                    var smoothed = top.Select(kv => (kv.Key, kv.Value / total)).ToArray();
                    foreach (int m in smMembers[g])
                        if (weightsOf.ContainsKey(m))
                            weightsOf[m] = smoothed;
                }
                ownNote += $"; {smRegion.Count} places smoothed {MirrorSmooth} times";
            }

            // ---- (reveal) the stage after which each vertex is drawn: the last stage of the opaque pieces over it, 0 none
            var coverer = n.reveal ? new Cover(cover) : null;
            var revealAt = coverer != null ? used.ToDictionary(i => i, i => coverer.Stage(position[i], normalOf[i], CoverReach)) : null;
            // ...and one under no piece and away from the suit's skin that shows waits for the pieces round it (never: only pieces
            // that stay on); the suit's skin shows where no opaque piece lies over it
            int outside = 0, skinShown = 0, skinHidden = 0;
            Grid shownSkin = null;
            if (coverer != null)
            {
                var shown = all.Where(k => skin[k] && coverer.Stage(points[k], pointNormal[k], CoverReach, SkinAside) == 0).ToList();
                skinShown = shown.Count;
                skinHidden = skinCount - shown.Count;
                shownSkin = new Grid(points, shown);
            }
            var coveredAt = revealAt != null ? new Dictionary<int, int>(revealAt) : null;
            var outfitGrid = revealAt != null ? new Grid(points, all.Where(k => !skin[k])) : null;
            var byShownSkin = new HashSet<int>();
            if (revealAt != null)
                foreach (int i in used)
                {
                    if (revealAt[i] != 0)
                        continue;
                    shownSkin.Near(position[i], NearSkin, near);
                    if (near.Count > 0)
                    {
                        byShownSkin.Add(i);
                        continue;
                    }
                    // the pieces nearest to it, however far (her base's flat bare feet reach 8.6 cm further forward than eve09's
                    // suit feet): those within 3 cm of the nearest
                    foreach (float radius in new[] { AroundPieces, 2f * AroundPieces, 4f * AroundPieces })
                    {
                        outfitGrid.Near(position[i], radius, near);
                        if (near.Count > 0)
                            break;
                    }
                    int stage = 0;
                    bool staying = false;
                    float nearest = near.Count > 0 ? near.Min(x => x.d) : 0f;
                    foreach (var (k, d) in near)
                    {
                        if (d > nearest + 0.03f)
                            continue;
                        if (pointStage[k] == int.MaxValue)
                            staying = true;
                        else
                            stage = Mathf.Max(stage, pointStage[k]);
                    }
                    if (stage == 0 && staying)
                        stage = int.MaxValue;
                    if (stage != 0)
                    {
                        revealAt[i] = stage;
                        outside++;
                    }
                }

            if (revealAt != null)
            {
                // each vertex's verdict, for looking into why a part shows: _work/burst_reveal/<id>.tsv
                string dumpDir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "_work", "burst_reveal");
                Directory.CreateDirectory(dumpDir);
                var tsv = new StringBuilder("vertex\tx\ty\tz\tcovered\tshown_skin_near\tdrawn_from\n");
                string St(int v) => v == int.MaxValue ? "never" : v.ToString();
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                foreach (int i in used)
                    tsv.Append($"{i}\t{position[i].x.ToString("F4", inv)}\t{position[i].y.ToString("F4", inv)}\t{position[i].z.ToString("F4", inv)}\t{St(coveredAt[i])}\t{(byShownSkin.Contains(i) ? 1 : 0)}\t{St(revealAt[i])}\n");
                File.WriteAllText(Path.Combine(dumpDir, $"{id}.tsv"), tsv.ToString());
                var pts = new StringBuilder("point\tx\ty\tz\tskin\tstage\n");
                for (int k = 0; k < points.Count; k++)
                    pts.Append($"{k}\t{points[k].x.ToString("F4", inv)}\t{points[k].y.ToString("F4", inv)}\t{points[k].z.ToString("F4", inv)}\t{(skin[k] ? 1 : 0)}\t{St(pointStage[k])}\n");
                File.WriteAllText(Path.Combine(dumpDir, $"{id}_outfit.tsv"), pts.ToString());
            }

            // ---- triangles: all but those only under pieces that never come off (and any with a vertex left without weights);
            // with reveal each with the stage it is drawn from
            var keep = new List<int>();
            var keepStage = new List<int>();
            var keepSub = new List<int>();          // the body submesh (index into bodySubs) each kept triangle is from
            int dropped = 0;
            for (int t = 0; t + 2 < tris.Length; t += 3)
            {
                int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                int stage = revealAt != null ? Mathf.Min(revealAt[a], Mathf.Min(revealAt[b], revealAt[c])) : 0;
                if (!weightsOf.ContainsKey(a) || !weightsOf.ContainsKey(b) || !weightsOf.ContainsKey(c) ||
                    (under.Contains(a) && under.Contains(b) && under.Contains(c)) || stage == int.MaxValue)
                {
                    dropped++;
                    continue;
                }
                keep.Add(a);
                keep.Add(b);
                keep.Add(c);
                keepStage.Add(stage);
                keepSub.Add(triSub[t / 3]);
            }
            var keepFill = new List<int>();
            for (int t = 0; t + 2 < fillTris.Count; t += 3)
            {
                int a = fillTris[t], b = fillTris[t + 1], c = fillTris[t + 2];
                if (!weightsOf.ContainsKey(a) || !weightsOf.ContainsKey(b) || !weightsOf.ContainsKey(c))
                    continue;
                keepFill.Add(a);
                keepFill.Add(b);
                keepFill.Add(c);
            }
            if (fillMaterial == null)
                keepFill.Clear();

            // ---- the mesh, in the suit renderer's space, on the suit's bones
            var order = keep.Concat(keepFill).Distinct().ToList();
            var map = new Dictionary<int, int>();
            for (int k = 0; k < order.Count; k++)
                map[order[k]] = k;
            string meshPath = $"{dir}/nude_{RoeBurstBuilder.Safe(n.renderer)}.asset";
            var stagesShown = keepStage.Where(s => s > 0).Distinct().OrderBy(s => s).ToList();
            bool split = stagesShown.Count > 0;
            var outMesh = split ? new Mesh { name = "nude whole" } : RoeBurstBuilder.MeshAsset(meshPath);
            outMesh.indexFormat = order.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            outMesh.vertices = order.Select(i => modelToTarget.MultiplyPoint3x4(position[i])).ToArray();
            if (mesh.HasVertexAttribute(VertexAttribute.Normal))
                outMesh.normals = order.Select(i => modelToTarget.MultiplyVector(normalOf[i]).normalized).ToArray();
            if (mesh.HasVertexAttribute(VertexAttribute.Tangent))
            {
                var tt = mesh.tangents;
                outMesh.tangents = order.Select(i =>
                {
                    var d = modelToTarget.MultiplyVector(PlacedDir(i, new Vector3(tt[i].x, tt[i].y, tt[i].z))).normalized;
                    return new Vector4(d.x, d.y, d.z, tt[i].w);
                }).ToArray();
            }
            if (mesh.HasVertexAttribute(VertexAttribute.Color))
            {
                var cc = mesh.colors32;
                outMesh.colors32 = order.Select(i => cc[i]).ToArray();
            }
            for (int ch = 0; ch < 8; ch++)
            {
                if (!mesh.HasVertexAttribute(VertexAttribute.TexCoord0 + ch))
                    continue;
                var l = new List<Vector4>();
                mesh.GetUVs(ch, l);
                int dim = mesh.GetVertexAttributeDimension(VertexAttribute.TexCoord0 + ch);
                if (dim <= 2)
                    outMesh.SetUVs(ch, order.Select(i => (Vector2)l[i]).ToList());
                else if (dim == 3)
                    outMesh.SetUVs(ch, order.Select(i => (Vector3)l[i]).ToList());
                else
                    outMesh.SetUVs(ch, order.Select(i => l[i]).ToList());
            }
            var bones = weightsOf.Values.SelectMany(w => w.Select(x => x.bone)).Distinct().ToList();
            var boneIndex = new Dictionary<Transform, int>();
            for (int k = 0; k < bones.Count; k++)
                boneIndex[bones[k]] = k;
            var counts = new NativeArray<byte>(order.Count, Allocator.Temp);
            var flat = new List<BoneWeight1>();
            for (int k = 0; k < order.Count; k++)
            {
                var w = weightsOf[order[k]];
                counts[k] = (byte)w.Length;
                foreach (var (bone, weight) in w)
                    flat.Add(new BoneWeight1 { boneIndex = boneIndex[bone], weight = weight });
            }
            var weights = new NativeArray<BoneWeight1>(flat.ToArray(), Allocator.Temp);
            outMesh.bindposes = bones.Select(b => bindpose[b]).ToArray();
            outMesh.SetBoneWeights(counts, weights);
            counts.Dispose();
            weights.Dispose();
            // the triangles by stage (reveal: drawn from that stage on; 0 always) and body submesh, as the new mesh numbers them
            int nb = bodySubs.Length;
            List<int>[] PerSub() => Enumerable.Range(0, nb).Select(_ => new List<int>()).ToArray();
            var byStage = new Dictionary<int, List<int>[]>();
            var bySub = PerSub();
            for (int k = 0; k < keepStage.Count; k++)
            {
                if (!byStage.TryGetValue(keepStage[k], out var lists))
                    byStage[keepStage[k]] = lists = PerSub();
                for (int c = 0; c < 3; c++)
                {
                    lists[keepSub[k]].Add(map[keep[k * 3 + c]]);
                    bySub[keepSub[k]].Add(map[keep[k * 3 + c]]);
                }
            }
            int Count(List<int>[] lists) => lists.Sum(l => l.Count) / 3;
            var fillMapped = keepFill.Select(i => map[i]).ToList();
            outMesh.subMeshCount = nb + (keepFill.Count > 0 ? 1 : 0);
            for (int k = 0; k < nb; k++)
                outMesh.SetTriangles(bySub[k].ToArray(), k, false);
            if (keepFill.Count > 0)
                outMesh.SetTriangles(fillMapped.ToArray(), nb, false);
            outMesh.RecalculateBounds();
            Mesh asset;
            var stageAssets = new Dictionary<int, (Mesh mesh, Material[] materials)>();
            if (split)
            {
                // the whole body cut by stage: what is always drawn (every body submesh, then the fill), then a mesh per stage with
                // the body submeshes it has triangles of (the same vertices, weights, bind poses)
                var always = byStage.TryGetValue(0, out var l0) ? l0 : PerSub();
                var lists0 = keepFill.Count > 0 ? always.Append(fillMapped).ToArray() : always;
                asset = RoeBurstBuilder.Commit(RoeBurstBuilder.Subset(outMesh, lists0, RoeBurstBuilder.MeshAsset(meshPath)), meshPath);
                foreach (int s in stagesShown)
                {
                    string path = $"{dir}/nude_{RoeBurstBuilder.Safe(n.renderer)}_stage{s}.asset";
                    var has = Enumerable.Range(0, nb).Where(k => byStage[s][k].Count > 0).ToArray();
                    stageAssets[s] = (RoeBurstBuilder.Commit(RoeBurstBuilder.Subset(outMesh, has.Select(k => byStage[s][k]).ToArray(), RoeBurstBuilder.MeshAsset(path)), path),
                                      has.Select(k => materials[k]).ToArray());
                }
                Object.DestroyImmediate(outMesh);
            }
            else
                asset = RoeBurstBuilder.Commit(outMesh, meshPath);

            SkinnedMeshRenderer Renderer(string name, Mesh m, Material[] materials)
            {
                var go = new GameObject(name) { layer = target.gameObject.layer };
                go.transform.SetParent(target.transform.parent, false);
                go.transform.localPosition = target.transform.localPosition;
                go.transform.localRotation = target.transform.localRotation;
                go.transform.localScale = target.transform.localScale;
                var r = go.AddComponent<SkinnedMeshRenderer>();
                r.sharedMesh = m;
                r.bones = bones.ToArray();
                r.rootBone = target.rootBone;
                r.sharedMaterials = materials;
                r.localBounds = target.localBounds;
                r.updateWhenOffscreen = target.updateWhenOffscreen;
                r.quality = target.quality;
                r.skinnedMotionVectors = target.skinnedMotionVectors;
                r.shadowCastingMode = target.shadowCastingMode;
                r.receiveShadows = target.receiveShadows;
                r.lightProbeUsage = target.lightProbeUsage;
                r.reflectionProbeUsage = target.reflectionProbeUsage;
                r.probeAnchor = target.probeAnchor;
                r.forceMatrixRecalculationPerRender = target.forceMatrixRecalculationPerRender;
                r.allowOcclusionWhenDynamic = target.allowOcclusionWhenDynamic;
                r.renderingLayerMask = target.renderingLayerMask;
                return r;
            }
            var smr = Renderer("nude body", asset, keepFill.Count > 0 ? materials.Append(fillMaterial).ToArray() : materials);
            var revealNote = new StringBuilder();
            foreach (var kv in stageAssets)
            {
                var r = Renderer($"nude body (stage {kv.Key})", kv.Value.mesh, kv.Value.materials);
                r.enabled = false;                  // RoeClothesBurst switches it on once that stage is off
                int count = Count(byStage[kv.Key]);
                reveals?.Add(new RoeClothesBurst.Reveal { stage = kv.Key, renderer = r, triangles = count });
                revealNote.Append($"{(revealNote.Length > 0 ? ", " : "")}{count} after stage {kv.Key}");
            }

            skinGap.Sort();
            float Pct(float q) => skinGap.Count > 0 ? skinGap[Mathf.Clamp(Mathf.RoundToInt(q * (skinGap.Count - 1)), 0, skinGap.Count - 1)] * 1000f : -1f;
            log.Append($"\n[ROE]   nude body from {Path.GetFileName(n.prefab)} {n.renderer}[{string.Join(",", bodySubs)}]: {used.Count} vertices, " +
                       $"{fromSkin} take the suit's skin (gap median {Pct(0.5f):F2} mm, p95 {Pct(0.95f):F2} mm), {fromOutfit} the outfit over them, " +
                       (ownOf != null ? $"{fromOwn} keep their own, {ownMixed} blend theirs in ({ownNote}), " : "") +
                       (fromRound > 0 ? $"{fromRound} the skin round a piece that follows the body, " : "") +
                       (unhooked > 0 ? $"{unhooked} had loose cloth (skirt, sash, hair, chain) among their sources and let it go, " : "") +
                       $"{lost} nothing; {dropped} of {tris.Length / 3} triangles left out (under pieces that never come off); " +
                       $"outfit pieces on the body {tightParts}, hanging away {looseParts}; " +
                       $"{bones.Count} bones; {skinCount} skin + {points.Count - skinCount} outfit points; material{(nb > 1 ? "s" : "")} " +
                       string.Join(", ", materials.Select(m => $"{m.name} ({m.shader.name})")) +
                       (place != null ? $"; placed by its bones: {placeNote}" : "") +
                       (n.fit ? $"; fit: {fitNote}" : "") +
                       (n.reveal ? $"; reveal: {(byStage.TryGetValue(0, out var shown0) ? Count(shown0) : 0)} triangles always drawn, " +
                                   (revealNote.Length > 0 ? revealNote.ToString() : "nothing covered") +
                                   $" ({cover.Count} opaque triangles on the body; the suit's skin shows at {skinShown} points, {skinHidden} under pieces; " +
                                   $"{outside} vertices under none and off the skin that shows wait for the pieces round them)" : "") +
                       (fills.Count > 0 ? $"; fill:{fillNote} -> {keepFill.Count / 3} triangles added ({(fillMaterial != null ? fillMaterial.name : "no material")})" : ""));
            return smr;
        }

        /// <summary>
        /// "fit": within FitInner of a piece that never comes off, a vertex of the base goes to the nearest point of the suit's skin
        /// it replaces (FitReach at most), its normal to that skin's; blended back to its own place out to FitOuter.  Returns a note.
        /// </summary>
        static string Fit(GameObject model, SkinnedMeshRenderer[] renderers, RoeBurstBuilder.Nude n, RoeBurstBuilder.Analysis analysis,
                          List<int> used, Dictionary<int, Vector3> position, Dictionary<int, Vector3> normalOf)
        {
            var root = model.transform;
            Matrix4x4 ToModelOf(Transform t) => root.worldToLocalMatrix * t.localToWorldMatrix;
            // the suit's skin, as triangles
            var skinTris = new List<(Vector3 a, Vector3 b, Vector3 c, Vector3 n, int stage)>();
            foreach (var s in n.replace ?? new RoeBurstBuilder.Outfit[0])
            {
                var r = renderers.FirstOrDefault(x => x.name == s.renderer);
                if (r == null)
                    continue;
                var toModel = ToModelOf(r.transform);
                var v = r.sharedMesh.vertices;
                var vn = r.sharedMesh.normals;
                foreach (int sub in RoeBurstBuilder.Subs(s, r))
                {
                    var t = r.sharedMesh.GetTriangles(sub);
                    for (int k = 0; k + 2 < t.Length; k += 3)
                    {
                        Vector3 a = toModel.MultiplyPoint3x4(v[t[k]]), b = toModel.MultiplyPoint3x4(v[t[k + 1]]), c = toModel.MultiplyPoint3x4(v[t[k + 2]]);
                        var fn = Vector3.Cross(b - a, c - a);
                        if (fn.sqrMagnitude < 1e-14f)
                            continue;
                        fn.Normalize();
                        if (vn.Length == v.Length && Vector3.Dot(fn, toModel.MultiplyVector(vn[t[k]] + vn[t[k + 1]] + vn[t[k + 2]])) < 0f)
                            fn = -fn;
                        skinTris.Add((a, b, c, fn, 0));
                    }
                }
            }
            // the points of the pieces that never come off
            var stay = new List<Vector3>();
            foreach (var part in analysis.parts.Where(x => x.group == null || x.group.stage <= 0))
            {
                var toModel = ToModelOf(part.renderer.transform);
                var v = part.renderer.sharedMesh.vertices;
                foreach (var kv in part.triangles)
                {
                    var t = part.renderer.sharedMesh.GetTriangles(kv.Key);
                    foreach (int tri in kv.Value)
                        for (int c = 0; c < 3; c++)
                            stay.Add(toModel.MultiplyPoint3x4(v[t[tri * 3 + c]]));
                }
            }
            if (skinTris.Count == 0 || stay.Count == 0)
                return "nothing to fit (no suit skin or no piece that stays on)";
            var skin = new Cover(skinTris);
            var stayGrid = new Grid(stay, Enumerable.Range(0, stay.Count));
            var near = new List<(int i, float d)>();
            int whole = 0, blended = 0, noSkin = 0;
            float moved = 0f;
            foreach (int i in used)
            {
                stayGrid.Near(position[i], FitOuter, near);
                if (near.Count == 0)
                    continue;
                float w = 1f - Mathf.InverseLerp(FitInner, FitOuter, near.Min(x => x.d));
                if (!skin.Nearest(position[i], FitReach, out var q, out var qn))
                {
                    noSkin++;
                    continue;
                }
                moved = Mathf.Max(moved, (q - position[i]).magnitude * w);
                position[i] = Vector3.Lerp(position[i], q, w);
                normalOf[i] = Vector3.Lerp(normalOf[i], qn, w).normalized;
                if (w >= 0.999f)
                    whole++;
                else
                    blended++;
            }
            return $"{whole} vertices on the suit's skin, {blended} blended, {noSkin} near a piece that stays on with no suit skin within " +
                   $"{FitReach * 100f:F0} cm (left as they are); moved at most {moved * 100f:F1} cm";
        }

        /// <summary>
        /// Which triangles of a submesh hide what is behind them: none of a blended material (see-through plastic), all of an
        /// opaque one; of one cut out by its alpha, those with at least SolidShare of their texels at or over its cutoff (the
        /// albedo's alpha times the colour's) - lace is full of holes (Eve's), but Vindictus cuts out all of an outfit: Fiona's
        /// PCF_005 dress, cuffs, shoes and tiara are one "pbr cutout" material each, the dress solid, its hem and the cuffs lace.
        /// A triangle smaller than a texel is read at its centre.  Null: none of them.
        /// </summary>
        internal static bool[] SolidTriangles(Material m, Mesh mesh, int sub)
        {
            if (m == null || (m.HasProperty("_Surface") && m.GetFloat("_Surface") > 0.5f))
                return null;
            var tris = mesh.GetTriangles(sub);
            var solid = new bool[tris.Length / 3];
            bool clip = m.HasProperty("_AlphaClip") && m.GetFloat("_AlphaClip") > 0.5f;
            if (!clip)
            {
                for (int k = 0; k < solid.Length; k++)
                    solid[k] = true;
                return solid;
            }
            float cutoff = m.HasProperty("_Cutoff") ? m.GetFloat("_Cutoff") : 0.5f;
            float tint = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor").a : 1f;
            var map = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") as Texture2D : null;
            var alpha = map != null ? Alpha(map) : null;
            var uv = mesh.uv;
            if (alpha == null || uv.Length != mesh.vertexCount)
            {
                for (int k = 0; k < solid.Length; k++)
                    solid[k] = tint >= cutoff;
                return solid;
            }
            var (a8, w, h) = alpha.Value;
            // a texel is solid when its alpha times the colour's reaches the cutoff
            int cut = tint > 0f ? Mathf.CeilToInt(cutoff / tint * 255f - 1e-3f) : 256;
            bool At(float x, float y)
            {
                int ix = Mathf.Clamp(Mathf.FloorToInt(x), 0, w - 1), iy = Mathf.Clamp(Mathf.FloorToInt(y), 0, h - 1);
                return a8[iy * w + ix] >= cut;
            }
            for (int k = 0; k < solid.Length; k++)
            {
                Vector2 a = uv[tris[k * 3]], b = uv[tris[k * 3 + 1]], c = uv[tris[k * 3 + 2]];
                var tile = new Vector2(Mathf.Floor((a.x + b.x + c.x) / 3f), Mathf.Floor((a.y + b.y + c.y) / 3f));
                a = Vector2.Scale(a - tile, new Vector2(w, h));
                b = Vector2.Scale(b - tile, new Vector2(w, h));
                c = Vector2.Scale(c - tile, new Vector2(w, h));
                float area = (b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y);
                int all = 0, on = 0;
                if (Mathf.Abs(area) > 1e-6f)
                {
                    int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x)))), x1 = Mathf.Min(w - 1, Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))));
                    int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y)))), y1 = Mathf.Min(h - 1, Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))));
                    for (int y = y0; y <= y1; y++)
                        for (int x = x0; x <= x1; x++)
                        {
                            float px = x + 0.5f, py = y + 0.5f;
                            float w0 = ((b.x - px) * (c.y - py) - (c.x - px) * (b.y - py)) / area;
                            float w1 = ((c.x - px) * (a.y - py) - (a.x - px) * (c.y - py)) / area;
                            if (w0 < -1e-4f || w1 < -1e-4f || 1f - w0 - w1 < -1e-4f)
                                continue;
                            all++;
                            if (a8[y * w + x] >= cut)
                                on++;
                        }
                }
                solid[k] = all > 0 ? on >= SolidShare * all : At((a.x + b.x + c.x) / 3f, (a.y + b.y + c.y) / 3f);
            }
            return solid;
        }

        const float SolidShare = 0.9f;      // SolidTriangles: a cut-out triangle hides what is behind it with this share of its texels solid

        static readonly Dictionary<string, (DateTime stamp, (byte[] a, int w, int h) alpha)> alphas = new Dictionary<string, (DateTime, (byte[], int, int))>();

        /// <summary>A texture's alpha as its file has it (png or jpg; cached by path and file time), null if unreadable.</summary>
        static (byte[] a, int w, int h)? Alpha(Texture2D tex)
        {
            string path = AssetDatabase.GetAssetPath(tex);
            if (string.IsNullOrEmpty(path))
                return null;
            string full = Path.Combine(Path.GetDirectoryName(Application.dataPath), path);
            if (!File.Exists(full))
                return null;
            var stamp = File.GetLastWriteTimeUtc(full);
            if (alphas.TryGetValue(path, out var hit) && hit.stamp == stamp)
                return hit.alpha;
            var img = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try
            {
                if (!img.LoadImage(File.ReadAllBytes(full)))
                    return null;
                var px = img.GetPixels32();
                var a = new byte[px.Length];
                for (int i = 0; i < px.Length; i++)
                    a[i] = px[i].a;
                var entry = (a, img.width, img.height);
                alphas[path] = (stamp, entry);
                return entry;
            }
            finally
            {
                Object.DestroyImmediate(img);
            }
        }

        /// <summary>The name of the same bone on the other side (ownMirror): "Bip001 L Clavicle" / "Bip001 LUpArmTwist" for R,
        /// "shoulder_L", "Breast_L02", "Earring_L1_01" for _R; a name with no side comes back as it is.</summary>
        static string SideSwap(string name)
        {
            var m = Regex.Match(name, @"^(Bip001 )([LR])(?=[ A-Z])");
            if (m.Success)
                return m.Groups[1].Value + (m.Groups[2].Value == "L" ? "R" : "L") + name.Substring(m.Length);
            var k = Regex.Match(name, @"_([LR])(?=$|\d|_)");
            if (k.Success)
                return name.Substring(0, k.Index) + "_" + (k.Groups[1].Value == "L" ? "R" : "L") + name.Substring(k.Index + k.Length);
            return name;
        }

        /// <summary>
        /// "ownWeights": the base's own weights of these vertices on the suit's skeleton, bones by name - a bone the suit lacks
        /// gives its weight to its nearest ancestor the suit has; loose cloth gives none; the four largest, normalised; only the
        /// vertices with weight on a bone named like region, with that weight's share of theirs (share).  The suit's meshes skin
        /// only the twist bones of its limbs, not the limbs (b14: no Bip001 R UpperArm in any of them): a bone no suit renderer
        /// skins with gets its bind pose from the base's (bindpose, in the target renderer's space - the family's skeleton,
        /// standing where the suit's does).
        /// </summary>
        static Dictionary<int, ((Transform bone, float weight)[] weights, float share)> OwnWeights(GameObject model, GameObject prefab, SkinnedMeshRenderer nudeRenderer,
                                                                            Matrix4x4 nudeToModel, Matrix4x4 targetToModel,
                                                                            Dictionary<Transform, Matrix4x4> bindpose, IEnumerable<int> vertices,
                                                                            Regex region, out string note)
        {
            var byName = new Dictionary<string, Transform>();
            foreach (var t in model.GetComponentsInChildren<Transform>(true))
                if (!byName.ContainsKey(t.name))
                    byName[t.name] = t;
            var nbones = nudeRenderer.bones;
            var nposes = nudeRenderer.sharedMesh.bindposes;
            var baseBind = new Dictionary<Transform, Matrix4x4>();         // the base's bones, bone space to model space
            for (int k = 0; k < nbones.Length && k < nposes.Length; k++)
                if (nbones[k] != null)
                    baseBind[nbones[k]] = nudeToModel * nposes[k].inverse;
            Matrix4x4 BaseBind(Transform t) => baseBind.TryGetValue(t, out var m) ? m : prefab.transform.worldToLocalMatrix * t.localToWorldMatrix;
            var to = new Transform[nbones.Length];
            int direct = 0, byAncestor = 0, none = 0, posed = 0;
            for (int k = 0; k < nbones.Length; k++)
            {
                var t = nbones[k];
                while (t != null && !byName.ContainsKey(t.name))
                    t = t.parent;
                if (t == null)
                {
                    none++;
                    continue;
                }
                var suit = byName[t.name];
                to[k] = suit;
                if (t == nbones[k])
                    direct++;
                else
                    byAncestor++;
                if (!bindpose.ContainsKey(suit))
                {
                    bindpose[suit] = BaseBind(t).inverse * targetToModel;
                    posed++;
                }
            }
            var mesh = nudeRenderer.sharedMesh;
            var perVertex = mesh.GetBonesPerVertex();
            var all = mesh.GetAllBoneWeights();
            var start = new int[mesh.vertexCount + 1];
            for (int i = 0; i < mesh.vertexCount && i < perVertex.Length; i++)
                start[i + 1] = start[i] + perVertex[i];
            var result = new Dictionary<int, ((Transform bone, float weight)[] weights, float share)>();
            int whole = 0;
            foreach (int i in vertices)
            {
                if (i >= perVertex.Length)
                    continue;
                var sum = new Dictionary<Transform, float>();
                for (int j = start[i]; j < start[i + 1]; j++)
                {
                    var w = all[j];
                    if (w.boneIndex >= to.Length || to[w.boneIndex] == null || w.weight <= 0f || Loose(to[w.boneIndex]))
                        continue;
                    var bone = to[w.boneIndex];
                    sum[bone] = (sum.TryGetValue(bone, out float s) ? s : 0f) + w.weight;
                }
                if (sum.Count == 0)
                    continue;
                var top = sum.OrderByDescending(kv => kv.Value).Take(4).ToArray();
                float total = top.Sum(kv => kv.Value);
                float share = top.Where(kv => region.IsMatch(kv.Key.name)).Sum(kv => kv.Value) / total;
                if (share <= 0f)
                    continue;
                result[i] = (top.Select(kv => (kv.Key, kv.Value / total)).ToArray(), share);
                if (share >= OwnTo)
                    whole++;
            }
            note = $"{result.Count} of the body's vertices on {region}, {whole} by {OwnTo:P0} or more; the base's bones: {direct} by name, {byAncestor} by an ancestor, {none} on nothing of the suit's; " +
                   $"{posed} the suit skins nothing with, bound where the base has them";
            return result;
        }

        /// <summary>
        /// Per vertex of the base, the matrix (model space to model space) that carries it from the base's bind pose to the
        /// suit's: its own weights over each bone's suit bind frame times the inverse of the base's.  Bones by name; one the suit
        /// lacks follows its nearest ancestor the suit has.
        /// </summary>
        static Dictionary<int, Matrix4x4> PlaceByBones(GameObject model, SkinnedMeshRenderer[] renderers, GameObject prefab, SkinnedMeshRenderer nudeRenderer,
                                                       Matrix4x4 nudeToModel, List<int> used, out string note)
        {
            var root = model.transform;
            var suitBind = new Dictionary<string, Matrix4x4>();         // the suit's bind frames in model space, by bone name
            foreach (var r in renderers)
            {
                var poses = r.sharedMesh.bindposes;
                var toModel = root.worldToLocalMatrix * r.transform.localToWorldMatrix;
                for (int k = 0; k < r.bones.Length && k < poses.Length; k++)
                    if (r.bones[k] != null && !suitBind.ContainsKey(r.bones[k].name))
                        suitBind[r.bones[k].name] = toModel * poses[k].inverse;
            }
            var mesh = nudeRenderer.sharedMesh;
            var nposes = mesh.bindposes;
            var nbones = nudeRenderer.bones;
            var baseBind = new Dictionary<Transform, Matrix4x4>();
            for (int k = 0; k < nbones.Length && k < nposes.Length; k++)
                if (nbones[k] != null)
                    baseBind[nbones[k]] = nudeToModel * nposes[k].inverse;
            Matrix4x4 BaseBind(Transform t) => baseBind.TryGetValue(t, out var m) ? m : prefab.transform.worldToLocalMatrix * t.localToWorldMatrix;
            var carry = new Matrix4x4[nbones.Length];
            int direct = 0, byAncestor = 0, none = 0;
            float worst = 0f, sum = 0f;
            string worstName = "";
            for (int k = 0; k < nbones.Length; k++)
            {
                var t = nbones[k];
                while (t != null && !suitBind.ContainsKey(t.name))
                    t = t.parent;
                if (t == null)
                {
                    carry[k] = Matrix4x4.identity;
                    none++;
                    continue;
                }
                if (t == nbones[k])
                    direct++;
                else
                    byAncestor++;
                carry[k] = suitBind[t.name] * BaseBind(t).inverse;
                var head = (Vector3)BaseBind(nbones[k]).GetColumn(3);
                float d = (carry[k].MultiplyPoint3x4(head) - head).magnitude;
                sum += d;
                if (d > worst)
                {
                    worst = d;
                    worstName = nbones[k].name;
                }
            }
            var perVertex = mesh.GetBonesPerVertex();
            var all = mesh.GetAllBoneWeights();
            var start = new int[mesh.vertexCount + 1];
            for (int i = 0; i < mesh.vertexCount && i < perVertex.Length; i++)
                start[i + 1] = start[i] + perVertex[i];
            var place = new Dictionary<int, Matrix4x4>();
            foreach (int i in used)
            {
                var m = new Matrix4x4();
                float total = 0f;
                if (i < perVertex.Length)
                    for (int j = start[i]; j < start[i + 1]; j++)
                    {
                        var w = all[j];
                        if (w.boneIndex >= carry.Length || w.weight <= 0f)
                            continue;
                        for (int e = 0; e < 16; e++)
                            m[e] += carry[w.boneIndex][e] * w.weight;
                        total += w.weight;
                    }
                if (total <= 0f)
                    place[i] = Matrix4x4.identity;
                else
                {
                    for (int e = 0; e < 16; e++)
                        m[e] /= total;
                    place[i] = m;
                }
            }
            note = $"{direct} bones by name, {byAncestor} by an ancestor, {none} not at all; bones moved {100f * sum / Mathf.Max(1, direct + byAncestor):F2} cm " +
                   $"on average, at most {100f * worst:F2} cm ({worstName})";
            return place;
        }

        /// <summary>Reveal: the outfit's opaque triangles that lie on the body, found by their centres (RoeBurstBuilder: what lies
        /// over a piece of the outfit).</summary>
        internal class Cover
        {
            readonly List<(Vector3 a, Vector3 b, Vector3 c, Vector3 n, int stage)> tris;
            readonly Grid grid;
            readonly float corner;                       // the furthest a triangle's corner is from its centre (at most 10 cm)
            readonly List<(int i, float d)> near = new List<(int i, float d)>();

            public Cover(List<(Vector3 a, Vector3 b, Vector3 c, Vector3 n, int stage)> tris)
            {
                this.tris = tris;
                var centres = new List<Vector3>(tris.Count);
                foreach (var t in tris)
                {
                    var c = (t.a + t.b + t.c) / 3f;
                    centres.Add(c);
                    corner = Mathf.Max(corner, Mathf.Max((t.a - c).magnitude, Mathf.Max((t.b - c).magnitude, (t.c - c).magnitude)));
                }
                corner = Mathf.Min(corner, 0.1f);
                grid = new Grid(centres, Enumerable.Range(0, centres.Count));
            }

            /// <summary>The nearest point of the triangles to p within reach, and that triangle's normal; false when none is.</summary>
            public bool Nearest(Vector3 p, float reach, out Vector3 point, out Vector3 normal)
            {
                point = p;
                normal = Vector3.up;
                if (tris.Count == 0)
                    return false;
                grid.Near(p, reach + corner, near);
                float best = reach;
                bool found = false;
                foreach (var (k, _) in near)
                {
                    var t = tris[k];
                    var q = ClosestOnTriangle(p, t.a, t.b, t.c);
                    float d = (q - p).magnitude;
                    if (d > best)
                        continue;
                    best = d;
                    point = q;
                    normal = t.n;
                    found = true;
                }
                return found;
            }

            /// <summary>
            /// The last stage of the triangles over p (int.MaxValue: a piece that stays on), 0 when none is: a triangle within
            /// aside of right over p (CoverAside for the body; the suit's skin only right under one, or the skin between the
            /// straps of eve37's shoes counted as hidden and her ankles went missing), at most reach along its normal either
            /// way, facing the way the body does there (facing zero: any way).
            /// </summary>
            public int Stage(Vector3 p, Vector3 facing, float reach, float aside = CoverAside)
            {
                if (tris.Count == 0)
                    return 0;
                grid.Near(p, reach + corner, near);
                int stage = 0;
                foreach (var (k, _) in near)
                {
                    var t = tris[k];
                    if (t.stage <= stage || (facing != Vector3.zero && Vector3.Dot(facing, t.n) < CoverFacing))
                        continue;
                    var q = ClosestOnTriangle(p, t.a, t.b, t.c);
                    var v = p - q;
                    float along = Vector3.Dot(v, t.n);
                    if (Mathf.Abs(along) > reach || (v - along * t.n).magnitude > aside)
                        continue;
                    stage = t.stage;
                }
                return stage;
            }
        }

        /// <summary>The point of triangle abc nearest to p (Ericson, Real-Time Collision Detection 5.1.5).</summary>
        static Vector3 ClosestOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f)
                return a;
            var bp = p - b;
            float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3)
                return b;
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f)
                return a + ab * (d1 / (d1 - d3));
            var cp = p - c;
            float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6)
                return c;
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f)
                return a + ac * (d2 / (d2 - d6));
            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f)
                return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
            float denom = 1f / (va + vb + vc);
            return a + ab * (vb * denom) + ac * (vc * denom);
        }
    }
}
