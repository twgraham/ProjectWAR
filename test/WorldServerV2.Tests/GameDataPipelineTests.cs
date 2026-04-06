using System.Collections.Frozen;
using System.Collections.Immutable;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using System.Threading;
using Core.Domain;
using Core.Domain.Entities;
using Core.Domain.ValueObjects;
using Core.GameWorld.DataStore;
using Core.GameWorld.DataStore.Models;
using Core.GameWorld.DataStore.Providers;
using Core.GameWorld.Items;
using WorldServerV2.Data;
using WorldServerV2.Data.Models;

namespace WorldServerV2.Tests;

/// <summary>
/// Unit tests for the game data pipeline: store, providers, and loader.
/// Providers are tested with a real <see cref="WorldDbContext"/> backed by the
/// EF Core in-memory database.
/// </summary>
public class GameDataPipelineTests
{
    // ── GameDataStore ──────────────────────────────────────────────────

    [Fact]
    public void Store_throws_when_accessed_before_initialization()
    {
        var store = new GameDataStore();

        Should.Throw<InvalidOperationException>(() => _ = store.Items);
        Should.Throw<InvalidOperationException>(() => _ = store.Creatures);
        Should.Throw<InvalidOperationException>(() => _ = store.Zones);
    }

    [Fact]
    public void Store_exposes_data_after_initialization()
    {
        var store = new GameDataStore();
        var snapshot = CreateEmptySnapshot();

        store.Initialize(snapshot);

        store.Items.Definitions.ShouldBeEmpty();
        store.Creatures.Protos.ShouldBeEmpty();
        store.Zones.Infos.ShouldBeEmpty();
    }

    [Fact]
    public void Store_rejects_double_initialization()
    {
        var store = new GameDataStore();
        store.Initialize(CreateEmptySnapshot());

        Should.Throw<InvalidOperationException>(() => store.Initialize(CreateEmptySnapshot()));
    }

    [Fact]
    public void Store_interface_returns_same_data_as_concrete()
    {
        var store = new GameDataStore();
        store.Initialize(CreateEmptySnapshot());

        IGameDataStore iface = store;
        iface.Items.ShouldBe(store.Items);
        iface.Creatures.ShouldBe(store.Creatures);
        iface.Zones.ShouldBe(store.Zones);
    }

    // ── ItemDataProvider ───────────────────────────────────────────────

    [Fact]
    public async Task ItemProvider_loads_items_into_frozen_dictionary()
    {
        await using var db = CreateDb(nameof(ItemProvider_loads_items_into_frozen_dictionary));
        db.ItemInfos.AddRange(
            new ItemInfo { Entry = 100, Name = "Sword" },
            new ItemInfo { Entry = 200, Name = "Shield" },
            new ItemInfo { Entry = 300, Name = "Helm" });
        await db.SaveChangesAsync();

        var provider = new ItemDataProvider(new TestDbContextFactory(db), NullLogger<ItemDataProvider>.Instance);
        var data = await provider.LoadAsync();

        data.Definitions.Count.ShouldBe(3);
        data.Definitions[100].Name.ShouldBe("Sword");
        data.Definitions[200].Name.ShouldBe("Shield");
        data.Definitions[300].Name.ShouldBe("Helm");
    }

    [Fact]
    public async Task ItemProvider_handles_empty_table()
    {
        await using var db = CreateDb(nameof(ItemProvider_handles_empty_table));
        var provider = new ItemDataProvider(new TestDbContextFactory(db), NullLogger<ItemDataProvider>.Instance);

        var data = await provider.LoadAsync();

        data.Definitions.ShouldBeEmpty();
    }

    // ── CreatureDataProvider ───────────────────────────────────────────

    [Fact]
    public async Task CreatureProvider_crosslinks_spawn_to_proto()
    {
        await using var db = CreateDb(nameof(CreatureProvider_crosslinks_spawn_to_proto));
        db.CreatureProtos.Add(new CreatureProto { Entry = 42, Name = "Goblin" });
        db.CreatureSpawns.Add(new CreatureSpawn { Guid = 1, Entry = 42 });
        await db.SaveChangesAsync();

        var provider = new CreatureDataProvider(new TestDbContextFactory(db), NullLogger<CreatureDataProvider>.Instance);
        var data = await provider.LoadAsync();

        data.Protos.Count.ShouldBe(1);
        data.Spawns.Count.ShouldBe(1);
        data.Spawns[1].Proto.ShouldNotBeNull();
        data.Spawns[1].Proto!.Entry.ShouldBe(42u);
    }

    [Fact]
    public async Task CreatureProvider_logs_orphan_spawns_without_proto()
    {
        await using var db = CreateDb(nameof(CreatureProvider_logs_orphan_spawns_without_proto));
        db.CreatureSpawns.Add(new CreatureSpawn { Guid = 1, Entry = 999 });
        await db.SaveChangesAsync();

        var logger = new CapturingLogger<CreatureDataProvider>();
        var provider = new CreatureDataProvider(new TestDbContextFactory(db), logger);

        var data = await provider.LoadAsync();

        data.Spawns[1].Proto.ShouldBeNull();
        logger.Entries.ShouldContain(e => e.LogLevel == LogLevel.Warning);
    }

    // ── ZoneDataProvider ───────────────────────────────────────────────

    [Fact]
    public async Task ZoneProvider_loads_zones_and_enabled_jumps()
    {
        await using var db = CreateDb(nameof(ZoneProvider_loads_zones_and_enabled_jumps));
        db.ZoneInfos.Add(new ZoneInfo { ZoneId = 10, Name = "Nordland" });
        db.ZoneJumps.AddRange(
            new ZoneJump { Entry = 1, Enabled = 1 },
            new ZoneJump { Entry = 2, Enabled = 0 });
        await db.SaveChangesAsync();

        var provider = new ZoneDataProvider(new TestDbContextFactory(db), NullLogger<ZoneDataProvider>.Instance);
        var data = await provider.LoadAsync();

        data.Infos.Count.ShouldBe(1);
        data.Infos[10].Name.ShouldBe("Nordland");
        data.Jumps.Count.ShouldBe(1);
        data.Jumps.ShouldContainKey(1u);
        data.Jumps.ShouldNotContainKey(2u);
    }

    // ── GameDataLoader ─────────────────────────────────────────────────

    [Fact]
    public async Task Loader_initializes_store_from_providers()
    {
        var store = new GameDataStore();

        // Build a minimal service collection with constant providers
        var services = new ServiceCollection();
        services.AddSingleton<IDataProvider<ClassData>>(
            new ConstantProvider<ClassData>(
                new ClassData(
                    FrozenDictionary<Class, ClassInfo>.Empty,
                    FrozenDictionary<Class, List<ClassInfoItem>>.Empty)));
        services.AddSingleton<IDataProvider<ItemData>>(
            new ConstantProvider<ItemData>(
                new ItemData(
                    FrozenDictionary<uint, ItemDefinition>.Empty,
                    FrozenDictionary<uint, ItemSetDefinition>.Empty)));
        services.AddSingleton<IDataProvider<CreatureData>>(
            new ConstantProvider<CreatureData>(
                new CreatureData(
                    FrozenDictionary<uint, CreatureProto>.Empty,
                    FrozenDictionary<uint, CreatureSpawn>.Empty,
                    FrozenDictionary<uint, ImmutableArray<CreatureItem>>.Empty)));
        services.AddSingleton<IDataProvider<ZoneData>>(
            new ConstantProvider<ZoneData>(
                new ZoneData(
                    FrozenDictionary<ushort, ZoneInfo>.Empty,
                    FrozenDictionary<uint, ZoneJump>.Empty)));
        services.AddSingleton<IDataProvider<CareerStatData>>(
            new ConstantProvider<CareerStatData>(CareerStatData.Empty));
        services.AddSingleton<IDataProvider<AbilityData>>(
            new ConstantProvider<AbilityData>(AbilityData.Empty));
        services.AddSingleton<IDataProvider<SpawnData>>(
            new ConstantProvider<SpawnData>(SpawnData.Empty));

        var sp = services.BuildServiceProvider();

        var loader = new GameDataLoader(
            store,
            sp.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<GameDataLoader>.Instance);

        await loader.StartAsync(CancellationToken.None);

        // Should not throw — store is now initialized
        store.Items.Definitions.ShouldBeEmpty();
        store.Creatures.Protos.ShouldBeEmpty();
        store.Zones.Infos.ShouldBeEmpty();
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a <see cref="WorldDbContext"/> backed by the EF Core in-memory database.
    /// Each test gets its own isolated database via a unique <paramref name="dbName"/>.
    /// </summary>
    private static WorldDbContext CreateDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<WorldDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        return new WorldDbContext(options);
    }

    private static GameDataStore.Snapshot CreateEmptySnapshot() =>
        new(
            new ClassData(
                FrozenDictionary<Class, ClassInfo>.Empty,
                FrozenDictionary<Class, List<ClassInfoItem>>.Empty),
            new ItemData(
                FrozenDictionary<uint, ItemDefinition>.Empty,
                FrozenDictionary<uint, ItemSetDefinition>.Empty),
            new CreatureData(
                FrozenDictionary<uint, CreatureProto>.Empty,
                FrozenDictionary<uint, CreatureSpawn>.Empty,
                FrozenDictionary<uint, ImmutableArray<CreatureItem>>.Empty),
            new ZoneData(
                FrozenDictionary<ushort, ZoneInfo>.Empty,
                FrozenDictionary<uint, ZoneJump>.Empty),
            CareerStatData.Empty,
            AbilityData.Empty,
            SpawnData.Empty);

    /// <summary>
    /// Simple provider that always returns a pre-built constant value.
    /// </summary>
    private sealed class ConstantProvider<T>(T value) : IDataProvider<T>
    {
        public Task<T> LoadAsync() => Task.FromResult(value);
    }

    /// <summary>
    /// Test implementation of <see cref="IDbContextFactory{WorldDbContext}"/> that
    /// returns a provided <see cref="WorldDbContext"/> instance. Intended for
    /// unit tests that use an in-memory database instance.
    /// </summary>
    private sealed class TestDbContextFactory : IDbContextFactory<WorldDbContext>
    {
        private readonly WorldDbContext _db;
        public TestDbContextFactory(WorldDbContext db) => _db = db;
        public WorldDbContext CreateDbContext() => _db;
        public ValueTask<WorldDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => new ValueTask<WorldDbContext>(_db);
    }

    /// <summary>
    /// Null-pattern logger that discards all messages.
    /// </summary>
    private sealed class NullLogger<T> : ILogger<T>
    {
        public static readonly NullLogger<T> Instance = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }

    /// <summary>
    /// Logger that captures entries for assertion.
    /// </summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel LogLevel, string Message)> Entries { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }
}
