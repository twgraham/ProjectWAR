using Core.Infrastructure.Network;
using Microsoft.Extensions.Logging;
using WorldServerV2.Data;
using WorldServerV2.Network.Dtos;
using WorldServerV2.Services;
using WorldServerV2.World.Entities;

namespace WorldServerV2.Network.Handlers;

public class CharacterScreenHandler : IPacketHandler
{
    /// <summary>Default max health for newly created player entities.
    /// The real value will be computed by the stats system (System 4) once implemented.</summary>
    private const uint DefaultMaxHealth = 1000;
    
    private readonly ILogger<CharacterScreenHandler> _logger;
    
    public CharacterScreenHandler(ILogger<CharacterScreenHandler> logger)
    {
        _logger = logger;
    }
    
    [Rpc((int)Opcodes.F_REQUEST_CHAR_TEMPLATES, (int)Opcodes.F_REQUEST_CHAR_TEMPLATES)]
    public RpcResult<CharacterTemplatesResponse> F_REQUEST_CHAR_TEMPLATES(CharacterTemplatesRequest request, IConnectionContext context)
    {
        return new CharacterTemplatesResponse();
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
    
    [Rpc((int)Opcodes.F_OPEN_GAME, (int)Opcodes.S_GAME_OPENED)]
    public OpenGameResponse F_OPEN_GAME(OpenGameRequest request, IConnectionContext context, [FromServices] PlayerService playerService)
    {
        return new OpenGameResponse
        {
            CharacterInitialized = playerService.GetPlayer(context.Session) == null
        };
    }
    
    [Rpc((int)Opcodes.F_DELETE_NAME, (int)Opcodes.F_CHECK_NAME)]
    public CheckNameResponse F_DELETE_NAME(DeleteNameRequest request, [FromServices] ICharacterService characterService, [FromServices] GameDataStore gameDataStore)
    {
        return new CheckNameResponse
        {
            AccountUsername = request.AccountUsername,
            CharacterName = request.CharacterName,
            Invalid = false
        };
    }

    [Rpc((int)Opcodes.F_CREATE_CHARACTER, (int)Opcodes.F_SEND_CHARACTER_RESPONSE)]
    public async Task<RpcResult<AccountCharacterModifiedResponse>> F_CREATE_CHARACTER(CreateCharacterRequest request, IConnectionContext context, [FromServices] ICharacterService characterService, [FromServices] RealmInfo realmInfo)
    {
        try
        {
            await characterService.CreateCharacterAsync(context.Account.Id, (ushort)realmInfo.RealmId,
                request.ToNewCharacterModel());
            
            context.Session.Characters = await characterService.GetCharactersForAccountAsync(context.Account.Id);

            return new AccountCharacterModifiedResponse
            {
                AccountUsername = request.Name
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating character for account {AccountId}", context.Account.Id);
            
            context.SendResponse((byte)Opcodes.F_SEND_CHARACTER_ERROR, new AccountCharacterModifyErrorResponse
            {
                AccountUsername = context.Account.Username,
                ErrorMessage = "You have entered a duplicate or invalid name. Please enter a new name."
            });
            
            return RpcResult<AccountCharacterModifiedResponse>.NoResponse;
        }
    }
}