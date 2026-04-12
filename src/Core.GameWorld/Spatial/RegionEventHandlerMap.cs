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

    /// <summary>Typed handler arrays — used by <see cref="Get{TEvent}"/> for the fast, exact-type path.</summary>
    private volatile FrozenDictionary<Type, object>? _handlers;

    /// <summary>Pre-built dispatch delegates — used by <see cref="Dispatch"/> for the runtime-type path.</summary>
    private volatile FrozenDictionary<Type, Action<object>>? _dispatchers;

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
    /// <para>
    /// <b>Important:</b> <typeparamref name="TEvent"/> must be the concrete event type.
    /// If the event is typed as a base interface (e.g. <c>ITickEvent</c>), use
    /// <see cref="Dispatch"/> instead to avoid an <see cref="InvalidCastException"/>.
    /// </para>
    /// </summary>
    public IRegionEventHandler<TEvent>[] Get<TEvent>(TEvent @event)
    {
        var frozen = _handlers ?? BuildAndFreeze();

        return frozen.TryGetValue(@event!.GetType(), out var handlers)
            ? (IRegionEventHandler<TEvent>[])handlers
            : [];
    }

    /// <summary>
    /// Dispatches <paramref name="event"/> to all handlers registered for its runtime type.
    /// Unlike <see cref="Get{TEvent}"/>, this works even when the compile-time type is a
    /// base interface (e.g. <c>ITickEvent</c>).
    /// </summary>
    public void Dispatch(object @event)
    {
        var dispatchers = _dispatchers ?? BuildAndFreeze_Dispatchers();

        if (dispatchers.TryGetValue(@event.GetType(), out var dispatch))
            dispatch(@event);
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
        Interlocked.CompareExchange(ref _handlers, result, null);
        return _handlers!;
    }

    private FrozenDictionary<Type, Action<object>> BuildAndFreeze_Dispatchers()
    {
        // Ensure handler arrays are built first.
        var frozen = _handlers ?? BuildAndFreeze();

        var dict = new Dictionary<Type, Action<object>>();

        foreach (var group in _registrations.GroupBy(r => r.EventType))
        {
            var eventType = group.Key;
            if (!frozen.TryGetValue(eventType, out var handlersObj))
                continue;

            // Build: (object e) => { for each handler in typed array, handler.Handle((TEvent)e); }
            dict[eventType] = BuildDispatchDelegate(eventType, handlersObj);
        }

        var result = dict.ToFrozenDictionary();
        Interlocked.CompareExchange(ref _dispatchers, result, null);
        return _dispatchers!;
    }

    /// <summary>
    /// Creates an <c>Action&lt;object&gt;</c> that casts the event to <paramref name="eventType"/>
    /// and invokes every handler in <paramref name="handlersArray"/>.
    /// </summary>
    private static Action<object> BuildDispatchDelegate(Type eventType, object handlersArray)
    {
        // handlersArray is IRegionEventHandler<TEvent>[] at runtime.
        // We resolve the concrete Handle method once and invoke it for each handler.
        var handlerInterfaceType = typeof(IRegionEventHandler<>).MakeGenericType(eventType);
        var handleMethod = handlerInterfaceType.GetMethod(nameof(IRegionEventHandler<>.Handle))!;
        var array = (Array)handlersArray;

        return e =>
        {
            for (var i = 0; i < array.Length; i++)
                handleMethod.Invoke(array.GetValue(i), [e]);
        };
    }
}