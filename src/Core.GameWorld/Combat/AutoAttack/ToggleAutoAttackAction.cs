using Core.GameWorld.Entities;
using Core.GameWorld.Spatial;

namespace Core.GameWorld.Combat.AutoAttack;

/// <summary>
/// Region-thread action that toggles a player's auto-attack state.
/// <para>
/// Enqueued by the network handler when the client sends <c>F_SWITCH_ATTACK_MODE</c>
/// (0xDC). Executes on the region thread where entity mutations are safe.
/// </para>
/// <para>
/// Behaviour matches V1: each invocation toggles <see cref="AutoAttackComponent.IsAttacking"/>.
/// When enabling, the component's target is set to the entity resolved from the
/// player's <see cref="UnitEntity.CurrentTargetOid"/>. When disabling, the target is cleared.
/// </para>
/// </summary>
public sealed class ToggleAutoAttackAction : IRegionAction
{
    private readonly ushort _casterOid;

    public ToggleAutoAttackAction(ushort casterOid)
    {
        _casterOid = casterOid;
    }

    /// <inheritdoc />
    public void Execute(IRegionActionContext context, long tick)
    {
        var caster = context.GetEntity(_casterOid) as UnitEntity;
        if (caster is null)
            return;

        var autoAttack = caster.AutoAttack;

        // Toggle — matches V1 CombatHandlers.F_SWITCH_ATTACK_MODE
        if (autoAttack.IsAttacking)
        {
            autoAttack.StopAttack();
            return;
        }

        // Enabling — resolve current target
        var targetOid = caster.CurrentTargetOid;
        if (targetOid is null or 0)
            return;

        var target = context.GetEntity(targetOid.Value) as UnitEntity;
        if (target is null || target.Health.IsDead)
            return;

        autoAttack.StartAttack(target);
    }
}
