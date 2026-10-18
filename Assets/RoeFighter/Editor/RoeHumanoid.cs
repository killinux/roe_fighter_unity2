using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RoeFighter.EditorTools
{
    /// <summary>
    /// Gives the ROE characters (3ds Max Biped rigs, Generic in the game) a Unity Humanoid avatar,
    /// so that humanoid animation - a fighting move set shared by all characters - can drive them.
    ///
    /// The game's prefabs have no model importer behind them, so the avatar is built from code:
    /// map the Biped bones, straighten the limbs into a T-pose (the bind pose has the elbows bent
    /// 11-17 degrees), then AvatarBuilder.  The feet are left as they are: these characters stand
    /// on high heels, and keeping that as the "neutral" foot makes flat-footed animation land
    /// on the heels instead of flattening the foot.
    /// </summary>
    public static class RoeHumanoid
    {
        public static string AvatarPath(string id) => $"{RoeFighterBuilder.OutDir}/{id}/{id}_humanoid_avatar.asset";
        public static string FighterPath(string id) => $"{RoeFighterBuilder.OutDir}/{id}/{id}_fighter.prefab";

        // Unity human bone -> Biped bone.  UpperChest only when the rig has Spine2.
        static readonly (string human, string bone)[] Body =
        {
            ("Hips", "Bip001 Pelvis"),
            ("Spine", "Bip001 Spine"),
            ("Chest", "Bip001 Spine1"),
            ("UpperChest", "Bip001 Spine2"),
            ("Neck", "Bip001 Neck"),
            ("Head", "Bip001 Head"),
            ("LeftShoulder", "Bip001 L Clavicle"),
            ("LeftUpperArm", "Bip001 L UpperArm"),
            ("LeftLowerArm", "Bip001 L Forearm"),
            ("LeftHand", "Bip001 L Hand"),
            ("RightShoulder", "Bip001 R Clavicle"),
            ("RightUpperArm", "Bip001 R UpperArm"),
            ("RightLowerArm", "Bip001 R Forearm"),
            ("RightHand", "Bip001 R Hand"),
            ("LeftUpperLeg", "Bip001 L Thigh"),
            ("LeftLowerLeg", "Bip001 L Calf"),
            ("LeftFoot", "Bip001 L Foot"),
            ("LeftToes", "Bip001 L Toe0"),
            ("RightUpperLeg", "Bip001 R Thigh"),
            ("RightLowerLeg", "Bip001 R Calf"),
            ("RightFoot", "Bip001 R Foot"),
            ("RightToes", "Bip001 R Toe0"),
        };

        // ... and in Dead or Alive 6's rigs (Kasumi): bones are named by their global id, bone_<id>, the same in every
        // character and every part (costume, hair, face); G1M +X is her left (ripper_tpose docs/doa6-fighting-motions.md,
        // scripts/doa6/g2a_bvh.py).  No UpperChest.
        static readonly (string human, string bone)[] Doa6Body =
        {
            ("Hips", "bone_2"), ("Spine", "bone_9"), ("Chest", "bone_10"), ("Neck", "bone_11"), ("Head", "bone_12"),
            ("LeftShoulder", "bone_13"), ("LeftUpperArm", "bone_15"), ("LeftLowerArm", "bone_17"), ("LeftHand", "bone_19"),
            ("RightShoulder", "bone_14"), ("RightUpperArm", "bone_16"), ("RightLowerArm", "bone_18"), ("RightHand", "bone_20"),
            ("LeftUpperLeg", "bone_3"), ("LeftLowerLeg", "bone_5"), ("LeftFoot", "bone_7"), ("LeftToes", "bone_23"),
            ("RightUpperLeg", "bone_4"), ("RightLowerLeg", "bone_6"), ("RightFoot", "bone_8"), ("RightToes", "bone_24"),
        };

        // ... and in Vindictus: Defying Fate's (Unreal Engine 5, the MetaHuman body): pelvis, spine_01..05, neck_01/02, head,
        // clavicle / upperarm / lowerarm / hand, thigh / calf / foot / ball, fingers <finger>_01..03_<l|r>.  spine_02, spine_04
        // and neck_02 are left between the mapped ones (a humanoid has three spine bones and one neck), as are the twist and
        // corrective bones.
        static readonly (string human, string bone)[] UeBody =
        {
            ("Hips", "pelvis"), ("Spine", "spine_01"), ("Chest", "spine_03"), ("UpperChest", "spine_05"), ("Neck", "neck_01"), ("Head", "head"),
            ("LeftShoulder", "clavicle_l"), ("LeftUpperArm", "upperarm_l"), ("LeftLowerArm", "lowerarm_l"), ("LeftHand", "hand_l"),
            ("RightShoulder", "clavicle_r"), ("RightUpperArm", "upperarm_r"), ("RightLowerArm", "lowerarm_r"), ("RightHand", "hand_r"),
            ("LeftUpperLeg", "thigh_l"), ("LeftLowerLeg", "calf_l"), ("LeftFoot", "foot_l"), ("LeftToes", "ball_l"),
            ("RightUpperLeg", "thigh_r"), ("RightLowerLeg", "calf_r"), ("RightFoot", "foot_r"), ("RightToes", "ball_r"),
        };
        static readonly string[] UeFingers = { "thumb", "index", "middle", "ring", "pinky" };

        /// <summary>An Unreal Engine (UE5 / MetaHuman body) skeleton: Vindictus.</summary>
        public static bool IsUe(Transform root) => root.GetComponentsInChildren<Transform>(true).Any(t => t.name == "pelvis") &&
                                                   root.GetComponentsInChildren<Transform>(true).Any(t => t.name == "spine_01");

        static readonly string[] FingerNames = { "Thumb", "Index", "Middle", "Ring", "Little" };
        static readonly string[] FingerParts = { "Proximal", "Intermediate", "Distal" };

        /// <summary>human bone name -> transform, for the bones this rig has.</summary>
        public static Dictionary<string, Transform> MapBones(GameObject character)
        {
            var byName = new Dictionary<string, Transform>();
            foreach (var t in character.GetComponentsInChildren<Transform>(true))
                if (!byName.ContainsKey(t.name))
                    byName[t.name] = t;

            var map = new Dictionary<string, Transform>();
            bool doa = !byName.ContainsKey("Bip001 Pelvis") && byName.ContainsKey("bone_2");
            bool ue = !byName.ContainsKey("Bip001 Pelvis") && byName.ContainsKey("pelvis") && byName.ContainsKey("spine_01");
            foreach (var (human, bone) in doa ? Doa6Body : ue ? UeBody : Body)
                if (byName.TryGetValue(bone, out var t))
                    map[human] = t;
            if (doa)
                return map;
            if (ue)
            {
                foreach (var side in new[] { ("Left", "l"), ("Right", "r") })
                    for (int finger = 0; finger < 5; finger++)
                        for (int part = 0; part < 3; part++)
                            if (byName.TryGetValue($"{UeFingers[finger]}_0{part + 1}_{side.Item2}", out var f))
                                map[$"{side.Item1} {FingerNames[finger]} {FingerParts[part]}"] = f;
                return map;
            }
            foreach (var side in new[] { ("Left", "L"), ("Right", "R") })
            {
                for (int finger = 0; finger < 5; finger++)
                {
                    for (int part = 0; part < 3; part++)
                    {
                        // Biped: Finger0, Finger01, Finger02 = thumb; Finger1, Finger11, Finger12 = index; ...
                        string bone = $"Bip001 {side.Item2} Finger{finger}" + (part == 0 ? "" : part.ToString());
                        if (byName.TryGetValue(bone, out var t))
                            map[$"{side.Item1} {FingerNames[finger]} {FingerParts[part]}"] = t;
                    }
                }
            }
            return map;
        }

        static void Align(Transform bone, Transform child, Vector3 worldDirection)
        {
            if (bone == null || child == null)
                return;
            var current = child.position - bone.position;
            if (current.sqrMagnitude < 1e-10f)
                return;
            bone.rotation = Quaternion.FromToRotation(current, worldDirection) * bone.rotation;
        }

        /// <summary>
        /// Straighten arms, fingers and legs of the instance (which must be in its bind pose).
        /// Returns the largest correction in degrees, for the log.
        /// </summary>
        public static float EnforceTPose(GameObject character, Dictionary<string, Transform> map)
        {
            var forward = RoeShowcase.Forward(character);
            var right = Vector3.Cross(Vector3.up, forward).normalized;
            float worst = 0f;

            void Do(string bone, string child, Vector3 dir)
            {
                if (!map.TryGetValue(bone, out var b) || !map.TryGetValue(child, out var c))
                    return;
                var before = b.rotation;
                Align(b, c, dir);
                worst = Mathf.Max(worst, Quaternion.Angle(before, b.rotation));
            }

            foreach (var (side, dir) in new[] { ("Left", -right), ("Right", right) })
            {
                Do($"{side}UpperArm", $"{side}LowerArm", dir);
                Do($"{side}LowerArm", $"{side}Hand", dir);
                Do($"{side}Hand", $"{side} Middle Proximal", dir);
                foreach (var finger in new[] { "Index", "Middle", "Ring", "Little" })
                {
                    Do($"{side} {finger} Proximal", $"{side} {finger} Intermediate", dir);
                    Do($"{side} {finger} Intermediate", $"{side} {finger} Distal", dir);
                }
                Do($"{side}UpperLeg", $"{side}LowerLeg", Vector3.down);
                Do($"{side}LowerLeg", $"{side}Foot", Vector3.down);
            }
            return worst;
        }

        /// <summary>
        /// The feet of a model whose shoes have heels but whose bind pose stands flat (Vindictus: the MetaHuman body rests on
        /// flat feet, so PCF_005's heel tips stand 5 cm below the ball of her shoes): each foot turned toes-down about the
        /// ankle until the lowest point of the heel and the lowest point of the ball of the sole are level, the toes turned
        /// back flat - on the copy the avatar is built from, so that the "neutral" foot of the avatar stands in the shoe and
        /// flat-footed animation stands her on her heels (the ROE models' bind poses have that foot already).  Heel = the
        /// sole's lowest point from 40 % of the way to the toe bone backwards, ball = its lowest point between 60 % and the toe
        /// bone; nothing is done below minDegrees (flat shoes, bare feet).  Returns a note for the log.
        /// </summary>
        public static string StandOnHeels(GameObject posed, Dictionary<string, Transform> map, float minDegrees = 4f)
        {
            var points = RoeBoneCloth.SkinPoints(posed.transform);
            var notes = new List<string>();
            foreach (var side in new[] { "Left", "Right" })
            {
                if (!map.TryGetValue($"{side}Foot", out var foot) || !map.TryGetValue($"{side}Toes", out var toes))
                    continue;
                var fwd = toes.position - foot.position;
                fwd.y = 0f;
                float ballAlong = fwd.magnitude;
                if (ballAlong < 0.02f)
                    continue;
                fwd /= ballAlong;
                // the sole: skin on the foot, the toes and anything under them (toe bones)
                var sole = points.Where(p => p.bone == foot || p.bone.IsChildOf(foot))
                    .Select(p => (along: Vector3.Dot(p.p - foot.position, fwd), y: p.p.y)).ToList();
                var heel = sole.Where(s => s.along < 0.4f * ballAlong).DefaultIfEmpty((along: 0f, y: float.MaxValue)).OrderBy(s => s.y).First();
                var ball = sole.Where(s => s.along >= 0.6f * ballAlong && s.along <= ballAlong).DefaultIfEmpty((along: 0f, y: float.MaxValue)).OrderBy(s => s.y).First();
                if (heel.y == float.MaxValue || ball.y == float.MaxValue || ball.along - heel.along < 0.02f)
                    continue;
                float degrees = Mathf.Atan2(ball.y - heel.y, ball.along - heel.along) * Mathf.Rad2Deg;
                if (degrees < minDegrees)
                {
                    notes.Add($"{side} flat ({degrees:F1} deg)");
                    continue;
                }
                var right = Vector3.Cross(Vector3.up, fwd).normalized;
                var turn = Quaternion.AngleAxis(degrees, right);        // toes down
                var toesWorld = toes.rotation;
                foot.rotation = turn * foot.rotation;
                toes.rotation = toesWorld;                               // the toes as they were: flat on the floor
                notes.Add($"{side} {degrees:F1} deg (heel {100f * (ball.y - heel.y):F1} cm below the ball over {100f * (ball.along - heel.along):F1} cm)");
            }
            return string.Join(", ", notes);
        }

        /// <summary>
        /// Some outfits hang the thighs from the spine and the clavicles from the neck (the two
        /// 3ds Max Biped options).  A humanoid Animator keeps bone lengths fixed, so on such a rig
        /// the hip joints would swing with every bend of the spine.  Moving those bones under the
        /// pelvis / upper chest (same world pose, skinning unaffected) gives every outfit the
        /// hierarchy Unity expects.  Returns how many bones were moved.
        /// </summary>
        public static int NormalizeHierarchy(Dictionary<string, Transform> map)
        {
            int moved = 0;
            void Reparent(string child, string parent)
            {
                if (!map.TryGetValue(child, out var c) || !map.TryGetValue(parent, out var p) || c.parent == p)
                    return;
                c.SetParent(p, true);
                moved++;
            }
            Reparent("LeftUpperLeg", "Hips");
            Reparent("RightUpperLeg", "Hips");
            string chest = map.ContainsKey("UpperChest") ? "UpperChest" : "Chest";
            Reparent("LeftShoulder", chest);
            Reparent("RightShoulder", chest);
            return moved;
        }

        /// <summary>The nodes between the Animator root and the hips (Root, Bip001).</summary>
        public static List<Transform> AboveHips(GameObject character, Dictionary<string, Transform> map)
        {
            var result = new List<Transform>();
            for (var t = map["Hips"].parent; t != null && t != character.transform; t = t.parent)
                result.Add(t);
            return result;
        }

        /// <summary>
        /// Weapon-like branches: children of the nodes above the hips that carry skinned bones
        /// (a08's Point007_L / Point007_R, g04's Bip001 Prop1 with the fan below it).
        /// </summary>
        public static List<Transform> Attachments(GameObject character, Dictionary<string, Transform> map)
        {
            var skinned = new HashSet<Transform>(character.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .SelectMany(r => r.bones).Where(b => b != null));
            var above = AboveHips(character, map);
            var hips = map["Hips"];
            var result = new List<Transform>();
            foreach (var node in above)
                foreach (Transform child in node)
                    if (child != hips && !above.Contains(child) && child.GetComponentsInChildren<Transform>(true).Any(skinned.Contains))
                        result.Add(child);
            return result;
        }

        static readonly string[] HoldingBones = { "LeftHand", "RightHand", "Hips", "Spine", "Chest", "UpperChest", "Head" };

        /// <summary>
        /// Makes the fighter rig usable with animation that knows nothing about this outfit:
        ///   - weapons are parented to the bone that holds them in the outfit's own idle (in the
        ///     game they float in root space and every clip animates them to follow the hand);
        ///   - every other non-human bone takes its idle value as default (g04's fan is wide open
        ///     and several times its in-game size in the bind pose; the idle folds and scales it).
        /// </summary>
        static string ApplyIdleDefaults(RoeManifest.Character c, GameObject prefab, GameObject fighter, Dictionary<string, Transform> map)
        {
            var idle = c.LoadClip("idle_01");
            if (idle == null)
                return "no idle_01 clip: bind pose kept";
            var reference = Object.Instantiate(prefab);
            reference.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            RoeCapture.Pose(reference, idle, 0f);
            var refMap = MapBones(reference);
            var refByName = new Dictionary<string, Transform>();
            foreach (var t in reference.GetComponentsInChildren<Transform>(true))
                if (!refByName.ContainsKey(t.name))
                    refByName[t.name] = t;

            var human = new HashSet<Transform>(map.Values);
            var above = AboveHips(fighter, map);
            var attachments = Attachments(fighter, map);
            var notes = new List<string>();
            foreach (var att in attachments)
            {
                if (!refByName.TryGetValue(att.name, out var r))
                    continue;
                string best = null;
                float bestDistance = float.MaxValue;
                foreach (var candidate in HoldingBones)
                {
                    if (!refMap.TryGetValue(candidate, out var bone))
                        continue;
                    float d = Vector3.Distance(bone.position, r.position);
                    if (d < bestDistance)
                    {
                        bestDistance = d;
                        best = candidate;
                    }
                }
                if (best == null)
                    continue;
                var rel = refMap[best].worldToLocalMatrix * r.localToWorldMatrix;
                att.SetParent(map[best], false);
                att.localPosition = rel.GetColumn(3);
                att.localRotation = rel.rotation;
                att.localScale = rel.lossyScale;
                notes.Add($"{att.name} -> {map[best].name} ({bestDistance:F2} m away in the idle)");
            }

            int posed = 0;
            foreach (var t in fighter.GetComponentsInChildren<Transform>(true))
            {
                if (t == fighter.transform || human.Contains(t) || above.Contains(t) || attachments.Contains(t))
                    continue;
                if (!refByName.TryGetValue(t.name, out var r) || r.parent == null || r.parent.name != t.parent.name)
                    continue;
                t.localPosition = r.localPosition;
                t.localRotation = r.localRotation;
                t.localScale = r.localScale;
                posed++;
            }
            RoeCapture.EndPosing();
            Object.DestroyImmediate(reference);
            return $"{posed} extra bones at their idle values; held: {(notes.Count > 0 ? string.Join(", ", notes) : "nothing")}";
        }

        /// <summary>
        /// The twist distribution of an Unreal / MetaHuman body (Vindictus): the upper arm and thigh keep their roll, the
        /// forearm's and shin's go into the hand and foot - the MetaHuman forearm and shin do not roll (twist bones share the
        /// hand's and foot's roll, RoeUeRig).  Measured on Fiona's 90 game clips played back through her avatar: the forearm's
        /// rotation came back 5.8 degrees from the game's on average with 0 (32, at worst 179, with 1), the upper arm's 5.6
        /// with 1 (28 with 0); the same for thigh and shin (VdfAnims -roeTwist).
        /// </summary>
        static readonly float[] UeTwist = { 1f, 0f, 1f, 0f };

        /// <param name="twist">upper arm, forearm, thigh, shin twist distribution (null: UeTwist on an Unreal body, else 1, 1, 1, 1 - see below)</param>
        internal static Avatar BuildAvatar(GameObject posed, Dictionary<string, Transform> map, float[] twist = null)
        {
            if (twist == null && IsUe(posed.transform))
                twist = UeTwist;
            var human = new List<HumanBone>();
            foreach (var pair in map)
            {
                var hb = new HumanBone { humanName = pair.Key, boneName = pair.Value.name };
                hb.limit.useDefaultValues = true;
                human.Add(hb);
            }

            var skeleton = new List<SkeletonBone>();
            foreach (var t in posed.GetComponentsInChildren<Transform>(true))
            {
                skeleton.Add(new SkeletonBone
                {
                    name = t.name,
                    position = t.localPosition,
                    rotation = t.localRotation,
                    scale = t.localScale,
                });
            }

            var description = new HumanDescription
            {
                human = human.ToArray(),
                skeleton = skeleton.ToArray(),
                // Where the roll of a limb goes: 0 = into the limb bone itself (elbow, knee side),
                // 1 = into the next joint (wrist, ankle).  The game's rigs keep the roll out of the
                // limb bones and have separate twist helpers below them, so 1 reproduces the original
                // animation; with Unity's default 0.5 the helpers ended up 7-13 cm off, with 0 12-22 cm.
                upperArmTwist = twist != null ? twist[0] : 1f,
                lowerArmTwist = twist != null ? twist[1] : 1f,
                upperLegTwist = twist != null ? twist[2] : 1f,
                lowerLegTwist = twist != null ? twist[3] : 1f,
                armStretch = 0.05f,
                legStretch = 0.05f,
                feetSpacing = 0f,
                hasTranslationDoF = false,
            };
            return AvatarBuilder.BuildHumanAvatar(posed, description);
        }

        /// <summary>
        /// Builds the humanoid version of a character: &lt;id&gt;_fighter.prefab (normalized hierarchy,
        /// humanoid avatar on its Animator) and the avatar asset.  The Generic prefab stays as it is.
        /// </summary>
        public static GameObject BuildFighter(RoeManifest.Character c)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RoeFighterBuilder.PrefabPath(c.id));
            if (prefab == null)
                prefab = RoeFighterBuilder.Build(c);
            var go = Object.Instantiate(prefab);
            go.name = c.id + "_fighter";
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var goMap = MapBones(go);
            int moved = NormalizeHierarchy(goMap);
            string idleNote = ApplyIdleDefaults(c, prefab, go, goMap);

            // the avatar is defined on a T-posed copy; the prefab keeps the bind pose
            var posed = Object.Instantiate(go);
            posed.name = go.name;
            var posedMap = MapBones(posed);
            var forward = RoeShowcase.Forward(posed);
            float corrected = EnforceTPose(posed, posedMap);
            var avatar = BuildAvatar(posed, posedMap);
            avatar.name = c.id + "_humanoid";
            bool ok = avatar.isValid && avatar.isHuman;
            int nodes = posed.GetComponentsInChildren<Transform>(true).Length;
            Object.DestroyImmediate(posed);
            if (!ok)
            {
                Debug.LogError($"[ROE] {c.id}: humanoid avatar is NOT valid (valid={avatar.isValid}, human={avatar.isHuman})");
                Object.DestroyImmediate(go);
                return null;
            }

            string avatarPath = AvatarPath(c.id);
            Directory.CreateDirectory(Path.GetDirectoryName(avatarPath));
            AssetDatabase.DeleteAsset(avatarPath);
            AssetDatabase.CreateAsset(avatar, avatarPath);

            var animator = go.GetComponent<Animator>();
            animator.avatar = avatar;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var saved = PrefabUtility.SaveAsPrefabAsset(go, FighterPath(c.id));
            Object.DestroyImmediate(go);

            Debug.Log($"[ROE] {c.id}: fighter prefab {FighterPath(c.id)} + avatar {avatarPath}; {posedMap.Count} human bones, " +
                      $"{moved} bones re-parented, bind pose faces {forward}, largest T-pose correction {corrected:F1} deg, {nodes} nodes");
            Debug.Log($"[ROE] {c.id}: {idleNote}");
            return saved;
        }

        [MenuItem("ROE Fighter/Build/Humanoid fighters")]
        public static void BuildAll()
        {
            foreach (var c in RoeManifest.Load().Chosen())     // -roeChars b10,g05: only these
                BuildFighter(c);
            AssetDatabase.SaveAssets();
        }
    }
}
