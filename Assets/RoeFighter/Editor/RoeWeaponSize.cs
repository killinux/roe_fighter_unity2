using System.Globalization;
using System.IO;
using System.Linq;
using RoeFighter.Fight;
using UnityEditor;
using UnityEngine;

namespace RoeFighter.EditorTools
{
    /// <summary>
    /// A fighter with her weapon at several sizes, in her battle stance (user 10-03: "扇子缩小一点" - g04's fan is
    /// as tall as she is).  The fight scales the bone that carries the weapon's own bones (FighterRig.weaponScale,
    /// RoeFightScene -roeWeaponScale g04=0.7); these stills show the sizes side by side.
    ///   -executeMethod RoeFighter.EditorTools.RoeWeaponSize.Stills [-roeChars g04] [-roeScales 1,0.7,0.5] [-roeOut dir]
    /// </summary>
    public static class RoeWeaponSize
    {
        [MenuItem("ROE Fighter/Fight/Weapon size stills")]
        public static void Stills()
        {
            string id = RoeCapture.Arg("-roeChars", "g04").Split(',')[0];
            var scales = RoeCapture.Arg("-roeScales", "1,0.7,0.5").Split(',').Select(x => float.Parse(x, CultureInfo.InvariantCulture)).ToArray();
            string outDir = RoeCapture.Arg("-roeOut", Path.Combine(Path.GetDirectoryName(Application.dataPath), "out", "weapon_size"));
            Directory.CreateDirectory(outDir);
            ShaderUtil.allowAsyncCompilation = false;
            var studio = RoeStudio.Build();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RoeHumanoid.FighterPath(id));
            var stance = AssetDatabase.LoadAssetAtPath<AnimationClip>(RoeHumanoidClips.ClipPath(id, "idle_01"));
            for (int k = 0; k < scales.Length; k++)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                var animator = go.GetComponent<Animator>();
                if (stance != null)
                    RoeCapture.Pose(go, stance, 0f);
                var weapons = go.GetComponentsInChildren<Renderer>(true).Where(r => r.sharedMaterials.Any(m => m != null && m.name.StartsWith("wp_"))).ToArray();
                var roots = FighterRig.WeaponRoots(animator, weapons);
                foreach (var t in roots)
                    t.localScale *= scales[k];
                foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    smr.forceMatrixRecalculationPerRender = true;
                var forward = RoeShowcase.Forward(go);
                studio.LightFrom(forward);
                studio.SetFocus(0f, 0f, 0f);
                studio.Aim(new Vector3(0f, 0.85f, 0f), forward, 25f, 6f, 5.2f, 30f);
                if (k == 0)
                {
                    RoeCapture.Render(studio.camera, 200, 200, Path.Combine(outDir, "_warm.png"));
                    RoeCapture.Render(studio.camera, 200, 200, Path.Combine(outDir, "_warm.png"));
                }
                RoeCapture.Render(studio.camera, 800, 1000, Path.Combine(outDir, $"{id}_weapon_{scales[k].ToString("0.##", CultureInfo.InvariantCulture)}.png"));
                Debug.Log($"[ROE] weapon size {id} x{scales[k]}: scaled {string.Join(", ", roots.Select(t => t.name))}");
                RoeCapture.EndPosing();
                Object.DestroyImmediate(go);
            }
            Debug.Log($"[ROE] weapon size stills in {outDir}");
        }
    }
}
