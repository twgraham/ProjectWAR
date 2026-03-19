using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WorldServerV2.World.Spatial;

namespace WorldServerV2.Services;

/// <summary>
/// Hosted service that manages the lifecycle of region tick threads.
/// <list type="bullet">
///   <item><b>StartAsync</b>: starts the tick thread for every region that has been created
///     (e.g. pre-registered regions during data loading, or regions created by the spawn system).</item>
///   <item><b>StopAsync</b>: signals all region threads to stop and waits for them to drain.</item>
/// </list>
/// <para>
/// Ordering: this service should start <b>after</b> <c>GameDataLoader</c> (so spawn data is
/// available) and <b>before</b> <c>NetworkManager</c> (so regions are ticking before clients
/// connect). The .NET Generic Host starts hosted services in registration order, so
/// <c>AddWorldTopology()</c> should be called between <c>AddGameData()</c> and
/// <c>AddServerNetworking()</c>.
/// </para>
/// </summary>
public sealed class WorldHostedService : IHostedService
{
    private readonly RegionManager _regionManager;
    private readonly ILogger<WorldHostedService> _logger;

    public WorldHostedService(RegionManager regionManager, ILogger<WorldHostedService> logger)
    {
        _regionManager = regionManager ?? throw new ArgumentNullException(nameof(regionManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var count = _regionManager.Count;
        _logger.LogInformation("Starting {Count} region tick thread(s)", count);

        _regionManager.StartAll();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping all region tick threads");

        _regionManager.StopAll();
        return Task.CompletedTask;
    }
}
