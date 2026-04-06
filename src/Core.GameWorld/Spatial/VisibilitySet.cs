using System.Buffers;
using Core.GameWorld.Entities;

namespace Core.GameWorld.Spatial;

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
/// <para>
/// Mutations and player snapshots are guarded by <see cref="_lock"/> to allow safe
/// concurrent reads from handler threads (e.g. movement relay) while the region tick
/// thread mutates the sets.
/// </para>
/// </summary>
public sealed class VisibilitySet
{
    private readonly object _lock = new();
    private readonly HashSet<WorldEntity> _entities = [];
    private readonly HashSet<PlayerEntity> _players = [];

    /// <summary>All entities currently within visibility range.</summary>
    public IReadOnlyCollection<WorldEntity> Entities
    {
        get
        {
            lock (_lock)
                return _entities.AsReadOnly();
        }
    }

    /// <summary>Players currently within visibility range (subset of <see cref="Entities"/>).</summary>
    public IReadOnlyCollection<PlayerEntity> Players
    {
        get {
            lock (_lock)
                return _players.AsReadOnly();
        }
    }

    /// <summary>Total number of entities in the visibility set.</summary>
    public int Count
    {
        get
        {
            lock (_lock)
                return _entities.Count;
        }
    }

    /// <summary>Number of players in the visibility set.</summary>
    public int PlayerCount
    {
        get
        {
            lock (_lock)
                return _players.Count;
        }
    }

    /// <summary>Returns <c>true</c> if <paramref name="entity"/> is in the visibility set.</summary>
    public bool Contains(WorldEntity entity)
    {
        lock (_lock)
            return _entities.Contains(entity);
    }

    /// <summary>
    /// Adds an entity to the visibility set. If the entity is a <see cref="PlayerEntity"/>,
    /// it is also added to the <see cref="Players"/> subset.
    /// </summary>
    /// <returns><c>true</c> if the entity was added; <c>false</c> if already present.</returns>
    internal bool Add(WorldEntity entity)
    {
        lock (_lock)
        {
            if (!_entities.Add(entity))
                return false;

            if (entity is PlayerEntity player)
                _players.Add(player);

            return true;
        }
    }

    /// <summary>
    /// Removes an entity from the visibility set and the <see cref="Players"/> subset.
    /// </summary>
    /// <returns><c>true</c> if the entity was removed; <c>false</c> if not present.</returns>
    internal bool Remove(WorldEntity entity)
    {
        lock (_lock)
        {
            if (!_entities.Remove(entity))
                return false;

            if (entity is PlayerEntity player)
                _players.Remove(player);

            return true;
        }
    }

    /// <summary>Removes all entities from both sets.</summary>
    internal void Clear()
    {
        lock (_lock)
        {
            _entities.Clear();
            _players.Clear();
        }
    }

    /// <summary>
    /// Creates a point-in-time snapshot of the <see cref="Players"/> set, backed by an
    /// <see cref="ArrayPool{T}"/>-rented array. The lock is held only for the duration
    /// of the copy — all downstream work (session resolution, packet sends) happens
    /// outside the lock.
    /// <para>
    /// Callers <b>must</b> dispose the returned <see cref="PlayerSnapshot"/> to return
    /// the rented array to the pool.
    /// </para>
    /// </summary>
    public PlayerSnapshot SnapshotPlayers()
    {
        lock (_lock)
        {
            var count = _players.Count;
            if (count == 0)
                return default;

            var array = ArrayPool<PlayerEntity>.Shared.Rent(count);
            _players.CopyTo(array);
            return new PlayerSnapshot(array, count);
        }
    }
}

/// <summary>
/// A disposable, pooled snapshot of the players in a <see cref="VisibilitySet"/>.
/// Use <see cref="Span"/> to iterate the players, then dispose to return the
/// backing array to <see cref="ArrayPool{T}"/>.
/// </summary>
public struct PlayerSnapshot : IDisposable
{
    private PlayerEntity[]? _array;

    /// <summary>Number of valid entries in the snapshot.</summary>
    public readonly int Count;

    internal PlayerSnapshot(PlayerEntity[] array, int count)
    {
        _array = array;
        Count = count;
    }

    /// <summary>The snapshot entries. Valid indices are <c>[0, Count)</c>.</summary>
    public readonly ReadOnlySpan<PlayerEntity> Span
    {
        get
        {
            var array = _array;
            return array is null ? default : array.AsSpan(0, Count);
        }
    }

    /// <summary>Returns the rented array to the pool, clearing references to avoid GC roots.</summary>
    public void Dispose()
    {
        if (_array is null)
            return;
        
        _array.AsSpan(0, Count).Clear();
        ArrayPool<PlayerEntity>.Shared.Return(_array, clearArray: true);
    }
}
