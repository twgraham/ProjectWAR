using WorldServerV2.World.Entities;

namespace WorldServerV2.World.Spatial;

/// <summary>
/// Per-entity cached set of nearby entities, split into all-entities and players-only
/// for fast packet dispatch. Replaces the old <c>ObjectsInRange</c> / <c>PlayersInRange</c>
/// mutable lists with a controlled-access design.
/// <para>
/// <b>Write access</b> is <c>internal</c> — only the <see cref="Region"/> tick thread
/// mutates this set during visibility updates. <b>Read access</b> is public — game systems
/// iterate <see cref="Entities"/> and <see cref="Players"/> freely during the tick.
/// </para>
/// <para>
/// Backed by <see cref="HashSet{T}"/> for O(1) add/remove/contains. The separate
/// <see cref="Players"/> set avoids <c>is PlayerEntity</c> type checks on the hot path
/// (e.g. "send combat packet to all nearby players").
/// </para>
/// </summary>
public sealed class VisibilitySet
{
    private readonly HashSet<WorldEntity> _entities = new();
    private readonly HashSet<PlayerEntity> _players = new();

    /// <summary>All entities currently within visibility range.</summary>
    public IReadOnlyCollection<WorldEntity> Entities => _entities;

    /// <summary>Players currently within visibility range (subset of <see cref="Entities"/>).</summary>
    public IReadOnlyCollection<PlayerEntity> Players => _players;

    /// <summary>Total number of entities in the visibility set.</summary>
    public int Count => _entities.Count;

    /// <summary>Number of players in the visibility set.</summary>
    public int PlayerCount => _players.Count;

    /// <summary>Returns <c>true</c> if <paramref name="entity"/> is in the visibility set.</summary>
    public bool Contains(WorldEntity entity) => _entities.Contains(entity);

    /// <summary>
    /// Adds an entity to the visibility set. If the entity is a <see cref="PlayerEntity"/>,
    /// it is also added to the <see cref="Players"/> subset.
    /// </summary>
    /// <returns><c>true</c> if the entity was added; <c>false</c> if already present.</returns>
    internal bool Add(WorldEntity entity)
    {
        if (!_entities.Add(entity))
            return false;

        if (entity is PlayerEntity player)
            _players.Add(player);

        return true;
    }

    /// <summary>
    /// Removes an entity from the visibility set and the <see cref="Players"/> subset.
    /// </summary>
    /// <returns><c>true</c> if the entity was removed; <c>false</c> if not present.</returns>
    internal bool Remove(WorldEntity entity)
    {
        if (!_entities.Remove(entity))
            return false;

        if (entity is PlayerEntity player)
            _players.Remove(player);

        return true;
    }

    /// <summary>Removes all entities from both sets.</summary>
    internal void Clear()
    {
        _entities.Clear();
        _players.Clear();
    }
}
