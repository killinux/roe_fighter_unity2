using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace RoeFighter.EditorTools
{
    /// <summary>
    /// Renders a camera to image files without entering play mode (works in batch mode as long
    /// as the editor was not started with -nographics).
    /// </summary>
    public static class RoeCapture
    {
        public static void Render(Camera camera, int width, int height, string path, int jpgQuality = 0)
        {
            var rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            try
            {
                var request = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
                if (RenderPipeline.SupportsRenderRequest(camera, request))
                {
                    RenderPipeline.SubmitRenderRequest(camera, request);
                }
                else
                {
                    var old = camera.targetTexture;
                    camera.targetTexture = rt;
                    camera.Render();
                    camera.targetTexture = old;
                }

                var previous = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(width, height, TextureFormat.RGB24, false, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                tex.Apply(false);
                RenderTexture.active = previous;

                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, jpgQuality > 0 ? tex.EncodeToJPG(jpgQuality) : tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);
            }
            finally
            {
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        /// <summary>
        /// Put a character into the pose of a clip at a time (edit mode); an Unreal body (RoeUeRig) as the fight poses it: its
        /// unmapped spine joints at rest before the clip, their share of the bend and the twist bones after.
        /// </summary>
        public static void Pose(GameObject character, AnimationClip clip, float time)
        {
            if (!AnimationMode.InAnimationMode())
                AnimationMode.StartAnimationMode();
            var ue = character.GetComponent<RoeUeRig>();
            if (ue != null)
            {
                ue.Build();         // no Awake in edit mode: bind rotations on first use
                ue.Rest();
            }
            AnimationMode.BeginSampling();
            AnimationMode.SampleAnimationClip(character, clip, time);
            AnimationMode.EndSampling();
            if (ue != null)
            {
                ue.Distribute();
                ue.DriveTwists();
            }
        }

        public static void EndPosing()
        {
            if (AnimationMode.InAnimationMode())
                AnimationMode.StopAnimationMode();
        }

        /// <summary>Value of "-name value" on the editor's command line, or the fallback.</summary>
        public static string Arg(string name, string fallback)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name)
                    return args[i + 1];
            return fallback;
        }
    }
}
