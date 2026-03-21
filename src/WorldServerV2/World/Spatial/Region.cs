using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using WorldServerV2.World.Entities;
using static WorldServerV2.World.Spatial.RegionConstants;

namespace WorldServerV2.World.Spatial;

/// <summary>
/// A region is the top-level spatial container in the game world. Each region owns a
/// sparse cell grid, an OID registry, and a tick loop that processes commands, updates
/// entities, and maintains visibility.
/// <para>
/// <b>Threading model</b>: A region runs on a single dedicated thread. All entity state
/// mutations (cell membership, visibility sets, position side-effects) happen on this
/// thread. External code (packet handlers, services) communicates via the command
/// <see cref="Channel{T}"/> — a lock-free queue drained at the start of each tick.
/// </para>
/// <para>
/// <b>Tick flow</b>:
/// <list type="number">
///   <item>Drain command channel (add, remove, move, transfer)</item>
///   <item>Iterate active cells → tick each entity</item>
///   <item>Update visibility for entities that moved beyond the threshold</item>
/// </list>
/// </para>
/// </summary>
public sealed class Region : IDisposable
{
    private readonly Cell?[,] _cells = new Cell?[MaxCellIndex, MaxCellIndex];
    private readonly ConcurrentDictionary<ushort, WorldEntity> _entitiesByOid = new();
    private readonly HashSet<WorldEntity> _allEntities = new();
    private readonly Stack<ushort> _oidPool;
    private readonly HashSet<Cell> _activeCells = new();
    private readonly HashSet<Cell> _cellsWithEntities = new();
    private readonly HashSet<Cell> _tickCells = new();
    private readonly Channel<RegionCommand> _commands;
    private readonly List<WorldEntity> _movedEntities = new();
    private readonly ILogger _logger;

    private Thread? _thread;
    private volatile bool _running;

    /// <summary>Creates a new region with the given identifier.</summary>
    public Region(ushort regionId, ILogger logger)
    {
        RegionId = regionId;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _commands = Channel.CreateUnbounded<RegionCommand>(
            new UnboundedChannelOptions { SingleReader = true });

        // Pre-populate OID pool: usable range is 1..65535 (0 is reserved).
        // Push in reverse so that low OIDs are assigned first (pop order).
        _oidPool = new Stack<ushort>(MaxOid);
        for (int i = MaxOid; i >= 1; i--)
            _oidPool.Push((ushort)i);
    }

    // ── Identity ────────────────────────────────────────────────────────

    /// <summary>Unique region identifier.</summary>
    public ushort RegionId { get; }

    /// <summary>Total entities currently in this region.</summary>
    public int EntityCount => _entitiesByOid.Count;

    /// <summary>Number of active cells (cells with at least one player).</summary>
    public int ActiveCellCount => _activeCells.Count;

    /// <summary>Number of cells that contain any entity.</summary>
    public int OccupiedCellCount => _cellsWithEntities.Count;

    /// <summary>Whether the tick thread is running.</summary>
    public bool IsRunning => _running;

    // ── Cell Grid ───────────────────────────────────────────────────────

    /// <summary>
    /// Gets the cell at the specified grid coordinates, or <c>null</c> if not yet allocated.
    /// </summary>
    public Cell? GetCell(int cellX, int cellY)
    {
        if (cellX < 0 || cellX >= MaxCellIndex || cellY < 0 || cellY >= MaxCellIndex)
            return null;

        return _cells[cellX, cellY];
    }

    /// <summary>
    /// Gets or lazily creates the cell at the specified grid coordinates.
    /// Returns <c>null</c> if coordinates are out of bounds.
    /// </summary>
    internal Cell? GetOrCreateCell(int cellX, int cellY)
    {
        if (cellX < 0 || cellX >= MaxCellIndex || cellY < 0 || cellY >= MaxCellIndex)
            return null;

        return _cells[cellX, cellY] ??= new Cell(this, cellX, cellY);
    }

    // ── OID Management ──────────────────────────────────────────────────

    /// <summary>
    /// Looks up an entity by its Object ID within this region. Returns <c>null</c>
    /// if no entity has that OID. Thread-safe — reads are lock-free via
    /// <see cref="ConcurrentDictionary{TKey,TValue}"/>.
    /// </summary>
    public WorldEntity? GetEntityByOid(ushort oid)
    {
        return _entitiesByOid.TryGetValue(oid, out var entity) ? entity : null;
    }

    private ushort AllocateOid()
    {
        if (_oidPool.Count == 0)
            throw new InvalidOperationException(
                $"Region {RegionId} has exhausted all {MaxOid} OIDs.");

        return _oidPool.Pop();
    }

    private void ReleaseOid(ushort oid)
    {
        _oidPool.Push(oid);
    }

    // ── Command Channel (thread-safe) ───────────────────────────────────

    /// <summary>
    /// Enqueues an entity to be added to this region at the next tick.
    /// Safe to call from any thread.
    /// </summary>
    /// <param name="entity">The entity to add.</param>
    /// <param name="position">Where to place the entity.</param>
    /// <param name="onAdded">
    /// Optional callback invoked on the region thread after the entity has been placed
    /// and assigned an OID. Used by player-init to run final init packets.
    /// </param>
    public void EnqueueAdd(WorldEntity entity, WorldPosition position, Action<WorldEntity>? onAdded = null)
    {
        _commands.Writer.TryWrite(new RegionCommand.AddEntity(entity, position, onAdded));
    }

    /// <summary>
    /// Enqueues an entity to be removed from this region at the next tick.
    /// Safe to call from any thread.
    /// </summary>
    public void EnqueueRemove(WorldEntity entity)
    {
        _commands.Writer.TryWrite(new RegionCommand.RemoveEntity(entity));
    }

    /// <summary>
    /// Enqueues a position update for an entity. The region will process the move,
    /// update cell membership, and refresh visibility at the next tick.
    /// Safe to call from any thread.
    /// </summary>
    public void EnqueueMove(WorldEntity entity, WorldPosition newPosition)
    {
        _commands.Writer.TryWrite(new RegionCommand.MoveEntity(entity, newPosition));
    }

    /// <summary>
    /// Enqueues an inbound region transfer. The entity must already have been removed
    /// from its source region. Safe to call from any thread.
    /// </summary>
    public void EnqueueTransfer(WorldEntity entity, WorldPosition destination)
    {
        _commands.Writer.TryWrite(new RegionCommand.TransferIn(entity, destination));
    }

    // ── Tick Loop ───────────────────────────────────────────────────────

    /// <summary>
    /// Starts the dedicated tick thread. Call once after construction.
    /// </summary>
    public void Start()
    {
        if (_running)
            return;

        _running = true;
        _thread = new Thread(TickLoop)
        {
            Name = $"Region-{RegionId}",
            IsBackground = true,
        };
        _thread.Start();
    }

    /// <summary>
    /// Signals the tick thread to stop and waits for it to finish.
    /// </summary>
    public void Stop()
    {
        _running = false;
        _commands.Writer.TryComplete();
        _thread?.Join(TimeSpan.FromSeconds(5));
    }

    private void TickLoop()
    {
        _logger.LogInformation("Region {RegionId} tick thread started", RegionId);

        while (_running)
        {
            var tickStart = Environment.TickCount64;
            Tick(tickStart);

            var elapsed = (int)(Environment.TickCount64 - tickStart);
            var remaining = TickIntervalMs - elapsed;
            if (remaining > 0)
                Thread.Sleep(remaining);
        }

        _logger.LogInformation("Region {RegionId} tick thread stopped", RegionId);
    }

    /// <summary>
    /// Executes a single tick: processes pending commands, ticks entities in active cells,
    /// and updates visibility for moved entities.
    /// <para>
    /// Public for testability — tests call this directly without starting the background thread.
    /// </para>
    /// </summary>
    public void Tick(long tickMs)
    {
        ProcessCommands();
        TickEntities(tickMs);
        UpdateMovedEntitiesVisibility();
    }

    // ── Command Processing ──────────────────────────────────────────────

    private void ProcessCommands()
    {
        while (_commands.Reader.TryRead(out var command))
        {
            switch (command)
            {
                case RegionCommand.AddEntity add:
                    ExecuteAdd(add.Entity, add.Position, add.OnAdded);
                    break;

                case RegionCommand.RemoveEntity remove:
                    ExecuteRemove(remove.Entity);
                    break;

                case RegionCommand.MoveEntity move:
                    ExecuteMove(move.Entity, move.NewPosition);
                    break;

                case RegionCommand.TransferIn transfer:
                    ExecuteAdd(transfer.Entity, transfer.Destination);
                    break;
            }
        }
    }

    private void ExecuteAdd(WorldEntity entity, WorldPosition position, Action<WorldEntity>? onAdded = null)
    {
        if (!_allEntities.Add(entity))
        {
            _logger.LogWarning(
                "Entity {Name} (OID {Oid}) already in region {Region} — ignoring duplicate add",
                entity.Name, entity.ObjectId, RegionId);
            return;
        }

        var oid = AllocateOid();
        entity.AssignOid(oid);
        entity.Position = position;
        _entitiesByOid[oid] = entity;

        var (cellX, cellY) = position.CellIndex;
        var cell = GetOrCreateCell(cellX, cellY);
        if (cell != null)
        {
            cell.AddEntity(entity);
            _cellsWithEntities.Add(cell);
            if (cell.IsActive)
                _activeCells.Add(cell);

            if (entity is PlayerEntity)
                OnPlayerEnteredCell(cell);
        }

        entity.LastVisibilityCheckPosition = default;
        _movedEntities.Add(entity);

        // Invoke the post-add callback (e.g. player init pipeline) on the region thread
        // after OID assignment and cell placement are complete.
        if (onAdded is not null)
        {
            try
            {
                onAdded(entity);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "OnAdded callback failed for entity {Name} (OID {Oid}) in region {Region}",
                    entity.Name, entity.ObjectId, RegionId);
            }
        }
    }

    private void ExecuteRemove(WorldEntity entity)
    {
        var oid = entity.ObjectId;
        if (oid == 0)
            return;

        // Clear visibility — bidirectional removal
        foreach (var other in entity.Visibility.Entities)
            other.Visibility.Remove(entity);
        entity.Visibility.Clear();

        // Remove from cell
        var (cellX, cellY) = entity.Position.CellIndex;
        var cell = GetCell(cellX, cellY);
        if (cell != null)
        {
            cell.RemoveEntity(entity);
            UpdateCellTracking(cell);
        }

        // Release OID
        _entitiesByOid.TryRemove(oid, out _);
        ReleaseOid(oid);
        entity.AssignOid(0);
        _allEntities.Remove(entity);
    }

    private void ExecuteMove(WorldEntity entity, WorldPosition newPosition)
    {
        if (entity.ObjectId == 0)
            return;

        var oldPosition = entity.Position;
        var (oldCellX, oldCellY) = oldPosition.CellIndex;
        var (newCellX, newCellY) = newPosition.CellIndex;

        entity.Position = newPosition;

        // Cell transition
        if (oldCellX != newCellX || oldCellY != newCellY)
        {
            var oldCell = GetCell(oldCellX, oldCellY);
            oldCell?.RemoveEntity(entity);
            if (oldCell != null)
                UpdateCellTracking(oldCell);

            var newCell = GetOrCreateCell(newCellX, newCellY);
            if (newCell != null)
            {
                newCell.AddEntity(entity);
                _cellsWithEntities.Add(newCell);
                if (newCell.IsActive)
                    _activeCells.Add(newCell);

                if (entity is PlayerEntity)
                    OnPlayerEnteredCell(newCell);
            }
        }

        _movedEntities.Add(entity);
    }

    // ── Entity Ticking ──────────────────────────────────────────────────

    private void TickEntities(long tickMs)
    {
        // Build the set of cells to tick: 3×3 neighborhood around each active
        // (player-containing) cell. HashSet deduplicates overlapping neighborhoods.
        _tickCells.Clear();
        foreach (var activeCell in _activeCells)
        {
            var minX = Math.Max(0, activeCell.X - CellScanRadius);
            var maxX = Math.Min(MaxCellIndex - 1, activeCell.X + CellScanRadius);
            var minY = Math.Max(0, activeCell.Y - CellScanRadius);
            var maxY = Math.Min(MaxCellIndex - 1, activeCell.Y + CellScanRadius);

            for (var cx = minX; cx <= maxX; cx++)
            {
                for (var cy = minY; cy <= maxY; cy++)
                {
                    var c = _cells[cx, cy];
                    if (c != null && c.EntityCount > 0)
                        _tickCells.Add(c);
                }
            }
        }

        foreach (var cell in _tickCells)
        {
            var entities = cell.Entities;
            for (var i = 0; i < entities.Count; i++)
                entities[i].Update(tickMs);
        }
    }

    // ── Visibility ──────────────────────────────────────────────────────

    private void UpdateMovedEntitiesVisibility()
    {
        foreach (var entity in _movedEntities)
            UpdateVisibility(entity);

        _movedEntities.Clear();
    }

    /// <summary>
    /// Re-scans the 3×3 cell neighborhood around <paramref name="entity"/> and updates
    /// its <see cref="VisibilitySet"/> (bidirectional add/remove). Only triggers if the
    /// entity has moved more than <see cref="RangeUpdateThreshold"/> since the last scan.
    /// </summary>
    internal void UpdateVisibility(WorldEntity entity, bool force = false)
    {
        if (entity.ObjectId == 0)
            return;

        if (!force)
        {
            var distSq = entity.Position.DistanceSquared2D(entity.LastVisibilityCheckPosition);
            if (distSq < RangeUpdateThresholdSquared)
                return;
        }

        entity.LastVisibilityCheckPosition = entity.Position;

        // Phase 1: Scan neighborhood, add new entities to visibility
        var (cellX, cellY) = entity.Position.CellIndex;
        var minX = Math.Max(0, cellX - CellScanRadius);
        var maxX = Math.Min(MaxCellIndex - 1, cellX + CellScanRadius);
        var minY = Math.Max(0, cellY - CellScanRadius);
        var maxY = Math.Min(MaxCellIndex - 1, cellY + CellScanRadius);

        for (var cx = minX; cx <= maxX; cx++)
        {
            for (var cy = minY; cy <= maxY; cy++)
            {
                var cell = _cells[cx, cy];
                if (cell == null)
                    continue;

                var cellEntities = cell.Entities;
                for (var i = 0; i < cellEntities.Count; i++)
                {
                    var other = cellEntities[i];
                    if (other == entity || other.ObjectId == 0)
                        continue;

                    var distSq = entity.Position.DistanceSquared2D(other.Position);
                    if (distSq <= MaxVisibilitySquared && !entity.Visibility.Contains(other))
                    {
                        entity.Visibility.Add(other);
                        other.Visibility.Add(entity);
                    }
                }
            }
        }

        // Phase 2: Remove entities that have left range
        // Iterate a snapshot to allow mutation during iteration
        foreach (var other in entity.Visibility.Entities.ToArray())
        {
            if (other.ObjectId == 0 ||
                entity.Position.DistanceSquared2D(other.Position) > MaxVisibilitySquared)
            {
                entity.Visibility.Remove(other);
                other.Visibility.Remove(entity);
            }
        }
    }

    // ── Spatial Queries ─────────────────────────────────────────────────

    /// <summary>
    /// Finds all entities within <paramref name="rangeUnits"/> of <paramref name="center"/>.
    /// Results are appended to <paramref name="results"/> (caller should clear if needed).
    /// </summary>
    public void GetEntitiesInRange(WorldPosition center, int rangeUnits, List<WorldEntity> results)
    {
        long rangeSq = (long)rangeUnits * rangeUnits;
        var cellRadius = (rangeUnits / CellSize) + 1;
        var (centerCellX, centerCellY) = center.CellIndex;

        var minX = Math.Max(0, centerCellX - cellRadius);
        var maxX = Math.Min(MaxCellIndex - 1, centerCellX + cellRadius);
        var minY = Math.Max(0, centerCellY - cellRadius);
        var maxY = Math.Min(MaxCellIndex - 1, centerCellY + cellRadius);

        for (var cx = minX; cx <= maxX; cx++)
        {
            for (var cy = minY; cy <= maxY; cy++)
            {
                var cell = _cells[cx, cy];
                if (cell == null)
                    continue;

                var entities = cell.Entities;
                for (var i = 0; i < entities.Count; i++)
                {
                    var e = entities[i];
                    if (center.DistanceSquared2D(e.Position) <= rangeSq)
                        results.Add(e);
                }
            }
        }
    }

    /// <summary>
    /// Finds all players within <paramref name="rangeUnits"/> of <paramref name="center"/>.
    /// Results are appended to <paramref name="results"/>.
    /// </summary>
    public void GetPlayersInRange(WorldPosition center, int rangeUnits, List<PlayerEntity> results)
    {
        long rangeSq = (long)rangeUnits * rangeUnits;
        var cellRadius = (rangeUnits / CellSize) + 1;
        var (centerCellX, centerCellY) = center.CellIndex;

        var minX = Math.Max(0, centerCellX - cellRadius);
        var maxX = Math.Min(MaxCellIndex - 1, centerCellX + cellRadius);
        var minY = Math.Max(0, centerCellY - cellRadius);
        var maxY = Math.Min(MaxCellIndex - 1, centerCellY + cellRadius);

        for (var cx = minX; cx <= maxX; cx++)
        {
            for (var cy = minY; cy <= maxY; cy++)
            {
                var cell = _cells[cx, cy];
                if (cell == null)
                    continue;

                var players = cell.Players;
                for (var i = 0; i < players.Count; i++)
                {
                    var p = players[i];
                    if (center.DistanceSquared2D(p.Position) <= rangeSq)
                        results.Add(p);
                }
            }
        }
    }

    // ── Cell Tracking ───────────────────────────────────────────────────

    private void UpdateCellTracking(Cell cell)
    {
        if (cell.EntityCount == 0)
        {
            _cellsWithEntities.Remove(cell);
            _activeCells.Remove(cell);
        }
        else if (cell.IsActive)
        {
            _activeCells.Add(cell);
        }
        else
        {
            _activeCells.Remove(cell);
        }
    }

    /// <summary>
    /// Called when a player enters a cell. Marks the cell's 3×3 neighborhood for
    /// NPC spawn loading if they haven't been loaded yet.
    /// </summary>
    private void OnPlayerEnteredCell(Cell cell)
    {
        _activeCells.Add(cell);

        // Load 3×3 neighborhood around the player's cell
        var minX = Math.Max(0, cell.X - CellScanRadius);
        var maxX = Math.Min(MaxCellIndex - 1, cell.X + CellScanRadius);
        var minY = Math.Max(0, cell.Y - CellScanRadius);
        var maxY = Math.Min(MaxCellIndex - 1, cell.Y + CellScanRadius);

        for (var cx = minX; cx <= maxX; cx++)
        {
            for (var cy = minY; cy <= maxY; cy++)
            {
                var neighbor = GetOrCreateCell(cx, cy);
                if (neighbor is { IsLoaded: false })
                    LoadCell(neighbor);
            }
        }
    }

    /// <summary>
    /// Marks a cell as loaded. Override point for spawn loading — the <see cref="RegionManager"/>
    /// or a spawn service can hook into this to populate the cell with NPCs.
    /// </summary>
    private void LoadCell(Cell cell)
    {
        // Mark as loaded to prevent re-entry. Spawn loading will be wired
        // when the GameDataStore spawn index is built (creature/game object spawns
        // keyed by regionId + cellX + cellY).
        cell.IsLoaded = true;

        _logger.LogDebug(
            "Cell ({CellX}, {CellY}) in region {RegionId} marked as loaded",
            cell.X, cell.Y, RegionId);
    }

    // ── Disposal ────────────────────────────────────────────────────────

    public void Dispose()
    {
        Stop();
    }
}
