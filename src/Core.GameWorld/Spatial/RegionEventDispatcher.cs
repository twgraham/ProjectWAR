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
        var registered = handlers.Get<TEvent>();
        for (var i = 0; i < registered.Length; i++)
        {
            registered[i].Handle(@event);
        }
    }
}