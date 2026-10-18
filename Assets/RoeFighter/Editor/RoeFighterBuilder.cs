using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RoeFighter.EditorTools
{
    /// <summary>
    /// Builds one clean character prefab per ROE outfit from what the game ships:
    ///   - the HD model prefab (skeleton, skinned meshes, Generic avatar; placeholder materials),
    ///   - the real materials, read per renderer from the game's "meta" showcase prefab,
    ///   - weapons that the HD prefab lacks, taken from the battle (LD) prefab, which has them
    ///     skinned to the same bone names.
    /// Result: Assets/RoeFighter/Generated/&lt;id&gt;/&lt;id&gt;.prefab
    /// Batch mode: -executeMethod RoeFighter.EditorTools.RoeFighterBuilder.BuildAll
    /// </summary>
    public static class RoeFighterBuilder
    {
        public const string OutDir = "Assets/RoeFighter/Generated";

        public static string PrefabPath(string id) => $"{OutDir}/{id}/{id}.prefab";

        [MenuItem("ROE Fighter/Build/Character prefabs")]
        public static void BuildAll()
        {
            var manifest = RoeManifest.Load();
            foreach (var c in manifest.Chosen())     // -roeChars b10,g05: only these
                Build(c);
            AssetDatabase.SaveAssets();
        }

        public static GameObject Build(RoeManifest.Character c)
        {
            var hd = AssetDatabase.LoadAssetAtPath<GameObject>(c.hdPrefab);
            if (hd == null)
            {
                Debug.LogError($"[ROE] {c.id}: HD prefab not found at {c.hdPrefab}");
                return null;
            }
            var go = Object.Instantiate(hd);
            go.name = c.id;
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            int assigned = AssignMaterials(c, go);
            int weapons = AddMissingWeapons(c, go);
            SetPupils(c, go);

            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                // skill clips carry the body metres away from the prefab root
                smr.updateWhenOffscreen = true;
                smr.skinnedMotionVectors = true;
                smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                smr.receiveShadows = true;
            }

            var animator = go.GetComponent<Animator>();
            if (animator != null)
            {
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }

            Directory.CreateDirectory($"{OutDir}/{c.id}");
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, PrefabPath(c.id));
            Object.DestroyImmediate(go);

            var renderers = prefab.GetComponentsInChildren<Renderer>(true);
            var placeholders = renderers.SelectMany(r => r.sharedMaterials)
                .Count(m => m == null || m.name.StartsWith("default_material"));
            Debug.Log($"[ROE] built {PrefabPath(c.id)}: {renderers.Length} renderers, {assigned} material slots from the game, " +
                      $"{weapons} weapon meshes added, {placeholders} slots still on a placeholder, " +
                      $"{prefab.GetComponentsInChildren<Transform>(true).Length} transforms");
            return prefab;
        }

        /// <summary>
        /// The eye materials ship with _PupilSizeScale 0.56 (a08) and 0.76 (g04).  With the game's
        /// eye shader that makes the pupil 45-60 % of the visible iris; the game's own renders of
        /// these characters (the gacha videos) show the size that _PupilSizeScale 0 gives, so the
        /// game evidently sets it at run time.  Ours: the eye materials are set to 0.
        /// </summary>
        static void SetPupils(RoeManifest.Character c, GameObject go)
        {
            foreach (var m in go.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Distinct())
            {
                if (m == null || m.shader == null || m.shader.name != "ROE/Eye" || !m.HasProperty("_PupilSizeScale"))
                    continue;
                float before = m.GetFloat("_PupilSizeScale");
                if (before == PupilSizeScale)
                    continue;
                m.SetFloat("_PupilSizeScale", PupilSizeScale);
                EditorUtility.SetDirty(m);
                Debug.Log($"[ROE] {c.id}: {m.name} _PupilSizeScale {before} -> {PupilSizeScale} (pupils as in the game's own renders)");
            }
        }

        const float PupilSizeScale = 0f;

        static int AssignMaterials(RoeManifest.Character c, GameObject go)
        {
            var meta = string.IsNullOrEmpty(c.metaPrefab) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(c.metaPrefab);
            var source = new Dictionary<string, Renderer>();
            if (meta != null)
                foreach (var r in meta.GetComponentsInChildren<Renderer>(true))
                    if (!source.ContainsKey(r.name))
                        source[r.name] = r;

            int assigned = 0;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (source.TryGetValue(r.name, out var src) && src.sharedMaterials.Length == r.sharedMaterials.Length)
                {
                    r.sharedMaterials = src.sharedMaterials;
                    assigned += r.sharedMaterials.Length;
                }
                else if (r.name.StartsWith("wp_"))
                {
                    var mat = FindWeaponMaterial(c, r.name);
                    if (mat != null)
                    {
                        r.sharedMaterials = Enumerable.Repeat(mat, r.sharedMaterials.Length).ToArray();
                        assigned += r.sharedMaterials.Length;
                    }
                }
                else
                {
                    Debug.LogWarning($"[ROE] {c.id}: no material source for renderer '{r.name}'");
                }
            }
            return assigned;
        }

        static int AddMissingWeapons(RoeManifest.Character c, GameObject go)
        {
            if (string.IsNullOrEmpty(c.ldPrefab))
                return 0;
            var ld = AssetDatabase.LoadAssetAtPath<GameObject>(c.ldPrefab);
            if (ld == null)
                return 0;

            var have = new HashSet<string>(go.GetComponentsInChildren<Renderer>(true).Select(r => r.name));
            var bones = new Dictionary<string, Transform>();
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                if (!bones.ContainsKey(t.name))
                    bones[t.name] = t;

            int added = 0;
            foreach (var src in ld.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!src.name.StartsWith("wp_") || have.Contains(src.name) || src.sharedMesh == null)
                    continue;
                var mapped = new Transform[src.bones.Length];
                bool ok = true;
                for (int i = 0; i < mapped.Length; i++)
                {
                    if (src.bones[i] != null && !bones.ContainsKey(src.bones[i].name))
                        GraftBone(c, src.bones[i], bones);
                    if (src.bones[i] == null || !bones.TryGetValue(src.bones[i].name, out mapped[i]))
                    {
                        Debug.LogWarning($"[ROE] {c.id}: weapon '{src.name}' needs bone '{(src.bones[i] ? src.bones[i].name : "null")}' which the HD rig lacks");
                        ok = false;
                        break;
                    }
                }
                if (!ok)
                    continue;

                var child = new GameObject(src.name);
                child.transform.SetParent(go.transform, false);
                var smr = child.AddComponent<SkinnedMeshRenderer>();
                smr.sharedMesh = src.sharedMesh;
                smr.bones = mapped;
                if (src.rootBone != null && bones.TryGetValue(src.rootBone.name, out var root))
                    smr.rootBone = root;
                smr.localBounds = src.localBounds;
                var mat = FindWeaponMaterial(c, src.name);
                smr.sharedMaterials = Enumerable.Repeat(mat, src.sharedMaterials.Length).ToArray();
                added++;
            }
            return added;
        }

        /// <summary>
        /// A weapon's bone the HD rig lacks (m02's floating eyes hang on Wp_eyes_01..06 and their children, bones only the
        /// battle LD rig has; the game's clips key them all, idle included): the LD bone and its LD parents up to one the HD
        /// rig has are copied under that parent with their LD local pose, so the weapon is skinned and the clips drive it.
        /// </summary>
        static void GraftBone(RoeManifest.Character c, Transform ldBone, Dictionary<string, Transform> bones)
        {
            var chain = new List<Transform>();
            var p = ldBone;
            while (p != null && !bones.ContainsKey(p.name))
            {
                chain.Add(p);
                p = p.parent;
            }
            if (p == null)
                return;
            var parent = bones[p.name];
            for (int i = chain.Count - 1; i >= 0; i--)
            {
                var b = new GameObject(chain[i].name).transform;
                b.SetParent(parent, false);
                b.localPosition = chain[i].localPosition;
                b.localRotation = chain[i].localRotation;
                b.localScale = chain[i].localScale;
                bones[b.name] = b;
                parent = b;
                Debug.Log($"[ROE] {c.id}: bone '{b.name}' copied from the LD rig under '{b.parent.name}'");
            }
        }

        /// <summary>
        /// wp_a08_l (outfit a08) -> material "wp_a_08_hd".  A suit with several weapons has one material each, named
        /// after the weapon: b10's wp_ax -> "wp_b_10_axe_hd" (the renderer's word starts the material's).  A recolour
        /// (tools/roe_outfit.py) is named as the game names it, b14_outfit1 -> "wp_b_14_outfit1"; its weapon has only the
        /// battle model's material, "wp_b_14_outfit1_ld": with no _hd one, the _ld one.
        /// </summary>
        static Material FindWeaponMaterial(RoeManifest.Character c, string rendererName)
        {
            string prefix = $"wp_{c.Family}_{c.id.Substring(1)}";
            var all = AssetDatabase.FindAssets(prefix + " t:Material", new[] { "Assets/ROE" })
                .Select(g => AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(m => m != null && m.name.StartsWith(prefix)).ToList();
            string end = all.Any(m => m.name.EndsWith("_hd")) ? "_hd" : "_ld";
            var found = all.Where(m => m.name.EndsWith(end)).ToList();
            var exact = found.FirstOrDefault(m => m.name == prefix + end);
            if (exact != null)
                return exact;
            var words = rendererName.Split('_').Skip(1).Where(w => w.Length > 1).Select(w => w.ToLowerInvariant()).ToList();
            var named = found.FirstOrDefault(m => words.Any(w => m.name.Substring(prefix.Length).TrimStart('_').StartsWith(w)));
            if (named != null)
                return named;
            Debug.LogWarning($"[ROE] {c.id}: weapon material '{prefix}_hd' (or '{prefix}_<weapon>_hd') not found for '{rendererName}'");
            return null;
        }
    }
}
