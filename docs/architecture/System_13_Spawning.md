# System 13: NPC & Static Object Spawning

> Part of the [WorldServerV2 Architecture](./Overview.md) suite.
> Related: [Glossary](./Glossary.md) · [System 1: Game Data Pipeline](./System_01_GameData.md) · [System 2: Entity Model](./System_02_EntityModel.md) · [System 3: World Topology](./System_03_WorldTopology.md)

NPCs (`Creature`, sent as `F_CREATE_MONSTER`) and static world objects (`GameObject`,
sent as `F_CREATE_STATIC`) are the two principal non-player entity types that must be
present in the world for any gameplay to function. This system covers their lifecycle:
data loading, cell materialization, visibility notification, respawn, and packet
serialisation.

**Status**: Design complete. Implementation not started.

---

## 20.1 Old WorldServer — Analysis

### What works (preserve the concepts)

| Pattern | Evidence | Rationale |
|---|---|---|
| **Cell-based lazy loading** | `CellMgr.Load()` triggered by `Region.LoadCells(X,Y,1)` when a player enters a cell | Sound: millions of spawn records never allocated for empty regions |
| **Proto / Spawn separation** | `Creature_proto` (shared template) vs `Creature_spawn` (instance placement) | Correct domain model — template is immutable; instance carries world position and overrides |
| **Spatial pre-indexing** | `CellSpawnService` indexes all spawn records by `(regionId, cellX, cellY)` at startup | O(1) cell-load — no linear scan across 100 K spawn records at runtime |
| **Per-viewer packet construction** | `SendMeTo(Player plr)` called per observer on visibility entry | Required by protocol — `F_CREATE_MONSTER` includes observer-specific quest marker state |

### What is broken (do not carry forward)

| Problem | Evidence | Impact |
|---|---|---|
| `Creature : Unit` inherits all interfaces | `LoadInterfaces()` in `Unit.OnLoad()` adds `AiInterface`, `AbilityInterface`, `BuffInterface`, etc. to every vendor NPC | ~23 per-frame Update() calls on entities that will never use them |
| Stats computed inline on `Creature` | 200-line `SetCreatureStats()` with hardcoded career switch blocks | Duplicates logic that belongs in a `StatService`; untestable; can't be reused during combat |
| `SendCreateMonster` is on the entity | `Standard`, `Pet`, `Siege` each override with hardcoded byte sequences | Couples entity to wire protocol; impossible to test independently; breaks on packet format changes |
| No explicit respawn lifecycle | Death → `EvtInterface.AddEvent(Respawn, ...)` in AI/combat code | No central respawn state; event timers outlive the entity; never cleaned up on region stop |
| `CellMgr.Objects` has no synchronisation | `List<Object>` — read by tick, written by `AddObject` which is called from any thread | Latent `ConcurrentModificationException` |
| `RegionMgr.CreateCreature()` calls `new Creature()` directly | No factory, no DI | Untestable; cannot inject services |
| `GameObject : Unit` | `GameObject` inherits a combat-capable base with health, stats, abilities | Most game objects don't fight; the inheritance is wrong for ~99 % of spawned objects |

---

## 20.2 New Design

The existing architecture already establishes all foundational pieces.
Spawning slots cleanly into the cell / region lifecycle.

### Structural overview

```
GameDataStore
└── SpawnData (new domain, §20.5)
    ├── FrozenDictionary<CellKey, IReadOnlyList<SpawnDescriptor>>
    └── FrozenDictionary<CellKey, IReadOnlyList<GameObjectSpawnDescriptor>>

Region (tick thread)
├── Cell.Load(IEntityFactory, SpawnData)
│       ├── foreach SpawnDescriptor         → factory.CreateCreature(descriptor) → region.Place(entity)
│       └── foreach GameObjectSpawnDescriptor → factory.CreateGameObject(descriptor) → region.Place(entity)
├── RespawnScheduler (§20.8)
│       └── PriorityQueue<RespawnEntry, long> — drained first each tick
├── EnqueueSpawn(SpawnDescriptor, zoneId)   ← thread-safe, via Channel<RegionCommand>
└── On visibility-set addition:
        CreateMonsterResponse.From(entity, proto) / CreateStaticResponse.From(entity, proto)
        → session.Send(dto)   ← source-generated binary encoding

IEntityFactory (§20.6)
├── CreateCreature(SpawnDescriptor, oid) → CreatureEntity
│       Components attached from proto flags:
│           has waypoints   → MovementComponent (stub)
│           has AI script   → BrainComponent (stub, System 5)
│           is vendor       → VendorComponent (future)
└── CreateGameObject(GameObjectSpawnDescriptor, oid) → GameObjectEntity
        proto.IsDestructible → DestructibleComponent (§20.9)

Packet DTOs (§20.10)
├── CreateMonsterResponse.From(CreatureEntity, CreatureProto)     → F_CREATE_MONSTER (0x72)
└── CreateStaticResponse.From(GameObjectEntity, GameObjectProto)  → F_CREATE_STATIC (0x71)
    ↓
    GameServerContext source-generated serializer → binary wire encoding
```

### Spawn lifecycle

```
Player enters cell
    ↓
Cell.Load(factory, spawnData)                          ← region thread, synchronous
    foreach SpawnDescriptor         → factory.CreateCreature(descriptor, oid)
    foreach GameObjectSpawnDescriptor → factory.CreateGameObject(descriptor, oid)
    region.Place(entity)  ← entity enters cell

Creature takes lethal damage
    ↓
Combat service calls region.RespawnScheduler.Schedule(descriptor, zoneId, nowMs)
Entity removed, OID returned to pool
  (descriptor.RespawnDelayMs == 0 → no re-queue; temporary spawn is gone)

RespawnScheduler.DrainDue(currentTick)                 ← first step of every region tick
    foreach due entry:
        factory.CreateCreature(entry.Descriptor, oid) → region.Place(entity)

Scripted/programmatic spawn (PQ wave, boss phase, quest trigger)
    ↓
region.EnqueueSpawn(new SpawnDescriptor { Proto = ..., Position = ..., RespawnDelayMs = 0 }, zoneId)
  → SpawnEntity command drained at next tick start → factory.CreateCreature → region.Place

Player enters visibility range of entity               ← existing VisibilitySet mechanism
    ↓
CreateMonsterResponse.From(creature, proto)             or CreateStaticResponse.From(go, proto)
    → session.Send(dto)   ← source-generated encoding
```

---

## 20.3 Key Design Decisions

| Decision | Rationale |
|---|---|
| Spawn data lives in `GameDataStore` as a `SpawnData` domain | Same pattern as `CreatureData` / `ZoneData` — `FrozenDictionary` keyed by `CellKey(regionId, cellX, cellY)`; no new singleton service, no static class |
| `Cell.Load()` runs on the region thread (called from visibility update) | Already true in V1. V2 preserves this — no separate spawning thread, no cross-thread OID contention. OID assignment from the region pool is thread-safe on the region thread via direct `Stack<ushort>` access. |
| `SpawnDescriptor` is the single factory input for both DB-driven and programmatic spawns | Unifies the two spawn paths through one interface. DB records produce `SpawnDescriptor` during `SpawnDataProvider` load; scripted systems construct them directly. The factory, respawn scheduler, and region command type all operate on `SpawnDescriptor` exclusively — no overloads. |
| `IEntityFactory` interface with DI | `StatService` (System 4) can be injected when built; until then the factory derives HP/Level directly from the proto. Same two-phase evolution as `PlayerInitPipeline`. |
| `RespawnScheduler` as a `PriorityQueue<RespawnEntry, long>` on `Region` | Dead creatures should not tick. A region-level priority queue drains only due entries — zero cost for empty queues. Draining first in the tick (before entity updates) keeps ordering deterministic. Temporary spawns (`RespawnDelayMs == 0`) are never re-queued. |
| Static `From()` mapping on DTOs, **not on the entity** | Entity remains a data holder. Binary serialization is source-generated (`[PacketSerializerContext]` infrastructure). Mapping logic lives as static `From(entity, proto)` methods on opcode-named DTOs (e.g. `CreateMonsterResponse`, `CreateStaticResponse`), matching the project-wide convention. No injected builder service is needed. |
| `GameObjectEntity : WorldEntity` (not `UnitEntity`) | Already correctly modelled in V2. GameObjects don't have stats. Destructible GOs get an optional `DestructibleComponent`. |
| Full synchronous cell load (no per-tick batch limit) | Matches V1 behaviour. Profile-then-optimise: if large cells create measurable tick spikes, add a `PendingSpawnQueue` draining N entities per tick at that point. |

---

## 20.4 Data & Threading Model

| Concern | V1 | V2 |
|---|---|---|
| Spawn index | `static CellSpawnService._RegionCells[,]` | `GameDataStore.Spawns` — `FrozenDictionary<CellKey, ...>` |
| Cell load trigger | `CellMgr.AddObject(Player)` → `Region.LoadCells(X,Y,1)` | Same — player cell transition in visibility update triggers `Cell.Load()` |
| DB spawn → entity | `new Creature(spawn)` inline in `RegionMgr` | `SpawnDataProvider` wraps DB record in `SpawnDescriptor`; `IEntityFactory.CreateCreature(descriptor)` — DI, testable |
| Programmatic spawn → entity | Ad-hoc `new Creature(...)` in scripts | `SpawnDescriptor` constructed directly; same factory path |
| OID assignment during load | Linear scan of 65 K array | Direct `Stack<ushort>` pop on region thread — O(1) |
| Respawn | `EvtInterface.AddEvent(Respawn, delay)` on the entity | `RespawnScheduler.Schedule(descriptor, zoneId, delay)` on the region |
| Create packet | Virtual `SendMeTo(Player)` override per subclass | Static `CreateMonsterResponse.From(entity, proto)` / `CreateStaticResponse.From(entity, proto)` on opcode-named DTOs; source-generated binary serializer encodes it |
| Thread safety of cell entity list | No synchronisation (`List<Object>`) | Cell entity list is only written on region thread — consistent with all other region state mutations |
| Cross-thread programmatic spawn | Ad hoc / unsafe | `Region.EnqueueSpawn(SpawnDescriptor, zoneId)` enqueues a `SpawnEntity` command via the existing `Channel<RegionCommand>` |

---

## 20.5 SpawnData Domain & SpawnDescriptor

### SpawnData (game data store domain)

`SpawnData` is a new domain in `GameDataStore`, following the
`IDataProvider<SpawnData>` pattern:

```csharp
public sealed record SpawnData(
    FrozenDictionary<CellKey, IReadOnlyList<SpawnDescriptor>>   CreaturesByCell,
    FrozenDictionary<CellKey, IReadOnlyList<GameObjectSpawnDescriptor>> GameObjectsByCell
);

// CellKey is a value type: (ushort RegionId, ushort CellX, ushort CellY)
public readonly record struct CellKey(ushort RegionId, ushort CellX, ushort CellY);
```

`SpawnDataProvider` loads `creature_spawns` and `gameobject_spawns` from the DB via
EF Core, cross-links each record to its `CreatureProto` / `GameObjectProto`, converts
to `SpawnDescriptor` / `GameObjectSpawnDescriptor`, and buckets by cell:
```
cellX = (ushort)(worldX >> 12)
cellY = (ushort)(worldY >> 12)
```

Invalid spawns (missing proto, invalid zone) are logged and skipped — no hard fail.

### SpawnDescriptor (unified spawn input)

`SpawnDescriptor` is the single input type accepted by `IEntityFactory`.
It carries everything needed to instantiate a `CreatureEntity` regardless of
whether the spawn originated from a DB record or was constructed programmatically:

```csharp
public sealed record SpawnDescriptor
{
    /// <summary>The creature template. Always required.</summary>
    public required CreatureProto Proto { get; init; }

    /// <summary>World position, heading, and zone at spawn time.</summary>
    public required WorldPosition Position { get; init; }

    /// <summary>
    /// Overrides the proto's level range when set.
    /// Null = derive level randomly from [Proto.MinLevel, Proto.MaxLevel].
    /// </summary>
    public byte? LevelOverride { get; init; }

    /// <summary>
    /// Faction override. Null = use Proto.Faction.
    /// Used by RvR scenarios to spawn the same proto for different realms.
    /// </summary>
    public byte? FactionOverride { get; init; }

    /// <summary>
    /// Emote to play on spawn. Null = use Proto.Emote.
    /// </summary>
    public byte? EmoteOverride { get; init; }

    /// <summary>
    /// Milliseconds before this creature respawns after death.
    /// 0 = no respawn (temporary spawn — removed permanently on death).
    /// </summary>
    public int RespawnDelayMs { get; init; }

    /// <summary>
    /// Guid of the originating DB spawn record, if any.
    /// Null for programmatically constructed spawns.
    /// Used for logging and diagnostics only — not required for behaviour.
    /// </summary>
    public uint? DbSpawnGuid { get; init; }
}
```

The same principle applies to game objects:

```csharp
public sealed record GameObjectSpawnDescriptor
{
    public required GameObjectProto Proto    { get; init; }
    public required WorldPosition   Position { get; init; }
    public byte  VfxState                   { get; init; }
    public uint? DbSpawnGuid                { get; init; }
}
```

---

## 20.6 IEntityFactory

```csharp
public interface IEntityFactory
{
    CreatureEntity   CreateCreature(SpawnDescriptor descriptor, ushort oid);
    GameObjectEntity CreateGameObject(GameObjectSpawnDescriptor descriptor, ushort oid);
}
```

`EntityFactory` (concrete impl):

- **`CreateCreature`**: constructs `CreatureEntity`, applies `LevelOverride` or derives
  level from `[Proto.MinLevel, Proto.MaxLevel]` range, derives scale and initial HP from
  proto (replaced by `StatService` in System 4), sets `Position` from descriptor,
  attaches optional components based on proto flags. Returns a fully initialised entity
  ready for `region.Place()`.

- **`CreateGameObject`**: constructs `GameObjectEntity`, sets `Entry`, `VfxState`,
  `Interactable`, `Position`. Attaches `DestructibleComponent` if `proto.IsDestructible`.

**Component attachment policy** (factory only attaches; entities never self-attach):

| Condition | Component attached |
|---|---|
| `proto.IsDestructible` | `DestructibleComponent(proto.HealthPoints, proto.DoorId)` |
| `proto.WaypointCount > 0` | `MovementComponent` (stub — System 5) |
| `proto.ScriptName != null` | `BrainComponent` (stub — System 5) |
| `proto.VendorId > 0` | `VendorComponent` (stub — future) |

---

## 20.7 Dynamic Spawning API

Any game system can spawn a creature at runtime by constructing a `SpawnDescriptor`
and calling into the region. The factory path is identical to the cell-load path —
the only difference is the origin of the descriptor.

### Region API

```csharp
public partial class Region
{
    // Thread-safe: enqueues a SpawnEntity command via the existing Channel<RegionCommand>.
    // Use from handler threads, PQ controllers, AI scripts, etc.
    public void EnqueueSpawn(SpawnDescriptor descriptor, ushort zoneId) { ... }

    // Region-thread only: immediate placement, bypasses command channel.
    // Use from within the tick loop itself (e.g. boss phase transitions).
    internal void SpawnImmediate(SpawnDescriptor descriptor, ushort zoneId) { ... }
}
```

`SpawnEntity` is a new discriminated case on `RegionCommand`:

```csharp
// Existing RegionCommand union gains:
public sealed record SpawnEntity(SpawnDescriptor Descriptor, ushort ZoneId) : RegionCommand;
```

### Usage examples

```csharp
// PQ wave controller (runs on region thread — SpawnImmediate)
foreach (var entry in wave.Entries)
{
    var descriptor = new SpawnDescriptor
    {
        Proto         = _gameData.Creatures.Protos[entry.ProtoEntry],
        Position      = entry.SpawnPosition,
        LevelOverride = entry.Level,
        RespawnDelayMs = 0    // temporary — no respawn
    };
    region.SpawnImmediate(descriptor, zoneId);
}

// Quest trigger handler (handler thread — EnqueueSpawn)
region.EnqueueSpawn(new SpawnDescriptor
{
    Proto          = _gameData.Creatures.Protos[questSpawnProtoEntry],
    Position       = triggerPosition,
    RespawnDelayMs = 0
}, zoneId);
```

### SpawnDescriptor construction helpers

`SpawnDataProvider` produces descriptors from DB records via a factory method
to avoid scattering the DB→descriptor mapping:

```csharp
public static class SpawnDescriptorFactory
{
    public static SpawnDescriptor FromDbRecord(CreatureSpawn spawn, CreatureProto proto)
        => new()
        {
            Proto          = proto,
            Position       = WorldPosition.FromWorld(spawn.WorldX, spawn.WorldY, spawn.WorldZ,
                                 spawn.WorldO, spawn.ZoneId),
            EmoteOverride  = spawn.Emote != 0 ? spawn.Emote : null,
            FactionOverride = spawn.Faction != 0 ? spawn.Faction : null,
            RespawnDelayMs = proto.RespawnTime * 1000,
            DbSpawnGuid    = spawn.Guid
        };

    public static GameObjectSpawnDescriptor FromDbRecord(GameObjectSpawn spawn, GameObjectProto proto)
        => new()
        {
            Proto       = proto,
            Position    = WorldPosition.FromWorld(spawn.WorldX, spawn.WorldY, spawn.WorldZ,
                              spawn.WorldO, spawn.ZoneId),
            VfxState    = (byte)spawn.VfxState,
            DbSpawnGuid = spawn.Guid
        };
}
```

---

## 20.8 RespawnScheduler

The respawn scheduler stores `SpawnDescriptor` directly — it works for both
DB-sourced and programmatically created spawns. Descriptors with `RespawnDelayMs == 0`
are never re-queued.

```csharp
public sealed class RespawnScheduler
{
    // PriorityQueue orders by due-tick ascending — earliest first.
    private readonly PriorityQueue<RespawnEntry, long> _queue = new();

    // Called by combat/death handling on the region thread.
    public void Schedule(SpawnDescriptor descriptor, ushort zoneId, long nowMs)
    {
        if (descriptor.RespawnDelayMs <= 0) return; // temporary spawn — no respawn
        _queue.Enqueue(
            new RespawnEntry(descriptor, zoneId),
            nowMs + descriptor.RespawnDelayMs);
    }

    // Called at the start of every region tick, before entity updates.
    public void DrainDue(long nowMs, IEntityFactory factory, Region region)
    {
        while (_queue.TryPeek(out _, out long dueAt) && dueAt <= nowMs)
        {
            var (entry, _) = _queue.Dequeue();
            var oid    = region.AllocateOid();
            var entity = factory.CreateCreature(entry.Descriptor, oid);
            region.Place(entity, entry.ZoneId);
        }
    }
}

public sealed record RespawnEntry(SpawnDescriptor Descriptor, ushort ZoneId);
```

Key properties:
- Only the region thread writes to the scheduler (death callback runs on region
  thread; cross-thread death would enqueue a `CreatureDied` command first).
- `PriorityQueue<,>` gives O(log n) enqueue / dequeue.
- No respawn for `GameObjectEntity` — keep door restoration is driven by the
  RvR/Campaign system (System 9), not the spawn scheduler.

---

## 20.9 DestructibleComponent

Applied to `GameObjectEntity` instances whose proto marks them as destructible
(principally keep doors, but also some scenario objectives).

```csharp
public sealed class DestructibleComponent : ComponentBase
{
    public DestructibleComponent(uint maxHealth, uint doorId = 0)
    {
        MaxHealth     = maxHealth;
        CurrentHealth = maxHealth;
        DoorId        = doorId;
    }

    public uint    MaxHealth        { get; private set; }
    public uint    CurrentHealth    { get; private set; }
    public uint    DoorId           { get; init; }   // non-zero for keep doors
    public Realms  ControllingRealm { get; set; }    // keep door realm ownership

    // Returns true if this damage call caused death.
    public bool TakeDamage(uint damage) { ... }
    public void Repair(uint amount)     { ... }
    public void SetHealth(uint hp)      { ... }
}
```

`DestructibleComponent` does **not** extend or wrap `HealthComponent`.
`HealthComponent` derives HP from level/wounds formulas (unit-domain logic);
`DestructibleComponent` uses raw HP values from the proto — the domains are
distinct even though the interface is similar.

`GameObjectEntity` is **not** a `UnitEntity` — the inheritance remains flat.
Only `GameObjectEntity` instances with `DestructibleComponent` attached can take
damage. The combat system checks `entity.TryGet<DestructibleComponent>(out var d)`
before applying damage to any game object.

---

## 20.10 Packet DTOs

Packet construction for spawned entities uses the existing source-generated
serialization infrastructure. DTOs are named after their opcodes (matching the
convention of `GetItemResponse`, `CareerAbilityResponse`, etc.) and carry all
mapping logic as static `From*()` methods — no injected builder service is needed.
Binary encoding is handled by the source-generated serializer registered in
`GameServerContext`.

Any business logic (fetching supplementary data from game tables, resolving quest
state, etc.) is handled by services *before* calling `From()`. The `From()` method
itself is pure mapping: it takes the entity and any pre-fetched data objects and
returns a fully populated DTO.

### `CreateMonsterResponse` (F_CREATE_MONSTER, 0x72)

```csharp
public class CreateMonsterResponse
{
    public ushort Oid       { get; set; }
    public ushort Heading   { get; set; }
    public ushort Z         { get; set; }
    public ushort X         { get; set; }
    public ushort Y         { get; set; }
    // ... remaining wire fields ...
    public byte   QuestState { get; set; }

    /// <summary>
    /// Maps a live <see cref="CreatureEntity"/> to an F_CREATE_MONSTER packet.
    /// </summary>
    /// <param name="entity">The creature being observed.</param>
    /// <param name="proto">Prototype data for the creature entry.</param>
    /// <param name="questState">
    ///     Hard-coded to 0 until System 10 (Quests).
    ///     Pass the resolved value once <c>IQuestStateResolver</c> is available.
    /// </param>
    public static CreateMonsterResponse From(
        CreatureEntity entity,
        CreatureProto  proto,
        byte           questState = 0)
    {
        return new CreateMonsterResponse
        {
            Oid       = entity.ObjectId,
            Heading   = entity.Position.Heading,
            Z         = entity.Position.Z,
            X         = entity.Position.X,
            Y         = entity.Position.Y,
            // ...
            QuestState = questState,
        };
    }
}
```

Fields mapped from `Creature.SendCreateMonster(Player)`:

| Field | Source |
|---|---|
| `Oid` | `entity.ObjectId` |
| `Heading`, `Z`, `X`, `Y` | `entity.Position` |
| `ModelId` | `proto.Model1` / `proto.Model2` (random if both set, chosen at factory time and stored on entity) |
| `Scale` | chosen at factory time, stored on entity |
| `Level` | `entity.Level` |
| `Faction` | `(CreatureFlags)entity.Faction` — compound bitfield: realm, sub-type, and behavioral flags |
| `Emote` | `descriptor.EmoteOverride ?? proto.Emote` |
| `UnkFields[1..6]` | `proto._Unks[1..6]` |
| `Title` | `proto.Title` |
| `States[]` | `proto.States` + runtime states (e.g. `IsDeployed` from `SiegeComponent`) |
| `QuestState` | **Hard-coded to 0** until System 10 (Quests) |
| `GenderedName` | `proto.GenderedName` |
| `PostNameBlob` | `proto.FigLeafData` — compound blob: content depends on creature state bits & flags |
| `InteractTrainerType` | `proto.InteractTrainerType` |
| `MovementState` | from `MovementComponent` if present, else stationary default |

### `CreateStaticResponse` (F_CREATE_STATIC, 0x71)

```csharp
public class CreateStaticResponse
{
    public ushort Oid    { get; set; }
    public byte   VfxState { get; set; }
    // ... remaining wire fields ...

    /// <summary>
    /// Maps a live <see cref="GameObjectEntity"/> to an F_CREATE_STATIC packet.
    /// </summary>
    public static CreateStaticResponse From(
        GameObjectEntity entity,
        GameObjectProto  proto)
    {
        return new CreateStaticResponse
        {
            Oid      = entity.ObjectId,
            VfxState = entity.VfxState,
            // ...
        };
    }
}
```

Fields mapped from `GameObject.SendMeTo(Player)`:

| Wire offset | Field | Source | Client notes |
|---|---|---|---|
| +0x00 | `Oid` | `entity.ObjectId` | Unique object identifier |
| +0x02 | `VfxState` | `entity.VfxState` | Visual-effect state |
| +0x04–0x06 | `Heading`, `Z` | `entity.Position` | Facing direction, vertical position |
| +0x08–0x0C | `X`, `Y` | `entity.Position` (zone-local) | Horizontal position |
| +0x10 | `DisplayId` | `descriptor.DisplayId` | Model / visual prototype |
| +0x12 | `UnkHi` | `Unks[0] >> 8` | **Dead** — client ignores |
| +0x13 | `StaticFlags` | `(StaticFlags)(Unks[0] & 0xFF)` | Compound bitfield: realm (bits 0-1), selectable (2), name-suffix (5), bit-flags (4/6/7) |
| +0x14–0x16 | `Unks1`, `Unks2` | `Unks[1]`, `Unks[2]` | Reserved pair — typically 0 in DB |
| +0x18 | `SpawnUnk1` | `descriptor.SpawnUnk1` | Part of level/rank block (game objects use a hardcoded level of 18) |
| +0x19 | `Flags` | computed: interactable (bit 2), attackable (bit 3) | Low byte = ObjectFlags bitfield; HI byte merges with SpawnUnk1 |
| +0x1B | `SpawnUnk2` | `descriptor.SpawnUnk2` | **Dead** — client ignores |
| +0x1C | `SpawnUnk3` | `descriptor.SpawnUnk3` | Byte layout: [dead][dead][Scale (val/50.0)][Variant] |
| +0x20–0x22 | `Unks4`, `Unks5` | `Unks[4]`, `Unks[5]` | Spawn-timestamp components (V1 sends 0) |
| +0x24 | `SpawnUnk4` | `descriptor.SpawnUnk4` | Spawn-timestamp high word (V1 sends 0) |
| +0x28 | `Name` | `proto?.Name ?? entity.Name` | Pascal string |
| after | `DoorIdSection` | 0x00 or {0x04 + DoorId u32 BE} | Door identifier for interactive objects |

**Quest-state handling**: `QuestState` on `CreateMonsterResponse` defaults to `0`
(no quest) for the first pass. When System 10 (Quests) is built, resolve the value
via `IQuestStateResolver` in the region/service layer and pass it into `From()`.
No structural change to the DTO is required.

---

## 20.11 Type Structure

```
src/WorldServerV2/
├── Data/
│   ├── Domain/
│   │   └── SpawnData.cs                    — CellKey, SpawnDescriptor, GameObjectSpawnDescriptor,
│   │                                          FrozenDictionary spawn collections
│   └── Providers/
│       ├── SpawnDataProvider.cs            — loads + cross-links + builds descriptors + buckets by cell
│       └── SpawnDescriptorFactory.cs       — FromDbRecord() helpers (creature + game object)
└── World/
    ├── Spawning/
    │   ├── IEntityFactory.cs               — CreateCreature / CreateGameObject
    │   ├── EntityFactory.cs                — implements factory, attaches components
    │   ├── RespawnScheduler.cs             — PriorityQueue<RespawnEntry, long>, DrainDue()
    │   └── DestructibleComponent.cs        — HP pool for destructible game objects
Network/
└── Dtos/
    ├── CreateMonsterResponse.cs            — F_CREATE_MONSTER (0x72) DTO + static From() mapping
    └── CreateStaticResponse.cs             — F_CREATE_STATIC (0x71) DTO + static From() mapping
```

---

## 20.12 Visibility Integration

The region already knows when entity A enters B's `VisibilitySet` (see [System 3: World Topology](./System_03_WorldTopology.md)).
When a `PlayerEntity` gains a new entity in its visibility set, the region dispatches
the appropriate DTO by calling the static `From()` method on the opcode-named DTO:

```csharp
// Inside Region.UpdateVisibility() — region thread
foreach (var newlyVisible in addedToPlayerView)
{
    switch (newlyVisible)
    {
        case CreatureEntity c:
            var proto = _gameData.Creatures.Protos[c.Entry];
            player.Session.Send(CreateMonsterResponse.From(c, proto));
            break;
        case GameObjectEntity g:
            var goProto = _gameData.GameObjects.Protos[g.Entry];
            player.Session.Send(CreateStaticResponse.From(g, goProto));
            break;
        case PlayerEntity p:
            // TODO: System 14 — F_CREATE_PLAYER DTO (see §20.13)
            break;
    }
}
```

When an entity leaves visibility, an `F_DESTROY_DISPLAY_OBJECT` (opcode `0x0F`) is
sent — same mechanic used today for players leaving range.

---

## 20.13 Tech Debt & Future Work

| Item | Category | Detail |
|---|---|---|
| Stats from `StatService` | Evolution | `EntityFactory.CreateCreature()` currently derives HP/Level directly from proto. When System 4 (Combat & Stats) is built, replace with `statService.ComputeCreatureStats(entity)`. |
| Quest state in `F_CREATE_MONSTER` | Gap | Hard-coded to `0`. When System 10 (Quests) is built, resolve the value via `IQuestStateResolver` in the region/service layer and pass it into `CreateMonsterResponse.From()`. |
| Cell load batching | Optional | If profiling shows large cells spike tick time, add a `PendingSpawnQueue` draining N entities per tick. Not needed until measured. |
| `MovementComponent` and `BrainComponent` stubs | Gap | Factory attaches them based on proto flags, but they carry no behaviour until System 5 (AI) is built. |
| `GameObjectSpawn` / `GameObjectProto` EF Core entities | Gap | Need to be defined in `WorldDbContext` (mirroring `Common.GameObject_spawn` / `Common.GameObject_proto`). |
| `CreatePlayerResponse` DTO | Gap | The visibility integration switch requires an F_CREATE_PLAYER DTO. This is the same packet sent during player init — define `CreatePlayerResponse` with a static `From(PlayerEntity, ...)` method and consolidate both call sites. |
