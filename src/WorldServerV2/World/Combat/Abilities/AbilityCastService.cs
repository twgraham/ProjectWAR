using WorldServerV2.World.Combat.Buffs;
using WorldServerV2.World.Entities;
using WorldServerV2.World.Spatial;
using WorldServerV2.World.Stats;

namespace WorldServerV2.World.Combat.Abilities;

/// <summary>
/// Orchestrates the ability cast lifecycle: validation, cast-bar management,
/// effect execution, cooldown application.
/// <para>
/// Follows the split initiation / execution model (§11.6):
/// <list type="bullet">
///   <item><see cref="TryInitiate"/> — handler thread (read-only validation, context creation)</item>
///   <item><see cref="ConfirmCast"/> — region thread (re-validation, register pending, instant execution)</item>
///   <item><see cref="Update"/> — region thread tick (cast-bar timer, channel ticks)</item>
/// </list>
/// </para>
/// </summary>
public sealed class AbilityCastService
{
    private readonly Random _rng;

    /// <summary>
    /// Injectable weapon check. Returns <c>true</c> if the caster meets the weapon
    /// requirement. Defaults to always-pass (equipment system wired later).
    /// </summary>
    public Func<UnitEntity, WeaponRequirement, bool> WeaponCheck { get; set; } = (_, _) => true;

    /// <summary>
    /// Injectable condition evaluator for ability modifiers.
    /// If null, conditional modifiers are skipped.
    /// </summary>
    public Func<ModifierCondition, int, AbilityCastContext, bool>? ConditionEvaluator { get; set; }

    public AbilityCastService(Random? rng = null)
    {
        _rng = rng ?? Random.Shared;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  PHASE 1: INITIATION (handler-thread-safe, read-only)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Validate the cast attempt and create an <see cref="AbilityCastContext"/>.
    /// <para>
    /// Read-only over entity state — all mutations go into the new context.
    /// On success the caller sends the cast-bar packet and enqueues
    /// <see cref="ConfirmCast"/> to the region thread.
    /// </para>
    /// </summary>
    /// <param name="abilities">Caster's ability component (advisory read).</param>
    /// <param name="definition">The ability to cast.</param>
    /// <param name="caster">The casting entity.</param>
    /// <param name="target">Primary target, or null for self-cast / ground-targeted.</param>
    /// <param name="tick">Current timestamp in ms.</param>
    /// <param name="failureCode">Set to the rejection reason on failure.</param>
    /// <returns>The cast context on success; <c>null</c> if validation failed.</returns>
    public AbilityCastContext? TryInitiate(
        AbilityComponent abilities,
        AbilityDefinition definition,
        UnitEntity caster,
        UnitEntity? target,
        long tick,
        out AbilityFailure failureCode)
    {
        failureCode = AbilityFailure.Ok;

        // 1. Caster alive
        if (caster.Health.IsDead)
        {
            failureCode = AbilityFailure.CasterDead;
            return null;
        }

        // 2. Already casting
        if (abilities.HasActiveCast)
        {
            failureCode = AbilityFailure.AlreadyActive;
            return null;
        }

        // 3. Create context (snapshots definition defaults)
        var context = new AbilityCastContext(definition, caster, target);

        // 4. Apply pre-cast modifiers (modify context only, not entity state)
        if (!definition.IgnoreOwnModifiers)
            ApplyModifiers(context, ModifierStage.PreCast);

        // 5. CC check
        var ccFailure = CheckCrowdControl(caster, definition);
        if (ccFailure.HasValue)
        {
            failureCode = ccFailure.Value;
            return null;
        }

        // 6. GCD check
        if (!definition.IgnoreGlobalCooldown && abilities.IsOnGlobalCooldown(tick))
        {
            failureCode = AbilityFailure.Cooldown;
            return null;
        }

        // 7. Cooldown check (shared cooldown group or own entry)
        var cdEntry = definition.CooldownEntry != 0 ? definition.CooldownEntry : definition.Entry;
        if (abilities.IsOnCooldown(cdEntry, tick))
        {
            failureCode = AbilityFailure.Cooldown;
            return null;
        }

        // 8. AP check (advisory read — actual consumption at cast completion)
        if (context.ApCost > 0 && caster.ActionPoints < (int)context.ApCost)
        {
            failureCode = AbilityFailure.NotEnoughAp;
            return null;
        }

        // 9. Target validation
        if (target is not null)
        {
            if (!definition.AffectsDead && target.Health.IsDead)
            {
                failureCode = AbilityFailure.TargetDead;
                return null;
            }

            if (definition.AffectsDead && target.Health.IsAlive)
            {
                failureCode = AbilityFailure.InvalidTarget;
                return null;
            }
        }
        else if (RequiresTarget(definition))
        {
            failureCode = AbilityFailure.InvalidTarget;
            return null;
        }

        // 10. Range check
        if (target is not null && context.Range > 0)
        {
            var rangeResult = CheckRange(caster, target, context.Range, context.MinRange);
            if (rangeResult.HasValue)
            {
                failureCode = rangeResult.Value;
                return null;
            }
        }

        // 11. Weapon check (delegated — equipment system not yet wired)
        if (definition.WeaponNeeded != WeaponRequirement.None
            && !WeaponCheck(caster, definition.WeaponNeeded))
        {
            failureCode = AbilityFailure.WrongWeapon;
            return null;
        }

        return context;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  PHASE 2: CONFIRM CAST (region thread — mutations begin)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Region thread: re-validate and start the cast. State may have changed since
    /// <see cref="TryInitiate"/>. For instant casts, executes immediately.
    /// </summary>
    /// <returns><c>true</c> if the cast was confirmed.</returns>
    public bool ConfirmCast(AbilityComponent abilities, AbilityCastContext context, long tick)
    {
        var caster = context.Caster;
        var target = context.Target;
        var definition = context.Definition;

        // Re-validate (state may have changed between initiation and region drain)
        if (caster.Health.IsDead)
        {
            context.Fail(AbilityFailure.CasterDead);
            return false;
        }

        if (target is not null && !definition.AffectsDead && target.Health.IsDead)
        {
            context.Fail(AbilityFailure.TargetDead);
            return false;
        }

        if (target is not null && context.Range > 0)
        {
            var rangeCheck = CheckRange(caster, target, context.Range, context.MinRange);
            if (rangeCheck.HasValue)
            {
                context.Fail(rangeCheck.Value);
                return false;
            }
        }

        var ccCheck = CheckCrowdControl(caster, definition);
        if (ccCheck.HasValue)
        {
            context.Fail(ccCheck.Value);
            return false;
        }

        // === Mutations start here ===

        // Set GCD
        if (!definition.IgnoreGlobalCooldown)
            abilities.SetGlobalCooldown(tick);

        // Register active cast
        abilities.ActiveCast = context;
        abilities.RangeCheckDone = false;

        switch (context.CastState)
        {
            case CastState.Instant:
                CompleteCast(abilities, context, tick);
                break;

            case CastState.Casting:
                context.CastStartTime = tick;
                break;

            case CastState.Channeling:
                context.CastStartTime = tick;
                var interval = definition.ChannelInterval > 0
                    ? definition.ChannelInterval
                    : 1000;
                abilities.NextChannelTick = tick + interval;
                break;
        }

        return true;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  PHASE 3: UPDATE (region thread tick)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Tick the active cast for an entity. Call once per region tick.
    /// </summary>
    public void Update(AbilityComponent abilities, UnitEntity caster, long tick)
    {
        var context = abilities.ActiveCast;
        if (context is null)
            return;

        if (context.HasFailed)
        {
            abilities.ClearCast();
            return;
        }

        switch (context.CastState)
        {
            case CastState.Casting:
                UpdateCasting(abilities, context, caster, tick);
                break;

            case CastState.Channeling:
                UpdateChanneling(abilities, context, caster, tick);
                break;

            // Instant casts are fully handled in ConfirmCast — never in ActiveCast.
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  CANCEL & SETBACK
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>Cancel the active cast with the given reason.</summary>
    public void CancelCast(AbilityComponent abilities, AbilityFailure reason)
    {
        var context = abilities.ActiveCast;
        if (context is null)
            return;

        context.Fail(reason);

        // Cast-bar abilities clear the GCD when interrupted (matching V1).
        if (context.Definition.CastTime > 0)
            abilities.ClearGlobalCooldown();

        abilities.ClearCast();
    }

    /// <summary>
    /// Apply setback to the active cast-bar ability (from being hit while casting).
    /// Extends the remaining cast time. For Fragile ≥ 2 abilities, interrupts immediately.
    /// </summary>
    /// <param name="abilities">The caster's ability component.</param>
    /// <param name="delayMs">Milliseconds of delay to add to the cast.</param>
    public void AddSetback(AbilityComponent abilities, float delayMs)
    {
        var context = abilities.ActiveCast;
        if (context is null || context.CastState != CastState.Casting)
            return;

        if (context.Definition.Fragile >= 2)
        {
            CancelCast(abilities, AbilityFailure.Interrupted);
            return;
        }

        context.SetbackAccumulator += delayMs;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  CAST COMPLETION
    // ═══════════════════════════════════════════════════════════════════

    private void CompleteCast(AbilityComponent abilities, AbilityCastContext context, long tick)
    {
        var caster = context.Caster;
        var definition = context.Definition;

        // Final caster-alive check
        if (caster.Health.IsDead)
        {
            context.Fail(AbilityFailure.CasterDead);
            abilities.ClearCast();
            return;
        }

        // Consume AP
        if (context.ApCost > 0)
            caster.ActionPoints = Math.Max(0, caster.ActionPoints - (int)context.ApCost);

        // Apply post-cast modifiers
        if (!definition.IgnoreOwnModifiers)
            ApplyModifiers(context, ModifierStage.PostCast);

        // Execute commands
        ExecuteCommands(context, caster, context.Target);

        // Apply cooldown
        ApplyCooldown(abilities, definition, context, tick);

        // Clear active cast
        abilities.ClearCast();
    }

    // ═══════════════════════════════════════════════════════════════════
    //  UPDATE HELPERS
    // ═══════════════════════════════════════════════════════════════════

    private void UpdateCasting(
        AbilityComponent abilities, AbilityCastContext context, UnitEntity caster, long tick)
    {
        var elapsed = tick - context.CastStartTime;
        var totalCastTime = context.CastTime + context.SetbackAccumulator;

        // 60% range re-check (V1 feature — catches target moving out of range mid-cast)
        if (!abilities.RangeCheckDone && elapsed >= totalCastTime * 0.6f)
        {
            abilities.RangeCheckDone = true;
            if (context.Target is not null && context.Range > 0)
            {
                var rangeFailure = CheckRange(caster, context.Target, context.Range, context.MinRange);
                if (rangeFailure.HasValue)
                {
                    CancelCast(abilities, rangeFailure.Value);
                    return;
                }
            }
        }

        // Cast completion
        if (elapsed >= totalCastTime)
            CompleteCast(abilities, context, tick);
    }

    private void UpdateChanneling(
        AbilityComponent abilities, AbilityCastContext context, UnitEntity caster, long tick)
    {
        // Check target alive
        if (context.Target is not null && context.Target.Health.IsDead)
        {
            CancelCast(abilities, AbilityFailure.TargetDead);
            return;
        }

        // Channel duration complete
        if (tick - context.CastStartTime >= context.CastTime)
        {
            abilities.ClearCast();
            return;
        }

        // Channel tick
        if (tick >= abilities.NextChannelTick)
        {
            // Consume AP per tick (V1 behavior)
            if (context.ApCost > 0 && caster.ActionPoints < (int)context.ApCost)
            {
                CancelCast(abilities, AbilityFailure.NotEnoughAp);
                return;
            }

            if (context.ApCost > 0)
                caster.ActionPoints = Math.Max(0, caster.ActionPoints - (int)context.ApCost);

            // Range re-check per tick
            if (context.Target is not null && context.Range > 0)
            {
                var rangeFailure = CheckRange(caster, context.Target, context.Range, context.MinRange);
                if (rangeFailure.HasValue)
                {
                    CancelCast(abilities, rangeFailure.Value);
                    return;
                }
            }

            // Apply channel effects
            ExecuteCommands(context, caster, context.Target);

            var interval = context.Definition.ChannelInterval > 0
                ? context.Definition.ChannelInterval
                : 1000;
            abilities.NextChannelTick += interval;
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  VALIDATION HELPERS
    // ═══════════════════════════════════════════════════════════════════

    private static AbilityFailure? CheckCrowdControl(UnitEntity caster, AbilityDefinition definition)
    {
        var cc = caster.Buffs.GetActiveCrowdControl();
        if (cc == CrowdControlFlags.None)
            return null;

        // Hard CC — blocks all casts
        if ((cc & CrowdControlFlags.Disabled) != 0)
            return AbilityFailure.Knockdown;

        // Silence blocks verbal (magic) abilities
        if ((cc & CrowdControlFlags.Silence) != 0 && definition.AbilityType == AbilityType.Verbal)
            return AbilityFailure.Silenced;

        // Disarm blocks melee and ranged abilities
        if ((cc & CrowdControlFlags.Disarm) != 0
            && definition.AbilityType is AbilityType.Melee or AbilityType.Ranged)
            return AbilityFailure.Disarmed;

        return null;
    }

    private static AbilityFailure? CheckRange(
        UnitEntity caster, UnitEntity target, float rangeFeet, float minRangeFeet)
    {
        long distSq = caster.Position.DistanceSquared2D(target.Position);

        if (rangeFeet > 0)
        {
            long rangeUnits = (long)(rangeFeet * RegionConstants.UnitsPerFoot);
            if (distSq > rangeUnits * rangeUnits)
                return AbilityFailure.OutOfRange;
        }

        if (minRangeFeet > 0)
        {
            long minRangeUnits = (long)(minRangeFeet * RegionConstants.UnitsPerFoot);
            if (distSq < minRangeUnits * minRangeUnits)
                return AbilityFailure.TooClose;
        }

        return null;
    }

    private static bool RequiresTarget(AbilityDefinition definition)
    {
        return definition.TargetType is CommandTargetType.Enemy
            or CommandTargetType.Ally
            or CommandTargetType.AllyOrSelf
            or CommandTargetType.CareerTarget
            or CommandTargetType.AllyOrCareerTarget;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  MODIFIER APPLICATION
    // ═══════════════════════════════════════════════════════════════════

    private void ApplyModifiers(AbilityCastContext context, ModifierStage stage)
    {
        var modifiers = context.Definition.Modifiers;
        for (var i = 0; i < modifiers.Count; i++)
        {
            if (modifiers[i].Stage == stage)
                ModifierApplicator.ApplyDefinition(context, modifiers[i], ConditionEvaluator);
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  EFFECT EXECUTION
    // ═══════════════════════════════════════════════════════════════════

    private void ExecuteCommands(AbilityCastContext context, UnitEntity caster, UnitEntity? target)
    {
        var commands = context.Definition.Commands;
        for (var i = 0; i < commands.Count; i++)
        {
            var cmd = commands[i];
            if (cmd.NoAutoUse)
                continue;

            var effectTarget = ResolveCommandTarget(cmd.TargetType, caster, target);
            ExecuteCommand(context, cmd, caster, effectTarget);

            // Chained sub-commands
            for (var j = 0; j < cmd.ChainedCommands.Count; j++)
                ExecuteCommand(context, cmd.ChainedCommands[j], caster, effectTarget);
        }
    }

    private void ExecuteCommand(
        AbilityCastContext context, AbilityCommandDefinition cmd,
        UnitEntity caster, UnitEntity? target)
    {
        switch (cmd.EffectType)
        {
            case AbilityEffectType.DealDamage:
                if (target is not null && cmd.Damage is not null)
                    ExecuteDealDamage(context, cmd, caster, target);
                break;

            case AbilityEffectType.StealLife:
                if (target is not null && cmd.Damage is not null)
                    ExecuteStealLife(context, cmd, caster, target);
                break;

            case AbilityEffectType.InvokeBuff:
                // Stub: buff invocation requires BuffDefinition lookup (wired in Step 6).
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
        AbilityCastContext context, AbilityCommandDefinition cmd,
        UnitEntity caster, UnitEntity target)
    {
        var dmgCtx = BuildDamageContext(context, cmd.Damage!, caster, target);

        // Resolve runs all pure-math stages. When buff events land (Step 6),
        // the caller will inject NotifyCombatEvent calls between stages.
        DamagePipeline.Resolve(dmgCtx);

        target.Health.TakeDamage(dmgCtx.FinalDamage);
    }

    private void ExecuteStealLife(
        AbilityCastContext context, AbilityCommandDefinition cmd,
        UnitEntity caster, UnitEntity target)
    {
        var dmgCtx = BuildDamageContext(context, cmd.Damage!, caster, target);
        DamagePipeline.Resolve(dmgCtx);

        var dealt = target.Health.TakeDamage(dmgCtx.FinalDamage);
        caster.Health.Heal(dealt);
    }

    private static void ExecuteModifyActionPoints(AbilityCommandDefinition cmd, UnitEntity target)
    {
        target.ActionPoints = Math.Max(0, target.ActionPoints + cmd.PrimaryValue);
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
    //  COOLDOWN
    // ═══════════════════════════════════════════════════════════════════

    private static void ApplyCooldown(
        AbilityComponent abilities, AbilityDefinition definition,
        AbilityCastContext context, long tick)
    {
        var cooldownMs = (int)context.Cooldown;
        if (cooldownMs <= 0)
            return;

        // Enforce cooldown cap (prevents stacking cooldown reduction to zero)
        if (definition.CooldownCap > 0 && cooldownMs < definition.CooldownCap)
            cooldownMs = definition.CooldownCap;

        var cdEntry = definition.CooldownEntry != 0 ? definition.CooldownEntry : definition.Entry;
        abilities.SetCooldown(cdEntry, tick, cooldownMs);
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
