using Assets.Scripts.Objects.Electrical;
using HarmonyLib;
using JetBrains.Annotations;

namespace PowerTransmitterPlus
{
    // Single source of truth: the VisualizerIntensity setter on WirelessPower.
    // Vanilla's Activate/SetMaterialPropertiesForIntensity both flow through
    // this value, so observing it gives us correct on/off AND the current
    // alpha / power-level. Fires from a ThreadPool worker during PowerTick;
    // BeamManager routes everything to the main thread.
    [HarmonyPatch(typeof(WirelessPower), "VisualizerIntensity", MethodType.Setter)]
    public static class VisualizerIntensitySetterPatch
    {
        [UsedImplicitly]
        public static void Postfix(WirelessPower __instance, float value)
        {
            if (__instance is PowerTransmitter transmitter)
                BeamManager.SetLineIntensity(transmitter, value);
        }
    }
}
