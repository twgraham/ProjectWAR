using System;
using System.IO;
using System.Net;
using System.Net.Http;
using FrameWork;
using FrameWork.Misc;
using FrameWork.NetWork.V4;
using Grpc.Net.Client;
using LauncherServer.Config;
using LauncherServer.Dtos;
using LauncherServer.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LauncherServer
{
    internal class Core
    {
        public static LauncherConfig Config;
        // public static TCPServer Server;
        public static NetworkManager NetworkManager;

        public static int Version => 1;

        public static string Message => "hello";
        public static FileInfo Info;
        public static string StrInfo;

        public static AccountMgr.AccountMgrClient AcctMgr;
        
        public static IServiceProvider ServiceProvider;
        private static ILogger<Core> _logger;

        [STAThread]
        private static void Main(string[] args)
        {
            // Set up dependency injection
            var services = new ServiceCollection();
            ConfigureServices(services);
            ServiceProvider = services.BuildServiceProvider();
            
            _logger = ServiceProvider.GetRequiredService<ILogger<Core>>();
            
            AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(onError);

            _logger.LogInformation("------------------- Launcher Server -------------------");

            // Loading all configs files
            // ConfigMgr.LoadConfigs();
            // Config = ConfigMgr.GetConfig<LauncherConfig>();

            // Loading log level from file
            if (!Log.InitLog(new LogInfo { Info = true, Error = true }, "LauncherServer"))
                ConsoleMgr.WaitAndExit(2000);

            // ServerState previousState = Config.ServerState;
            // Config.ServerState = ServerState.PATCH;
            Config = new LauncherConfig()
            {
                IConfiguredTheFile = true,
                LauncherServerPort = 8000,
                ServerState = ServerState.CLOSED,
                TempFilesPath = "TempFilesDirectory"
            };

            LoaderMgr.Start();

            // Config.ServerState = previousState;

            Info = new FileInfo("Configs/mythloginserviceconfig.xml");
            if (!Info.Exists)
            {
                _logger.LogError("Config file missing: Configs/mythloginserviceconfig.xml");
                ConsoleMgr.WaitAndExit(5000);
            }

            StrInfo = Info.OpenText().ReadToEnd();
            _logger.LogInformation("mythloginserviceconfig.xml: {ConfigContent}", StrInfo);
            
            var loggerFactory = ServiceProvider.GetRequiredService<ILoggerFactory>();
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

            ConsoleMgr.Start();
        }
        
        private static void ConfigureServices(IServiceCollection services)
        {
            services.AddLogging(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Debug);
            });
        }

        private static void onError(object sender, UnhandledExceptionEventArgs e)
        {
            _logger?.LogError("Unhandled exception: {Exception}", e.ExceptionObject.ToString());
            CrashGuard.GenerateCrashReport(e);
        }
    }
}