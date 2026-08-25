using System.Collections.Concurrent;
using Core.GameWorld.DataStore;
using Core.GameWorld.Spawning;
using Core.GameWorld.Telemetry;
using Core.Spatial;
using Microsoft.Extensions.Logging;

namespace Core.GameWorld.Spatial;

/// <summary>
/// Singleton registry of all <see cref="Region"/> instances in the server. Provides
/// O(1) lookup by region ID and lazy creation for regions that haven't been accessed yet.
/// <para>
/// <b>Lifecycle</b>: Registered as a singleton in DI. Each region is created on first
/// access (via <see cref="GetOrCreate"/>) and started when <see cref="StartAll"/> is called
/// (typically by an <see cref="Microsoft.Extensions.Hosting.IHostedService"/>).
/// </para>
/// <para>
/// <b>Thread-safety</b>: All state lives in a <see cref="ConcurrentDictionary{TKey,TValue}"/>.
/// No manual locking required.
/// </para>
/// </summary>
public sealed class RegionManager : IDisposable
{
    private readonly ConcurrentDictionary<ushort, Region> _regions = new();
    private readonly ILoggerFactory _loggerFactory;
    private readonly IRegionEventDispatcher _eventDispatcher;
    private readonly IEntityFactory _entityFactory;
    private readonly IGameDataStore _gameData;
    private readonly IWorldServerMetrics _metrics;
    private readonly IOcclusionProvider? _occlusion;
    private readonly bool _autoStart;

    public RegionManager(
        ILoggerFactory loggerFactory,
        IRegionEventDispatcher eventDispatcher,
        IEntityFactory entityFactory,
        IGameDataStore gameData,
        IWorldServerMetrics metrics,
        IOcclusionProvider? occlusion = null,
        bool autoStart = true)
    {
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _eventDispatcher = eventDispatcher ?? throw new ArgumentNullException(nameof(eventDispatcher));
        _entityFactory = entityFactory ?? throw new ArgumentNullException(nameof(entityFactory));
        _gameData = gameData ?? throw new ArgumentNullException(nameof(gameData));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _occlusion = occlusion;
        _autoStart = autoStart;
    }

    /// <summary>Number of regions currently created.</summary>
    public int Count => _regions.Count;

    /// <summary>
    /// Gets an existing region by ID, or <c>null</c> if it hasn't been created yet.
    /// Thread-safe.
    /// </summary>
    public Region? Get(ushort regionId)
    {
        return _regions.GetValueOrDefault(regionId);
    }

    /// <summary>
    /// Gets or creates a region for the given ID. When <c>autoStart</c> is <c>true</c>
    /// (the default), the region's tick thread is started immediately on first creation.
    /// Safe to call concurrently — <see cref="Region.Start"/> is idempotent. Thread-safe.
    /// </summary>
    public Region GetOrCreate(ushort regionId)
    {
        return _regions.GetOrAdd(regionId, id =>
        {
            var region = new Region(id, _eventDispatcher, _entityFactory, _gameData, _loggerFactory.CreateLogger<Region>(), _metrics, _occlusion);
            if (_autoStart)
                region.Start();
            return region;
        });
    }

    /// <summary>
    /// Starts all regions that have been created but not yet started.
    /// Typically called once during server startup.
    /// </summary>
    public void StartAll()
    {
        foreach (var region in _regions.Values)
        {
            if (!region.IsRunning)
                region.Start();
        }
    }

    /// <summary>
    /// Stops all running regions and waits for their tick threads to finish.
    /// Typically called during server shutdown.
    /// </summary>
    public void StopAll()
    {
        foreach (var region in _regions.Values)
            region.Stop();
    }

    /// <summary>
    /// Returns a snapshot of all region IDs currently registered.
    /// </summary>
    public IReadOnlyList<ushort> GetAllRegionIds()
        => _regions.Keys.ToList();

    /// <summary>
    /// Returns a snapshot of all regions currently registered.
    /// </summary>
    public IReadOnlyList<Region> GetAllRegions()
        => _regions.Values.ToList();

    public void Dispose()
    {
        StopAll();

        foreach (var region in _regions.Values)
            region.Dispose();

        _regions.Clear();
    }
}
