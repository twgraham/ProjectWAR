using AccountCacher.Services;
using Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace AccountCacher;

internal static class ServiceCollectionExtensions
{
    public static void ConfigureServices(this IServiceCollection services, AccountConfig config)
    {
        var connectionString = new NpgsqlConnectionStringBuilder
        {
            Host = config.AccountDB.Host,
            Port = config.AccountDB.Port,
            Database = config.AccountDB.Database,
            Username = config.AccountDB.Username,
            Password = config.AccountDB.Password,
        }.ToString();

        services.AddDbContextFactory<AccountDbContext>(options =>
            options.UseNpgsql(connectionString));

        services.AddGrpc();

        services.AddSingleton<AccountMgrService>(sp =>
            new AccountMgrService(
                sp.GetRequiredService<IDbContextFactory<AccountDbContext>>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AccountMgrService>>(),
                config.EnableCache,
                config.MaxCacheSize));
        services.AddHostedService(sp => sp.GetRequiredService<AccountMgrService>());
    }
}
