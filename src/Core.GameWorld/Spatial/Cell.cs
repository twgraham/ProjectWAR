using Core.GameWorld.Entities;

namespace Core.GameWorld.Spatial;

/// <summary>
/// A spatial partition cell within a <see cref="Region"/>'s grid. Each cell covers
/// <see cref="RegionConstants.CellSize"/> × <see cref="RegionConstants.CellSize"/> game units
/// (4096×4096 ≈ 341×341 feet).
/// <para>
/// Cells are lazily allocated when first accessed via <see cref="Region.GetOrCreateCell"/>.
/// A cell becomes <see cref="IsActive"/> when it contains at least one player — only active
/// cells are iterated during the region tick, and only cells adjacent to active cells
/// trigger NPC spawn loading.
/// </para>
/// <para>
/// All mutation happens on the region's tick thread. No locking is required.
/// </para>
/// </summary>
public sealed class Cell
{
    private readonly List<WorldEntity> _entities = new();
    private readonly List<PlayerEntity> _players = new();

    /// <summary>
    /// Creates a new cell at the given grid coordinates within the specified region.
    /// </summary>
    public Cell(Region region, int x, int y)
    {
        Region = region;
        X = x;
        Y = y;
    }

    /// <summary>The owning region.</summary>
    public Region Region { get; }

    /// <summary>Cell X index in the region grid (0-based).</summary>
    public int X { get; }

    /// <summary>Cell Y index in the region grid (0-based).</summary>
    public int Y { get; }

    /// <summary>All entities currently in this cell.</summary>
    public IReadOnlyList<WorldEntity> Entities => _entities;

    /// <summary>Players currently in this cell (subset of <see cref="Entities"/>).</summary>
    public IReadOnlyList<PlayerEntity> Players => _players;

    /// <summary>Total entities in this cell.</summary>
    public int EntityCount => _entities.Count;

    /// <summary>Number of players in this cell.</summary>
    public int PlayerCount => _players.Count;

    /// <summary>
    /// Whether this cell has at least one player. Active cells are iterated during the
    /// region tick. Adjacent cells are loaded when a cell becomes active.
    /// </summary>
    public bool IsActive => _players.Count > 0;

    /// <summary>
    /// Whether NPC spawns for this cell have been loaded. Set to <c>true</c> after the
    /// first call to the spawn loading system. Prevents duplicate loading.
    /// </summary>
    public bool IsLoaded { get; internal set; }

    /// <summary>
    /// Adds an entity to this cell. If the entity is a <see cref="PlayerEntity"/>,
    /// it is also added to the <see cref="Players"/> list.
    /// </summary>
    internal void AddEntity(WorldEntity entity)
    {
        _entities.Add(entity);

        if (entity is PlayerEntity player)
            _players.Add(player);
    }

    /// <summary>
    /// Removes an entity from this cell. Returns <c>true</c> if found and removed.
    /// </summary>
    internal bool RemoveEntity(WorldEntity entity)
    {
        if (!_entities.Remove(entity))
            return false;

        if (entity is PlayerEntity player)
            _players.Remove(player);

        return true;
    }

    /// <summary>Returns <c>true</c> if this cell contains the specified entity.</summary>
    public bool Contains(WorldEntity entity) => _entities.Contains(entity);

    public override string ToString() => $"Cell({X}, {Y}) — {_entities.Count} entities, {_players.Count} players";
}
