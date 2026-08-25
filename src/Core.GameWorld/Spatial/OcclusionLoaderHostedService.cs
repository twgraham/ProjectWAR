using Core.Spatial.Zone;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Core.GameWorld.Spatial;

/// <summary>
/// Hosted service that loads zone occlusion data in the background so that
/// host startup is not blocked by disk I/O and KdTree construction.
/// <para>
/// Zone queries issued before loading completes degrade gracefully:
/// <see cref="ZoneManager"/> returns <c>0</c> for terrain height and
/// <c>NotLoaded</c> for raytests on zones that are not yet in memory.
/// </para>
/// </summary>
internal sealed class OcclusionLoaderHostedService(
    ZoneManager zoneManager,
    ILogger<OcclusionLoaderHostedService> logger) : IHostedService
{
    private Task? _loadingTask;

    /// <summary>
    /// Kicks off zone loading on the thread pool and returns immediately.
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // CancellationToken.None: zone loading is not incremental, so there
        // is nothing meaningful to cancel mid-run. StopAsync waits for it.
        _loadingTask = Task.Run(zoneManager.InitZones, CancellationToken.None);
        logger.LogInformation("Zone loading started in the background.");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Waits for any in-progress zone load to finish before the process exits.
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken) =>
        _loadingTask is { IsCompleted: false }
            ? _loadingTask.WaitAsync(cancellationToken)
            : Task.CompletedTask;
}
