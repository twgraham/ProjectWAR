using System.Diagnostics;
using Core.Infrastructure.Network;
using Microsoft.Extensions.Logging;
using WorldServerV2.Network.Dtos;
using WorldServerV2.Services;
using WorldServerV2.World.Entities;
using IPacketHandler = Core.Infrastructure.Network.IPacketHandler;

namespace WorldServerV2.Network.Handlers;

/// <summary>
/// Handles authentication-related packets from the game client.
/// This is the modernized equivalent of the legacy AuthentificationHandlers class.
/// </summary>
public class AuthenticationHandler : IPacketHandler
{
    private readonly AccountMgr.AccountMgrClient _accountMgrClient;
    private readonly ICharacterService _characterService;
    private readonly RealmInfo _realmInfo;
    private readonly ILogger<AuthenticationHandler> _logger;

    public AuthenticationHandler(
        AccountMgr.AccountMgrClient accountMgrClient,
        ICharacterService characterService,
        RealmInfo realmInfo,
        ILogger<AuthenticationHandler> logger
    )
    {
        _accountMgrClient = accountMgrClient;
        _characterService = characterService;
        _realmInfo = realmInfo;
        _logger = logger;
    }

    /// <summary>
    /// Handles the F_ENCRYPTKEY packet (opcode 0x5C).
    /// The client sends its encryption capabilities and a 256-byte key.
    /// If cipher == 0: respond with F_RECEIVE_ENCRYPTKEY indicating no encryption.
    /// If cipher == 1: install RC4 encryption on the connection (not yet implemented).
    /// </summary>
    [Rpc((byte)Opcodes.F_ENCRYPTKEY)]
    public void F_ENCRYPTKEY(EncryptKeyRequest request, IConnectionContext context)
    {
        _logger.LogInformation(
            "Received F_ENCRYPTKEY from {RemoteAddress} — cipher={Cipher}, version={VersionMajor}.{VersionMinor}.{VersionRevision}, keyLength={KeyLength}",
            context.RemoteAddress, request.Cipher, request.Major, request.Minor, request.Revision, request.Key.Length);

        switch (request.Cipher)
        {
            case 0:
                context.SendResponse((byte)Opcodes.F_RECEIVE_ENCRYPTKEY, new EncryptKeyResponse { Status = 1 });
                break;
            case 1:
            {
                if (request.Key.Length < 256)
                {
                    _logger.LogError("Invalid encryption key length: {KeyLength}", request.Key.Length);
                    context.Disconnect("Invalid encryption key");
                    return;
                }
                
                if (context.PacketFramer is GameServerFramer framer)
                {
                    framer.SetEncryptionKey(request.Key.AsSpan()[..256]);
                }

                break;
            }
        }
    }

    [Rpc((int)Opcodes.F_CONNECT, (int)Opcodes.S_CONNECTED)]
    public async Task<RpcResult<ConnectResponse>> F_CONNECT(ConnectRequest request, IConnectionContext context, [FromServices] SessionRegistry sessionRegistry)
    {
        // _logger.LogInformation("Entering F_CONNECT {ClientId}", context.ClientId);

        var result = await _accountMgrClient.CheckTokenAsync(new CheckTokenRequest
            { Username = request.Username, Token = request.Token });

        if (result.Result == AuthResult.AuthSuspended)
        {
            _logger.LogError("Banned Account = {Username}", request.Username);
            context.Disconnect("Banned account");
            return RpcResult<ConnectResponse>.NoResponse;
        }
        
        if (result.Result != AuthResult.AuthSuccess)
        {
            _logger.LogError("Invalid Token = {Username} {Result}", request.Username, result.Result);

            context.SendResponse((byte)Opcodes.F_PLAYER_QUIT, new PlayerQuitResponse { Disconnect = true });
            context.Disconnect("Invalid token", true);
            return RpcResult<ConnectResponse>.NoResponse;
        }

        context.Account = (await _accountMgrClient.GetAccountAsync(new GetAccountRequest { Username = request.Username }))
            .Account;
        
        if (context.Account == null)
        {
            _logger.LogWarning("Invalid Account = {Username}", request.Username);
            context.Disconnect("Invalid account");
            return RpcResult<ConnectResponse>.NoResponse;
        }

        _logger.LogInformation("Connecting account to session session ID: {SessionId}, username: {Username}", context.Session.Id, request.Username);

        // Disconnect any existing sessions for this account (e.g. if they logged in from another location while we were processing)
        sessionRegistry.FindByAccountId(context.Account.Id)?.Disconnect("New session started for account");
        sessionRegistry.SetSessionAccount(context.Session, context.Account);

        // Check if ip is banned. (they may have been just banned so launcher server wouldnt have picked it up)
        if (_accountMgrClient.IsIpBanned(new IsIpBannedRequest { IpAddress = context.RemoteAddress?.Split(':')[0] })
            .IsBanned)
        {
            _logger.LogWarning("Banned IP = {Username}", request.Username);
            context.Disconnect("Banned by IP");
            return RpcResult<ConnectResponse>.NoResponse;
        }

        // Load characters into the session before responding
        context.Session.Characters = await _characterService.GetCharactersForAccountAsync(context.Account.Id);

        return new ConnectResponse
        {
            RealmId = Convert.ToByte(_realmInfo.RealmId),
            RealmName = _realmInfo.Name,
            Username = request.Username,
            Version = request.ProtocolVersion,
            TransferFlag = false,
        };
    }

    [Rpc((int)Opcodes.F_PING, (int)Opcodes.S_PONG)]
    public PingResponse F_PING(PingRequest request, IConnectionContext context)
    {
        return new PingResponse
        {
            ClientTimestamp = request.Timestamp,
            Timestamp = (ulong)Stopwatch.GetTimestamp(),
            // Sequence = context.SequenceId,
            Unk1 = 0
        };
    }
    
    [Rpc((int)Opcodes.F_DISCONNECT)]
    public void F_DISCONNECT(IConnectionContext context)
    {
        context.Disconnect("Client requested disconnect");
    }

    [Rpc((int)Opcodes.F_PLAYER_ENTER_FULL, (int)Opcodes.S_PID_ASSIGN)]
    public RpcResult<PlayerEnterResponse> F_PLAYER_ENTER_FULL(PlayerEnterRequest request, IConnectionContext context, [FromServices] PlayerService playerService)
    {
        _logger.LogDebug("Enter the game : {CharacterName},Slot={CharacterSlot}" , request.CharacterName, request.CharacterSlot);

        if (_realmInfo.RealmId != request.ServerID)
        {
            context.Disconnect("Requested realm ID does not match this server's ID");
            return RpcResult<PlayerEnterResponse>.NoResponse;
        }

        var player = playerService.GetPlayer(context.Session);
        player?.DisconnectType = DisconnectType.Unclean;

        return new PlayerEnterResponse
        {
            SessionId = context.Session.Id
        };
    }
    
    [Rpc((int)Opcodes.F_PLAYER_EXIT)]
    public void F_PLAYER_EXIT(PlayerExitRequest request, IConnectionContext context, [FromServices] PlayerService playerService)
    {
        if (context.Session.Id != request.SessionId)
            return;
        
        var player = playerService.GetPlayer(context.Session);
        
        if (player == null)
            return;
        
        _logger.LogDebug("Exit the game : {CharacterName}", player.Name);
        player.DisconnectType = DisconnectType.Clean;
    }
    
    [Rpc((int)Opcodes.F_REQUEST_CHAR, (int)Opcodes.F_REQUEST_CHAR_RESPONSE)]
    public async Task<RpcResult<RequestCharacterResponse>> F_REQUEST_CHAR(RequestCharacterRequest request, IConnectionContext context)
    {
        context.Session.State = ClientState.CharScreen;
        
        if (request.Operation == 0x2D58)
        {
            context.SendResponse((byte)Opcodes.F_REQUEST_CHAR_ERROR, new RequestCharacterErrorResponse
            {
               RealmType = context.Session.Realm
            });
            return RpcResult<RequestCharacterResponse>.NoResponse;
        }

        return new RequestCharacterResponse(context.Session);
    }
}
