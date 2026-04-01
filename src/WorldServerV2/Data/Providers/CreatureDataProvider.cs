using System.Collections.Frozen;
using System.Collections.Immutable;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WorldServerV2.Data.Domain;
using WorldServerV2.Data.Entities;

namespace WorldServerV2.Data.Providers;

/// <summary>
/// Loads all creature-related data from the World database via EF Core and performs
/// intra-domain cross-linking (e.g., <see cref="CreatureSpawn.Proto"/>).
/// </summary>
public sealed class CreatureDataProvider(
    IDbContextFactory<WorldDbContext> dbContextFactory,
    ILogger<CreatureDataProvider> logger) : IDataProvider<CreatureData>
{
    public async Task<CreatureData> LoadAsync()
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var protos = (await db.CreatureProtos
            .AsNoTracking()
            .ToListAsync())
            .ToFrozenDictionary(x => x.Entry);

        var spawnList = await db.CreatureSpawns
            .AsNoTracking()
            .ToListAsync();
        
        CrossLinkSpawns(spawnList, protos);

        var spawns = spawnList.ToFrozenDictionary(s => s.Guid);

        var items = (await db.CreatureItems
            .AsNoTracking()
            .ToListAsync())
            .GroupBy(i => i.Entry)
            .ToFrozenDictionary(g => g.Key, g => g.ToImmutableArray());

        logger.LogInformation(
            "Loaded {ProtoCount} creature prototypes, {SpawnCount} creature spawns, {ItemGroupCount} creatures with equipment",
            protos.Count,
            spawns.Count,
            items.Count);

        return new CreatureData(Protos: protos, Spawns: spawns, Items: items);
    }

    /// <summary>
    /// Resolves <see cref="CreatureSpawn.Proto"/> from the prototype dictionary.
    /// Spawns that reference a missing prototype are logged and skipped.
    /// </summary>
    private void CrossLinkSpawns(
        List<CreatureSpawn> spawnList,
        FrozenDictionary<uint, CreatureProto> protos)
    {
        var orphanCount = 0;

        foreach (var spawn in spawnList)
        {
            if (protos.TryGetValue(spawn.Entry, out var proto))
            {
                spawn.Proto = proto;
            }
            else
            {
                orphanCount++;
            }
        }

        if (orphanCount > 0)
        {
            logger.LogWarning(
                "{OrphanCount} creature spawns reference missing prototypes and were not linked",
                orphanCount);
        }
    }
}
