# System 2: Game Object Model (Entity Hierarchy + Composition)

> Part of the [WorldServerV2 Architecture](./Overview.md) documentation suite.
> See also: [Glossary](./Glossary.md) · [System 1: Game Data](./System_01_GameData.md) · [System 3: World Topology](./System_03_WorldTopology.md)
>
> **Status**: ✅ Complete — hierarchy, component bag, health, and all entity subclasses implemented.
>
> **Last updated**: April 2026

---

**Problem**: Deep 6+ level inheritance tree. `Player.cs` is 6,837 lines. 23
`BaseInterface` components ticked every frame even when idle. Pervasive `is Player` /
`is Creature` type checks.

**New design direction**: Shallow sealed hierarchy for **required** state (compile-time
safety), with an optional component bag for **dynamic** behaviors.

| Concern | Old | New |
|---------|-----|-----|
| Identity | Runtime type (`is Player`) | Concrete subclass + `EntityType` discriminator |
| Required state | Scattered across 23 `BaseInterface` components | Direct fields on typed entity subclasses |
| Optional behaviors | `BaseInterface` subclasses, all ticked | `IComponent` bag — opt-in attach/detach, `ITickable` tick only when present |
| Health | `Unit.Health` field (always present for units) | `UnitEntity.Health` direct field (never null) |
| Player | 6,837-line god class | `PlayerEntity` sealed class with thin direct fields; logic in focused services/components |
| Global lists | `static Player._Players`, `Player.PlayersByCharId` | `PlayerService` singleton (typed to `PlayerEntity`) |

---

## Hierarchy

```
WorldEntity (abstract) — ObjectId, Name, Position, optional component bag
├── UnitEntity (abstract) — Health (HealthComponent, direct field), Level, Realm, Faction
│   ├── PlayerEntity (sealed) — Character record, CharacterId, DisconnectType
│   ├── CreatureEntity (sealed) — CreatureProto, CreatureSpawn, Entry
│   └── PetEntity (sealed) — CreatureProto, CreatureSpawn, Owner
└── GameObjectEntity (sealed) — Entry, VfxState, Interactable
```

---

## Key Design Decisions

| Decision | Rationale |
|----------|----------|
| Required state as direct fields | Compile-time safety — `player.CharacterId` never fails, unlike `entity.Get<PlayerIdentity>().CharacterId` |
| Hierarchy is sealed at leaf level | Prevents accidental extension; new entity kinds require explicit design |
| `HealthComponent` is standalone (not `IComponent`) | Health is required on all units — it's a direct field on `UnitEntity`, not an optional bag item |
| `EntityType` is a plain `enum : byte`, not `[Flags]` | Entities are exactly one type — flags were dishonest (nothing was ever `Unit \| Player`) |
| `PlayerService` typed to `PlayerEntity` | `Bind(GameSession, PlayerEntity)` rejects non-player entities at compile time |
| Optional component bag retained on `WorldEntity` | Guild membership, crafting state, scenario tracking, etc. are truly optional and dynamic |

---

## Key Types

| Type | Purpose |
|------|--------|
| `WorldEntity` | Abstract base — identity, position, optional `IComponent` bag |
| `UnitEntity` | Abstract — `HealthComponent Health`, `Level`, `Realm`, `Faction` |
| `PlayerEntity` | `Character`, `CharacterId`, `DisconnectType` (human-controlled) |
| `CreatureEntity` | `CreatureProto`, `CreatureSpawn`, `Entry` (NPC mobs) |
| `PetEntity` | Like creature but with `UnitEntity Owner` (player-owned) |
| `GameObjectEntity` | `Entry`, `VfxState`, `Interactable` (doors, chests, capture points) |
| `HealthComponent` | HP pool — `Current`, `Max`, `TakeDamage()`, `Heal()`, `Resurrect()` |
| `IComponent` / `ComponentBase` | Optional behavior contract, auto-manages `Owner` back-reference |
| `ITickable` | Opt-in tick for optional components (only ticked when attached) |
| `EntityType` | Discriminator enum: `Player`, `Creature`, `Pet`, `GameObject`, `Siege`, `PublicQuest`, `Keep` |

---

## Implemented Files

```
src/WorldServerV2/World/
├── Components/
│   ├── IComponent.cs              — Optional behaviour contract + ComponentBase convenience class
│   ├── ITickable.cs               — Opt-in tick interface for optional components
│   └── HealthComponent.cs         — HP pool (standalone class, direct field on UnitEntity)
└── Entities/
    ├── EntityType.cs              — enum: Player, Creature, Pet, GameObject, Siege, PublicQuest, Keep
    ├── WorldEntity.cs             — Abstract base: ObjectId, Name, Position, optional component bag
    ├── WorldPosition.cs           — readonly record struct: X, Y, Z, Heading, ZoneId
    ├── UnitEntity.cs              — Abstract: Health (direct), Level, Realm, Faction
    ├── PlayerEntity.cs            — Sealed: Character record, CharacterId, DisconnectType
    ├── CreatureEntity.cs          — Sealed: Proto, Spawn, Entry
    ├── PetEntity.cs               — Sealed: Proto, Spawn, Owner
    ├── GameObjectEntity.cs        — Sealed: Entry, VfxState, Interactable
    └── DisconnectType.cs          — enum: Unclean, Clean, Crash
```

**Tests**: 31 tests in `WorldEntityTests.cs` (entity identity, component lifecycle, tick dispatch, health, player/game-object specifics).

---

## Next Steps

Add domain-specific optional components as later systems are built:

- System 4 (Combat): `CombatComponent`, stat dirty tracking
- System 7 (Groups): `GroupMemberComponent`
- System 8 (Guilds): `GuildComponent`
- System 11 (Scenarios): `ScenarioParticipantComponent`
- System 13 (Spawning): `MovementComponent`, `BrainComponent` stubs already attached by `IEntityFactory`

The rule: if state is required on all units, it is a direct field on `UnitEntity`. If it is optional or dynamic, it lives in the component bag.

---

## Old Architecture (Reference)

```
Point3D (FrameWork)
 └── Object (826 lines) — base world entity with OID, position, cell membership
       └── Unit (1,545 lines) — health, stats, combat state, 9 interfaces
             ├── Player (6,837 lines) — 14 additional interfaces, 23 total
             ├── Creature (1,463 lines) → Pet, Standard, Boss, KeepCreature, ...
             ├── GameObject (793 lines) → LootChest, GoldChest, KeepGameObject
             └── PublicQuest (1,015 lines)
       └── BattleFrontObjective → BattleFrontKeep (2,103 lines)
       └── BattlefieldObjective, ChapterObject, HotSpot, RvRStructure, Siege
```

Each `Unit` has up to 23 `BaseInterface` components (`CombatInterface`,
`BuffInterface`, `ItemsInterface`, `AbilityInterface`, etc.) registered in a `List<BaseInterface>`
and **all ticked every frame** — even idle players.
