# WorldServerV2 — Glossary of Game Concepts

> Part of the [WorldServerV2 Architecture](./Overview.md) documentation suite.
>
> **Last updated**: April 2026

This glossary defines WAR-specific and server-internal terms used consistently across all architecture and design documents. Terms are linked from individual system documents where first introduced.

---

| Term | Definition |
|------|------------|
| **Region** | The top-level spatial container. Each region runs on its own dedicated thread with a 20Hz tick loop. A region owns a sparse cell grid, an OID pool, and processes all entity mutations via a command channel. In WAR, each major geographical area (e.g. "Tier 1 shared starting area") is a region. The `characterinfo.RegionId` column determines which region manages a character. |
| **Zone** | A named sub-area within a region (e.g. "Ekrund", "Mt. Bloodhorn", "Nordland"). Zones define their own coordinate offset (`OffX`, `OffY`) within the region and a **client-facing Region ID** (`zone_infos.Region`) that tells the client which terrain/map to load. Multiple zones can share one server-side region. |
| **Cell** | A fixed-size spatial bucket within a region's grid. Each cell is 4096×4096 game units (~341×341 feet). The grid is 800×800 but sparse — cells are only allocated when an entity enters. Cells track entity lists and whether they are active (contain players) or loaded (spawn data materialized). |
| **OID (Object ID)** | A `ushort` (1–65535) that uniquely identifies an entity within a region. Assigned from a `ConcurrentBag<ushort>` pool via `Region.ReserveOid()`. Returned to the pool when the entity is removed. OIDs are region-scoped — two entities in different regions can share the same OID. |
| **World-Absolute Coordinates** | The region-wide X/Y position of an entity, stored as `int`. This is the primary coordinate system used internally. All distance checks, cell lookups, and spatial queries use world-absolute coordinates. Stored in the DB as `characterinfo.WorldX` / `WorldY`. |
| **Zone-Local Coordinates** | X/Y coordinates relative to a zone's origin. Computed as `WorldX - (OffX × 4096)`. Used at the network boundary when the client needs zone-relative positions (e.g. for terrain alignment). Derived on demand via `WorldPosition.ToZoneLocal()` — never stored internally. |
| **Zone Offset (OffX / OffY)** | Integer multipliers from `zone_infos` that define a zone's origin in world-absolute space. The zone origin is `(OffX × 4096, OffY × 4096)`. Example: Zone "Nordland" has `OffX=200`, so its origin X is 819,200. |
| **Client Region ID** | The `zone_infos.Region` value sent to the client in `S_PLAYER_INITTED`. Tells the client which terrain map to load. This may differ from the server-side region that manages the entity. Example: Greenskin starting zones have client Region ID = 1 but are managed by server Region 8. |
| **Server Region ID** | The `characterinfo.RegionId` value used server-side to determine which `Region` instance manages the entity. All starting characters currently use Region 8. |
| **Entity** | Any object in the game world with an OID and a position: players, creatures, pets, game objects, siege weapons, public quests, keeps. Represented by the `WorldEntity` hierarchy. |
| **IsActive** | A per-entity flag (`WorldEntity.IsActive`) that gates whether a player receives visibility notifications and state broadcasts. NPCs/creatures/game objects default to `true`; players default to `false` and are activated when the client sends `F_DUMP_STATICS` (0x0D) after finishing the world load. This prevents the client from receiving entity-create packets before the loading screen has finished. The region thread flips this flag via the `ActivateEntity` command, then re-sends create packets for all entities already in the player's visibility set. |
| **Component** | An optional behavior or state container that can be attached to an entity at runtime. Implements `IComponent`. Components that need per-tick updates also implement `ITickable`. Components that need to send follow-up packets when an entity becomes visible implement `IVisibilityInitContributor`. Required state (like health) is a direct field, not a component. |
| **IVisibilityInitContributor** | A component interface whose `SendVisibilityInit(session)` method is called by the region after each entity-create packet (`F_CREATE_MONSTER` / `F_CREATE_STATIC`). Allows components to send follow-up packets (e.g. `F_PLAYER_INVENTORY` for equipment) without the region needing to know about specific component types. Current implementor: `EquipmentComponent`. |
| **EquipmentComponent** | An `IVisibilityInitContributor` attached to creatures with visual equipment (from the `creature_items` table). Sends `F_PLAYER_INVENTORY` with equipped item slots on visibility. **Tech debt**: primary/secondary color fields are loaded but not yet serialized — the source generator's `[ConditionalOn]` only supports equality (not bitwise flags). |
| **Tick** | One iteration of a region's update loop. Runs at 20Hz (50ms interval). Processes pending commands, updates entities in active cells, and refreshes visibility sets. |
| **Visibility Set** | Per-entity cache of nearby entities within the max visibility range (4800 units / ~400 feet). Updated when an entity moves more than 100 units. Used to determine which entities a player can see and which players need to receive packets about an entity. |
| **Active Cell** | A cell containing at least one player. Only active cells and their 3×3 neighborhood are ticked each frame — empty cells have zero cost. |
| **Command Channel** | A `Channel<RegionCommand>` that allows handler threads and services to safely enqueue mutations (add, remove, move, transfer) for the region thread to process at the start of each tick. |
| **Handler Thread** | A thread-pool thread that executes packet handler methods. Stateless — each `[Rpc]` method receives a deserialized request DTO, performs work (possibly async), and returns a response DTO. Handler threads may read entity state but mutate it only via region commands. |
| **Realm** | The player's faction: Order (1) or Destruction (2). Determines enemies, allies, starting zones, and career availability. |
| **Career** | The player's class/profession (e.g. Black Orc, Bright Wizard, Warrior Priest). Determines abilities, stats, armor type, and mastery trees. A `byte` value (20–107) from the `characterinfo` table. |
| **Session** | A `GameSession` instance representing one authenticated client connection. Holds the session ID, client state machine, character list, and a thread-safe send channel. A session can outlive individual characters (e.g. character select → play → return to select). |
| **OidReservation** | A disposable ticket returned by `Region.ReserveOid()`. Ensures the OID is returned to the pool if initialization fails (via `using`). Consumed by `Region.AddAsync()` on success — subsequent disposal is a no-op. |
