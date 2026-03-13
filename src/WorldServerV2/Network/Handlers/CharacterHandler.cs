using Core.Infrastructure.Network;
using Microsoft.Extensions.Logging;
using WorldServerV2.Network.Dtos;
using WorldServerV2.Services;
using WorldServerV2.World.Entities;
using IPacketHandler = Core.Infrastructure.Network.IPacketHandler;

namespace WorldServerV2.Network.Handlers;

/// <summary>
/// Handles character-related packets: character selection, world entry.
/// This is the modernized equivalent of the legacy CharacterHandlers class.
/// </summary>
public class CharacterHandler : IPacketHandler
{
    private readonly ILogger<CharacterHandler> _logger;

    /// <summary>Default max health for newly created player entities.
    /// The real value will be computed by the stats system (System 4) once implemented.</summary>
    private const uint DefaultMaxHealth = 1000;

    public CharacterHandler(
        ILogger<CharacterHandler> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Handles the F_DUMP_ARENAS_LARGE packet (opcode 0x35).
    /// The client sends this when a character is selected on the character screen.
    /// We look up the selected character, create the <see cref="PlayerEntity"/>, bind it
    /// to the session via <see cref="PlayerService"/>, and respond with F_WORLD_ENTER.
    /// </summary>
    [Rpc((int)Opcodes.F_DUMP_ARENAS_LARGE, (int)Opcodes.F_WORLD_ENTER)]
    public RpcResult<WorldEnterResponse> F_DUMP_ARENAS_LARGE(
        DumpArenasLargeRequest request,
        IConnectionContext context,
        [FromServices] PlayerService playerService)
    {
        if (context.Account == null)
        {
            context.Disconnect("No account in F_DUMP_ARENAS_LARGE");
            return RpcResult<WorldEnterResponse>.NoResponse;
        }

        var character = context.Session.GetCharacterBySlot(request.CharacterSlot);
        if (character == null)
        {
            _logger.LogError("Character not found on slot {Slot} for account {AccountId}",
                request.CharacterSlot, context.Account.Id);
            context.Disconnect("Character not found in F_DUMP_ARENAS_LARGE");
            return RpcResult<WorldEnterResponse>.NoResponse;
        }

        // If this session already has a player (e.g. switching characters), the existing
        // binding will be displaced by PlayerService.Bind.
        if (playerService.GetPlayer(context.Session) is { } existing)
        {
            _logger.LogDebug("Session {SessionId} already has player {CharName}, re-binding",
                context.Session.Id, existing.Name);
        }

        var player = new PlayerEntity(context.Session.Id, character, DefaultMaxHealth);
        playerService.Bind(context.Session, player);

        _logger.LogInformation(
            "Player {CharName} ({CharId}) created on Session {SessionId}",
            player.Name, player.CharacterId, context.Session.Id);

        return new WorldEnterResponse();
    }
}
