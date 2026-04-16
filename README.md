# Power Transmitter Plus [StationeersLaunchPad]

![Power Transmitter Plus](PowerTransmitterPlus/About/Preview.png)

A Stationeers mod that gives the Microwave Power Transmitter a visible laser beam with scrolling energy pulses, replaces the vanilla distance-based capacity derate with a configurable source-draw overhead, adds IC10-driven auto-aim, and exposes four new logic readouts.

> **WARNING:** This is a StationeersLaunchPad mod. It requires [BepInEx](https://docs.bepinex.dev/) and [StationeersLaunchPad](https://github.com/StationeersLaunchPad/StationeersLaunchPad) to be installed.

## Installation

1. Copy `PowerTransmitterPlus.dll` and the `About/` folder into your Stationeers local mods directory
2. Restart the game

## Features

### Visible Laser Beam
A colored beam is drawn between a linked transmitter and receiver whenever the link is up and powered. The beam is at full brightness at all times while visible, so you can see at a glance which dishes are actually connected.

### Scrolling Pulse Train
Energy pulses scroll along the beam from transmitter to receiver. Stripe spacing is constant in world meters (same look on a 5m link or a 500m link). Scroll speed scales with power throughput, so a lightly-loaded link is clearly distinguishable from a heavily-loaded one at a glance.

### Distance Cost
Vanilla Stationeers caps microwave transmission capacity based on distance and hard-stops at 500m. This mod removes that cap entirely and replaces it with a source-draw overhead: the transmitter pulls more from its source cable network per watt delivered, proportional to link distance. Formula: for every watt delivered, the source pulls `1 + k * distance_km` watts. With the default `k = 5`:

| Distance | Watts drawn per watt delivered |
|---:|---:|
| 0 m | 1.0 |
| 100 m | 1.5 |
| 500 m | 3.5 |
| 1 km | 6.0 |
| 5 km | 26.0 |

You can transmit any distance you like, but long-range transmission is paid for in waste heat at the source.

### Logic Readouts
Four new logic types are available on both the transmitter and the receiver, readable from configuration tablets and from IC10:

| Name | Value | Units |
|---|---:|---|
| `MicrowaveSourceDraw` | 6571 | watts pulled from the source cable network |
| `MicrowaveDestinationDraw` | 6572 | watts delivered to the receiver's cable network |
| `MicrowaveTransmissionLoss` | 6573 | source minus destination (watts lost to distance) |
| `MicrowaveEfficiency` | 6574 | delivered / source as a 0..1 ratio |

All four return 0 when the link is down, the device is off, or no power is flowing.

### Auto-Aim
A fifth logic type, `MicrowaveAutoAimTarget` (value 6575), is **writable** on both transmitter and receiver. Write a Thing's `ReferenceId` and the dish slews to point at it via the built-in servo; the base game's line-of-sight link raycast decides when the actual pairing forms, so obstacles in the path still take priority like in vanilla.

| Name | Value | Behavior |
|---|---:|---|
| `MicrowaveAutoAimTarget` | 6575 | Write: target's ReferenceId (0 disables). Read: current target id |

Auto-aim is per-dish and one-sided: setting the target on a transmitter does not touch its receiver, and vice versa. Manually adjusting `Horizontal` or `Vertical` (player, tablet, or IC10 `s d0 Horizontal ...`) cancels auto-aim. Writing 0 disables without moving the dish. Writing an unresolved id is a no-op. This enables logic like switching the transmit target when remote batteries fill up:

```
define self_ref 12345     # this transmitter's ReferenceId
define batt_a  67890      # receiver A's ReferenceId
define batt_b  67891      # receiver B's ReferenceId

loop:
l r0 d0 Setting           # current charge ratio read from some sensor
blt r0 0.99 aim_a
s d0 MicrowaveAutoAimTarget batt_b
j wait
aim_a:
s d0 MicrowaveAutoAimTarget batt_a
wait:
yield
j loop
```

Example IC10 reading a single named transmitter on the data network:

```
define trans HASH("StructurePowerTransmitter")
define name HASH("Silicon Power Transmitter")

start:
lbn r0 trans name MicrowaveSourceDraw 0
lbn r1 trans name MicrowaveDestinationDraw 0
lbn r2 trans name MicrowaveTransmissionLoss 0
yield
j start
```

### Settings

All features are configurable via the mod settings panel.

**Client settings** (visual preference, each player sets independently):

| Setting | Default | Description |
|---|---|---|
| Beam Width | 0.1 | Thickness of the beam in world meters |
| Beam Color | 000DFF | Hex RGB color. 000DFF is the vanilla cyan-blue |
| Emission Intensity | 10.0 | HDR brightness multiplier on the beam color |
| Stripe Wavelength | 2.0 | Distance in world meters between one pulse and the next |
| Scroll Speed | 25.0 | Pulse scroll speed in world meters per second at full power (5 kW delivered). Scales with `sqrt(intensity)` so low loads still move visibly |
| Trough Brightness | 0.5 | Beam brightness between pulses, 0..1. Regenerates on game restart |
| Shader Name | Legacy Shaders/Particles/Additive | Unity shader used for the beam. Fallbacks are tried automatically if missing |

**Server settings** (the host's value controls gameplay for everyone):

| Setting | Default | Description |
|---|---|---|
| Cost Factor (k) | 5.0 | Per-kilometer overhead on transmitter source draw. `k = 0` disables the overhead entirely; `k = 10` doubles it compared to the default. Live-broadcast to all connected clients when changed |

## Compatibility

**Requires:** BepInEx + StationeersLaunchPad

**All players** on a server must have Power Transmitter Plus installed. Matching mod versions are enforced during the connection handshake automatically.

**Dedicated servers** need the same BepInEx + StationeersLaunchPad + PowerTransmitterPlus setup installed server-side. The distance-cost simulation runs server-authoritatively and the handshake rejects mixed installs.

**Custom logic type range** 6571-6599 is reserved by this mod. Any other mod that registers a LogicType in that range will collide. The range sits well outside the vanilla enum (0-349) and the range used by Stationeers Logic Extended (1000-1830).

## Reporting Issues

If you run into a bug or something behaves unexpectedly, please open an issue on [GitHub](https://github.com/SixFive7/PowerTransmitterPlus/issues). It would be greatly appreciated. Steam comment notifications don't always come through, so GitHub is the reliable way to make sure a report is seen.

## Credits

- **ThunderDuck**: Created [Stationeers Logic Extended](https://steamcommunity.com/sharedfiles/filedetails/?id=3625190467), which pioneered the pattern for registering custom LogicType values. The injection approach used here (extending `ProgrammableChip.AllConstants`, `Logicable.LogicTypes`, and the enum-name lookup paths) is adapted from that work.


## License

Apache License 2.0. See [LICENSE](LICENSE) for the full text and [NOTICE](NOTICE) for attribution.
