using Core.GameWorld.DataStore;
using Core.GameWorld.Entities;
using Core.GameWorld.Spatial;
using Core.Infrastructure.Network;
using Microsoft.Extensions.Logging;
using WorldServerV2.Network.Dtos;
using WorldServerV2.Services;

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
    public async Task<RpcResult<WorldEnterResponse>> F_DUMP_ARENAS_LARGE(
        DumpArenasLargeRequest request,
        IConnectionContext context,
        [FromServices] ICharacterService characterService,
        [FromServices] PlayerService playerService)
    {
        if (context.Account == null)
        {
            context.Disconnect("No account in F_DUMP_ARENAS_LARGE");
            return RpcResult<WorldEnterResponse>.NoResponse;
        }

        var characters = await characterService.GetCharactersForAccountAsync(context.Account.Id);
        var character = characters.FirstOrDefault(c => c.SlotId == request.CharacterSlot);
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
            CharacterRequiresInitialize = playerService.GetPlayer(context.Session) == null
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

    [Rpc((int)Opcodes.F_DELETE_CHARACTER, (int)Opcodes.F_SEND_CHARACTER_RESPONSE)]
    public async Task<RpcResult<AccountCharacterModifiedResponse>> F_DELETE_CHARACTER(DeleteCharacterRequest request,
        IConnectionContext context, [FromServices] ICharacterService characterService)
    {
        // A users characters should be available on the session
        var characters = await characterService.GetCharactersForAccountAsync(context.Account.Id);
        var character = characters.FirstOrDefault(x => x.SlotId == request.SlotId);
        
        if (character == null)
            return RpcResult<AccountCharacterModifiedResponse>.NoResponse;
        
        await characterService.DeleteCharacterAsync(character);
        
        return new AccountCharacterModifiedResponse
        {
            AccountUsername = context.Account.Username
        };
    }

    [Rpc((int)Opcodes.F_INIT_PLAYER)]
    public async Task F_INIT_PLAYER(InitializePlayerRequest request, IConnectionContext context,
        [FromServices] PlayerService playerService,
        [FromServices] PlayerInitPipeline initPipeline,
        [FromServices] RegionManager regionManager)
    {
        var player = playerService.GetPlayer(context.Session);
        if (player == null)
        {
            _logger.LogError("No player bound to session {SessionId} in F_INIT_PLAYER", context.Session.Id);
            context.Disconnect("No player in F_INIT_PLAYER");
            return;
        }

        var charValue = player.Character.Value;

        // Resolve the region from the character's saved position.
        var regionId = (ushort)charValue.RegionId;
        var region = regionManager.GetOrCreate(regionId);

        // Build the world position from the character's saved coordinates.
        var position = WorldPosition.FromRegionAbsolute(
            regionId, charValue.ZoneId, charValue.WorldX, charValue.WorldY,
            charValue.WorldZ, (ushort)charValue.WorldO);

        // Capture the session reference — used by the pipeline for sending packets.
        var session = context.Session;

        _logger.LogInformation(
            "Initializing player {Name} ({CharId}) for region {RegionId} at ({X}, {Y}, {Z})",
            player.Name, player.CharacterId, regionId,
            charValue.WorldX, charValue.WorldY, charValue.WorldZ);

        // Reserve an OID from the region's thread-safe pool. The reservation is
        // IDisposable — if init fails, the using block returns the OID to the pool.
        // Once consumed by Region.AddAsync, disposal is a no-op.
        using var reservation = region.ReserveOid();
        try
        {
            player.AssignOid(reservation);

            // ── Phase B + C run here, on the handler thread ─────────────
            // The client is on a loading screen and cannot interact.
            // GameSession.Send is thread-safe (channel-based send queue).
            initPipeline.Initialize(player, session);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Player init failed for {Name} ({CharId}) — OID {Oid} will be released",
                player.Name, player.CharacterId, reservation.Oid);
            context.Disconnect("Player initialization failed");
            return;
            // reservation.Dispose() runs at method exit, returning the OID to the pool.
        }

        // Add the fully-initialized player into the region. This consumes the
        // reservation — subsequent disposal at end of scope is a no-op.
        // We await placement so the entity is in a cell before the client receives
        // INIT_COMPLETE and can interact with the world.
        await region.AddAsync(player, position, reservation);

        // Now that the entity is placed, send the final init signal.
        session.SendPlayerInitComplete(new PlayerInitCompleteResponse
        {
            Oid = player.ObjectId,
        });
        session.MoveToWorldEnter();

        _logger.LogInformation(
            "Player {Name} ({CharId}, OID {Oid}) initialization complete — session {SessionId} → WorldEnter",
            player.Name, player.CharacterId, player.ObjectId, session.Id);
    }
}