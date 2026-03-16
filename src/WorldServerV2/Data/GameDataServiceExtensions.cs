using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WorldServerV2.Data.Domain;
using WorldServerV2.Data.Providers;

namespace WorldServerV2.Data;

/// <summary>
/// Extension methods for registering the game data pipeline in the DI container.
/// </summary>
public static class GameDataServiceExtensions
{
    /// <summary>
    /// Registers the <see cref="WorldDbContext"/>, <see cref="GameDataStore"/> (singleton),
    /// all <see cref="IDataProvider{TData}"/> implementations, and the
    /// <see cref="GameDataLoader"/> hosted service that populates the store at startup.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">PostgreSQL connection string for the World database.</param>
    public static IServiceCollection AddGameData(
        this IServiceCollection services,
        string connectionString)
    {
        // EF Core DbContext — pooled for connection reuse
        services.AddPooledDbContextFactory<WorldDbContext>(options =>
            options.UseNpgsql(connectionString)
                   .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));

        // Store — single instance, exposed through both concrete and interface types
        // so the loader can call Initialize() via the concrete type while consumers
        // depend only on the read-only interface.
        services.AddSingleton<GameDataStore>();
        services.AddSingleton<IGameDataStore>(sp => sp.GetRequiredService<GameDataStore>());

        // Data providers — one per domain
        services.AddScoped<IDataProvider<ClassData>, ClassDataProvider>();
        services.AddScoped<IDataProvider<ItemData>, ItemDataProvider>();
        services.AddScoped<IDataProvider<CreatureData>, CreatureDataProvider>();
        services.AddScoped<IDataProvider<ZoneData>, ZoneDataProvider>();

        // Loader — hosted service runs before the server accepts connections
        services.AddHostedService<GameDataLoader>();

        return services;
    }

    /// <summary>
    /// Registers the <see cref="CharacterDbContext"/> as a factory for on-demand
    /// short-lived context creation. Used by the singleton <c>CharacterService</c>
    /// which cannot inject a scoped DbContext directly.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">PostgreSQL connection string for the Characters database.</param>
    public static IServiceCollection AddCharacterData(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContextFactory<CharacterDbContext>(options =>
            options.UseNpgsql(connectionString));

        return services;
    }
}
