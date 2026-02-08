using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FrameWork;
using FrameWork.Misc;
using FrameWork.NetWork.V4;
using Grpc.Net.Client;
using LauncherServer.Config;
using LauncherServer.Dtos;
using LauncherServer.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LauncherServer
{
    internal class Core
    {
        public static LauncherConfig Config;
        public static NetworkManager NetworkManager;

        public static int Version => 1;

        public static string Message => "hello";
        public static FileInfo Info;
        public static string StrInfo;

        public static AccountMgr.AccountMgrClient AcctMgr;
        
        // Keep for backwards compatibility with static classes that can't easily use DI
        internal static IServiceProvider ServiceProvider { get; private set; }

        [STAThread]
        private static void Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(onError);

            var host = Host.CreateDefaultBuilder(args)
                .ConfigureServices((hostContext, services) =>
                {
                    ConfigureServices(services);
                })
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.AddConsole();
                    logging.SetMinimumLevel(LogLevel.Debug);
                })
                .Build();

            // Set ServiceProvider for backwards compatibility with LoaderMgr/static classes
            ServiceProvider = host.Services;
            
            var logger = host.Services.GetRequiredService<ILogger<Core>>();
            
            logger.LogInformation("------------------- Launcher Server -------------------");

            // Loading log level from file (for FrameWork compatibility)
            if (!Log.InitLog(new LogInfo { Info = true, Error = true }, "LauncherServer"))
                ConsoleMgr.WaitAndExit(2000);

            Config = new LauncherConfig()
            {
                IConfiguredTheFile = true,
                LauncherServerPort = 8000,
                ServerState = ServerState.CLOSED,
                TempFilesPath = "TempFilesDirectory"
            };

            LoaderMgr.Start();

            Info = new FileInfo("Configs/mythloginserviceconfig.xml");
            if (!Info.Exists)
            {
                logger.LogError("Config file missing: Configs/mythloginserviceconfig.xml");
                ConsoleMgr.WaitAndExit(5000);
            }

            StrInfo = Info.OpenText().ReadToEnd();
            logger.LogInformation("mythloginserviceconfig.xml: {ConfigContent}", StrInfo);
            
            var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
            NetworkManager = new NetworkManager();
            var serializerContext = new LauncherSerializerContext();
            NetworkManager.Start(IPEndPoint.Parse("127.0.0.1:8000"), s => new LauncherClient(s, new BinaryPacketSerializerFactory(serializerContext), loggerFactory.CreateLogger<LauncherClient>()));
            
            AcctMgr = new AccountMgr.AccountMgrClient(GrpcChannel.ForAddress($"https://127.0.0.1:6800",
                new GrpcChannelOptions
                {
                    HttpHandler = new HttpClientHandler
                    {
                        ServerCertificateCustomValidationCallback = (message, certificate2, arg3, arg4) => true
                    }
                }));

            host.Run();
        }
        
        private static void ConfigureServices(IServiceCollection services)
        {
            // Register ConsoleMgr as a hosted service
            services.AddHostedService<ConsoleMgr>();
            
            // Register console handlers
            services.AddSingleton<Console.State>();
            
            // Register loggers for static classes that need them
            services.AddSingleton(sp => sp.GetRequiredService<ILogger<PatchMgr>>());
        }

        private static void onError(object sender, UnhandledExceptionEventArgs e)
        {
            // Use static Log for unhandled exceptions as we may not have access to ILogger at this point
            Log.Error("OnError", e.ExceptionObject.ToString());
            CrashGuard.GenerateCrashReport(e);
        }
    }
}