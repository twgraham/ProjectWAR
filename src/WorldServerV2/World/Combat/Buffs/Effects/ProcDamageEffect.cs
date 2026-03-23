using WorldServerV2.World.Entities;

namespace WorldServerV2.World.Combat.Buffs.Effects;

/// <summary>
/// Deals damage when a subscribed combat event fires (reactive proc).
/// <para>
/// <see cref="BuffEffectDefinition.PrimaryValue"/> — min damage.<br/>
/// <see cref="BuffEffectDefinition.SecondaryValue"/> — max damage.<br/>
/// <see cref="BuffEffectDefinition.TertiaryValue"/> — <see cref="DamageType"/> byte value.<br/>
/// The proc damage is resolved via <see cref="DamagePipeline.Resolve"/> as a proc
/// (skips percentage multipliers, not defendable).
/// </para>
/// </summary>
public sealed class ProcDamageEffect : IBuffEffect
{
    public BuffEffectDefinition Definition { get; }

    public ProcDamageEffect(BuffEffectDefinition definition)
    {
        Definition = definition;
    }

    public void OnStart(Buff buff, UnitEntity target) { }
    public void OnTick(Buff buff, UnitEntity target, long tick) { }
    public void OnEnd(Buff buff, UnitEntity target) { }

    public void OnCombatEvent(Buff buff, CombatEventType eventType,
        DamageContext? context, UnitEntity? instigator)
    {
        // Proc damage targets the instigator (the entity who caused the event).
        // For DealtDamage events, the instigator is the target that was hit.
        // For ReceivedDamage events, the instigator is the attacker.
        var procTarget = instigator;
        if (procTarget is null || procTarget.Health.IsDead) return;

        var caster = buff.Caster ?? buff.Target;
        if (caster.Health.IsDead) return;

        var dmgCtx = BuildProcContext(buff, caster, procTarget);
        DamagePipeline.Resolve(dmgCtx);

        if (dmgCtx.FinalDamage > 0)
            procTarget.Health.TakeDamage(dmgCtx.FinalDamage);
    }

    // ── Internals ────────────────────────────────────────────────────

    private DamageContext BuildProcContext(Buff buff, UnitEntity caster, UnitEntity target)
    {
        return new DamageContext
        {
            AbilityEntry = buff.Definition.Entry,
            DamageType = ResolveDamageType(),
            AttackerLevel = caster.Level,
            TargetLevel = target.Level,
            MinDamage = (ushort)Math.Max(0, Definition.PrimaryValue),
            MaxDamage = (ushort)Math.Max(0, Definition.SecondaryValue),
            IsProc = true,          // procs skip percentage multipliers
            Undefendable = true,    // procs are not defendable
            NoCrits = true,         // procs don't crit
            DefenseRoll = 99,
            CritRoll = 99,
            CritVarianceRoll = 0f,
            DamageVarianceRoll = 0f,
        };
    }

    private DamageType ResolveDamageType()
    {
        return Definition.TertiaryValue switch
        {
            (int)DamageType.Physical => DamageType.Physical,
            (int)DamageType.Elemental => DamageType.Elemental,
            (int)DamageType.Corporeal => DamageType.Corporeal,
            (int)DamageType.RawDamage => DamageType.RawDamage,
            _ => DamageType.Spiritual,
        };
    }
}
