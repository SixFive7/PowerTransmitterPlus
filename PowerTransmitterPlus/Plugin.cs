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
        public const string PluginVersion = "1.1.0";

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

        // Pulse train (power indicator). World-space so stripes look consistent
        // on beams of any length. See BeamPulseTrain for rendering detail.
        internal static ConfigEntry<float> StripeWavelength;
        internal static ConfigEntry<float> ScrollSpeed;
        internal static ConfigEntry<float> StripeTroughBrightness;

        // Distance cost: replaces vanilla's distance-based capacity derate with
        // a source-draw multiplier. See DistanceCostPatches for the math + the
        // four Harmony patches that implement it. Server-authoritative.
        internal static ConfigEntry<float> DistanceCostFactor;


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

            StripeWavelength = Config.Bind(
                "Pulse", "Stripe Wavelength", 2.0f,
                "(Client-side) Distance in world meters between one bright pulse and the next. Same physical spacing on 5m beams and 200m beams.");

            ScrollSpeed = Config.Bind(
                "Pulse", "Scroll Speed", 25.0f,
                "(Client-side) Pulse scroll speed in world meters per second at full power (5 kW delivered). Scales with sqrt(intensity), so a 1 kW load runs at about 45% of this, and draws above 5 kW (possible with the distance-cost model) exceed it.");

            StripeTroughBrightness = Config.Bind(
                "Pulse", "Trough Brightness", 0.5f,
                "(Client-side) Beam brightness between pulses, 0..1. 1 = no visible pulsing (beam flat). 0 = troughs fully dark. Default 0.5 keeps the link clearly visible between peaks.");

            DistanceCostFactor = Config.Bind(
                "Distance", "Cost Factor (k)", 5f,
                "(Server-authoritative) Per-kilometer overhead on transmitter source draw. Source pulls (1 + k * distance_m / 1000) watts for every watt delivered. k=0 = no overhead. k=5 (default) = 1km doubles to 6:1, 5km is 26:1. k=10 = 1km is 11:1. ONLY THE HOST'S VALUE AFFECTS GAMEPLAY in multiplayer — clients' values are ignored for simulation but used for tablet/IC10 display until the host's value is pushed at connect time. Changing this on the host live-broadcasts the new value to all clients.");

            MainThreadDispatcher.Init();
            DistanceConfigSync.HookHostBroadcast();
            BeamVisualConfigSync.HookHostBroadcast();

            Prefab.OnPrefabsLoaded += OnAllModsLoaded;
        }

        private void OnAllModsLoaded()
        {
            Prefab.OnPrefabsLoaded -= OnAllModsLoaded;

            try
            {
                MOD.Networking.Required = true;
                MOD.Networking.RegisterMessage<DistanceConfigMessage>();
                MOD.Networking.RegisterMessage<BeamVisualConfigMessage>();

                var harmony = new Harmony(PluginGuid);
                harmony.PatchAll();

                // After Harmony patches are live, inject our IC10 named constants
                // so MIPS source can refer to MicrowaveSourceDraw etc. by name.
                Ic10ConstantsPatcher.Apply();

                Log.LogInfo("Patches applied successfully");
            }
            catch (Exception e)
            {
                Log.LogFatal($"Failed to apply patches: {e}");
            }
        }
    }
}
