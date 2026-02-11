using Common;
using DotNet.Testcontainers.Builders;
using FrameWork;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MySql.Data.MySqlClient;
using Testcontainers.MySql;
using Grpc.Net.Client;
using AccountCacher.Services;

namespace AccountCacher.Tests;

public class AccountCacherFixture : IAsyncLifetime
{
    private MySqlContainer? _mysqlContainer;
    private IHost? _host;
    public GrpcChannel? Channel { get; private set; }
    public AccountMgr.AccountMgrClient? Client { get; private set; }
    public string? ConnectionString { get; private set; }
    
    public async ValueTask InitializeAsync()
    {
        // Create and start MySQL container
        _mysqlContainer = new MySqlBuilder()
            .WithImage("mysql:8.0")
            .WithDatabase("war_accounts")
            .WithUsername("root")
            .WithPassword("admin")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(3306))
            .Build();

        await _mysqlContainer.StartAsync();
        
        // Get connection string
        ConnectionString = _mysqlContainer.GetConnectionString();
        
        // Initialize database schema
        await InitializeDatabaseSchema();
        
        // Create AccountConfig
        var config = new AccountConfig
        {
            IConfiguredTheFile = true,
            AccountDB = new DatabaseInfo
            {
                Server = _mysqlContainer.Hostname,
                Port = _mysqlContainer.GetMappedPublicPort(3306).ToString(),
                Database = "war_accounts",
                Username = "root",
                Password = "admin",
                Custom = "Treat Tiny As Boolean=False",
                MultipleActiveResultSets = false,
                ConnectionType = ConnectionType.DATABASE_MYSQL
            },
            EnableCache = true,
            MaxCacheSize = 10000
        };
        
        // Initialize logging
        Log.InitLog(new LogInfo { Info = true, Error = true, Debug = true, Tcp = false }, "AccountCacherTests");
        
        // Build and start the host
        var port = Random.Shared.Next(6000, 7000); // Use a random port in a specific range
        _host = Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(builder =>
            {
                builder.ConfigureKestrel(opts =>
                        opts.ListenLocalhost(port, o => { o.UseHttps(); })) // Use specific port
                    .ConfigureServices((context, services) =>
                    {
                        // Initialize database connection
                        var acc = new Account();
                        services.AddSingleton(
                            DBManager.Start(config.AccountDB.Total(), config.AccountDB.ConnectionType, "Accounts",
                                config.AccountDB.Database));
                        
                        services.AddGrpc();
                        
                        services.AddSingleton<AccountMgrService>(sp =>
                            new AccountMgrService(sp.GetRequiredService<IObjectDatabase>(), config.EnableCache, config.MaxCacheSize));
                        services.AddHostedService(sp => sp.GetRequiredService<AccountMgrService>());
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseEndpoints(endpoints => { endpoints.MapGrpcService<AccountMgrService>(); });
                    });
            })
            .Build();

        await _host.StartAsync();
        
        // Use the port we configured
        var address = $"https://localhost:{port}";
        
        // Create gRPC channel and client
        var httpHandler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
        
        Channel = GrpcChannel.ForAddress(address, new GrpcChannelOptions
        {
            HttpHandler = httpHandler
        });
        
        Client = new AccountMgr.AccountMgrClient(Channel);
        
        // Wait for service to be ready
        await Task.Delay(2000);
    }
    
    public async ValueTask DisposeAsync()
    {
        if (Channel != null)
        {
            await Channel.ShutdownAsync();
            Channel.Dispose();
        }
        
        if (_host != null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
        
        if (_mysqlContainer != null)
        {
            await _mysqlContainer.DisposeAsync();
        }
    }
    
    private async Task InitializeDatabaseSchema()
    {
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();
        
        // Create accounts table
        await ExecuteNonQueryAsync(connection, @"
            CREATE TABLE IF NOT EXISTS `accounts` (
              `AccountId` int NOT NULL AUTO_INCREMENT,
              `PacketLog` tinyint unsigned DEFAULT NULL,
              `Username` varchar(255) DEFAULT NULL,
              `Password` varchar(255) DEFAULT NULL,
              `CryptPassword` varchar(255) DEFAULT NULL,
              `Ip` varchar(255) DEFAULT NULL,
              `Token` varchar(255) DEFAULT NULL,
              `GmLevel` tinyint NOT NULL,
              `Banned` int NOT NULL,
              `BanReason` text,
              `AdviceBlockEnd` int DEFAULT NULL,
              `StealthMuteEnd` int DEFAULT NULL,
              `CoreLevel` int DEFAULT NULL,
              `LastLogged` int DEFAULT NULL,
              `LastNameChanged` int DEFAULT NULL,
              `LastPatcherLog` text,
              `InvalidPasswordCount` int unsigned NOT NULL,
              `noSurname` tinyint NOT NULL,
              `Email` text,
              PRIMARY KEY (`AccountId`),
              UNIQUE KEY `Username` (`Username`)
            ) ENGINE=InnoDB DEFAULT CHARSET=latin1;
        ");
        
        // Create accounts_pending table
        await ExecuteNonQueryAsync(connection, @"
            CREATE TABLE IF NOT EXISTS `accounts_pending` (
              `Id` int NOT NULL AUTO_INCREMENT,
              `Username` varchar(255) DEFAULT NULL,
              `Code` varchar(255) DEFAULT NULL,
              `Expires` datetime DEFAULT NULL,
              PRIMARY KEY (`Id`),
              UNIQUE KEY `Username` (`Username`)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        ");
        
        // Create realms table
        await ExecuteNonQueryAsync(connection, @"
            CREATE TABLE IF NOT EXISTS `realms` (
              `RealmId` tinyint unsigned NOT NULL DEFAULT '0',
              `Name` varchar(255) DEFAULT NULL,
              `Language` varchar(255) DEFAULT NULL,
              `Adresse` varchar(255) DEFAULT NULL,
              `Port` int NOT NULL,
              `AllowTrials` varchar(32) DEFAULT NULL,
              `CharfxerAvailable` varchar(32) DEFAULT NULL,
              `Legacy` varchar(32) DEFAULT NULL,
              `BonusDestruction` varchar(32) DEFAULT NULL,
              `BonusOrder` varchar(32) DEFAULT NULL,
              `Redirect` varchar(32) DEFAULT NULL,
              `Region` varchar(32) DEFAULT NULL,
              `Retired` varchar(32) DEFAULT NULL,
              `WaitingDestruction` varchar(32) DEFAULT NULL,
              `WaitingOrder` varchar(32) DEFAULT NULL,
              `DensityDestruction` varchar(32) DEFAULT NULL,
              `DensityOrder` varchar(32) DEFAULT NULL,
              `OpenRvr` varchar(32) DEFAULT NULL,
              `Rp` varchar(32) DEFAULT NULL,
              `Status` varchar(32) DEFAULT NULL,
              `Online` tinyint unsigned NOT NULL,
              `OnlineDate` datetime DEFAULT NULL,
              `OnlinePlayers` int unsigned DEFAULT NULL,
              `OrderCount` int unsigned DEFAULT NULL,
              `DestructionCount` int unsigned DEFAULT NULL,
              `MaxPlayers` int unsigned DEFAULT NULL,
              `OrderCharacters` int unsigned DEFAULT NULL,
              `DestruCharacters` int unsigned DEFAULT NULL,
              `NextRotationTime` bigint DEFAULT NULL,
              `MasterPassword` text,
              `BootTime` int DEFAULT NULL,
              PRIMARY KEY (`RealmId`),
              UNIQUE KEY `RealmId` (`RealmId`)
            ) ENGINE=InnoDB DEFAULT CHARSET=latin1;
        ");
        
        // Create ip_bans table
        await ExecuteNonQueryAsync(connection, @"
            CREATE TABLE IF NOT EXISTS `ip_bans` (
              `Ip` varchar(255) NOT NULL,
              `Expire` int DEFAULT NULL,
              PRIMARY KEY (`Ip`)
            ) ENGINE=MyISAM DEFAULT CHARSET=latin1;
        ");
    }
    
    private async Task ExecuteNonQueryAsync(MySqlConnection connection, string sql)
    {
        using var command = new MySqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
    
    public async Task<int> InsertTestAccountAsync(string username, string password, string email = "test@test.com", int gmLevel = 0, int banned = 0)
    {
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();
        
        var cryptPassword = Account.ConvertSHA256(username.ToLower() + ":" + password.ToLower());
        
        var sql = @"
            INSERT INTO accounts (Username, CryptPassword, Email, GmLevel, Banned, Ip, Token, InvalidPasswordCount, noSurname)
            VALUES (@Username, @CryptPassword, @Email, @GmLevel, @Banned, '127.0.0.1', '', 0, 0);
            SELECT LAST_INSERT_ID();
        ";
        
        using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Username", username.ToLower());
        command.Parameters.AddWithValue("@CryptPassword", cryptPassword);
        command.Parameters.AddWithValue("@Email", email);
        command.Parameters.AddWithValue("@GmLevel", gmLevel);
        command.Parameters.AddWithValue("@Banned", banned);
        
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }
    
    public async Task<int> InsertTestRealmAsync(byte realmId, string name, string address = "127.0.0.1", int port = 10300)
    {
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();
        
        var sql = @"
            INSERT INTO realms (RealmId, Name, Language, Adresse, Port, Online, OnlinePlayers, OrderCount, DestructionCount, MaxPlayers, OrderCharacters, DestruCharacters, NextRotationTime, MasterPassword, BootTime)
            VALUES (@RealmId, @Name, 'EN', @Address, @Port, 0, 0, 0, 0, 1000, 0, 0, 0, '', 0);
        ";
        
        using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@RealmId", realmId);
        command.Parameters.AddWithValue("@Name", name);
        command.Parameters.AddWithValue("@Address", address);
        command.Parameters.AddWithValue("@Port", port);
        
        return await command.ExecuteNonQueryAsync();
    }
    
    public async Task<int> InsertIpBanAsync(string ip, int expire)
    {
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();
        
        var sql = "INSERT INTO ip_bans (Ip, Expire) VALUES (@Ip, @Expire)";
        
        using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Ip", ip);
        command.Parameters.AddWithValue("@Expire", expire);
        
        return await command.ExecuteNonQueryAsync();
    }
    
    public async Task ClearAccountsAsync()
    {
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();
        await ExecuteNonQueryAsync(connection, "DELETE FROM accounts");
        await ExecuteNonQueryAsync(connection, "DELETE FROM accounts_pending");
    }
    
    public async Task ClearRealmsAsync()
    {
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();
        await ExecuteNonQueryAsync(connection, "DELETE FROM realms");
    }
    
    public async Task ClearIpBansAsync()
    {
        using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();
        await ExecuteNonQueryAsync(connection, "DELETE FROM ip_bans");
    }
}
