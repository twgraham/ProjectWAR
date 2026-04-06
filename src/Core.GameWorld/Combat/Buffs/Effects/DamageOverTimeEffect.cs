using Core.GameWorld.Entities;

namespace Core.GameWorld.Combat.Buffs.Effects;

/// <summary>
/// Deals damage on each tick using the precalculated pipeline path.
/// <para>
/// <see cref="BuffEffectDefinition.PrimaryValue"/> — min total damage.<br/>
/// <see cref="BuffEffectDefinition.SecondaryValue"/> — max total damage.<br/>
/// The total damage is level-interpolated on start, then each tick deals
/// <c>total / intervals</c> via <see cref="DamagePipeline.Resolve"/> with
/// <see cref="DamageContext.IsPrecalculated"/> = true.
/// </para>
/// </summary>
public sealed class DamageOverTimeEffect : IBuffEffect
{
    public BuffEffectDefinition Definition { get; }

    /// <summary>Precalculated total damage (after level scaling, before per-tick split).</summary>
    private float _precalcDamage;

    /// <summary>Precalculated total mitigation.</summary>
    private float _precalcMitigation;

    /// <summary>Number of tick intervals over the buff duration.</summary>
    private int _intervals;

    public DamageOverTimeEffect(BuffEffectDefinition definition)
    {
        Definition = definition;
    }

    public void OnStart(Buff buff, UnitEntity target)
    {
        var caster = buff.Caster ?? target;
        var def = buff.Definition;

        // Calculate number of ticks over the buff's duration.
        _intervals = def.IntervalMs > 0 && def.DurationMs > 0
            ? Math.Max(1, (int)(def.DurationMs / def.IntervalMs))
            : 1;

        // Precalculate total damage and mitigation using the non-proc damage pipeline.
        var ctx = BuildPrecalcContext(buff, caster, target);
        DamagePipeline.Resolve(ctx);

        _precalcDamage = ctx.Damage * buff.StackLevel;
        _precalcMitigation = ctx.Mitigation * buff.StackLevel;
    }

    public void OnTick(Buff buff, UnitEntity target, long tick)
    {
        if (_intervals <= 0 || target.Health.IsDead) return;

        var caster = buff.Caster ?? target;
        float multiplier = 1f / _intervals;

        var ctx = new DamageContext
        {
            AbilityEntry = buff.Definition.Entry,
            IsPrecalculated = true,
            PrecalcDamage = _precalcDamage,
            PrecalcMitigation = _precalcMitigation,
            PrecalcMultiplier = multiplier,
            DamageType = ResolveDamageType(),
            IsProc = true, // DoTs skip percentage multipliers
            AttackerLevel = caster.Level,
            TargetLevel = target.Level,
            NoCrits = Definition.TertiaryValue != 0, // TertiaryValue != 0 means NoCrits
            CritRoll = 99,  // high roll = no crit by default
            CritVarianceRoll = 0f,
        };

        DamagePipeline.Resolve(ctx);
        target.Health.TakeDamage(ctx.FinalDamage);
    }

    public void OnEnd(Buff buff, UnitEntity target) { }

    public void OnCombatEvent(Buff buff, CombatEventType eventType,
        DamageContext? context, UnitEntity? instigator) { }

    // ── Internals ────────────────────────────────────────────────────

    /// <summary>
    /// Builds a DamageContext for the initial precalculation pass (stages 4-5 only).
    /// Uses MinDamage/MaxDamage from PrimaryValue/SecondaryValue with level interpolation.
    /// </summary>
    private DamageContext BuildPrecalcContext(Buff buff, UnitEntity caster, UnitEntity target)
    {
        return new DamageContext
        {
            AbilityEntry = buff.Definition.Entry,
            DamageType = ResolveDamageType(),
            AttackerLevel = caster.Level,
            TargetLevel = target.Level,
            MinDamage = (ushort)Math.Max(0, Definition.PrimaryValue),
            MaxDamage = (ushort)Math.Max(0, Definition.SecondaryValue),
            Undefendable = true, // DoTs are not defendable
            NoCrits = true,      // initial pass never crits
            IsProc = true,       // skip percentage multipliers in precalc
            DefenseRoll = 99,
            CritRoll = 99,
            CritVarianceRoll = 0f,
            DamageVarianceRoll = 0f,
        };
    }

    private DamageType ResolveDamageType()
    {
        // TertiaryValue encodes damage type if specified; default to Spiritual.
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
