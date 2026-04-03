using Core.Infrastructure.Network;
using Microsoft.Extensions.Logging;
using WorldServerV2.Data;
using WorldServerV2.Network.Dtos;
using WorldServerV2.Services;
using WorldServerV2.World.Entities;

namespace WorldServerV2.Network.Handlers;

/// <summary>
/// Handles inbound movement packets. Each <c>F_PLAYER_STATE2</c> packet is a variable-length
/// bitstream carrying movement state and (optionally) position data. The handler classifies
/// the packet variant, decodes the relevant fields, updates the player entity's world
/// position via the region command pipeline, and relays the state to nearby players.
/// </summary>
public class MovementHandler : IPacketHandler
{
    private readonly ILogger<MovementHandler> _logger;

    public MovementHandler(ILogger<MovementHandler> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Handles <c>F_PLAYER_STATE2</c> (0x62).
    /// <para>
    /// Classifies the packet as heartbeat, standard movement, or combat movement,
    /// decodes the bitstream accordingly, and dispatches the appropriate world update.
    /// Heartbeat packets are relayed but do not update the player's position.
    /// </para>
    /// </summary>
    [Rpc((int)Opcodes.F_PLAYER_STATE2)]
    public void F_PLAYER_STATE2(
        PlayerStateRequest request,
        IConnectionContext context,
        [FromServices] PlayerService playerService,
        [FromServices] WorldService worldService,
        [FromServices] IGameDataStore gameData)
    {
        var player = playerService.GetPlayer(context.Session);
        if (player is null)
        {
            _logger.LogWarning(
                "No player bound in F_PLAYER_STATE2 for session {SessionId}",
                context.Session.Id);
            return;
        }

        if (!player.IsActive)
            return;

        var relay = PlayerStateRelayResponse.FromRequest(request);

        if (request.Type == PlayerStateType.Heartbeat)
        {
            // State-only update — relay to nearby players, no position change.
            worldService.RelayPlayerState(player, relay);
            return;
        }

        // Standard or combat movement — decode full position.
        var position = request.DecodePosition();
        if (position is null)
        {
            // Click-to-move destination — relay only, server doesn't track destination.
            worldService.RelayPlayerState(player, relay);
            return;
        }

        var pos = position.Value;

        var zone = gameData.Zones.Infos.GetValueOrDefault(pos.ZoneId);
        if (zone is null)
        {
            _logger.LogWarning(
                "Unknown zone {ZoneId} in F_PLAYER_STATE2 from {Name}",
                pos.ZoneId, player.Name);
            return;
        }

        var newPosition = WorldPosition.FromZoneLocal(
            zone.Region, pos.ZoneId,
            zone.OffX, zone.OffY,
            pos.X, pos.Y,
            pos.Z, (ushort)(pos.Heading * 4095 / (2 * MathF.PI)));

        worldService.UpdatePlayerState(player, newPosition, relay);
    }
}
