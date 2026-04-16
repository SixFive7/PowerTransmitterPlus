# PowerTransmitterPlus: Research Reference

Internals reference. How the mod is wired, what each patch does, and the game mechanics the patches build on. Read this before touching anything non-trivial.

Related files:

- `README.md`, `PowerTransmitterPlus/About/About.xml`: end-user documentation.
- `TODO.md`: pending work.
- `git log`, `git status`: recent commits and working state.

---

## 1. Mod identity

| Field | Value |
|---|---|
| Name | PowerTransmitterPlus |
| Display | Power Transmitter Plus |
| Plugin GUID | `net.powertransmitterplus` |
| Author | SixFive7 |
| Custom LogicType reserved band | `6571 - 6599` |
| Safely outside of | vanilla (0-349) and SLE (1000-1830) |
| Target framework | .NET Framework 4.7.2, classic-style csproj |
| Hard dependency | `stationeers.launchpad` (StationeersLaunchPad) |

## 2. What the mod does (one-paragraph summary)

Server-authoritative BepInEx mod enhancing the Microwave Power Transmitter / Receiver pair. Four feature pillars:

1. A visible colored beam between any aligned, linked, powered transmitter / receiver pair.
2. A texture-scroll pulse train along the beam whose speed scales with delivered power (`sqrt(intensity) × configured m/s`).
3. Replacement of the vanilla distance-based capacity derate with a source-draw overhead: per watt delivered, the source pulls `1 + k × distance_km` watts (server-authoritative `k`, live-broadcast on change).
4. Five new LogicTypes on both transmitter and receiver: `MicrowaveSourceDraw` (6571), `MicrowaveDestinationDraw` (6572), `MicrowaveTransmissionLoss` (6573), `MicrowaveEfficiency` (6574), `MicrowaveAutoAimTarget` (6575, writable). Readable from configuration tablet and from IC10 by name. Auto-aim writes a target Thing's ReferenceId and slews the dish via the vanilla servo; `TryContactReceiver` handles link establishment.

The mod preserves vanilla gameplay rules everywhere possible: the `TryContactReceiver` raycast still decides when pairs link (so "obstacle in the path" behavior is intact), the dish slew servo still animates rotations, `LinkedReceiver` / `LinkedPowerTransmitter` are never written directly.

---

## 3. Architecture overview

### 3.1. Threading model (CRITICAL)

`PowerTick.ApplyState()` runs on a UniTask **ThreadPool worker** thread. Methods called from there (`UsePower`, `GetUsedPower`, `ReceivePower`, `GetGeneratedPower`, `VisualizerIntensity` setter) all execute on a background thread. **Any Unity API call from those threads, `new GameObject`, `Shader.Find`, `Transform.position`, `LineRenderer.SetPosition`, `Material.SetXxx`, hard-crashes the native Unity player.**

`MainThreadDispatcher` is a `MonoBehaviour` on a `DontDestroyOnLoad` GameObject. It maintains a `ConcurrentQueue<Action>` drained in `Update()`. Every Harmony postfix that touches Unity API enqueues onto this dispatcher. Closure runs on main thread one frame later. ~1 frame latency, fully safe.

Field reads/writes (managed memory, no Unity P/Invoke) ARE safe from background threads. That's why the `_powerProvided` reflection in `DistanceCostPatches` works without the dispatcher.

### 3.2. Server vs client

Stationeers is **server-authoritative for simulation**. Only the server runs the power tick; clients receive synced state. The `DistanceCostPatches` (4 patches) only meaningfully execute on the server. Clients run the patches but they are no-ops because power-tick code doesn't run on clients.

Detection: `Assets.Scripts.Networking.NetworkManager.IsServer` (true on host or single-player) and `NetworkManager.IsActive` (true in multiplayer either side). Single-player has `IsActive = false`.

Client-side display values for the readouts are computed from already-synced game state (`OutputNetwork.CurrentLoad`, `_linkedReceiverDistance`) plus the host's `k` value. The `k` value is pushed via `DistanceConfigMessage` on `PlayerConnected` and on every `SettingChanged` event.

Auto-aim rides entirely on pre-existing infrastructure: `SetLogicValue` is server-authoritative; `TargetHorizontal` / `TargetVertical` writes set `NetworkUpdateFlags |= 256` which the existing delta-state serialization ships to clients.

### 3.3. Patch family overview

| Family | Files | Purpose |
|---|---|---|
| Visual on/off | `VisualiserPatches.cs`, `RotationPatches.cs` | Show/hide beam tied to game's `VisualizerIntensity` setter; refresh endpoints when dish rotates |
| Power-flow simulation | `DistanceCostPatches.cs` | 4 patches replacing vanilla distance derate with source-draw multiplier |
| Logic readout (UI/IC10) | `LogicReadoutPatches.cs` | `CanLogicRead` postfix + `GetLogicValue` prefix on `WirelessPower` (base class); branches on instance type inside |
| Auto-aim logic write | `AutoAimPatches.cs` | `SetLogicValue` prefix intercepts `MicrowaveAutoAimTarget`; `RotatableBehaviour` target setter postfixes clear the cache on manual override. Per-dish cache via `ConditionalWeakTable` |
| Logic system bootstrap | `Ic10ConstantsPatcher.cs`, `LogicableInitializePatch.cs`, `EnumNamePatches.cs`, `StationpediaPatches.cs` | Teach the game about our `LogicType` values 6571-6575 everywhere the game looks them up by name |
| Multiplayer sync | `DistanceConfigMessage.cs`, `DistanceConfigSync.cs` | Server-authoritative `k` push to clients via LPB networking |
| Foundation | `Plugin.cs`, `MainThreadDispatcher.cs`, `LogicTypeRegistry.cs`, `BeamManager.cs`, `BeamLine.cs`, `BeamPulseTrain.cs` | Wiring, registry, beam GameObjects |

---

## 4. File-by-file walkthroughs

### `Plugin.cs`

`PowerTransmitterPlusPlugin : BaseUnityPlugin`. Hard-depends on `stationeers.launchpad`. GUID `net.powertransmitterplus`.

`Awake()`:
1. Capture `Logger` into `Log`.
2. Bind config entries (see §7 Configuration).
3. Init `MainThreadDispatcher`.
4. Hook `DistanceConfigSync.HookHostBroadcast()` (wires `SettingChanged` → broadcast).
5. Subscribe `Prefab.OnPrefabsLoaded += OnAllModsLoaded`.

`OnAllModsLoaded()` (deferred until SLP finishes loading all mods):
1. Set `MOD.Networking.Required = true`.
2. Register `DistanceConfigMessage` with `MOD.Networking.RegisterMessage<T>()`.
3. `new Harmony(PluginGuid).PatchAll()`.
4. Call `Ic10ConstantsPatcher.Apply()` (must run AFTER PatchAll because it reflects into the game's static array).

### `MainThreadDispatcher.cs`

Singleton `MonoBehaviour` on a `DontDestroyOnLoad` GameObject. `Init()` is idempotent. `Enqueue(Action)` is thread-safe via `ConcurrentQueue<Action>`. `Update()` drains the queue and catches per-action exceptions.

### `BeamManager.cs`

Static class. Holds `Dictionary<PowerTransmitter, BeamLine> Beams`, a `SharedMaterial` (lazy-created with shader-fallback chain), `BeamColor` (parsed from config hex × emission intensity), and `StripeTexture` (lazy-created 32×1 cosine grayscale, repeat-wrapped, used by the pulse train).

Public surface (all enqueue to dispatcher): `SetLineIntensity(transmitter, intensity)` and `RefreshIfVisible(transmitter)`.

### `BeamLine.cs`

Per-transmitter wrapper. Owns a child `GameObject` parented to `transmitter.transform`, a `LineRenderer` with `useWorldSpace = true, positionCount = 2`, and a `BeamPulseTrain` MonoBehaviour. Beam alpha is permanently 1 when visible: the pulse train is the only power-level indicator.

### `BeamPulseTrain.cs`

MonoBehaviour on the BeamLine GameObject. In `Awake`: sets `textureMode = Stretch`, clones material via `_lr.material`, sets `mainTexture = BeamManager.StripeTexture`. In `Update`: reads positions, computes `tiles = distance / wavelength` and `offset = -Time.time * sqrt(intensity) * scrollMps / wavelength`, writes `mainTextureScale` and `mainTextureOffset`. `OnDestroy` destroys the cloned material.

### `VisualiserPatches.cs`

Single Harmony patch on `WirelessPower.VisualizerIntensity` setter. Postfix: if `__instance is PowerTransmitter`, call `BeamManager.SetLineIntensity(...)`. This fires from the ThreadPool worker; safe because `SetLineIntensity` enqueues to the dispatcher.

### `RotationPatches.cs`

Harmony postfixes on `WirelessPower.Horizontal` and `Vertical` setters. If dish is a `PowerTransmitter` and currently visible, call `BeamManager.RefreshIfVisible` to re-cache beam endpoints.

### `DistanceCostPatches.cs`

Four power-tick patches implementing the source-draw overhead model. See §6.2 for the math.

`DistanceCostShared` (static helper): `PowerProvidedField` and `LinkedDistanceField` reflections, `GetWirelessOutputNetwork(t)` via `Traverse` (field-or-property tolerant), `GetMultiplier(t)` reads distance via reflection and multiplies by `DistanceConfigSync.GetEffectiveK()`.

### `LogicTypeRegistry.cs`

Constants for all custom LogicType values (6571-6575). `List<CustomLogicType> All` with name / value / description per entry. `Dictionary<ushort, CustomLogicType> ByValue` index. `IsCustom(LogicType)` and `TryGetName(LogicType, out string)` helpers.

### `DistanceConfigMessage.cs`

`INetworkMessage` from LaunchPadBooster. Single field `float K`. `Process(long hostId)`: if NOT server, calls `DistanceConfigSync.OnHostConfigReceived(K)` (guard against self-echo).

### `DistanceConfigSync.cs`

Static class. Holds `_syncedHostK : float?` (null until first message arrives).

- `GetEffectiveK()`: returns local config when on host or single-player; returns synced value (or local fallback) on client.
- `OnHostConfigReceived(float k)`: stores value, logs change.
- `HookHostBroadcast()`: subscribes to `DistanceCostFactor.SettingChanged` to call `BroadcastIfHost()`.
- `BroadcastIfHost()`: if `IsServer`, builds and `SendAll(0L)`s a `DistanceConfigMessage`.
- `[HarmonyPatch(typeof(NetworkManager), "PlayerConnected")]` postfix calls `BroadcastIfHost()` on every join.

### `Ic10ConstantsPatcher.cs`

Static `Apply()`. One-time reflection injection into `ProgrammableChip.AllConstants` (a `public static ProgrammableChip.Constant[]`). Merges our entries into the array. The MIPS compiler resolves any name token against this array via `OrdinalIgnoreCase` string comparison.

Idempotent guard: `_applied` bool.

### `LogicableInitializePatch.cs`

Postfix on `Logicable.Initialize`. Two steps:

1. Appends to `Logicable.LogicTypes` (`LogicType[]`) and `Logicable.LogicTypeNames` (`string[]`) via reflection. Rebuilds `LogicTypeNamesRedirects` if present.
2. Calls `ExtendEnumCollection(...)` which reflects into `EnumCollections.LogicTypes` and extends `Values`, `ValuesAsInts`, `Names`, `PaddedNames`, and the `<Length>k__BackingField`.

Both steps are required: step 1 drives `Logicable.NextLogicType` cycling; step 2 drives the configuration tablet cartridge UI. See §5.3 pitfall "More than one LogicTypes array".

Idempotent guard: `_injected` bool.

### `EnumNamePatches.cs`

Three Harmony postfixes, all gated on `__result != null`:

1. `Enum.GetName(Type, object)`: for direct `Enum.GetName(typeof(LogicType), value)` calls.
2. `EnumCollection<LogicType, ushort>.GetName`.
3. `EnumCollection<LogicType, ushort>.GetNameFromValue`.

For unknown LogicType values, looks up our registry and substitutes the name. Without these, the UI displays raw integers like "6571" instead of "MicrowaveSourceDraw".

### `LogicReadoutPatches.cs`

`LogicReadoutCompute` static helper:
- `delivered = transmitter.OutputNetwork.CurrentLoad`
- `multiplier = 1 + k × distance / 1000` where `k = DistanceConfigSync.GetEffectiveK()`
- `sourceDraw = delivered × multiplier`
- `loss = delivered × (multiplier − 1)`
- `efficiency = delivered > 0 ? 1 / multiplier : 0`
- All return 0 if `transmitter == null`, `!OnOff`, `Error == 1`, `LinkedReceiver == null`, or `OutputNetwork == null`.

Two patches on the `WirelessPower` base class:
- `WirelessPowerCanLogicReadPatch` (Postfix): returns `true` for custom LogicTypes on `PowerTransmitter` / `PowerReceiver` instances.
- `WirelessPowerGetLogicValuePatch` (Prefix, returns false): reads `MicrowaveAutoAimTarget` from the per-dish cache (before the transmitter resolution path); for the other readouts, `PowerTransmitter` reads `__instance`, `PowerReceiver` resolves through `LinkedPowerTransmitter`.

The base-class targeting is required because `CanLogicRead` / `GetLogicValue` are declared `override` on `WirelessPower` and the subclasses inherit without re-overriding; Harmony's attribute-based lookup uses `AccessTools.DeclaredMethod` which doesn't match inherited methods. See §8 pitfalls.

No caching, no per-tick state. Snap-to-zero is automatic when delivered is 0.

### `AutoAimPatches.cs`

Implements `MicrowaveAutoAimTarget` (LogicType 6575, writable). Writing a Thing's `ReferenceId` aims the dish at it; writing 0 disables. The base-game `TryContactReceiver` raycast decides link establishment.

`AutoAimState` (static):
- `ConditionalWeakTable<WirelessPower, StrongBox<long>> _target`: per-dish cache. Lifetime-tied to the dish instance, self-cleans on GC.
- `[ThreadStatic] bool _suppressReset`: re-entry flag set during our own servo writes.
- `HandleWrite(dish, newId)`:
  - Cache hit → early return.
  - `newId == 0` → cache 0, no aim change.
  - `Thing.Find(newId) == null || target == dish` → return WITHOUT updating cache (so a later rewrite of the same id re-attempts lookup).
  - Otherwise: pivot-to-pivot geometry → set `RotatableBehaviour.TargetHorizontal` / `TargetVertical` under suppression flag.

Geometry, both endpoints are rotation-invariant:
- `from = dish.transform.position` (the placed-structure root; NOT `RayTransform`, which moves with H/V)
- `to = target.transform.position` (NOT `RayTransform` / `DishTarget`: those are `Head` children and swing with the target's current aim)
- `d_local = dish.transform.InverseTransformDirection((to - from).normalized)`
- `V = 0.5 + asin(d_local.y) / π` ∈ `[0, 1]`
- `H = (atan2(d_local.x, d_local.z) / (2π) + 1) mod 1`
- At the poles (`|d_local.y| ≈ 1`), `H` is undefined. Keep the current value.

Four Harmony patches:

1. `WirelessPowerSetLogicValuePatch` (Prefix on `WirelessPower.SetLogicValue`): intercepts LogicType 6575, calls `AutoAimState.HandleWrite`, returns false. Other LogicTypes pass through.
2. `WirelessPowerCanLogicWritePatch` (Postfix on `WirelessPower.CanLogicWrite`): marks 6575 writable on `PowerTransmitter` and `PowerReceiver`.
3. `RotatableTargetHorizontalResetPatch` (Postfix on `RotatableBehaviour.TargetHorizontal` setter): if our suppression flag is off, clears the cached target. Catches manual aim writes from every source (tablet, IC10 `s d0 Horizontal ...`, dish UI buttons, scroll-wheel) because they all funnel through this setter.
4. `RotatableTargetVerticalResetPatch`: same for vertical.

Reading `MicrowaveAutoAimTarget` is handled in `LogicReadoutPatches.cs` (per-dish lookup on `__instance`).

### `StationpediaPatches.cs`

Best-effort Stationpedia integration via `AccessTools.TypeByName` and `TargetMethod()`+`Prepare()`. Adds in-game wiki entries for each custom LogicType. Failure is non-fatal.

---

## 5. Game internals reference (decompiled)

### 5.1. Class hierarchy

```
MonoBehaviour
  Thing
    ...
      Device
        ElectricalInputOutput     ← public CableNetwork InputNetwork; public CableNetwork OutputNetwork;
          WirelessPower            ← public Transform RayTransform; AxleTransform; DishTransform;
                                     double Horizontal { get; set; }; double Vertical { get; set; };
                                     protected PowerTransmitterVisualiser PowerTransmitterVisualiser;
            PowerTransmitter       ← public PowerReceiver LinkedReceiver; private PowerReceiver _linkedReceiver;
                                     private float _linkedReceiverDistance; private float _powerProvided;
                                     public static float MaxPowerTransmission = 5000f;
                                     private static readonly float _MaxTransmitterDistance = 500f;
                                     public AnimationCurve PowerLossOverDistance;
            PowerReceiver          ← public PowerTransmitter LinkedPowerTransmitter;
                                     private PowerTransmitter _linkedPowerTransmitter;
                                     public Transform DishTarget; private float _powerProvided
            PowerTransmitterOmni   ← unrelated, omnidirectional charger
```

`PowerTransmitterVisualiser` lives in **global namespace** (no `Assets.Scripts...` prefix). `Thing.EMISSION_COLOR = Shader.PropertyToID("_EmissionColor")`.

### 5.2. Critical method bodies (verbatim)

`PowerTransmitter.GetGeneratedPower`:
```csharp
public override float GetGeneratedPower(CableNetwork cableNetwork)
{
    if (OutputNetwork == null || Error == 1 || cableNetwork != OutputNetwork) return 0f;
    float num = PowerLossOverDistance.Evaluate(
        Mathf.Clamp01(_linkedReceiverDistance / _MaxTransmitterDistance)) * MaxPowerTransmission;
    if (!OnOff || InputNetwork == null) return 0f;
    return Mathf.Min(MaxPowerTransmission, InputNetwork.PotentialLoad) - num;
}
```

`PowerTransmitter.UsePower`:
```csharp
public override void UsePower(CableNetwork cableNetwork, float powerUsed)
{
    if (Error != 1 && OnOff && cableNetwork == WirelessOutputNetwork)
        _powerProvided += powerUsed;
}
```

`PowerTransmitter.GetUsedPower`:
```csharp
public override float GetUsedPower([NotNull] CableNetwork cableNetwork)
{
    if (InputNetwork == null) base.VisualizerIntensity = 0f;
    if (InputNetwork == null || cableNetwork != InputNetwork) return 0f;
    if (Error == 1) {
        base.VisualizerIntensity = 0f;
        if (!OnOff) return 0f;
        return UsedPower;
    }
    if (!OnOff) return 0f;
    return Mathf.Min(MaxPowerTransmission, _powerProvided);
}
```

`PowerTransmitter.ReceivePower`:
```csharp
public override void ReceivePower(CableNetwork cableNetwork, float powerAdded)
{
    if (InputNetwork == null || cableNetwork == InputNetwork) {
        if (!OnOff || InputNetwork == null) { base.VisualizerIntensity = 0f; return; }
        base.VisualizerIntensity = RocketMath.MapToScale(0f, MaxPowerTransmission, 0f, 1f, powerAdded);
        _powerProvided -= powerAdded;
    }
}
```

`TryContactReceiver` core condition (from `PowerTransmitter.cs`):
```csharp
Physics.Raycast(RayTransform.position, RayTransform.TransformDirection(Vector3.forward), out hit, float.PositiveInfinity)
    && Thing._colliderLookup.TryGetValue(hit.collider, out value) && value is PowerReceiver rx
    && hit.transform == rx.DishTarget
    && RocketMath.Approximately(Vector3.Angle(RayTransform.forward, rx.RayTransform.forward), 180f, 7f)
    && RocketMath.Approximately(Vector3.Angle(RayTransform.right,   rx.RayTransform.right),   180f, 7f)
```

Link requires: raycast hit lands on the receiver's `DishTarget` collider AND both dishes' forward axes anti-parallel AND both right axes anti-parallel, within 7° on each.

### 5.3. Dish transform hierarchy (extracted with UnityPy)

Both `StructurePowerTransmitter` and `StructurePowerTransmitterReceiver` share the same rig:

```
root GameObject                     pos (0, 1, 0)          rot identity
  inner StructureXxx                pos (0, 0, 0)          rot identity
    Rotation     (= AxleTransform)  pos (0, 0.27, 0)       rot identity → runtime Euler(0, H·360°, 0)
      Arm                           pos (0, 0.19, 0)       rot identity
        Head     (= DishTransform)  pos (0, 0.65, 0)       rot identity → runtime Euler(Lerp(90°, -90°, V), 0, 0)
          Line     (= RayTransform)  pos (0, 0.34, 0.03)   rot identity   ← ray origin, moves with H/V
          DishTarget (RX only)       pos (0, 0.33, 0.54)   rot identity   ← link raycast target, moves with H/V
          Transmitter (TX, dish mesh) pos (0, 0.34, 0.76)  rot identity
```

`RayTransform`, `DishTarget`, and `Transmitter` are all children of `Head` and **their world positions change as the dish rotates**. The only positions invariant under H/V rotation are the root GameObject's `transform.position` and the `Rotation` / `Arm` nodes up to `Head`.

Dish reachable space: full sphere via `V ∈ [0, 1]` (nadir → horizon → zenith) and `H ∈ [0, 1)` (full azimuth).

- V=0 (Euler X = +90°): `RayTransform.forward` = `(0, -1, 0)` in root-local = **straight down**
- V=0.5 (Euler X = 0°): `(0, 0, 1)` = horizon forward
- V=1 (Euler X = -90°): `(0, 1, 0)` = **straight up** (the vanilla rest state)

Inverse formula (world direction → (H, V)):
```
d_local = dish.transform.InverseTransformDirection((targetPos - dishPos).normalized)
V       = 0.5 + asin(d_local.y) / π
H       = (atan2(d_local.x, d_local.z) / (2π) + 1) mod 1
```
At the poles (`|d_local.y| ≈ 1`), `H` is undefined. Keep the current value.

Placement: floor-only, four cardinal rotations. `root.up = world.up` always. The math is frame-agnostic via `InverseTransformDirection`, so any future placement orientation continues to work without changes.

### 5.4. Constants table

| Constant | Class | Type | Value | Notes |
|---|---|---|---|---|
| `MaxPowerTransmission` | `PowerTransmitter` | `public static float` | `5000f` | Mutable at runtime |
| `_MaxTransmitterDistance` | `PowerTransmitter` | `private static readonly float` | `500f` | Only used as loss-curve denominator |
| `PowerLossOverDistance` | `PowerTransmitter` | `AnimationCurve` | `(0,0)→(1,1)→(2,1)` linear | `loss = distance × 10 W` capped at 5000 W |
| `BatteryChargeRate` | `AreaPowerControl` | `[SerializeField] float` | `1000f` | APC's max battery-slot charge rate |
| `PowerMaximum` | `BatteryCell` | `public float` | `36000f` | Joule capacity, NOT a rate cap |
| `MaxVoltage` | `Cable` | `float` | `5000f` | Rupture threshold in watts (despite the name) |

### 5.5. Prefab extraction values

Extracted via UnityPy from `rocketstation_Data/sharedassets0.assets`:

**`PowerTransmitterVisualiser` MonoBehaviour serialized values**:
- `InnerColor` = `(1, 1, 1, 1)` white
- `EmissionColor` = `(0, 0.4915, 10, 10)` HDR cyan-blue (`[ColorUsage(false, true)]`); alpha animated 0..1 by Activate/Deactivate.

**`LineRenderer` on child GO `Line` under `.../Rotation/Arm/Head/Line`**:
- `widthMultiplier` = `0.1`
- `alignment` = local
- `colorGradient` baked to semi-transparent red; **never touched at runtime**

**Material `Custom_PowerTransmission` on the LineRenderer**:
- Shader: name **stripped from build**; `Shader.Find("Custom_PowerTransmission")` will NOT find it.
- `_EmissionColor` = `(5.992, 0.188, 0, 1)` HDR orange-red baked. **Overwritten at runtime by DOColor with the MonoBehaviour's cyan-blue EmissionColor**. The baked orange-red is a vestige.

Net result: the visible in-game beam color comes from the MonoBehaviour field, not the baked material.

### 5.6. Logic system anatomy

`LogicType` enum (`Assets.Scripts.Objects.Motherboards.LogicType`): `ushort`, vanilla values 0-349 with one true gap at **159**. Examples: `Power = 1`, `On = 28`, `Charge = 11`, `Horizontal = 20`, `Vertical = 21`, `PowerPotential = 25`, `PowerActual = 26`, `Error = 4`, `PrefabHash = 84`, `ReferenceId = 217`, `PositionX/Y/Z = 76/77/78`.

LogicType is `[ushort]` so values up to 65535 are runtime-legal. The IC10 / MIPS parser resolves name tokens against `ProgrammableChip.AllConstants[].Literal` via `OrdinalIgnoreCase` string comparison, so out-of-enum values WORK as long as we register their names in that array.

`WirelessPower.CanLogicRead` returns true for: Charge, Horizontal, Vertical, PowerPotential, PowerActual, PositionX/Y/Z (plus Device-base ones: Power, On, Error, Mode, RequiredPower, PrefabHash, ReferenceId). `PowerTransmitter` and `PowerReceiver` inherit without override. See pitfall §8.9.

`WirelessPower.GetLogicValue` returns:
- `Horizontal` → `Horizontal × 360.0` (degrees)
- `Vertical` → `Vertical × 180.0`
- `HorizontalRatio` → `Horizontal` (0..1)
- `VerticalRatio` → `Vertical`
- `Charge` → `AvailablePower` = `InputNetwork.PotentialLoad`
- `PowerPotential` → `base.PotentialLoad`
- `PowerActual` → `base.CurrentLoad` = `OutputNetwork.CurrentLoad` ← **delivered watts**
- `PositionX/Y/Z` → `RayTransform.position`

The game keeps **three separate arrays of logic types**:
1. `Logicable.LogicTypes` / `LogicTypeNames`: used by `Logicable.NextLogicType` cycling.
2. `Assets.Scripts.EnumCollections.LogicTypes`: the `EnumCollection<LogicType, ushort>` consumed by `ConfigCartridge` (configuration tablet cartridge).
3. `Assets.Scripts.UI.Motherboard.ScreenDropdownBase.LogicTypes` / `LogicTypeNames`: IC housing on-screen dropdowns.

All three are populated from `Enum.GetValues(typeof(LogicType))` at class load. Extending only `Logicable`'s pair is not enough: the configuration tablet is driven by `EnumCollections.LogicTypes`. This mod extends (1) and (2). (3) is not extended; no path the tablet drives touches it.

`EnumCollection<T1, T2>` lives in `Assets.Scripts`, NOT `Assets.Scripts.Util`. `ProgrammableChip` lives in `Assets.Scripts.Objects.Electrical`, NOT `Motherboards`; `ProgrammableChip.Constant` is a nested `public readonly struct`.

The MIPS compiler resolves name tokens against `ProgrammableChip.AllConstants` directly: verified in-game for both `l r0 d0 MicrowaveSourceDraw` and `lbn ... MicrowaveSourceDraw 0`. The `Hash` field on `Constant` is for the `#hash` MIPS directive, not pure name lookups.

---

## 6. Patch catalog with formulas

### 6.1. Visual patches

| Patch | Target | Type | Effect |
|---|---|---|---|
| `VisualizerIntensitySetterPatch` | `WirelessPower.VisualizerIntensity` (setter) | Postfix | Cast to `PowerTransmitter`; `BeamManager.SetLineIntensity`. Drives beam show/hide and current intensity. |
| `WirelessPowerHorizontalSetterPatch` | `WirelessPower.Horizontal` (setter) | Postfix | If `PowerTransmitter` and beam visible, refresh endpoints |
| `WirelessPowerVerticalSetterPatch` | `WirelessPower.Vertical` (setter) | Postfix | Same |

### 6.2. Distance-cost patches (the four power-tick patches)

Vanilla power tick:

```
WirelessOutputNetwork tick:
  GetGeneratedPower → Min(5000, InputNetwork.PotentialLoad) - distance_loss
  Receiver demands D, gets D up to ceiling
  UsePower(WirelessOutputNetwork, D) → _powerProvided += D

InputNetwork tick:
  GetUsedPower(InputNetwork) → Min(5000, _powerProvided) = D
  ReceivePower(InputNetwork, D) → _powerProvided -= D
                                  → VisualizerIntensity = D / 5000
```

After equilibrium each tick: `_powerProvided` returns to 0.

Patched flow with multiplier `m = 1 + k × dist_m / 1000`:

```
WirelessOutputNetwork tick:
  GetGeneratedPower → Min(5000, InputNetwork.PotentialLoad)   ← patch 1: drop loss
  Receiver demands D, gets D
  UsePower(WirelessOutputNetwork, D) → _powerProvided += D
                                       ← patch 2: also += D × (m-1)
                                       → _powerProvided = D × m

InputNetwork tick:
  GetUsedPower(InputNetwork) → Min(5000, _powerProvided)
                              ← patch 3: lifted to uncapped _powerProvided = D × m
  ReceivePower(InputNetwork, D × m) → _powerProvided -= D × m  (back to 0)
                                      → VisualizerIntensity = (D × m) / 5000
                                      ← patch 4: overridden to D / 5000
```

Energy conservation: `_powerProvided` net-zeros each tick.

| # | Patch class | Target method | Type | What |
|---|---|---|---|---|
| 1 | `GeneratedPowerNoDistanceDeratePatch` | `PowerTransmitter.GetGeneratedPower` | Prefix (return false) | Replicate vanilla guards. Return `Min(MaxPowerTransmission, InputNetwork.PotentialLoad)` with no loss subtraction |
| 2 | `UsePowerInflateDebtPatch` | `PowerTransmitter.UsePower` | Postfix | Skip if powerUsed ≤ 0 / Error / !OnOff / wrong network. Compute multiplier; if > 1, add `powerUsed × (multiplier − 1)` to `_powerProvided` |
| 3 | `GetUsedPowerLiftCapPatch` | `PowerTransmitter.GetUsedPower` | Postfix | Skip if Error / !OnOff / no InputNetwork. Read `_powerProvided`. If `debt > __result`, set `__result = debt` |
| 4 | `ReceivePowerVisualizerFixPatch` | `PowerTransmitter.ReceivePower` | Postfix | Skip if multiplier ≤ 1. Compute `delivered = powerAdded / multiplier`, set `VisualizerIntensity = delivered / MaxPowerTransmission` |

**All four patches are required as a set.** Disabling any one produces observable breakage. See pitfall §8.6.

### 6.3. Logic-readout patches

| # | Patch class | Target | Type |
|---|---|---|---|
| 5 | `WirelessPowerCanLogicReadPatch` | `WirelessPower.CanLogicRead` | Postfix (branches on `__instance is PowerTransmitter / PowerReceiver`) |
| 6 | `WirelessPowerGetLogicValuePatch` | `WirelessPower.GetLogicValue` | Prefix (same branch; reads AutoAim cache per-dish before the transmitter-side resolution) |
| 9 | `LogicableInitializePatch` | `Logicable.Initialize` | Postfix (one-shot, idempotent); also extends `EnumCollections.LogicTypes` in-line |
| 10 | `EnumGetNamePatch` | `Enum.GetName(Type, object)` | Postfix |
| 11 | `EnumCollectionGetNamePatch` | `EnumCollection<LogicType,ushort>.GetName` | Postfix |
| 12 | `EnumCollectionGetNameFromValuePatch` | `EnumCollection<LogicType,ushort>.GetNameFromValue` | Postfix |
| 13 | `StationpediaPopulateLogicVariablesPatch` | `Stationpedia.PopulateLogicVariables` (via `TargetMethod`) | Postfix (Prepare-gated) |
| 14 | `PlayerConnectedSyncPatch` | `NetworkManager.PlayerConnected` | Postfix |

### 6.4. Auto-aim patches

| # | Patch class | Target | Type |
|---|---|---|---|
| 15 | `WirelessPowerSetLogicValuePatch` | `WirelessPower.SetLogicValue` | Prefix (false for `MicrowaveAutoAimTarget`; passes through for everything else) |
| 16 | `WirelessPowerCanLogicWritePatch` | `WirelessPower.CanLogicWrite` | Postfix (marks 6575 writable on TX and RX) |
| 17 | `RotatableTargetHorizontalResetPatch` | `RotatableBehaviour.TargetHorizontal` (setter) | Postfix (clears auto-aim cache on any external override) |
| 18 | `RotatableTargetVerticalResetPatch` | `RotatableBehaviour.TargetVertical` (setter) | Postfix (same) |

### 6.5. Non-Harmony reflection injection

- `Ic10ConstantsPatcher.Apply()`: called from `Plugin.OnAllModsLoaded` after `PatchAll`. Reflects into `ProgrammableChip.AllConstants` (a `public static ProgrammableChip.Constant[]`) and assigns a new merged array. Idempotent.
- `AutoAimState.ParentRotatableField`: one-shot `AccessTools.Field(typeof(RotatableBehaviour), "_parentRotatable")` used by the reset postfixes to map a `RotatableBehaviour` back to its owning `WirelessPower`.

---

## 7. Configuration

All in `BepInEx/config/net.powertransmitterplus.cfg`.

| Section | Key | Default | Description |
|---|---|---|---|
| Visual | `Beam Width` | `0.1` | Thickness in world units. Matches vanilla's prefab `widthMultiplier` |
| Visual | `Beam Color` | `000DFF` | Hex RGB. Normalized cyan-blue from game's runtime emission |
| Visual | `Emission Intensity` | `10.0` | HDR brightness multiplier. Matches game's HDR intensity |
| Pulse | `Stripe Wavelength` | `2.0` | Meters between pulse peaks. World-space, same on 5m and 200m beams |
| Pulse | `Scroll Speed` | `25.0` | m/s at full power (5 kW delivered). Scales with `sqrt(intensity)`; draws above 5 kW (enabled by the distance-cost patches) exceed this |
| Pulse | `Trough Brightness` | `0.5` | 0..1, beam brightness between pulses. Affects cached stripe texture (regenerates on game restart; see pitfall §8.8) |
| Advanced | `Shader Name` | `Legacy Shaders/Particles/Additive` | Falls back through `Particles/Additive`, `Sprites/Default`, `Hidden/Internal-Colored` |
| Distance | `Cost Factor (k)` | `5.0` | **Server-authoritative.** Per-km overhead on source draw |

### k table

`m = 1 + k × dist_m / 1000`. For 200 W receiver demand:

| Distance | k=0.5 | k=1 | k=2 | k=4 | k=5 | k=10 |
|---:|---:|---:|---:|---:|---:|---:|
| 0 m | 200 W | 200 W | 200 W | 200 W | 200 W | 200 W |
| 100 m | 210 W | 220 W | 240 W | 280 W | 300 W | 400 W |
| 500 m | 250 W | 300 W | 400 W | 600 W | 700 W | 1.2 kW |
| 1 km | 300 W | 400 W | 600 W | 1.0 kW | 1.2 kW | 2.2 kW |
| 5 km | 700 W | 1.2 kW | 2.2 kW | 4.2 kW | 5.2 kW | 10.2 kW |
| 10 km | 1.2 kW | 2.2 kW | 4.2 kW | 8.2 kW | 10.2 kW | 20.2 kW |

For 1 kW receiver demand: scale by 5×. For 15 kW: scale by 75×. Default `k=5` gives 1 km = 6:1, 5 km = 26:1.

---

## 8. Pitfalls and gotchas

### 8.1. Beam alpha is permanently 1

The beam's `LineRenderer` color alpha is held at 1 whenever the beam is visible. The pulse train is the sole power-level indicator, not beam dimming. This is the intended design, not a workaround.

### 8.2. PowerTick runs on ThreadPool worker

`PowerTick.ApplyState` runs on a UniTask ThreadPool worker thread. Any Unity API call from that thread will hard-crash the native player. Apply `MainThreadDispatcher` discipline to any Unity-API-touching code paths.

A representative crash stack:
```
Shader.Find → BeamManager.SharedMaterial → BeamLine.ctor → ...
  ← VisualizerIntensitySetterPatch.Postfix
  ← PowerTransmitter.ReceivePower
  ← PowerTick.ConsumePower / ApplyState
  ← CableNetwork.OnPowerTick
  ← Cysharp.Threading.Tasks.SwitchToThreadPoolAwaitable
```

### 8.3. `Material.material` vs `Material.sharedMaterial`

`renderer.material` getter clones the shared material on first access and caches the clone on the renderer (subsequent reads return the same clone). `renderer.sharedMaterial` gives the original. Use `_lr.material` once in `Awake` to get a per-instance copy, store the reference. Must `Destroy(_instanceMaterial)` in `OnDestroy` to avoid a leak.

### 8.4. Namespaces that are easy to get wrong

| Type | Namespace | Common mistake |
|---|---|---|
| `EnumCollection<,>` | `Assets.Scripts` | Not `Assets.Scripts.Util` |
| `ProgrammableChip` | `Assets.Scripts.Objects.Electrical` | Not `Motherboards` |
| `ProgrammableChip.Constant` | nested in `ProgrammableChip` | Must qualify as `ProgrammableChip.Constant` |
| `LogicType` | `Assets.Scripts.Objects.Motherboards` | (Most Logic types ARE in Motherboards, just not the chip) |
| `PowerTransmitterVisualiser` | global namespace | NOT `Assets.Scripts.Objects.Electrical` despite the dish being there |

### 8.5. `WirelessOutputNetwork` access

`PowerTransmitter.UsePower` checks `cableNetwork == WirelessOutputNetwork`, but it's unclear whether `WirelessOutputNetwork` is a public field or property. Use `Traverse.Create(t).Field("WirelessOutputNetwork").GetValue<CableNetwork>()` with a property fallback.

### 8.6. `_powerProvided` debt accounting

`_powerProvided` is the debt accumulator between two networks in the vanilla flow. Our four distance-cost patches all depend on each other:

- Patch 2 alone (add to `_powerProvided`) without patch 3 (lift cap): debt grows over time because `Min(5000, ...)` cap means source can't pay the inflated amount.
- Patch 3 alone: no behavior change unless debt is inflated by patch 2.
- Patch 4 alone: visualizer wrong on long beams, gameplay unchanged.
- Patch 1 alone: loss still applied elsewhere, breaks accounting.

**Don't disable any without considering the others.**

### 8.7. `0.202` is float16 quantization

Network serialization quantizes floats to half-precision. `0.2` is not exactly representable; the nearest representable above is `0.2002...`, which prints as `0.202`. Useful for diagnosing "weird value just above the expected" reports.

### 8.8. Stripe trough brightness changes require game restart

`BeamManager.StripeTexture` is created once and cached. Changing `Trough Brightness` only takes effect on game restart (the static `_stripeTexture` field is null then).

### 8.9. Harmony attribute lookup and inherited methods

`[HarmonyPatch(typeof(Subclass), "InheritedMethodName")]` throws `Undefined target method for patch method ...` at `PatchAll` time because HarmonyX's attribute path calls `AccessTools.DeclaredMethod`, which only finds methods declared directly on the target type. If the method is inherited without override, target the class that actually declares the override. Virtual dispatch runs the postfix/prefix for any subclass instance. Applies to `WirelessPower.CanLogicRead` / `GetLogicValue` / `SetLogicValue` / `CanLogicWrite`. All inherited by `PowerTransmitter` and `PowerReceiver` without override.

### 8.10. `DishForward = DishTransform.up` is a lie for aim purposes

`WirelessPower.Vertical` and `Horizontal` setters both update `DishForward = DishTransform.up`, which reads naturally when inferring aim direction. **It is wrong for the raycast.** The base-game link raycast uses `RayTransform.forward`, NOT `DishTransform.up`. These two vectors are ORTHOGONAL in the local Head frame (forward is local +Z, up is local +Y). Using `DishForward` restricts aim to the upper hemisphere only, contradicting observed in-game behavior.

**Rule**: the dish's true aim direction is the raycast's direction vector. Check the actual raycast call site before deriving aim math.

### 8.11. Transforms under `Head` move with dish rotation

`Line` (RayTransform), `DishTarget`, and `Transmitter` are all children of `Head`, which rotates via `DishTransform.localRotation = Euler(Lerp(90°, -90°, V), 0, 0)`. Their world positions therefore change as the dish rotates. Any aim algorithm that treats them as fixed produces:

- Self-referential error when used as RAY ORIGIN: aim computed from current RayTransform position goes stale once the dish rotates and the RayTransform moves. Observed as ~0.3° drift that prevented link-raycast hits.
- Pose-lock-in when used as RAY TARGET: aiming at the other dish's RayTransform / DishTarget locks onto that dish's CURRENT pose. When the target later rotates to a correct aim, your aim is pointing at empty space.

**Rule**: use `dish.transform.position` (the placement-anchored root) as both origin and target for aim computation. Invariant under all dish rotation.

### 8.12. There is more than one `LogicTypes` array

See §5.6. Extending only `Logicable.LogicTypes` / `LogicTypeNames` is not enough: the configuration tablet cartridge reads `EnumCollections.LogicTypes`. Extend both. `ScreenDropdownBase.LogicTypes` is a third copy used by IC housing screen dropdowns; currently not extended and no missing behavior reported.

---

## 9. Multiplayer architecture

### 9.1. LaunchPadBooster networking primitives

`LaunchPadBooster.Networking.IModNetworking` exposes:
- `bool Required { get; set; }`: mod-version handshake rejects clients without matching install.
- `void RegisterMessage<T>() where T : INetworkMessage, new()`.

There are NO public connect/disconnect events; Harmony-patch `NetworkManager.PlayerConnected` as the documented workaround.

`Mod.SetMultiplayerRequired()` exists but is `[Obsolete(error: true)]`. Use `MOD.Networking.Required = true` instead.

`INetworkMessage` extension methods (in `LaunchPadBooster.Networking.ModNetworkingExtensions`):
- `void SendToHost()`: client → server
- `void SendToClient(Client client)`: server → specific client
- `void SendDirect(long connectionId, ConnectionMethod method)`: low-level
- `void SendAll(long excludeConnectionId)`: server → all clients. Pass `0L` for "no exclusion"; there is no zero-arg overload.

`INetworkMessage.Process(long hostId)`: `hostId` is the connection ID of the peer who sent the message. On a client receiving a host broadcast, `hostId` = host's connection ID.

### 9.2. Distance-cost `k` sync protocol

```
Host:
  On DistanceCostFactor.SettingChanged     → DistanceConfigSync.BroadcastIfHost()
  On NetworkManager.PlayerConnected (postfix) → BroadcastIfHost()
  BroadcastIfHost(): if IsServer, new DistanceConfigMessage{K=k}.SendAll(0L)

Client:
  DistanceConfigMessage.Process(hostId):
    if !IsServer, DistanceConfigSync.OnHostConfigReceived(K)
  OnHostConfigReceived(k): _syncedHostK = k

Effective k decision:
  !NetworkManager.IsActive  → local (single-player)
  IsServer                  → local (host)
  else (client)             → _syncedHostK ?? local
```

### 9.3. Auto-aim sync (no new infrastructure)

`WirelessPower.SetLogicValue` is server-authoritative in vanilla. Our prefix runs on the server. The ensuing writes to `RotatableBehaviour.TargetHorizontal` / `TargetVertical` set `NetworkUpdateFlags |= 256`, which the existing delta-state serialization ships to clients. `WirelessPower.ProcessUpdate` reads the flag and writes those targets on the client; the client's local servo then slews the dish. No new `INetworkMessage`.

### 9.4. Why on-the-fly (not cached) computation for readouts

Readouts compute directly from `OutputNetwork.CurrentLoad` and `_linkedReceiverDistance` in the `GetLogicValue` prefix. Both are already client-synced via cable network and wireless link state respectively. Clients have everything they need to display the same numbers as the server given matching `k`.

No `PowerStatsTracker` dictionary, no per-tick stamping, no age-out. Snap-to-zero is automatic when delivered = 0.

---

## 10. Reference patterns from other mods

### 10.1. Stationeers Logic Extended (SLE, Workshop ID 3625190467)

Author: ThunderDuck. Establishes the pattern for mod-authored custom LogicTypes. This mod adopts that pattern in full:
- Registry of `LogicTypeInfo` entries, hardcoded inline.
- Reflection injection into `ProgrammableChip.AllConstants`.
- Postfix on `Logicable.Initialize` to extend tablet UI arrays.
- Postfix on `Enum.GetName` and `EnumCollection<LogicType, ushort>.GetName / GetNameFromValue`.
- Per-device `CanLogicRead` postfix + `GetLogicValue` prefix.
- Postfix on `Stationpedia.PopulateLogicVariables`.

SLE has NO public extensibility API. Every mod that wants custom LogicTypes reimplements the registration pattern from scratch.

`Animator.StringToHash(name)` is the value stored in `Constant.Hash`, used for the `#hash` MIPS directive; pure name lookups don't require it.

### 10.2. SprayPaintPlus (same author, earlier mod)

Adopted patterns:
- BepInPlugin skeleton: `[BepInDependency("stationeers.launchpad", HardDependency)]`, `BaseUnityPlugin`, `Prefab.OnPrefabsLoaded += OnAllModsLoaded`.
- Harmony attribute-based patching with `[UsedImplicitly]` on Postfix/Prefix methods.
- `INetworkMessage` Serialize/Deserialize/Process protocol.
- `MOD.Networking.Required = true; MOD.Networking.RegisterMessage<T>()`.
- About.xml with `ModID` matching `PluginGuid`, `InGameDescription` with TMP rich-text.

SprayPaintPlus only does client→server messages; this mod is the first in the collection with a server→client broadcast (the `k` sync).

---

## 11. Design decisions (durable)

### 11.1. Applied

| Decision | Rationale |
|---|---|
| Beam color `000DFF` × intensity `10.0` | Extracted from game's runtime EmissionColor (cyan-blue HDR `(0, 0.4915, 10)`) |
| Beam width `0.1` | Matches game's prefab `widthMultiplier` exactly |
| Beam shader `Legacy Shaders/Particles/Additive` | Glowy laser look, no HDR-bloom dependency |
| Beam alpha permanently 1 | Beam = "link is up" indicator; the pulse train carries throughput information |
| Pulse train via texture scroll, not gradient | Length-invariant; constant world-space wavelength on any beam length |
| Pulse intensity ramp `sqrt(intensity)` | Vanilla `VisualizerIntensity` rarely exceeds 0.3 in real bases; `sqrt` makes low values still visible |
| Scroll speed default `25.0 m/s` | At 5 kW (intensity = 1) this is clearly energetic; the distance-cost patches allow > 5 kW, which pushes speed higher organically |
| `k = 5` distance-cost default | Gives 1 km = 6:1, 5 km = 26:1; meaningful but not punishing |
| LogicType values `6571 - 6575` (reserved `6571 - 6599`) | Safely outside vanilla (0-349) and SLE (1000-1830) |
| `MicrowaveDestinationDraw` added (redundant with `PowerActual`) | Clearer naming on receiver side |
| `MicrowaveEfficiency` as a fourth readout | Ratio of delivered/source purely derivable from distance and `k`, but convenient to expose directly rather than requiring an IC10 division every tick |
| Pivot-to-pivot aim geometry, NOT `RayTransform` or `DishTarget` | `RayTransform` / `DishTarget` are Head children. Their world positions swing with dish rotation. Aiming from or at them produces self-referential error and locks aim onto the target's CURRENT pose. `dish.transform.position` → `target.transform.position` makes both endpoints rotation-invariant, so a dish targets correctly even when the other side is still pointing the wrong way |
| Don't touch `LinkedReceiver` / `LinkedPowerTransmitter` from auto-aim | The vanilla `TryContactReceiver` raycast handles link/unlink based on alignment, including the "obstacle C in the path" case. Writing link fields directly bypasses the physics check |
| Multiplayer server broadcast (rather than client-side config) | Guarantees all clients see the same gameplay numbers as the host |
| `MOD.Networking.Required = true` | LaunchPad version handshake catches clients with missing or mismatched installs |

### 11.2. Rejected or deferred

- Custom shader for in-beam pulse animation matching the vanilla `Custom_PowerTransmission`: the vanilla shader's name is stripped from the build, so `Shader.Find` can't resolve it. Texture-scroll on the standard additive shader is the workaround and is sufficient.
- MIPS name registration for vanilla value `159` (the unused slot): using our own `6571+` band is cleaner.
- `RotationPatches` on `WirelessOutputNetwork` field changes: unnecessary since beam endpoints come from `RayTransform`, which is a Transform reference.
