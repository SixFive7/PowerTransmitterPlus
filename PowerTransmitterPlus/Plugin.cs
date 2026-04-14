using Assets.Scripts.Objects;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using LaunchPadBooster;
using System;

namespace PowerTransmitterPlus
{
    [BepInDependency("stationeers.launchpad", BepInDependency.DependencyFlags.HardDependency)]
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class PowerTransmitterPlusPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "net.powertransmitterplus";
        public const string PluginName = "PowerTransmitterPlus";
        public const string PluginVersion = "1.0.0";

        internal static readonly Mod MOD = new Mod(PluginName, PluginVersion);
        internal static ManualLogSource Log;

        // Defaults extracted from the Stationeers game, matching what the
        // player actually sees on a live transmitter's dish stub.
        // PowerTransmitterVisualiser.Activate() DOTweens the MonoBehaviour's
        // EmissionColor field onto the line material's _EmissionColor,
        // overriding the material's baked orange. The live value is
        // HDR cyan-blue: (R=0, G=0.4915, B=10, A=10). Split here into a
        // normalized hex "000DFF" and an intensity 10.0 so either can be
        // tweaked independently. Width matches prefab widthMultiplier = 0.1.
        internal static ConfigEntry<float> BeamWidth;
        internal static ConfigEntry<string> BeamColorHex;
        internal static ConfigEntry<float> EmissionIntensity;
        internal static ConfigEntry<string> ShaderName;

        void Awake()
        {
            Log = Logger;

            BeamWidth = Config.Bind(
                "Visual", "Beam Width", 0.1f,
                "(Client-side) Thickness of the laser beam in world units. 0.1 matches the game's built-in dish beam width.");

            BeamColorHex = Config.Bind(
                "Visual", "Beam Color", "000DFF",
                "(Client-side) Hex RGB color of the beam (no '#', no alpha). Default 000DFF is the normalized cyan-blue the game actually applies to the beam material at runtime.");

            EmissionIntensity = Config.Bind(
                "Visual", "Emission Intensity", 10.0f,
                "(Client-side) HDR brightness multiplier applied to the beam color. 10.0 matches the game's built-in beam emission intensity. Raise for more glow, lower for subtlety.");

            ShaderName = Config.Bind(
                "Advanced", "Shader Name", "Legacy Shaders/Particles/Additive",
                "(Client-side) Unity shader used for the beam. Additive gives a laser-like glow. Fallbacks are tried automatically if this shader is missing.");

            MainThreadDispatcher.Init();

            Prefab.OnPrefabsLoaded += OnAllModsLoaded;
        }

        private void OnAllModsLoaded()
        {
            Prefab.OnPrefabsLoaded -= OnAllModsLoaded;

            try
            {
                var harmony = new Harmony(PluginGuid);
                harmony.PatchAll();
                Log.LogInfo("Patches applied successfully");
            }
            catch (Exception e)
            {
                Log.LogFatal($"Failed to apply patches: {e}");
            }
        }
    }
}
