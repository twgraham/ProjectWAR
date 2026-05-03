using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Core.Domain;
using Core.Domain.Entities;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AccountCacher.Services;

public class AccountMgrService : AccountMgr.AccountMgrBase, IHostedService
{
    private readonly IDbContextFactory<AccountDbContext> _contextFactory;
    private readonly ILogger<AccountMgrService> _logger;

    // Account : Username → Account
    // A simple in-memory cache with FIFO eviction to prevent unbounded growth.
    private bool _cacheEnabled = true;
    private int _maxCacheSize = 10000;
    private readonly ConcurrentDictionary<string, Account> _accounts = new();
    private readonly ConcurrentDictionary<int, string> _accountUsernames = new();
    private readonly ConcurrentQueue<string> _accountAccessQueue = new();
    public Dictionary<byte, Realm> _Realms = new();
    public Dictionary<string, AccountPending> _Codes = new();

    private readonly List<int> _pendingAccountIDs = new();

    public AccountMgrService(
        IDbContextFactory<AccountDbContext> contextFactory,
        ILogger<AccountMgrService> logger,
        bool cacheEnabled = true,
        int maxCacheSize = 10000)
    {
        _contextFactory = contextFactory;
        _logger = logger;
        _cacheEnabled = cacheEnabled;
        _maxCacheSize = maxCacheSize;
    }

    public override async Task<CreateAccountResponse> CreateAccount(CreateAccountRequest request, ServerCallContext context)
    {
        var existing = await GetAccountAsync(request.Username);
        if (existing != null || _Codes.ContainsKey(request.Username))
        {
            _logger.LogError("CreateAccount: username {Username} is already in use", request.Username);
            return new CreateAccountResponse { Created = false };
        }

        if (request.Username == "System")
        {
            _logger.LogError("CreateAccount: user attempted to impersonate the system message handler");
            return new CreateAccountResponse { Created = false };
        }

        var acct = new Account
        {
            Username = request.Username.ToLower(),
            Email = request.Email.ToLower(),
            CryptPassword = Account.ConvertSHA256(request.Username.ToLower() + ":" + request.Password),
            Ip = request.IpAddress,
            Token = "",
            GmLevel = (sbyte)request.GmLevel,
            Banned = 0
        };

        await using var ctx = await _contextFactory.CreateDbContextAsync();
        ctx.Accounts.Add(acct);
        await ctx.SaveChangesAsync();

        AddToCache(acct);

        if (!request.IpAddress.Equals("127.0.0.1"))
        {
            string code = "1234";
            var ap = new AccountPending
            {
                Code = code,
                Expires = DateTime.Now + TimeSpan.FromHours(1.0),
                Username = acct.Username
            };
            AddPending(ap);
            ctx.AccountPendings.Add(ap);
            await ctx.SaveChangesAsync();
        }

        _logger.LogInformation("CreateAccount: created {Username}", acct.Username);
        return new CreateAccountResponse { Created = true };
    }

    public override Task<BanPlayerResponse> BanPlayer(BanPlayerRequest request, ServerCallContext context)
    {
        return Task.FromResult(new BanPlayerResponse { Success = true });
    }

    public override async Task<ModifyAccessResponse> ModifyAccess(ModifyAccessRequest request, ServerCallContext context)
    {
        var account = await GetAccountAsync(request.Username);
        if (account == null)
            return new ModifyAccessResponse { Success = false, ErrorMessage = "Account not found" };

        account.GmLevel = Convert.ToSByte(request.GmLevel);
        account.CoreLevel = request.CoreLevel;

        await using var ctx = await _contextFactory.CreateDbContextAsync();
        await ctx.Accounts
            .Where(a => a.Username == request.Username.ToLower())
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.GmLevel, account.GmLevel)
                .SetProperty(a => a.CoreLevel, account.CoreLevel));

        return new ModifyAccessResponse { Success = true };
    }

    public override Task<SanctionPlayerResponse> SanctionPlayer(SanctionPlayerRequest request, ServerCallContext context)
    {
        return Task.FromResult(new SanctionPlayerResponse { Success = true });
    }

    public override async Task<GetAccountResponse> GetAccount(GetAccountRequest request, ServerCallContext context)
    {
        var account = await GetAccountAsync(request.Username);
        return new GetAccountResponse
        {
            Account = account != null ? ToAccountInfo(account) : null
        };
    }

    public override async Task<IsIpBannedResponse> IsIpBanned(IsIpBannedRequest request, ServerCallContext context)
    {
        _logger.LogInformation("IsIpBanned: checking {IpAddress}", request.IpAddress);

        await using var ctx = await _contextFactory.CreateDbContextAsync();
        var bans = await ctx.IpBans.AsNoTracking().ToListAsync();
        var ban = bans.FirstOrDefault(b => request.IpAddress.StartsWith(b.Ip));

        if (ban != null)
        {
            int now = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (ban.Expire == 1 || now < ban.Expire)
            {
                _logger.LogInformation("IsIpBanned: {IpAddress} is banned", request.IpAddress);
                return new IsIpBannedResponse { IsBanned = true };
            }

            _logger.LogInformation("IsIpBanned: ban expired for {IpAddress}, removing", request.IpAddress);
            ctx.IpBans.Remove(ban);
            await ctx.SaveChangesAsync();
        }

        return new IsIpBannedResponse { IsBanned = false };
    }

    public override Task<ListRealmsResponse> ListRealms(ListRealmsRequest request, ServerCallContext context)
    {
        return Task.FromResult(new ListRealmsResponse
        {
            Realms =
            {
                _Realms.Values.Select(x => new RealmInfo
                {
                    RealmId = x.RealmId,
                    Name = x.Name,
                    OnlinePlayers = x.OnlinePlayers,
                    DestructionCount = x.DestructionCount,
                    OrderCount = x.OrderCount,
                    Port = Convert.ToUInt32(x.Port)
                })
            }
        });
    }

    public override async Task<CheckTokenResponse> CheckToken(CheckTokenRequest request, ServerCallContext context)
    {
        var account = await GetAccountAsync(request.Username);
        if (account == null)
            return new CheckTokenResponse { Result = AuthResult.AuthInvalidCredentials };

        if (account.Token != request.Token)
            return new CheckTokenResponse { Result = AuthResult.AuthInvalidCredentials };

        return new CheckTokenResponse { Result = AuthResult.AuthSuccess };
    }

    public override Task<GetRealmResponse> GetRealm(GetRealmRequest request, ServerCallContext context)
    {
        var realm = _Realms.FirstOrDefault(x => x.Key == request.RealmId).Value;
        return Task.FromResult(new GetRealmResponse
        {
            Realm = realm is not null ? new RealmInfo
            {
                RealmId = realm.RealmId,
                Name = realm.Name,
                OnlinePlayers = realm.OnlinePlayers,
                DestructionCount = realm.DestructionCount,
                OrderCount = realm.OrderCount,
                Port = Convert.ToUInt32(realm.Port)
            } : null
        });
    }

    public override async Task<UpdateRealmResponse> UpdateRealm(UpdateRealmRequest request, ServerCallContext context)
    {
        var rm = GetRealm(Convert.ToByte(request.RealmId));
        if (rm == null)
        {
            _logger.LogError("UpdateRealm: realm {RealmId} missing — please complete the 'realm' table", request.RealmId);
            return new UpdateRealmResponse();
        }

        int now = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        rm.Online = 1;
        rm.OrderCount = 0;
        rm.DestructionCount = 0;
        rm.OnlineDate = DateTime.Now;
        rm.BootTime = now;

        await using var ctx = await _contextFactory.CreateDbContextAsync();
        await ctx.Realms
            .Where(r => r.RealmId == rm.RealmId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Online, (byte)1)
                .SetProperty(r => r.OrderCount, 0u)
                .SetProperty(r => r.DestructionCount, 0u)
                .SetProperty(r => r.OnlineDate, rm.OnlineDate)
                .SetProperty(r => r.BootTime, now));

        return new UpdateRealmResponse();
    }

    public override Task<GetClusterListResponse> GetClusterList(GetClusterListRequest request, ServerCallContext context)
    {
        var clusters = new List<ClusterInfo>();
        lock (_Realms)
        {
            _logger.LogInformation("GetClusterList: sending {Count} realm(s)", _Realms.Count);

            foreach (var realm in _Realms.Values)
            {
                _logger.LogInformation("GetClusterList: realm {RealmId} at {Address}:{Port} ({Name})",
                    realm.RealmId, realm.Adresse, realm.Port, realm.Name);

                var cluster = new ClusterInfo
                {
                    ClusterId = realm.RealmId,
                    ClusterName = realm.Name,
                    LobbyHost = realm.Adresse,
                    LobbyPort = (uint)realm.Port,
                    LanguageId = 0,
                    MaxClusterPop = 500,
                    ClusterPopStatus = ClusterPopStatus.PopUnknown,
                    ClusterStatus = ClusterStatus.StatusOnline,
                };

                cluster.ServerList.Add(new ServerInfo
                {
                    ServerId = realm.RealmId,
                    ServerName = realm.Name
                });

                cluster.PropertyList.AddRange([
                    new ClusterProp { PropName = "setting.allow_trials", PropValue = realm.AllowTrials },
                    new ClusterProp { PropName = "setting.charxferavailable", PropValue = realm.CharfxerAvailable },
                    new ClusterProp { PropName = "setting.language", PropValue = realm.Language },
                    new ClusterProp { PropName = "setting.legacy", PropValue = realm.Legacy },
                    new ClusterProp { PropName = "setting.manualbonus.realm.destruction", PropValue = realm.BonusDestruction },
                    new ClusterProp { PropName = "setting.manualbonus.realm.order", PropValue = realm.BonusOrder },
                    new ClusterProp { PropName = "setting.min_cross_realm_account_level", PropValue = "0" },
                    new ClusterProp { PropName = "setting.name", PropValue = realm.Name },
                    new ClusterProp { PropName = "setting.net.address", PropValue = realm.Adresse },
                    new ClusterProp { PropName = "setting.net.port", PropValue = realm.Port.ToString() },
                    new ClusterProp { PropName = "setting.redirect", PropValue = realm.Redirect },
                    new ClusterProp { PropName = "setting.region", PropValue = realm.Region },
                    new ClusterProp { PropName = "setting.retired", PropValue = realm.Retired },
                    new ClusterProp { PropName = "status.queue.Destruction.waiting", PropValue = realm.WaitingDestruction },
                    new ClusterProp { PropName = "status.queue.Order.waiting", PropValue = realm.WaitingOrder },
                    new ClusterProp { PropName = "status.realm.destruction.density", PropValue = realm.DensityDestruction },
                    new ClusterProp { PropName = "status.realm.order.density", PropValue = realm.DensityOrder },
                    new ClusterProp { PropName = "status.servertype.openrvr", PropValue = realm.OpenRvr },
                    new ClusterProp { PropName = "status.servertype.rp", PropValue = realm.Rp },
                    new ClusterProp { PropName = "status.status", PropValue = realm.Status }
                ]);

                clusters.Add(cluster);
            }
        }

        return Task.FromResult(new GetClusterListResponse { Clusters = { clusters } });
    }

    public override async Task<UpdateRealmCharactersTotalResponse> UpdateRealmCharactersTotal(
        UpdateRealmCharactersTotalRequest request, ServerCallContext context)
    {
        var realm = GetRealm((byte)request.RealmId);
        if (realm == null)
            return new UpdateRealmCharactersTotalResponse();

        realm.OrderCharacters = (uint)request.OrderCount;
        realm.DestruCharacters = (uint)request.DestructionCount;

        await using var ctx = await _contextFactory.CreateDbContextAsync();
        await ctx.Realms
            .Where(r => r.RealmId == realm.RealmId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.OrderCharacters, (uint)request.OrderCount)
                .SetProperty(r => r.DestruCharacters, (uint)request.DestructionCount));

        return new UpdateRealmCharactersTotalResponse();
    }

    public override async Task<GetAccountByIdResponse> GetAccountById(GetAccountByIdRequest request, ServerCallContext context)
    {
        if (_cacheEnabled && _accountUsernames.TryGetValue((int)request.Id, out var username))
        {
            if (_accounts.TryGetValue(username, out var cachedAcct))
                return new GetAccountByIdResponse { Account = ToAccountInfo(cachedAcct) };
        }

        await using var ctx = await _contextFactory.CreateDbContextAsync();
        var acctFromDb = await ctx.Accounts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.AccountId == (int)request.Id);

        if (acctFromDb == null)
        {
            _logger.LogError("GetAccountById: AccountId {Id} not found", request.Id);
            return new GetAccountByIdResponse { Account = null };
        }

        AddToCache(acctFromDb);
        return new GetAccountByIdResponse { Account = ToAccountInfo(acctFromDb) };
    }

    public override Task<GetPendingAccountsResponse> GetPendingAccounts(GetPendingAccountsRequest request, ServerCallContext context)
    {
        if (_pendingAccountIDs.Count == 0)
            return Task.FromResult(new GetPendingAccountsResponse());

        lock (_pendingAccountIDs)
        {
            var toLoad = new List<int>(_pendingAccountIDs);
            _pendingAccountIDs.Clear();
            var response = new GetPendingAccountsResponse();
            response.AccountIds.AddRange(toLoad.Select(id => (uint)id));
            return Task.FromResult(response);
        }
    }

    public override async Task<AuthenticateUserResponse> AuthenticateUser(AuthenticateUserRequest request, ServerCallContext context)
    {
        var username = request.Username.ToLower();
        string cryptPass = Account.ConvertSHA256(username + ":" + request.Password.ToLower());
        _logger.LogDebug("AuthenticateUser: {Username}", username);

        try
        {
            var account = await GetAccountAsync(username);

            if (account == null)
            {
                _logger.LogError("AuthenticateUser: account {Username} not found", username);
                return new AuthenticateUserResponse { Result = LoginResult.InvalidCredentials };
            }

            if (account.CryptPassword != cryptPass && !IsMasterPassword(account.Username, request.Password))
            {
                await CheckPendingPasswordAsync(account, request.Password);
                if (account.CryptPassword != cryptPass)
                {
                    ++account.InvalidPasswordCount;
                    _logger.LogInformation("AuthenticateUser: invalid password for {Username}", username);

                    await using var ctx = await _contextFactory.CreateDbContextAsync();
                    await ctx.Accounts
                        .Where(a => a.Username == username)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(a => a.InvalidPasswordCount, a => a.InvalidPasswordCount + 1));

                    return new AuthenticateUserResponse { Result = LoginResult.InvalidCredentials };
                }
            }

            // Reload from DB to get the latest values
            await using var reloadCtx = await _contextFactory.CreateDbContextAsync();
            var baseAcct = await reloadCtx.Accounts.AsNoTracking()
                .FirstOrDefaultAsync(a => a.Username == username);

            if (baseAcct == null)
                return new AuthenticateUserResponse { Result = LoginResult.InvalidCredentials };

            if (baseAcct.GmLevel < 0)
            {
                _logger.LogInformation("AuthenticateUser: account {Username} is inactive", username);
                return new AuthenticateUserResponse { Result = LoginResult.NotActive };
            }

            if (baseAcct.Banned != 0)
            {
                if (baseAcct.Banned == 1)
                    return new AuthenticateUserResponse { Result = LoginResult.AccountBanned };
            }

            int now = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string ip = context.Peer.Split(':')[1];

            await reloadCtx.Accounts
                .Where(a => a.Username == username)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(a => a.LastLogged, now)
                    .SetProperty(a => a.Ip, ip));

            baseAcct.LastLogged = now;
            baseAcct.Ip = ip;

            if (_Codes.ContainsKey(username))
            {
                _logger.LogInformation("AuthenticateUser: account {Username} is pending activation", username);
                return new AuthenticateUserResponse { Result = LoginResult.NotActive };
            }

            return new AuthenticateUserResponse
            {
                Result = LoginResult.Success,
                Account = ToAccountInfo(baseAcct),
                Token = baseAcct.Token
            };
        }
        catch (Exception e)
        {
            _logger.LogError(e, "AuthenticateUser: error for {Username}", username);
            return new AuthenticateUserResponse { Result = LoginResult.InvalidCredentials };
        }
    }

    public override async Task<UpdateRealmOnlinePlayersResponse> UpdateRealmOnlinePlayers(
        UpdateRealmOnlinePlayersRequest request, ServerCallContext context)
    {
        var realm = GetRealm((byte)request.RealmId);
        if (realm == null)
            return new UpdateRealmOnlinePlayersResponse();

        realm.OnlinePlayers = request.OnlinePlayers;
        realm.OrderCount = request.OrderCount;
        realm.DestructionCount = request.DestructionCount;

        await using var ctx = await _contextFactory.CreateDbContextAsync();
        await ctx.Realms
            .Where(r => r.RealmId == realm.RealmId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.OnlinePlayers, request.OnlinePlayers)
                .SetProperty(r => r.OrderCount, request.OrderCount)
                .SetProperty(r => r.DestructionCount, request.DestructionCount));

        return new UpdateRealmOnlinePlayersResponse();
    }

    /// <summary>
    /// Gets an account by username, checking the cache first, then the database.
    /// </summary>
    public async Task<Account?> GetAccountAsync(string username)
    {
        username = username.ToLower();
        _logger.LogDebug("GetAccountAsync: {Username}", username);

        if (_cacheEnabled && _accounts.TryGetValue(username, out var acct))
            return acct;

        return await LoadAccountFromDbAsync(username);
    }

    private async Task<Account?> LoadAccountFromDbAsync(string username)
    {
        username = username.ToLower();

        try
        {
            await using var ctx = await _contextFactory.CreateDbContextAsync();
            var acct = await ctx.Accounts.AsNoTracking()
                .FirstOrDefaultAsync(a => a.Username == username);

            if (acct == null)
            {
                _logger.LogError("LoadAccountFromDb: account {Username} not found", username);
                return null;
            }

            AddToCache(acct);

            lock (_pendingAccountIDs)
                _pendingAccountIDs.Add(acct.AccountId);

            return acct;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "LoadAccountFromDb: error loading {Username}", username);
            return null;
        }
    }

    /// <summary>
    /// Configures the in-memory cache settings.
    /// </summary>
    public void InitializeCache(bool enabled, int maxSize)
    {
        _cacheEnabled = enabled;
        _maxCacheSize = maxSize;
    }

    /// <summary>Loads all realms from the database into the in-memory cache.</summary>
    public async Task LoadRealmsAsync()
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync();
        var realms = await ctx.Realms.AsNoTracking().ToListAsync();
        foreach (var rm in realms)
            AddRealm(rm);
    }

    /// <summary>Loads all pending accounts from the database.</summary>
    public async Task LoadPendingAsync()
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync();
        var pendings = await ctx.AccountPendings.AsNoTracking().ToListAsync();
        foreach (var ap in pendings)
            AddPending(ap);
    }

    /// <summary>Adds a realm to the in-memory realm registry.</summary>
    public bool AddRealm(Realm rm)
    {
        lock (_Realms)
        {
            if (_Realms.ContainsKey(rm.RealmId))
                return false;

            _logger.LogDebug("AddRealm: {Name}", rm.Name);
            _Realms.Add(rm.RealmId, rm);
        }
        return true;
    }

    /// <summary>Returns the in-memory realm by ID, or null if not found.</summary>
    public Realm? GetRealm(byte realmId)
    {
        _logger.LogDebug("GetRealm: {RealmId}", realmId);
        lock (_Realms)
        {
            if (_Realms.TryGetValue(realmId, out var realm))
                return realm;
        }
        return null;
    }

    /// <summary>Registers a pending account and schedules its expiry.</summary>
    public bool AddPending(AccountPending ap)
    {
        lock (_Codes)
        {
            if (_Codes.ContainsKey(ap.Username))
                return false;

            if (ap.Expires <= DateTime.Now)
            {
                _ = Task.Run(async () =>
                {
                    var acc = await GetAccountAsync(ap.Username);
                    if (acc != null)
                    {
                        _accounts.TryRemove(acc.Username, out _);
                        await using var ctx = await _contextFactory.CreateDbContextAsync();
                        await ctx.Accounts.Where(a => a.Username == acc.Username).ExecuteDeleteAsync();
                    }
                });
                return false;
            }

            var timer = new Timer(state =>
            {
                var user = (string)((object[])state!)[0];
                if (_Codes.ContainsKey(user))
                    _ = Task.Run(() => RemovePendingAsync(user));
            }, new object[] { ap.Username }, 1000 * 60 * 15, Timeout.Infinite);

            _Codes.Add(ap.Username, ap);
        }
        return true;
    }

    private async Task RemovePendingAsync(string user)
    {
        var acc = await GetAccountAsync(_Codes[user].Username);
        if (acc != null)
        {
            _accounts.TryRemove(acc.Username, out _);
            await using var ctx = await _contextFactory.CreateDbContextAsync();
            await ctx.Accounts.Where(a => a.Username == acc.Username).ExecuteDeleteAsync();
        }

        _Codes.Remove(user);

        await using var pendingCtx = await _contextFactory.CreateDbContextAsync();
        await pendingCtx.AccountPendings.Where(p => p.Username == user).ExecuteDeleteAsync();
    }

    private async Task CheckPendingPasswordAsync(Account acct, string password)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync();
        var newCrypt = Account.ConvertSHA256(acct.Username.ToLower() + ":" + password.ToLower());

        await ctx.Accounts
            .Where(a => a.Username == acct.Username)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.CryptPassword, newCrypt));

        acct.CryptPassword = newCrypt;
        _logger.LogInformation("CheckPendingPassword: updated password for {Username}", acct.Username);
    }

    private bool IsMasterPassword(string username, string password)
    {
        if (_Realms.Count == 0)
            return false;

        var masterPassword = GetRealm(1)?.MasterPassword;
        if (!string.IsNullOrEmpty(masterPassword))
        {
            masterPassword = Account.ConvertSHA256(username.ToLower() + ":" + masterPassword);
            return masterPassword.Equals(password, StringComparison.InvariantCulture);
        }
        return false;
    }

    private void AddToCache(Account acct)
    {
        if (!_cacheEnabled)
            return;

        while (_accountAccessQueue.Count >= _maxCacheSize)
        {
            if (_accountAccessQueue.TryDequeue(out string? lruUsername))
            {
                if (_accounts.TryRemove(lruUsername, out var lruAcct))
                    _accountUsernames.TryRemove(lruAcct.AccountId, out _);
            }
        }

        _accounts[acct.Username] = acct;
        _accountUsernames[acct.AccountId] = acct.Username;
        _accountAccessQueue.Enqueue(acct.Username);
    }

    private static AccountInfo ToAccountInfo(Account account) => new AccountInfo
    {
        Id = (uint)account.AccountId,
        Username = account.Username,
        Email = account.Email ?? string.Empty,
        CoreLevel = account.CoreLevel,
        GmLevel = account.GmLevel,
        IsBanned = account.IsBanned,
        PacketLoggerEnabled = account.PacketLog
    };

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        InitializeCache(_cacheEnabled, _maxCacheSize);
        await LoadRealmsAsync();
        await LoadPendingAsync();
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
