using System.Collections.Concurrent;
using Core.GameWorld.DataStore;
using Core.GameWorld.Entities;

namespace Core.GameWorld.Spatial;

/// <summary>
/// Default implementation of <see cref="IRegionActionContext"/> backed by the
/// region's internal entity dictionary, game data store, and event dispatcher.
/// Created once per region and reused across all action executions.
/// </summary>
internal sealed class RegionActionContext : IRegionActionContext
{
    private readonly ConcurrentDictionary<ushort, WorldEntity> _entitiesByOid;
    private readonly IGameDataStore _gameData;
    private readonly IRegionEventDispatcher _dispatcher;

    public RegionActionContext(
        ConcurrentDictionary<ushort, WorldEntity> entitiesByOid,
        IGameDataStore gameData,
        IRegionEventDispatcher dispatcher)
    {
        _entitiesByOid = entitiesByOid;
        _gameData = gameData;
        _dispatcher = dispatcher;
    }

    /// <inheritdoc />
    public WorldEntity? GetEntity(ushort oid)
    {
        _entitiesByOid.TryGetValue(oid, out var entity);
        return entity;
    }

    /// <inheritdoc />
    public IGameDataStore GameData => _gameData;

    /// <inheritdoc />
    public IRegionEventDispatcher Dispatcher => _dispatcher;
}
