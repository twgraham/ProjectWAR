using Core.GameWorld.Entities;
using Core.GameWorld.Spatial;

namespace Core.GameWorld.Combat.AutoAttack;

/// <summary>
/// Region-thread action that unconditionally stops a unit's auto-attack.
/// <para>
/// Enqueued by the network handler when the client sends <c>F_PLAYER_INFO</c>
/// with a new enemy target OID (0x18). Matches V1 behaviour where
/// <c>CombatInterface_Player.SetTarget</c> sets <c>IsAttacking = false</c> on
/// any enemy-target change.
/// </para>
/// </summary>
public sealed class StopAutoAttackAction : IRegionAction
{
    private readonly ushort _casterOid;

    public StopAutoAttackAction(ushort casterOid)
    {
        _casterOid = casterOid;
    }

    /// <inheritdoc />
    public void Execute(IRegionActionContext context, long tick)
    {
        var caster = context.GetEntity(_casterOid) as UnitEntity;
        caster?.AutoAttack.StopAttack();
    }
}
