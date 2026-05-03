using System.Net;
using System.Net.Http;
using Core.Infrastructure.Network;
using Grpc.Net.Client;
using LobbyServer;
using LobbyServer.NetWork;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NLog;
using NLog.Extensions.Logging;

try
{
    var builder = Host.CreateDefaultBuilder()
        .ConfigureAppConfiguration((ctx, config) =>
        {
            config
                .AddJsonFile("appsettings.json", optional: true)
                .AddJsonFile($"appsettings.{ctx.HostingEnvironment.EnvironmentName}.json", optional: true)
                .AddUserSecrets<Program>(optional: true);
        })
        .ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace);
            logging.AddNLog();
        })
        .ConfigureServices((ctx, s) =>
        {
            var config = ctx.Configuration.GetSection("lobbyServer").Get<LobbyConfigs>()
                ?? new LobbyConfigs();

            s.AddSingleton(new AccountMgr.AccountMgrClient(GrpcChannel.ForAddress("https://127.0.0.1:6800",
                new GrpcChannelOptions
                {
                    HttpHandler = new HttpClientHandler
                    {
                        ServerCertificateCustomValidationCallback = (_, _, _, _) => true
                    }
                })));

            s.AddServerNetworking(IPEndPoint.Parse($"127.0.0.1:{config.ClientPort}"))
                .WithPacketFramer<VarintLengthFramer>()
                .WithPacketSerializer<ProtobufPacketSerializer>()
                .AddDefaultPacketHandlers();
        });

    var host = builder.Build();
    await host.RunAsync();
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[FATAL] {ex}");
}
finally
{
    LogManager.Shutdown();
}

