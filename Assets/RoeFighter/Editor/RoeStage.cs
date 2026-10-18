using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace RoeFighter.EditorTools
{
    /// <summary>
    /// The game's own battle stages (imported by tools/import_stage.py into Assets/ROE/stages).
    ///   Open():   loads the stage scene and makes it usable here - game scripts that do not exist
    ///             in this project are stripped, the game's cameras are switched off, renderers
    ///             whose shader we have no replacement for (particles, decals) are hidden.
    ///   Stills(): the two fighters face each other on the stage, a few camera angles.
    /// Batch mode: -executeMethod RoeFighter.EditorTools.RoeStage.Stills [-roeStage s01] [-roeOut dir]
    /// </summary>
    public static class RoeStage
    {
        public static string ScenePath(string stage)
        {
            var found = Directory.GetFiles($"Assets/ROE/stages/{stage}", "*.unity", SearchOption.AllDirectories);
            if (found.Length == 0)
                throw new FileNotFoundException($"No scene under Assets/ROE/stages/{stage}; run tools/import_stage.py {stage}");
            return found[0].Replace('\\', '/');
        }

        public class Info
        {
            public Bounds bounds;
            public List<Transform> formation = new List<Transform>();
            public Light sun;
            public Vector3 gameCameraPosition;
            public bool hasGameCamera;
            public string report;
        }

        public static Info Open(string stage)
        {
            var scene = EditorSceneManager.OpenScene(ScenePath(stage), OpenSceneMode.Single);
            var info = new Info();
            var sb = new StringBuilder();
            int stripped = 0, hidden = 0, shown = 0;

            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    stripped += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
            }
            foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!info.hasGameCamera)
                {
                    info.gameCameraPosition = cam.transform.position;   // tells us from which side the game films the fight
                    info.hasGameCamera = true;
                    sb.Append($" gameCamera[{cam.name} pos {cam.transform.position} rot {cam.transform.rotation.eulerAngles} fov {cam.fieldOfView}]");
                }
                cam.gameObject.SetActive(false);
            }
            foreach (var listener in Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                listener.enabled = false;

            bool first = true;
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                bool ok = r.sharedMaterials.Length > 0 && r.sharedMaterials.All(m => m != null && m.shader != null && m.shader.isSupported
                                                                                    && !m.shader.name.StartsWith("Hidden/")
                                                                                    && !m.shader.name.StartsWith("Pinkcore/"));
                if (!ok)
                {
                    r.enabled = false;
                    hidden++;
                    continue;
                }
                shown++;
                if (r is MeshRenderer)
                {
                    if (first) { info.bounds = r.bounds; first = false; }
                    else info.bounds.Encapsulate(r.bounds);
                }
            }

            foreach (var light in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                sb.Append($" light[{light.name} {light.type} i={light.intensity:F2} shadows={light.shadows} bake={light.lightmapBakeType}]");
                if (light.type == LightType.Directional && (info.sun == null || light.intensity > info.sun.intensity))
                    info.sun = light;
            }

            var formation = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true))
                .FirstOrDefault(t => t.name == "FormationSetting");
            if (formation != null)
                info.formation = formation.GetComponentsInChildren<Transform>(true).Where(t => t != formation).ToList();

            info.report = $"stage {stage}: {shown} renderers shown, {hidden} hidden (no shader), {stripped} game scripts stripped, " +
                          $"bounds {info.bounds.min} .. {info.bounds.max}, lightmaps {LightmapSettings.lightmaps.Length}, " +
                          $"skybox {(RenderSettings.skybox ? RenderSettings.skybox.shader.name : "none")}, fog {RenderSettings.fog} " +
                          $"({RenderSettings.fogMode}, {RenderSettings.fogDensity}), ambient {RenderSettings.ambientMode}, " +
                          $"formation nodes {info.formation.Count};{sb}";
            return info;
        }

        [MenuItem("ROE Fighter/Check/Stage report")]
        public static void Report()
        {
            string stage = RoeCapture.Arg("-roeStage", "s01");
            var info = Open(stage);
            Debug.Log("[ROE] " + info.report);
            var sb = new StringBuilder("[ROE] formation nodes:");
            foreach (var t in info.formation.Take(60))
                sb.Append($"\n[ROE]   {RoeHumanoidClips.PathOf(t, null)} pos {t.position} rot {t.rotation.eulerAngles}");
            Debug.Log(sb.ToString());
        }

        /// <summary>
        /// Ray tests against the stage meshes.  The stages come without colliders, so every visible
        /// mesh gets a temporary mesh collider (the scene is never saved; Dispose removes them again).
        /// </summary>
        public sealed class Sight : System.IDisposable
        {
            readonly List<MeshCollider> added = new List<MeshCollider>();
            readonly HashSet<Collider> stage = new HashSet<Collider>();
            readonly HashSet<Collider> solid = new HashSet<Collider>();
            readonly RaycastHit[] hits = new RaycastHit[64];
            readonly Collider[] overlaps = new Collider[64];
            readonly bool backfaces;

            /// <param name="exclude">Renderers under these are not stage (the fighters, their weapons).</param>
            public Sight(ICollection<Transform> exclude = null)
            {
                foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                {
                    if (!r.enabled)
                        continue;
                    if (exclude != null && exclude.Any(t => t != null && r.transform.IsChildOf(t)))
                        continue;
                    var filter = r.GetComponent<MeshFilter>();
                    if (filter == null || filter.sharedMesh == null)
                        continue;
                    var collider = r.GetComponent<MeshCollider>();
                    if (collider == null)
                    {
                        collider = r.gameObject.AddComponent<MeshCollider>();
                        added.Add(collider);
                    }
                    if (collider.sharedMesh == null)
                        collider.sharedMesh = filter.sharedMesh;
                    stage.Add(collider);
                    // what one cannot see through: drawn opaque or cut out (glass, light shafts and fog cards are drawn see-through)
                    if (r.sharedMaterials.Any(m => m != null && m.renderQueue <= (int)RenderQueue.GeometryLast))
                        solid.Add(collider);
                }
                backfaces = Physics.queriesHitBackfaces;
                Physics.queriesHitBackfaces = true;     // a prop seen from behind hides a fighter just as well
                Physics.SyncTransforms();
            }

            public int Count => stage.Count;
            public int SeeThrough => stage.Count - solid.Count;

            /// <summary>Does solid stage geometry pass through the box (its surface: a box wholly inside a thick wall is not seen)?</summary>
            public bool Occupied(Vector3 centre, Vector3 halfExtents)
            {
                int n = Physics.OverlapBoxNonAlloc(centre, halfExtents, overlaps, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                    if (solid.Contains(overlaps[i]))
                        return true;
                return false;
            }

            /// <summary>Is there stage geometry between the two points?</summary>
            public bool Blocked(Vector3 from, Vector3 to, float slack = 0.05f) => Blocker(from, to, out _, slack) != null;

            /// <summary>The first piece of stage geometry between the two points and where the line meets it (null: none).</summary>
            public Collider Blocker(Vector3 from, Vector3 to, out Vector3 point, float slack = 0.05f)
            {
                point = default;
                var d = to - from;
                float length = d.magnitude - slack;
                if (length <= 0f)
                    return null;
                int n = Physics.RaycastNonAlloc(from, d.normalized, hits, length, ~0, QueryTriggerInteraction.Ignore);
                Collider first = null;
                float nearest = float.MaxValue;
                for (int i = 0; i < n; i++)
                    if (stage.Contains(hits[i].collider) && hits[i].distance < nearest)
                    {
                        nearest = hits[i].distance;
                        first = hits[i].collider;
                        point = hits[i].point;
                    }
                return first;
            }

            /// <summary>The highest stage surface under a point, looking down from <paramref name="above"/> metres over it.</summary>
            public bool Ground(Vector3 point, float above, out float y)
            {
                y = 0f;
                int n = Physics.RaycastNonAlloc(point + Vector3.up * above, Vector3.down, hits, above + 3f, ~0, QueryTriggerInteraction.Ignore);
                bool found = false;
                for (int i = 0; i < n; i++)
                    if (stage.Contains(hits[i].collider) && (!found || hits[i].point.y > y))
                    {
                        y = hits[i].point.y;
                        found = true;
                    }
                return found;
            }

            public void Dispose()
            {
                Physics.queriesHitBackfaces = backfaces;
                foreach (var c in added)
                    if (c != null)
                        Object.DestroyImmediate(c);
                added.Clear();
                stage.Clear();
            }
        }

        /// <summary>Where the fight takes place on a stage and from where it is filmed.</summary>
        public class Layout
        {
            public Vector3 centre;      // on the ground, between the fighters
            public Vector3 axis;        // unit vector from fighter 1 to fighter 2 (= screen right)
            public Vector3 normal;      // unit vector from the fight line towards the camera
            public float halfLength;    // the fight line is free this far either way from the centre
            public float blocked;       // share of the tested sight lines that hit a stage prop
            public float floor;         // share of the fight area that is not flat, free floor
            public float angle;         // degrees the view is turned away from the game's own view direction
            public Vector2 shift;       // metres the centre is moved away from the game's battle centre (sideways, along the view)
            public float score;

            public override string ToString() =>
                $"centre {centre}, fight axis {axis}, camera side {normal}, fight line {halfLength * 2f:F1} m, view turned {angle:F0} deg, " +
                $"moved {shift.magnitude:F2} m, sight lines blocked {blocked * 100f:F1}%, floor not free {floor * 100f:F1}%, score {score:F3}";
        }

        /// <summary>
        /// How good is a fight line of <paramref name="halfLength"/> metres either way from
        /// <paramref name="centre"/>, filmed by a camera that looks along <paramref name="view"/>?
        /// </summary>
        static Layout Rate(Sight sight, Vector3 centre, Vector3 view, float halfLength)
        {
            var layout = new Layout { centre = centre, axis = Vector3.Cross(Vector3.up, view).normalized, normal = -view, halfLength = halfLength };
            // the floor the fighters use: the fight line and 1.2 m to both sides of it, flat and with
            // nothing standing on it (a ray from 2.6 m up lands on a prop first)
            int bad = 0, count = 0;
            int steps = Mathf.CeilToInt(halfLength / 0.5f);
            for (int i = -steps; i <= steps; i++)
                for (float w = -1.2f; w <= 1.21f; w += 0.6f)
                {
                    count++;
                    var p = centre + layout.axis * (halfLength * i / steps) + layout.normal * w;
                    if (!sight.Ground(p, 2.6f, out float y) || Mathf.Abs(y - centre.y) > 0.12f)
                        bad++;
                }
            layout.floor = bad / (float)count;
            // what the camera has to see: it follows the fighters along the line (m) and backs off when
            // they are far apart or jump (d); from every such spot the whole fight volume must be visible
            int hit = 0, rays = 0;
            foreach (float d in new[] { 6f, 8f, 10.5f, 13f })
                foreach (float m in new[] { -0.5f, 0f, 0.5f })
                {
                    var eye = centre + layout.axis * (m * halfLength) + layout.normal * d + Vector3.up * (1.2f + d * 0.05f);
                    foreach (float u in new[] { -1f, -0.5f, 0f, 0.5f, 1f })
                        foreach (float h in new[] { 0.2f, 1f, 1.8f, 2.8f })
                        {
                            rays++;
                            if (sight.Blocked(eye, centre + layout.axis * (u * halfLength) + Vector3.up * h))
                                hit++;
                        }
                }
            layout.blocked = hit / (float)rays;
            return layout;
        }

        /// <summary>
        /// The game's battle layout (FormationSetting) says where its two sides stand; its camera
        /// looks from behind side 1 towards side 2, and that is the view the stage is dressed for.
        /// A fighting-game camera needs more: a free strip of floor seen from the side with nothing
        /// between camera and fighters.  So every spot near the game's battle centre and every view
        /// direction is rated with real ray tests against the stage meshes, first for a long fight
        /// line, then for shorter ones, until some spot is completely free; among the free ones the
        /// one closest to the game's own centre and view wins.
        /// </summary>
        public static Layout FindLayout(Info info, Sight sight, bool log = true)
        {
            Layout best = null;
            foreach (float halfLength in new[] { 6f, 5f, 4.25f, 3.5f, 3f, 2.6f })
            {
                var found = FindLayout(info, sight, halfLength, log);
                if (best == null || found.score < best.score - 1e-4f)
                    best = found;
                if (found.blocked == 0f && found.floor == 0f)
                    return found;
            }
            Debug.LogWarning($"[ROE] no completely free fight line on this stage; the least bad one: {best}");
            return best;
        }

        static Layout FindLayout(Info info, Sight sight, float halfLength, bool log)
        {
            var home = new Vector3(info.bounds.center.x, info.bounds.min.y, info.bounds.center.z);
            var gameView = Vector3.forward;
            var centreNode = info.formation.FirstOrDefault(t => t.name == "Center");
            var p1 = info.formation.FirstOrDefault(t => t.name == "Anchor1" && t.parent.name == "1" && t.parent.parent.name == "Player1");
            var p2 = info.formation.FirstOrDefault(t => t.name == "Anchor1" && t.parent.name == "1" && t.parent.parent.name == "Player2");
            if (centreNode != null)
                home = centreNode.position;
            if (p1 != null && p2 != null && (p2.position - p1.position).sqrMagnitude > 0.01f)
            {
                gameView = Vector3.ProjectOnPlane(p2.position - p1.position, Vector3.up).normalized;
                if (centreNode == null)
                    home = (p1.position + p2.position) * 0.5f;
            }
            if (sight.Ground(home, 2.6f, out float groundY) && Mathf.Abs(groundY - home.y) < 0.3f)
                home.y = groundY;       // stand on the floor that is really there

            var side = Vector3.Cross(Vector3.up, gameView).normalized;
            var all = new List<Layout>();
            for (int step = 0; step < 24; step++)
            {
                float angle = step * 15f;
                if (angle > 180f)
                    angle -= 360f;
                var view = Quaternion.AngleAxis(angle, Vector3.up) * gameView;
                for (float dx = -3f; dx <= 3.01f; dx += 0.75f)
                    for (float dz = -6f; dz <= 6.01f; dz += 0.75f)
                    {
                        var layout = Rate(sight, home + side * dx + gameView * dz, view, halfLength);
                        layout.angle = angle;
                        layout.shift = new Vector2(dx, dz);
                        // an occluded fighter or a prop in the ring is not acceptable, a short walk is cheap;
                        // the game itself turns its camera 32.5 degrees either way, so that much costs next to nothing
                        layout.score = layout.blocked * 30f + layout.floor * 30f + layout.shift.magnitude * 0.03f
                                       + Mathf.Max(0f, Mathf.Abs(angle) - 30f) * 0.004f + Mathf.Abs(angle) * 0.0005f;
                        all.Add(layout);
                    }
            }
            all.Sort((a, b) => a.score.CompareTo(b.score));
            if (log)
            {
                Debug.Log($"[ROE] layout search, fight line {halfLength * 2f:F1} m: {sight.Count} stage meshes, {all.Count} candidates around {home}, " +
                          $"game view {gameView}, {all.Count(l => l.blocked == 0f && l.floor == 0f)} completely free");
                foreach (var l in all.Take(3))
                    Debug.Log($"[ROE]   {l}");
                var own = all.First(l => l.angle == 90f && l.shift == Vector2.zero);
                Debug.Log($"[ROE]   the game's own line, filmed from the side: {own}");
            }
            return all[0];
        }

        public static Transform Bone(GameObject root, params string[] names)
        {
            var all = root.GetComponentsInChildren<Transform>(true);
            foreach (var n in names)
            {
                var t = all.FirstOrDefault(x => string.Equals(x.name, n, System.StringComparison.OrdinalIgnoreCase));
                if (t != null)
                    return t;
            }
            return null;
        }

        /// <summary>Which way a left/right pair of bones faces: at right angles to the line between them.</summary>
        public static Vector3 FacingOf(Transform left, Transform right)
        {
            if (left == null || right == null)
                return Vector3.zero;
            var across = Vector3.ProjectOnPlane(right.position - left.position, Vector3.up);
            return across.sqrMagnitude > 1e-8f ? Vector3.Cross(across.normalized, Vector3.up) : Vector3.zero;
        }

        /// <summary>Where the face looks (from the eyeball bones), horizontally.</summary>
        public static Vector3 FaceForward(GameObject character)
        {
            return FacingOf(Bone(character, "Bip001 Eyeball_L"), Bone(character, "Bip001 Eyeball_R"));
        }

        static string Facing(GameObject character)
        {
            var root = character.transform;
            string Yaw(Vector3 v) => v == Vector3.zero ? "?" : Vector3.SignedAngle(root.forward, v, Vector3.up).ToString("F0");
            return $"feet {Yaw(RoeShowcase.Forward(character))}, hips {Yaw(FacingOf(Bone(character, "Bip001 L Thigh"), Bone(character, "Bip001 R Thigh")))}, " +
                   $"chest {Yaw(FacingOf(Bone(character, "Bip001 L UpperArm"), Bone(character, "Bip001 R UpperArm")))}, " +
                   $"face {Yaw(FaceForward(character))}";
        }

        /// <summary>
        /// Puts a fighter (already posed in its idle) on its spot.  The game's clips are made for a
        /// root that looks straight at the enemy, so the root is aimed at the opponent - NOT the
        /// feet: the idle stances stand sideways, hips turned 20 to 40 degrees.  That turn opens the
        /// body towards the camera on one side of the screen only; on the other side the fighter is
        /// mirrored, the way fighting games show both players from the front.
        /// </summary>
        public static void Place(GameObject fighter, Vector3 position, Vector3 towardsOpponent, Vector3 towardsCamera)
        {
            var t = fighter.transform;
            t.localScale = Vector3.one;
            t.position = position;
            t.rotation = Quaternion.LookRotation(towardsOpponent, Vector3.up);
            var hips = FacingOf(Bone(fighter, "Bip001 L Thigh"), Bone(fighter, "Bip001 R Thigh"));
            bool turnedAway = Vector3.Dot(hips, towardsCamera) < -0.05f;
            t.localScale = new Vector3(turnedAway ? -1f : 1f, 1f, 1f);
            Debug.Log($"[ROE] {fighter.name}: at {position}, {(turnedAway ? "mirrored (the stance would show her back)" : "as authored")}");
        }

        public static void SampleAll(IList<GameObject> fighters, IList<AnimationClip> clips, IList<float> times)
        {
            // one batch for everybody: a new batch reverts whatever the previous one posed
            if (!AnimationMode.InAnimationMode())
                AnimationMode.StartAnimationMode();
            AnimationMode.BeginSampling();
            for (int i = 0; i < fighters.Count; i++)
                if (clips[i] != null)
                    AnimationMode.SampleAnimationClip(fighters[i], clips[i], times[i]);
            AnimationMode.EndSampling();
        }

        [MenuItem("ROE Fighter/Showcase/Stage stills")]
        public static void Stills()
        {
            string stage = RoeCapture.Arg("-roeStage", "s01");
            string outDir = RoeCapture.Arg("-roeOut", Path.Combine(Path.GetDirectoryName(Application.dataPath), "_work", "stage"));
            ShaderUtil.allowAsyncCompilation = false;
            var info = Open(stage);
            Debug.Log("[ROE] " + info.report);

            // where the fight happens and from where it is filmed
            Layout layout;
            using (var sight = new Sight())
                layout = FindLayout(info, sight);
            Debug.Log($"[ROE] stills layout: {layout}");
            Vector3 centre = layout.centre, axis = layout.axis, normal = layout.normal;

            var manifest = RoeManifest.Load();
            var ids = manifest.WithModels.Select(c => c.id).Take(2).ToArray();
            var fighters = new List<GameObject>();
            for (int i = 0; i < ids.Length; i++)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RoeFighterBuilder.PrefabPath(ids[i]));
                fighters.Add((GameObject)PrefabUtility.InstantiatePrefab(prefab));
            }
            // one sampling batch for both: a new batch reverts whatever the previous one posed
            if (!AnimationMode.InAnimationMode())
                AnimationMode.StartAnimationMode();
            AnimationMode.BeginSampling();
            for (int i = 0; i < ids.Length; i++)
            {
                var idle = manifest.Find(ids[i]).LoadClip("idle_01");
                if (idle != null)
                    AnimationMode.SampleAnimationClip(fighters[i], idle, 0.5f);
            }
            AnimationMode.EndSampling();
            for (int i = 0; i < ids.Length; i++)
                Debug.Log($"[ROE] {ids[i]} idle stance, turned away from the root's forward (degrees, + = to her right): {Facing(fighters[i])}");
            for (int i = 0; i < ids.Length; i++)
            {
                float side = i == 0 ? -1f : 1f;
                Place(fighters[i], centre + axis * (1.7f * side), -axis * side, normal);
            }

            // camera + post processing
            var camGo = new GameObject("Stage Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 500f;
            cam.allowHDR = true;
            cam.allowMSAA = true;
            var data = camGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.renderShadows = true;
            data.requiresDepthTexture = true;
            var volGo = new GameObject("Stage Volume");
            var volume = volGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(RoeProjectSetup.SettingsDir + "/RoeStudioVolume.asset");

            Vector3 look = centre + Vector3.up * 1.0f;
            void Shot(string name, Vector3 offset, float fov, Vector3 target, int w, int h)
            {
                cam.transform.position = centre + offset;
                cam.transform.LookAt(target, Vector3.up);
                cam.fieldOfView = fov;
                RoeCapture.Render(cam, w, h, Path.Combine(outDir, name));
            }

            string warm = "_warmup.png";
            Shot(warm, normal * 6f + Vector3.up * 1.3f, 30f, look, 320, 180);
            Shot(warm, normal * 6f + Vector3.up * 1.3f, 30f, look, 320, 180);
            foreach (var old in Directory.Exists(outDir) ? Directory.GetFiles(outDir, $"{stage}_?_*.png") : new string[0])
                File.Delete(old);
            Shot($"{stage}_1_fight.png", normal * 6.4f + Vector3.up * 1.25f, 30f, look, 1920, 1080);
            Shot($"{stage}_2_wide.png", normal * 12f + axis * 4f + Vector3.up * 4.5f, 40f, look, 1920, 1080);
            Shot($"{stage}_3_close_left.png", normal * 3.2f - axis * 2.4f + Vector3.up * 1.45f, 32f, centre - axis * 1.7f + Vector3.up * 1.2f, 1920, 1080);
            Shot($"{stage}_4_close_right.png", normal * 3.2f + axis * 2.4f + Vector3.up * 1.45f, 32f, centre + axis * 1.7f + Vector3.up * 1.2f, 1920, 1080);
            RoeCapture.EndPosing();
            Debug.Log($"[ROE] stage stills written to {outDir}");
        }
    }
}
