using System.Collections.Generic;
using System.IO;
using System.Linq;
using RoeFighter.Fight;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RoeFighter.EditorTools
{
    /// <summary>
    /// The hit effects side by side (user 10-05: "普通攻击的效果不用luf的特效了，看有没有其他碰撞的特效"): each candidate where a
    /// strike lands - between the two fighters at chest height on the fight stage - filmed at a few moments after it starts, for
    /// tools/hitfx_sheet.py.  Candidates: the strikes' spark now (FightGame.hitEffect, a skill hit of g04's), the other hit effects
    /// the skills spawn, and UFE 2's hit particles (Light, Medium, Heavy, Block, Crumple, GroundBounce; tools/ufe_extract.py
    /// copies them out of the package into the gitignored Assets/UFE).
    ///   -executeMethod RoeFighter.EditorTools.RoeHitFx.Sheet [-roeOut dir] [-roeTimes 0.02,0.06,0.12,0.2,0.3,0.45]
    /// </summary>
    public static class RoeHitFx
    {
        public const string UfeFolder = "Assets/UFE/Demos/Shared_Assets/Particles";
        public static readonly string[] Ufe = { "Light", "Medium", "Heavy", "Block", "Crumple", "GroundBounce" };

        /// <summary>UFE 2's hit particles found in the project, by name ("ufe_light" ...).</summary>
        public static List<FightGame.NamedPrefab> UfePrefabs() =>
            Ufe.Select(n => (n, p: AssetDatabase.LoadAssetAtPath<GameObject>($"{UfeFolder}/{n}.prefab")))
               .Where(x => x.p != null)
               .Select(x => new FightGame.NamedPrefab { name = "ufe_" + x.n.ToLowerInvariant(), prefab = x.p }).ToList();

        /// <summary>
        /// UFE's particles in URP: their legacy additive / alpha-blended particle shaders draw as they are (no light mode: URP
        /// draws them unlit).  The grab-pass distortion of Heavy and Crumple drew black over the whole picture for a frame (and
        /// its normal map is not in the package): it takes the project's URP distortion shader at strength 0 - no heat haze.
        /// </summary>
        public static void Prepare()
        {
            var shader = Shader.Find("ROE/Particles/Distortion");
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { UfeFolder }))
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (mat == null || shader == null || mat.shader == null || mat.shader.name != "FX/Distortion")
                    continue;
                mat.shader = shader;
                mat.SetFloat("_DistortStrength", 0f);
                EditorUtility.SetDirty(mat);
                Debug.Log($"[ROE] UFE {mat.name}: grab-pass distortion -> ROE/Particles/Distortion at strength 0");
            }
            AssetDatabase.SaveAssets();
        }

        public static void Sheet()
        {
            Prepare();
            string stage = RoeCapture.Arg("-roeStage", "e23_steel_s02");
            string outDir = RoeCapture.Arg("-roeOut", Path.Combine(Path.GetDirectoryName(Application.dataPath), "_work", "hitfx"));
            var times = RoeCapture.Arg("-roeTimes", "0.02,0.06,0.12,0.2,0.3,0.45").Split(',')
                .Select(s => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            if (Directory.Exists(outDir))
                Directory.Delete(outDir, true);
            Directory.CreateDirectory(outDir);
            ShaderUtil.allowAsyncCompilation = false;
            EditorSceneManager.OpenScene(RoeFightScene.ScenePath(stage), OpenSceneMode.Single);
            var game = Object.FindFirstObjectByType<FightGame>();
            game.cpu = new[] { false, false };
            game.hud.gameObject.SetActive(false);
            game.Setup();
            while (game.phase != FightGame.Phase.Fight)
                game.Step(new FighterInput[2]);
            for (int i = 0; i < 20; i++)
                game.Step(new FighterInput[2]);
            // where a strike lands: between them, at chest height; the camera from the side, 2.6 m off
            var a = game.f[0].pos;
            var b = game.f[1].pos;
            var at = (a + b) * 0.5f + Vector3.up * 1.25f;
            var along = (b - a);
            along.y = 0f;
            var side = Vector3.Cross(Vector3.up, along.normalized);
            var cam = game.cam;
            cam.transform.position = at + side * 2.6f + Vector3.up * 0.15f;
            cam.transform.LookAt(at, Vector3.up);
            cam.fieldOfView = 40f;

            var candidates = new List<FightGame.NamedPrefab>();
            var now = game.effects.FirstOrDefault(e => e.name == game.hitEffect);
            if (now != null)
                candidates.Add(now);
            candidates.AddRange(game.effects.Where(e => e != now && e.name.ToLowerInvariant().Contains("hit")));
            candidates.AddRange(UfePrefabs());
            var lines = new List<string>();
            RoeCapture.Render(cam, 320, 240, Path.Combine(outDir, "_warm.jpg"), 80);
            foreach (var c in candidates)
            {
                var go = Object.Instantiate(c.prefab, at, Quaternion.LookRotation(cam.transform.position - at));
                go.hideFlags = HideFlags.DontSave;
                var roots = go.GetComponentsInChildren<ParticleSystem>(true)
                    .Where(p => p.transform.parent == null || p.transform.parent.GetComponentInParent<ParticleSystem>() == null).ToArray();
                var shaders = go.GetComponentsInChildren<ParticleSystemRenderer>(true).SelectMany(r => r.sharedMaterials)
                    .Where(m => m != null).Select(m => m.shader.name).Distinct();
                for (int k = 0; k < times.Length; k++)
                {
                    foreach (var p in roots)
                        p.Simulate(times[k], true, true);
                    RoeCapture.Render(cam, 480, 360, Path.Combine(outDir, $"{c.name}_{k}.jpg"), 88);
                }
                lines.Add($"{c.name}\t{roots.Length} systems\t{string.Join(", ", shaders)}");
                Object.DestroyImmediate(go);
            }
            File.WriteAllText(Path.Combine(outDir, "fx.txt"), string.Join("\n", lines) + "\n" + "times\t" + string.Join(",", times) + "\n");
            Debug.Log($"[ROE] hit effects: {candidates.Count} filmed to {outDir}\n[ROE]   " + string.Join("\n[ROE]   ", lines));
        }
    }
}
