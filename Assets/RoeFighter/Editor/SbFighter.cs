using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RoeFighter.EditorTools
{
    /// <summary>
    /// Stellar Blade's Eve as a fighter (user 10-05: "另外把剑星里的eve的也加个角色进来，物理能用剑星自己的就用自己的，次选项才是用
    /// magic cloth2").  tools/sb_fbx.py turns her archived outfit .blend (ripper_tpose scripts/stellarblade) into
    /// Assets/SB/&lt;id&gt;/&lt;id&gt;.fbx + materials.json + bones.json, tools/sb_textures.py makes the maps from her game's
    /// material instances (unity.json), tools/sb_physics.py reads her game's physics (kawaii.json, sbphysics.json); this
    ///   - builds the model, materials and the humanoid fighter as Fiona's are (VdfFighter.BuildModel; her bind pose already
    ///     stands in her heels, as the ROE characters' does);
    ///   - puts her game's KawaiiPhysics nodes (RoeKawaiiRig) and spring bones and rigid bodies (RoeSbRig) on the fighter
    ///     prefab, for the "stellar" physics (RoeSbPhysics; F4, and "auto" takes it for her);
    ///   - checks where the game's shapes land on her: each capsule against her skin, each rigid body's shapes, each joint's
    ///     rest (where the game's constraint frames put the child against where her bones have it);
    ///   - hangs her sword in her right hand when tools/sb_weapons.py listed it (VdfFighter.AttachWeapons: a holder where the
    ///     game's prop bone stands, which her own clips key - SbAnims).
    /// Her fight definition: tools/sb/&lt;id&gt;.json (DoaFighter.Load).
    ///   -executeMethod RoeFighter.EditorTools.SbFighter.Build [-roeSb eve09]
    /// </summary>
    public static class SbFighter
    {
        public static string Dir(string id) => $"Assets/SB/{id}";

        [MenuItem("ROE Fighter/Build/Stellar Blade fighter (Eve)")]
        public static void Build()
        {
            foreach (var id in RoeCapture.Arg("-roeSb", "eve09").Split(','))
                Build(id.Trim());
            AssetDatabase.SaveAssets();
        }

        public static GameObject Build(string id)
        {
            string dir = Dir(id);
            if (VdfFighter.BuildModel(id, dir, "Stellar Blade", heels: false) == null)
                return null;
            VdfFighter.AttachKawaii(id, dir);
            AttachSb(id, dir);
            // her sword (tools/sb_weapons.py: Assets/SB/<id>/weapons/weapons.json), on a holder in her right hand that her own
            // clips key (SbAnims); none listed: none
            VdfFighter.AttachWeapons(id, dir);
            return AssetDatabase.LoadAssetAtPath<GameObject>(RoeHumanoid.FighterPath(id));
        }

        /// <summary>
        /// A model that is no fighter, only its maps, materials and import settings (VdfFighter.BuildMaterials): Eve's nude
        /// base for the clothes burst, Assets/SB/eve_nude (tools/sb_fbx.py on ripper_tpose's Eve_Nude_Barefoot.blend with
        /// SB_FBX_SKIP="Hair|Head", then tools/sb_textures.py), which Editor/Burst/eve09.json and eve37.json put under her
        /// outfits (RoeNudeBody).
        ///   -executeMethod RoeFighter.EditorTools.SbFighter.BuildBase [-roeSb eve_nude]
        /// </summary>
        public static void BuildBase()
        {
            foreach (var raw in RoeCapture.Arg("-roeSb", "eve_nude").Split(','))
            {
                string id = raw.Trim();
                var model = VdfFighter.BuildMaterials(id, Dir(id), out int made, out int set);
                Debug.Log($"[ROE] {id}: base model {(model != null ? AssetDatabase.GetAssetPath(model) : "FAILED")}, {made} materials, {set} texture importers set");
            }
            AssetDatabase.SaveAssets();
        }

        /// <summary>RoeSbRig on the fighter prefab, then a check of the game's rigid bodies on her (RoeSbPhysics built on an instance).</summary>
        public static void AttachSb(string id, string dir)
        {
            var text = AssetDatabase.LoadAssetAtPath<TextAsset>($"{dir}/sbphysics.json");
            if (text == null)
            {
                Debug.Log($"[ROE] {id}: no {dir}/sbphysics.json - no Stellar Blade physics (tools/sb_physics.py)");
                return;
            }
            string path = RoeHumanoid.FighterPath(id);
            var root = PrefabUtility.LoadPrefabContents(path);
            var rig = root.GetComponent<RoeSbRig>() ?? root.AddComponent<RoeSbRig>();
            rig.data = text;
            PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);

            var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var animator = go.GetComponent<Animator>();
            var byName = new Dictionary<string, Transform>();
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                if (!byName.ContainsKey(t.name))
                    byName[t.name] = t;
            var data = go.GetComponent<RoeSbRig>().Get();
            var notes = new List<string>();
            // joints: the child's rest against its parent as the game's frames say, and as her bones have it
            foreach (var j in data.joints)
            {
                if (!byName.TryGetValue(j.child, out var child) || !byName.TryGetValue(j.parent, out var parent))
                    continue;
                var f2 = Frame(j.pos2, j.pri2, j.sec2);
                var f1 = Frame(j.pos1, j.pri1, j.sec1);
                var rel = f2 * f1.inverse;
                var wantPos = parent.TransformPoint(rel.GetColumn(3));
                var wantRot = parent.rotation * rel.rotation;
                notes.Add($"{j.child}->{j.parent}: {100f * Vector3.Distance(wantPos, child.position):F1} cm, {Quaternion.Angle(wantRot, child.rotation):F0} deg");
            }
            // the control rigs: their units run again on the editor's last run (as the package keeps it), and her bind pose
            // against the rig's own skeleton (both zero when the port and the axes are right)
            var rigNotes = new List<string>();
            foreach (var p in data.rigs)
            {
                try
                {
                    var vm = new RoeRigVM(p);
                    var (worst, where) = vm.Replay();
                    var bones = new RoeRigVM.UnityBones(vm, go.transform, byName, t => (t.localPosition, t.localRotation));
                    rigNotes.Add($"{p.name}: {p.code.Count} steps replayed, worst difference {worst:G3} ({where}); {bones.Check(vm)}");
                }
                catch (Exception e) when (e is NotSupportedException || e is ArgumentException)
                {
                    rigNotes.Add($"{p.name}: {e.Message}");
                }
            }
            // a solver on the instance: what it builds (and that the physics scene steps); each frame from the rest pose, as
            // FighterRig does it
            var cloth = new RoeSbPhysics(new RoeClothScope { animator = animator, world = go.transform });
            for (int i = 0; i < 30; i++)
            {
                cloth.Rest();
                cloth.Step(1f / 60f, 0f);
            }
            var ponytailTip = byName.TryGetValue("Ab-TL-HairB09", out var tip) ? tip.position : Vector3.zero;
            string report = cloth.Report;
            cloth.Dispose();
            Object.DestroyImmediate(go);
            Debug.Log($"[ROE] {id}: Stellar Blade physics - {report}; after 0.5 s standing the ponytail's tip is at {ponytailTip}; " +
                      $"joint rests (game frames vs her bones): {string.Join("; ", notes)}" +
                      (rigNotes.Count > 0 ? $"; control rigs: {string.Join("; ", rigNotes)}" : ""));
        }

        /// <summary>For checks: how the FBX itself stands (its top nodes' rotations, which way the feet point), before any prefab work.</summary>
        public static void Probe()
        {
            foreach (var id in RoeCapture.Arg("-roeSb", "eve09").Split(','))
                Probe(id);
        }

        static void Probe(string id)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{Dir(id)}/{id}.fbx");
            var go = Object.Instantiate(model);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var lines = new List<string>();
            foreach (var t in go.GetComponentsInChildren<Transform>(true).Where(t => t.parent == null || t.parent.parent == null || t.parent.parent.parent == null || t.name.StartsWith("Bip001 L Foot") || t.name.StartsWith("Bip001 L Toe0")))
                lines.Add($"{t.name}: local {t.localPosition} {t.localEulerAngles}, world {t.position}");
            Object.DestroyImmediate(go);
            Debug.Log($"[ROE] probe {id}: " + string.Join(" | ", lines));
        }

        static Matrix4x4 Frame(float[] pos, float[] pri, float[] sec)
        {
            Vector3 V(float[] v) => v != null && v.Length == 3 ? new Vector3(-v[0], -v[1], v[2]) : Vector3.zero;
            var x = V(pri).normalized;
            var y = V(sec);
            y = (y - Vector3.Dot(y, x) * x).normalized;
            var m = Matrix4x4.identity;
            m.SetColumn(0, x);
            m.SetColumn(1, y);
            m.SetColumn(2, Vector3.Cross(x, y));
            var p = V(pos) * 0.01f;
            m.SetColumn(3, new Vector4(p.x, p.y, p.z, 1f));
            return m;
        }
    }
}
