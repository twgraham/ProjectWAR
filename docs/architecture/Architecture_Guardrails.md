# WorldServerV2 — Architecture Guardrails & Solution Design

> Part of the [WorldServerV2 Architecture](./Overview.md) documentation suite.
>
> **Purpose**: Defines the structural rules, threading model, and data flow patterns
> that all systems in the new server must follow. These are not guidelines — they are
> enforced by assembly boundaries, access modifiers, and the tick pipeline.
>
> **Last updated**: April 2026

---

## Table of Contents

1. [The Three Rules](#1-the-three-rules)
2. [Solution Structure](#2-solution-structure)
3. [Project Responsibilities](#3-project-responsibilities)
4. [Dependency Graph](#4-dependency-graph)
5. [Threading Model](#5-threading-model)
6. [Region Tick Pipeline](#6-region-tick-pipeline)
7. [IRegionAction Extension Model](#7-iregionaction-extension-model)
8. [Data Flow Patterns](#8-data-flow-patterns)
9. [Thread-Safety Guarantees](#9-thread-safety-guarantees)
10. [Region Event Dispatch](#10-region-event-dispatch)
11. [Persistence Model](#11-persistence-model)
12. [Anti-Patterns](#12-anti-patterns)

---

## 1. The Three Rules

Every system in the new server obeys these three rules. They are enforced at the
assembly boundary, not by convention.

| # | Rule | Enforcement |
|---|------|-------------|
| 1 | **The region thread owns all mutable entity state.** | Entity mutation APIs are `internal` to `Core.GameWorld`. Code in `WorldServerV2` (handlers, services) cannot call them. |
| 2 | **Handler threads read public API, enqueue actions via services.** | Services create `IRegionAction` instances and enqueue them via `Region.EnqueueAction()`. Services also implement `IRegionEventHandler<T>` to handle outbound events dispatched by the region (see [§10](#10-region-event-dispatch)). Handlers call services, not regions. |
| 3 | **Background services consume snapshot DTOs.** | Persistence, analytics, and cross-region features receive immutable snapshot copies produced on the region thread, never live entity references. |

---

## 2. Solution Structure

The restructuring is **already underway**. The projects below reflect the current
solution layout. `Core.Domain` and `Core.Session` have been extracted; `Core.GameWorld`
owns the simulation layer. `WorldServerV2/World/` still contains systems that will
migrate into `Core.GameWorld` as each system is refactored.

```
ProjectWAR.slnx
├── src/
│   ├── Core.Domain/                                 ← leaf — EF entities, DbContexts, value objects
│   ├── Core.Session/                                ← session management, ISessionResolver
│   ├── Core.GameWorld/                              ← domain + simulation (growing)
│   ├── WorldServerV2/                               ← host, handlers, services, data
│   │
│   ├── Core.Infrastructure.Network/                 (shared TCP/framing)
│   ├── Core.Infrastructure.Network.RpcSourceGenerators/ ([Rpc] codegen)
│   ├── Core.Infrastructure.Cryptography/            (RC4)
│   ├── Core.Entities/                               (account EF entities)
│   ├── Core.Accounts/                               (account service)
│   ├── Core.Spatial/                                (spatial utilities)
│   │
│   ├── Common/                                      (legacy — old server shared types)
│   ├── FrameWork/                                   (legacy)
│   ├── WorldServer/                                 (legacy)
│   └── ... (other legacy/supporting projects)
│
├── test/
│   ├── Core.GameWorld.Tests/
│   ├── WorldServerV2.Tests/
│   └── ... (existing test projects)
```

### Still Migrating to Core.GameWorld

These directories currently live under `WorldServerV2/World/` and will move into
`Core.GameWorld` as each system is refactored:

| Source (`WorldServerV2/World/`) | Target (`Core.GameWorld/`) | Status |
|---|---|---|
| `Stats/` | `Stats/` | Pending |
| `Combat/` | `Combat/` | Pending |
| `Items/` | `Items/` | Pending |
| `Abilities/` | `Abilities/` | Pending |
| `Spawning/` | `Spawning/` | Pending |

### What Stays in WorldServerV2

| Directory | Contents |
|---|---|
| `Network/` | GameSession, GameServerFramer, Serializer, Handlers/, Dtos/ |
| `Services/` | PlayerService, WorldService, CombatService, VisibilityService, PersistenceService, CharacterService, AbilityResolver, PlayerInitPipeline |
| `Data/` | GameDataStore, GameDataLoader, Providers/, DbContexts |
| `Config/` | `appsettings.json`, environment overrides |
| `Telemetry/` | Metrics, tracing |
| `Program.cs` | DI composition root |

---

## 3. Project Responsibilities

### Core.Domain (class library, net10.0)

**One sentence**: Leaf dependency — EF entities, database contexts, and value objects.

```
Core.Domain/
├── Entities/                  — EF entity POCOs (Character, CreatureProto, ItemInfo, etc.)
├── ValueObjects/              — Class, Faction, Gender, Race
├── CharacterDbContext.cs
└── WorldDbContext.cs
```

**Dependencies**: None. This is the innermost layer.

### Core.Session (class library, net10.0)

**One sentence**: Owns session lifecycle and provides the `GameSession` type used by all
layers to send packets to clients.

```
Core.Session/
├── GameSession.cs             — send-only session wrapper (thread-safe Send<T>)
├── SessionRegistry.cs         — ushort ID pool, create/remove sessions
├── SessionLifecycleService.cs — IHostedService for session cleanup
├── ISessionResolver.cs        — resolve GameSession from entity (generic)
└── ClientState.cs             — enum: Connecting, CharacterScreen, Playing, etc.
```

**Dependencies**: `Core.Domain`, `Core.Infrastructure.Network`

### Core.GameWorld (class library, net10.0)

**One sentence**: Owns the game domain, simulation logic, and all mutable entity state.

```
Core.GameWorld/
├── Actions/
│   ├── IRegionAction.cs               — generic action contract
│   ├── IRegionActionContext.cs         — context passed to actions during execution
│   └── (grouped by domain)/           — action implementations + public factories
├── Components/
│   ├── IComponent.cs / ITickable.cs   — optional behavior contracts
│   ├── HealthComponent.cs
│   ├── DestructibleComponent.cs
│   └── EquipmentComponent.cs          — pure data: ImmutableArray<CreatureItem> Items
├── Entities/
│   ├── WorldEntity.cs                 — abstract base: OID, name, position, component bag
│   ├── UnitEntity.cs                  — abstract: health, stats, buffs, level, realm
│   ├── PlayerEntity.cs                — sealed: Character record, inventory
│   ├── CreatureEntity.cs              — sealed: proto, spawn
│   ├── PetEntity.cs                   — sealed: proto, spawn, owner
│   ├── GameObjectEntity.cs            — sealed: entry, VfxState
│   └── WorldPosition.cs              — readonly record struct
├── Events/
│   ├── IRegionEventHandler.cs         — generic handler: IRegionEventHandler<TEvent>
│   ├── IRegionEventDispatcher.cs      — dispatch events to registered handlers
│   └── RegionEvents.cs               — readonly record struct event types
├── Spatial/
│   ├── Region.cs                      — tick loop, command channel, cell grid, visibility
│   ├── Cell.cs                        — per-cell entity lists
│   ├── VisibilitySet.cs               — per-entity range cache with snapshot support
│   ├── RegionManager.cs               — singleton region registry
│   ├── RegionCommand.cs               — spatial lifecycle + ExecuteAction
│   └── RegionConstants.cs             — cell size, visibility range, tick interval
├── Stats/                             — (migrating from WorldServerV2/World/)
├── Combat/                            — (migrating from WorldServerV2/World/)
├── Items/                             — (migrating from WorldServerV2/World/)
└── Spawning/                          — (migrating from WorldServerV2/World/)
```

**Access modifier strategy**:

| API | Modifier | Who can call |
|---|---|---|
| Entity property getters (`Health.Current`, `Stats.GetTotal()`, `Position`) | `public` | Anyone (handlers, services, tests) |
| Entity property setters (`Position`, `Health.TakeDamage()`, `Stats.AddBonus()`) | `internal` | Only Core.GameWorld (region thread, actions) |
| `IRegionAction` implementations | `internal` classes | Core.GameWorld only — created via public factory methods |
| Action factory methods (`CombatActions.BeginCast()`) | `public static` | Services in WorldServerV2 |
| `Region.EnqueueAction()` | `public` | Services in WorldServerV2 |

```csharp
[assembly: InternalsVisibleTo("Core.GameWorld.Tests")]
```

**Dependencies**: `Core.Domain`, `Core.Session`

### WorldServerV2 (executable, net10.0)

**One sentence**: Application host — wires DI, handles network I/O, orchestrates via
services, and implements all event handlers that bridge the domain to the wire protocol.

**Key rules**:
- Cannot mutate entity state (internal in different assembly).
- Handlers are thin — decode DTO, call one service method.
- Services orchestrate — read entity public API, create actions, enqueue on region.
- Services also implement `IRegionEventHandler<T>` to receive domain events and send
  packets (see [§10](#10-region-event-dispatch)).
- No direct `Region` interaction from handlers.

**Dependencies**:

| Dependency | Purpose |
|---|---|
| `Core.GameWorld` | Domain types, IRegionAction, events, RegionManager |
| `Core.Session` | GameSession, SessionRegistry, ISessionResolver |
| `Core.Domain` | EF entities, DbContexts |
| `Core.Infrastructure.Network` | NetworkManager, IConnectionContext, [Rpc] dispatch |
| `Core.Infrastructure.Cryptography` | MythicRc4 |
| `Core.Infrastructure.Network.RpcSourceGenerators` | Source generator for packet dispatch |
| Npgsql.EntityFrameworkCore.PostgreSQL | EF Core for character/world data |
| Grpc.Net.Client | AccountMgr gRPC client |
| OpenTelemetry | Metrics, tracing |

---

## 4. Dependency Graph

```
                     Core.Domain           (leaf — no project refs)
                      ↑      ↑
     Core.Infra.Network    Core.Session    → Core.Domain, Core.Infra.Network
                                ↑
                           Core.GameWorld   → Core.Domain, Core.Session
                                ↑
                           WorldServerV2    → Core.GameWorld, Core.Session, Core.Domain,
                            ↗    ↖            Core.Infra.Network, Core.Infra.Crypto
              Core.Infra.Crypto  (NuGet packages)
```

Arrows point from dependent → dependency. No circular references.

**Layering**:
- `Core.Domain` is the innermost layer (zero project references).
- `Core.Session` depends on `Core.Domain` + transport abstractions.
- `Core.GameWorld` depends on `Core.Domain` + `Core.Session` (for `GameSession`,
  `ISessionResolver`). It defines event and action contracts but has no knowledge of
  handlers, DTOs, or wire format.
- `WorldServerV2` is the outermost layer — it implements all event handlers, owns all
  DTOs, and composes the DI container.

**Rule**: dependencies point inward. Network → Services → Domain. Never the reverse.

---

## 5. Threading Model

| Thread | What it does | What it can access |
|---|---|---|
| **Network I/O** | TCP accept, read, write | Raw bytes only |
| **Handler pool** (thread pool) | Execute `[Rpc]` handler methods | READ entity public API; call services |
| **Region tick** (1 dedicated thread per region) | Drain commands, tick entities, broadcast, visibility | READ + WRITE all entity state (internal APIs) |
| **Background services** (thread pool / async) | Persistence, analytics, admin tools | Immutable snapshot DTOs only |

**Handler thread safety**: Handlers run concurrently on thread-pool threads. They must
not store mutable state. Injected services handle coordination (e.g., `PlayerService`
uses `Lock`-serialized writes, lock-free reads).

**Region thread isolation**: Each region's tick thread is the **sole writer** of entity
state within that region. No locks are needed for entity mutation — the single-threaded
guarantee eliminates write-write races by design.

---

## 6. Region Tick Pipeline

The region tick has a fixed, immutable ordering. Each tick executes these phases in
sequence:

```
Phase 1 — DRAIN RESPAWN SCHEDULER
  Re-enqueue creatures whose respawn timer has elapsed.

Phase 2 — DRAIN & EXECUTE COMMANDS
  Read all commands from Channel<RegionCommand> (FIFO).
  ├── Spatial commands: Add, Remove, Move, Transfer, Activate
  │   (handled directly by Region — this IS the region's responsibility)
  └── ExecuteAction: call IRegionAction.Execute(context)
      (region has zero domain knowledge — polymorphic dispatch)

Phase 3 — TICK ENTITIES
  For each active cell (3×3 neighborhood of cells containing players):
    For each entity in cell:
      entity.Update(tick)

Phase 4 — BROADCAST STATE
  For each dirty entity with visible players:
    Send F_OBJECT_STATE to each visible player

Phase 5 — UPDATE VISIBILITY
  For each entity that moved beyond the range-update threshold:
    Re-scan 3×3 cell neighborhood, update bidirectional VisibilitySet
```

### Entity Update Ordering (Phase 3)

The fixed ordering within `UnitEntity.Update()`:

```csharp
public override void Update(long tick)
{
    Buffs.Update(tick);   // 1st — buff effects apply/expire, modify stat layers
    Stats.Flush();        // 2nd — recompute derived stats from modified layers
    base.Update(tick);    // 3rd — tick ITickable components (abilities, AI, movement)
}
```

**Why this order?**: Buffs modify stat layers (strength bonus, armor debuff). Stats
recomputes totals from those layers (max health, effective armor). Components then read
fresh stats for their logic (ability timers check action points, AI reads health %).

**Adding new systems**: Implement `ITickable` on a component. It ticks in Phase 3
after buffs and stats. If a system must run before buffs (rare), add a direct call
in `UnitEntity.Update()` above `Buffs.Update()` — this requires explicit design review.

### Why Actions Don't Need Priority

Actions from handlers represent **player intent** (cast ability, move, interact). They
set up entity state that the tick then advances. A `BeginCastAction` sets a cast timer;
the timer counts down in Phase 3 when `AbilityComponent.Update()` ticks.

Actions don't execute game simulation — they initiate it. FIFO ordering in Phase 2 is
fair (it reflects network arrival order) and deterministic (same inputs → same order).

---

## 7. IRegionAction Extension Model

### Contracts (in Core.GameWorld)

```csharp
/// <summary>
/// A unit of game logic that executes on the region thread with full access to
/// entity internals. Created by services in WorldServerV2, executed by the region.
/// </summary>
public interface IRegionAction
{
    void Execute(IRegionActionContext context);
}

/// <summary>
/// Passed to actions during execution. Provides access to entities, sessions,
/// game data, and spatial queries — everything an action needs without importing
/// Region directly.
/// </summary>
public interface IRegionActionContext
{
    WorldEntity? GetEntity(ushort oid);
    GameSession? GetSession(PlayerEntity player);
    IGameDataStore GameData { get; }
    IRegionEventDispatcher Dispatcher { get; }
    void GetEntitiesInRange(WorldPosition center, int range, List<WorldEntity> results);
    void GetPlayersInRange(WorldPosition center, int range, List<PlayerEntity> results);
    IEnumerable<PlayerEntity> AllPlayers { get; }
}
```

### Implementation Pattern

Action implementations are `internal` to Core.GameWorld (they access `internal` entity
APIs). Services create them via `public static` factory methods.

```csharp
// Core.GameWorld/Actions/Combat/CombatActions.cs — public factory
public static class CombatActions
{
    public static IRegionAction BeginCast(ushort casterOid, ushort abilityId, ushort targetOid)
        => new BeginCastAction(casterOid, abilityId, targetOid);
}

// Core.GameWorld/Actions/Combat/BeginCastAction.cs — internal implementation
internal sealed class BeginCastAction(
    ushort casterOid, ushort abilityId, ushort targetOid) : IRegionAction
{
    public void Execute(IRegionActionContext ctx)
    {
        var caster = ctx.GetEntity(casterOid) as UnitEntity;
        if (caster is null) return;

        // Internal API — legal here, illegal from WorldServerV2
        var ability = ctx.GameData.Abilities.GetById(abilityId);
        // ... validate, set up cast timer on AbilityComponent ...
    }
}
```

### Region Integration

The region gains one case in `ProcessCommands()` and one public enqueue method.
Zero domain knowledge imported:

```csharp
// RegionCommand.cs — add one case
public sealed class ExecuteAction(IRegionAction action) : RegionCommand
{
    public IRegionAction Action { get; } = action;
}

// Region.cs — add one method + one case
public void EnqueueAction(IRegionAction action)
{
    _commands.Writer.TryWrite(new RegionCommand.ExecuteAction(action));
}

// In ProcessCommands():
case RegionCommand.ExecuteAction exec:
    exec.Action.Execute(_actionContext);
    break;
```

`_actionContext` is an `IRegionActionContext` implementation backed by the region's
internal state (entity dictionary, cell grid, session resolver, game data store).

---

## 8. Data Flow Patterns

### 8.1 Handler → Service → Region

```
Handler thread                Service layer                  Region thread
──────────────                ─────────────                  ─────────────
F_DO_ABILITY(req)     ──→     combatService.TryCast()  ──→  region.EnqueueAction(
  decode DTO                    validate (read snapshot)       CombatActions.BeginCast(...))
  call service                  create IRegionAction
                                enqueue on region        ──→  action.Execute(ctx)
                                send optimistic                re-validate authoritatively
                                feedback to client             mutate entity state
                                                               dispatch region events
```

**Handlers**: Thin. Decode DTOs, call one service method. No region interaction.
**Services**: Bidirectional. Inbound: read entity public API for fast-reject, create
`IRegionAction`, enqueue on region. Outbound: implement `IRegionEventHandler<T>`, construct DTOs, send packets.
**Actions**: Execute on region thread. Re-validate against authoritative state. Full
access to internal entity mutation. Fire events via `IRegionEventDispatcher`.

### 8.2 Read Patterns

| Need | Pattern | Thread safety |
|---|---|---|
| Single field validation | Direct public getter: `player.Health.Current` | Atomic for primitives (≤8 bytes on x64) |
| Multi-field validation | Read individually, accept possible inconsistency | Handler is optimistic; action re-validates |
| Iterate visible players | `entity.Visibility.SnapshotPlayers()` | Lock-guarded copy; work outside lock |
| Iterate inventory | `player.Inventory.Snapshot()` (to be implemented) | Same pattern as VisibilitySet |
| Full entity state | Enqueue action; action reads on region thread | Fully consistent (single-threaded) |

### 8.3 Write Patterns

| Need | Pattern | Example |
|---|---|---|
| Entity lifecycle | Spatial command (built into Region) | `region.EnqueueAdd()`, `.EnqueueRemove()`, `.EnqueueMove()` |
| Game logic mutation | `IRegionAction` via service | `region.EnqueueAction(CombatActions.BeginCast(...))` |
| Awaited result | `TaskCompletionSource` on action | `region.AddAsync()` (player login placement) |

---

## 9. Thread-Safety Guarantees

### 9.1 Per-Type Read Safety from Handler Threads

| Type | Size | Atomic? | Safe to read from handler? | Notes |
|---|---|---|---|---|
| `uint` (`Health.Current`) | 4 bytes | ✅ Yes | ✅ Possibly one tick stale | |
| `int` (`Stats.GetTotal()`) | 4 bytes | ✅ Yes | ✅ Possibly one tick stale | |
| `ushort`, `byte` | ≤2 bytes | ✅ Yes | ✅ | |
| `bool` (`IsActive`, `IsDead`) | 1 byte | ✅ Yes | ✅ | |
| `string` (reference) | 8 bytes (ref) | ✅ Yes | ✅ Immutable once set | |
| `WorldPosition` (record struct) | 18 bytes | ❌ No | ⚠️ Torn read possible | Handlers use client packet data for own player; actions re-validate for targets |
| `HashSet`, `List`, `Dictionary` | Collection | ❌ No | ❌ Use snapshot pattern | `VisibilitySet.SnapshotPlayers()`, future `Inventory.Snapshot()` |
| Multi-field compound read | N/A | ❌ No | ⚠️ Fields may be from different ticks | Acceptable: optimistic validation only |

### 9.2 The Optimistic Validation Guarantee

Handler reads are **fast-reject filters**, not authoritative decisions.

1. Handler reads public getter (possibly one tick / 50ms stale).
2. If stale data causes a false accept → action re-validates on region thread and
   cancels (client sees cast interrupt). Imperceptible at 50ms.
3. If stale data causes a false reject → client retries on next packet.
   Imperceptible at 50ms.
4. No data corruption is possible — writes are single-threaded on the region thread.

### 9.3 Collection Access Rule

Any entity state that is a collection (inventory, buff list, component bag, visibility
set) must provide a snapshot method following the `VisibilitySet.SnapshotPlayers()`
pattern:

1. Acquire a short lock.
2. Copy references to a pooled array (`ArrayPool<T>.Shared.Rent()`).
3. Release the lock.
4. Caller iterates the snapshot outside the lock.
5. Caller disposes the snapshot to return the array to the pool.

```csharp
using var snapshot = entity.Visibility.SnapshotPlayers();
foreach (var player in snapshot.Span)
{
    // Safe — iterating a copy, no lock held
    var session = sessionResolver.GetSession(player);
    session?.Send(...);
}
```

---

## 10. Region Event Dispatch

The region thread produces events as side-effects of simulation — entities becoming
visible, damage resolving, buffs expiring. These events must reach the application
layer (WorldServerV2) for packet construction and delivery, but the domain layer
(Core.GameWorld) must not know about wire format or DTO types.

### 10.1 Contracts

```csharp
// Core.GameWorld/Events/IRegionEventHandler.cs
public interface IRegionEventHandler<in TEvent>
{
    void Handle(TEvent evt);
}

// Core.GameWorld/Events/IRegionEventDispatcher.cs
public interface IRegionEventDispatcher
{
    void Dispatch<TEvent>(TEvent evt);
}
```

`IRegionEventHandler<T>` is the generic handler interface. Each implementation handles
one event type. A service can implement multiple `IRegionEventHandler<T>` interfaces
for different event types.

`IRegionEventDispatcher` is the dispatch surface — the region calls `Dispatch(evt)` and
the dispatcher routes to all registered `IRegionEventHandler<T>` implementations for
that event type.

### 10.2 Event Types

Events are `readonly record struct` types defined in Core.GameWorld. They carry all the
context a handler needs — the handler never reaches back into the domain to look up
additional state.

```csharp
// Core.GameWorld/Events/RegionEvents.cs

// Visibility lifecycle
public readonly record struct EntityBecameVisible(GameSession Observer, WorldEntity Entity);
public readonly record struct EntityLeftVisibility(GameSession Observer, WorldEntity Entity);
public readonly record struct EntityStateChanged(WorldEntity Entity);

// Combat
public readonly record struct DamageResolved(UnitEntity Source, UnitEntity Target, DamageContext Context);
public readonly record struct EntityDied(UnitEntity Entity, UnitEntity? Killer);

// Buffs
public readonly record struct BuffApplied(UnitEntity Target, Buff Buff);
public readonly record struct BuffExpired(UnitEntity Target, Buff Buff);

// Abilities
public readonly record struct AbilityCastStarted(UnitEntity Caster, ushort AbilityId, WorldEntity? Target);
public readonly record struct AbilityCastCompleted(UnitEntity Caster, ushort AbilityId);
```

Events are **fire-and-forget from the domain's perspective**. The region dispatches them
and moves on. It does not inspect results or wait for acknowledgment.

### 10.3 Dispatcher Implementation

`RegionEventDispatcher` lives in WorldServerV2. It resolves all `IRegionEventHandler<T>`
implementations from DI at startup and freezes them into a lookup structure for zero-
allocation dispatch at runtime.

```csharp
// WorldServerV2/Services/RegionEventDispatcher.cs
public sealed class RegionEventDispatcher : IRegionEventDispatcher
{
    // Built at startup from IServiceProvider
    // FrozenDictionary<Type, object[]> where object[] is IRegionEventHandler<T>[]

    public void Dispatch<TEvent>(TEvent evt)
    {
        foreach (var handler in GetHandlers<TEvent>())
            handler.Handle(evt);
    }
}
```

Registered in DI:

```csharp
// Program.cs
services.AddSingleton<IRegionEventDispatcher, RegionEventDispatcher>();
services.AddSingleton<IRegionEventHandler<EntityBecameVisible>, VisibilityService>();
services.AddSingleton<IRegionEventHandler<EntityLeftVisibility>, VisibilityService>();
services.AddSingleton<IRegionEventHandler<DamageResolved>, CombatService>();
// ... etc.
```

### 10.4 Region Integration

Region holds an `IRegionEventDispatcher` (injected via constructor). Notification
sites become single-line dispatch calls:

```csharp
// Before (type-switch + DTO construction in Region):
private void NotifyEntityVisible(PlayerEntity observer, WorldEntity entity)
{
    // 50+ lines switching on entity type, constructing DTOs, iterating components...
}

// After:
private void NotifyEntityVisible(PlayerEntity observer, WorldEntity entity)
{
    var session = _sessionResolver.GetSession(observer);
    if (session is null) return;
    _dispatcher.Dispatch(new EntityBecameVisible(session, entity));
}
```

The region no longer knows what packets exist, what DTOs look like, or which components
contribute visibility data. It fires one event and moves on.

### 10.5 Services as Bidirectional Intermediaries

Services in WorldServerV2 straddle both directions of data flow:

| Direction | Role | Example |
|---|---|---|
| **Inbound** (handler → region) | Receive requests from packet handlers, create `IRegionAction`, enqueue on region | `CombatService.TryCast(casterOid, abilityId, targetOid)` |
| **Outbound** (region → client) | Implement `IRegionEventHandler<T>`, construct DTOs, send packets | `CombatService : IRegionEventHandler<DamageResolved>` |

```csharp
// WorldServerV2/Services/CombatService.cs
public sealed class CombatService
    : IRegionEventHandler<DamageResolved>,
      IRegionEventHandler<EntityDied>
{
    // Inbound — called by CombatHandler
    public void TryCast(ushort casterOid, ushort abilityId, ushort targetOid)
    {
        var action = CombatActions.BeginCast(casterOid, abilityId, targetOid);
        _regionManager.GetRegion(casterOid)?.EnqueueAction(action);
    }

    // Outbound — called by dispatcher on region thread
    public void Handle(DamageResolved evt)
    {
        // Construct F_COMBAT_LOG / F_SET_PLAYER_HEALTH DTOs
        // Send to source, target, and visible players
    }

    public void Handle(EntityDied evt)
    {
        // Construct death notification packets
    }
}
```

This keeps domain code clean (fire-and-forget events) while concentrating all protocol
knowledge in WorldServerV2 services.

### 10.6 Threading Constraints

Event handlers execute **on the region thread** during the tick phase that fires them.
They must obey these rules:

| Rule | Reason |
|---|---|
| No blocking calls (async, locks, DB I/O) | Would stall the region's 50ms tick budget |
| No entity mutation | Handlers are in WorldServerV2, mutation APIs are internal to Core.GameWorld |
| `GameSession.Send<T>` is safe | Enqueues onto the connection's channel-based send queue (non-blocking from any thread) |
| No exceptions should escape | Dispatcher should catch and log; one bad handler must not kill the tick |

### 10.7 Retiring IVisibilityInitContributor

The current `IVisibilityInitContributor` interface on components is **retired**. Components
become pure data holders. The event handler in WorldServerV2 replaces this pattern entirely:

| Before | After |
|---|---|
| Region iterates `IVisibilityInitContributor` components on each entity | Region dispatches `EntityBecameVisible` event |
| `EquipmentComponent.SendVisibilityInit(session)` constructs and sends packets | `VisibilityService.Handle(EntityBecameVisible)` reads equipment from entity public API and sends packets |
| Component imports `GameSession` and knows about packet format | Component is pure data: `ImmutableArray<CreatureItem> Items` |
| Adding new visibility data = new contributor component | Adding new visibility data = extend `VisibilityService` (or register a new handler) |

This eliminates the coupling between Core.GameWorld components and the wire protocol.
Components hold state. Services decide what to do with it.

---

## 11. Persistence Model

### 11.1 Dirty Tracking

Mutation code on the region thread sets dirty flags. Zero-cost: bit-OR on a uint.

```csharp
[Flags]
public enum CharacterDirtyFlags : uint
{
    None       = 0,
    Position   = 1 << 0,
    Stats      = 1 << 1,
    Inventory  = 1 << 2,
    Quests     = 1 << 3,
    Skills     = 1 << 4,
    Buffs      = 1 << 5,
    // Extensible per system
}
```

`PlayerEntity` gains an `internal CharacterDirtyFlags DirtyFlags` field, settable only
from Core.GameWorld (region thread / actions).

### 11.2 Snapshot Collection via IRegionAction

The region never imports persistence types. A `CollectSnapshotsAction` is an
`IRegionAction` that runs on the region thread, reads dirty players, creates immutable
snapshot DTOs, clears dirty flags, and writes snapshots to a `Channel<T>`.

```csharp
// Core.GameWorld — action implementation
internal sealed class CollectSnapshotsAction(
    ChannelWriter<CharacterSnapshot> output) : IRegionAction
{
    public void Execute(IRegionActionContext ctx)
    {
        foreach (var player in ctx.AllPlayers)
        {
            if (player.DirtyFlags == CharacterDirtyFlags.None) continue;

            var snapshot = CharacterSnapshot.From(player);  // copies dirty fields
            player.DirtyFlags = CharacterDirtyFlags.None;
            output.TryWrite(snapshot);
        }
    }
}

// Public factory
public static class PersistenceActions
{
    public static IRegionAction CollectSnapshots(ChannelWriter<CharacterSnapshot> output)
        => new CollectSnapshotsAction(output);
}
```

### 11.3 Background Flush

`PersistenceService : BackgroundService` in WorldServerV2 periodically enqueues the
snapshot action on each region, then drains the output channel and writes to the
database. DB writes are fully asynchronous. No entity locks are ever held during writes.

```
PersistenceService (background, every 30s)
  │
  ├── For each region: region.EnqueueAction(PersistenceActions.CollectSnapshots(channel))
  │     → runs on region thread → copies dirty data → writes to channel
  │
  └── Drain channel → batch write to DB (async, no entity locks)
```

### 11.4 Edge Cases

| Scenario | Handling |
|---|---|
| **Logout / disconnect** | Immediate snapshot + priority write; await DB completion before ack |
| **Missed updates between cycles** | Caught in next cycle (max 30s latency); DirtyFlags accumulate |
| **Unclean shutdown** | Last cycle's unsaved changes lost (max 30s); standard for MMOs |
| **Critical transactions** | Trades, AH purchases get immediate-write path outside snapshot system |

---

## 12. Anti-Patterns

| ❌ Don't | ✅ Instead | Why |
|---|---|---|
| Mutate entity state from a handler or service | Enqueue an `IRegionAction` | Write safety: only region thread mutates |
| Read a collection without snapshot | Use `SnapshotPlayers()` / `Snapshot()` pattern | Collection mutation during iteration → crash |
| Add domain logic to `Region.ProcessCommands()` | Implement `IRegionAction` | Region stays lean; domain logic stays composable |
| Call `Region.*` from a handler | Call a service, which calls the region | Separation of concerns; handler stays thin |
| Hold entity locks during DB writes | Snapshot on region thread, write in background | Prevents tick budget overrun |
| Make Region aware of persistence | Enqueue `CollectSnapshotsAction` | Region has zero domain knowledge |
| Create new `RegionCommand` subclasses for game logic | Use `IRegionAction` | `RegionCommand` is for spatial lifecycle only (add/remove/move/transfer/activate) |
| Read `WorldPosition` from handler for authoritative logic | Use client packet data; re-validate in action | 18-byte struct read is not atomic |
| Add new tick phases to the region | Add `ITickable` components or direct calls in `UnitEntity.Update()` | Tick pipeline is fixed; extensibility is via components |
| Construct DTOs or send packets from Core.GameWorld | Dispatch a region event; let the handler in WorldServerV2 build the DTO | Domain must not know about wire format |
| Put packet/DTO knowledge in a component (`SendVisibilityInit`) | Make the component pure data; handle packets in `IRegionEventHandler<T>` | Components hold state, services handle protocol |
| Block or do async I/O in an `IRegionEventHandler<T>` | `GameSession.Send<T>` only (non-blocking channel enqueue) | Handlers run on the region thread — blocking stalls the tick |
| Let exceptions escape from `IRegionEventHandler<T>.Handle()` | Catch and log inside the dispatcher | One bad handler must not kill the region tick |

---

## Summary

```
┌────────────────────────────────────────────────────────────────────┐
│  WorldServerV2 (host)                                              │
│                                                                    │
│  Handlers ──→ Services ──→ create IRegionAction                    │
│  (thin DTOs)   (bidirectional)  + enqueue on Region                │
│                    ↑                                               │
│                    │ IRegionEventHandler<T>                         │
│                    │ (VisibilityService, CombatService, ...)        │
│                                                                    │
│  Can READ entity public API (Health.Current, Stats.GetTotal())     │
│  CANNOT WRITE (Health.TakeDamage, Position set — internal)         │
├──────────────────────────────────────────────────────┬─────────────┤
│  Core.GameWorld (domain + simulation)                │ Core.Session│
│                                                      │             │
│  Entities ←── internal mutation ──→ IRegionAction    │ GameSession │
│  Region drains commands:                             │ (Send<T>)   │
│    Spatial (Add/Remove/Move) — region handles        │             │
│    ExecuteAction(IRegionAction) — action handles     ├─────────────┤
│  Region dispatches events via IRegionEventDispatcher │ Core.Domain │
│                                                      │             │
│  Tick: respawn → commands → entities                 │ EF entities │
│        → broadcast → visibility                      │ DbContexts  │
└──────────────────────────────────────────────────────┴─────────────┘
```
