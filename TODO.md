# PowerTransmitterPlus TODO

## Visual sync multiplayer test (deferred)

Only relevant if playing multiplayer. Verifies the host's beam visual config reaches clients.

Steps:

1. Host a multiplayer session.
2. Connect a second machine as client. Both should have different beam color/width configs locally.
3. Build + link a Microwave Transmitter/Receiver pair. Confirm the client's beams match the host's appearance, not their own local config.
4. On the host: change `Beam Color` to a visibly different hex value. Confirm the client's beam updates within 1-2 seconds.
5. On the host: change `Beam Width` to something visible (e.g. 0.5). Confirm the client's beam width updates.
6. Also verify new clients joining mid-session immediately see the host's visual config.

## MicrowaveLinkedPartner IC10 test (deferred)

Verify the new readout works via tablet and IC10.

Steps:

1. Place a transmitter and receiver, link them.
2. Point a configuration tablet at the transmitter. Confirm `MicrowaveLinkedPartner` shows the receiver's ReferenceId.
3. Point the tablet at the receiver. Confirm it shows the transmitter's ReferenceId.
4. Break the link (move the dish). Confirm both read 0.
5. IC10 test: `l r0 d0 MicrowaveLinkedPartner` on a chip wired to a dish. Confirm it returns the expected value.

## Distance-cost k multiplayer sync test (deferred)

Only relevant if playing multiplayer. Verifies the server-authoritative `k` value reaches clients live.

Steps:

1. Host a multiplayer session.
2. Connect a second machine as client.
3. Build + link a Microwave Transmitter/Receiver pair on the client's side, put a load on it.
4. On the client: point a configuration tablet at either dish, note `MicrowaveSourceDraw` value (and the `MicrowaveTransmissionLoss`).
5. On the host: edit `E:\Steam\steamapps\common\Stationeers\BepInEx\config\net.powertransmitterplus.cfg` and change `Cost Factor (k) = 5` to a very different value (e.g. `20`). Save.
6. Confirm the client's tablet readouts shift within a second or two without requiring a rejoin.
7. Also verify new clients joining mid-session immediately receive the host's `k` value (this is handled by the `NetworkManager.PlayerConnected` Harmony postfix -> `BroadcastIfHost`).

Reference: `RESEARCH.md` section 9 for the sync protocol.
