using Core.GameWorld.Entities;
using Core.GameWorld.Spatial;
using Core.Session;
using Microsoft.Extensions.Logging;
using WorldServerV2.Network;
using WorldServerV2.Network.Dtos;

namespace WorldServerV2.Services;

/// <summary>
/// Stateless facade over the world topology system. Provides the API that packet handlers
/// and other services use to place entities into the world, move them, and remove them.
/// <para>
/// Registered as a <b>singleton</b>. Packet handlers receive it via constructor injection
/// or <c>[FromServices]</c> on method parameters.
/// </para>
/// <para>
/// <b>Usage pattern</b> (thin handler, service orchestration):
/// <code>
/// // In a movement packet handler:
/// var player = _playerService.GetPlayer(session);
/// var newPos = WorldPosition.FromZoneLocal(...);
/// _worldService.MoveEntity(player, newPos);
/// </code>
/// All mutations are enqueued as commands and processed on the region's tick thread —
/// callers never block on entity state changes.
/// </para>
/// </summary>
public sealed class WorldService
{
    private readonly RegionManager _regionManager;
    private readonly ISessionResolver<PlayerEntity> _sessionResolver;
    private readonly ILogger<WorldService> _logger;

    public WorldService(
        RegionManager regionManager,
        ISessionResolver<PlayerEntity> sessionResolver,
        ILogger<WorldService> logger)
    {
        _regionManager = regionManager ?? throw new ArgumentNullException(nameof(regionManager));
        _sessionResolver = sessionResolver ?? throw new ArgumentNullException(nameof(sessionResolver));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>The underlying region registry. Exposed for advanced queries only.</summary>
    public RegionManager Regions => _regionManager;

    /// <summary>
    /// Places an entity into the world at the specified position. The entity will be
    /// assigned an OID by the target region and appear after the next tick.
    /// <para>
    /// Typical callers: world-enter handler (player), spawn system (NPCs).
    /// </para>
    /// </summary>
    /// <param name="entity">The entity to add.</param>
    /// <param name="position">Region-wide position where the entity should appear.</param>
    public void EnterWorld(WorldEntity entity, WorldPosition position)
    {
        ArgumentNullException.ThrowIfNull(entity);

        var region = _regionManager.GetOrCreate(position.RegionId);
        region.EnqueueAdd(entity, position);

        _logger.LogDebug(
            "Enqueued {EntityType} {Name} to enter region {RegionId} at ({X}, {Y})",
            entity.Type, entity.Name, position.RegionId, position.X, position.Y);
    }

    /// <summary>
    /// Removes an entity from the world. The entity's OID is released and it is removed
    /// from all visibility sets after the next tick.
    /// <para>
    /// Typical callers: logout handler (player), despawn system (NPCs).
    /// </para>
    /// </summary>
    /// <param name="entity">The entity to remove.</param>
    public void LeaveWorld(WorldEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        var region = _regionManager.Get(entity.Position.RegionId);
        if (region == null)
        {
            _logger.LogWarning(
                "Cannot remove {Name} — region {RegionId} not found",
                entity.Name, entity.Position.RegionId);
            return;
        }

        region.EnqueueRemove(entity);

        _logger.LogDebug(
            "Enqueued {EntityType} {Name} to leave region {RegionId}",
            entity.Type, entity.Name, entity.Position.RegionId);
    }

    /// <summary>
    /// Moves an entity to a new position. Handles both intra-region moves and
    /// cross-region transfers transparently.
    /// <para>
    /// Typical callers: movement handler, teleport system, knockback.
    /// </para>
    /// </summary>
    /// <param name="entity">The entity to move.</param>
    /// <param name="newPosition">The destination position (may be in a different region).</param>
    public void MoveEntity(WorldEntity entity, WorldPosition newPosition)
    {
        ArgumentNullException.ThrowIfNull(entity);

        var currentRegionId = entity.Position.RegionId;
        var targetRegionId = newPosition.RegionId;

        if (currentRegionId == targetRegionId)
        {
            // Same region — simple move command
            var region = _regionManager.Get(currentRegionId);
            region?.EnqueueMove(entity, newPosition);
        }
        else
        {
            // Cross-region transfer: remove from source, add to destination
            var sourceRegion = _regionManager.Get(currentRegionId);
            sourceRegion?.EnqueueRemove(entity);

            var targetRegion = _regionManager.GetOrCreate(targetRegionId);
            targetRegion.EnqueueTransfer(entity, newPosition);

            _logger.LogDebug(
                "Cross-region transfer for {Name}: region {Source} → {Target}",
                entity.Name, currentRegionId, targetRegionId);
        }
    }

    /// <summary>
    /// Gets the region that an entity currently resides in, or <c>null</c> if the entity's
    /// region hasn't been created.
    /// </summary>
    public Region? GetEntityRegion(WorldEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        return _regionManager.Get(entity.Position.RegionId);
    }

    /// <summary>
    /// Updates a player's position and immediately broadcasts the relay packet to nearby
    /// players on the <b>calling thread</b> (handler I/O thread). The position move is
    /// enqueued as a region command for the tick thread; the relay is sent directly via
    /// a lock-guarded snapshot of the visibility set — eliminating the tick-interval
    /// latency that a command-based relay would incur.
    /// <para>
    /// Typical caller: the <c>F_PLAYER_STATE2</c> movement handler.
    /// </para>
    /// </summary>
    /// <param name="player">The player whose position is being updated.</param>
    /// <param name="newPosition">The player's new region-wide position.</param>
    /// <param name="relay">The pre-built relay response to broadcast to nearby players.</param>
    public void UpdatePlayerState(PlayerEntity player, WorldPosition newPosition, PlayerStateRelayResponse relay)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(relay);

        // Broadcast immediately on the caller (handler) thread via a pooled snapshot.
        // The lock inside SnapshotPlayers covers only the array copy — all send work
        // happens outside the lock and off the region tick thread.
        BroadcastPlayerStateRelay(player, relay);

        // Enqueue the authoritative position update for the region tick thread.
        MoveEntity(player, newPosition);
    }

    /// <summary>
    /// Broadcasts the relay packet to nearby players without updating position.
    /// Used for heartbeat packets and click-to-move where the server doesn't need
    /// to track a position change but still needs to relay the state to neighbors.
    /// </summary>
    public void RelayPlayerState(PlayerEntity player, PlayerStateRelayResponse relay)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(relay);

        BroadcastPlayerStateRelay(player, relay);
    }

    /// <summary>
    /// Sends a <see cref="PlayerStateRelayResponse"/> to every active player in the
    /// source player's visibility set. Executes entirely on the calling thread.
    /// </summary>
    private void BroadcastPlayerStateRelay(PlayerEntity source, PlayerStateRelayResponse relay)
    {
        using var snapshot = source.Visibility.SnapshotPlayers();
        if (snapshot.Count == 0)
            return;

        foreach (var player in snapshot.Span)
        {
            if (!player.IsActive)
                continue;

            var session = _sessionResolver.GetSession(player);
            session?.SendPlayerStateRelay(relay);
        }
    }
}
