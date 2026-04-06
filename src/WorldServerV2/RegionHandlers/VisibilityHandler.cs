using Core.Domain.Entities;
using Core.GameWorld.Components;
using Core.GameWorld.Entities;
using Core.GameWorld.Events;
using Core.GameWorld.Spatial;
using Core.Session;
using WorldServerV2.Network;
using WorldServerV2.Network.Dtos;

namespace WorldServerV2.RegionHandlers;

public class VisibilityHandler : IRegionEventHandler<EntityBecameVisible>, IRegionEventHandler<EntityStateChanged>
{
    private readonly ISessionResolver<PlayerEntity> _sessionResolver;
    
    public VisibilityHandler(ISessionResolver<PlayerEntity> sessionResolver)
    {
        _sessionResolver = sessionResolver;
    }
    
    public void Handle(EntityBecameVisible @event)
    {
        switch (@event.Entity)
        {
            case CreatureEntity creature:
            {
                @event.Observer.SendCreateMonster(CreateMonsterResponse.From(creature, @event.Zone));
                break;
            }

            case GameObjectEntity gameObject:
            {
                @event.Observer.SendCreateStatic(CreateStaticResponse.From(gameObject, gameObject.Descriptor, @event.Zone));
                break;
            }

            case PlayerEntity otherPlayer:
            {
                var session = _sessionResolver.GetSession(otherPlayer);

                // The user has disconnected and the session will be cleaned up. No further processing.
                if (session == null)
                    return;
                
                @event.Observer.SendCreatePlayer(CreatePlayerResponse.From(session.Id, otherPlayer));
                break;
            }

            default:
                return; // Unknown entity type — no create-packet, skip follow-ups
        }

        // Generic component follow-ups (e.g. F_PLAYER_INVENTORY for equipment)
        if (@event.Entity.TryGet<EquipmentComponent>() is  { } equipmentComponent)
        {
            @event.Observer.SendEquippedInventory(EquippedInventoryResponse.From(@event.Entity, equipmentComponent.Items));
        }

        // Follow the create-packet with a stationary F_OBJECT_STATE so the client has
        // up-to-date position, health, and heading immediately.
        @event.Observer.SendObjectState(BuildStationaryState(@event.Entity, @event.Zone));
    }
    
    /// <summary>
    /// Builds a <see cref="StationaryObjectStateResponse"/> for the given entity.
    /// Dispatches between <see cref="UnitEntity"/> and <see cref="GameObjectEntity"/>.
    /// </summary>
    private static StationaryObjectStateResponse BuildStationaryState(
        WorldEntity entity, ZoneInfo zone)
    {
        return entity switch
        {
            UnitEntity unit => StationaryObjectStateResponse.From(unit, zone),
            GameObjectEntity go => StationaryObjectStateResponse.From(go, zone),
            _ => throw new InvalidOperationException(
                $"Unexpected entity type {entity.GetType().Name} in BroadcastEntityStates"),
        };
    }

    public void Handle(EntityStateChanged @event)
    {
        @event.Observer.SendObjectState(BuildStationaryState(@event.Entity, @event.Zone));
    }
}