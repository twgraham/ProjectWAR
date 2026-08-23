using Core.GameWorld.Entities;
using Core.GameWorld.Events;
using Core.GameWorld.Spatial;

namespace Core.GameWorld.Combat.Abilities;

/// <summary>
/// Region-thread action that performs authoritative ability cast validation and execution.
/// <para>
/// Created by the handler-thread combat service, enqueued to the region, and executed
/// during the region's command-drain phase.
/// </para>
/// <para>
/// Flow:
/// <list type="number">
///   <item>Resolve caster and target entities from OIDs</item>
///   <item>Call <see cref="AbilityComponent.TryInitiate"/> for read-only validation + context creation</item>
///   <item>Call <see cref="AbilityComponent.ConfirmCast"/> for mutations (GCD, register active cast)</item>
///   <item>Component callbacks on the caster entity emit all success/effect events automatically</item>
/// </list>
/// </para>
/// <para>
/// The action only dispatches failure events that occur <em>before</em> the component
/// callbacks can fire (TryInitiate or ConfirmCast re-validation failures).
/// </para>
/// </summary>
public sealed class BeginCastAction : IRegionAction
{
    private readonly ushort _casterOid;
    private readonly ushort _targetOid;
    private readonly AbilityDefinition _definition;
    private readonly byte _castSequence;

    public BeginCastAction(
        ushort casterOid,
        ushort targetOid,
        AbilityDefinition definition,
        byte castSequence)
    {
        _casterOid = casterOid;
        _targetOid = targetOid;
        _definition = definition;
        _castSequence = castSequence;
    }

    /// <inheritdoc />
    public void Execute(IRegionActionContext context, long tick)
    {
        // 1. Resolve caster
        var caster = context.GetEntity(_casterOid) as UnitEntity;
        if (caster is null)
            return;

        // 2. Resolve target (null is valid for self-cast / ground-targeted)
        UnitEntity? target = null;
        if (_targetOid != 0)
            target = context.GetEntity(_targetOid) as UnitEntity;

        var abilities = caster.Abilities;

        // 3. Phase 1: TryInitiate (read-only validation + context creation)
        var castContext = abilities.TryInitiate(
            _definition, target, tick, out var failureCode);

        if (castContext is null)
        {
            // Validation failed before any component state changed — dispatch directly
            context.Dispatcher.Dispatch(new AbilityCastFailed(
                caster,
                new AbilityCastContext(_definition, caster, target)
                {
                    CastSequence = _castSequence,
                    FailureCode = failureCode,
                },
                failureCode));
            return;
        }

        castContext.CastSequence = _castSequence;

        // 4. Phase 2: ConfirmCast (mutations — GCD, register active cast, instant execution)
        //    On success, component callbacks on the caster entity emit all relevant
        //    events (confirmed/completed/failed/cooldown/damage) through EventEmitted.
        if (!abilities.ConfirmCast(castContext, tick))
        {
            // Re-validation failed — dispatch directly (component didn't fire callbacks)
            context.Dispatcher.Dispatch(new AbilityCastFailed(
                caster,
                castContext,
                castContext.FailureCode ?? AbilityFailure.Cancelled));
        }
    }
}
