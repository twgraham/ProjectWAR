using Core.GameWorld.Events;

namespace Core.GameWorld.Spatial;

/// <summary>
/// Routes region events to all registered <see cref="IRegionEventHandler{TEvent}"/>
/// implementations. Handler lookup is backed by <see cref="RegionEventHandlerMap"/> which
/// freezes on first use for zero-allocation dispatch.
/// </summary>
internal sealed class RegionEventDispatcher(RegionEventHandlerMap handlers) : IRegionEventDispatcher
{
    public void Dispatch<TEvent>(TEvent @event)
    {
        // When TEvent is a concrete event type the fast path works: Get<T> returns
        // the typed handler array directly.  When TEvent is a base interface (e.g.
        // ITickEvent) the cast inside Get<T> would fail, so we fall back to the
        // runtime-type dispatch path which resolves handlers by @event.GetType().
        if (typeof(TEvent) != @event!.GetType())
        {
            handlers.Dispatch(@event);
            return;
        }

        var registered = handlers.Get(@event);
        for (var i = 0; i < registered.Length; i++)
        {
            registered[i].Handle(@event);
        }
    }
}
