using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RoeFighter.EditorTools
{
    /// <summary>
    /// How a fighter's feet stand, from the game's own standing clips (user 10-03: "有的角色是有高跟鞋的，有的角色是没有的，
    /// 得区分一下").  Every frame of the clips she stands in (idle_01 if it stands, idle_02, react_01, react_02) is posed on
    /// the fighter prefab; of the frames where a foot is planted (toe bone within 2.5 cm of the floor), the one a quarter of
    /// the way up from the lowest ankle is how she stands on it (the very lowest was a stray frame: g04's heel sank into the
    /// floor for a moment in react_01, her ankle at 9 cm).  Only frames with the whole sole down count when there are
    /// any: the skin under the heel half and under the toe half both within 2.5 cm of the floor (g05's right foot was
    /// taken from a frame of idle_02 with the heel 5 cm up, on the ball of the foot).
    ///   heels   the shoe holds the ankle up even then (a08, g04: 13-15 cm): the fight keeps posing planted feet in the
    ///           battle stance's high-heeled angle, as before;
    ///   flat    the ankle comes down (g05 barefoot: 8 cm): the motion capture's feet - in the bind pose's angle, which
    ///           for ROE's bare feet is pointed 72 degrees like a high heel, so she stood on the tips of her toes - are
    ///           turned up by the difference to the flattest stand, and planted feet take that stand.
    /// -roeFeet g05=flat,b10=heels overrides the verdict.
    ///   -executeMethod RoeFighter.EditorTools.RoeFeet.Report [-roeChars a08,g04,b10,g05]
    /// </summary>
    public static class RoeFeet
    {
        /// <summary>Planted feet whose ankle stays at least this high (m) stand in heels.</summary>
        public const float HeelAnkle = 0.105f;
        /// <summary>A foot is planted when its toe bone is this close to the floor (m).</summary>
        public const float Planted = 0.025f;
        /// <summary>The whole sole is down when the skin under the heel half and under the toe half are both this close to the floor (m).</summary>
        public const float SoleDown = 0.025f;

        public class Result
        {
            public bool heels;
            public string note;
            // the flattest stand, per foot (left, right): the foot against the direction it points along the floor, the
            // toe bone's local rotation, the bone heights
            public Quaternion[] rest = new Quaternion[2], toe = new Quaternion[2];
            public float toeSole, ankleSole;
            // how far the bind pose's foot is pointed beyond the flattest stand (degrees), and the foot's local axis to turn it up about
            public float[] pitchFix = new float[2];
            public Vector3[] pitchAxis = new Vector3[2];
        }

        static float Pitch(Transform foot, Transform toe)
        {
            var d = toe.position - foot.position;
            return Mathf.Atan2(-d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;     // + = toes below the ankle
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        public static Result Measure(string id)
        {
            var r = new Result();
            var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(RoeHumanoid.FighterPath(id)));
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var an = go.GetComponent<Animator>();
            var feet = new[] { an.GetBoneTransform(HumanBodyBones.LeftFoot), an.GetBoneTransform(HumanBodyBones.RightFoot) };
            var toes = new[] { an.GetBoneTransform(HumanBodyBones.LeftToes), an.GetBoneTransform(HumanBodyBones.RightToes) };
            if (feet.Any(f => f == null) || toes.Any(t => t == null))
            {
                Object.DestroyImmediate(go);
                r.heels = true;
                r.note = "no foot or toe bones: as the stance has them";
                return r;
            }
            // the bind pose (the motion capture's feet keep its angle: the avatar's T-pose keeps the feet as they are)
            var bindPitch = new[] { Pitch(feet[0], toes[0]), Pitch(feet[1], toes[1]) };
            var bindLocal = feet.Select(f => f.localRotation).ToArray();
            var bindWorld = feet.Select(f => f.rotation).ToArray();
            // the skin of each foot: vertices whose strongest bone is the foot or under it, in that bone's space
            var skin = new[] { new List<(Transform bone, Vector3 local)>(), new List<(Transform bone, Vector3 local)>() };
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.sharedMesh == null || !smr.enabled || !smr.gameObject.activeInHierarchy)
                    continue;
                var mesh = smr.sharedMesh;
                var bones = smr.bones;
                var bind = mesh.bindposes;
                var bw = mesh.boneWeights;
                var verts = mesh.vertices;
                for (int i = 0; i < bw.Length; i++)
                {
                    int b = bw[i].boneIndex0;
                    if (b >= bones.Length || b >= bind.Length || bones[b] == null)
                        continue;
                    for (int k = 0; k < 2; k++)
                        if (bones[b].IsChildOf(feet[k]))
                            skin[k].Add((bones[b], bind[b].MultiplyPoint3x4(verts[i])));
                }
            }
            // the lowest skin under the heel half and under the toe half of a foot (behind / ahead of the ankle along the floor)
            (float heel, float ball) Sole(int k)
            {
                var ahead = Flat(toes[k].position - feet[k].position).normalized;
                float heel = float.MaxValue, ball = float.MaxValue;
                foreach (var (bone, local) in skin[k])
                {
                    var p = bone.TransformPoint(local);
                    if (Vector3.Dot(p - feet[k].position, ahead) < 0f)
                        heel = Mathf.Min(heel, p.y);
                    else
                        ball = Mathf.Min(ball, p.y);
                }
                return (heel, ball);
            }

            var samples = new[] { new List<(float ankle, float toe, float pitch, string where, Quaternion rest, Quaternion toeLocal, float heel, float ball)>(),
                                  new List<(float ankle, float toe, float pitch, string where, Quaternion rest, Quaternion toeLocal, float heel, float ball)>() };
            foreach (var name in new[] { "idle_01", "idle_02", "react_01", "react_02" })
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(RoeHumanoidClips.ClipPath(id, name));
                if (clip == null)
                    continue;
                for (float t = 0f; t <= clip.length + 1e-4f; t += 1f / 30f)
                {
                    RoeCapture.Pose(go, clip, t);
                    for (int k = 0; k < 2; k++)
                    {
                        float ankle = feet[k].position.y, toe = toes[k].position.y;
                        if (toe > Planted)
                            continue;
                        var ahead = Flat(toes[k].position - feet[k].position);
                        if (ahead.sqrMagnitude < 1e-6f)
                            continue;
                        ahead.Normalize();
                        var (heel, ball) = skin[k].Count > 0 ? Sole(k) : (0f, 0f);
                        samples[k].Add((ankle, toe, Pitch(feet[k], toes[k]), $"{name} {t:F2}s",
                                        Quaternion.Inverse(Quaternion.LookRotation(ahead, Vector3.up)) * feet[k].rotation,
                                        toes[k].localRotation, heel, ball));
                    }
                }
            }
            RoeCapture.EndPosing();
            if (samples.Any(x => x.Count == 0))
            {
                Object.DestroyImmediate(go);
                r.heels = true;
                r.note = "no planted foot in the standing clips: as the stance has them";
                return r;
            }
            // the frames with the whole sole down, if there are any
            var down = samples.Select(x => x.Where(v => v.heel < SoleDown && v.ball < SoleDown).ToList()).ToArray();
            var pool = samples.Select((x, k) => down[k].Count > 0 ? down[k] : x).ToArray();
            var best = pool.Select(x => x.OrderBy(v => v.ankle).ElementAt(x.Count / 4)).ToArray();
            float flattest = Mathf.Min(best[0].ankle, best[1].ankle);
            r.heels = flattest >= HeelAnkle;
            for (int k = 0; k < 2; k++)
            {
                r.rest[k] = best[k].rest;
                r.toe[k] = best[k].toeLocal;
                // the bind pose's foot turned up about its own lateral axis (the ankle's hinge) by the difference in pitch
                feet[k].localRotation = bindLocal[k];
                var lateral = Vector3.Cross(Vector3.up, Flat(toes[k].position - feet[k].position).normalized);
                r.pitchAxis[k] = (Quaternion.Inverse(bindWorld[k]) * lateral).normalized;
                r.pitchFix[k] = Mathf.Max(0f, bindPitch[k] - best[k].pitch);
                // which way round lifts the toes: the one that ends nearer her standing pitch.  (Comparing toe heights
                // could not tell: turned the wrong way, 72 + 40 degrees, the toes pass straight down and come up again -
                // g05's right foot was pointed 40 degrees further, toes backwards, wherever the stand did not cover it.)
                var bindAhead = Flat(toes[k].position - feet[k].position).normalized;
                float Through()          // the pitch, on past 90 when the toes point backwards
                {
                    var d = toes[k].position - feet[k].position;
                    return Mathf.Atan2(-d.y, Vector3.Dot(d, bindAhead)) * Mathf.Rad2Deg;
                }
                feet[k].localRotation = bindLocal[k] * Quaternion.AngleAxis(r.pitchFix[k], r.pitchAxis[k]);
                float plus = Through();
                feet[k].localRotation = bindLocal[k] * Quaternion.AngleAxis(-r.pitchFix[k], r.pitchAxis[k]);
                float minus = Through();
                if (Mathf.Abs(minus - best[k].pitch) < Mathf.Abs(plus - best[k].pitch))
                    r.pitchAxis[k] = -r.pitchAxis[k];
                feet[k].localRotation = bindLocal[k];
            }
            r.toeSole = (best[0].toe + best[1].toe) * 0.5f;
            r.ankleSole = (best[0].ankle + best[1].ankle) * 0.5f;
            string Side(int k) => $"{(k == 0 ? "L" : "R")} {best[k].ankle * 100f:F1} cm pitch {best[k].pitch:F0} heel {best[k].heel * 100f:F1} ball {best[k].ball * 100f:F1} " +
                                  $"in {best[k].where} of {down[k].Count} sole-down/{samples[k].Count} planted, lowest {pool[k].Min(v => v.ankle) * 100f:F1}";
            r.note = $"{(r.heels ? "heels" : "flat")}: planted ankle {flattest * 100f:F1} cm (a quarter up the planted frames; {Side(0)}, {Side(1)}; " +
                     $"bind pose pitch {bindPitch[0]:F0}/{bindPitch[1]:F0}" + (r.heels ? "" : $", the motion capture's feet turned up {r.pitchFix[0]:F0}/{r.pitchFix[1]:F0} deg") + ")";
            Object.DestroyImmediate(go);
            return r;
        }

        [MenuItem("ROE Fighter/Fight/Feet kinds")]
        public static void Report()
        {
            var sb = new System.Text.StringBuilder("[ROE] feet:");
            foreach (var id in RoeCapture.Arg("-roeChars", "a08,g04,b10,g05").Split(','))
                sb.Append($"\n[ROE]   {id}: {Measure(id).note}");
            Debug.Log(sb.ToString());
        }
    }
}
