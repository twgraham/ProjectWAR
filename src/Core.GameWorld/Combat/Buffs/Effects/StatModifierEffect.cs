using Core.GameWorld.Entities;
using Core.GameWorld.Stats;

namespace Core.GameWorld.Combat.Buffs.Effects;

/// <summary>
/// Adds a flat stat bonus (or reduction) while the buff is active.
/// <para>
/// <see cref="BuffEffectDefinition.StatId"/> — which stat to modify.<br/>
/// <see cref="BuffEffectDefinition.PrimaryValue"/> — value at level 1.<br/>
/// <see cref="BuffEffectDefinition.SecondaryValue"/> — value at level 40.<br/>
/// Actual value = lerp(Primary, Secondary, (buffLevel-1)/39) × stackLevel.
/// Negative = reduction; positive = bonus.
/// </para>
/// </summary>
public sealed class StatModifierEffect : IBuffEffect
{
    public BuffEffectDefinition Definition { get; }

    /// <summary>The resolved flat value currently applied (for clean removal).</summary>
    private int _appliedValue;

    public StatModifierEffect(BuffEffectDefinition definition)
    {
        Definition = definition;
    }

    public void OnStart(Buff buff, UnitEntity target)
    {
        _appliedValue = ComputeValue(buff);
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

    private int ComputeValue(Buff buff)
    {
        int lo = Definition.PrimaryValue;
        int hi = Definition.SecondaryValue;
        float t = Math.Clamp((buff.BuffLevel - 1) / 39f, 0f, 1f);
        int baseValue = (int)(lo + (hi - lo) * t);
        return baseValue * buff.StackLevel;
    }

    private BuffClass ResolveClass(Buff buff) =>
        Definition.BuffClassOverride ?? buff.Definition.BuffClass;

    private void Apply(UnitEntity target, Buff buff, int value)
    {
        if (value == 0) return;
        var cls = ResolveClass(buff);

        if (value < 0)
            target.Stats.AddReduction(Definition.StatId, -value, cls);
        else
            target.Stats.AddBonus(Definition.StatId, value, cls);
    }

    private void Remove(UnitEntity target, Buff buff, int value)
    {
        if (value == 0) return;
        var cls = ResolveClass(buff);

        if (value < 0)
            target.Stats.RemoveReduction(Definition.StatId, -value, cls);
        else
            target.Stats.RemoveBonus(Definition.StatId, value, cls);
    }
}
