using Shouldly;
using WorldServerV2.Data.Domain;
using WorldServerV2.Data.Entities;
using WorldServerV2.World.Components;
using WorldServerV2.World.Entities;

namespace WorldServer.Tests;

/// <summary>
/// Tests for the entity hierarchy and optional component model:
/// <see cref="WorldEntity"/> (abstract), <see cref="UnitEntity"/> (abstract),
/// <see cref="PlayerEntity"/>, <see cref="CreatureEntity"/>, <see cref="GameObjectEntity"/>,
/// <see cref="HealthComponent"/>, and <see cref="IComponent"/>.
/// </summary>
public class WorldEntityTests
{
    // ── Helper factory ──────────────────────────────────────────────────

    private static PlayerEntity MakePlayer(ushort id = 1, uint maxHp = 1000, string name = "TestPlayer")
        => new(id, new Character { CharacterId = id, Name = name }, maxHp);

    private static GameObjectEntity MakeGameObject(ushort id = 1, uint entry = 100, string name = "Chest")
        => new(id, new GameObjectSpawnDescriptor { Entry = entry, RegionId = 1, ZoneId = 100, Position = default, Interactable = true }, name);

    // ── WorldEntity: Identity ───────────────────────────────────────────

    [Fact]
    public void Entity_exposes_identity_set_at_construction()
    {
        var player = MakePlayer(42, name: "Hero");

        player.ObjectId.ShouldBe((ushort)42);
        player.Type.ShouldBe(EntityType.Player);
        player.Name.ShouldBe("Hero");
    }

    [Fact]
    public void Entity_type_matches_concrete_subclass()
    {
        var player = MakePlayer();
        var gameObj = MakeGameObject();

        player.Type.ShouldBe(EntityType.Player);
        gameObj.Type.ShouldBe(EntityType.GameObject);
    }

    [Fact]
    public void Entity_position_defaults_to_zero()
    {
        var entity = MakePlayer();
        entity.Position.ShouldBe(WorldPosition.Zero);
    }

    [Fact]
    public void Entity_position_is_mutable()
    {
        var entity = MakePlayer();
        var pos = new WorldPosition(1, 100, 200, 50, 1024, 5);

        entity.Position = pos;

        entity.Position.RegionId.ShouldBe((ushort)1);
        entity.Position.X.ShouldBe(100);
        entity.Position.Y.ShouldBe(200);
        entity.Position.Z.ShouldBe(50);
        entity.Position.Heading.ShouldBe((ushort)1024);
        entity.Position.ZoneId.ShouldBe((ushort)5);
    }

    // ── WorldEntity: Optional Component Bag ─────────────────────────────

    [Fact]
    public void Attach_adds_component_and_sets_owner()
    {
        var entity = MakePlayer();
        var stub = new StubComponent();

        entity.Attach(stub);

        entity.Has<StubComponent>().ShouldBeTrue();
        entity.Get<StubComponent>().ShouldBeSameAs(stub);
        stub.Owner.ShouldBeSameAs(entity);
    }

    [Fact]
    public void Attach_duplicate_type_throws()
    {
        var entity = MakePlayer();
        entity.Attach(new StubComponent());

        Should.Throw<InvalidOperationException>(() => entity.Attach(new StubComponent()));
    }

    [Fact]
    public void Attach_null_throws()
    {
        var entity = MakePlayer();
        Should.Throw<ArgumentNullException>(() => entity.Attach<StubComponent>(null!));
    }

    [Fact]
    public void Get_missing_component_throws()
    {
        var entity = MakePlayer();
        Should.Throw<InvalidOperationException>(() => entity.Get<StubComponent>());
    }

    [Fact]
    public void TryGet_returns_null_for_missing_component()
    {
        var entity = MakePlayer();
        entity.TryGet<StubComponent>().ShouldBeNull();
    }

    [Fact]
    public void Detach_removes_component_and_clears_owner()
    {
        var entity = MakePlayer();
        var stub = new StubComponent();
        entity.Attach(stub);

        entity.Detach<StubComponent>().ShouldBeTrue();

        entity.Has<StubComponent>().ShouldBeFalse();
        stub.Owner.ShouldBeNull();
    }

    [Fact]
    public void Detach_missing_component_returns_false()
    {
        var entity = MakePlayer();
        entity.Detach<StubComponent>().ShouldBeFalse();
    }

    [Fact]
    public void ComponentCount_tracks_attached_components()
    {
        var entity = MakePlayer();
        entity.ComponentCount.ShouldBe(0);

        entity.Attach(new StubComponent());
        entity.ComponentCount.ShouldBe(1);
    }

    // ── WorldEntity: Tick Dispatch ──────────────────────────────────────

    [Fact]
    public void Update_ticks_only_tickable_components()
    {
        var tickable = new TickableStub();
        var nonTickable = new StubComponent();
        var entity = MakePlayer();

        entity.Attach(tickable);
        entity.Attach(nonTickable);

        entity.Update(1000);

        tickable.LastTick.ShouldBe(1000L);
    }

    [Fact]
    public void Update_with_no_tickable_components_is_safe()
    {
        var entity = MakePlayer();
        entity.Attach(new StubComponent());

        // Should not throw
        entity.Update(1000);
    }

    [Fact]
    public void Tickable_cache_invalidates_on_attach()
    {
        var entity = MakePlayer();
        entity.Update(100); // builds empty cache

        var tickable = new TickableStub();
        entity.Attach(tickable);
        entity.Update(200);

        tickable.LastTick.ShouldBe(200L);
    }

    [Fact]
    public void Tickable_cache_invalidates_on_detach()
    {
        var tickable = new TickableStub();
        var entity = MakePlayer();
        entity.Attach(tickable);
        entity.Update(100);
        tickable.LastTick.ShouldBe(100L);

        entity.Detach<TickableStub>();
        entity.Update(200);

        // Should still be 100 — was detached before tick 200
        tickable.LastTick.ShouldBe(100L);
    }

    // ── UnitEntity: Direct Health Field ─────────────────────────────────

    [Fact]
    public void UnitEntity_has_health_at_construction()
    {
        var player = MakePlayer(maxHp: 500);

        player.Health.ShouldNotBeNull();
        player.Health.Current.ShouldBe(500u);
        player.Health.Max.ShouldBe(500u);
    }

    [Fact]
    public void UnitEntity_level_realm_faction_default_to_zero()
    {
        var player = MakePlayer();

        player.Level.ShouldBe((byte)0);
        player.Realm.ShouldBe((byte)0);
        player.Faction.ShouldBe((byte)0);
    }

    [Fact]
    public void UnitEntity_level_realm_faction_are_mutable()
    {
        var player = MakePlayer();

        player.Level = 40;
        player.Realm = 1;
        player.Faction = 6;

        player.Level.ShouldBe((byte)40);
        player.Realm.ShouldBe((byte)1);
        player.Faction.ShouldBe((byte)6);
    }

    // ── HealthComponent ─────────────────────────────────────────────────

    [Fact]
    public void Health_starts_at_max()
    {
        var hp = new HealthComponent(500);
        hp.Current.ShouldBe(500u);
        hp.Max.ShouldBe(500u);
        hp.Percent.ShouldBe((byte)100);
        hp.IsAlive.ShouldBeTrue();
        hp.IsDead.ShouldBeFalse();
    }

    [Fact]
    public void TakeDamage_reduces_current_hp()
    {
        var hp = new HealthComponent(1000);

        var dealt = hp.TakeDamage(300);

        dealt.ShouldBe(300u);
        hp.Current.ShouldBe(700u);
        hp.Percent.ShouldBe((byte)70);
    }

    [Fact]
    public void TakeDamage_clamps_to_zero()
    {
        var hp = new HealthComponent(100);

        var dealt = hp.TakeDamage(999);

        dealt.ShouldBe(100u);
        hp.Current.ShouldBe(0u);
        hp.IsDead.ShouldBeTrue();
    }

    [Fact]
    public void TakeDamage_on_dead_entity_returns_zero()
    {
        var hp = new HealthComponent(100);
        hp.TakeDamage(100);

        hp.TakeDamage(50).ShouldBe(0u);
    }

    [Fact]
    public void Heal_restores_hp_clamped_to_max()
    {
        var hp = new HealthComponent(100);
        hp.TakeDamage(60);

        var healed = hp.Heal(999);

        healed.ShouldBe(60u);
        hp.Current.ShouldBe(100u);
    }

    [Fact]
    public void Heal_on_dead_entity_does_nothing()
    {
        var hp = new HealthComponent(100);
        hp.TakeDamage(100);

        hp.Heal(50).ShouldBe(0u);
        hp.Current.ShouldBe(0u);
    }

    [Fact]
    public void Resurrect_restores_from_death()
    {
        var hp = new HealthComponent(1000);
        hp.TakeDamage(1000);
        hp.IsDead.ShouldBeTrue();

        hp.Resurrect(50).ShouldBeTrue();

        hp.IsAlive.ShouldBeTrue();
        hp.Current.ShouldBe(500u);
    }

    [Fact]
    public void Resurrect_on_alive_entity_returns_false()
    {
        var hp = new HealthComponent(100);
        hp.Resurrect().ShouldBeFalse();
    }

    [Fact]
    public void Max_health_change_clamps_current()
    {
        var hp = new HealthComponent(1000);
        hp.Max = 500;

        hp.Current.ShouldBe(500u);
        hp.Max.ShouldBe(500u);
    }

    [Fact]
    public void Zero_max_health_throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new HealthComponent(0));

        var hp = new HealthComponent(100);
        Should.Throw<ArgumentOutOfRangeException>(() => hp.Max = 0);
    }

    // ── PlayerEntity ────────────────────────────────────────────────────

    [Fact]
    public void PlayerEntity_exposes_character_data()
    {
        var character = new Character { CharacterId = 42, Name = "Hero" };
        var player = new PlayerEntity(1, character, 1000);

        player.Character.ShouldBeSameAs(character);
        player.CharacterId.ShouldBe(42u);
        player.Name.ShouldBe("Hero");
        player.Type.ShouldBe(EntityType.Player);
        player.DisconnectType.ShouldBe(DisconnectType.Unclean); // default
    }

    [Fact]
    public void PlayerEntity_null_character_throws()
    {
        Should.Throw<ArgumentNullException>(() => new PlayerEntity(1, null!, 1000));
    }

    // ── GameObjectEntity ────────────────────────────────────────────────

    [Fact]
    public void GameObjectEntity_has_no_health()
    {
        var go = MakeGameObject();

        go.Type.ShouldBe(EntityType.GameObject);
        go.Entry.ShouldBe(100u);
        go.Interactable.ShouldBeTrue();
        // GameObjectEntity is NOT a UnitEntity, so no Health field
        go.ShouldNotBeOfType<UnitEntity>();
    }

    // ── Test Helpers ────────────────────────────────────────────────────

    private sealed class StubComponent : ComponentBase;

    private sealed class TickableStub : ComponentBase, ITickable
    {
        public long LastTick { get; private set; }
        public void Update(long tick) => LastTick = tick;
    }
}
