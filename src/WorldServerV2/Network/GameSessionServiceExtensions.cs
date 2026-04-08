using Core.GameWorld.Entities;
using Core.Session;
using Microsoft.Extensions.DependencyInjection;
using WorldServerV2.Services;

namespace WorldServerV2.Network;

/// <summary>
/// Extension methods for registering the game session infrastructure in the DI container.
/// </summary>
public static class GameSessionServiceExtensions
{
    /// <summary>
    /// Registers the session infrastructure: <see cref="SessionRegistry"/> (singleton),
    /// <see cref="PlayerInitPipeline"/> (singleton), and <see cref="SessionLifecycleService"/>
    /// (<see cref="Microsoft.Extensions.Hosting.IHostedService"/>).
    /// <para>
    /// <see cref="SessionLifecycleService"/> is registered as a hosted service so the
    /// host eagerly starts it during application startup, wiring event handlers on
    /// <see cref="Core.Infrastructure.Network.NetworkManager"/> before it begins
    /// accepting connections.
    /// </para>
    /// </summary>
    public static IServiceCollection AddGameSessions(this IServiceCollection services)
    {
        services.AddSingleton<SessionRegistry>();
        services.AddSingleton<ISessionResolver<PlayerEntity>>(sp => sp.GetRequiredService<PlayerService>());
        services.AddSingleton<PlayerInitPipeline>();
        services.AddHostedService<SessionLifecycleService>();

        return services;
    }
}
