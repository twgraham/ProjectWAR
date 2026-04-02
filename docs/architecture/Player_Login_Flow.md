# Player Initialization & Login Flow

> Part of the [WorldServerV2 Architecture](./Overview.md) documentation suite.
> See also: [Glossary](./Glossary.md) · [System 2: Entity Model](./System_02_EntityModel.md) · [System 3: World Topology](./System_03_WorldTopology.md) · [System 4: Combat](./System_04_Combat.md)
>
> **Status**: Phase 3 minimum viable set implemented (17 of 41 init steps). See [§10.9](#109-complete-init-packet-reference) for the full packet reference.
>
> **Last updated**: April 2026

Player initialization is the cross-cutting workflow that bridges character persistence
(System 6), the entity model (System 2), and world topology (System 3). It's documented
here because it's a prerequisite for testing every gameplay system from Combat onward —
without a character loaded into a region, there's nothing to test.

---

## 10.1 Old Architecture

The old init is a three-method chain spread across a 6,837-line `Player.cs`:

| Method | Lines | Responsibility |
|--------|-------|----------------|
| `Player()` constructor | ~45 | Add 27 `BaseInterface` components, wire events |
| `Player.OnLoad()` | ~140 | Load DB data into interfaces (items, stats, quests, toks, mail, guild, abilities), set level/renown, rest XP |
| `Player.StartInit()` | ~120 | Send ~30 packets to client (stats, items, quests, health, position, abilities, channels, titles, mount) |

`OnLoad()` calls `base.OnLoad()` (Unit → Object), which calls `LoadInterfaces()` on
*all* registered interfaces — meaning many interfaces are loaded twice (once with DB
params, once without). The `_initialized` / `_initInProgress` guards ensure
`StartInit()` only runs on first load (not on zone changes).

---

## 10.2 Protocol Sequence

The complete client↔server handshake from character selection to in-game:

```mermaid
sequenceDiagram
    participant C as Client
    participant H as Packet Handlers (Handler Thread)
    participant R as Region Thread

    Note over C,H: Phase 1 — Character Selection
    C->>H: F_DUMP_ARENAS_LARGE (0x35) [character slot]
    H->>H: Load character from DB, create PlayerEntity, bind to PlayerService
    H->>C: F_WORLD_ENTER (0x19) [zone server info]

    Note over C,H: Phase 2 — Game Session
    C->>H: F_OPEN_GAME (0x17)
    H->>C: S_GAME_OPENED (0x85)

    Note over C,H: Phase 3 — Player Init (handler thread; client on loading screen)
    C->>H: F_INIT_PLAYER (0x7C)
    H->>R: region.ReserveOid() [thread-safe]
    H->>H: Phase B — compute state (level, stats, health)
    H->>H: Phase C — build & send ~40 packets via session.Send()
    H->>C: S_PLAYER_INITTED (0x88) [position, zone, career, realm]
    H->>C: F_MAX_VELOCITY (0x1E) .. F_PLAYER_INIT_COMPLETE (0xEF)
    H->>R: region.EnqueueAdd(player, position) [fully initialized]
    Note over R: Next tick: place in cell, run visibility

    Note over C,H: Phase 4 — World Load
    C->>H: F_REQUEST_WORLD_LARGE (0x40)
    H->>C: F_SET_TIME (0xD6)
    H->>C: S_WORLD_SENT (0x83)
    Note over C: Client renders world terrain

    Note over C,H: Phase 5 — Activation
    C->>H: F_DUMP_STATICS (0x0D) [client finished loading]
    H->>H: session.State = Playing
    H->>R: region.EnqueueActivate(player)
    Note over R: Next tick: set IsActive = true, send create-packets for visible entities
    R->>C: F_CREATE_MONSTER / F_CREATE_STATIC (per visible entity)
    R->>C: F_PLAYER_INVENTORY (if entity has EquipmentComponent)
    R->>C: F_OBJECT_STATE (stationary state: position, health, heading)
    Note over C,R: Player now receives all visibility notifications and state broadcasts
```

---

## 10.3 Minimum Viable Packet Set

| Packet | Opcode | Gate | Existing in V2? |
|--------|--------|------|------------------|
| `S_PID_ASSIGN` | 0x80 | Hard — client needs session identity | ✅ `AuthenticationHandler` |
| `F_WORLD_ENTER` | 0x19 | Hard — triggers zone load screen | ✅ `CharacterScreenHandler` |
| `S_GAME_OPENED` | 0x85 | Hard — game session ack | ✅ `CharacterScreenHandler` |
| `S_PLAYER_INITTED` | 0x88 | Hard — position/zone/region/career | ✅ `PlayerInitPipeline` |
| `F_MAX_VELOCITY` | 0x1E | Soft — prevents frozen movement | ✅ `PlayerInitPipeline` |
| `F_PLAYER_STATS` | 0x46 | Soft — empty stats pane otherwise | ✅ `PlayerInitPipeline` |
| `F_PLAYER_HEALTH` | 0x05 | Soft — no HP bar otherwise | ✅ `PlayerInitPipeline` |
| `S_PLAYER_LOADED` | 0x89 | Hard — client-side init gate | ✅ `PlayerInitPipeline` |
| `F_PLAYER_INIT_COMPLETE` | 0xEF | Hard — client unblocks | ✅ `PlayerInitPipeline` |
| `F_SET_TIME` | 0xD6 | Hard — part of world load response | ✅ `CharacterHandler` |
| `S_WORLD_SENT` | 0x83 | Hard — final gate, world renders | ✅ `CharacterHandler` |
| `F_DUMP_STATICS` | 0x0D | Hard — activates player for visibility/broadcasts | ✅ `CharacterHandler` |
| `F_CREATE_MONSTER` | 0x72 | Visibility — NPC/creature create packet | ✅ `Region.NotifyEntityVisible` |
| `F_CREATE_STATIC` | 0x71 | Visibility — game object create packet | ✅ `Region.NotifyEntityVisible` |
| `F_PLAYER_INVENTORY` | 0xBD | Visibility follow-up — NPC equipped items (via `IVisibilityInitContributor`) | ✅ `EquipmentComponent` |
| `F_OBJECT_STATE` | 0x09 | Visibility follow-up — stationary state (position, health, heading) | ✅ `Region.NotifyEntityVisible` |

All other init packets (money, quests, inventory, ToK, social, guild, abilities,
tactics, channels, bestiary, morale, live events, RvR status, appearance) are
**additive** — the client shows empty UI panes but doesn't stall without them.
These are added incrementally as each game system is built.

---

## 10.4 Three-Phase Init Model

The old code interleaves DB loading, state computation, and packet serialization
in a single call chain. V2 separates these into distinct phases with clear ownership:

| Phase | When | Where | Responsibility |
|-------|------|-------|----------------|
| **A — Load** | Character selection (`F_DUMP_ARENAS_LARGE`) | Handler thread | Eager-load full character graph from DB via `ICharacterService` |
| **B — Compute** | `F_INIT_PLAYER` handler, after OID reservation | Handler thread | Derive runtime state from loaded data via game services |
| **C — Serialize** | Immediately after Phase B | Handler thread | Send protocol packets to client via `PlayerInitPipeline` |

All three phases execute on the handler thread. The region thread is only involved
for the OID reservation (`Region.ReserveOid()`, thread-safe) and the final
`EnqueueAdd()` that places the fully-initialized entity into the spatial grid.

**Phase A — Load**: `ICharacterService.LoadFullCharacterAsync()` fetches the
complete character graph (Character + CharacterValue + Items + Quests + Toks +
Abilities + Social + Mail) during character selection. All DB I/O happens here,
on the handler thread, with async/await. By the time `F_INIT_PLAYER` arrives,
all data is in memory.

**Phase B — Compute**: Game services derive runtime state from the loaded data.
These are the *same services* used during gameplay (level-up stat recomputation,
buff recalculation, etc.) — init simply calls them for the first time:

```csharp
// Minimal viable Phase B (grows as systems are built)
_progression.ApplyLevel(player, player.Character.Value.Level);
_progression.ApplyRenownRank(player, player.Character.Value.RenownRank);
_stats.Recompute(player);
player.Health.SetToMax();
```

**Phase C — Serialize**: An explicit, sequential pipeline sends the protocol
packets. Every step is visible in one method. `GameSession.Send<T>()` is
thread-safe (backed by a channel-based send queue), so packets can be enqueued
from the handler thread without blocking:

```csharp
public void Initialize(PlayerEntity player, GameSession session)
{
    // Phase B — Compute
    ComputeInitialState(player);

    // Phase C — Serialize (protocol order)
    SendSpeed(player, session);
    SendPlayerInitted(player, session);
    SendStats(player, session);
    SendHealth(player, session);
    SendPlayerLoaded(player, session);
    SendInitComplete(player, session);
}
```

---

## 10.5 Threading Model

Init runs entirely on the handler thread. The region thread's only involvement is
providing a thread-safe OID and later receiving the fully-initialized entity:

```mermaid
sequenceDiagram
    participant HT as Handler Thread
    participant OP as Region OID Pool (ConcurrentBag)
    participant CH as Region Command Channel
    participant RT as Region Tick Thread

    HT->>HT: Validate request, resolve region/zone
    HT->>OP: using var reservation = region.ReserveOid()
    OP-->>HT: OidReservation { Oid, Owner }
    HT->>HT: player.AssignOid(reservation.Oid)
    HT->>HT: Phase B — compute state (level, stats, health)
    HT->>HT: Phase C — send ~40 packets via session.Send() [channel enqueue]
    Note over HT: Player initialized but not yet placed; client on loading screen
    HT->>CH: await region.AddAsync(player, position, reservation)
    Note over HT: Reservation consumed — Dispose() is a no-op
    Note over HT: If init threw, Dispose() returns OID to pool

    RT->>CH: Drain commands (tick start)
    RT->>RT: Place entity in cell (OID already assigned)
    RT->>RT: TCS.SetResult(true) — signal placement complete
    RT->>RT: UpdateVisibility() — other players discover new player

    HT->>HT: await returns — entity is placed
    HT->>HT: Send F_PLAYER_INIT_COMPLETE
    HT->>HT: session.State = WorldEnter
    Note over HT: Client receives INIT_COMPLETE, sends F_REQUEST_WORLD_LARGE
    Note over HT: Client renders terrain, then sends F_DUMP_STATICS

    HT->>CH: region.EnqueueActivate(player)
    HT->>HT: session.State = Playing
    RT->>RT: ExecuteActivate — set IsActive, send create-packets for visible entities
```

**Why handler thread, not region thread?**

The full init audit (§10.9) shows ~40 steps producing 60–100+ packets. Most are
fast struct fills, but some iterate large collections (all inventory slots, 1500-byte
ToK bitmask, 25+ career packages, 12+ guild packets). Individually ~0.5–2ms per
player. But 10 simultaneous zone-ins (server restart, scenario pop, keep siege)
would consume 5–20ms of a 100ms tick — meaningful for a loop that must also tick
entities and update visibility.

During init the client is gated behind `F_PLAYER_INIT_COMPLETE` →
`F_REQUEST_WORLD_LARGE` → `S_WORLD_SENT`. No movement, damage, or social
interaction can occur. The entity doesn't need to be in a cell for packet
serialization — every init packet derives from the DB-loaded character graph +
computed state. The only thing needed from the region is an **OID**.

| Concern | Decision | Rationale |
|---------|----------|-----------|
| OID availability | `Region.ReserveOid()` returns `OidReservation` ticket | Pre-allocates OID before init; `ConcurrentBag` pool is thread-safe; region thread never blocks |
| OID safety net | `OidReservation : IDisposable` + `using` in handler | Dispose returns OID to pool if not consumed; consumed by `AddAsync` on success. No raw release API. |
| No DB I/O on region thread | All data loaded in Phase A on handler thread | Region thread does only spatial placement — microseconds of work |
| Entity visibility | Entity added to region *after* init completes | Other players can't see a half-initialized entity — visibility runs after command processing |
| Packet sending | `GameSession.Send<T>()` is thread-safe (channel-based) | Packets enqueued from any thread; flushed on network I/O thread |
| No dormant-entity state | Pre-allocated OID avoids "dormant" entity concept | Region subsystems (ticking, visibility, collision) don't need a new state to filter on |
| State gating | `session.State` set to `Playing` only after init completes | Prevents handler threads from processing gameplay packets for a not-yet-initialized player |
| Post-init concurrency | Handler threads enqueue commands; never mutate entity state directly | Existing region threading discipline applies unchanged |

---

## 10.6 Handler Responsibilities

| Handler | Trigger | Actions |
|---------|---------|--------|
| `CharacterSelectionHandler` | `F_DUMP_ARENAS_LARGE` | Load character from DB, create `PlayerEntity`, `PlayerService.Bind()`, send `F_WORLD_ENTER` |
| `GameSessionHandler` | `F_OPEN_GAME` | Send `S_GAME_OPENED`, set state → `GameOpened` |
| `CharacterScreenHandler` | `F_INIT_PLAYER` | Resolve region, `using var reservation = region.ReserveOid()`, run `PlayerInitPipeline.Initialize()`, `await region.AddAsync(player, position, reservation)`, send `F_PLAYER_INIT_COMPLETE`, set state → `Playing` |
| `CharacterHandler` | `F_REQUEST_WORLD_LARGE` | Send `F_SET_TIME` + `S_WORLD_SENT` |

---

## 10.7 Key Design Decisions

| Decision | Rationale |
|----------|-----------|
| Init on handler thread, not region thread | The full init audit (§10.9) shows 60–100+ packets — too much work for a deterministic tick loop. The client is on a loading screen; nothing requires spatial placement until init is done. |
| `OidReservation` ticket pattern | `ReserveOid()` returns a disposable ticket — not a raw `ushort`. Ticket is consumed by `AddAsync`, validated against the owning region, and state-tracked via `Interlocked` (Active → Consumed or Released). No public `ReleaseOid(ushort)` exists — prevents accidental release of in-use OIDs. |
| No dormant-entity concept | Pre-allocating the OID avoids adding a "dormant" state that every region subsystem (ticking, visibility, collision) would need to understand and filter. The entity simply doesn't exist in the region until it's ready. |
| Explicit `PlayerInitPipeline` (not auto-discovered contributors) | Init is protocol-critical — sequence must be visible in one place, debuggable without searching multiple files. YAGNI: 0 systems exist to contribute today. |
| Pipeline is a plain class with direct methods, not an interface | No polymorphism needed. When multiple serialization steps exist, they're explicit calls in the method body, not registered plugins. |
| Game services for Phase B computation, not methods on `PlayerEntity` | Same stat/progression services used during gameplay — avoids duplicating formulas on the entity and keeps `PlayerEntity` as a data holder. |
| `PlayerEntity` is the data nexus, services act on it | Consistent with entity-component pattern. Entity owns state, services own logic. |
| Full character graph loaded during selection, not init | DB I/O on handler thread (async-friendly). Region thread never blocks on I/O. Clean separation of load vs. compute. |
| State gating via `session.State` enum | Same pattern already used for char screen → world transition. Extended to cover init → playing. State set to `Playing` only after `AddAsync` completes and `F_PLAYER_INIT_COMPLETE` is sent — not inside the pipeline. |
| OID release on failure via `using` + `IDisposable` | `using var reservation` guarantees the OID is returned to the pool if `EnqueueAdd` is never reached — no explicit `try/catch` release needed. |

---

## 10.8 Future Direction & Tech Debt

### Domain-Services Evolution

The current `PlayerInitPipeline` manually constructs packet DTOs (e.g. `SpeedResponse`,
`PlayerStatsResponse`, `PlayerHealthResponse`) inline. This creates a **duplication trap**:
when gameplay systems are built, those same services will need to produce the same packets
(e.g. a stats service recomputing and sending `F_PLAYER_STATS` after a buff changes).

The intended evolution path:

1. **Now** — Pipeline builds packets inline. Acceptable because no gameplay services exist yet.
2. **When System 4 (Stats/Combat) is built** — Extract domain services (`StatsService`,
   `HealthService`, `SpeedService`, etc.) that own the "compute state → build packet → send"
   logic. The pipeline's Phase C calls become thin delegates into those services.
3. **Eventually** — The pipeline class is inlined into the handler. The handler orchestrates
   login as a series of service calls in protocol order. Phase B becomes
   `player.LoadFrom(charValue)` or a factory method.

The key principle: **domain services own packet building, not the pipeline and not
components**. Both the login sequence and gameplay ticks call the same services:

```
Login (handler thread):              Gameplay (region tick):
  statsService.ComputeAndSend(p)       StatsComponent.Update(tick):
  healthService.SendCurrent(p)           if (dirty) statsService.ComputeAndSend(p)
  speedService.SendCurrent(p)          HealthComponent regen:
                                         if (changed) healthService.SendCurrent(p)
```

This avoids:
- Components sending packets directly (coupling to network layer, untestable)
- Entity lifecycle hooks like `OnLoad()` / `OnInitialize()` (recreates V1's god class)
- The entity needing to orchestrate its own components (breaks composition pattern)

Entities remain state containers. Services own logic and network dispatch. Components
hold state and mark themselves dirty; on tick they call services if needed.

### Tech Debt & Gaps

| Item | Category | Detail |
|------|----------|--------|
| Domain service extraction | Evolution | Extract `StatsService.ComputeAndSend()`, `HealthService.SendCurrent()`, etc. when System 4 is built. Pipeline Phase C calls become one-liners into these services. |
| Pipeline → handler inlining | Evolution | Once domain services exist, the pipeline class adds no value — inline the service calls into `CharacterScreenHandler.F_INIT_PLAYER()`. Init-only packets (`S_PLAYER_INITTED`, `S_PLAYER_LOADED`) remain in the handler since no gameplay system sends them. |
| Entity hydration factory | Evolution | Replace direct field-setting in Phase B with `PlayerEntity.LoadFrom(charValue)` or a `PlayerFactory`. Pure domain logic, no network dependency. |
| Zone change re-init | Gap | Currently only covers first login. Zone transfers need a lighter re-init path (skip Phase A, re-run Phase B for zone-specific state, re-run Phase C). |
| Reconnect / session resume | Gap | If a player disconnects and reconnects before timeout, the entity may still be in the region. Need a resume path that skips Phase A + add, runs partial Phase B, full Phase C. |
| `ICharacterService.LoadFullCharacterAsync` | Tech debt | Currently loads Character + Value + Items. Needs extension to load quests, toks, abilities, social, mail as those systems are built. Each addition is a new EF Core `Include()` or separate query. |
| Phase B service stubs | Tech debt | `IStatService`, `IProgressionService` don't exist yet. Minimal viable init sets fields directly. These services emerge with System 4 (Combat). |
| OID reservation timeout | Defensive | `OidReservation.Dispose()` handles the normal failure path, but if a handler thread is abandoned without disposal (e.g. unhandled `ThreadAbortException` or process crash), the OID is permanently leaked. A periodic sweep on the region thread could track outstanding reservations by timestamp and reclaim those beyond a threshold (e.g. 30s). Low priority — the pool is 65,534 entries and the `using` pattern covers all expected failure modes. |
| Placement gate via `TaskCompletionSource` | Design | `Region.AddAsync()` creates a `TaskCompletionSource<bool>` embedded in the `AddEntity` command. The region thread calls `TCS.SetResult(true)` after placing the entity in its cell. The handler `await`s this task before sending `F_PLAYER_INIT_COMPLETE`, guaranteeing the entity is placed before the client can interact. Uses `RunContinuationsAsynchronously` to prevent continuations from running on the region thread. |
| Phase B concurrency | Future | When Phase B computation becomes expensive (e.g. stats + career mastery + buff recomputation), individual steps could be parallelized with `Task.WhenAll` since they act on disjoint state. Only worthwhile after measurement shows init latency matters. |
| Integration test: full login flow | Testing | Test harness sends the full packet sequence (`F_DUMP_ARENAS_LARGE` → `S_WORLD_SENT`) and verifies every response opcode and critical field. Requires a test DB or in-memory character service. |

---

## 10.9 Complete Init Packet Reference

Comprehensive ordered list of every packet sent during player login, traced from
the old `Player.OnLoad()` and `Player.StartInit()` methods. This serves as the
canonical roadmap for incrementally adding packets as each game system is built.

> **Conventions**: "Count" is packets per invocation. "Conditional" means the
> packet is only sent when a runtime condition is true (e.g. player has a guild).
> "V2 Status" tracks implementation in `PlayerInitPipeline` / handlers.

### Phase 0 — Pre-Init (`OnLoad()`, before `StartInit()`)

These packets are sent while loading interface data and applying initial state.
In V2, the equivalent work happens during Phase A (character selection) or at the
start of Phase B, and the corresponding packets are sent during Phase C.

| # | Opcode | Hex | Source Method | Count | Cond? | Description |
|---|--------|-----|---------------|-------|-------|-------------|
| 0a | `F_TRADE_SKILL_UPDATE` | 0x15 | `GatherInterface.Load()` / `CraftXxxInterface.Load()` | 2–4 | Yes | Tradeskill levels (gathering + crafting) |
| 0b | `F_GET_CULTIVATION_INFO` | 0x72 | `CultivInterface.Load()` | 0–4 | Yes | Cultivation plots (gathering skill == 3) |
| 0c | `F_INTERACT_RESPONSE` | 0x50 | `ScenarioMgr.SendScenarioStatus()` | 1 | No | Scenario queue status |
| 0d | `F_LOCALIZED_STRING` | 0x3E | `SendLocalizeString()` | 1 | No | Message of the Day |
| 0e | `F_MAIL` | 0x00 | `MlInterface.SendMailCount()` | 1–2 | No | Unread mail + auction counts |
| 0f | `F_INFLUENCE_INFO` | 0x59 | `SendInfluenceInfo()` | 1 | No | Chapter influence progress |
| 0g | `F_RVR_STATS` | 0x8F | `SendRVRStats()` | 1 | No | RvR statistics |
| 0h | `F_ACTION_COUNTER_INFO` | 0x7A | `SendLockouts()` | 0–1 | Yes | Instance lockouts |
| 0i | `F_KEEP_STATUS` | 0xCC | `WorldMgr.SendKeepStatus()` | N | No | Status of every keep in the game |

### Phase 3 — `StartInit()` Block 1

| # | Opcode | Hex | Source Method | Count | Cond? | V2? | Description |
|---|--------|-----|---------------|-------|-------|-----|-------------|
| 1 | `F_PLAYER_WEALTH` | 0x1C | `SendMoney()` | 1 | No | — | Gold/silver/brass |
| 2 | `F_SOCIAL_NETWORK` | 0x49 | `SocInterface.SendSocialLists()` | 2 | No | — | Friends list + ignore list |
| 3 | `F_MAX_VELOCITY` | 0x1E | `SendSpeed(Speed)` | 1 | No | ✅ | Movement speed |
| 4 | `F_BAG_INFO` (sub 0x19) | 0x0C | `StsInterface.SendRenownStats()` | 1 | No | — | Renown stat bonuses |
| 5 | `F_REALM_BONUS` | 0xD8 | `SendRealmBonus()` | 1 | No | — | Active realm bonuses |
| 6 | `S_PLAYER_INITTED` | 0x88 | `SendInited()` | 1 | No | ✅ | Identity, position, realm, career |
| 7 | `F_TACTICS` | 0xF7 | `TacInterface.HandleTactics()` + `SendTactics()` | 1–2 | Partial | ✅ | Equipped + available tactics |

### Phase 3 — `StartInit()` Block 2

| # | Opcode | Hex | Source Method | Count | Cond? | V2? | Description |
|---|--------|-----|---------------|-------|-------|-----|-------------|
| 8 | `F_QUEST_LIST` | 0x3B | `QtsInterface.SendQuests()` | 1 | No | — | Active quest journal |
| 9 | `F_BAG_INFO` (live events) | 0x0C | `LiveEventInterface.SendLiveEvents()` | 0–1 | Yes | — | Active live event data |
| 10 | `F_EXPERIENCE_TABLE` | 0x20 | `SendXpTable()` | 1 | No | — | XP-per-level table (~2 KB) |
| 11 | `F_GUILD_DATA` | 0x4E | `GldInterface.Guild.SendGuildInfo()` | 12+ | Yes | — | Guild roster, ranks, heraldry, tax, alliance |
| 12 | `F_CAREER_PACKAGE_INFO` + `F_CAREER_CATEGORY` | 0xF3 + 0xEE | `PlayerInitPipeline.SendCareerPackages()` | N | No | ✅ | Career ability packages (treeId 0 abilities + treeId 1 tactics) |
| 13 | `F_INTRO_CINEMA` | 0x3A | (inline) | 0–1 | Yes | — | Intro cinematic (first connect only) |
| 14 | `F_PLAYER_EXPERIENCE` | 0x4F | `SendXp()` | 1 | No | — | Current XP / rest XP |
| 15 | `F_PLAYER_RENOWN` | 0x4C | `SendRenown()` | 1 | No | — | Current renown points |
| 16 | `F_PLAYER_STATS` | 0x46 | `SendStats()` (1st) | 1 | No | ✅ | Full stat block |
| 17 | `F_TOK_ENTRY_UPDATE` | 0x67 | `TokInterface.SendAllToks()` | 1 | No | — | Tome of Knowledge (1500-byte bitmask) |
| 18 | `F_PLAYER_RANK_UPDATE` | 0x36 | `SendRankUpdate()` | 1 | No | — | Level/renown rank |
| 19 | `F_CHARACTER_INFO` (sub 3) + `F_WAR_REPORT` | 0xBE + 0x52 | `SendSkills()` | 2 | No | ✅¹ | Skills list + war report |
| 20 | `F_ACTION_COUNTER_INFO` | 0x7A | `SendBestiary()` | 1 | No | — | Kill counts (bestiary) |
| 21 | `F_PLAY_TIME_STATS` | 0x2B | `SendPlayedTime()` | 1 | No | — | /played time |
| 22 | `F_BAG_INFO` + N×`F_GET_ITEM` + `F_ITEM_SET_DATA` | 0x0C + 0x0E + 0x55 | `ItmInterface.SendAllItems()` | 1+N+cond | No | — | Bag layout + every item + set bonuses |
| 23 | `F_CHARACTER_INFO` (sub 6) | 0x07 | `Group.SendNullGroup()` | 0–1 | Yes | — | No-group indicator (when solo) |
| 24 | `F_PLAYER_HEALTH` | 0x05 | `SendHealth()` | 1 | No | ✅ | HP, AP, morale |
| 25 | `S_PLAYER_LOADED` | 0x89 | (inline) | 1 | No | ✅ | Data-complete marker |
| 26 | `F_MAX_VELOCITY` | 0x1E | `SendSpeed(Speed)` (2nd) | 1 | No | ✅ | Speed (re-sent, matches old server) |
| 27 | `F_MORALE_LIST` | 0x8C | `SendMoraleAbilities()` | 1 | No | ✅ | Equipped morale abilities |
| 28 | `F_PLAYER_STATS` | 0x46 | `SendStats()` (2nd) | 1 | No | ✅ | Stats (re-sent, matches old server) |
| 29 | `F_CHARACTER_INFO` (sub 1) | 0x07 | `AbtInterface.SendAbilityLevels()` | 1 | No | ✅ | Ability levels |
| 30 | `F_CAREER_CATEGORY` + 24×`F_CAREER_PACKAGE_INFO` | 0xEE + 0xF3 | `AbtInterface.ReloadMastery()` | 25 | No | ✅ | Mastery tree + packages |
| 31 | 3×`F_CAREER_PACKAGE_UPDATE` + `F_CHARACTER_INFO` (sub 1) | 0x69 + 0x07 | `AbtInterface.SendMasteryPointsUpdate()` | 4 | No | ✅ | Mastery point allocation |
| 32 | `F_CLIENT_DATA` | 0x23 | `SendClientData()` | 1 | No | — | Client settings blob (1024 bytes) |
| 33 | N×`F_OBJECT_EFFECT_STATE` | 0x8A | `OSInterface.SendObjectStates()` | 0–N | Yes | — | Active visual effects |
| 34 | 3×`F_UPDATE_STATE` | 0xC5 | `DispatchUpdateState()` × 2 + `SendHelmCloakShowing()` | 3 | No | — | Renown title, ToK title, helm/cloak visibility |
| 35 | `F_MOUNT_UPDATE` | 0x71 | `SendMount()` | 0–1 | Yes | — | Mount model (if mounted) |
| 36 | `F_MAGUS_DISC_UPDATE` | 0x4D | `SendDisc()` | 0–1 | Yes | — | Magus disc model (Magus career only) |
| 37 | `F_CHANNEL_LIST` | 0x16 | `LoadChannels()` | 1 | No | — | Chat channel subscriptions |
| 38 | `F_PLAYER_INIT_COMPLETE` | 0xEF | `SendInitComplete()` | 1 | No | ✅ | Init-done signal → client unblocks |
| 39 | `F_UPDATE_HOT_SPOT` | 0x4B | `WorldMgr.SendZoneFightLevel()` | 1+N | No | — | Zone fight levels (RvR heatmap) |

### Phase 4 — Post-Init (client sends `F_REQUEST_WORLD_LARGE`)

| # | Opcode | Hex | Source Method | Count | V2? | Description |
|---|--------|-----|---------------|-------|-----|-------------|
| 40 | `F_SET_TIME` | 0xD6 | `CharacterHandler` | 1 | ✅ | In-game clock |
| 41 | `S_WORLD_SENT` | 0x83 | `CharacterHandler` | 1 | ✅ | Final render signal → world appears |

### Summary

- **41 ordered steps**, producing **60–100+ individual packets** per player login
  (varies with inventory size, guild membership, career packages)
- **V2 currently implements 17 of 41 steps** — the hard client gates plus
  health/stats/speed, skills, abilities, morales, tactics, and mastery trees
- ¹ `F_CHARACTER_INFO` sub 3 (skills) is implemented; `F_WAR_REPORT` is deferred
- All remaining steps are **additive** — the client functions but shows empty UI
  panes for unimplemented systems
- Each step is added to `PlayerInitPipeline.Initialize()` as the corresponding
  game system is built
