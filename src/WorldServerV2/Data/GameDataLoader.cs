using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WorldServerV2.Data.Domain;

namespace WorldServerV2.Data;

/// <summary>
/// <see cref="IHostedService"/> that orchestrates loading all static game data
/// at application startup.
/// <para>
/// Each domain is loaded via its <see cref="IDataProvider{TData}"/>, then assembled
/// into an immutable <see cref="GameDataStore.Snapshot"/> and published through the
/// <see cref="GameDataStore"/>. Because hosted services complete before the server
/// begins accepting connections, all game data is guaranteed to be available when
/// the first packet handler runs.
/// </para>
/// <para>
/// Data providers are scoped (they depend on <see cref="WorldDbContext"/>), so the
/// loader creates a service scope to resolve them.
/// </para>
/// </summary>
public sealed class GameDataLoader(
    GameDataStore store,
    IServiceScopeFactory scopeFactory,
    ILogger<GameDataLoader> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Loading game data...");
        var sw = Stopwatch.StartNew();

        using var scope = scopeFactory.CreateScope();
        var classesTask = scope.ServiceProvider.GetRequiredService<IDataProvider<ClassData>>().LoadAsync();
        var itemsTask = scope.ServiceProvider.GetRequiredService<IDataProvider<ItemData>>().LoadAsync();
        var creaturesTask = scope.ServiceProvider.GetRequiredService<IDataProvider<CreatureData>>().LoadAsync();
        var zonesTask = scope.ServiceProvider.GetRequiredService<IDataProvider<ZoneData>>().LoadAsync();
        var careerStatsTask = scope.ServiceProvider.GetRequiredService<IDataProvider<CareerStatData>>().LoadAsync();
        var abilitiesTask = scope.ServiceProvider.GetRequiredService<IDataProvider<AbilityData>>().LoadAsync();
        var spawnsTask = scope.ServiceProvider.GetRequiredService<IDataProvider<SpawnData>>().LoadAsync();
        
        await Task.WhenAll(classesTask, itemsTask, creaturesTask, zonesTask, careerStatsTask, abilitiesTask, spawnsTask);

        var snapshot = new GameDataStore.Snapshot(classesTask.Result, itemsTask.Result, creaturesTask.Result, zonesTask.Result, careerStatsTask.Result, abilitiesTask.Result, spawnsTask.Result);
        store.Initialize(snapshot);

        logger.LogInformation("Game data loaded in {ElapsedMs}ms", sw.ElapsedMilliseconds);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
