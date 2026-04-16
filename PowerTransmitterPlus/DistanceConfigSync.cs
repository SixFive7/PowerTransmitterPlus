using Assets.Scripts.Networking;
using HarmonyLib;
using JetBrains.Annotations;
using LaunchPadBooster.Networking;

namespace PowerTransmitterPlus
{
    // Server-authoritative distance-cost (k) sync.
    //
    // - Local config value is the source of truth ON THE HOST (and in single-player).
    // - On a multiplayer client, the host's value is pushed via DistanceConfigMessage
    //   on connect and on every subsequent SettingChanged event.
    // - GetEffectiveK() returns the right value for the current side: host config
    //   on host or single-player; synced (or local fallback) on client.
    //
    // The simulation patches (UsePower / GetUsedPower / ReceivePower / GetGeneratedPower)
    // run only on the server, so the gameplay number is always the host's. Clients
    // need this only so their tablet/IC10 readouts compute matching display values.
    internal static class DistanceConfigSync
    {
        // Last value broadcast by the host. Null until the first message arrives.
        // Falls back to local config when null so client display still works
        // before the first sync completes.
        private static float? _syncedHostK;

        internal static float GetEffectiveK()
        {
            var local = PowerTransmitterPlusPlugin.DistanceCostFactor?.Value ?? 0f;
            // Single-player or host: local config is authoritative.
            if (!NetworkManager.IsActive || NetworkManager.IsServer) return local;
            // Client: prefer the host's pushed value; fall back to local until it arrives.
            return _syncedHostK ?? local;
        }

        internal static void OnHostConfigReceived(float k)
        {
            if (_syncedHostK != k)
            {
                _syncedHostK = k;
                PowerTransmitterPlusPlugin.Log?.LogInfo(
                    $"Received DistanceCostFactor (k) from host: {k:F2}");
            }
        }

        // Called from Plugin.Awake after the BepInEx config is bound. Wires the
        // host-side broadcast: any time the host changes k, push to all clients.
        internal static void HookHostBroadcast()
        {
            var entry = PowerTransmitterPlusPlugin.DistanceCostFactor;
            if (entry == null) return;
            entry.SettingChanged += (_, __) => BroadcastIfHost();
        }

        // Called when a client connects (server-side) AND when the host changes
        // k mid-game. Safe to call on non-host — it short-circuits.
        internal static void BroadcastIfHost()
        {
            if (!NetworkManager.IsServer) return;
            var k = PowerTransmitterPlusPlugin.DistanceCostFactor?.Value ?? 0f;
            new DistanceConfigMessage { K = k }.SendAll(0L);
            PowerTransmitterPlusPlugin.Log?.LogDebug(
                $"Broadcast DistanceCostFactor (k) to clients: {k:F2}");
        }
    }

    // Hook the existing game event for "a client just finished connecting" so we
    // can push the current k to them. LaunchPadBooster has no public event for
    // this — the documented pattern (per LPB authors) is to Harmony-postfix
    // NetworkManager.PlayerConnected. We re-broadcast to everyone on each
    // connect rather than chase the new client's connectionId; the cost is one
    // tiny float message per existing client per join, which is negligible.
    [HarmonyPatch(typeof(NetworkManager), "PlayerConnected")]
    public static class PlayerConnectedSyncPatch
    {
        [UsedImplicitly]
        public static void Postfix()
        {
            DistanceConfigSync.BroadcastIfHost();
            BeamVisualConfigSync.BroadcastIfHost();
        }
    }
}
