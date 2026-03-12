using System.Net;
using Core.Infrastructure.Network;
using Grpc.Net.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using WorldServerV2.Config;
using WorldServerV2.Data;
using WorldServerV2.Network;
using WorldServerV2.Services;

try
{
    var builder = Host.CreateDefaultBuilder()
        .ConfigureAppConfiguration((ctx, config) =>
        {
            config
                .AddJsonFile("appsettings.json", optional: true)
                .AddJsonFile($"appsettings.{ctx.HostingEnvironment.EnvironmentName}.json", optional: true)
                .AddUserSecrets<Program>();
        })
        .ConfigureServices((ctx, s) =>
        {
            var accountCacherConfig = ctx.Configuration.GetSection("accountService").Get<AccountCacherConfig>()
                ?? throw new ConfigurationException("Missing or invalid accountService configuration section.");

            var accountMgrClient = new AccountMgr.AccountMgrClient(
                GrpcChannel.ForAddress(accountCacherConfig.BaseUrl, new GrpcChannelOptions
                {
                    HttpHandler = new HttpClientHandler
                    {
                        ServerCertificateCustomValidationCallback = (_, _, _, _) => true
                    }
                }));
            
            // gRPC client for account management service
            s.AddSingleton(accountMgrClient);
            
            var realmConfig = accountMgrClient.GetRealm(new GetRealmRequest { RealmId = ctx.Configuration.GetValue<uint>("realmId") });

            // Realm identity
            s.AddSingleton(realmConfig.Realm);
            s.AddSingleton<IPacketSerializerContext, GameServerContext>();
            s.AddSingleton<ICharacterService, CharacterService>();
            s.AddGameSessions();
            
            var databaseConfig = ctx.Configuration.GetSection("database").Get<DatabaseConfig>()
                ?? throw new ConfigurationException("Missing or invalid database configuration section.");

            var postgresConfig = new NpgsqlConnectionStringBuilder
            {
                Host = databaseConfig.Host,
                Port = databaseConfig.Port,
                Database = databaseConfig.Database,
                Username = databaseConfig.Username,
                Password = databaseConfig.Password,
            };
            
            s.AddGameData(postgresConfig.ToString());

            s.AddServerNetworking(IPEndPoint.Parse($"0.0.0.0:{realmConfig.Realm.Port}"))
                .WithPacketFramer<GameServerFramer>(ServiceLifetime.Scoped)
                .WithPacketSerializer<BinaryPacketSerializer>(ServiceLifetime.Scoped)
                .AddDefaultPacketHandlers();
        });

    var host = builder.Build();
    await host.RunAsync();
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[FATAL] {ex.Message}");
}