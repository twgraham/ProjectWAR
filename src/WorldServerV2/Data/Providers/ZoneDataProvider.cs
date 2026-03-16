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
    IDbContextFactory<WorldDbContext> dbContextFactory,
    ILogger<ZoneDataProvider> logger) : IDataProvider<ZoneData>
{
    public async Task<ZoneData> LoadAsync()
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var infos = (await db.ZoneInfos
            .AsNoTracking()
            .ToListAsync())
            .ToFrozenDictionary(z => z.ZoneId);

        var jumps = (await db.ZoneJumps
            .AsNoTracking()
            .Where(j => j.Enabled == 1)
            .ToListAsync())
            .ToFrozenDictionary(j => j.Entry);
        
        logger.LogInformation(
            "Loaded {ZoneCount} zones, {JumpCount} zone jumps",
            infos.Count,
            jumps.Count);

        return new ZoneData(Infos: infos, Jumps: jumps);
    }
}
