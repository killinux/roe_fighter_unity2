using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RoeFighter.Fight;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RoeFighter.EditorTools
{
    /// <summary>
    /// A ROE fighter's weapon in her hand under motion capture (user 10-06: "动作有拿巨剑的普通攻击么，给inase拿着长剑的找一些普通
    /// 攻击"): a08's greatsword.  Her game's battle stance (idle_01) holds the sword rigid in her right hand, the tip on the
    /// ground; the humanoid model hangs its bones (Point007_L / _R, no humanoid bones) on the pelvis, so the motion capture
    /// left it where the stance put it and the fight showed it only in her game's own clips.  This measures, on the game's own
    /// rig (the Generic prefab) posed by the game's own idle_01 at GripSamples times, where each weapon's carrying bone
    /// (FighterRig.WeaponRoots of the fighter, found by name) is in each hand's frame; the hand it moves least in, within
    /// GripSlack and GripTurn, holds it: a RoeGrips on the fighter prefab.  (On the humanoid stance the hand moves up to 15 cm
    /// against the sword: the hand is retargeted, the sword's bones keep their curves on the pelvis.)  The
    /// longest weapon also gets a RoeBlade (hilt to tip in its bone's frame, from its baked mesh): strikes with that hand hit
    /// and reach with the blade (only while she holds it: FighterRig.Blade).  Check pictures of the stance and of the grip on
    /// a raised arm go to out/weapon_grip/&lt;id&gt;_0..2.png.  A fighter prefab rebuilt from scratch gets the grips again
    /// (RoeHumanoidClips.ConvertAll, when tools/roe/&lt;id&gt;_grip.json is there).  b10 (Kart, user 10-06: "一个拿斧子和枪的上衣是
    /// 红色的卡地亚"): her axe in her right hand, her pistols in the holsters on her thighs (any humanoid bone holds), the
    /// pistols gone once burst stage 1 takes the holsters off.
    ///   -executeMethod RoeFighter.EditorTools.RoeWeaponGrip.Build -roeChars b10
    ///   -executeMethod RoeFighter.EditorTools.RoeWeaponGrip.Build [-roeChars a08]
    /// </summary>
    public static class RoeWeaponGrip
    {
        const int GripSamples = 9;
        const float GripSlack = 0.01f;     // m: a weapon held moves no more than this in the hand's frame over the stance
        const float GripTurn = 2f;         // degrees

        [MenuItem("ROE Fighter/Build/Weapon grips (a08)")]
        public static void Build()
        {
            foreach (var id in RoeCapture.Arg("-roeChars", "a08").Split(','))
                Debug.Log(Attach(id.Trim()));
            AssetDatabase.SaveAssets();
        }

        [System.Serializable]
        class GripFile
        {
            public string id, clip, source;
            public List<GripEntry> grips = new List<GripEntry>();
        }

        [System.Serializable]
        class GripEntry
        {
            public string bone, hand;            // hand: the holding bone's name on the game's skeleton (any humanoid bone)
            public float[] position, rotation;
            public float slack, turn;
            public int burstStage;               // RoeGrips.Grip.burstStage (tools/roe_grip.py --burst)
        }

        /// <summary>The humanoid bone whose transform has this name (the game's: "Bip001 R Hand", "Bip001 R Thigh"), or null.</summary>
        static HumanBodyBones? HumanBone(Animator animator, string name)
        {
            foreach (HumanBodyBones b in System.Enum.GetValues(typeof(HumanBodyBones)))
                if (b != HumanBodyBones.LastBone && animator.GetBoneTransform(b)?.name == name)
                    return b;
            return null;
        }

        static string GripJson(string id) => $"tools/roe/{id}_grip.json";

        static GripFile GameGrips(string id) => GameGrips(id, out _);

        static GripFile GameGrips(string id, out string file)
        {
            file = GripJson(id);
            if (!File.Exists(Path.Combine(Path.GetDirectoryName(Application.dataPath), file)))
                file = GripJson(RoeManifest.SuitOf(id));        // a recolour holds its weapon as its suit does (b14_outfit1: b14's)
            string path = Path.Combine(Path.GetDirectoryName(Application.dataPath), file);
            return File.Exists(path) ? JsonUtility.FromJson<GripFile>(File.ReadAllText(path)) : null;
        }

        /// <summary>tools/roe_grip.py measured her grip (tools/roe/&lt;id&gt;_grip.json): a fighter prefab rebuilt from scratch gets
        /// it again (RoeHumanoidClips.ConvertAll), as the fight needs it for her own strikes (MotionPack.grip).</summary>
        public static bool HasGameGrips(string id) => GameGrips(id) != null;

        static string PathTo(Transform root, Transform t)
        {
            var parts = new List<string>();
            for (var x = t; x != null && x != root; x = x.parent)
                parts.Add(x.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        /// <summary>The grips and the blade onto &lt;id&gt;_fighter.prefab; picture: the check pictures too (needs -Graphics).</summary>
        public static string Attach(string id, bool picture = true)
        {
            string path = RoeHumanoid.FighterPath(id);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var stance = AssetDatabase.LoadAssetAtPath<AnimationClip>(RoeHumanoidClips.ClipPath(id, "idle_01"));
            if (prefab == null || stance == null)
                return $"[ROE] grips {id}: no fighter prefab ({path}) or no stance clip";
            var game = AssetDatabase.LoadAssetAtPath<GameObject>(RoeFighterBuilder.PrefabPath(id));
            var gameStance = RoeManifest.Load().Find(id).LoadClip("idle_01");
            if (game == null || gameStance == null)
                return $"[ROE] grips {id}: no game rig ({RoeFighterBuilder.PrefabPath(id)}) or no idle_01 in the manifest";
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var original = Object.Instantiate(game);
            original.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var log = new StringBuilder($"[ROE] grips {id}:");
            var grips = new List<(string bone, HumanBodyBones hand, Vector3 pos, Quaternion rot, float slack, float turn, int burst)>();
            (string bone, Vector3 hilt, Vector3 tip, HumanBodyBones hand, float length)? blade = null;
            try
            {
                var animator = go.GetComponent<Animator>();
                var weapons = go.GetComponentsInChildren<Renderer>(true).Where(r => r.sharedMaterials.Any(m => m != null && m.name.StartsWith("wp_"))).ToArray();
                var roots = FighterRig.WeaponRoots(animator, weapons);
                if (roots.Length == 0)
                    return $"[ROE] grips {id}: no weapons ({weapons.Length} renderers, no bone of their own)";
                var hands = new[] { HumanBodyBones.RightHand, HumanBodyBones.LeftHand };
                var byName = original.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
                Transform Game(Transform t) => t != null && byName.TryGetValue(t.name, out var x) ? x : null;
                // which of the game clip's curves find no transform on the game's rig (a chain above the hand that does not bind
                // leaves the hand where the rig has it while the weapon's own curves move it)
                var unbound = AnimationUtility.GetCurveBindings(gameStance).Where(bd => bd.type == typeof(Transform) && AnimationUtility.GetAnimatedObject(original, bd) == null)
                    .Select(bd => bd.path).Distinct().ToList();
                log.Append($" game clip {gameStance.name}: {unbound.Count} transform paths unbound" +
                           (unbound.Count > 0 ? $" ({string.Join(", ", unbound.Take(6))})" : "") + ";");
                // each root in each hand's frame on the game's rig, over the game's stance
                var local = new Dictionary<(Transform, HumanBodyBones), List<(Vector3 p, Quaternion r)>>();
                if (!AnimationMode.InAnimationMode())
                    AnimationMode.StartAnimationMode();
                for (int s = 0; s < GripSamples; s++)
                {
                    AnimationMode.BeginSampling();
                    AnimationMode.SampleAnimationClip(original, gameStance, gameStance.length * s / (GripSamples - 1));
                    AnimationMode.EndSampling();
                    if (s == 0 || s == GripSamples / 2)
                        log.Append($" t{s}: " + string.Join(" ", new[] { "Root", "Bip001", "Bip001 Pelvis", "Bip001 R Hand", "Point007_L" }
                            .Where(byName.ContainsKey).Select(n => $"{n} {byName[n].position.x:F3},{byName[n].position.y:F3},{byName[n].position.z:F3}")) + ";");
                    foreach (var r in roots)
                        foreach (var h in hands)
                        {
                            Transform gr = Game(r), gh = Game(animator.GetBoneTransform(h));
                            if (gr == null || gh == null)
                                continue;
                            if (!local.TryGetValue((r, h), out var list))
                                local[(r, h)] = list = new List<(Vector3, Quaternion)>();
                            list.Add((Quaternion.Inverse(gh.rotation) * (gr.position - gh.position), Quaternion.Inverse(gh.rotation) * gr.rotation));
                        }
                }
                // the game's own grip, when tools/roe_grip.py measured it on her decoded animation (tools/roe/<id>_grip.json):
                // sampled here, the imported rig turns the hand a few degrees otherwise than the game does
                var fromGame = GameGrips(id, out string gripFile);
                if (fromGame != null)
                {
                    log.Append($" from {gripFile}:");
                    foreach (var r in roots)
                    {
                        var e = fromGame.grips.FirstOrDefault(x => x.bone == r.name);
                        var h = e == null ? null : HumanBone(animator, e.hand);
                        if (e == null || h == null || e.position == null || e.position.Length != 3 || e.rotation == null || e.rotation.Length != 4)
                        {
                            log.Append($" {r.name}: none");
                            continue;
                        }
                        log.Append($" {r.name} in the {h.Value} ({e.slack * 100f:F2} cm / {e.turn:F2} deg over the game's {fromGame.clip})" +
                                   (e.burstStage > 0 ? $", gone with burst stage {e.burstStage}" : ""));
                        grips.Add((PathTo(go.transform, r), h.Value, new Vector3(e.position[0], e.position[1], e.position[2]),
                                   new Quaternion(e.rotation[0], e.rotation[1], e.rotation[2], e.rotation[3]).normalized, e.slack, e.turn, e.burstStage));
                    }
                }
                foreach (var r in fromGame != null ? new Transform[0] : roots)
                {
                    var measured = hands.Where(h => local.ContainsKey((r, h))).Select(h =>
                    {
                        var l = local[(r, h)];
                        float slack = l.Max(a => l.Max(b => (a.p - b.p).magnitude));
                        float turn = l.Max(a => l.Max(b => Quaternion.Angle(a.r, b.r)));
                        return (h, slack, turn);
                    }).OrderBy(x => x.slack).ToList();
                    if (measured.Count == 0)
                    {
                        log.Append($" {r.name}: not on the game's rig");
                        continue;
                    }
                    var best = measured[0];
                    log.Append($" {r.name}: in the {best.h} {best.slack * 100f:F2} cm / {best.turn:F1} deg over the game's stance");
                    if (best.slack > GripSlack || best.turn > GripTurn)
                    {
                        log.Append(" - not held");
                        continue;
                    }
                    var first = local[(r, best.h)][0];
                    grips.Add((PathTo(go.transform, r), best.h, first.p, first.r, best.slack, best.turn, 0));
                }
                // the blade: the longest held weapon, from its mesh as the stance's first frame poses it, held as measured
                RoeCapture.Pose(go, stance, 0f);
                foreach (var g in grips)
                {
                    var hand = animator.GetBoneTransform(g.hand);
                    go.transform.Find(g.bone).SetPositionAndRotation(hand.position + hand.rotation * g.pos, hand.rotation * g.rot);
                }
                foreach (var w in weapons.OfType<SkinnedMeshRenderer>())
                {
                    var g = grips.FirstOrDefault(x => w.bones.Any(b => b != null && PathTo(go.transform, b).StartsWith(x.bone)));
                    if (g.bone == null)
                        continue;
                    var bone = go.transform.Find(g.bone);
                    var baked = new Mesh();
                    w.BakeMesh(baked, true);
                    var toBone = bone.worldToLocalMatrix * w.transform.localToWorldMatrix;
                    var pts = baked.vertices.Select(v => toBone.MultiplyPoint3x4(v)).ToArray();
                    Object.DestroyImmediate(baked);
                    if (pts.Length < 2)
                        continue;
                    // its long axis: the two vertices farthest apart (sampled), the hilt end the one nearer the hand
                    var sample = pts.Where((p, i) => i % Mathf.Max(1, pts.Length / 400) == 0).ToArray();
                    Vector3 a = sample[0], b = sample[0];
                    float far = 0f;
                    foreach (var p in sample)
                        foreach (var q in sample)
                        {
                            float d = (p - q).sqrMagnitude;
                            if (d > far)
                            {
                                far = d;
                                a = p;
                                b = q;
                            }
                        }
                    var handAt = bone.InverseTransformPoint(animator.GetBoneTransform(g.hand).position);
                    if ((b - handAt).sqrMagnitude < (a - handAt).sqrMagnitude)
                        (a, b) = (b, a);
                    // the hilt where the hand is along it (the blade from the guard on is what cuts; the grip is short beside it)
                    var axis = (b - a).normalized;
                    var hilt = a + axis * Mathf.Clamp(Vector3.Dot(handAt - a, axis), 0f, (b - a).magnitude);
                    float length = (b - hilt).magnitude * bone.lossyScale.x;
                    log.Append($"; {w.name}: {Mathf.Sqrt(far) * bone.lossyScale.x:F2} m long, blade {length:F2} m from the hand");
                    if (blade == null || length > blade.Value.length)
                        blade = (g.bone, hilt, b, g.hand, length);
                }
            }
            finally
            {
                RoeCapture.EndPosing();
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(original);
            }
            // onto the prefab
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                foreach (var old in root.GetComponentsInChildren<RoeBlade>(true).Where(x => grips.Any(g => PathTo(root.transform, x.transform) == g.bone)).ToArray())
                    Object.DestroyImmediate(old);
                var comp = root.GetComponent<RoeGrips>();
                if (grips.Count == 0)
                {
                    if (comp != null)
                        Object.DestroyImmediate(comp);
                }
                else
                {
                    comp = comp != null ? comp : root.AddComponent<RoeGrips>();
                    comp.grips = grips.Select(g => new RoeGrips.Grip { bone = root.transform.Find(g.bone), hand = g.hand, position = g.pos, rotation = g.rot, burstStage = g.burst }).ToArray();
                    if (blade.HasValue)
                    {
                        var on = root.transform.Find(blade.Value.bone);
                        var rb = on.gameObject.AddComponent<RoeBlade>();
                        rb.hand = blade.Value.hand;
                        rb.hilt = blade.Value.hilt;
                        rb.tip = blade.Value.tip;
                        log.Append($"; blade on {on.name} ({blade.Value.hand}), {blade.Value.length:F2} m");
                    }
                }
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            log.Append(grips.Count > 0 ? $"; {grips.Count} grip(s) on {Path.GetFileName(path)}" : "; nothing held");
            if (picture)
                Picture(id);
            return log.ToString();
        }

        /// <summary>The stance and two raised-arm poses with the grips applied, the weapons shown: out/weapon_grip/&lt;id&gt;.png.</summary>
        static void Picture(string id)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RoeHumanoid.FighterPath(id));
            var stance = AssetDatabase.LoadAssetAtPath<AnimationClip>(RoeHumanoidClips.ClipPath(id, "idle_01"));
            var grips = prefab != null ? prefab.GetComponent<RoeGrips>() : null;
            if (grips == null)
                return;
            string outDir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "out", "weapon_grip");
            Directory.CreateDirectory(outDir);
            var studio = RoeStudio.Build();
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var animator = go.GetComponent<Animator>();
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                r.enabled = true;
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                smr.forceMatrixRecalculationPerRender = true;
            var held = go.GetComponent<RoeGrips>();
            var forward = RoeShowcase.Forward(go);
            float height = RoeShowcase.WorldBounds(go).max.y;
            var body = new Vector3(0f, height * 0.5f, 0f);
            studio.LightFrom(forward);
            // the stance as the game has it, then the same with the grip applied (should match), then the right arm raised
            var shots = new List<string>();
            for (int k = 0; k < 3; k++)
            {
                RoeCapture.Pose(go, stance, 0f);
                if (k >= 1)
                    held.Apply(animator);
                if (k == 2)
                {
                    var upper = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
                    var lower = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
                    upper.rotation = Quaternion.AngleAxis(-110f, Vector3.Cross(Vector3.up, forward).normalized) * upper.rotation;
                    lower.rotation = Quaternion.AngleAxis(-30f, Vector3.Cross(Vector3.up, forward).normalized) * lower.rotation;
                    held.Apply(animator);
                }
                studio.Aim(body, forward, 25f, 6f, height * 2.4f, 26f);
                string file = Path.Combine(outDir, $"{id}_{k}.png");
                RoeCapture.Render(studio.camera, 700, 900, file);
                RoeCapture.Render(studio.camera, 700, 900, file);
                shots.Add(file);
            }
            RoeCapture.EndPosing();
            Object.DestroyImmediate(go);
            Debug.Log($"[ROE] grips {id}: pictures {string.Join(", ", shots)}");
        }
    }
}
