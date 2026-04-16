# PowerTransmitterPlus TODO

## Multiplayer sync test (deferred)

Only relevant if playing multiplayer. Verifies the server-authoritative `k` value reaches clients live.

Steps:

1. Host a multiplayer session.
2. Connect a second machine as client.
3. Build + link a Microwave Transmitter/Receiver pair on the client's side, put a load on it.
4. On the client: point a configuration tablet at either dish, note `MicrowaveSourceDraw` value (and the `MicrowaveTransmissionLoss`).
5. On the host: edit `E:\Steam\steamapps\common\Stationeers\BepInEx\config\net.powertransmitterplus.cfg` and change `Cost Factor (k) = 5` to a very different value (e.g. `20`). Save.
6. Confirm the client's tablet readouts shift within a second or two without requiring a rejoin.
7. Also verify new clients joining mid-session immediately receive the host's `k` value (this is handled by the `NetworkManager.PlayerConnected` Harmony postfix → `BroadcastIfHost`).

Reference: `RESEARCH.md` §9 for the sync protocol.
