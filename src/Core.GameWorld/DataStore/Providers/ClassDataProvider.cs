using System.Collections.Frozen;
using Core.Domain;
using Core.GameWorld.DataStore.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Core.GameWorld.DataStore.Providers;

public class ClassDataProvider(
    IDbContextFactory<WorldDbContext> dbContextFactory,
    ILogger<ItemDataProvider> logger) : IDataProvider<ClassData>
{
    public async Task<ClassData> LoadAsync()
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var classInfo = (await db.ClassInfos
                .AsNoTracking()
                .ToListAsync())
            .ToFrozenDictionary(i => i.ClassId);

        var classIdLookup = classInfo.ToDictionary(x => x.Value.Id, x => x.Key);
        
        var classInfoItems = (await db.ClassInfoItems
                .AsNoTracking()
                .ToListAsync())
            .GroupBy(i => i.Id)
            .ToFrozenDictionary(x => classIdLookup[x.Key], x => x.ToList());

        logger.LogInformation("Loaded {Count} item definitions", classInfo.Count);

        return new ClassData(Infos: classInfo, Items: classInfoItems);
    }
}