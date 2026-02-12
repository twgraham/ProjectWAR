using System;
using System.Linq;
using FrameWork;
using FrameWork.NetWork.V4;
using LauncherServer.Config;
using LauncherServer.Dtos;

namespace LauncherServer.Server;

public partial class LauncherClient : PacketHandler
{
    private readonly AccountMgr.AccountMgrClient _accountMgrClient;
    private readonly MythLoginServiceConfigManager _loginServiceConfigManager;
    private readonly LauncherConfig _config;

    public LauncherClient(
        AccountMgr.AccountMgrClient accountMgrClient,
        MythLoginServiceConfigManager loginServiceConfigManager,
        LauncherConfig config)
    {
        _accountMgrClient = accountMgrClient;
        _loginServiceConfigManager = loginServiceConfigManager;
        _config = config;
    }

    [Rpc(Opcodes.CL_CHECK, Opcodes.LCR_CHECK)]
    public CheckVersionResponse CL_CHECK(CheckVersionRequest packet)
    {
        Log.Debug("CL_CHECK", "Launcher Version : " + packet.Version);

        if (packet.Version != _config.Version)
        {
            return new CheckVersionResponse
            {
                Result = (byte)CheckResult.LAUNCHER_VERSION,
                MessageOrMythLoginServiceConfig = _config.Message
            };
        }

        if ((packet.Options & 1) == 1)
        {
            Log.Debug("CHECK", "Has mythic file info");
            if (packet.MythLoginServiceConfigLength != (ulong)_loginServiceConfigManager.Content.Length)
            {
                return new CheckVersionResponse
                {
                    Result = (byte)CheckResult.LAUNCHER_FILE,
                    MessageOrMythLoginServiceConfig = _config.Message
                };
            }
        }

        if ((packet.Options & 2) == 2)
        {
            Log.Debug("CHECK", "Has system info");
        }

        return new CheckVersionResponse { Result = (byte)CheckResult.LAUNCHER_OK };
    }

    [Rpc(Opcodes.CL_CREATE, Opcodes.LCR_CREATE)]
    public Dtos.CreateAccountResponse CL_CREATE(Dtos.CreateAccountRequest request, IConnectionContext context)
    {
        var result = CreateAccountResult.ACCOUNT_BANNED;
        var ip = context.RemoteAddress?.Split(":")[0];

        if (!_accountMgrClient.IsIpBanned(new IsIpBannedRequest { IpAddress = ip }).IsBanned)
        {
            var createAccountRequest = new CreateAccountRequest()
            {
                Username = request.Username,
                Password = request.Password,
                Email = request.Email ?? "",
                LanguageId = Convert.ToUInt32(request.LangID),
                IpAddress = ip
            };

            if (_accountMgrClient.CreateAccount(createAccountRequest).Created)
                result = CreateAccountResult.ACCOUNT_NAME_SUCCESS;
            else
                result = CreateAccountResult.ACCOUNT_NAME_BUSY;
        }

        return new Dtos.CreateAccountResponse { Status = result };
    }

    [Rpc(Opcodes.CL_START, Opcodes.LCR_START)]
    public StartResponse CL_START(StartRequest startRequest)
    {
        var authResult = _accountMgrClient.AuthenticateUser(new AuthenticateUserRequest
        {
            Username = startRequest.Username,
            Password = startRequest.PasswordHash
        });

        var response = new StartResponse
        {
            Result = authResult.Result switch
            {
                LoginResult.Success => Dtos.LoginResult.Success,
                LoginResult.InvalidCredentials => Dtos.LoginResult.InvalidCredentials,
                LoginResult.AccountBanned => Dtos.LoginResult.AccountBanned,
                LoginResult.NotActive => Dtos.LoginResult.NotActive,
                LoginResult.PatcherNotAllowed => Dtos.LoginResult.PatcherNotAllowed,
                _ => throw new InvalidOperationException()
            }
        };

        if (authResult.Result == LoginResult.Success)
        {
            Log.Debug("CL_START", "Sending token to client : " + startRequest.Username + " token : " + authResult.Token);
            response.AuthToken = authResult.Token;
        }

        return response;
    }

    [Rpc(Opcodes.CL_INFO, Opcodes.LCR_INFO)]
    public GetInfoResponse CL_INFO(GetInfoRequest request)
    {
        var realmsResponse = _accountMgrClient.ListRealms(new ListRealmsRequest());

        return new GetInfoResponse
        {
            RealmInfo = realmsResponse.Realms.Select(x =>
                new Dtos.RealmInfo
                {
                    Name = x.Name,
                    OnlinePlayers = x.OnlinePlayers,
                    OrderCount = x.OrderCount,
                    DestructionCount = x.DestructionCount
                }
            ).ToList()
        };
    }

    [Rpc(Opcodes.CL_VERSION, Opcodes.LCR_VERSION)]
    public GetVersionResponse CL_VERSION(GetVersionRequest request)
    {
        var g = Guid.NewGuid();
        return new GetVersionResponse
        {
            VersionHash = PatchMgr.VersionHash,
            ServerState = _config.ServerState,
            InstalId = g.ToString()
        };
    }
}
