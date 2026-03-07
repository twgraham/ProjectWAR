using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Core.Infrastructure.Network;
using Microsoft.Extensions.Logging;
using WorldServer.Managers;
using WorldServer.NetWork.V2.Dtos;
using WorldServer.World.Objects;
using IPacketHandler = Core.Infrastructure.Network.IPacketHandler;

namespace WorldServer.NetWork.V2.Handlers;

/// <summary>
/// Handles authentication-related packets from the game client.
/// This is the modernized equivalent of the legacy AuthentificationHandlers class.
/// </summary>
public class AuthenticationHandler : IPacketHandler
{
    private readonly ILogger<AuthenticationHandler> _logger;

    public AuthenticationHandler(ILogger<AuthenticationHandler> logger)
    {
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
        var version = $"{request.Major}.{request.Minor}.{request.Revision}";
        _logger.LogInformation(
            "Received F_ENCRYPTKEY from {RemoteAddress} — cipher={Cipher}, version={Version}, keyLength={KeyLength}",
            context.RemoteAddress, request.Cipher, version, request.Key.Length);

        switch (request.Cipher)
        {
            case 0:
                context.SendResponse((byte)Opcodes.F_RECEIVE_ENCRYPTKEY, new EncryptKeyResponse { Status = 1 });
                break;
            case 1:
            {
                if (context.PacketFramer is GameServerFramer framer)
                {
                    framer.SetEncryptionKey(request.Key);
                }

                break;
            }
        }
    }

    [Rpc((int)Opcodes.F_CONNECT, (int)Opcodes.S_CONNECTED)]
    public async Task<RpcResult<ConnectResponse>> F_CONNECT(ConnectRequest request, IConnectionContext context, [FromServices] SessionRegistry sessionRegistry)
    {
        _logger.LogInformation("Entering F_CONNECT {ClientId}", context.ClientId);

        var result = await Core.AcctMgr.CheckTokenAsync(new CheckTokenRequest
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

        context.Account = (await Core.AcctMgr.GetAccountAsync(new GetAccountRequest { Username = request.Username }))
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

        // Check if ip is banned. (they may have been just banned so launcher server wouldnt have picked it up)
        if (Core.AcctMgr.IsIpBanned(new IsIpBannedRequest { IpAddress = context.RemoteAddress?.Split(':')[0] })
            .IsBanned)
        {
            _logger.LogWarning("Banned IP = {Username}", request.Username);
            context.Disconnect("Banned by IP");
            return RpcResult<ConnectResponse>.NoResponse;
        }

        // Load characters before connection instead of later on
        CharMgr.LoadCharacters((int)context.Account.Id);

        return new ConnectResponse
        {
            RealmId = Convert.ToByte(Core.Rm.RealmId),
            RealmName = Core.Rm.Name,
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

        if (Core.Rm.RealmId != request.ServerID)
        {
            context.Disconnect("Requested realm ID does not match this server's ID");
            return RpcResult<PlayerEnterResponse>.NoResponse;
        }

        var player = playerService.GetPlayer(context.Session);
        player?.DisconnectType = Player.EDisconnectType.Unclean;

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
        player.DisconnectType = Player.EDisconnectType.Clean;
    }
    
    [Rpc((int)Opcodes.F_REQUEST_CHAR, (int)Opcodes.F_REQUEST_CHAR_RESPONSE)]
    public RpcResult<RequestCharacterResponse> F_REQUEST_CHAR(RequestCharacterRequest request, IConnectionContext context)
    {
        context.Session.State = eClientState.CharScreen;
        
        if (request.Operation == 0x2D58)
        {
            context.SendResponse((byte)Opcodes.F_REQUEST_CHAR_ERROR, new RequestCharacterErrorResponse
            {
               RealmType = (byte)CharMgr.GetAccountRealm((int)context.Account!.Id)
            });
            return RpcResult<RequestCharacterResponse>.NoResponse;
        }

        var response = new RequestCharacterResponse
        {
            AccountUsername = context.Account!.Username,
        };

        return response;

        // byte[] Chars = CharMgr.BuildCharacters((int)cclient._Account.Id);
        // Out.Write(Chars, 0, Chars.Length);
    }
}
