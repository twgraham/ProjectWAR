using System.Collections.Frozen;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WorldServerV2.Data.Domain;
using WorldServerV2.World.Stats;

namespace WorldServerV2.Data.Providers;

/// <summary>
/// Loads career base stats from the <c>characterinfo_stats</c> table and groups them
/// into a <see cref="CareerStatData"/> bundle keyed by <c>(CareerLine, Level)</c>.
/// </summary>
public class CareerStatDataProvider(
    IDbContextFactory<WorldDbContext> dbContextFactory,
    ILogger<CareerStatDataProvider> logger) : IDataProvider<CareerStatData>
{
    public async Task<CareerStatData> LoadAsync()
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var rows = await db.CharacterInfoStats
            .AsNoTracking()
            .ToListAsync();

        var lookup = rows
            .GroupBy(r => ((byte)r.CareerLine, (byte)r.Level))
            .ToFrozenDictionary(
                g => g.Key,
                g => g.Select(r => new CareerStatEntry((StatId)r.StatId, (ushort)r.StatValue))
                      .ToArray());

        logger.LogInformation("Loaded career stats for {Count} career/level combinations", lookup.Count);

        return new CareerStatData(lookup);
    }
}
