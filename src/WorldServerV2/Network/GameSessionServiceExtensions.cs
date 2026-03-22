using Microsoft.Extensions.DependencyInjection;
using WorldServerV2.Services;
using WorldServerV2.Services.PlayerInit;

namespace WorldServerV2.Network;

/// <summary>
/// Extension methods for registering the game session infrastructure in the DI container.
/// </summary>
public static class GameSessionServiceExtensions
{
    /// <summary>
    /// Registers the <see cref="SessionRegistry"/> (singleton) and
    /// <see cref="SessionLifecycleService"/> (<see cref="Microsoft.Extensions.Hosting.IHostedService"/>)
    /// into the DI container.
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
        services.AddSingleton<PlayerService>();

        // Player initialization steps — registration order defines packet sequence.
        // Steps 1–5 form the minimum viable init set; steps 6–7 duplicate speed/stats
        // to match legacy server behavior.
        services.AddSingleton<IPlayerInitStep, SpeedInitStep>();            // 1. F_MAX_VELOCITY
        services.AddSingleton<IPlayerInitStep, PlayerInittedInitStep>();    // 2. S_PLAYER_INITTED
        services.AddSingleton<IPlayerInitStep, StatsInitStep>();            // 3. F_PLAYER_STATS
        services.AddSingleton<IPlayerInitStep, HealthInitStep>();           // 4. F_PLAYER_HEALTH
        services.AddSingleton<IPlayerInitStep, PlayerLoadedInitStep>();     // 5. S_PLAYER_LOADED
        services.AddSingleton<IPlayerInitStep, SpeedInitStep>();            // 6. F_MAX_VELOCITY (again)
        services.AddSingleton<IPlayerInitStep, StatsInitStep>();            // 7. F_PLAYER_STATS (again)

        services.AddSingleton<PlayerInitPipeline>();
        services.AddHostedService<SessionLifecycleService>();

        return services;
    }
}
