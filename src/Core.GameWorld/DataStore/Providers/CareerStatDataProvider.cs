using System.Collections.Frozen;
using Core.Domain;
using Core.GameWorld.DataStore.Models;
using Core.GameWorld.Stats;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Core.GameWorld.DataStore.Providers;

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
