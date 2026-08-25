using Core.GameWorld.Entities;
using Core.GameWorld.Events;
using Core.GameWorld.Spatial;

namespace Core.GameWorld.Combat.Abilities;

/// <summary>
/// Region-thread action that cancels the caster's active cast-bar ability if it
/// cannot be cast while moving.
/// <para>
/// Enqueued by the movement handler on every position-update packet. Mirrors V1's
/// <c>AbilityInterface.OnPlayerMoved → CheckMoveInterrupt</c> path.
/// </para>
/// </summary>
public sealed class CancelCastOnMoveAction : IRegionAction
{
    private readonly ushort _casterOid;
    private readonly WorldPosition _newPosition;

    public CancelCastOnMoveAction(ushort casterOid, WorldPosition newPosition)
    {
        _casterOid = casterOid;
        _newPosition = newPosition;
    }

    /// <inheritdoc />
    public void Execute(IRegionActionContext context, long tick)
    {
        var caster = context.GetEntity(_casterOid) as UnitEntity;
        if (caster is null)
            return;

        // F_PLAYER_STATE2 carries position states even while stationary. Only an
        // actual displacement interrupts a non-moveable cast; heading changes do not.
        var oldPosition = caster.Position;
        if (oldPosition.RegionId == _newPosition.RegionId
            && oldPosition.X == _newPosition.X
            && oldPosition.Y == _newPosition.Y
            && oldPosition.Z == _newPosition.Z)
        {
            return;
        }

        var activeCast = caster.Abilities.ActiveCast;
        if (activeCast is null || activeCast.Definition.CanCastWhileMoving)
            return;

        // Instant casts complete synchronously and are never in ActiveCast at tick time.
        // Only Casting (cast bar) and Channeling are relevant here.
        if (activeCast.IsInstant)
            return;

        caster.Abilities.CancelCast(AbilityFailure.Interrupted);
        context.Dispatcher.Dispatch(new AbilityCastFailed(
            caster,
            activeCast,
            AbilityFailure.Interrupted));
    }
}
