using System.Collections.Frozen;
using Microsoft.Extensions.DependencyInjection;

namespace Core.GameWorld.Spatial;

/// <summary>
/// Stores the mapping from event types to their <see cref="IRegionEventHandler{TEvent}"/>
/// implementations. Registrations are collected during DI configuration via
/// <see cref="WorldTopologyBuilder"/>. On first lookup the map resolves all handler instances
/// from DI and freezes into a <see cref="FrozenDictionary{TKey,TValue}"/> for zero-allocation
/// dispatch at runtime.
/// </summary>
internal sealed class RegionEventHandlerMap
{
    private readonly IServiceProvider _serviceProvider;
    private readonly List<(Type EventType, Type HandlerType)> _registrations;
    private volatile FrozenDictionary<Type, object>? _frozen;

    internal RegionEventHandlerMap(
        IServiceProvider serviceProvider,
        List<(Type EventType, Type HandlerType)> registrations)
    {
        _serviceProvider = serviceProvider;
        _registrations = [..registrations]; // defensive copy — registration phase is over
    }

    /// <summary>
    /// Returns all handlers registered for <typeparamref name="TEvent"/>.
    /// Freezes the map on first call; all subsequent calls are lock-free lookups.
    /// </summary>
    public IRegionEventHandler<TEvent>[] Get<TEvent>()
    {
        var frozen = _frozen ?? BuildAndFreeze();

        return frozen.TryGetValue(typeof(TEvent), out var handlers)
            ? (IRegionEventHandler<TEvent>[])handlers
            : [];
    }

    private FrozenDictionary<Type, object> BuildAndFreeze()
    {
        var dict = new Dictionary<Type, object>();

        foreach (var group in _registrations.GroupBy(r => r.EventType))
        {
            var eventType = group.Key;
            var handlerInterfaceType = typeof(IRegionEventHandler<>).MakeGenericType(eventType);

            var instances = group
                .Select(r => r.HandlerType)
                .Distinct()
                .Select(t => _serviceProvider.GetRequiredService(t))
                .ToArray();

            // Build a runtime-typed IRegionEventHandler<TEvent>[] so the cast in Get<T> succeeds.
            var typedArray = Array.CreateInstance(handlerInterfaceType, instances.Length);
            for (var i = 0; i < instances.Length; i++)
                typedArray.SetValue(instances[i], i);

            dict[eventType] = typedArray;
        }

        var result = dict.ToFrozenDictionary();
        Interlocked.CompareExchange(ref _frozen, result, null);
        return _frozen!;
    }
}