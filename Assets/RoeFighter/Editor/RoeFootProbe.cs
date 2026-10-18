using System.Linq;
using System.Reflection;
using RoeFighter.Fight;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RoeFighter.EditorTools
{
    /// <summary>
    /// Why a fighter's planted feet stand the way they do (10-05, Eve's platform shoes stood twisted and tilted): the stance
    /// sampled alone on a fresh copy of the fighter prefab against its bind pose, then in the fight logic the foot frames
    /// FighterRig took at Init (footRest, footSide, the sole heights) and, for the first second of the guard, per foot the
    /// heading it stands in (from the ankle's hinge) and where its toes point, both against her facing, the foot's pitch,
    /// the ankle and toe heights.
    ///   -executeMethod RoeFighter.EditorTools.RoeFootProbe.FootFrames [-roeChar eve09]
    /// </summary>
    public static class RoeFootProbe
    {
        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        static float Pitch(Transform foot, Transform toe)
        {
            var d = toe.position - foot.position;
            return Mathf.Atan2(-d.y, Flat(d).magnitude) * Mathf.Rad2Deg;      // + = toes below the ankle
        }

        static string Foot(string label, Transform foot, Transform toe, Vector3 forward, float floor)
        {
            var toeDir = Flat(toe.position - foot.position);
            return $"{label}: pitch {Pitch(foot, toe):F0}, toes {Vector3.SignedAngle(forward, toeDir, Vector3.up):F0} deg off her facing " +
                   $"({100f * toeDir.magnitude:F1} cm along the floor), ankle {foot.position.y - floor:F3}, toe {toe.position.y - floor:F3}";
        }

        public static void FootFrames()
        {
            string stage = RoeCapture.Arg("-roeStage", "e23_steel_s02");
            string id = RoeCapture.Arg("-roeChar", "eve09");
            var log = new System.Text.StringBuilder($"[ROE] foot frames {id}:");

            // 1. the prefab: bind pose, then the stance clip sampled on it (what FighterRig.Init does first)
            EditorSceneManager.OpenScene(RoeFightScene.ScenePath(stage), OpenSceneMode.Single);
            var game = Object.FindFirstObjectByType<FightGame>();
            game.cpu = new[] { false, false };
            if (game.rigs.All(r => r.id != id))
                game.PickIds(null, id);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RoeHumanoid.FighterPath(id));
            var copy = Object.Instantiate(prefab);
            copy.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var ca = copy.GetComponent<Animator>();
            var stanceClip = (game.roster.Length > 0 ? game.roster : game.rigs).First(r => r.id == id).stance;
            for (int k = 0; k < 2; k++)
            {
                var foot = ca.GetBoneTransform(k == 0 ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
                var toe = ca.GetBoneTransform(k == 0 ? HumanBodyBones.LeftToes : HumanBodyBones.RightToes);
                log.Append($"\n[ROE]   bind {Foot(k == 0 ? "L" : "R", foot, toe, copy.transform.forward, 0f)}");
            }
            if (stanceClip != null)
            {
                stanceClip.SampleAnimation(copy, 0f);
                log.Append($"\n[ROE]   stance clip {stanceClip.name} (humanoid {stanceClip.humanMotion}) sampled on the prefab:");
                for (int k = 0; k < 2; k++)
                {
                    var foot = ca.GetBoneTransform(k == 0 ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
                    var toe = ca.GetBoneTransform(k == 0 ? HumanBodyBones.LeftToes : HumanBodyBones.RightToes);
                    log.Append($"\n[ROE]     {Foot(k == 0 ? "L" : "R", foot, toe, copy.transform.forward, 0f)}");
                }
            }
            Object.DestroyImmediate(copy);

            // 2. the fight: what Init took, then the guard
            game.Setup();
            int who = game.f[0].rig.id == id ? 0 : 1;
            var me = game.f[who];
            var rig = me.rig;
            var a = rig.animator;
            const BindingFlags Any = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
            object Field(string name) => typeof(FighterRig).GetField(name, Any)?.GetValue(rig);
            var footRest = (Quaternion[])Field("footRest");
            var footSide = (Vector3[])Field("footSide");
            log.Append($"\n[ROE]   Init: toeSole {(float)Field("toeSole"):F3}, ankleSole {(float)Field("ankleSole"):F3}, grounded {Field("grounded")}, flatFeet {rig.flatFeet}");
            for (int k = 0; k < 2; k++)
                log.Append($"\n[ROE]     {(k == 0 ? "L" : "R")} footRest {footRest[k].eulerAngles} footSide {footSide[k]}");
            var feet = new[] { a.GetBoneTransform(HumanBodyBones.LeftFoot), a.GetBoneTransform(HumanBodyBones.RightFoot) };
            var toes = new[] { a.GetBoneTransform(HumanBodyBones.LeftToes), a.GetBoneTransform(HumanBodyBones.RightToes) };
            while (game.phase != FightGame.Phase.Fight)
                game.Step(new FighterInput[2]);
            for (int s = 0; s <= 60; s++)
            {
                game.Step(new FighterInput[2]);
                if (s % 12 != 0)
                    continue;
                float floor = a.transform.position.y;
                for (int k = 0; k < 2; k++)
                {
                    var heading = rig.Heading(k);
                    log.Append($"\n[ROE]   step {s} {Foot(k == 0 ? "L" : "R", feet[k], toes[k], me.Forward, floor)}; stands in a heading " +
                               $"{Vector3.SignedAngle(me.Forward, heading, Vector3.up):F0} deg off her facing (weight {rig.FootWeight[k]:F2}), " +
                               $"side axis up {(feet[k].rotation * footSide[k]).y:F2}");
                }
            }
            Debug.Log(log.ToString());
        }
    }
}
