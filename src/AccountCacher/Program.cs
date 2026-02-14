using FrameWork;
using System;
using AccountCacher;
using AccountCacher.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;

try
{
    Log.Info("", "-------------------- Account Cacher  -------------------");

    // Loading all configs files
    // ConfigMgr.LoadConfigs();
    // var configuration = ConfigMgr.GetConfig<AccountConfig>();
    var configuration = new AccountConfig
    {
        IConfiguredTheFile = true,
        AccountDB = new DatabaseInfo
        {
            Server = "127.0.0.1",
            Port = "3306",
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

    // Loading log level from file
    if (!Log.InitLog(configuration.LogLevel, "AccountCacher"))
        ConsoleMgr.WaitAndExit(2000);

    var host = Program.CreateHostBuilder(Array.Empty<string>(), configuration, 6800).Build();
    await host.RunAsync();
}
catch (Exception ex)
{
    Console.WriteLine(ex.Message);
}

// Make the Program class accessible and expose a factory method
public partial class Program
{
    public static IHostBuilder CreateHostBuilder(string[] args, AccountConfig configuration, int port = 6800)
    {
        return Host.CreateDefaultBuilder(args)
            .ConfigureWebHostDefaults(builder =>
            {
                builder.ConfigureKestrel(opts =>
                        opts.ListenLocalhost(port, o => { o.UseHttps(); }))
                    .ConfigureServices(s => s.ConfigureServices(configuration))
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseEndpoints(endpoints => { endpoints.MapGrpcService<AccountMgrService>(); });
                    });
            });
    }
}
