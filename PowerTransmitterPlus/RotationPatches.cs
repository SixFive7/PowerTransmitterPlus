using Assets.Scripts.Objects.Electrical;
using HarmonyLib;
using JetBrains.Annotations;

namespace PowerTransmitterPlus
{
    // Safety net: if the dish rotates while the beam is visible (e.g. post-lock-on
    // micro-adjustment), the cached endpoint moves with the child transform.
    // These postfixes re-cache endpoints only when a beam is already visible,
    // so inactive transmitters pay only a dictionary lookup per setter call.

    [HarmonyPatch(typeof(WirelessPower), "Horizontal", MethodType.Setter)]
    public static class WirelessPowerHorizontalSetterPatch
    {
        [UsedImplicitly]
        public static void Postfix(WirelessPower __instance)
        {
            if (__instance is PowerTransmitter transmitter)
                BeamManager.RefreshIfVisible(transmitter);
        }
    }

    [HarmonyPatch(typeof(WirelessPower), "Vertical", MethodType.Setter)]
    public static class WirelessPowerVerticalSetterPatch
    {
        [UsedImplicitly]
        public static void Postfix(WirelessPower __instance)
        {
            if (__instance is PowerTransmitter transmitter)
                BeamManager.RefreshIfVisible(transmitter);
        }
    }
}
