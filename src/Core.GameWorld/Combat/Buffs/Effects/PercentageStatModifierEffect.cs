using Core.GameWorld.Entities;
using Core.GameWorld.Stats;

namespace Core.GameWorld.Combat.Buffs.Effects;

/// <summary>
/// Adds a percentage stat multiplier (or reduction) while the buff is active.
/// <para>
/// <see cref="BuffEffectDefinition.StatId"/> — which stat to modify.<br/>
/// <see cref="BuffEffectDefinition.PrimaryValue"/> — percentage at level 1 (e.g. 10 = +10%).<br/>
/// <see cref="BuffEffectDefinition.SecondaryValue"/> — percentage at level 40.<br/>
/// Negative = reduction multiplier; positive = bonus multiplier.
/// </para>
/// </summary>
public sealed class PercentageStatModifierEffect : IBuffEffect
{
    public BuffEffectDefinition Definition { get; }

    /// <summary>The resolved fraction currently applied (for clean removal).</summary>
    private float _appliedFraction;

    public PercentageStatModifierEffect(BuffEffectDefinition definition)
    {
        Definition = definition;
    }

    public void OnStart(Buff buff, UnitEntity target)
    {
        _appliedFraction = ComputeFraction(buff);
        Apply(target, buff, _appliedFraction);
    }

    public void OnTick(Buff buff, UnitEntity target, long tick) { }

    public void OnEnd(Buff buff, UnitEntity target)
    {
        Remove(target, buff, _appliedFraction);
        _appliedFraction = 0f;
    }

    public void OnCombatEvent(Buff buff, CombatEventType eventType,
        DamageContext? context, UnitEntity? instigator) { }

    // ── Internals ────────────────────────────────────────────────────

    private float ComputeFraction(Buff buff)
    {
        int lo = Definition.PrimaryValue;
        int hi = Definition.SecondaryValue;
        float t = Math.Clamp((buff.BuffLevel - 1) / 39f, 0f, 1f);
        float pct = lo + (hi - lo) * t;
        return pct * buff.StackLevel * 0.01f;
    }

    private BuffClass ResolveClass(Buff buff) =>
        Definition.BuffClassOverride ?? buff.Definition.BuffClass;

    private void Apply(UnitEntity target, Buff buff, float fraction)
    {
        if (fraction == 0f) return;
        var cls = ResolveClass(buff);

        if (fraction < 0)
            target.Stats.AddReductionMultiplier(Definition.StatId, 1f + fraction, cls);
        else
            target.Stats.AddBonusMultiplier(Definition.StatId, 1f + fraction, cls);
    }

    private void Remove(UnitEntity target, Buff buff, float fraction)
    {
        if (fraction == 0f) return;
        var cls = ResolveClass(buff);

        if (fraction < 0)
            target.Stats.RemoveReductionMultiplier(Definition.StatId, 1f + fraction, cls);
        else
            target.Stats.RemoveBonusMultiplier(Definition.StatId, 1f + fraction, cls);
    }
}
