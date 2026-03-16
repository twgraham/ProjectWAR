using System.Collections.Frozen;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WorldServerV2.Data.Domain;

namespace WorldServerV2.Data.Providers;

/// <summary>
/// Loads all item-related data from the World database via EF Core.
/// </summary>
public sealed class ItemDataProvider(
    IDbContextFactory<WorldDbContext> dbContextFactory,
    ILogger<ItemDataProvider> logger) : IDataProvider<ItemData>
{
    public async Task<ItemData> LoadAsync()
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var infos = (await db.ItemInfos
            .AsNoTracking()
            .ToListAsync())
            .ToFrozenDictionary(i => i.Entry);

        logger.LogInformation("Loaded {Count} item definitions", infos.Count);

        return new ItemData(Infos: infos);
    }
}
