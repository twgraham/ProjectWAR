using System.Collections.Frozen;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WorldServerV2.Data.Domain;
using WorldServerV2.Data.Entities;

namespace WorldServerV2.Data.Providers;

/// <summary>
/// Loads all item-related data from the World database via EF Core.
/// </summary>
public sealed class ItemDataProvider(
    WorldDbContext db,
    ILogger<ItemDataProvider> logger) : IDataProvider<ItemData>
{
    public ItemData Load()
    {
        var infos = db.ItemInfos
            .AsNoTracking()
            .ToList()
            .ToFrozenDictionary(i => i.Entry);

        logger.LogInformation("Loaded {Count} item definitions", infos.Count);

        return new ItemData(Infos: infos);
    }
}
