using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;

namespace RoeFighter.EditorTools
{
    /// <summary>
    /// One fighter as the game stages it: the game's own unit prefab (gameplay_prefab_*) with its
    /// timelines - animation, effects, sounds per action - plus our high-detail character.
    ///   rig    the unit prefab instance.  Its timelines animate the low-detail model inside it
    ///          and switch the effects on and off; effect anchors hang on that model's bones.
    ///   ghost  that low-detail model.  Kept, animated, but not drawn.
    ///   model  our high-detail character (Generated/&lt;id&gt;/&lt;id&gt;.prefab), child of the rig; after
    ///          every evaluation it copies the ghost's pose bone by bone (same skeleton, same names).
    /// Editor only for now: actions are shown with Show(action, time), no play mode involved.
    /// </summary>
    public class RoeUnit
    {
        public RoeManifest.Character info;
        public RoeSkillSheet sheet;
        public GameObject rig, ghost, model;
        public Transform pivot;
        public readonly Dictionary<string, PlayableDirector> directors = new Dictionary<string, PlayableDirector>();
        readonly List<(Transform from, Transform to)> pose = new List<(Transform, Transform)>();
        PlayableDirector current;

        static readonly string[] CharacterShaders = { "ROE/Character", "ROE/Skin", "ROE/Hair", "ROE/Eye", "ROE/Eyebrow" };

        public static string RigPath(RoeSkillSheet sheet) =>
            $"Assets/ROE/skills/gameplay_prefab_{sheet.unit.ToLowerInvariant()}/{sheet.unit}.prefab";

        public static RoeUnit Create(RoeManifest.Character info)
        {
            var unit = new RoeUnit { info = info };
            unit.sheet = RoeSkillSheet.FromJson(File.ReadAllText(RoeSkillSheet.PathOf(info.id)));
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RigPath(unit.sheet));
            if (prefab == null)
                throw new FileNotFoundException("Run tools/import_skills.py for " + unit.sheet.unit, RigPath(unit.sheet));
            unit.rig = Object.Instantiate(prefab);
            unit.rig.name = unit.sheet.unit;
            int stripped = 0;
            foreach (var t in unit.rig.GetComponentsInChildren<Transform>(true))
                stripped += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
            foreach (var source in unit.rig.GetComponentsInChildren<AudioSource>(true))
                source.enabled = false;         // the videos get their sound from the skill sheet
            foreach (var listener in unit.rig.GetComponentsInChildren<AudioListener>(true))
                listener.enabled = false;
            foreach (var cam in unit.rig.GetComponentsInChildren<Camera>(true))
                cam.enabled = false;

            unit.pivot = unit.rig.transform.Find("transformPivot");
            var ghostTransform = string.IsNullOrEmpty(unit.sheet.model) ? null : unit.rig.transform.Find(unit.sheet.model);
            if (ghostTransform == null)
                throw new System.InvalidOperationException($"{unit.sheet.unit}: no model at '{unit.sheet.model}' in the unit prefab");
            unit.ghost = ghostTransform.gameObject;
            int hidden = 0, effects = 0;
            foreach (var r in unit.ghost.GetComponentsInChildren<Renderer>(true))
            {
                bool character = r.sharedMaterials.Any(m => m != null && m.shader != null && CharacterShaders.Contains(m.shader.name));
                if (character)
                {
                    r.enabled = false;
                    hidden++;
                }
                else
                {
                    effects++;
                }
            }
            foreach (var animator in unit.rig.GetComponentsInChildren<Animator>(true))
            {
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.applyRootMotion = false;
            }

            // our character, standing exactly where the ghost stands
            unit.model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(RoeFighterBuilder.PrefabPath(info.id)));
            unit.model.transform.SetParent(ghostTransform.parent, false);
            unit.model.transform.localPosition = ghostTransform.localPosition;
            unit.model.transform.localRotation = ghostTransform.localRotation;
            unit.model.transform.localScale = ghostTransform.localScale;
            var modelAnimator = unit.model.GetComponent<Animator>();
            if (modelAnimator != null)
                modelAnimator.enabled = false;      // posed by copying, nothing else may move it
            // Only bones that some clip animates are copied.  The two models share bone names, but the
            // rest values of bones no clip touches differ (the low-detail head sits elsewhere on its
            // neck); copying those would tear the high-detail head off.
            var animated = new HashSet<string>();
            foreach (var entry in info.clips)
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(entry.path);
                if (clip == null)
                    continue;
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                    if (binding.type == typeof(Transform))
                        animated.Add(binding.path);
            }
            var byPath = new Dictionary<string, Transform>();
            foreach (var t in unit.model.GetComponentsInChildren<Transform>(true))
                if (t != unit.model.transform)
                    byPath[RoeHumanoidClips.PathOf(t, unit.model.transform)] = t;
            int unmatched = 0, still = 0;
            foreach (var t in unit.ghost.GetComponentsInChildren<Transform>(true))
            {
                if (t == ghostTransform)
                    continue;
                string path = RoeHumanoidClips.PathOf(t, ghostTransform);
                if (!byPath.TryGetValue(path, out var target))
                    unmatched++;
                else if (animated.Contains(path))
                    unit.pose.Add((t, target));
                else
                    still++;
            }

            foreach (var d in unit.rig.GetComponentsInChildren<PlayableDirector>(true))
            {
                d.playOnAwake = false;
                d.timeUpdateMode = DirectorUpdateMode.Manual;
                unit.directors[d.gameObject.name.ToLowerInvariant()] = d;
            }
            unit.FreshSkinning();
            Debug.Log($"[ROE] unit {unit.sheet.unit}: {unit.directors.Count} timelines ({string.Join(", ", unit.directors.Keys.OrderBy(k => k))}), " +
                      $"{stripped} game scripts stripped, ghost: {hidden} character renderers hidden, {effects} effect renderers kept, " +
                      $"{unit.pose.Count} animated bones follow the ghost, {still} shared bones no clip animates are left alone, " +
                      $"{unmatched} ghost nodes have no counterpart (effect anchors)");
            return unit;
        }

        public bool Has(string action) => directors.ContainsKey(action);

        /// <summary>Show an action of the game ("skill1", "idle_01", "hurt", "die" ...) at a time in seconds.</summary>
        public void Show(string action, float time)
        {
            if (!directors.TryGetValue(action, out var director))
                return;
            if (current != director)
            {
                if (current != null)
                    current.Stop();
                current = director;
                director.RebuildGraph();
                FreshSkinning();    // the timeline may just have instantiated skinned effects (g04's tiger)
            }
            if (director.extrapolationMode == DirectorWrapMode.Loop && director.duration > 0.0)
                time = (float)(time % director.duration);
            director.time = time;
            director.Evaluate();
            CopyPose();
        }

        public void Stop()
        {
            if (current != null)
                current.Stop();
            current = null;
        }

        /// <summary>
        /// Unity works out the bone matrices of a skinned mesh once per frame.  Rendering a whole
        /// video inside one editor frame, posing by script in between, needs them redone at every
        /// render - otherwise a renderer keeps drawing an earlier pose (the head of one frame on
        /// the body of another).
        /// </summary>
        void FreshSkinning()
        {
            foreach (var r in rig.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                r.forceMatrixRecalculationPerRender = true;
        }

        void CopyPose()
        {
            foreach (var (from, to) in pose)
            {
                to.localPosition = from.localPosition;
                to.localRotation = from.localRotation;
                to.localScale = from.localScale;
            }
        }
    }
}
