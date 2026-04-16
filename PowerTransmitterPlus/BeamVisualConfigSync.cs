using Assets.Scripts.Networking;
using LaunchPadBooster.Networking;

namespace PowerTransmitterPlus
{
    // Server-authoritative visual config sync. The host's beam visual settings
    // always override client-local config in multiplayer. Mirrors the
    // DistanceConfigSync pattern: host pushes values on connect and on every
    // visual config change; clients store them and return them from
    // GetEffective* methods.
    internal static class BeamVisualConfigSync
    {
        private static bool _received;
        private static float _syncedBeamWidth;
        private static string _syncedBeamColorHex;
        private static float _syncedEmissionIntensity;
        private static float _syncedStripeWavelength;
        private static float _syncedScrollSpeed;

        private static bool UseHostValues =>
            _received && NetworkManager.IsActive && !NetworkManager.IsServer;

        internal static float GetEffectiveBeamWidth()
        {
            if (UseHostValues) return _syncedBeamWidth;
            return PowerTransmitterPlusPlugin.BeamWidth?.Value ?? 0.1f;
        }

        internal static string GetEffectiveBeamColorHex()
        {
            if (UseHostValues) return _syncedBeamColorHex ?? "000DFF";
            return PowerTransmitterPlusPlugin.BeamColorHex?.Value ?? "000DFF";
        }

        internal static float GetEffectiveEmissionIntensity()
        {
            if (UseHostValues) return _syncedEmissionIntensity;
            return PowerTransmitterPlusPlugin.EmissionIntensity?.Value ?? 10f;
        }

        internal static float GetEffectiveStripeWavelength()
        {
            if (UseHostValues) return _syncedStripeWavelength;
            return PowerTransmitterPlusPlugin.StripeWavelength?.Value ?? 2f;
        }

        internal static float GetEffectiveScrollSpeed()
        {
            if (UseHostValues) return _syncedScrollSpeed;
            return PowerTransmitterPlusPlugin.ScrollSpeed?.Value ?? 25f;
        }

        internal static void OnHostConfigReceived(BeamVisualConfigMessage msg)
        {
            _received = true;
            _syncedBeamWidth = msg.BeamWidth;
            _syncedBeamColorHex = msg.BeamColorHex;
            _syncedEmissionIntensity = msg.EmissionIntensity;
            _syncedStripeWavelength = msg.StripeWavelength;
            _syncedScrollSpeed = msg.ScrollSpeed;

            PowerTransmitterPlusPlugin.Log?.LogInfo(
                $"Received host visual config: width={msg.BeamWidth:F2}, color={msg.BeamColorHex}, " +
                $"emission={msg.EmissionIntensity:F1}, wavelength={msg.StripeWavelength:F2}, " +
                $"scroll={msg.ScrollSpeed:F1}");

            BeamManager.InvalidateAllBeams();
        }

        internal static void HookHostBroadcast()
        {
            PowerTransmitterPlusPlugin.BeamWidth.SettingChanged += (_, __) => BroadcastIfHost();
            PowerTransmitterPlusPlugin.BeamColorHex.SettingChanged += (_, __) => BroadcastIfHost();
            PowerTransmitterPlusPlugin.EmissionIntensity.SettingChanged += (_, __) => BroadcastIfHost();
            PowerTransmitterPlusPlugin.StripeWavelength.SettingChanged += (_, __) => BroadcastIfHost();
            PowerTransmitterPlusPlugin.ScrollSpeed.SettingChanged += (_, __) => BroadcastIfHost();
        }

        internal static void BroadcastIfHost()
        {
            if (!NetworkManager.IsServer) return;
            new BeamVisualConfigMessage
            {
                BeamWidth = PowerTransmitterPlusPlugin.BeamWidth?.Value ?? 0.1f,
                BeamColorHex = PowerTransmitterPlusPlugin.BeamColorHex?.Value ?? "000DFF",
                EmissionIntensity = PowerTransmitterPlusPlugin.EmissionIntensity?.Value ?? 10f,
                StripeWavelength = PowerTransmitterPlusPlugin.StripeWavelength?.Value ?? 2f,
                ScrollSpeed = PowerTransmitterPlusPlugin.ScrollSpeed?.Value ?? 25f,
            }.SendAll(0L);
            PowerTransmitterPlusPlugin.Log?.LogDebug("Broadcast visual config to clients");
        }
    }
}
