namespace Core.GameWorld.Spatial;

public interface IRegionEventDispatcher
{
    void Dispatch<TEvent>(TEvent @event);
}