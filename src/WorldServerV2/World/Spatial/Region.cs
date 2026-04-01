using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using WorldServerV2.Data;
using WorldServerV2.Data.Domain;
using WorldServerV2.Network;
using WorldServerV2.Network.Dtos;
using WorldServerV2.Services;
using WorldServerV2.World.Entities;
using WorldServerV2.World.Spawning;
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
///   <item>Broadcast dirty entity states (<c>F_OBJECT_STATE</c>) to players in range</item>
///   <item>Update visibility for entities that moved beyond the threshold</item>
/// </list>
/// </para>
/// </summary>
public sealed class Region : IDisposable
{
    private readonly Cell?[,] _cells = new Cell?[MaxCellIndex, MaxCellIndex];
    private readonly ConcurrentDictionary<ushort, WorldEntity> _entitiesByOid = new();
    private readonly HashSet<WorldEntity> _allEntities = new();
    private readonly ConcurrentBag<ushort> _oidPool = new();
    private readonly HashSet<Cell> _activeCells = new();
    private readonly HashSet<Cell> _cellsWithEntities = new();
    private readonly HashSet<Cell> _tickCells = new();
    private readonly Channel<RegionCommand> _commands;
    private readonly List<WorldEntity> _movedEntities = new();
    private readonly ILogger _logger;
    private readonly IEntityFactory _entityFactory;
    private readonly IGameDataStore _gameData;
    private readonly ISessionResolver _sessionResolver;
    private readonly RespawnScheduler _respawnScheduler = new();

    private Thread? _thread;
    private volatile bool _running;
    private int _startGuard; // 0 = not started, 1 = started; CAS-protected

    /// <summary>Creates a new region with the given identifier.</summary>
    public Region(ushort regionId, ILogger logger, IEntityFactory entityFactory, IGameDataStore gameData, ISessionResolver sessionResolver)
    {
        RegionId = regionId;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _entityFactory = entityFactory ?? throw new ArgumentNullException(nameof(entityFactory));
        _gameData = gameData ?? throw new ArgumentNullException(nameof(gameData));
        _sessionResolver = sessionResolver ?? throw new ArgumentNullException(nameof(sessionResolver));

        _commands = Channel.CreateUnbounded<RegionCommand>(
            new UnboundedChannelOptions { SingleReader = true });

        // Pre-populate OID pool: usable range is 1..65535 (0 is reserved).
        // ConcurrentBag does not guarantee ordering — callers must not depend on
        // OIDs being assigned in any particular sequence.
        for (int i = MaxOid; i >= 1; i--)
            _oidPool.Add((ushort)i);
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
        if (!_oidPool.TryTake(out var oid))
            throw new InvalidOperationException(
                $"Region {RegionId} has exhausted all {MaxOid} OIDs.");

        return oid;
    }

    private void ReleaseOid(ushort oid)
    {
        _oidPool.Add(oid);
    }

    /// <summary>
    /// Reserves an OID from the pool, returning an <see cref="OidReservation"/> ticket.
    /// Thread-safe — may be called from any thread (e.g. handler threads during player
    /// initialization).
    /// <para>
    /// The returned reservation is <see cref="IDisposable"/>. If the caller does not
    /// pass it to <see cref="AddAsync"/>,
    /// disposing the reservation returns the OID to the pool automatically. Once
    /// consumed by <c>AddAsync</c>, disposal is a no-op.
    /// </para>
    /// </summary>
    /// <returns>A disposable reservation ticket holding the reserved OID.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the pool is exhausted.</exception>
    public OidReservation ReserveOid()
    {
        var oid = AllocateOid();
        return new OidReservation(oid, this);
    }

    /// <summary>
    /// Returns a reserved OID to the pool. Called internally by
    /// <see cref="OidReservation.Dispose"/> — not intended for direct use.
    /// Thread-safe.
    /// </summary>
    internal void ReturnReservedOid(ushort oid)
    {
        ReleaseOid(oid);
    }

    // ── Command Channel (thread-safe) ───────────────────────────────────

    /// <summary>
    /// Enqueues an entity to be added to this region at the next tick.
    /// Safe to call from any thread.
    /// <para>
    /// This overload is for entities that do <b>not</b> have a pre-reserved OID
    /// (e.g. NPCs, creatures). The region thread allocates an OID during processing.
    /// For entities with a reserved OID, use
    /// <see cref="AddAsync"/>.
    /// </para>
    /// </summary>
    /// <param name="entity">The entity to add.</param>
    /// <param name="position">Where to place the entity.</param>
    public void EnqueueAdd(WorldEntity entity, WorldPosition position)
    {
        _commands.Writer.TryWrite(new RegionCommand.AddEntity(entity, position));
    }

    /// <summary>
    /// Adds a pre-initialized entity (with a reserved OID) to this region.
    /// Consumes the <paramref name="reservation"/>, making its disposal a no-op.
    /// The returned <see cref="Task"/> completes once the region thread has placed the
    /// entity in its cell — callers can <c>await</c> this to guarantee placement before
    /// sending post-init packets.
    /// <para>
    /// Safe to call from any thread. The entity must already have
    /// <see cref="WorldEntity.ObjectId"/> set to <see cref="OidReservation.Oid"/>.
    /// </para>
    /// </summary>
    /// <param name="entity">The entity to add (OID already assigned).</param>
    /// <param name="position">Where to place the entity.</param>
    /// <param name="reservation">
    /// The OID reservation obtained from <see cref="ReserveOid"/>. Must belong to
    /// this region and be in the active state.
    /// </param>
    /// <returns>A task that completes when the entity has been placed in the region.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reservation"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// The reservation belongs to a different region, has already been consumed, or has
    /// been disposed.
    /// </exception>
    public Task AddAsync(WorldEntity entity, WorldPosition position, OidReservation reservation)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(reservation);

        if (reservation.Owner != this)
            throw new InvalidOperationException(
                $"OID reservation (OID {reservation.Oid}) belongs to region {reservation.Owner.RegionId}, " +
                $"not region {RegionId}.");

        if (entity.ObjectId != reservation.Oid)
            throw new InvalidOperationException(
                $"Entity {entity.Name} has OID {entity.ObjectId}, but reservation holds OID {reservation.Oid}.");

        if (!reservation.TryConsume())
            throw new InvalidOperationException(
                $"OID reservation (OID {reservation.Oid}) has already been consumed or disposed.");

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var enqueued = _commands.Writer.TryWrite(new RegionCommand.AddEntity(entity, position, tcs));

        if (!enqueued)
        {
            tcs.SetException(new InvalidOperationException(
                $"Failed to enqueue AddEntity command for region {RegionId}; the region may have been stopped."));
        }
        return tcs.Task;
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

    /// <summary>
    /// Enqueues an activation command for the given entity. When processed on the
    /// region thread, the entity’s <see cref="WorldEntity.IsActive"/> is set to <c>true</c>
    /// and a forced visibility rescan is triggered so the entity discovers all nearby
    /// entities. Safe to call from any thread.
    /// </summary>
    public void EnqueueActivate(WorldEntity entity)
    {
        _commands.Writer.TryWrite(new RegionCommand.ActivateEntity(entity));
    }

    // ── Tick Loop ───────────────────────────────────────────────────────

    /// <summary>
    /// Starts the dedicated tick thread. Safe to call from multiple threads — only the
    /// first caller starts the thread; subsequent calls are no-ops.
    /// </summary>
    public void Start()
    {
        // CAS from 0 → 1: only one thread wins; all others return immediately.
        if (Interlocked.CompareExchange(ref _startGuard, 1, 0) != 0)
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
    /// broadcasts dirty entity states, and updates visibility for moved entities.
    /// <para>
    /// Public for testability — tests call this directly without starting the background thread.
    /// </para>
    /// </summary>
    public void Tick(long tickMs)
    {
        _respawnScheduler.DrainDue(tickMs, entry =>
        {
            var entity = _entityFactory.CreateCreature(entry.Descriptor);
            EnqueueAdd(entity, entry.Descriptor.Position);
        });
        ProcessCommands();
        TickEntities(tickMs);
        BroadcastEntityStates(tickMs);
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
                    ExecuteAdd(add.Entity, add.Position, add.Placed);
                    break;

                case RegionCommand.RemoveEntity remove:
                    ExecuteRemove(remove.Entity);
                    break;

                case RegionCommand.MoveEntity move:
                    ExecuteMove(move.Entity, move.NewPosition);
                    break;

                case RegionCommand.TransferIn transfer:
                    ExecuteAdd(transfer.Entity, transfer.Destination, placed: null);
                    break;

                case RegionCommand.ActivateEntity activate:
                    ExecuteActivate(activate.Entity);
                    break;
            }
        }
    }

    private void ExecuteAdd(WorldEntity entity, WorldPosition position, TaskCompletionSource<bool>? placed = null)
    {
        if (!_allEntities.Add(entity))
        {
            _logger.LogWarning(
                "Entity {Name} (OID {Oid}) already in region {Region} — ignoring duplicate add",
                entity.Name, entity.ObjectId, RegionId);
            placed?.TrySetResult(false);
            return;
        }

        // If the entity already has a pre-assigned OID (reserved via ReserveOid()),
        // use it directly. Otherwise allocate a fresh one on the region thread.
        var oid = entity.ObjectId != 0 ? entity.ObjectId : AllocateOid();
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

        placed?.TrySetResult(true);
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

    private void ExecuteActivate(WorldEntity entity)
    {
        if (entity.ObjectId == 0 || !_allEntities.Contains(entity))
            return;

        entity.IsActive = true;

        // Send create-packets for every entity already in the player's visibility set.
        // These were discovered spatially during the initial add but suppressed because
        // the player was inactive at that time.
        //
        // The reverse direction (telling other active players about this player) is also
        // handled here. The forced visibility rescan below only fires NotifyEntityVisible
        // for *new* visibility entries — entities already in the set are skipped because
        // UpdateVisibility checks !entity.Visibility.Contains(other). Without the reverse
        // call, an already-active player B would never learn that player A appeared.
        if (entity is PlayerEntity player)
        {
            foreach (var other in player.Visibility.Entities)
            {
                NotifyEntityVisible(player, other);

                if (other is PlayerEntity otherPlayer)
                    NotifyEntityVisible(otherPlayer, player);
            }
        }

        // Force a full visibility rescan so any entities that entered range between
        // the initial add and activation are also discovered.
        entity.LastVisibilityCheckPosition = default;
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

    // ── State Broadcasting ──────────────────────────────────────────────

    /// <summary>
    /// Iterates all entities in the tick-cell set and broadcasts <c>F_OBJECT_STATE</c>
    /// (opcode 0x09) to players in each entity's <see cref="VisibilitySet"/>.
    /// <para>
    /// Each non-player entity is asked via <see cref="WorldEntity.TryRefresh"/> whether it
    /// needs a broadcast (dirty flag or keepalive timer expiry). The entity owns its own
    /// refresh logic; the region is responsible only for dispatch.
    /// </para>
    /// <para>
    /// Reuses the <c>_tickCells</c> set already populated by <c>TickEntities</c> to avoid
    /// re-scanning active cell neighborhoods.
    /// </para>
    /// </summary>
    private void BroadcastEntityStates(long tickMs)
    {
        foreach (var cell in _tickCells)
        {
            var entities = cell.Entities;
            for (var i = 0; i < entities.Count; i++)
            {
                var entity = entities[i];

                if (entity is PlayerEntity)
                    continue;

                if (!entity.TryRefresh(tickMs))
                    continue;

                if (entity.Visibility.PlayerCount == 0)
                    continue;

                var zone = _gameData.Zones.Infos.GetValueOrDefault(entity.Position.ZoneId);
                if (zone is null)
                    continue;

                var response = BuildStationaryState(entity, zone);

                foreach (var player in entity.Visibility.Players)
                {
                    if (!player.IsActive)
                        continue;

                    var session = _sessionResolver.GetSession(player);
                    session?.SendObjectState(response);
                }
            }
        }
    }

    /// <summary>
    /// Builds a <see cref="StationaryObjectStateResponse"/> for the given entity.
    /// Dispatches between <see cref="UnitEntity"/> and <see cref="GameObjectEntity"/>.
    /// </summary>
    private static StationaryObjectStateResponse BuildStationaryState(
        WorldEntity entity, Data.Entities.ZoneInfo zone)
    {
        return entity switch
        {
            UnitEntity unit => StationaryObjectStateResponse.From(unit, zone),
            GameObjectEntity go => StationaryObjectStateResponse.From(go, zone),
            _ => throw new InvalidOperationException(
                $"Unexpected entity type {entity.GetType().Name} in BroadcastEntityStates"),
        };
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

                        // Notify players when a new entity enters their visibility
                        if (entity is PlayerEntity playerA)
                            NotifyEntityVisible(playerA, other);
                        if (other is PlayerEntity playerB)
                            NotifyEntityVisible(playerB, entity);
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

    // ── Visibility Notifications ─────────────────────────────────────

    /// <summary>
    /// Notifies <paramref name="player"/> that <paramref name="target"/> has entered
    /// its visibility range. Builds the appropriate entity-create message and delivers
    /// it via the player's session. Called exclusively from the region tick thread.
    /// </summary>
    private void NotifyEntityVisible(PlayerEntity player, WorldEntity target)
    {
        // Don't send entity-create packets to a player who hasn't finished loading.
        // The player will receive a forced visibility rescan when activated.
        if (!player.IsActive)
            return;

        var session = _sessionResolver.GetSession(player);
        if (session is null)
            return;

        var zone = _gameData.Zones.Infos.GetValueOrDefault(target.Position.ZoneId);
        if (zone is null)
            return;

        switch (target)
        {
            case CreatureEntity creature:
            {
                var proto = _gameData.Creatures.Protos.GetValueOrDefault(creature.Entry);
                if (proto is null)
                    return;

                session.SendCreateMonster(CreateMonsterResponse.From(creature, proto, zone));
                break;
            }

            case GameObjectEntity gameObject:
            {
                // GameObjectProto lookup is not yet wired into IGameDataStore (factory TODO);
                // pass null to fall back to entity.Name.
                if (!_gameData.Spawns.GameObjects.TryGetValue(
                        new CellKey(RegionId, target.Position.CellIndex.CellX, target.Position.CellIndex.CellY),
                        out var descriptors))
                    return;

                // Find the descriptor that matches this entity's entry (best-effort)
                GameObjectSpawnDescriptor? desc = null;
                foreach (var d in descriptors)
                {
                    if (d.Entry == gameObject.Entry)
                    {
                        desc = d;
                        break;
                    }
                }

                if (desc is null)
                    return;

                session.SendCreateStatic(CreateStaticResponse.From(gameObject, desc.Value, zone));
                break;
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
    /// Marks a cell as loaded and spawns all creatures and game objects whose spawn
    /// descriptor falls within this cell.
    /// </summary>
    private void LoadCell(Cell cell)
    {
        cell.IsLoaded = true;

        var cellKey = new CellKey(RegionId, cell.X, cell.Y);

        // Spawn creatures
        if (_gameData.Spawns.Creatures.TryGetValue(cellKey, out var creatures))
        {
            foreach (var descriptor in creatures)
            {
                var entity = _entityFactory.CreateCreature(descriptor);
                EnqueueAdd(entity, descriptor.Position);

                if (descriptor.RespawnDelayMs > 0)
                {
                    // Pre-register for respawn: when the entity dies, the health component
                    // will call _respawnScheduler.Schedule. For now we only load, not pre-queue.
                }
            }
        }

        // Spawn game objects
        if (_gameData.Spawns.GameObjects.TryGetValue(cellKey, out var gameObjects))
        {
            foreach (var descriptor in gameObjects)
            {
                var entity = _entityFactory.CreateGameObject(descriptor);
                EnqueueAdd(entity, descriptor.Position);
            }
        }

        _logger.LogDebug(
            "Cell ({CellX}, {CellY}) in region {RegionId} loaded",
            cell.X, cell.Y, RegionId);
    }

    // ── Disposal ────────────────────────────────────────────────────────

    public void Dispose()
    {
        Stop();
    }
}
