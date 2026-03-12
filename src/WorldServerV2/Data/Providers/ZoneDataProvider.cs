using System.Collections.Frozen;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WorldServerV2.Data.Domain;
using WorldServerV2.Data.Entities;

namespace WorldServerV2.Data.Providers;

/// <summary>
/// Loads all zone/map-related data from the World database via EF Core.
/// </summary>
public sealed class ZoneDataProvider(
    WorldDbContext db,
    ILogger<ZoneDataProvider> logger) : IDataProvider<ZoneData>
{
    public ZoneData Load()
    {
        var infos = db.ZoneInfos
            .AsNoTracking()
            .ToList()
            .ToFrozenDictionary(z => z.ZoneId);

        var jumps = db.ZoneJumps
            .AsNoTracking()
            .Where(j => j.Enabled == 1)
            .ToList()
            .ToFrozenDictionary(j => j.Entry);

        logger.LogInformation(
            "Loaded {ZoneCount} zones, {JumpCount} zone jumps",
            infos.Count,
            jumps.Count);

        return new ZoneData(Infos: infos, Jumps: jumps);
    }
}
