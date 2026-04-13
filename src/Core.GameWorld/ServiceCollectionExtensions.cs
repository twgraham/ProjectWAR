using Core.Domain;
using Core.GameWorld.DataStore;
using Core.GameWorld.DataStore.Models;
using Core.GameWorld.DataStore.Providers;
using Core.GameWorld.Spatial;
using Core.GameWorld.Spawning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Core.GameWorld;

public static class ServiceCollectionExtensions
{
    /// <param name="services">The service collection.</param>
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers the <see cref="WorldDbContext"/>, <see cref="GameDataStore"/> (singleton),
        /// all <see cref="IDataProvider{TData}"/> implementations, and the
        /// <see cref="GameDataLoader"/> hosted service that populates the store at startup.
        /// </summary>
        /// <param name="connectionString">PostgreSQL connection string for the World database.</param>
        public IServiceCollection AddGameData(string connectionString)
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
            services.AddScoped<IDataProvider<CareerStatData>, CareerStatDataProvider>();
            services.AddScoped<IDataProvider<AbilityData>, AbilityDataProvider>();
            services.AddScoped<IDataProvider<SpawnData>, SpawnDataProvider>();

            // Loader — hosted service runs before the server accepts connections
            services.AddHostedService<GameDataLoader>();

            return services;
        }

        /// <summary>
        /// Registers the <see cref="CharacterDbContext"/> as a factory for on-demand
        /// short-lived context creation. Used by the singleton <c>CharacterService</c>
        /// which cannot inject a scoped DbContext directly.
        /// </summary>
        /// <param name="connectionString">PostgreSQL connection string for the Characters database.</param>
        public IServiceCollection AddCharacterData(string connectionString)
        {
            services.AddDbContextFactory<CharacterDbContext>(options =>
                options.UseNpgsql(connectionString));

            return services;
        }

        /// <summary>
        /// Registers the world topology infrastructure:
        /// <list type="bullet">
        ///   <item><see cref="RegionManager"/> — singleton region registry</item>
        ///   <item><see cref="IRegionEventDispatcher"/> — region event dispatch</item>
        /// </list>
        /// <para>
        /// Returns a <see cref="WorldTopologyBuilder"/> that lets consumers register
        /// <see cref="IRegionEventHandler{TEvent}"/> implementations via
        /// <c>.OnEvent&lt;TEvent, THandler&gt;()</c>.
        /// </para>
        /// <para>
        /// <b>Registration order matters.</b> Call this after <c>AddGameData()</c> (so spawn data
        /// is loaded before regions start) and before <c>AddServerNetworking()</c> (so regions
        /// are ticking before clients connect).
        /// </para>
        /// </summary>
        public WorldTopologyBuilder AddWorldTopology()
        {
            services.AddSingleton<IEntityFactory, EntityFactory>();
            services.AddSingleton<RegionManager>();

            var builder = new WorldTopologyBuilder(services);

            services.AddSingleton<RegionEventHandlerMap>(sp =>
                new RegionEventHandlerMap(sp, builder.Registrations));
            services.AddSingleton<IRegionEventDispatcher, RegionEventDispatcher>();

            return builder;
        }
    }
}