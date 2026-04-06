using Core.GameWorld.Entities;

namespace Core.GameWorld.Combat.Buffs.Effects;

/// <summary>
/// Heals the target on each tick.
/// <para>
/// <see cref="BuffEffectDefinition.PrimaryValue"/> — heal at level 1.<br/>
/// <see cref="BuffEffectDefinition.SecondaryValue"/> — heal at level 40.<br/>
/// Per-tick heal = lerp(Primary, Secondary, (level-1)/39) × stackLevel / intervals.
/// </para>
/// </summary>
public sealed class HealOverTimeEffect : IBuffEffect
{
    public BuffEffectDefinition Definition { get; }

    /// <summary>Precalculated per-tick heal amount.</summary>
    private uint _perTickHeal;

    public HealOverTimeEffect(BuffEffectDefinition definition)
    {
        Definition = definition;
    }

    public void OnStart(Buff buff, UnitEntity target)
    {
        var def = buff.Definition;
        int intervals = def.IntervalMs > 0 && def.DurationMs > 0
            ? Math.Max(1, (int)(def.DurationMs / def.IntervalMs))
            : 1;

        int lo = Definition.PrimaryValue;
        int hi = Definition.SecondaryValue;
        byte level = (buff.Caster ?? target).Level;
        float t = Math.Clamp((level - 1) / 39f, 0f, 1f);
        float totalHeal = (lo + (hi - lo) * t) * buff.StackLevel;

        _perTickHeal = (uint)Math.Max(0, (int)(totalHeal / intervals));
    }

    public void OnTick(Buff buff, UnitEntity target, long tick)
    {
        if (_perTickHeal == 0 || target.Health.IsDead) return;
        target.Health.Heal(_perTickHeal);
    }

    public void OnEnd(Buff buff, UnitEntity target) { }

    public void OnCombatEvent(Buff buff, CombatEventType eventType,
        DamageContext? context, UnitEntity? instigator) { }
}
