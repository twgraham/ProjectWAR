using Core.GameWorld.Entities;
using Core.GameWorld.Stats;

namespace Core.GameWorld.Combat.Buffs.Effects;

/// <summary>
/// Modifies an entity's movement speed while the buff is active.
/// <para>
/// <see cref="BuffEffectDefinition.PrimaryValue"/> — speed modifier percentage
///     (e.g. -40 = 40% slow, +30 = 30% haste).<br/>
/// Uses <see cref="StatId.Velocity"/> in the entity's <see cref="StatContainer"/>.
/// </para>
/// </summary>
public sealed class SpeedModifierEffect : IBuffEffect
{
    public BuffEffectDefinition Definition { get; }

    /// <summary>The resolved bonus value currently applied.</summary>
    private int _appliedValue;

    public SpeedModifierEffect(BuffEffectDefinition definition)
    {
        Definition = definition;
    }

    public void OnStart(Buff buff, UnitEntity target)
    {
        _appliedValue = Definition.PrimaryValue * buff.StackLevel;
        Apply(target, buff, _appliedValue);
    }

    public void OnTick(Buff buff, UnitEntity target, long tick) { }

    public void OnEnd(Buff buff, UnitEntity target)
    {
        Remove(target, buff, _appliedValue);
        _appliedValue = 0;
    }

    public void OnCombatEvent(Buff buff, CombatEventType eventType,
        DamageContext? context, UnitEntity? instigator) { }

    // ── Internals ────────────────────────────────────────────────────

    private BuffClass ResolveClass(Buff buff) =>
        Definition.BuffClassOverride ?? buff.Definition.BuffClass;

    private void Apply(UnitEntity target, Buff buff, int value)
    {
        if (value == 0) return;
        var cls = ResolveClass(buff);

        if (value < 0)
            target.Stats.AddReduction(StatId.Velocity, -value, cls);
        else
            target.Stats.AddBonus(StatId.Velocity, value, cls);
    }

    private void Remove(UnitEntity target, Buff buff, int value)
    {
        if (value == 0) return;
        var cls = ResolveClass(buff);

        if (value < 0)
            target.Stats.RemoveReduction(StatId.Velocity, -value, cls);
        else
            target.Stats.RemoveBonus(StatId.Velocity, value, cls);
    }
}
