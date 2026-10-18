using System.Linq;
using UnityEngine;

namespace RoeFighter
{
    /// <summary>
    /// Magica Cloth 2's own presets (its Res/Preset/MC2_Preset_*.json, as the plugin ships them) for the setup that leaves a
    /// fighter's cloth wholly to Magica ("magica_full", RoeMagicaCloth): the scene build (RoeFightScene) puts the plugin's
    /// preset files on the fight, so a player build carries them without copying them anywhere.  No Magica types here: the
    /// scene loads the same with or without the plugin.
    /// </summary>
    public class RoeMagicaPresets : MonoBehaviour
    {
        public TextAsset[] presets = new TextAsset[0];

        static RoeMagicaPresets instance;

        void OnEnable() => instance = this;

        /// <summary>A preset's JSON by its name ("MC2_Preset_Skirt"), or null when the scene has none.</summary>
        public static string Json(string name)
        {
            if (instance == null)
                instance = Object.FindFirstObjectByType<RoeMagicaPresets>();
            var asset = instance != null ? instance.presets.FirstOrDefault(p => p != null && p.name == name) : null;
            return asset != null ? asset.text : null;
        }
    }
}
