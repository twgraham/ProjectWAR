# System 1: Game Data Pipeline

> Part of the [WorldServerV2 Architecture](./Overview.md) documentation suite.
> See also: [Glossary](./Glossary.md) · [System 2: Entity Model](./System_02_EntityModel.md)
>
> **Status**: ✅ Complete — infrastructure + 3 domains (Items, Creatures, Zones).
>
> **Last updated**: April 2026

---

**Problem**: 27 static service classes load data into static dictionaries. No DI, no
thread safety for reload, no separation of data access from domain logic. Cross-linking
lives in a single 400+ line method.

**New design direction**:

| Concern | Old | New |
|---------|-----|-----|
| Storage | `static Dictionary<uint, Item_Info>` on `ItemService` | Typed read-only collections on an injectable `IGameDataStore` |
| Loading | Reflection-discovered `[LoadingFunction]` methods | Explicit startup pipeline via `IHostedService` with ordered phases |
| Cross-linking | `WorldMgr.LoadRelation()` monolith | Post-load composition step with declared dependencies |
| Reload | Ad-hoc `ReloadAbilities()` clears and reloads | Versioned snapshot swap — build new store, atomically swap ref |
| Access | `ItemService._Item_Info[id]` (static field) | `gameDataStore.Items[id]` (injected interface) |
| Thread safety | Inconsistent (some locks, most none) | Immutable collections after load — zero contention |

**Key types to introduce**:

- `IGameDataStore` — readonly facade: `.Items`, `.Creatures`, `.Abilities`, `.Zones`, etc.
- `GameDataStore` — concrete implementation holding `FrozenDictionary` or `ReadOnlyDictionary` collections
- `GameDataLoader` — `IHostedService` that orchestrates DB reads and builds the store
- Per-domain data providers (e.g. `ItemDataProvider`, `AbilityDataProvider`) injected into `GameDataLoader`
  for separation of loading logic

---

## Implemented Files (`src/WorldServerV2/Data/`)

| File | Purpose |
|------|---------|
| `IGameDataStore.cs` | Read-only facade interface |
| `GameDataStore.cs` | Concrete impl with immutable `Snapshot` + atomic swap |
| `IDataProvider.cs` | Generic provider contract (`IDataProvider<TData>`) |
| `GameDataLoader.cs` | `IHostedService` that orchestrates providers at startup |
| `GameDataServiceExtensions.cs` | DI registration (`services.AddGameData()`) |
| `Domain/ItemData.cs` | `FrozenDictionary<uint, ItemInfo>` (EF Core POCO) |
| `Domain/CreatureData.cs` | `FrozenDictionary<uint, CreatureProto>` + `FrozenDictionary<uint, CreatureSpawn>` |
| `Domain/ZoneData.cs` | `FrozenDictionary<ushort, ZoneInfo>` + `FrozenDictionary<uint, ZoneJump>` |
| `Providers/ItemDataProvider.cs` | Loads items from `WorldDbContext` (EF Core) |
| `Providers/CreatureDataProvider.cs` | Loads creatures + cross-links `CreatureSpawn.Proto` |
| `Providers/ZoneDataProvider.cs` | Loads zones, filters disabled jumps |

**Tests**: 10 tests in `GameDataPipelineTests.cs` (store lifecycle, provider loading, cross-linking, loader orchestration).

---

## Next Steps

Add domains incrementally (Quests, Abilities, PQuests, Battlefront, etc.) following the same `IDataProvider<TData>` pattern. A new domain requires:

1. A `Domain/XxxData.cs` record with `FrozenDictionary` properties
2. A `Providers/XxxDataProvider.cs` implementing `IDataProvider<XxxData>`
3. A new property on `IGameDataStore` / `GameDataStore`
4. Registration in `GameDataServiceExtensions.AddGameData()`

---

## Old Architecture (Reference)

27 static `*Service` classes discovered via reflection (`[Service(...)]` + `[LoadingFunction]`):

```
AnnounceService, BagService, BattlefrontService, BountyService, CellSpawnService,
ChapterService, CreatureService, DyeService, GameObjectService, GuildService,
HonorService, InstanceService, ItemService, LiveEventService, MailService,
PQuestService, QuestService, RallyPointService, RewardService,
RVRProgressionService, RVRZoneRewardService, ScenarioService, TokService,
VendorService, WaypointService, XpRenownService, ZoneService
```

All follow the same pattern:
```csharp
public static class ItemService {
    public static Dictionary<uint, Item_Info> _Item_Info;
    [LoadingFunction(true)]
    public static void LoadItem_Info() {
        _Item_Info = Database.SelectAllObjects<Item_Info>()...;
    }
}
```

Cross-linking happens in `WorldMgr.LoadRelation()` — a single 400+ line method.
