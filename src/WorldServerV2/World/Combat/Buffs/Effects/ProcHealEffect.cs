using WorldServerV2.World.Entities;

namespace WorldServerV2.World.Combat.Buffs.Effects;

/// <summary>
/// Heals a target when a subscribed combat event fires (reactive proc).
/// <para>
/// <see cref="BuffEffectDefinition.PrimaryValue"/> — heal at level 1.<br/>
/// <see cref="BuffEffectDefinition.SecondaryValue"/> — heal at level 40.<br/>
/// The heal amount is level-interpolated.
/// </para>
/// </summary>
public sealed class ProcHealEffect : IBuffEffect
{
    public BuffEffectDefinition Definition { get; }

    public ProcHealEffect(BuffEffectDefinition definition)
    {
        Definition = definition;
    }

    public void OnStart(Buff buff, UnitEntity target) { }
    public void OnTick(Buff buff, UnitEntity target, long tick) { }
    public void OnEnd(Buff buff, UnitEntity target) { }

    public void OnCombatEvent(Buff buff, CombatEventType eventType,
        DamageContext? context, UnitEntity? instigator)
    {
        var healTarget = buff.Target;
        if (healTarget.Health.IsDead) return;

        uint healAmount = ComputeHeal(buff);
        if (healAmount > 0)
            healTarget.Health.Heal(healAmount);
    }

    // ── Internals ────────────────────────────────────────────────────

    private uint ComputeHeal(Buff buff)
    {
        int lo = Definition.PrimaryValue;
        int hi = Definition.SecondaryValue;
        byte level = (buff.Caster ?? buff.Target).Level;
        float t = Math.Clamp((level - 1) / 39f, 0f, 1f);
        float value = (lo + (hi - lo) * t) * buff.StackLevel;
        return (uint)Math.Max(0, (int)value);
    }
}
