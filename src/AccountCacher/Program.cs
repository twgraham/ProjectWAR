using AccountCacher;
using AccountCacher.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NLog;
using NLog.Extensions.Logging;
using NLog.Web;

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
        .UseNLog()
        .ConfigureWebHostDefaults(webBuilder =>
        {
            webBuilder.ConfigureKestrel(opts =>
                    opts.ListenLocalhost(6800, o => { o.UseHttps(); }))
                .ConfigureServices((ctx, s) =>
                {
                    var config = ctx.Configuration.GetSection("accountCacher").Get<AccountConfig>()
                        ?? new AccountConfig();
                    s.ConfigureServices(config);
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => { endpoints.MapGrpcService<AccountMgrService>(); });
                });
        });

    var host = builder.Build();
    await host.RunAsync();
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[FATAL] {ex.Message}");
}
finally
{
    LogManager.Shutdown();
}

