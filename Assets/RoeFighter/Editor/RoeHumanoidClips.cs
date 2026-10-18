using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace RoeFighter.EditorTools
{
    /// <summary>
    /// Converts the game's Generic clips (one transform curve per bone) into Humanoid clips for
    /// the fighter prefab (see RoeHumanoid):
    ///   - the body becomes muscle curves + root curves.  The original clip is sampled on the
    ///     game's rig, the pose is copied bone by bone onto the fighter rig (whose hierarchy may
    ///     differ), and HumanPoseHandler reads the muscles from there;
    ///   - everything that is not a human bone (hair, skirt, breasts, twist helpers, weapons, face)
    ///     keeps its original transform curves, re-addressed to the fighter rig's paths;
    ///   - the nodes above the hips (Root, Bip001) lose their curves, because a humanoid Animator
    ///     places the hips itself; their other children (weapon attach points) are re-expressed
    ///     relative to the now static parent so that they end up in the same place.
    /// Verify() plays the result back next to the original and reports the differences.
    /// Batch mode: -executeMethod RoeFighter.EditorTools.RoeHumanoidClips.ConvertAll
    /// </summary>
    public static class RoeHumanoidClips
    {
        const int Fps = 60;

        public static string ClipPath(string id, string clip) => $"{RoeFighterBuilder.OutDir}/{id}/humanoid/{id}_{clip}.anim";

        /// <summary>
        /// The pose the fight stands a fighter in (FighterRig.stance: layer 0, the soles, how the feet stand, how the
        /// skirts and ribbons hang): the battle stance idle_01, unless it does not stand on the floor (g05 floats,
        /// sitting in the air) - then the first showcase clip that does (idle_02, react_01, react_02).
        /// </summary>
        public static AnimationClip Standing(string id, out string note) => Standing(id, out note, out _);

        /// <summary>The same; hover: how high the battle stance holds the lowest sole bone when it does not stand (m; g05 0.23), else 0.</summary>
        public static AnimationClip Standing(string id, out string note, out float hover)
        {
            hover = 0f;
            float battle = 0f;
            var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(RoeHumanoid.FighterPath(id)));
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var animator = go.GetComponent<Animator>();
            var soles = new[] { HumanBodyBones.LeftToes, HumanBodyBones.RightToes, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot }
                .Select(animator.GetBoneTransform).Where(t => t != null).ToArray();
            AnimationClip chosen = null, first = null;
            var tried = new List<string>();
            foreach (var name in new[] { "idle_01", "idle_02", "react_01", "react_02" })
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath(id, name));
                if (clip == null)
                    continue;
                first = first ?? clip;
                RoeCapture.Pose(go, clip, 0f);
                float lowest = soles.Length > 0 ? soles.Min(t => t.position.y) : 0f;
                tried.Add($"{name} lowest sole bone {lowest * 100f:F0} cm");
                if (name == "idle_01")
                    battle = lowest;
                if (lowest < StandingSole)
                {
                    chosen = clip;
                    break;
                }
            }
            RoeCapture.EndPosing();
            Object.DestroyImmediate(go);
            note = $"stands in {(chosen ?? first)?.name ?? "nothing"} ({string.Join(", ", tried)})";
            if (chosen != null && !chosen.name.EndsWith("idle_01"))
                hover = battle;
            return chosen ?? first;
        }

        /// <summary>A pose stands on the floor when its lowest toe or ankle bone is this low (m; heels lift the ankles 10-15 cm).</summary>
        public const float StandingSole = 0.12f;

        /// <summary>
        /// A converted game clip with its humanoid curves only (muscles, RootT, RootQ), saved as ClipPath(id, name): for the
        /// motion-capture layer (FighterRig.ownGuard - Luf's battle stance as her guard), where the transform curves of her
        /// fan, hair and skirt would pull those bones to the bind pose whenever a clip without them blends in.
        /// </summary>
        public static AnimationClip HumanOnly(string id, AnimationClip source, string name)
        {
            var clip = new AnimationClip { name = $"{id}_{name}", frameRate = source.frameRate };
            var bindings = AnimationUtility.GetCurveBindings(source).Where(b => b.type == typeof(Animator)).ToArray();
            AnimationUtility.SetEditorCurves(clip, bindings, bindings.Select(b => AnimationUtility.GetEditorCurve(source, b)).ToArray());
            AnimationUtility.SetAnimationClipSettings(clip, AnimationUtility.GetAnimationClipSettings(source));
            string assetPath = ClipPath(id, name);
            Directory.CreateDirectory(Path.GetDirectoryName(assetPath));
            AssetDatabase.DeleteAsset(assetPath);
            AssetDatabase.CreateAsset(clip, assetPath);
            return clip;
        }

        /// <summary>"Left Index 1 Stretched" (HumanTrait) -> "LeftHand.Index.1 Stretched" (curve attribute).</summary>
        public static string MuscleAttribute(string traitName)
        {
            var m = Regex.Match(traitName, @"^(Left|Right) (Thumb|Index|Middle|Ring|Little) (.+)$");
            return m.Success ? $"{m.Groups[1].Value}Hand.{m.Groups[2].Value}.{m.Groups[3].Value}" : traitName;
        }

        public static string PathOf(Transform t, Transform root)
        {
            var parts = new List<string>();
            while (t != null && t != root)
            {
                parts.Add(t.name);
                t = t.parent;
            }
            parts.Reverse();
            return string.Join("/", parts);
        }

        static Dictionary<string, Transform> ByName(GameObject go)
        {
            var d = new Dictionary<string, Transform>();
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                if (t != go.transform && !d.ContainsKey(t.name))
                    d[t.name] = t;
            return d;
        }

        /// <summary>Copies the pose of one rig onto another with the same bone names (parents first).</summary>
        public class PoseCopier
        {
            readonly Transform[] from, to;

            public PoseCopier(GameObject source, GameObject target, ICollection<Transform> skip = null)
            {
                var src = ByName(source);
                var a = new List<Transform>();
                var b = new List<Transform>();
                foreach (var t in target.GetComponentsInChildren<Transform>(true))   // depth first: parents come first
                {
                    if (t == target.transform || !src.TryGetValue(t.name, out var s) || (skip != null && skip.Contains(t)))
                        continue;
                    a.Add(s);
                    b.Add(t);
                }
                from = a.ToArray();
                to = b.ToArray();
            }

            public void Copy()
            {
                for (int i = 0; i < from.Length; i++)
                {
                    to[i].localScale = from[i].localScale;
                    to[i].SetPositionAndRotation(from[i].position, from[i].rotation);
                }
            }
        }

        class Keys
        {
            public readonly List<Keyframe>[] curves;
            public Keys(int count)
            {
                curves = new List<Keyframe>[count];
                for (int i = 0; i < count; i++)
                    curves[i] = new List<Keyframe>();
            }
        }

        static AnimationCurve Curve(List<Keyframe> keys)
        {
            var curve = new AnimationCurve(keys.ToArray());
            for (int i = 0; i < curve.length; i++)
                curve.SmoothTangents(i, 0f);
            return curve;
        }

        /// <param name="original">instance of the Generic prefab (the game's hierarchy)</param>
        /// <param name="fighter">instance of the fighter prefab (humanoid avatar, normalized hierarchy)</param>
        /// <param name="fighterPrefab">the fighter prefab asset, for bind-pose values</param>
        public static AnimationClip Convert(string id, string clipName, AnimationClip source,
                                            GameObject original, GameObject fighter, GameObject fighterPrefab)
        {
            var root = fighter.transform;
            var avatar = fighter.GetComponent<Animator>().avatar;
            var map = RoeHumanoid.MapBones(fighter);
            var humanTransforms = new HashSet<Transform>(map.Values);
            var hips = map["Hips"];
            // Nodes above the hips stay in their bind pose (a humanoid Animator places the hips itself).
            // Every non-human node whose parent is one of them, or whose parent differs from the game's
            // rig (weapons moved under a hand), gets new curves: its local values on the fighter rig
            // once the original pose has been copied over.
            var dropped = RoeHumanoid.AboveHips(fighter, map);
            var copier = new PoseCopier(original, fighter, dropped);
            var originalByName = ByName(original);
            var rebake = new List<Transform>();
            foreach (var t in fighter.GetComponentsInChildren<Transform>(true))
            {
                if (t == root || humanTransforms.Contains(t) || dropped.Contains(t))
                    continue;
                if (!originalByName.TryGetValue(t.name, out var o) || o.parent == null)
                    continue;
                if (dropped.Contains(t.parent) || o.parent.name != t.parent.name)
                    rebake.Add(t);
            }
            var bindScale = map.ToDictionary(p => p.Value, p => fighterPrefab.transform.Find(PathOf(p.Value, root)).localScale);

            // Non-human nodes on a limb bone: the avatar moves each limb's twist to the next joint (RoeHumanoid,
            // upperArmTwist ... = 1), so in playback the limb lacks the twist the game gave it and everything hung on
            // it turns about the limb with it.  Their curves are re-expressed against the limb as the avatar poses it
            // (posed: a second instance that takes the muscles back).  g05's ribbons hang a metre down from her arms:
            // their tips were 0.9-1.2 m off; the twist helpers 1-3 cm.
            var limbs = new HashSet<Transform>(new[] { "LeftUpperArm", "RightUpperArm", "LeftLowerArm", "RightLowerArm",
                    "LeftUpperLeg", "RightUpperLeg", "LeftLowerLeg", "RightLowerLeg" }.Where(map.ContainsKey).Select(k => map[k]));
            var onLimb = fighter.GetComponentsInChildren<Transform>(true)
                .Where(t => t != root && t.parent != null && limbs.Contains(t.parent) && !humanTransforms.Contains(t) && !dropped.Contains(t)
                            && !rebake.Contains(t) && originalByName.ContainsKey(t.name)).ToList();
            GameObject posed = null;
            HumanPoseHandler posedHandler = null;
            var posedParent = new Dictionary<Transform, Transform>();
            if (onLimb.Count > 0)
            {
                posed = Object.Instantiate(fighterPrefab);
                posed.transform.SetPositionAndRotation(root.position, root.rotation);
                var posedByName = ByName(posed);
                foreach (var t in onLimb)
                    posedParent[t] = posedByName[t.parent.name];
                posedHandler = new HumanPoseHandler(avatar, posed.transform);
            }
            var limbKeys = onLimb.ToDictionary(r => r, r => new Keys(10));
            var lastLimbQ = onLimb.ToDictionary(r => r, r => Quaternion.identity);

            var handler = new HumanPoseHandler(avatar, root);
            var pose = new HumanPose();
            int muscles = HumanTrait.MuscleCount;
            int frames = Mathf.Max(1, Mathf.RoundToInt(source.length * Fps));

            var muscleKeys = new Keys(muscles);
            var rootKeys = new Keys(7);
            var rebakeKeys = rebake.ToDictionary(r => r, r => new Keys(10));
            var lastRootQ = Quaternion.identity;
            var lastRebakeQ = rebake.ToDictionary(r => r, r => Quaternion.identity);
            float worstScale = 0f;
            string worstScaleBone = "";

            for (int f = 0; f <= frames; f++)
            {
                float time = Mathf.Min(source.length, f / (float)Fps);
                RoeCapture.Pose(original, source, time);
                copier.Copy();
                handler.GetHumanPose(ref pose);

                for (int i = 0; i < muscles; i++)
                    muscleKeys.curves[i].Add(new Keyframe(time, pose.muscles[i]));

                var q = pose.bodyRotation;
                if (f > 0 && Quaternion.Dot(q, lastRootQ) < 0f)
                    q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                lastRootQ = q;
                float[] rootValues = { pose.bodyPosition.x, pose.bodyPosition.y, pose.bodyPosition.z, q.x, q.y, q.z, q.w };
                for (int i = 0; i < 7; i++)
                    rootKeys.curves[i].Add(new Keyframe(time, rootValues[i]));

                foreach (var r in rebake)
                {
                    Vector3 p = r.localPosition;
                    var rq = r.localRotation;
                    if (f > 0 && Quaternion.Dot(rq, lastRebakeQ[r]) < 0f)
                        rq = new Quaternion(-rq.x, -rq.y, -rq.z, -rq.w);
                    lastRebakeQ[r] = rq;
                    var s = r.localScale;
                    float[] v = { p.x, p.y, p.z, rq.x, rq.y, rq.z, rq.w, s.x, s.y, s.z };
                    for (int i = 0; i < 10; i++)
                        rebakeKeys[r].curves[i].Add(new Keyframe(time, v[i]));
                }

                if (posedHandler != null)
                {
                    posedHandler.SetHumanPose(ref pose);
                    foreach (var r in onLimb)
                    {
                        // r holds the game's pose (copied); its parent as the avatar will pose it
                        var parent = posedParent[r];
                        var p = parent.InverseTransformPoint(r.position);
                        var rq = Quaternion.Inverse(parent.rotation) * r.rotation;
                        if (f > 0 && Quaternion.Dot(rq, lastLimbQ[r]) < 0f)
                            rq = new Quaternion(-rq.x, -rq.y, -rq.z, -rq.w);
                        lastLimbQ[r] = rq;
                        var s = r.localScale;
                        float[] v = { p.x, p.y, p.z, rq.x, rq.y, rq.z, rq.w, s.x, s.y, s.z };
                        for (int i = 0; i < 10; i++)
                            limbKeys[r].curves[i].Add(new Keyframe(time, v[i]));
                    }
                }

                foreach (var pair in bindScale)
                {
                    float d = (pair.Key.localScale - pair.Value).magnitude;
                    if (d > worstScale)
                    {
                        worstScale = d;
                        worstScaleBone = pair.Key.name;
                    }
                }
            }
            handler.Dispose();
            posedHandler?.Dispose();
            if (posed != null)
                Object.DestroyImmediate(posed);

            var clip = new AnimationClip { name = $"{id}_{clipName}", frameRate = Fps };
            var bindings = new List<EditorCurveBinding>();
            var curves = new List<AnimationCurve>();

            for (int i = 0; i < muscles; i++)
            {
                bindings.Add(EditorCurveBinding.FloatCurve("", typeof(Animator), MuscleAttribute(HumanTrait.MuscleName[i])));
                curves.Add(Curve(muscleKeys.curves[i]));
            }
            string[] rootNames = { "RootT.x", "RootT.y", "RootT.z", "RootQ.x", "RootQ.y", "RootQ.z", "RootQ.w" };
            for (int i = 0; i < 7; i++)
            {
                bindings.Add(EditorCurveBinding.FloatCurve("", typeof(Animator), rootNames[i]));
                curves.Add(Curve(rootKeys.curves[i]));
            }

            // Original transform curves of everything that is not a human bone and not above the hips,
            // addressed by bone name: the fighter rig's paths can differ from the game's.
            var fighterByName = ByName(fighter);
            var skip = new HashSet<Transform>(humanTransforms);
            skip.UnionWith(dropped);
            skip.UnionWith(rebake);
            skip.UnionWith(onLimb);
            int kept = 0, replaced = 0, missing = 0;
            foreach (var binding in AnimationUtility.GetCurveBindings(source))
            {
                if (binding.type != typeof(Transform))
                {
                    replaced++;     // the game's own root-motion floats of the Generic rig
                    continue;
                }
                string boneName = binding.path.Substring(binding.path.LastIndexOf('/') + 1);
                if (!fighterByName.TryGetValue(boneName, out var target))
                {
                    missing++;
                    continue;
                }
                if (skip.Contains(target))
                {
                    replaced++;
                    continue;
                }
                var b = binding;
                b.path = PathOf(target, root);
                bindings.Add(b);
                curves.Add(AnimationUtility.GetEditorCurve(source, binding));
                kept++;
            }

            string[] trs =
            {
                "m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z",
                "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w",
                "m_LocalScale.x", "m_LocalScale.y", "m_LocalScale.z",
            };
            foreach (var r in rebake)
            {
                string path = PathOf(r, root);
                for (int i = 0; i < 10; i++)
                {
                    bindings.Add(EditorCurveBinding.FloatCurve(path, typeof(Transform), trs[i]));
                    curves.Add(Curve(rebakeKeys[r].curves[i]));
                }
            }
            foreach (var r in onLimb)
            {
                string path = PathOf(r, root);
                for (int i = 0; i < 10; i++)
                {
                    bindings.Add(EditorCurveBinding.FloatCurve(path, typeof(Transform), trs[i]));
                    curves.Add(Curve(limbKeys[r].curves[i]));
                }
            }

            AnimationUtility.SetEditorCurves(clip, bindings.ToArray(), curves.ToArray());
            clip.EnsureQuaternionContinuity();

            var settings = AnimationUtility.GetAnimationClipSettings(source);
            settings.loopBlend = false;
            // keep the motion inside the pose, as the game plays these clips: the object stays, the body travels
            settings.loopBlendOrientation = true;
            settings.loopBlendPositionY = true;
            settings.loopBlendPositionXZ = true;
            settings.keepOriginalOrientation = true;
            settings.keepOriginalPositionY = true;
            settings.keepOriginalPositionXZ = true;
            settings.heightFromFeet = false;
            settings.mirror = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            string assetPath = ClipPath(id, clipName);
            Directory.CreateDirectory(Path.GetDirectoryName(assetPath));
            AssetDatabase.DeleteAsset(assetPath);
            AssetDatabase.CreateAsset(clip, assetPath);
            Debug.Log($"[ROE] {id} {clipName}: {assetPath} human={clip.humanMotion}; {frames + 1} keys x {muscles} muscles, " +
                      $"{kept} curves kept, {replaced} replaced, {missing} without a target, {rebake.Count} nodes re-based, {onLimb.Count} on the limbs re-expressed" +
                      (worstScale > 0.01f ? $"; WARNING {worstScaleBone} is scaled by the clip ({worstScale:F2}) and loses that" : ""));
            return clip;
        }

        /// <summary>
        /// Plays the humanoid clip on a fighter instance and compares every skinned bone's position
        /// with the original Generic playback on the game's rig.
        /// </summary>
        public static string Verify(GameObject original, AnimationClip source, GameObject fighter, AnimationClip converted)
        {
            var target = ByName(fighter);
            var a = original.GetComponentsInChildren<SkinnedMeshRenderer>(true).SelectMany(r => r.bones)
                .Where(b => b != null && target.ContainsKey(b.name)).Distinct().ToArray();
            var b2 = a.Select(t => target[t.name]).ToArray();

            int frames = Mathf.Max(1, Mathf.RoundToInt(source.length * Fps));
            float worst = 0f, sum = 0f;
            int count = 0;
            string worstBone = "";
            float worstTime = 0f;
            for (int f = 0; f <= frames; f += 2)
            {
                float time = Mathf.Min(source.length, f / (float)Fps);
                AnimationMode.BeginSampling();
                AnimationMode.SampleAnimationClip(original, source, time);
                AnimationMode.SampleAnimationClip(fighter, converted, time);
                AnimationMode.EndSampling();
                for (int i = 0; i < a.Length; i++)
                {
                    float d = Vector3.Distance(a[i].position - original.transform.position, b2[i].position - fighter.transform.position);
                    sum += d;
                    count++;
                    if (d > worst)
                    {
                        worst = d;
                        worstBone = a[i].name;
                        worstTime = time;
                    }
                }
            }
            return $"mean {sum / count * 100f:F2} cm, worst {worst * 100f:F1} cm ({worstBone} at {worstTime:F2} s) over {a.Length} bones";
        }

        [MenuItem("ROE Fighter/Build/Humanoid fighters and clips")]
        public static void ConvertAll()
        {
            var manifest = RoeManifest.Load();
            // -roeChars b10,g05: only these (rebuilding a fighter prefab drops what RoeHelperFit put on it).
            // -roeKeepPrefab 1: the clips again on the fighter prefab as it is (its avatar, the helper and skirt rigs
            // fitted on it stay): after a fix in the conversion itself (10-03: the limbs, for a08 and g04)
            bool keep = RoeCapture.Arg("-roeKeepPrefab", "0") == "1";
            foreach (var c in manifest.Chosen())
            {
                var fighterPrefab = keep ? AssetDatabase.LoadAssetAtPath<GameObject>(RoeHumanoid.FighterPath(c.id)) : null;
                bool rebuilt = fighterPrefab == null;
                if (fighterPrefab == null)
                    fighterPrefab = RoeHumanoid.BuildFighter(c);    // always rebuilt otherwise: the clips depend on the avatar
                if (fighterPrefab == null)
                    continue;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RoeFighterBuilder.PrefabPath(c.id));
                var original = Object.Instantiate(prefab);
                original.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var worker = Object.Instantiate(fighterPrefab);     // receives the copied poses
                worker.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var player = Object.Instantiate(fighterPrefab);     // plays the converted clips
                player.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

                if (!AnimationMode.InAnimationMode())
                    AnimationMode.StartAnimationMode();
                foreach (var entry in c.clips)
                {
                    var source = AssetDatabase.LoadAssetAtPath<AnimationClip>(entry.path);
                    if (source == null)
                        continue;
                    var converted = Convert(c.id, entry.name, source, original, worker, fighterPrefab);
                    Debug.Log($"[ROE] {c.id} {entry.name}: playback difference {Verify(original, source, player, converted)}");
                }
                RoeCapture.EndPosing();
                Object.DestroyImmediate(original);
                Object.DestroyImmediate(worker);
                Object.DestroyImmediate(player);
                // her weapon in her hand again (a08's greatsword: RoeWeaponGrip), which the rebuilt prefab lost
                if (rebuilt && RoeWeaponGrip.HasGameGrips(c.id))
                    Debug.Log(RoeWeaponGrip.Attach(c.id, picture: false));
            }
            AssetDatabase.SaveAssets();
        }
    }
}
