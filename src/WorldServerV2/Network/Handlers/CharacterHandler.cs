using Core.Infrastructure.Network;
using Microsoft.Extensions.Logging;
using WorldServerV2.Network.Dtos;
using WorldServerV2.Services;

namespace WorldServerV2.Network.Handlers;

/// <summary>
/// Handles in-game character packets that occur after the character screen phase:
/// world loading finalization, movement, and other gameplay-phase requests.
/// </summary>
public class CharacterHandler : IPacketHandler
{
    private readonly ILogger<CharacterHandler> _logger;

    public CharacterHandler(ILogger<CharacterHandler> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Handles <c>F_REQUEST_WORLD_LARGE</c> (0x40).
    /// Sent by the client after it processes <c>F_PLAYER_INIT_COMPLETE</c>.
    /// We respond with <c>F_SET_TIME</c> and <c>S_WORLD_SENT</c> to signal the client
    /// to begin rendering the game world.
    /// </summary>
    [Rpc((int)Opcodes.F_REQUEST_WORLD_LARGE)]
    public void F_REQUEST_WORLD_LARGE(RequestWorldLargeRequest request, IConnectionContext context,
        [FromServices] PlayerService playerService)
    {
        var player = playerService.GetPlayer(context.Session);
        if (player == null)
        {
            _logger.LogWarning("No player bound in F_REQUEST_WORLD_LARGE for session {SessionId}",
                context.Session.Id);
            return;
        }

        // F_SET_TIME — tells the client the current in-game time
        var gameTime = (ushort)(DateTime.UtcNow.TimeOfDay.TotalSeconds / 65.5d);
        context.Session.SendSetTime(new SetTimeResponse
        {
            GameTime = gameTime,
        });

        // S_WORLD_SENT — final signal: client can render the world
        context.Session.SendWorldSent(new WorldSentResponse());

        _logger.LogInformation(
            "World sent for player {Name} (OID {Oid}) — session {SessionId}",
            player.Name, player.ObjectId, context.Session.Id);
    }
}
