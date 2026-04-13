namespace Core.GameWorld.Spatial;

public interface IRegionEventHandler<in TEvent>
{
    void Handle(TEvent @event);
}