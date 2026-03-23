using WorldServerV2.World.Combat.Buffs.Effects;
using WorldServerV2.World.Entities;

namespace WorldServerV2.World.Combat.Buffs;

/// <summary>
/// Creates <see cref="IBuffEffect"/> instances from <see cref="BuffEffectDefinition"/>s.
/// <para>
/// Provides a default factory via <see cref="Default"/> that maps every
/// <see cref="BuffEffectType"/> to its concrete implementation. Inject into
/// <see cref="BuffContainer.EffectFactory"/> to activate buff effects.
/// </para>
/// </summary>
public static class BuffEffectFactory
{
    /// <summary>
    /// Optional delegate for resolving buff entries (needed by <see cref="ProcBuffEffect"/>).
    /// When null, <see cref="ProcBuffEffect"/> will be unable to look up buff definitions.
    /// </summary>
    public static Func<ushort, BuffDefinition?>? BuffLookup { get; set; }

    /// <summary>
    /// The default factory delegate. Assign to <see cref="BuffContainer.EffectFactory"/>:
    /// <code>container.EffectFactory = BuffEffectFactory.Default;</code>
    /// </summary>
    public static readonly Func<BuffEffectDefinition, IBuffEffect> Default = Create;

    /// <summary>
    /// Creates an <see cref="IBuffEffect"/> for the given definition.
    /// Unknown effect types return a <see cref="NullEffect"/>.
    /// </summary>
    public static IBuffEffect Create(BuffEffectDefinition definition)
    {
        return definition.EffectType switch
        {
            BuffEffectType.StatModifier => new StatModifierEffect(definition),
            BuffEffectType.PercentageStatModifier => new PercentageStatModifierEffect(definition),
            BuffEffectType.DamageOverTime => new DamageOverTimeEffect(definition),
            BuffEffectType.HealOverTime => new HealOverTimeEffect(definition),
            BuffEffectType.CrowdControl => new CrowdControlEffect(definition),
            BuffEffectType.SpeedModifier => new SpeedModifierEffect(definition),
            BuffEffectType.AbsorbShield => new AbsorbShieldEffect(definition),
            BuffEffectType.DamageSplit => new DamageSplitEffect(definition),
            BuffEffectType.ProcDamage => new ProcDamageEffect(definition),
            BuffEffectType.ProcHeal => new ProcHealEffect(definition),
            BuffEffectType.ProcBuff => new ProcBuffEffect(definition) { BuffLookup = BuffLookup },

            // Effect types that are stubs for future steps (aura, resource, etc.)
            _ => new NullEffect(definition),
        };
    }

    /// <summary>
    /// No-op effect for unimplemented or unknown <see cref="BuffEffectType"/> values.
    /// Satisfies the <see cref="IBuffEffect"/> contract without side effects.
    /// </summary>
    internal sealed class NullEffect : IBuffEffect
    {
        public BuffEffectDefinition Definition { get; }

        public NullEffect(BuffEffectDefinition definition) => Definition = definition;

        public void OnStart(Buff buff, UnitEntity target) { }
        public void OnTick(Buff buff, UnitEntity target, long tick) { }
        public void OnEnd(Buff buff, UnitEntity target) { }
        public void OnCombatEvent(Buff buff, CombatEventType eventType,
            DamageContext? context, UnitEntity? instigator) { }
    }
}
