using System.Collections.Frozen;
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
    WorldDbContext db,
    ILogger<CreatureDataProvider> logger) : IDataProvider<CreatureData>
{
    public CreatureData Load()
    {
        var protos = db.CreatureProtos
            .AsNoTracking()
            .ToList()
            .ToFrozenDictionary(p => p.Entry);

        var spawnList = db.CreatureSpawns
            .AsNoTracking()
            .ToList();

        CrossLinkSpawns(spawnList, protos);

        var spawns = spawnList.ToFrozenDictionary(s => s.Guid);

        logger.LogInformation(
            "Loaded {ProtoCount} creature prototypes, {SpawnCount} creature spawns",
            protos.Count,
            spawns.Count);

        return new CreatureData(Protos: protos, Spawns: spawns);
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
