using Core.GameWorld.Entities;

namespace Core.GameWorld.Combat.Buffs.Effects;

/// <summary>
/// Queues a buff onto a target when a subscribed combat event fires (proc buff).
/// <para>
/// <see cref="BuffEffectDefinition.PrimaryValue"/> — entry of the buff to apply.<br/>
/// <see cref="BuffEffectDefinition.SecondaryValue"/> — 0 = apply to self (buff target),
///     1 = apply to instigator.<br/>
/// Requires a <see cref="BuffDefinitionResolver"/> to look up the buff definition.
/// </para>
/// </summary>
public sealed class ProcBuffEffect : IBuffEffect
{
    public BuffEffectDefinition Definition { get; }

    /// <summary>
    /// Delegate that resolves a buff entry to its immutable definition.
    /// Injected via the effect factory.
    /// </summary>
    public Func<ushort, BuffDefinition?>? BuffLookup { get; init; }

    public ProcBuffEffect(BuffEffectDefinition definition)
    {
        Definition = definition;
    }

    public void OnStart(Buff buff, UnitEntity target) { }
    public void OnTick(Buff buff, UnitEntity target, long tick) { }
    public void OnEnd(Buff buff, UnitEntity target) { }

    public void OnCombatEvent(Buff buff, CombatEventType eventType,
        DamageContext? context, UnitEntity? instigator)
    {
        ushort buffEntry = (ushort)Definition.PrimaryValue;
        if (buffEntry == 0) return;

        var buffDef = BuffLookup?.Invoke(buffEntry);
        if (buffDef is null) return;

        // Determine proc target: self (buff owner) or instigator.
        bool applyToInstigator = Definition.SecondaryValue == 1;
        var procTarget = applyToInstigator ? instigator : buff.Target;
        if (procTarget is null || procTarget.Health.IsDead) return;

        var caster = buff.Caster ?? buff.Target;
        procTarget.Buffs.QueueBuff(buffDef, caster);
    }
}
