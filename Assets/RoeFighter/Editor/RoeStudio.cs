using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace RoeFighter.EditorTools
{
    /// <summary>
    /// A neutral photo-studio scene for looking at characters: key / fill / rim lights, a dark
    /// floor that takes shadows, the game's own reflection cubemap, and a post-processing volume.
    /// Everything is created from code so that the look is reproducible and tunable in one place.
    /// </summary>
    public class RoeStudio
    {
        public Camera camera;
        public UniversalAdditionalCameraData cameraData;
        public Light key, fill, rim;
        /// <summary>How steeply the key light comes down (degrees; 90 straight down, as the fight stage's spotlights do).</summary>
        public float keyPitch = 38f;
        public Volume volume;
        public DepthOfField depthOfField;
        public GameObject floor;

        const string ProfilePath = RoeProjectSetup.SettingsDir + "/RoeStudioVolume.asset";
        const string FloorMaterialPath = RoeProjectSetup.SettingsDir + "/RoeStudioFloor.mat";
        const string CubemapPath = "Assets/ROE/common/chara_tex_bare_common/StandardCubeMap.png";
        static readonly Color Backdrop = new Color(0.20f, 0.215f, 0.25f);

        public static RoeStudio Build(bool newScene = true)
        {
            if (newScene)
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var s = new RoeStudio();

            // ---- environment
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.66f, 0.74f) * 0.7f;
            RenderSettings.ambientEquatorColor = new Color(0.50f, 0.49f, 0.50f) * 0.6f;
            RenderSettings.ambientGroundColor = new Color(0.30f, 0.27f, 0.25f) * 0.4f;
            RenderSettings.fog = true;                       // lets the floor melt into the backdrop
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.03f;
            RenderSettings.fogColor = Backdrop;
            var cube = AssetDatabase.LoadAssetAtPath<Texture>(CubemapPath);
            if (cube != null)
            {
                RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
                RenderSettings.customReflectionTexture = cube;
                RenderSettings.reflectionIntensity = 0.8f;
            }
            else
            {
                Debug.LogWarning("[ROE] reflection cubemap not found: " + CubemapPath);
            }

            // ---- lights
            s.key = MakeLight("Key Light", new Vector3(38f, -32f, 0f), new Color(1.00f, 0.96f, 0.90f), 1.2f, true);
            s.fill = MakeLight("Fill Light", new Vector3(18f, 70f, 0f), new Color(0.78f, 0.86f, 1.00f), 0.42f, false);
            s.rim = MakeLight("Rim Light", new Vector3(22f, 165f, 0f), new Color(0.85f, 0.92f, 1.00f), 0.8f, false);

            // ---- floor
            s.floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            s.floor.name = "Floor";
            s.floor.transform.localScale = new Vector3(40f, 1f, 40f);
            Object.DestroyImmediate(s.floor.GetComponent<Collider>());
            s.floor.GetComponent<Renderer>().sharedMaterial = FloorMaterial();

            // ---- camera
            var camGo = new GameObject("Studio Camera");
            s.camera = camGo.AddComponent<Camera>();
            s.camera.clearFlags = CameraClearFlags.SolidColor;
            s.camera.backgroundColor = Backdrop;
            s.camera.fieldOfView = 30f;
            s.camera.nearClipPlane = 0.05f;
            s.camera.farClipPlane = 200f;
            s.camera.allowHDR = true;
            s.camera.allowMSAA = true;
            s.cameraData = camGo.AddComponent<UniversalAdditionalCameraData>();
            s.cameraData.renderPostProcessing = true;
            s.cameraData.antialiasing = AntialiasingMode.None;       // MSAA does the work (hair is alpha-to-coverage)
            s.cameraData.renderShadows = true;
            s.cameraData.requiresDepthTexture = true;
            s.cameraData.stopNaN = true;
            s.cameraData.dithering = true;

            // ---- post processing
            var volGo = new GameObject("Studio Volume");
            s.volume = volGo.AddComponent<Volume>();
            s.volume.isGlobal = true;
            s.volume.priority = 1f;
            s.volume.sharedProfile = Profile(out s.depthOfField);

            DynamicGI.UpdateEnvironment();
            return s;
        }

        static Light MakeLight(string name, Vector3 euler, Color color, float intensity, bool shadows)
        {
            var go = new GameObject(name);
            go.transform.rotation = Quaternion.Euler(euler);
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = color;
            light.intensity = intensity;
            light.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            light.shadowStrength = 0.92f;
            var data = go.AddComponent<UniversalAdditionalLightData>();
            data.usePipelineSettings = true;
            return light;
        }

        static Material FloorMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(FloorMaterialPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                Directory.CreateDirectory(RoeProjectSetup.SettingsDir);
                AssetDatabase.CreateAsset(mat, FloorMaterialPath);
            }
            mat.SetColor("_BaseColor", new Color(0.30f, 0.31f, 0.34f));
            mat.SetFloat("_Smoothness", 0.25f);
            mat.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static VolumeProfile Profile(out DepthOfField dof)
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                Directory.CreateDirectory(RoeProjectSetup.SettingsDir);
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }

            var tone = Get<Tonemapping>(profile);
            tone.mode.Override(TonemappingMode.Neutral);

            var bloom = Get<Bloom>(profile);
            bloom.threshold.Override(1.05f);
            bloom.intensity.Override(0.28f);
            bloom.scatter.Override(0.62f);
            bloom.highQualityFiltering.Override(true);

            var color = Get<ColorAdjustments>(profile);
            color.postExposure.Override(0.0f);
            color.contrast.Override(6f);
            color.saturation.Override(4f);

            var vignette = Get<Vignette>(profile);
            vignette.intensity.Override(0.20f);
            vignette.smoothness.Override(0.45f);

            dof = Get<DepthOfField>(profile);
            dof.mode.Override(DepthOfFieldMode.Off);

            EditorUtility.SetDirty(profile);
            return profile;
        }

        static T Get<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (profile.TryGet<T>(out var c))
                return c;
            c = profile.Add<T>(true);
            AssetDatabase.AddObjectToAsset(c, profile);
            return c;
        }

        /// <summary>Bokeh depth of field focused at a distance, or off when aperture is 0.</summary>
        public void SetFocus(float distance, float aperture, float focalLengthMm)
        {
            if (aperture <= 0f)
            {
                depthOfField.mode.Override(DepthOfFieldMode.Off);
                return;
            }
            depthOfField.mode.Override(DepthOfFieldMode.Bokeh);
            depthOfField.focusDistance.Override(distance);
            depthOfField.aperture.Override(aperture);
            depthOfField.focalLength.Override(focalLengthMm);
            depthOfField.bladeCount.Override(7);
        }

        /// <summary>Place the camera on a circle around a target: yaw 0 = in front of the subject.</summary>
        public void Aim(Vector3 target, Vector3 subjectForward, float yawDeg, float pitchDeg, float distance, float fov)
        {
            var forward = Vector3.ProjectOnPlane(subjectForward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.5f)
                forward = Vector3.forward;
            var dir = Quaternion.AngleAxis(yawDeg, Vector3.up) * forward;
            dir = Quaternion.AngleAxis(-pitchDeg, Vector3.Cross(Vector3.up, dir)) * dir;
            camera.transform.position = target + dir * distance;
            camera.transform.LookAt(target, Vector3.up);
            camera.fieldOfView = fov;
        }

        /// <summary>Turn the three lights with the subject so that it is lit the same from any facing.</summary>
        public void LightFrom(Vector3 subjectForward)
        {
            var forward = Vector3.ProjectOnPlane(subjectForward, Vector3.up).normalized;
            float yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;      // 0 when the subject faces +Z
            // light euler y = direction the light travels; a light in front of the subject travels against its forward
            key.transform.rotation = Quaternion.Euler(keyPitch, yaw + 180f - 32f, 0f);
            fill.transform.rotation = Quaternion.Euler(18f, yaw + 180f + 70f, 0f);
            rim.transform.rotation = Quaternion.Euler(22f, yaw + 15f, 0f);
        }
    }
}
