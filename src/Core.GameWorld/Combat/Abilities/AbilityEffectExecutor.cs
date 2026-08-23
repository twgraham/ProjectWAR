using Core.GameWorld.Combat.AutoAttack;
using Core.GameWorld.Combat.Career;
using Core.GameWorld.Entities;
using Core.GameWorld.Stats;

namespace Core.GameWorld.Combat.Abilities;

/// <summary>
/// Executes the effect commands of an ability (damage, healing, buffs, resource changes).
/// <para>
/// Extracted from <see cref="AbilityComponent"/> to keep the component focused on
/// lifecycle management. The executor is stateless aside from injectable delegates
/// and the RNG, and is shared across all entities via the component's
/// <see cref="AbilityComponent.EffectExecutor"/> property.
/// </para>
/// </summary>
public sealed class AbilityEffectExecutor
{
    private readonly Random _rng;

    /// <summary>
    /// Injectable buff definition resolver for <see cref="AbilityEffectType.InvokeBuff"/>
    /// commands. Returns the <see cref="BuffDefinition"/> for a given entry, or null.
    /// </summary>
    public Func<ushort, BuffDefinition?>? BuffLookup { get; set; }

    public AbilityEffectExecutor(Random? rng = null)
    {
        _rng = rng ?? Random.Shared;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  COMMAND DISPATCH
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Execute all non-<c>NoAutoUse</c> commands for the given ability context.
    /// </summary>
    /// <param name="onDamage">Optional callback invoked for each damage effect resolved.
    /// The <see cref="AbilityComponent"/> passes its <see cref="AbilityComponent.OnDamageDealt"/>
    /// callback here so damage results flow up to the entity.</param>
    public void ExecuteCommands(
        AbilityCastContext context, UnitEntity caster, UnitEntity? target,
        Action<UnitEntity, DamageResult>? onDamage = null)
    {
        var commands = context.Definition.Commands;
        for (var i = 0; i < commands.Count; i++)
        {
            var cmd = commands[i];
            if (cmd.NoAutoUse)
                continue;

            var effectTarget = ResolveCommandTarget(cmd.TargetType, caster, target);
            ExecuteCommand(context, cmd, (byte)i, caster, effectTarget, onDamage);

            // Chained sub-commands
            for (var j = 0; j < cmd.ChainedCommands.Count; j++)
                ExecuteCommand(context, cmd.ChainedCommands[j], (byte)i, caster, effectTarget, onDamage);
        }
    }

    private void ExecuteCommand(
        AbilityCastContext context, AbilityCommandDefinition cmd, byte commandIndex,
        UnitEntity caster, UnitEntity? target, Action<UnitEntity, DamageResult>? onDamage)
    {
        switch (cmd.EffectType)
        {
            case AbilityEffectType.DealDamage:
                if (target is not null && cmd.Damage is not null)
                    ExecuteDealDamage(context, cmd, commandIndex, caster, target, onDamage);
                break;

            case AbilityEffectType.StealLife:
                if (target is not null && cmd.Damage is not null)
                    ExecuteStealLife(context, cmd, commandIndex, caster, target, onDamage);
                break;

            case AbilityEffectType.InvokeBuff:
                ExecuteInvokeBuff(cmd, caster, target);
                break;

            case AbilityEffectType.ModifyActionPoints:
                ExecuteModifyActionPoints(cmd, target ?? caster);
                break;

            // ── Stubs for future steps ───────────────────────────────
            case AbilityEffectType.MultipleDealDamage:
            case AbilityEffectType.BounceDamage:
            case AbilityEffectType.Slay:
            case AbilityEffectType.InvokeAura:
            case AbilityEffectType.InvokeLinkedBuff:
            case AbilityEffectType.Knockback:
            case AbilityEffectType.Pull:
            case AbilityEffectType.JumpTo:
            case AbilityEffectType.CleanseCC:
            case AbilityEffectType.CleanseDebuffType:
            case AbilityEffectType.Interrupt:
            case AbilityEffectType.SummonPet:
            case AbilityEffectType.ModifyCareerResource:
                ExecuteModifyCareerResource(cmd, caster, target);
                break;

            case AbilityEffectType.ModifyMorale:
            case AbilityEffectType.GroundEffect:
            case AbilityEffectType.CreateLandMine:
                break;
        }
    }

    private static UnitEntity? ResolveCommandTarget(
        CommandTargetType targetType, UnitEntity caster, UnitEntity? target)
    {
        return targetType switch
        {
            CommandTargetType.Caster => caster,
            CommandTargetType.AllyOrSelf => target ?? caster,
            _ => target,
        };
    }

    // ═══════════════════════════════════════════════════════════════════
    //  EFFECT IMPLEMENTATIONS
    // ═══════════════════════════════════════════════════════════════════

    private void ExecuteDealDamage(
        AbilityCastContext context, AbilityCommandDefinition cmd, byte commandIndex,
        UnitEntity caster, UnitEntity target, Action<UnitEntity, DamageResult>? onDamage)
    {
        var dmgCtx = BuildDamageContext(context, cmd.Damage!, caster, target);

        // Resolve runs all pure-math stages. When buff events land (Step 6),
        // the caller will inject NotifyCombatEvent calls between stages.
        DamagePipeline.Resolve(dmgCtx);

        target.Health.TakeDamage(dmgCtx.FinalDamage);
        target.StateDirty = true;

        onDamage?.Invoke(caster, new DamageResult(
            target, dmgCtx.AbilityEntry, commandIndex,
            dmgCtx.FinalDamage, dmgCtx.FinalMitigation, dmgCtx.FinalAbsorption,
            dmgCtx.WasCritical, dmgCtx.WasDefended, dmgCtx.DefenseType));
    }

    private void ExecuteStealLife(
        AbilityCastContext context, AbilityCommandDefinition cmd, byte commandIndex,
        UnitEntity caster, UnitEntity target, Action<UnitEntity, DamageResult>? onDamage)
    {
        var dmgCtx = BuildDamageContext(context, cmd.Damage!, caster, target);
        DamagePipeline.Resolve(dmgCtx);

        var dealt = target.Health.TakeDamage(dmgCtx.FinalDamage);
        target.StateDirty = true;
        caster.Health.Heal(dealt);

        onDamage?.Invoke(caster, new DamageResult(
            target, dmgCtx.AbilityEntry, commandIndex,
            dmgCtx.FinalDamage, dmgCtx.FinalMitigation, dmgCtx.FinalAbsorption,
            dmgCtx.WasCritical, dmgCtx.WasDefended, dmgCtx.DefenseType));
    }

    private static void ExecuteModifyActionPoints(AbilityCommandDefinition cmd, UnitEntity target)
    {
        target.ActionPoints = Math.Max(0, target.ActionPoints + cmd.PrimaryValue);
    }

    private static void ExecuteModifyCareerResource(
        AbilityCommandDefinition cmd, UnitEntity caster, UnitEntity? target)
    {
        var entity = target ?? caster;
        var resource = entity.TryGet<CareerResourceComponent>()?.Resource;
        if (resource is null) return;

        int amount = cmd.PrimaryValue;
        if (amount > 0)
            resource.Generate(amount);
        else if (amount < 0)
            resource.Consume(-amount);
    }

    private void ExecuteInvokeBuff(AbilityCommandDefinition cmd, UnitEntity caster, UnitEntity? target)
    {
        ushort buffEntry = (ushort)cmd.PrimaryValue;
        if (buffEntry == 0 || BuffLookup is null) return;

        var buffDef = BuffLookup(buffEntry);
        if (buffDef is null) return;

        var buffTarget = target ?? caster;
        buffTarget.Buffs.QueueBuff(buffDef, caster);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  DAMAGE CONTEXT BUILDER
    // ═══════════════════════════════════════════════════════════════════

    private DamageContext BuildDamageContext(
        AbilityCastContext cast, DamageDefinition dmg,
        UnitEntity caster, UnitEntity target)
    {
        var casterStats = caster.Stats;
        var targetStats = target.Stats;
        var def = cast.Definition;

        return new DamageContext
        {
            // ── Input from ability ───────────────────────────────────
            AbilityEntry = def.Entry,
            DamageType = dmg.DamageType,
            SubDamageType = dmg.SubDamageType,
            AttackerLevel = caster.Level,
            TargetLevel = target.Level,
            MinDamage = dmg.MinDamage,
            MaxDamage = dmg.MaxDamage,
            DamageVariance = dmg.DamageVariance,
            CastTimeDamageMult = dmg.CastTimeDamageMult,
            StatDamageScale = dmg.StatDamageScale,
            PriStatMultiplier = dmg.PriStatMultiplier,
            BaseCritRate = (byte)Math.Clamp(
                dmg.CriticalHitRate + (int)cast.CritBonus, 0, 255),
            BaseCritDamageBonus = dmg.CriticalHitDamageBonus + cast.CritDamageBonus,
            ArmorResistPenFactor = dmg.ArmorResistPenFactor + cast.ArmorPenBonus,
            MinArmorPen = dmg.MinArmorPen,
            MaxArmorPen = dmg.MaxArmorPen,
            Defensibility = cast.Defensibility,
            Undefendable = dmg.Undefendable || cast.IsUndefendable,
            NoCrits = dmg.NoCrits,
            DamageBonus = cast.DamageBonus,
            DamageReduction = cast.DamageReduction,
            // ── Weapon (— used by weapon-damage abilities) ───────────────────────
            WeaponDps = caster.GetWeaponInfo(WeaponSlot.MainHand)?.Dps ?? 0f,
            WeaponDamageScale = dmg.WeaponDamageScale,
            // ── Attacker stat snapshots ──────────────────────────────
            AttackerPrimaryStat = dmg.StatUsed > 0
                ? casterStats.GetTotal((StatId)dmg.StatUsed)
                : 0,
            AttackerPowerStat = GetPowerStat(casterStats, def.AbilityType),
            AttackerWeaponSkill = casterStats.GetTotal(StatId.WeaponSkill),
            AttackerArmorPenPct = casterStats.GetTotal(StatId.ArmorPenetration) / 100f,
            AttackerCritRate = casterStats.GetTotal(StatId.CriticalHitRate),
            AttackerTypeCritRate = GetTypeCritRate(casterStats, def.AbilityType),
            AttackerCritDamage = casterStats.GetTotal(StatId.CriticalDamage),

            // ── Target stat snapshots ────────────────────────────────
            TargetToughness = targetStats.GetTotal(StatId.Toughness),
            TargetInitiative = targetStats.GetTotal(StatId.Initiative),
            TargetArmor = targetStats.GetTotal(StatId.Armor),
            TargetResistance = GetResistanceStat(targetStats, dmg.DamageType),
            TargetCritReduction = targetStats.GetTotal(StatId.CriticalHitRateReduction),
            TargetCritDamageTaken = targetStats.GetTotal(StatId.CriticalDamageTaken),
            TargetDefenseRating = GetDefenseRating(targetStats, def.AbilityType),
            TargetBlockRating = targetStats.GetTotal(StatId.Block),
            TargetBlock = targetStats.GetTotal(StatId.BlockSkill),
            TargetDefense = GetSecondaryDefense(targetStats, def.AbilityType),
            TargetIsFacing = true, // positional checks come later

            // ── Pre-rolled random values (deterministic pipeline) ────
            DamageVarianceRoll = (float)(_rng.NextDouble() * 2 - 1),
            DefenseRoll = _rng.Next(100),
            CritRoll = _rng.Next(100),
            CritVarianceRoll = (float)(_rng.NextDouble() * 0.2),
        };
    }

    // ═══════════════════════════════════════════════════════════════════
    //  STAT RESOLUTION HELPERS
    // ═══════════════════════════════════════════════════════════════════

    private static int GetPowerStat(StatContainer stats, AbilityType type) => type switch
    {
        AbilityType.Melee => stats.GetTotal(StatId.MeleePower),
        AbilityType.Ranged => stats.GetTotal(StatId.RangedPower),
        AbilityType.Verbal => stats.GetTotal(StatId.MagicPower),
        _ => 0,
    };

    private static int GetTypeCritRate(StatContainer stats, AbilityType type) => type switch
    {
        AbilityType.Melee => stats.GetTotal(StatId.MeleeCritRate),
        AbilityType.Ranged => stats.GetTotal(StatId.RangedCritRate),
        AbilityType.Verbal => stats.GetTotal(StatId.MagicCritRate),
        _ => 0,
    };

    private static int GetResistanceStat(StatContainer stats, DamageType dmgType) => dmgType switch
    {
        DamageType.Spiritual => stats.GetTotal(StatId.SpiritResistance),
        DamageType.Elemental => stats.GetTotal(StatId.ElementalResistance),
        DamageType.Corporeal => stats.GetTotal(StatId.CorporealResistance),
        _ => 0,
    };

    private static int GetDefenseRating(StatContainer stats, AbilityType type) => type switch
    {
        AbilityType.Melee => stats.GetTotal(StatId.WeaponSkill),
        AbilityType.Ranged => stats.GetTotal(StatId.Initiative),
        AbilityType.Verbal => stats.GetTotal(StatId.Willpower),
        _ => 0,
    };

    private static int GetSecondaryDefense(StatContainer stats, AbilityType type) => type switch
    {
        AbilityType.Melee => stats.GetTotal(StatId.ParrySkill),
        AbilityType.Ranged => stats.GetTotal(StatId.EvadeSkill),
        AbilityType.Verbal => stats.GetTotal(StatId.DisruptSkill),
        _ => 0,
    };
}
