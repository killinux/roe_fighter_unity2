using System.Collections.Generic;
using System.Linq;
using RoeFighter.Fight;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RoeFighter.EditorTools
{
    /// <summary>
    /// A DOA6 character's soft bodies in the fight, by numbers (no pictures): she stands, walks, side-steps and strikes
    /// as in RoeClothDemo with the "doa6" physics setup, and at the given steps the log lists, per soft body, the nodes
    /// furthest off their animated targets (offset in her own frame, the target's bones, colliders the target is in,
    /// how far the node's lattice springs are stretched), and at the end the solver's report.
    ///   -executeMethod RoeFighter.EditorTools.DoaPhysicsProbe.Soft [-roeChar kas] [-roeCloth doa6] [-roeSteps 30,120,600] [-roeTop 6] [-roeSoftColliders 0]
    ///     [-roeSoftRest pose|bind] (the lattice's rest shape, RoeDoaPhysics.SoftRestFromPose)
    /// </summary>
    public static class DoaPhysicsProbe
    {
        public static void Soft()
        {
            string stage = RoeCapture.Arg("-roeStage", "e23_steel_s02");
            string id = RoeCapture.Arg("-roeChar", "kas");
            FighterRig.ClothBackend = RoeCapture.Arg("-roeCloth", "doa6");
            var at = RoeCapture.Arg("-roeSteps", "30,120,600").Split(',').Select(int.Parse).ToHashSet();
            int top = int.Parse(RoeCapture.Arg("-roeTop", "6"));
            RoeDoaPhysics.SoftColliders = RoeCapture.Arg("-roeSoftColliders", "1") != "0";
            RoeDoaPhysics.SoftRestFromPose = RoeCapture.Arg("-roeSoftRest", "pose") != "bind";
            EditorSceneManager.OpenScene(RoeFightScene.ScenePath(stage), OpenSceneMode.Single);
            var game = Object.FindFirstObjectByType<FightGame>();
            game.cpu = new[] { false, false };
            if (game.roster.Length > 0 && game.roster[game.pick[0]].id != id && game.roster[game.pick[1]].id != id)
                game.PickIds(null, id);
            game.Setup();
            while (game.phase != FightGame.Phase.Fight)
                game.Step(new FighterInput[2]);
            int who = game.f[0].rig.id == id ? 0 : 1;
            var me = game.f[who];
            me.foe.pos = me.pos + (me.foe.pos - me.pos).normalized * 5.5f;
            me.foe.Place();
            var doa = me.rig.ClothPart<RoeDoaPhysics>();
            var sb = new System.Text.StringBuilder($"[ROE] {id} soft bodies ({FighterRig.ClothBackend}):");
            if (doa == null)
            {
                Debug.Log(sb.Append(" no DOA6 physics on her").ToString());
                return;
            }
            // RoeClothDemo's script: guard, walk on and back, side steps, then A C D B
            var script = new (float from, float to, FighterInput input)[]
            {
                (0.0f, 1.0f, default), (1.0f, 2.6f, new FighterInput { x = 1 }), (2.6f, 3.8f, new FighterInput { x = -1 }),
                (3.8f, 5.3f, new FighterInput { y = 1 }), (5.3f, 6.8f, new FighterInput { y = -1 }),
                (7.0f, 7.0f, new FighterInput { a = true }), (7.8f, 7.8f, new FighterInput { c = true }),
                (8.8f, 8.8f, new FighterInput { d = true }), (10.2f, 10.2f, new FighterInput { b = true }),
            };
            int steps = at.Max();
            for (int s = 1; s <= steps; s++)
            {
                float t = (s - 1) / 60f;
                var input = default(FighterInput);
                foreach (var (from, to, inp) in script)
                    if (from == to ? Mathf.Abs(t - from) < 0.5f / 60f : t >= from && t < to)
                        input = inp;
                game.UpdateCamera(FightGame.Dt);
                var camRight = game.cam.transform.right;
                camRight.y = 0f;
                input.x *= Vector3.Dot(me.foe.pos - me.pos, camRight) >= 0f ? 1 : -1;
                var inputs = new FighterInput[2];
                inputs[who] = input;
                game.Step(inputs);
                if (at.Contains(s))
                    sb.Append($"\n[ROE]   step {s} ({me.rig.Current} {me.rig.CurrentTime:F2} s):\n[ROE]   " + doa.Probe(top).Replace("\n", "\n[ROE]   "));
            }
            sb.Append($"\n[ROE]   {doa.Report}");
            Debug.Log(sb.ToString());
        }

        /// <summary>
        /// Which of her transforms a physics setup puts somewhere else than another one does: the first steps of the fight
        /// under each setup, standing, then every transform under her model compared in her own frame; listed are the ones
        /// that start a difference (their parent agrees), furthest first.
        ///   -executeMethod RoeFighter.EditorTools.DoaPhysicsProbe.Moved [-roeChar kas011] [-roeCloths off,magica_style] [-roeSteps 5] [-roeTop 15]
        /// </summary>
        public static void Moved()
        {
            string stage = RoeCapture.Arg("-roeStage", "e23_steel_s02");
            string id = RoeCapture.Arg("-roeChar", "kas011");
            var setups = RoeCapture.Arg("-roeCloths", "off,magica_style").Split(',');
            int steps = int.Parse(RoeCapture.Arg("-roeSteps", "5"));
            int top = int.Parse(RoeCapture.Arg("-roeTop", "15"));
            EditorSceneManager.OpenScene(RoeFightScene.ScenePath(stage), OpenSceneMode.Single);
            var poses = new List<Dictionary<Transform, Vector3>>();
            var sb = new System.Text.StringBuilder($"[ROE] {id}: where the setups {string.Join(" / ", setups)} put her transforms after {steps} steps:");
            Transform root = null;
            foreach (var setup in setups)
            {
                FighterRig.ClothBackend = setup;
                var game = Object.FindFirstObjectByType<FightGame>();
                game.cpu = new[] { false, false };
                if (game.roster.Length > 0 && game.roster[game.pick[0]].id != id && game.roster[game.pick[1]].id != id)
                    game.PickIds(null, id);
                foreach (var rig in game.rigs)
                    rig.useCloth = true;
                game.Setup();
                while (game.phase != FightGame.Phase.Fight)
                    game.Step(new FighterInput[2]);
                var me = game.f[game.f[0].rig.id == id ? 0 : 1];
                me.foe.pos = me.pos + (me.foe.pos - me.pos).normalized * 5.5f;
                me.foe.Place();
                for (int s = 0; s < steps; s++)
                    game.Step(new FighterInput[2]);
                root = me.rig.animator.transform;
                sb.Append($"\n[ROE]   {setup}: {me.rig.ClothTitle}; her place {me.pos}, model root {root.position} turned {root.eulerAngles}");
                poses.Add(root.GetComponentsInChildren<Transform>(true).ToDictionary(t => t, t => root.InverseTransformPoint(t.position)));
            }
            for (int k = 1; k < poses.Count; k++)
            {
                var a = poses[0];
                var b = poses[k];
                float Off(Transform t) => a.TryGetValue(t, out var p) && b.TryGetValue(t, out var q) ? Vector3.Distance(p, q) : 0f;
                var starts = a.Keys.Where(t => b.ContainsKey(t) && Off(t) > 0.01f && (t.parent == null || Off(t.parent) < 0.01f))
                              .OrderByDescending(Off).ToList();
                int moved = a.Keys.Count(t => Off(t) > 0.01f);
                sb.Append($"\n[ROE]   {setups[0]} -> {setups[k]}: {moved} transforms more than 1 cm apart, {starts.Count} start it:");
                foreach (var t in starts.Take(top))
                {
                    int under = t.GetComponentsInChildren<Transform>(true).Length - 1;
                    sb.Append($"\n[ROE]     {t.name} (on {t.parent?.name}, {under} under it): {Off(t) * 100f:F1} cm, {a[t]} vs {b[t]}");
                }
            }
            Debug.Log(sb.ToString());
        }
    }
}
