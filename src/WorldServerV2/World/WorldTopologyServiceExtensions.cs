using Microsoft.Extensions.DependencyInjection;
using WorldServerV2.Services;
using WorldServerV2.World.Spatial;
using WorldServerV2.World.Spawning;

namespace WorldServerV2.World;

/// <summary>
/// Extension methods for registering the world topology system in the DI container.
/// </summary>
public static class WorldTopologyServiceExtensions
{
    /// <summary>
    /// Registers the world topology infrastructure:
    /// <list type="bullet">
    ///   <item><see cref="RegionManager"/> — singleton region registry</item>
    ///   <item><see cref="WorldService"/> — singleton facade for entity world operations</item>
    ///   <item><see cref="WorldHostedService"/> — <see cref="Microsoft.Extensions.Hosting.IHostedService"/>
    ///     that starts/stops region tick threads</item>
    /// </list>
    /// <para>
    /// <b>Registration order matters.</b> Call this after <c>AddGameData()</c> (so spawn data
    /// is loaded before regions start) and before <c>AddServerNetworking()</c> (so regions
    /// are ticking before clients connect).
    /// </para>
    /// </summary>
    public static IServiceCollection AddWorldTopology(this IServiceCollection services)
    {
        services.AddSingleton<IEntityFactory, EntityFactory>();
        services.AddSingleton<RegionManager>();
        services.AddSingleton<WorldService>();
        services.AddHostedService<WorldHostedService>();

        return services;
    }
}
