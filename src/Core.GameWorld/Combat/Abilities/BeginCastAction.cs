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
///   <item>Dispatch appropriate region events (confirmed, completed, failed, cooldown)</item>
/// </list>
/// </para>
/// </summary>
public sealed class BeginCastAction : IRegionAction
{
    private readonly ushort _casterOid;
    private readonly ushort _targetOid;
    private readonly AbilityDefinition _definition;

    public BeginCastAction(
        ushort casterOid,
        ushort targetOid,
        AbilityDefinition definition)
    {
        _casterOid = casterOid;
        _targetOid = targetOid;
        _definition = definition;
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
            // Validation failed — dispatch failure event
            context.Dispatcher.Dispatch(new AbilityCastFailed(
                caster,
                new AbilityCastContext(_definition, caster, target) { FailureCode = failureCode },
                failureCode));
            return;
        }

        // 4. Phase 2: ConfirmCast (mutations — GCD, register active cast, instant execution)
        if (!abilities.ConfirmCast(castContext, tick))
        {
            // Re-validation failed — dispatch failure event
            context.Dispatcher.Dispatch(new AbilityCastFailed(
                caster,
                castContext,
                castContext.FailureCode ?? AbilityFailure.Cancelled));
            return;
        }

        // 5. Dispatch success events based on cast state
        if (castContext.IsInstant)
        {
            // Instant casts are already completed by ConfirmCast
            context.Dispatcher.Dispatch(new AbilityCastCompleted(caster, castContext));

            // Dispatch effect events (damage, heals) collected during instant execution
            var effects = abilities.PendingEffects;
            for (var i = 0; i < effects.Count; i++)
                context.Dispatcher.Dispatch(effects[i]);
            abilities.ClearPendingEffects();

            // Dispatch cooldown event if applicable
            DispatchCooldown(context, caster, castContext);
        }
        else
        {
            // Cast-bar or channel started — notify observers
            context.Dispatcher.Dispatch(new AbilityCastConfirmed(caster, castContext));
        }
    }

    private static void DispatchCooldown(IRegionActionContext context, UnitEntity caster, AbilityCastContext castContext)
    {
        var cooldownMs = (int)castContext.Cooldown;
        if (cooldownMs <= 0)
            return;

        var cdEntry = castContext.Definition.CooldownEntry != 0
            ? castContext.Definition.CooldownEntry
            : castContext.Definition.Entry;

        context.Dispatcher.Dispatch(new AbilityCooldownApplied(caster, cdEntry, cooldownMs));
    }
}
