using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Core.GameWorld.Spatial;

/// <summary>
/// Fluent builder returned by <c>AddWorldTopology()</c> that lets consumers register
/// <see cref="IRegionEventHandler{TEvent}"/> implementations for region event dispatch.
/// <para>
/// All registrations are collected during DI configuration. The resulting
/// <see cref="RegionEventHandlerMap"/> resolves handler instances from DI at first use
/// and freezes them for efficient lookup.
/// </para>
/// </summary>
/// <example>
/// <code>
/// services.AddWorldTopology()
///     .OnEvent&lt;EntityBecameVisible, VisibilityService&gt;()
///     .OnEvent&lt;DamageResolved, CombatService&gt;();
/// </code>
/// </example>
public sealed class WorldTopologyBuilder
{
    internal readonly IServiceCollection Services;
    internal readonly List<(Type EventType, Type HandlerType)> Registrations = [];

    internal WorldTopologyBuilder(IServiceCollection services) => Services = services;

    /// <summary>
    /// Registers <typeparamref name="THandler"/> as a handler for <typeparamref name="TEvent"/>
    /// events dispatched by the region. The handler is also registered as a singleton in DI
    /// if not already present.
    /// </summary>
    public WorldTopologyBuilder OnEvent<TEvent, THandler>()
        where THandler : class, IRegionEventHandler<TEvent>
    {
        Registrations.Add((typeof(TEvent), typeof(THandler)));
        Services.TryAddSingleton<THandler>();
        return this;
    }
}
