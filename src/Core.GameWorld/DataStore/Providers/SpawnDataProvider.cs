using System.Collections.Frozen;
using Core.Domain;
using Core.Domain.Entities;
using Core.GameWorld.DataStore.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Core.GameWorld.DataStore.Providers;

/// <summary>
/// Loads all spawn data from the World database, converts each DB record to a
/// <see cref="SpawnDescriptor"/> or <see cref="GameObjectSpawnDescriptor"/>, and buckets
/// the results by <see cref="CellKey"/> for O(1) cell-load lookups at runtime.
/// <para>
/// Zone data is loaded inline (a single extra query) to resolve region IDs and cell offsets
/// without coupling this provider to <see cref="ZoneDataProvider"/>.
/// </para>
/// </summary>
public sealed class SpawnDataProvider(
    IDbContextFactory<WorldDbContext> dbContextFactory,
    ILogger<SpawnDataProvider> logger) : IDataProvider<SpawnData>
{
    public async Task<SpawnData> LoadAsync()
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();

        // Load zone info for offset/region-id cross-linking
        var zones = (await db.ZoneInfos.AsNoTracking().ToListAsync())
            .ToFrozenDictionary(z => z.ZoneId);

        // ── Creature spawns ──────────────────────────────────────────────
        var creatureSpawns = await db.CreatureSpawns
            .AsNoTracking()
            .Where(s => s.Enabled != 0)
            .ToListAsync();

        var creatureBuckets = BucketCreatureSpawns(creatureSpawns, zones);

        logger.LogInformation(
            "Loaded {SpawnCount} creature spawns across {CellCount} cells",
            creatureSpawns.Count,
            creatureBuckets.Count);

        // ── Game-object spawns ───────────────────────────────────────────
        var gameObjectSpawns = await db.GameObjectSpawns
            .AsNoTracking()
            .ToListAsync();

        var gameObjectBuckets = BucketGameObjectSpawns(gameObjectSpawns, zones);

        logger.LogInformation(
            "Loaded {SpawnCount} game-object spawns across {CellCount} cells",
            gameObjectSpawns.Count,
            gameObjectBuckets.Count);

        return new SpawnData(Creatures: creatureBuckets, GameObjects: gameObjectBuckets);
    }

    private FrozenDictionary<CellKey, IReadOnlyList<SpawnDescriptor>> BucketCreatureSpawns(
        List<CreatureSpawn> spawns,
        FrozenDictionary<ushort, ZoneInfo> zones)
    {
        var buckets = new Dictionary<CellKey, List<SpawnDescriptor>>();
        var skippedCount = 0;

        foreach (var spawn in spawns)
        {
            if (!zones.TryGetValue(spawn.ZoneId, out var zone))
            {
                skippedCount++;
                continue;
            }

            var descriptor = SpawnDescriptorFactory.FromDbRecord(spawn, zone);
            var (cellX, cellY) = descriptor.Position.CellIndex;
            var key = new CellKey(descriptor.RegionId, cellX, cellY);

            if (!buckets.TryGetValue(key, out var list))
                buckets[key] = list = [];

            list.Add(descriptor);
        }

        if (skippedCount > 0)
            logger.LogWarning(
                "{Count} creature spawns skipped — zone {Field} not found in zone_infos",
                skippedCount, nameof(CreatureSpawn.ZoneId));

        return buckets.ToFrozenDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlyList<SpawnDescriptor>)kvp.Value);
    }

    private FrozenDictionary<CellKey, IReadOnlyList<GameObjectSpawnDescriptor>> BucketGameObjectSpawns(
        List<GameObjectSpawn> spawns,
        FrozenDictionary<ushort, ZoneInfo> zones)
    {
        var buckets = new Dictionary<CellKey, List<GameObjectSpawnDescriptor>>();
        var skippedCount = 0;

        foreach (var spawn in spawns)
        {
            if (!zones.TryGetValue((ushort)spawn.ZoneId, out var zone))
            {
                skippedCount++;
                continue;
            }

            var descriptor = SpawnDescriptorFactory.FromDbRecord(spawn, zone);
            var (cellX, cellY) = descriptor.Position.CellIndex;
            var key = new CellKey(descriptor.RegionId, cellX, cellY);

            if (!buckets.TryGetValue(key, out var list))
                buckets[key] = list = [];

            list.Add(descriptor);
        }

        if (skippedCount > 0)
            logger.LogWarning(
                "{Count} game-object spawns skipped — zone not found in zone_infos",
                skippedCount);

        return buckets.ToFrozenDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlyList<GameObjectSpawnDescriptor>)kvp.Value);
    }
}
