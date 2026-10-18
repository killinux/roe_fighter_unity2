using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RoeFighter.EditorTools
{
    /// <summary>
    /// Plays one action of a unit through the game's own timeline in the studio and writes a few
    /// stills: the quick check that animation, effect prefabs and effect shaders work together.
    /// Batch mode: -executeMethod RoeFighter.EditorTools.RoeSkillTest.Run [-roeChar a08] [-roeAction skill1]
    ///             [-roeTimes 0.5,0.9,1.5] [-roeOut dir] [-roeYaw 55] [-roeDistance 7]
    /// The timeline is stepped at 60 fps from 0 to each time, as it would play, so particles are where they should be.
    /// </summary>
    public static class RoeSkillTest
    {
        [MenuItem("ROE Fighter/Check/Skill stills")]
        public static void Run()
        {
            string id = RoeCapture.Arg("-roeChar", "a08");
            string action = RoeCapture.Arg("-roeAction", "skill1");
            var times = RoeCapture.Arg("-roeTimes", "0.5,0.9,1.2,1.6,2.2,2.8,3.0,3.4").Split(',')
                .Select(t => float.Parse(t, CultureInfo.InvariantCulture)).OrderBy(t => t).ToArray();
            string outDir = RoeCapture.Arg("-roeOut", Path.Combine(Path.GetDirectoryName(Application.dataPath), "_work", "skilltest"));
            float yaw = float.Parse(RoeCapture.Arg("-roeYaw", "55"), CultureInfo.InvariantCulture);
            float distance = float.Parse(RoeCapture.Arg("-roeDistance", "7"), CultureInfo.InvariantCulture);

            ShaderUtil.allowAsyncCompilation = false;
            var studio = RoeStudio.Build();
            studio.cameraData.requiresColorTexture = true;
            var unit = RoeUnit.Create(RoeManifest.Load().Find(id));
            if (RoeCapture.Arg("-roeMirror", "0") == "1")
                unit.rig.transform.localScale = new Vector3(-1f, 1f, 1f);
            studio.LightFrom(unit.rig.transform.forward);

            const int fps = 60;
            int next = 0;
            int last = Mathf.RoundToInt(times[times.Length - 1] * fps);
            string warm = Path.Combine(outDir, "_warmup.png");
            for (int f = 0; f <= last; f++)
            {
                unit.Show(action, f / (float)fps);
                if (f == 0)
                {
                    var director = unit.directors[action];
                    var timeline = director.playableAsset as UnityEngine.Timeline.TimelineAsset;
                    Debug.Log($"[ROE] {action}: asset {director.playableAsset?.GetType().Name} '{director.playableAsset?.name}', graph valid {director.playableGraph.IsValid()}, " +
                              $"duration {director.duration:F2} s");
                    if (timeline != null)
                        foreach (var track in timeline.GetOutputTracks())
                            Debug.Log($"[ROE]   track {track.GetType().Name} '{track.name}' muted {track.muted}: " +
                                      string.Join("; ", track.GetClips().Select(c =>
                                      {
                                          var control = c.asset as UnityEngine.Timeline.ControlPlayableAsset;
                                          if (control == null)
                                              return $"{c.start:F2}-{c.end:F2} {c.asset?.GetType().Name}";
                                          var source = control.sourceGameObject.Resolve(director);
                                          return $"{c.start:F2}-{c.end:F2} prefab {(control.prefabGameObject ? control.prefabGameObject.name : "NULL")} " +
                                                 $"on {(source ? source.name : "NULL")} ({control.sourceGameObject.exposedName})";
                                      })));
                    foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>().Where(g => g.scene.IsValid() && g.name.Contains("[Timeline]")))
                        Debug.Log($"[ROE]   instance {RoeHumanoidClips.PathOf(go.transform, null)} active {go.activeSelf}");
                    studio.Aim(new Vector3(0f, 1.2f, 1.0f), unit.rig.transform.forward, yaw, 8f, distance, 34f);
                    RoeCapture.Render(studio.camera, 320, 180, warm);
                    RoeCapture.Render(studio.camera, 320, 180, warm);
                }
                if (next < times.Length && f == Mathf.RoundToInt(times[next] * fps))
                {
                    // what a timeline instantiates is flagged "don't save" and invisible to FindObjectsByType
                    var everything = Resources.FindObjectsOfTypeAll<ParticleSystem>()
                        .Where(p => p.gameObject.scene.IsValid() && p.gameObject.activeInHierarchy).ToArray();
                    int alive = everything.Sum(p => p.particleCount);
                    string shot = Path.Combine(outDir, $"{id}_{next + 1}_{action}_{times[next]:F2}");
                    RoeCapture.Render(studio.camera, 1280, 720, shot + ".png");
                    Debug.Log($"[ROE] {id} {action} at {times[next]:F2} s: {everything.Length} active particle systems, {alive} particles");
                    if (RoeCapture.Arg("-roeDump", "") == (next + 1).ToString())
                    {
                        foreach (var light in Resources.FindObjectsOfTypeAll<Light>().Where(l => l.gameObject.scene.IsValid() && l.isActiveAndEnabled))
                            Debug.Log($"[ROE]   light {RoeHumanoidClips.PathOf(light.transform, null)}: {light.type} {light.color} x{light.intensity} range {light.range}");
                        var renderers = unit.rig.GetComponentsInChildren<Renderer>(true)
                            .Where(r => r.gameObject.activeInHierarchy && r.enabled).ToArray();
                        Debug.Log($"[ROE]   under the rig: {unit.rig.GetComponentsInChildren<Transform>(true).Length} nodes, " +
                                  $"{unit.rig.GetComponentsInChildren<Transform>(true).Count(t => t.name.Contains("[Timeline]"))} made by the timeline, " +
                                  $"{unit.rig.GetComponentsInChildren<Renderer>(true).Length} renderers, {renderers.Length} active");
                        foreach (var r in renderers)
                        {
                            var ps = r.GetComponent<ParticleSystem>();
                            Debug.Log($"[ROE]   {r.GetType().Name} {RoeHumanoidClips.PathOf(r.transform, unit.rig.transform)}: " +
                                      $"{string.Join(" + ", r.sharedMaterials.Select(m => m == null ? "null" : $"{m.name} [{m.shader.name}] q{m.renderQueue}"))}" +
                                      (ps != null ? $", {ps.particleCount} particles" : "") + $", bounds {r.bounds.size}");
                        }
                        // the same moment without the character, and without the effects
                        var body = unit.model.GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToArray();
                        foreach (var r in body) r.enabled = false;
                        RoeCapture.Render(studio.camera, 1280, 720, shot + "_effects_only.png");
                        foreach (var r in body) r.enabled = true;
                        var fx = renderers.Where(r => !body.Contains(r) && r.gameObject != studio.floor).ToArray();
                        foreach (var r in fx) r.enabled = false;
                        RoeCapture.Render(studio.camera, 1280, 720, shot + "_no_effects.png");
                        foreach (var r in fx) r.enabled = true;
                    }
                    next++;
                }
            }
            // the pose the timeline gave the character against the pose of the clip sampled straight onto it
            if (RoeCapture.Arg("-roeCompare", "") != "")
            {
                float at = float.Parse(RoeCapture.Arg("-roeCompare", "0"), CultureInfo.InvariantCulture);
                unit.Show(action, at);
                var bones = unit.model.GetComponentsInChildren<Transform>(true);
                var byTimeline = bones.Select(b => unit.rig.transform.InverseTransformPoint(b.position)).ToArray();
                var localByTimeline = bones.Select(b => (b.localPosition, b.localRotation)).ToArray();
                var clipName = unit.sheet.Find(action).Body.clip;
                var clip = unit.info.LoadClip(clipName);
                RoeCapture.Pose(unit.model, clip, at);
                var worst = bones.Select((b, i) => (b, d: Vector3.Distance(unit.rig.transform.InverseTransformPoint(b.position), byTimeline[i]), i))
                    .OrderByDescending(x => x.d).ToArray();
                Debug.Log($"[ROE] {id} {action} at {at:F2} s, timeline pose against clip '{clipName}' sampled directly: " +
                          $"{worst.Count(x => x.d > 0.005f)} of {bones.Length} nodes differ by more than 5 mm, worst {worst[0].d * 100f:F1} cm");
                foreach (var x in worst.Take(25).Where(x => x.d > 0.005f))
                {
                    var (lp, lr) = localByTimeline[x.i];
                    Debug.Log($"[ROE]   {x.d * 100f,6:F1} cm  {RoeHumanoidClips.PathOf(x.b, unit.model.transform)}  " +
                              $"local pos timeline {lp} clip {x.b.localPosition}, rot differs {Quaternion.Angle(lr, x.b.localRotation):F1} deg");
                }
                RoeCapture.EndPosing();
            }
            unit.Stop();
            Debug.Log($"[ROE] skill stills written to {outDir}");
        }
    }
}
