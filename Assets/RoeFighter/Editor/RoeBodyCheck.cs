using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using RoeFighter.Fight;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RoeFighter.EditorTools
{
    /// <summary>
    /// Body checks of every fighter (user 10-05: "爆衣，乳摇，都身体权重，inase的头发也衣服，这些都检查一下"), in her battle
    /// stance in the fight scene:
    ///   burst    the outfit pieces that come off (RoeClothesBurst): stages, pieces, the nude body under them;
    ///   breasts  the bones that carry the breasts (by name for ROE, Vindictus and Stellar Blade; a DOA6 character's soft
    ///            bodies' nodes) and the solver that moves them, in the editor's batch runs and in the game;
    ///   weights  each breast turned 20 degrees down about its root, the way a bounce goes, every mesh baked before and after:
    ///            what moves with it.  A vertex is the breast's when half its weight or more is on the breast's bones; one
    ///            that moves while most of its weight is on an arm, the neck, the head or a leg, or that lies more than
    ///            15 cm from the breast as modelled (bind pose: the arms away from the chest), is a leak.  Together: the
    ///            skin under the chest's clothes (the clothes lying on it as modelled - within 12 mm in front, 6 mm aside - and
    ///            worn on the chest) against them - moved apart by more than 5 mm, and skin that comes out through the cloth;
    ///            then each lower bone of a breast's chain moved alone, 3 cm down (a spring solver moves the chain's end);
    ///   skin     how the skin and the nude body deform in the stance: triangles squeezed under 30 % of their area as
    ///            modelled (a pinch: the armpit of a raised arm, the inside of a bent elbow), stretched over 200 %, or turned
    ///            over (folded through themselves), by the body part nearest them as modelled; a close-up of the worst
    ///            pinch (&lt;id&gt;_pinch.jpg).
    /// Writes to -roeOut (default _work/body_check): report.txt; per fighter &lt;id&gt;_chest.tsv (every vertex round the
    /// chest as modelled, front view, with how far it moved with both breasts turned - tools/body_check_sheet.py) and chest
    /// pictures &lt;id&gt;_{dressed,nude}_{rest,turned}.jpg (nude: every outfit piece that can come off taken off; the meshes
    /// drawn as baked, so the pictures show exactly what was measured).
    ///   -executeMethod RoeFighter.EditorTools.RoeBodyCheck.Run [-roeChars a08,g04] [-roeOut dir] [-roeTurn 20] [-roeSize 720x720]
    /// </summary>
    public static class RoeBodyCheck
    {
        static string ProjectDir => Path.GetDirectoryName(Application.dataPath);
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // the breasts' root bones: ROE (3ds Max biped helpers), Vindictus (the simulated pair of the breast rig, 02 and 03),
        // Stellar Blade (the spring bones); a DOA6 character's breasts are the nodes of her soft bodies (RoeDoaRig)
        static readonly Regex BreastRoot = new Regex(@"^Bip001 (Breast|chest)_[LR]$|^breast_physics_0[23]_[lr]$|^Ab-[LR]-Breast$",
                                                     RegexOptions.IgnoreCase);
        static readonly Regex HairName = new Regex(@"hair|ponytail|bangs", RegexOptions.IgnoreCase);
        static readonly Regex HeadName = new Regex(@"head|face|eye|lash|brow|teeth|tongue|mouth|tear|iris|cornea", RegexOptions.IgnoreCase);
        static readonly Regex SkinName = new Regex(@"skin|_nk_body|bare|nude|naked|^MI_PC[FM]_(Upper|Lower|Hand|Foot|Body)\d*$", RegexOptions.IgnoreCase);

        const float Moved = 0.001f;         // a vertex that moved this far moved (m)
        const float Reach = 0.15f;          // further than this from the breast as modelled it is not the breast (m)
        const float Over = 0.025f;          // the grid's cell for the cloth round the chest (m)
        const float OnSkin = 0.012f;        // cloth this close in front of the skin (along its normal) is worn on it (m) - a cup's rim
                                            // standing 2 cm off the chest under the breast is not, and rightly moves with the breast
        const float Aside = 0.006f;         // ... and at most this far to the side of the skin's normal (m)
        const float Apart = 0.005f;         // skin and the cloth on it moved apart this far: they do not go together (m)
        const float Around = 0.35f;         // the vertices written to the table: this close to the middle of the chest (m)

        internal class Breast
        {
            public string side, name;                                   // side: L or R, her own
            public List<Transform> moved = new List<Transform>();       // the top-most of the set, turned about the pivot
            public Vector3 pivot;
            public Vector3 centre;                                      // as modelled (bind frame): the middle of its vertices
        }

        class Layer
        {
            public SkinnedMeshRenderer smr;
            public string[] role, what;     // per vertex: nude, skin, cloth, piece, hair, head; the piece or the material
            public string[] cls;            // per vertex: the strongest bone that is not a breast's, as a body part
            public float[][] breastWeight;  // per breast, per vertex: its weight on that breast's bones
            public Vector3[] bind, bindNormal;  // as modelled, in the bind frame (metres: x her right, y up, z her front)
            public Vector3[] rest, normal, now;
        }

        [MenuItem("ROE Fighter/Checks/Body check (burst, breasts, weights)")]
        public static void Run()
        {
            string stage = RoeCapture.Arg("-roeStage", "e23_steel_s02");
            string outDir = RoeCapture.Arg("-roeOut", Path.Combine(ProjectDir, "_work", "body_check"));
            float turn = float.Parse(RoeCapture.Arg("-roeTurn", "20"), Inv);
            var size = RoeCapture.Arg("-roeSize", "720x720").Split('x');
            int width = int.Parse(size[0]), height = int.Parse(size[1]);
            Directory.CreateDirectory(outDir);
            ShaderUtil.allowAsyncCompilation = false;
            EditorSceneManager.OpenScene(RoeFightScene.ScenePath(stage), OpenSceneMode.Single);
            var game = Object.FindFirstObjectByType<FightGame>();
            var ids = RoeCapture.Arg("-roeChars", string.Join(",", game.roster.Select(r => r.id))).Split(',').Select(x => x.Trim()).ToList();
            var report = new StringBuilder($"body check, {DateTime.Now:yyyy-MM-dd HH:mm}, breasts turned {turn:F0} deg down about their roots\n");
            if (game.hud != null)
                game.hud.gameObject.SetActive(false);
            foreach (var id in ids)
            {
                try
                {
                    report.Append(Check(game, id, turn, outDir, width, height));
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    report.Append($"\n== {id}: FAILED {e.GetType().Name}: {e.Message}\n");
                }
            }
            File.WriteAllText(Path.Combine(outDir, "report.txt"), report.ToString());
            Debug.Log("[ROE] body check:\n" + report);
        }

        static string Check(FightGame game, string id, float turn, string outDir, int width, int height)
        {
            var sb = new StringBuilder();
            game.PickIds(game.roster.First(r => r.id != id).id, id);
            game.cpu = new[] { false, false };
            game.Setup();
            foreach (var r in game.rigs)
                foreach (var smr in r.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    smr.forceMatrixRecalculationPerRender = true;
            for (int guard = 0; game.phase != FightGame.Phase.Fight && guard < 3600; guard++)
                game.Step(new FighterInput[2]);
            var me = game.f[0].rig.id == id ? game.f[0] : game.f[1];
            me.foe.pos = me.pos + (me.foe.pos - me.pos).normalized * 6f;
            me.foe.Place();
            for (int k = 0; k < 45; k++)
                game.Step(new FighterInput[2]);
            var rig = me.rig;
            var animator = rig.animator;
            sb.Append($"\n== {id} {rig.displayName}\n");

            // ---- the burst
            var burst = rig.burst;
            var nude = rig.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r => r.sharedMesh != null && r.sharedMesh.name.StartsWith("nude_")).ToList();
            if (burst == null)
                sb.Append("  burst: NONE (no Editor/Burst rules: the outfit cannot come off)\n");
            else
            {
                burst.Restore(1);
                sb.Append($"  burst: {burst.stages} stages, {burst.pieces.Count} pieces; nude body under them: " +
                          (nude.Count > 0 ? string.Join(", ", nude.Select(r => $"{r.name} ({r.sharedMesh.vertexCount} verts)")) : "NONE") + "\n");
                foreach (var g in burst.pieces.GroupBy(p => p.stage).OrderBy(g => g.Key))
                    sb.Append($"    stage {g.Key}: {string.Join(", ", g.Select(p => p.title).Distinct())}\n");
            }

            // ---- the breasts and what moves them
            var breasts = Breasts(animator, out string how);
            sb.Append($"  breasts: {how}\n");
            string editorSetup = RoeClothBackends.Resolve("auto", animator, id);
            string gameSetup = RoeClothBackends.Preferred.TryGetValue(id, out var own) && RoeClothBackends.All.Any(b => b.name == own) ? own : editorSetup;
            foreach (var (where, setup) in new[] { ("editor batch", editorSetup), ("game (exe)", gameSetup) })
            {
                var backend = RoeClothBackends.Find(setup);
                sb.Append($"  breast physics, {where}: setup {setup} -> solver {backend.SolverFor("breast") ?? "none"}" +
                          $"{(backend.overGameClips || rig.clothOverGameClips ? "; also over the game's own clips" : "; the game's own clips: their keys")}\n");
            }
            sb.Append($"  cloth now ({rig.ClothTitle}): {rig.ClothReport}\n");
            var layers = Layers(rig, burst, breasts, out var frame);
            sb.Append("  meshes:\n" + string.Join("", layers.Select(l => $"    {l.smr.name}: " + Materials(l) + "\n")));
            if (breasts.Count == 0)
            {
                sb.Append("  weights: no breasts found - nothing turned\n");
                return sb.ToString();
            }

            // ---- weights: each breast turned down alone; then both for the table and the pictures
            var right = (Bone(animator, HumanBodyBones.RightUpperArm) - Bone(animator, HumanBodyBones.LeftUpperArm)).normalized;
            var up = (Bone(animator, HumanBodyBones.Neck) - Bone(animator, HumanBodyBones.Spine)).normalized;
            var forward = Vector3.Cross(right, up).normalized;
            up = Vector3.Cross(forward, right).normalized;
            Bake(layers, true);
            for (int b = 0; b < breasts.Count; b++)
            {
                var saved = Turn(new[] { breasts[b] }, right, turn);
                Bake(layers, false);
                Undo(saved);
                sb.Append(Measure(layers, breasts, b, $"{breasts[b].side} breast ({breasts[b].name}, {breasts[b].moved.Count} transforms turned)"));
                sb.Append(BoneTable(layers, breasts[b]));
                // each bone further down the chain moved alone, 3 cm down - the way a spring solver moves the chain's end
                // (Magica's BoneSpring, our bone cloth): skin and cloth on it must go along together
                foreach (var bone in ChainBelow(layers, breasts[b]))
                {
                    var keep = (bone.position, bone.rotation);
                    bone.position -= up * 0.03f;
                    Bake(layers, false);
                    bone.SetPositionAndRotation(keep.position, keep.rotation);
                    sb.Append(Measure(layers, breasts, b, $"{breasts[b].side} breast: {bone.name} alone moved 3 cm down"));
                }
            }
            var both = Turn(breasts, right, turn);
            Bake(layers, false);
            Undo(both);
            var middle = breasts.Aggregate(Vector3.zero, (s, x) => s + x.centre) / breasts.Count;
            var tsv = new StringBuilder("renderer\trole\twhat\tx\ty\tz\tmoved_cm\tbreast_w\tpart\n");
            foreach (var l in layers)
                for (int i = 0; i < l.bind.Length; i++)
                {
                    if (l.role[i] == null || (l.bind[i] - middle).sqrMagnitude > Around * Around)
                        continue;
                    var d = l.bind[i] - middle;
                    float bw = l.breastWeight.Sum(w => w[i]);
                    tsv.Append($"{l.smr.name}\t{l.role[i]}\t{l.what[i]}\t{d.x.ToString("F4", Inv)}\t{d.y.ToString("F4", Inv)}\t{d.z.ToString("F4", Inv)}\t" +
                               $"{((l.now[i] - l.rest[i]).magnitude * 100f).ToString("F2", Inv)}\t{bw.ToString("F2", Inv)}\t{l.cls[i]}\n");
                }
            File.WriteAllText(Path.Combine(outDir, $"{id}_chest.tsv"), tsv.ToString());

            // ---- pictures: the chest from in front and a little to her right, dressed and (burst) with everything off
            var cam = game.cam;
            float fov = cam.fieldOfView;
            cam.fieldOfView = 30f;
            var mid = breasts.Aggregate(Vector3.zero, (s, x) => s + x.pivot) / breasts.Count;
            var look = mid + forward * 0.06f;
            cam.transform.position = look + (forward * 0.85f + right * 0.45f + up * 0.15f).normalized * 1.25f;
            cam.transform.LookAt(look, Vector3.up);
            void Shoot(string name)
            {
                Render(rig, cam, width, height, Path.Combine(outDir, $"{id}_{name}_rest.jpg"));
                var s = Turn(breasts, right, turn);
                Render(rig, cam, width, height, Path.Combine(outDir, $"{id}_{name}_turned.jpg"));
                Undo(s);
            }
            RoeCapture.Render(cam, 240, 240, Path.Combine(outDir, "_warm.jpg"), 80);
            Shoot("dressed");
            if (burst != null)
            {
                burst.Show(burst.stages);
                Shoot("nude");
                burst.Show(0);
            }
            sb.Append(Deformation(layers, rig, cam, Path.Combine(outDir, $"{id}_pinch.jpg"), width, height));
            cam.fieldOfView = fov;
            return sb.ToString();
        }

        /// <summary>
        /// The skin in the stance against the skin as modelled, triangle by triangle (areas do not care how the body is turned):
        /// squeezed under 30 % of its area, stretched over 200 %, or turned over (its face against the normals of its own
        /// corners).  Per body part (the human bone nearest it as modelled).  The worst squeezed spot is filmed from outside.
        /// </summary>
        static string Deformation(List<Layer> layers, FighterRig rig, Camera cam, string picture, int width, int height)
        {
            var parts = new Dictionary<string, (int total, int pinched, int stretched, int turned, float least)>();
            var worst = new List<(float ratio, Vector3 at, Vector3 normal)>();
            int all = 0;
            foreach (var l in layers)
            {
                var mesh = l.smr.sharedMesh;
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    var t = mesh.GetIndices(sub);
                    for (int k = 0; k + 2 < t.Length; k += 3)
                    {
                        int a = t[k], b = t[k + 1], c = t[k + 2];
                        string role = l.role[a];
                        if (role != "skin" && role != "nude")
                            continue;
                        float bindArea = Vector3.Cross(l.bind[b] - l.bind[a], l.bind[c] - l.bind[a]).magnitude;
                        if (bindArea < 1e-7f)
                            continue;
                        var face = Vector3.Cross(l.rest[b] - l.rest[a], l.rest[c] - l.rest[a]);
                        float ratio = face.magnitude / bindArea;
                        bool turned = Vector3.Dot(face, l.normal[a] + l.normal[b] + l.normal[c]) < 0f;
                        var mid = (l.bind[a] + l.bind[b] + l.bind[c]) / 3f;
                        string part = humanBind.Count > 0 ? humanBind.OrderBy(h => (h.at - mid).sqrMagnitude).First().name : "?";
                        parts.TryGetValue(part, out var e);
                        e.total++;
                        if (e.total == 1)
                            e.least = float.MaxValue;
                        if (ratio < 0.3f)
                            e.pinched++;
                        if (ratio > 2f)
                            e.stretched++;
                        if (turned)
                            e.turned++;
                        e.least = Mathf.Min(e.least, ratio);
                        parts[part] = e;
                        all++;
                        if (ratio < 0.3f || turned)
                            worst.Add((turned ? 0f : ratio, (l.rest[a] + l.rest[b] + l.rest[c]) / 3f, (l.normal[a] + l.normal[b] + l.normal[c]).normalized));
                    }
                }
            }
            var sb = new StringBuilder($"  skin in the stance ({all} triangles of skin and nude body): ");
            var bad = parts.Where(kv => kv.Value.pinched + kv.Value.turned + kv.Value.stretched > 0)
                .OrderByDescending(kv => kv.Value.pinched + kv.Value.turned).ToList();
            sb.Append(bad.Count == 0 ? "nothing squeezed, stretched or turned over\n" :
                string.Join(", ", bad.Take(8).Select(kv => $"{kv.Key}: {kv.Value.pinched} squeezed, {kv.Value.turned} turned over, {kv.Value.stretched} stretched " +
                                                            $"(of {kv.Value.total}; smallest {kv.Value.least * 100f:F0} %)")) + "\n");
            if (worst.Count == 0)
                return sb.ToString();
            // the worst spot: where most squeezed and turned triangles crowd within 6 cm of one, seen from outside along
            // their corners' normals
            var w0 = worst[0];
            int most = -1;
            foreach (var x in worst.Count > 4000 ? worst.Where((_, i) => i % (worst.Count / 2000) == 0) : worst)
            {
                int count = worst.Count(y => (y.at - x.at).sqrMagnitude < 0.06f * 0.06f);
                if (count > most)
                {
                    most = count;
                    w0 = x;
                }
            }
            var near = worst.Where(x => (x.at - w0.at).sqrMagnitude < 0.06f * 0.06f).ToList();
            var at = near.Aggregate(Vector3.zero, (acc, x) => acc + x.at) / near.Count;
            var n = near.Aggregate(Vector3.zero, (acc, x) => acc + x.normal);
            n = n.sqrMagnitude > 1e-6f ? n.normalized : -cam.transform.forward;
            cam.fieldOfView = 30f;
            cam.transform.position = at + (n + Vector3.up * 0.2f).normalized * 0.55f;
            cam.transform.LookAt(at, Vector3.up);
            Render(rig, cam, width, height, picture);
            var where = humanBind.Count > 0 && rig.animator != null
                ? Enum.GetValues(typeof(HumanBodyBones)).Cast<HumanBodyBones>().Where(h => h != HumanBodyBones.LastBone && rig.animator.GetBoneTransform(h) != null)
                    .OrderBy(h => (rig.animator.GetBoneTransform(h).position - at).sqrMagnitude).First().ToString() : "?";
            sb.Append($"    the worst spot: {near.Count} squeezed or turned triangles within 6 cm, nearest {where} in the stance - {Path.GetFileName(picture)}\n");
            return sb.ToString();
        }

        static Vector3 Bone(Animator animator, HumanBodyBones b)
        {
            var t = animator.GetBoneTransform(b);
            if (t == null && b == HumanBodyBones.Neck)
                t = animator.GetBoneTransform(HumanBodyBones.Head);
            return t != null ? t.position : animator.transform.position;
        }

        /// <summary>The breasts: per side the bones (or soft-body nodes) that carry it and the point they turn about.</summary>
        internal static List<Breast> Breasts(Animator animator, out string how)
        {
            var result = new List<Breast>();
            // her own left and right: across the shoulders, whatever way the stance turns her
            var la = Bone(animator, HumanBodyBones.LeftUpperArm);
            var ra = Bone(animator, HumanBodyBones.RightUpperArm);
            string Side(Vector3 p) => Vector3.Dot(p - (la + ra) * 0.5f, ra - la) > 0f ? "R" : "L";
            var doa = animator.GetComponent<RoeDoaRig>();
            var softs = doa != null && doa.softs != null ? doa.softs.Where(s => s.kind == "breast" && s.nodes != null).ToList() : new List<RoeDoaRig.Soft>();
            if (softs.Count > 0)
            {
                foreach (var s in softs)
                {
                    var pinned = new List<Vector3>();
                    for (int i = 0; i < s.nodes.Length; i++)
                        if (s.nodes[i] != null && s.flags != null && i < s.flags.Length && (s.flags[i] & 1) != 0)
                            pinned.Add(s.nodes[i].position);
                    var nodes = s.nodes.Where(n => n != null).ToList();
                    var pivot = pinned.Count > 0 ? pinned.Aggregate(Vector3.zero, (a, p) => a + p) / pinned.Count : s.parent.position;
                    result.Add(new Breast { name = s.name, pivot = pivot, moved = TopMost(nodes) });
                }
                how = $"DOA6 soft bodies {string.Join(", ", softs.Select(s => $"{s.name} ({s.nodes.Length} nodes)"))}";
            }
            else
            {
                var found = animator.GetComponentsInChildren<Transform>(true).Where(t => BreastRoot.IsMatch(t.name)).ToList();
                how = found.Count > 0 ? "bones " + string.Join(", ", found.Select(t => t.name)) : "NO breast bones found";
                foreach (var g in found.GroupBy(t => Side(t.position)))
                {
                    var top = TopMost(g.ToList());
                    var first = top.OrderBy(Depth).First();
                    result.Add(new Breast { name = string.Join("+", top.Select(t => t.name)), pivot = first.position, moved = top });
                }
            }
            foreach (var b in result)
                b.side = Side(b.pivot);
            return result.OrderBy(b => b.side).ToList();
        }

        static int Depth(Transform t)
        {
            int d = 0;
            for (var x = t; x != null; x = x.parent)
                d++;
            return d;
        }

        static List<Transform> TopMost(List<Transform> set) => set.Where(t => !set.Any(o => o != t && t.IsChildOf(o))).ToList();

        /// <summary>A bone as a part of the body: the human bone it is or hangs under.</summary>
        static string PartOf(Transform bone, Dictionary<Transform, HumanBodyBones> human)
        {
            for (var t = bone; t != null; t = t.parent)
                if (human.TryGetValue(t, out var h))
                {
                    string n = h.ToString();
                    if (n.Contains("Shoulder"))
                        return "shoulder";
                    if (n.Contains("Arm") || n.Contains("Hand") || n.Contains("Thumb") || n.Contains("Index") || n.Contains("Middle") ||
                        n.Contains("Ring") || n.Contains("Little"))
                        return "arm";
                    if (n.Contains("Leg") || n.Contains("Foot") || n.Contains("Toes"))
                        return "leg";
                    if (n == "Neck" || n == "Head" || n == "Jaw" || n.Contains("Eye"))
                        return "neck/head";
                    return "torso";
                }
            return "outside";
        }

        /// <summary>The human bones as modelled (bind frame), filled by Layers.</summary>
        static readonly List<(string name, Vector3 at)> humanBind = new List<(string, Vector3)>();

        static List<Layer> Layers(FighterRig rig, RoeClothesBurst burst, List<Breast> breasts, out Matrix4x4 frame)
        {
            var animator = rig.animator;
            var human = new Dictionary<Transform, HumanBodyBones>();
            foreach (HumanBodyBones h in Enum.GetValues(typeof(HumanBodyBones)))
                if (h != HumanBodyBones.LastBone && animator.GetBoneTransform(h) != null)
                    human[animator.GetBoneTransform(h)] = h;
            var pieces = new Dictionary<SkinnedMeshRenderer, RoeClothesBurst.Piece>();
            if (burst != null)
                foreach (var p in burst.pieces)
                    if (p.renderer != null)
                        pieces[p.renderer] = p;
            var smrs = rig.GetComponentsInChildren<SkinnedMeshRenderer>(false).Where(s => s.enabled && s.sharedMesh != null).ToList();

            // the bind frame: the model as modelled (each mesh's own vertices, placed by its renderer), her right across the
            // upper arms' bind places, y up, z her front
            Vector3? BindOf(HumanBodyBones h)
            {
                var t = animator.GetBoneTransform(h);
                foreach (var s in smrs)
                {
                    int k = Array.IndexOf(s.bones, t);
                    if (k >= 0 && k < s.sharedMesh.bindposes.Length)
                        return (Vector3)(s.transform.localToWorldMatrix * s.sharedMesh.bindposes[k].inverse).GetColumn(3);
                }
                return null;
            }
            var la = BindOf(HumanBodyBones.LeftUpperArm) ?? Bone(animator, HumanBodyBones.LeftUpperArm);
            var ra = BindOf(HumanBodyBones.RightUpperArm) ?? Bone(animator, HumanBodyBones.RightUpperArm);
            var hips = BindOf(HumanBodyBones.Hips) ?? Bone(animator, HumanBodyBones.Hips);
            var neck = BindOf(HumanBodyBones.Neck) ?? BindOf(HumanBodyBones.Head) ?? Bone(animator, HumanBodyBones.Neck);
            var bx = (ra - la).normalized;
            var by = (neck - hips).normalized;
            var bz = Vector3.Cross(bx, by).normalized;
            by = Vector3.Cross(bz, bx).normalized;
            var origin = (la + ra) * 0.5f;
            frame = Matrix4x4.identity;
            frame.SetRow(0, new Vector4(bx.x, bx.y, bx.z, -Vector3.Dot(bx, origin)));
            frame.SetRow(1, new Vector4(by.x, by.y, by.z, -Vector3.Dot(by, origin)));
            frame.SetRow(2, new Vector4(bz.x, bz.y, bz.z, -Vector3.Dot(bz, origin)));
            humanBind.Clear();
            foreach (HumanBodyBones h in Enum.GetValues(typeof(HumanBodyBones)))
                if (h != HumanBodyBones.LastBone && animator.GetBoneTransform(h) != null && BindOf(h) is Vector3 at)
                    humanBind.Add((h.ToString(), frame.MultiplyPoint3x4(at)));

            // each breast's bones: the moved transforms and everything under them
            var breastBones = breasts.Select(b => new HashSet<Transform>(b.moved.SelectMany(t => t.GetComponentsInChildren<Transform>(true)))).ToList();
            var list = new List<Layer>();
            foreach (var smr in smrs)
            {
                var mesh = smr.sharedMesh;
                int n = mesh.vertexCount;
                var layer = new Layer
                {
                    smr = smr, role = new string[n], what = new string[n], cls = new string[n],
                    breastWeight = breasts.Select(_ => new float[n]).ToArray(), bind = new Vector3[n], bindNormal = new Vector3[n],
                };
                var mats = smr.sharedMaterials;
                pieces.TryGetValue(smr, out var piece);
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    var mat = s < mats.Length ? mats[s] : null;
                    string role = Role(smr, mesh, mat, piece);
                    string what = piece != null ? piece.name : mat != null ? mat.name : "?";
                    foreach (int i in mesh.GetIndices(s))
                        if (layer.role[i] == null)
                        {
                            layer.role[i] = role;
                            layer.what[i] = what;
                        }
                }
                var m = frame * smr.transform.localToWorldMatrix;
                var v = mesh.vertices;
                var nm = mesh.normals;
                for (int i = 0; i < n; i++)
                {
                    layer.bind[i] = m.MultiplyPoint3x4(v[i]);
                    if (i < nm.Length)
                        layer.bindNormal[i] = m.MultiplyVector(nm[i]).normalized;
                }
                var counts = mesh.GetBonesPerVertex();
                var weights = mesh.GetAllBoneWeights();
                var bones = smr.bones;
                int at = 0;
                for (int i = 0; i < n && i < counts.Length; i++)
                {
                    Transform strongest = null;
                    float best = -1f;
                    for (int k = 0; k < counts[i]; k++)
                    {
                        var w = weights[at + k];
                        if (w.boneIndex < 0 || w.boneIndex >= bones.Length || bones[w.boneIndex] == null)
                            continue;
                        var bone = bones[w.boneIndex];
                        bool breast = false;
                        for (int b = 0; b < breasts.Count; b++)
                            if (breastBones[b].Contains(bone))
                            {
                                layer.breastWeight[b][i] += w.weight;
                                breast = true;
                            }
                        if (!breast && w.weight > best)
                        {
                            best = w.weight;
                            strongest = bone;
                        }
                    }
                    at += counts[i];
                    layer.cls[i] = strongest != null ? PartOf(strongest, human) : "breast only";
                }
                list.Add(layer);
            }
            // each breast as modelled: the middle of the vertices half on its bones or more
            for (int b = 0; b < breasts.Count; b++)
            {
                var sum = Vector3.zero;
                int count = 0;
                foreach (var l in list)
                    for (int i = 0; i < l.bind.Length; i++)
                        if (l.breastWeight[b][i] >= 0.5f && l.role[i] != null)
                        {
                            sum += l.bind[i];
                            count++;
                        }
                breasts[b].centre = count > 0 ? sum / count : frame.MultiplyPoint3x4(breasts[b].pivot);
            }
            return list;
        }

        static string Role(SkinnedMeshRenderer smr, Mesh mesh, Material mat, RoeClothesBurst.Piece piece)
        {
            if (mesh.name.StartsWith("nude_"))
                return "nude";
            if (piece != null)
                return "piece";
            string tex = MainTexture(mat);
            string m = mat != null ? mat.name + " " + (mat.shader != null ? mat.shader.name : "") + " " + tex : "";
            if (HairName.IsMatch(m))
                return "hair";
            if (HeadName.IsMatch(m))
                return "head";
            if (SkinName.IsMatch(m))
                return "skin";
            if (HairName.IsMatch(smr.name))
                return "hair";
            if (HeadName.IsMatch(smr.name))
                return "head";
            return "cloth";
        }

        static string MainTexture(Material mat)
        {
            if (mat == null)
                return "";
            Texture t = null;
            foreach (var p in new[] { "_BaseMap", "_MainTex", "_BaseColorMap", "_Albedo" })
                if (mat.HasProperty(p) && (t = mat.GetTexture(p)) != null)
                    break;
            return t != null ? t.name : "";
        }

        static string Materials(Layer l)
        {
            var mesh = l.smr.sharedMesh;
            var mats = l.smr.sharedMaterials;
            var parts = new List<string>();
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                var mat = s < mats.Length ? mats[s] : null;
                int verts = mesh.GetSubMesh(s).vertexCount;
                string role = null;
                foreach (int i in mesh.GetIndices(s))
                {
                    role = l.role[i];
                    break;
                }
                parts.Add($"[{s}] {role ?? "-"} {(mat != null ? mat.name : "none")} ({MainTexture(mat)}, {mat?.shader?.name}) {verts}v");
            }
            return string.Join("; ", parts);
        }

        static void Bake(List<Layer> layers, bool rest)
        {
            var mesh = new Mesh();
            foreach (var l in layers)
            {
                l.smr.BakeMesh(mesh, true);
                var m = l.smr.transform.localToWorldMatrix;
                var v = mesh.vertices;
                var n = mesh.normals;
                var pos = new Vector3[v.Length];
                for (int i = 0; i < v.Length; i++)
                    pos[i] = m.MultiplyPoint3x4(v[i]);
                if (rest)
                {
                    l.rest = pos;
                    l.normal = new Vector3[v.Length];
                    for (int i = 0; i < v.Length && i < n.Length; i++)
                        l.normal[i] = m.MultiplyVector(n[i]).normalized;
                }
                else
                    l.now = pos;
            }
            Object.DestroyImmediate(mesh);
        }

        static List<(Transform t, Vector3 p, Quaternion r)> Turn(IEnumerable<Breast> breasts, Vector3 right, float degrees)
        {
            var saved = new List<(Transform, Vector3, Quaternion)>();
            var q = Quaternion.AngleAxis(degrees, right);
            foreach (var b in breasts)
                foreach (var t in b.moved)
                {
                    saved.Add((t, t.position, t.rotation));
                    t.SetPositionAndRotation(b.pivot + q * (t.position - b.pivot), q * t.rotation);
                }
            return saved;
        }

        static void Undo(List<(Transform t, Vector3 p, Quaternion r)> saved)
        {
            for (int i = saved.Count - 1; i >= 0; i--)
                saved[i].t.SetPositionAndRotation(saved[i].p, saved[i].r);
        }

        /// <summary>The fighter as the meshes bake now, drawn by the camera (the skinned renderers' own drawing does not
        /// always see bones moved by a script between two editor updates).</summary>
        static void Render(FighterRig rig, Camera cam, int width, int height, string path)
        {
            var temp = new List<(SkinnedMeshRenderer smr, GameObject go, Mesh mesh)>();
            foreach (var smr in rig.GetComponentsInChildren<SkinnedMeshRenderer>(false).Where(s => s.enabled && s.sharedMesh != null && !s.forceRenderingOff))
            {
                var mesh = new Mesh();
                smr.BakeMesh(mesh, true);
                var go = new GameObject("baked " + smr.name) { hideFlags = HideFlags.HideAndDontSave };
                go.transform.SetParent(smr.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = smr.sharedMaterials;
                mr.shadowCastingMode = smr.shadowCastingMode;
                smr.forceRenderingOff = true;
                temp.Add((smr, go, mesh));
            }
            RoeCapture.Render(cam, width, height, path, 90);
            foreach (var (smr, go, mesh) in temp)
            {
                smr.forceRenderingOff = false;
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(mesh);
            }
        }

        /// <summary>One breast turned: what moved, leaks, and the skin against the cloth worn on it.</summary>
        /// <summary>The bones under a breast's root that carry skin or cloth (the end of the chain a spring solver moves).</summary>
        static List<Transform> ChainBelow(List<Layer> layers, Breast b)
        {
            var below = new HashSet<Transform>();
            foreach (var l in layers)
            {
                var bones = l.smr.bones;
                var counts = l.smr.sharedMesh.GetBonesPerVertex();
                var weights = l.smr.sharedMesh.GetAllBoneWeights();
                int at = 0;
                for (int i = 0; i < counts.Length; i++)
                {
                    for (int k = 0; k < counts[i]; k++)
                    {
                        var w = weights[at + k];
                        if (w.weight < 0.05f || w.boneIndex < 0 || w.boneIndex >= bones.Length || bones[w.boneIndex] == null)
                            continue;
                        var bone = bones[w.boneIndex];
                        if (!b.moved.Contains(bone) && b.moved.Any(m => bone.IsChildOf(m)))
                            below.Add(bone);
                    }
                    at += counts[i];
                }
            }
            // (a Vindictus breast rig hangs 16 shape helpers under each breast: the two that carry the most)
            var carried = new Dictionary<Transform, int>();
            foreach (var l in layers)
            {
                var bones = l.smr.bones;
                var counts = l.smr.sharedMesh.GetBonesPerVertex();
                var weights = l.smr.sharedMesh.GetAllBoneWeights();
                int at = 0;
                for (int i = 0; i < counts.Length; i++)
                {
                    for (int k = 0; k < counts[i]; k++)
                    {
                        var w = weights[at + k];
                        if (w.weight >= 0.05f && w.boneIndex >= 0 && w.boneIndex < bones.Length && bones[w.boneIndex] != null && below.Contains(bones[w.boneIndex]))
                            carried[bones[w.boneIndex]] = (carried.TryGetValue(bones[w.boneIndex], out int c) ? c : 0) + 1;
                    }
                    at += counts[i];
                }
            }
            return carried.OrderByDescending(kv => kv.Value).Take(2).Select(kv => kv.Key).OrderBy(Depth).ToList();
        }

        /// <summary>Per bone of a breast (root and below): the vertices on it per kind of mesh, and their mean weight on it.</summary>
        static string BoneTable(List<Layer> layers, Breast b)
        {
            var table = new Dictionary<Transform, Dictionary<string, (int n, double w)>>();
            foreach (var l in layers)
            {
                var bones = l.smr.bones;
                var counts = l.smr.sharedMesh.GetBonesPerVertex();
                var weights = l.smr.sharedMesh.GetAllBoneWeights();
                int at = 0;
                for (int i = 0; i < counts.Length; i++)
                {
                    for (int k = 0; k < counts[i]; k++)
                    {
                        var w = weights[at + k];
                        if (w.weight < 0.05f || w.boneIndex < 0 || w.boneIndex >= bones.Length || bones[w.boneIndex] == null || l.role[i] == null)
                            continue;
                        var bone = bones[w.boneIndex];
                        if (!b.moved.Any(m => bone == m || bone.IsChildOf(m)))
                            continue;
                        if (!table.TryGetValue(bone, out var row))
                            table[bone] = row = new Dictionary<string, (int, double)>();
                        string key = l.role[i] == "piece" || l.role[i] == "cloth" ? l.what[i] : l.role[i];
                        row.TryGetValue(key, out var c);
                        row[key] = (c.n + 1, c.w + w.weight);
                    }
                    at += counts[i];
                }
            }
            if (table.Count > 12)
                return $"      on its bones: {table.Count} bones (soft-body nodes) carry {table.Values.Sum(r => r.Values.Sum(c => c.n))} vertex weights\n";
            var sb = new StringBuilder("      on its bones (vertices with 5 % or more, mean weight):\n");
            foreach (var kv in table.OrderBy(kv => Depth(kv.Key)))
                sb.Append($"        {kv.Key.name}: " + string.Join(", ", kv.Value.OrderByDescending(x => x.Value.n).Select(x => $"{x.Key} {x.Value.n} ({x.Value.w / x.Value.n:F2})")) + "\n");
            return sb.ToString();
        }

        static string Measure(List<Layer> layers, List<Breast> breasts, int which, string title)
        {
            var b = breasts[which];
            var moved = new Dictionary<string, (int n, float max)>();
            var leaks = new Dictionary<string, (int n, float max)>();
            int own = 0;
            foreach (var l in layers)
                for (int i = 0; i < l.rest.Length; i++)
                {
                    if (l.role[i] == null)
                        continue;
                    float d = (l.now[i] - l.rest[i]).magnitude;
                    if (d < Moved)
                        continue;
                    string key = l.role[i] == "piece" || l.role[i] == "cloth" ? $"{l.role[i]} {l.what[i]}" : l.role[i];
                    moved.TryGetValue(key, out var c);
                    moved[key] = (c.n + 1, Mathf.Max(c.max, d));
                    float bw = l.breastWeight[which][i];
                    float far = (l.bind[i] - b.centre).magnitude;
                    bool mine = bw >= 0.5f;
                    if (mine)
                        own++;
                    string part = l.cls[i];
                    string why = far > Reach ? $"{far * 100f:F0}+ cm away" : !mine && part != "torso" && part != "breast only" ? "on " + part : null;
                    if (why == null)
                        continue;
                    string where = $"{l.role[i]} {(far > Reach ? "far (" + part + ")" : "on " + part)}";
                    leaks.TryGetValue(where, out var e);
                    leaks[where] = (e.n + 1, Mathf.Max(e.max, d));
                }

            // the skin under the chest's clothes against them, paired as modelled: cloth lying on the skin (within 12 mm in front
            // of it along its normal, at most 6 mm aside), itself worn on the chest (its strongest bone the torso's or a breast's)
            var cloth = new List<(Vector3 bind, Vector3 rest, Vector3 now, string what, float bw)>();
            foreach (var l in layers)
                for (int i = 0; i < l.bind.Length; i++)
                    if ((l.role[i] == "cloth" || l.role[i] == "piece") && (l.cls[i] == "torso" || l.cls[i] == "breast only" || l.breastWeight[which][i] > 0f) &&
                        (l.bind[i] - b.centre).sqrMagnitude < 0.25f * 0.25f)
                        cloth.Add((l.bind[i], l.rest[i], l.now[i], l.what[i], l.breastWeight[which][i]));
            var worstPairs = new List<(float sep, string about)>();
            var byPiece = new Dictionary<string, (int pairs, int apart, int poke, float worst)>();
            var grid = new Dictionary<Vector3Int, List<int>>();
            for (int k = 0; k < cloth.Count; k++)
            {
                var key = Vector3Int.FloorToInt(cloth[k].bind / Over);
                if (!grid.TryGetValue(key, out var list))
                    grid[key] = list = new List<int>();
                list.Add(k);
            }
            int pairs = 0, apart = 0, poke = 0;
            float maxApart = 0f, maxPoke = 0f;
            foreach (var l in layers)
                for (int i = 0; i < l.bind.Length; i++)
                {
                    if ((l.role[i] != "skin" && l.role[i] != "nude") || (l.bind[i] - b.centre).sqrMagnitude > Reach * Reach)
                        continue;
                    var p = l.bind[i];
                    var n = l.bindNormal[i];
                    if (n == Vector3.zero)
                        continue;
                    var c0 = Vector3Int.FloorToInt(p / Over);
                    int best = -1;
                    float bestD = Aside * Aside;
                    for (int x = -1; x <= 1; x++)
                        for (int y = -1; y <= 1; y++)
                            for (int z = -1; z <= 1; z++)
                                if (grid.TryGetValue(new Vector3Int(c0.x + x, c0.y + y, c0.z + z), out var list))
                                    foreach (int k in list)
                                    {
                                        // on it: in front of the skin (along its normal) within OnSkin, at most Aside to the
                                        // side, and of those the one most nearly straight out from it
                                        var r = cloth[k].bind - p;
                                        float along = Vector3.Dot(r, n);
                                        if (along < -0.002f || along > OnSkin || (r - along * n).sqrMagnitude > Aside * Aside)
                                            continue;
                                        float side = (r - along * n).sqrMagnitude;
                                        if (side < bestD)
                                        {
                                            bestD = side;
                                            best = k;
                                        }
                                    }
                    if (best < 0)
                        continue;
                    pairs++;
                    var restRel = cloth[best].rest - l.rest[i];
                    var nowRel = cloth[best].now - l.now[i];
                    float sep = (nowRel - restRel).magnitude;
                    byPiece.TryGetValue(cloth[best].what, out var bp);
                    bp.pairs++;
                    if (sep > Apart)
                    {
                        apart++;
                        bp.apart++;
                    }
                    maxApart = Mathf.Max(maxApart, sep);
                    bp.worst = Mathf.Max(bp.worst, sep);
                    var nr = l.normal[i];
                    float gapRest = Vector3.Dot(restRel, nr), gapNow = Vector3.Dot(nowRel, nr);
                    if (gapRest >= 0f && gapNow < -0.001f)
                    {
                        poke++;
                        bp.poke++;
                        maxPoke = Mathf.Max(maxPoke, -gapNow);
                    }
                    byPiece[cloth[best].what] = bp;
                    if (sep > Apart)
                    {
                        var rb = l.bind[i] - b.centre;
                        worstPairs.Add((sep, $"{sep * 1000f:F0} mm: {l.role[i]} #{i} breast weight {l.breastWeight[which][i]:F2} at ({rb.x * 100f:F1}, {rb.y * 100f:F1}, {rb.z * 100f:F1}) cm, " +
                                             $"{cloth[best].what} breast weight {cloth[best].bw:F2}, {Vector3.Dot(cloth[best].bind - p, n) * 1000f:F0} mm out, {Mathf.Sqrt(bestD) * 1000f:F0} mm aside"));
                    }
                }

            var sb = new StringBuilder($"    {title}:\n");
            foreach (var w in worstPairs.OrderByDescending(x => x.sep).Take(6))
                sb.Append($"      apart: {w.about}\n");
            sb.Append("      moved: " + (moved.Count == 0 ? "NOTHING (no skin on these bones)" :
                string.Join(", ", moved.OrderByDescending(kv => kv.Value.n).Select(kv => $"{kv.Key} {kv.Value.n} (up to {kv.Value.max * 100f:F1} cm)"))) +
                $"; of them {own} half or more on this breast's bones\n");
            sb.Append("      leaks (moved but most of their weight on an arm / the neck / a leg, or further than 15 cm from the breast as modelled): " +
                (leaks.Count == 0 ? "none" :
                string.Join(", ", leaks.OrderByDescending(kv => kv.Value.n).Take(8).Select(kv => $"{kv.Key} {kv.Value.n} (up to {kv.Value.max * 100f:F1} cm)"))) + "\n");
            sb.Append($"      skin against the chest's cloth on it: {pairs} pairs, {apart} moved apart more than 5 mm (worst {maxApart * 1000f:F0} mm), " +
                      $"{poke} skin came out through the cloth (deepest {maxPoke * 1000f:F0} mm)" +
                      (byPiece.Count > 0 ? "; by piece: " + string.Join(", ", byPiece.OrderByDescending(kv => kv.Value.apart).Select(kv =>
                          $"{kv.Key} {kv.Value.pairs} pairs / {kv.Value.apart} apart / {kv.Value.poke} through (worst {kv.Value.worst * 1000f:F0} mm)")) : "") + "\n");
            return sb.ToString();
        }
    }
}
