using WorldServerV2.World.Entities;

namespace WorldServerV2.World.Spatial;

/// <summary>
/// A command queued to a <see cref="Region"/>'s inbound channel for thread-safe
/// cross-thread operations. Commands are processed at the start of each tick before
/// entity updates. Concrete command types are nested for discoverability.
/// </summary>
public abstract class RegionCommand
{
    private RegionCommand() { }

    /// <summary>Add an entity to the region at the specified position.</summary>
    /// <param name="entity">The entity to add.</param>
    /// <param name="position">Where to place the entity.</param>
    /// <param name="placed">Optional signal set after the entity is placed in its cell.</param>
    public sealed class AddEntity(
        WorldEntity entity,
        WorldPosition position,
        TaskCompletionSource<bool>? placed = null) : RegionCommand
    {
        public WorldEntity Entity { get; } = entity ?? throw new ArgumentNullException(nameof(entity));
        public WorldPosition Position { get; } = position;
        public TaskCompletionSource<bool>? Placed { get; } = placed;
    }

    /// <summary>Remove an entity from the region.</summary>
    public sealed class RemoveEntity(WorldEntity entity) : RegionCommand
    {
        public WorldEntity Entity { get; } = entity ?? throw new ArgumentNullException(nameof(entity));
    }

    /// <summary>Update an entity's position (from a movement packet or game system).</summary>
    public sealed class MoveEntity(WorldEntity entity, WorldPosition newPosition) : RegionCommand
    {
        public WorldEntity Entity { get; } = entity ?? throw new ArgumentNullException(nameof(entity));
        public WorldPosition NewPosition { get; } = newPosition;
    }

    /// <summary>Transfer an entity from another region into this one.</summary>
    public sealed class TransferIn(WorldEntity entity, WorldPosition destination) : RegionCommand
    {
        public WorldEntity Entity { get; } = entity ?? throw new ArgumentNullException(nameof(entity));
        public WorldPosition Destination { get; } = destination;
    }
}
