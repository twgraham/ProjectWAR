using Core.GameWorld.DataStore;
using Core.GameWorld.Entities;

namespace Core.GameWorld.Spatial;

/// <summary>
/// Context passed to <see cref="IRegionAction"/> implementations during execution
/// on the region thread. Provides access to entities, game data, and the event
/// dispatcher — everything an action needs without importing <see cref="Region"/> directly.
/// </summary>
public interface IRegionActionContext
{
    /// <summary>Look up an entity by its runtime OID. Returns null if not found.</summary>
    WorldEntity? GetEntity(ushort oid);

    /// <summary>The immutable game data store.</summary>
    IGameDataStore GameData { get; }

    /// <summary>The region event dispatcher for firing domain events.</summary>
    IRegionEventDispatcher Dispatcher { get; }
}
