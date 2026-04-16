using Assets.Scripts.Objects.Electrical;
using Assets.Scripts.Objects.Motherboards;
using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;

namespace PowerTransmitterPlus
{
    // Make CanLogicRead return true for our three slots on both transmitter and
    // receiver, and intercept GetLogicValue to compute the values on the fly
    // from already-synced game state (no caching, no per-tick tracking).
    //
    // Computation:
    //   delivered  = transmitter.OutputNetwork.CurrentLoad
    //                  (the wireless network's actual throughput this tick)
    //   distance   = transmitter._linkedReceiverDistance
    //   k          = DistanceConfigSync.GetEffectiveK()
    //                  (host's value when on a client; local config otherwise)
    //   multiplier = 1 + k * distance / 1000
    //   sourceDraw = delivered * multiplier
    //   loss       = delivered * (multiplier - 1)
    //
    // Receiver path resolves to its LinkedPowerTransmitter and reads from there.
    // When unlinked or no transmission is happening, all values fall to 0
    // naturally because delivered is 0.
    internal static class LogicReadoutCompute
    {
        internal static float GetSourceDraw(PowerTransmitter t) => GetDelivered(t) * GetMultiplier(t);
        internal static float GetDestinationDraw(PowerTransmitter t) => GetDelivered(t);
        internal static float GetTransmissionLoss(PowerTransmitter t) => GetDelivered(t) * (GetMultiplier(t) - 1f);

        // Returns 0 when no transmission is happening (matches the sibling
        // readouts' snap-to-zero convention). Otherwise 1 / multiplier, which
        // is purely a function of distance and k.
        internal static float GetEfficiency(PowerTransmitter t)
        {
            if (GetDelivered(t) <= 0f) return 0f;
            var m = GetMultiplier(t);
            return m > 0f ? 1f / m : 0f;
        }

        private static float GetDelivered(PowerTransmitter t)
        {
            if (t == null || !t.OnOff || t.Error == 1) return 0f;
            if (t.LinkedReceiver == null) return 0f;
            if (t.OutputNetwork == null) return 0f;
            // OutputNetwork on a transmitter is the wireless network it's pumping
            // power into. CurrentLoad is what the receiver's downstream cable
            // network is actually consuming this tick — i.e., delivered.
            return (float)t.OutputNetwork.CurrentLoad;
        }

        private static float GetMultiplier(PowerTransmitter t)
        {
            if (t == null || DistanceCostShared.LinkedDistanceField == null) return 1f;
            var distance = (float)DistanceCostShared.LinkedDistanceField.GetValue(t);
            var k = DistanceConfigSync.GetEffectiveK();
            if (k <= 0f) return 1f;
            var m = 1f + k * distance / 1000f;
            return m < 1f ? 1f : m;
        }
    }

    // CanLogicRead and GetLogicValue are declared on WirelessPower, not on
    // PowerTransmitter/PowerReceiver (those inherit without override). Harmony
    // attribute patching uses DeclaredMethod and won't resolve inherited methods,
    // so we target the base class and branch on instance type.

    [HarmonyPatch(typeof(WirelessPower), nameof(WirelessPower.CanLogicRead))]
    public static class WirelessPowerCanLogicReadPatch
    {
        [UsedImplicitly]
        public static void Postfix(WirelessPower __instance, LogicType logicType, ref bool __result)
        {
            if (!LogicTypeRegistry.IsCustom(logicType)) return;
            if (__instance is PowerTransmitter || __instance is PowerReceiver) __result = true;
        }
    }

    [HarmonyPatch(typeof(WirelessPower), nameof(WirelessPower.GetLogicValue))]
    public static class WirelessPowerGetLogicValuePatch
    {
        [UsedImplicitly]
        public static bool Prefix(WirelessPower __instance, LogicType logicType, ref double __result)
        {
            if (!LogicTypeRegistry.IsCustom(logicType)) return true;

            // AutoAim target is per-dish state (not forwarded through the link),
            // so read it before the transmitter-side resolution below.
            if ((ushort)logicType == LogicTypeRegistry.AutoAimTargetValue)
            {
                __result = (double)AutoAimState.GetCachedTarget(__instance);
                return false;
            }

            // LinkedPartner is also per-dish: TX returns its receiver's id,
            // RX returns its transmitter's id.
            if ((ushort)logicType == LogicTypeRegistry.LinkedPartnerValue)
            {
                if (__instance is PowerTransmitter tx)
                    __result = tx.LinkedReceiver != null ? (double)tx.LinkedReceiver.ReferenceId : 0.0;
                else if (__instance is PowerReceiver rx)
                    __result = rx.LinkedPowerTransmitter != null ? (double)rx.LinkedPowerTransmitter.ReferenceId : 0.0;
                else
                    __result = 0.0;
                return false;
            }

            PowerTransmitter t = null;
            if (__instance is PowerTransmitter transmitter) t = transmitter;
            else if (__instance is PowerReceiver receiver) t = receiver.LinkedPowerTransmitter;
            else return true;

            switch ((ushort)logicType)
            {
                case LogicTypeRegistry.SourceDrawValue:
                    __result = LogicReadoutCompute.GetSourceDraw(t);
                    return false;
                case LogicTypeRegistry.DestinationDrawValue:
                    __result = LogicReadoutCompute.GetDestinationDraw(t);
                    return false;
                case LogicTypeRegistry.TransmissionLossValue:
                    __result = LogicReadoutCompute.GetTransmissionLoss(t);
                    return false;
                case LogicTypeRegistry.EfficiencyValue:
                    __result = LogicReadoutCompute.GetEfficiency(t);
                    return false;
                default:
                    return true;
            }
        }
    }
}
