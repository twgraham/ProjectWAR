using Core.GameWorld.Entities;

namespace Core.GameWorld.Combat.Buffs.Effects;

/// <summary>
/// Applies crowd-control flags while the buff is active.
/// <para>
/// CC flags are stored on <see cref="BuffDefinition.CrowdControl"/> and aggregated
/// by <see cref="BuffContainer.GetActiveCrowdControl"/>. This effect handles
/// the lifecycle notification and any future immunity checks.
/// </para>
/// <para>
/// <see cref="BuffEffectDefinition.PrimaryValue"/> — reserved for immunity duration override.<br/>
/// </para>
/// </summary>
public sealed class CrowdControlEffect : IBuffEffect
{
    public BuffEffectDefinition Definition { get; }

    public CrowdControlEffect(BuffEffectDefinition definition)
    {
        Definition = definition;
    }

    /// <summary>
    /// CC flags are applied implicitly through the buff's definition.
    /// The container's <see cref="BuffContainer.GetActiveCrowdControl"/> aggregates
    /// all active buffs' <see cref="BuffDefinition.CrowdControl"/> flags.
    /// No additional stat mutation needed here. Future: immunity check.
    /// </summary>
    public void OnStart(Buff buff, UnitEntity target) { }

    public void OnTick(Buff buff, UnitEntity target, long tick) { }

    /// <summary>
    /// CC is removed when the buff ends (flags cleared automatically by aggregation).
    /// </summary>
    public void OnEnd(Buff buff, UnitEntity target) { }

    public void OnCombatEvent(Buff buff, CombatEventType eventType,
        DamageContext? context, UnitEntity? instigator) { }
}
