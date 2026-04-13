using Core.GameWorld.Entities;

namespace Core.GameWorld.Combat.Buffs.Effects;

/// <summary>
/// Guard-style damage split: redirects a fraction of incoming damage to the caster.
/// <para>
/// <see cref="BuffEffectDefinition.PrimaryValue"/> — target damage fraction × 100
///     (e.g. 50 = target takes 50% damage).<br/>
/// <see cref="BuffEffectDefinition.SecondaryValue"/> — caster damage fraction × 100
///     (e.g. 50 = caster absorbs 50% of original).<br/>
/// Subscribes to <see cref="CombatEventType.ReceivingDamage"/> at
/// <see cref="CombatEventPriority.Guard"/>.
/// </para>
/// </summary>
public sealed class DamageSplitEffect : IBuffEffect
{
    public BuffEffectDefinition Definition { get; }

    /// <summary>Fraction of damage the target keeps (0.0–1.0).</summary>
    private float _targetFraction;

    /// <summary>Fraction of original damage the caster absorbs (0.0–1.0).</summary>
    private float _casterFraction;

    public DamageSplitEffect(BuffEffectDefinition definition)
    {
        Definition = definition;
    }

    public void OnStart(Buff buff, UnitEntity target)
    {
        _targetFraction = Definition.PrimaryValue * 0.01f;
        _casterFraction = Definition.SecondaryValue * 0.01f;
    }

    public void OnTick(Buff buff, UnitEntity target, long tick) { }
    public void OnEnd(Buff buff, UnitEntity target) { }

    public void OnCombatEvent(Buff buff, CombatEventType eventType,
        DamageContext? context, UnitEntity? instigator)
    {
        if (context is null) return;

        var caster = buff.Caster;
        if (caster is null || caster.Health.IsDead) return;

        // Guard only works while caster is alive.
        float originalDamage = context.Damage;
        if (originalDamage <= 0) return;

        // Reduce target's damage to the target fraction.
        context.Damage = originalDamage * _targetFraction;

        // Record the guard split amount. The caller (AbilityCastService or
        // combat pipeline) applies this to the guard tank separately.
        context.GuardSplitAmount += originalDamage * _casterFraction;
    }
}
