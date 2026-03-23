namespace WorldServerV2.World.Combat;

/// <summary>
/// Unified damage pipeline — replaces V1's five duplicate paths in CombatManager.
/// All stages are pure math operating on <see cref="DamageContext"/>. The context
/// carries pre-resolved stat snapshots and pre-rolled random values, making every
/// method deterministic and independently testable.
/// <para>
/// <b>Stage ordering</b> matches the §11.3 Mermaid diagram. Stages that require
/// entity interaction (buff events, guard, HP application, kill check) are hook
/// points invoked by the caller (<c>AbilityCastService</c>, Step 5) between
/// the math stages. This class only implements the math.
/// </para>
/// </summary>
public static class DamagePipeline
{
    // ── Constants (preserved from V1 CombatManager) ──────────────────

    /// <summary>Off-hand damage penalty (90% of normal).</summary>
    public const float OffhandDamagePenalty = 0.9f;

    /// <summary>Off-hand stat coefficient.</summary>
    public const float OffhandStatCoefficient = 0.05f;

    /// <summary>Base crit multiplier floor (before random variance).</summary>
    public const float BaseCritMultiplier = 1.35f;

    /// <summary>Maximum armor/resistance mitigation percentage.</summary>
    public const float MaxMitigationFraction = 0.75f;

    /// <summary>Resistance soft-cap threshold (40% mitigation).</summary>
    public const float ResistSoftCapThreshold = 0.40f;

    /// <summary>Cap on block chance from rating (%).</summary>
    public const float BlockRatingCap = 50f;

    /// <summary>Cap on parry/evade/disrupt chance from rating (%).</summary>
    public const float SecondaryDefenseCap = 25f;

    /// <summary>AoE damage penalty vs pets.</summary>
    public const float AoePetPenalty = 0.5f;

    // ═══════════════════════════════════════════════════════════════════
    //  PUBLIC ENTRY POINT
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Runs the pure-math stages of the damage pipeline. The caller is responsible
    /// for buff event notifications (DealingDamage/ReceivingDamage, shield pass,
    /// guard check, DealtDamage/ReceivedDamage) between the appropriate stages.
    /// <para>
    /// Call order for a standard ability hit:
    /// <code>
    /// DamagePipeline.Resolve(ctx);           // stages 1–5  (base → stat → toughness)
    /// // caller: notify DealingDamage / ReceivingDamage (buff mods mutate ctx)
    /// // caller: notify ShieldPass (absorb shields reduce ctx.Damage)
    /// DamagePipeline.ApplyCriticalHit(ctx);  // stage 9
    /// DamagePipeline.ApplyArmorOrResist(ctx); // stage 10
    /// DamagePipeline.ApplyPercentageModifiers(ctx); // stage 11
    /// // caller: guard split check
    /// DamagePipeline.ApplyModifiers(ctx);    // stage 13
    /// DamagePipeline.ApplyAoePetPenalty(ctx); // stage 14
    /// DamagePipeline.Finalize(ctx);          // write result
    /// </code>
    /// </para>
    /// </summary>
    public static void Resolve(DamageContext ctx)
    {
        if (ctx.IsPrecalculated)
        {
            ResolvePrecalculated(ctx);
            return;
        }

        // Stage 3: Defense roll (before any damage computation)
        if (!ctx.Undefendable && !ctx.IsProc
            && ctx.DamageType != DamageType.RawDamage
            && ctx.DamageType != DamageType.RawHealing)
        {
            if (CheckDefense(ctx))
            {
                ctx.WasDefended = true;
                return; // fully defended — zero damage
            }
        }

        // Stage 4: Base damage
        ComputeBaseDamage(ctx);

        // Stage 4b: Weapon DPS contribution
        AddWeaponDamage(ctx);

        // Stage 5: Stat scaling + toughness
        if (ctx.AttackerPrimaryStat > 0 || ctx.PriStatMultiplier > 0)
        {
            AddStatScaling(ctx);
            SubtractToughness(ctx);
        }

        // Stages 6–8 are caller-driven: DealingDamage/ReceivingDamage events, shield pass

        // Stage 9: Critical hit
        ApplyCriticalHit(ctx);

        // Stage 10: Armor or resistance
        ApplyArmorOrResist(ctx);

        // Stage 11: Percentage multipliers
        ApplyPercentageModifiers(ctx);

        // Stage 12: Guard split is caller-driven (reads BuffContainer)

        // Stage 13: Apply accumulated damage modifiers
        ApplyModifiers(ctx);

        // Stage 14: AoE pet penalty
        ApplyAoePetPenalty(ctx);

        // Stage 15–16: Finalize
        Finalize(ctx);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  STAGE IMPLEMENTATIONS (internal for testability)
    // ═══════════════════════════════════════════════════════════════════

    // ── Stage 3: Defense ─────────────────────────────────────────────

    /// <summary>
    /// Checks whether the attack is fully defended (block/parry/evade/disrupt).
    /// Returns <c>true</c> if defended, and sets <see cref="DamageContext.DefenseType"/>.
    /// </summary>
    internal static bool CheckDefense(DamageContext ctx)
    {
        int roll = ctx.DefenseRoll;

        // Block: requires shield + frontal arc + not magic
        bool canBlock = ctx.TargetHasShield
                        && ctx.TargetIsFacing
                        && ctx.DamageType == DamageType.Physical;

        if (canBlock)
        {
            float blockChance = ComputeBlockChance(
                ctx.TargetBlockRating,
                Math.Max(ctx.AttackerPrimaryStat, ctx.AttackerLevel * 10),
                ctx.Defensibility,
                ctx.TargetBlock,
                ctx.AttackerBlockStrikethrough);

            if (roll <= (int)blockChance)
            {
                ctx.DefenseType = DefenseType.Block;
                return true;
            }
        }

        // Secondary defense (parry for melee, evade for ranged, disrupt for magic)
        float defChance = ComputeSecondaryDefenseChance(
            ctx.TargetDefenseRating,
            Math.Max(ctx.AttackerPrimaryStat, ctx.AttackerLevel * 10),
            ctx.Defensibility,
            ctx.TargetDefense,
            ctx.AttackerDefStrikethrough);

        if (roll <= (int)defChance)
        {
            ctx.DefenseType = ctx.DamageType == DamageType.Physical
                ? DefenseType.Parry
                : DefenseType.Evade; // ranged or magic — caller sets context appropriately
            return true;
        }

        ctx.DefenseType = DefenseType.None;
        return false;
    }

    /// <summary>
    /// Computes block chance percentage.
    /// <c>block = (blockRating / offensiveStat) × 0.2 × 100</c>, capped at 50%.
    /// </summary>
    internal static float ComputeBlockChance(
        int blockRating, int offensiveStat, int defensibility,
        int flatBlockBonus, int blockStrikethrough)
    {
        if (offensiveStat <= 0) offensiveStat = 1;
        float chance = (float)blockRating / offensiveStat * 0.2f * 100f;
        if (chance > BlockRatingCap)
            chance = BlockRatingCap;
        chance += defensibility + flatBlockBonus - blockStrikethrough;
        return chance;
    }

    /// <summary>
    /// Computes parry/evade/disrupt chance percentage.
    /// <c>defense = (defensiveStat / offensiveStat) × 0.075 × 100</c>, capped at 25%.
    /// </summary>
    internal static float ComputeSecondaryDefenseChance(
        int defensiveRating, int offensiveStat, int defensibility,
        int flatDefenseBonus, int defenseStrikethrough)
    {
        if (offensiveStat <= 0) offensiveStat = 1;
        float chance = (float)defensiveRating / offensiveStat * 0.075f * 100f;
        if (chance > SecondaryDefenseCap)
            chance = SecondaryDefenseCap;
        chance += defensibility + flatDefenseBonus - defenseStrikethrough;
        return chance;
    }

    // ── Stage 4: Base Damage ─────────────────────────────────────────

    /// <summary>
    /// Computes level-scaled base damage with optional variance.
    /// <c>damage = min + (max − min) × ((level − 1) / 39)</c>
    /// </summary>
    internal static void ComputeBaseDamage(DamageContext ctx)
    {
        float damage = GetDamageForLevel(
            ctx.MinDamage, ctx.MaxDamage, ctx.AttackerLevel);

        if (ctx.DamageVariance > 0)
        {
            float variancePct = ctx.DamageVarianceRoll * ctx.DamageVariance * 0.01f;
            damage *= 1f + variancePct;
        }

        ctx.Damage = damage;
    }

    /// <summary>
    /// Level-interpolated damage: <c>min + (max − min) × ((level − 1) / 39)</c>.
    /// </summary>
    internal static float GetDamageForLevel(ushort minDamage, ushort maxDamage, byte level)
    {
        return minDamage + (maxDamage - minDamage) * ((level - 1) / 39.0f);
    }

    /// <summary>
    /// Level-interpolated armor penetration: <c>min + (max − min) × ((level − 1) / 39)</c>.
    /// </summary>
    internal static float GetArmorPenForLevel(ushort minPen, ushort maxPen, byte level)
    {
        return minPen + (maxPen - minPen) * ((level - 1) / 39.0f);
    }

    // ── Stage 4b: Weapon DPS ─────────────────────────────────────────

    /// <summary>
    /// Adds weapon DPS contribution to damage. Skipped for procs and precalculated.
    /// </summary>
    internal static void AddWeaponDamage(DamageContext ctx)
    {
        if (ctx.IsProc || ctx.WeaponDps == 0)
            return;

        if (ctx.IsAutoAttack)
        {
            // Auto-attack: weapon DPS IS the base damage
            ctx.Damage = ctx.WeaponDps * ctx.CastTimeDamageMult;
        }
        else if (ctx.PriStatMultiplier > 0)
        {
            ctx.Damage += ctx.WeaponDps * ctx.PriStatMultiplier;
        }
        else
        {
            ctx.Damage += ctx.WeaponDps * ctx.WeaponDamageScale * ctx.CastTimeDamageMult;
        }
    }

    // ── Stage 5: Stat Scaling ────────────────────────────────────────

    /// <summary>
    /// Adds offensive stat contribution to damage.
    /// Uses soft/hard cap system: soft = 50 + 25×level, hard = 50 + 55×level.
    /// Power stats bypass caps.
    /// </summary>
    internal static void AddStatScaling(DamageContext ctx)
    {
        float stat = ApplySoftHardCap(ctx.AttackerPrimaryStat, ctx.AttackerLevel);

        // Power stats bypass caps — added directly
        stat += ctx.AttackerPowerStat;

        if (ctx.PriStatMultiplier > 0)
        {
            ctx.Damage += (stat / 5f) * ctx.PriStatMultiplier;
        }
        else
        {
            ctx.Damage += stat * ctx.StatCoefficient * ctx.StatDamageScale * ctx.CastTimeDamageMult;
        }
    }

    /// <summary>
    /// Subtracts toughness mitigation from damage. Mitigation can never fully
    /// negate damage — minimum 1 damage is always dealt.
    /// </summary>
    internal static void SubtractToughness(DamageContext ctx)
    {
        float toughness = ApplySoftHardCap(ctx.TargetToughness, ctx.AttackerLevel);

        float mitigation;
        if (ctx.PriStatMultiplier > 0)
        {
            mitigation = (toughness / 5f) * ctx.PriStatMultiplier;
        }
        else
        {
            mitigation = toughness * ctx.StatCoefficient * ctx.StatDamageScale * ctx.CastTimeDamageMult;
        }

        if (mitigation >= ctx.Damage)
        {
            ctx.Mitigation += ctx.Damage - 1;
            ctx.Damage = 1;
        }
        else
        {
            ctx.Mitigation += mitigation;
            ctx.Damage -= mitigation;
        }
    }

    /// <summary>
    /// Applies the soft/hard cap formula to a stat value.
    /// <list type="bullet">
    ///   <item>Soft cap = 50 + 25 × level (1050 at L40)</item>
    ///   <item>Hard cap = 50 + 55 × level (2250 at L40)</item>
    ///   <item>Between soft and hard: effective = softcap + (stat − softcap) / 3</item>
    ///   <item>Above hard: clamped to hardcap</item>
    /// </list>
    /// </summary>
    internal static float ApplySoftHardCap(int stat, byte level)
    {
        uint softcap = (uint)(50 + 25 * level);
        uint hardcap = (uint)(50 + 55 * level);

        if (stat > hardcap)
            return hardcap;
        if (stat > softcap)
            return softcap + (stat - softcap) / 3f;
        return stat;
    }

    // ── Stage 9: Critical Hit ────────────────────────────────────────

    /// <summary>
    /// Evaluates critical hit chance and applies crit multiplier to damage/mitigation.
    /// Skipped for procs, NoCrits, or RawDamage.
    /// </summary>
    internal static void ApplyCriticalHit(DamageContext ctx)
    {
        if (ctx.NoCrits || ctx.IsProc || ctx.DamageType == DamageType.RawDamage)
            return;

        float critChance = ComputeCritChance(
            ctx.AttackerLevel, ctx.TargetInitiative,
            ctx.BaseCritRate, ctx.AttackerCritRate, ctx.AttackerTypeCritRate,
            ctx.TargetCritReduction);

        if (ctx.CritRoll <= (int)critChance)
        {
            float multiplier = BaseCritMultiplier
                               + ctx.CritVarianceRoll
                               + ctx.BaseCritDamageBonus
                               + ctx.AttackerCritDamage * 0.01f
                               + ctx.TargetCritDamageTaken * 0.01f;

            ctx.Damage *= multiplier;
            ctx.Mitigation *= multiplier;
            ctx.WasCritical = true;
            ctx.CritMultiplier = multiplier;
        }
    }

    /// <summary>
    /// Computes critical hit chance percentage.
    /// <c>base = ((level × 7.5 + 50) / 10) / targetInitiative × 100</c>
    /// </summary>
    internal static float ComputeCritChance(
        byte attackerLevel, int targetInitiative,
        int baseCritRate, int attackerCritRate, int typeCritRate,
        int targetCritReduction)
    {
        if (targetInitiative <= 0) targetInitiative = 1;
        float chance = (attackerLevel * 7.5f + 50f) / 10f / targetInitiative * 100f;
        chance += baseCritRate + attackerCritRate + typeCritRate - targetCritReduction;
        return chance;
    }

    // ── Stage 10: Armor / Resistance ─────────────────────────────────

    /// <summary>
    /// Applies armor reduction (physical) or resistance reduction (magical) to damage.
    /// </summary>
    internal static void ApplyArmorOrResist(DamageContext ctx)
    {
        if (ctx.DamageType == DamageType.RawDamage || ctx.DamageType == DamageType.Healing
            || ctx.DamageType == DamageType.RawHealing)
            return;

        if (ctx.DamageType == DamageType.Physical)
            ApplyArmorReduction(ctx);
        else
            ApplyResistanceReduction(ctx);
    }

    /// <summary>
    /// Physical armor mitigation.
    /// <code>
    /// flatPen = level-interpolated armor pen from ability
    /// armorMit = (Armor − flatPen) / (level × 44) × 0.4
    /// pen = min(1, WeaponSkill / (7.5 × level + 50) × 0.25 + bonusPen)
    /// finalMit = armorMit × (1 − pen) × (1 − ArmorResistPenFactor), capped at 75%
    /// </code>
    /// </summary>
    internal static void ApplyArmorReduction(DamageContext ctx)
    {
        // Penetration percentage from weapon skill + bonus
        float penFromSkill = ctx.AttackerWeaponSkill / (7.5f * ctx.AttackerLevel + 50f) * 0.25f;
        float pen = Math.Min(1f, penFromSkill + ctx.AttackerArmorPenPct);

        // Target's pen resistance
        if (ctx.TargetArmorPenReduction > pen)
            pen = 0;
        else
            pen -= ctx.TargetArmorPenReduction;

        // Flat armor pen from ability
        float flatPen = GetArmorPenForLevel(ctx.MinArmorPen, ctx.MaxArmorPen, ctx.AttackerLevel);
        float effectiveArmor = ctx.TargetArmor - flatPen;

        float mitFraction;
        if (effectiveArmor <= 0)
        {
            mitFraction = 0;
        }
        else
        {
            mitFraction = effectiveArmor / (ctx.AttackerLevel * 44f) * 0.4f;
            mitFraction *= 1f - pen;
            mitFraction *= 1f - ctx.ArmorResistPenFactor;
            if (mitFraction > MaxMitigationFraction)
                mitFraction = MaxMitigationFraction;
        }

        float reduction = ctx.Damage * mitFraction;
        ctx.Mitigation += reduction;
        ctx.Damage -= reduction;
    }

    /// <summary>
    /// Magical resistance mitigation (Spirit/Elemental/Corporeal).
    /// Two-tier formula with soft cap at 40%:
    /// <code>
    /// base = Resistance / (level × 8.4) × 0.2
    /// if base &gt; 0.4: effective = (base − 0.4) / 3 + 0.4
    /// final = effective × (1 − ArmorResistPenFactor), capped at 75%
    /// </code>
    /// </summary>
    internal static void ApplyResistanceReduction(DamageContext ctx)
    {
        if (ctx.TargetResistance <= 0)
            return;

        float mitFraction = ctx.TargetResistance / (ctx.AttackerLevel * 8.4f) * 0.2f;

        // Soft cap at 40%
        if (mitFraction > ResistSoftCapThreshold)
            mitFraction = (mitFraction - ResistSoftCapThreshold) / 3f + ResistSoftCapThreshold;

        mitFraction *= 1f - ctx.ArmorResistPenFactor;

        if (mitFraction > MaxMitigationFraction)
            mitFraction = MaxMitigationFraction;

        if (mitFraction <= 0)
            return;

        float reduction = ctx.Damage * mitFraction;
        ctx.Mitigation += reduction;
        ctx.Damage -= reduction;
    }

    // ── Stage 11: Percentage Multipliers ─────────────────────────────

    /// <summary>
    /// Applies type-specific and general percentage damage modifiers from stats.
    /// Accumulates into <see cref="DamageContext.DamageBonus"/> /
    /// <see cref="DamageContext.DamageReduction"/> for application in
    /// <see cref="ApplyModifiers"/>.
    /// </summary>
    internal static void ApplyPercentageModifiers(DamageContext ctx)
    {
        if (ctx.IsProc)
            return; // Procs skip percentage multipliers (V1 behavior)

        // Type-specific power (melee/magic)
        float bonus = ctx.AttackerTypePowerBonus + ctx.TargetInTypeDmgBonus;
        float reduction = ctx.AttackerTypePowerReduction * ctx.TargetInTypeDmgReduction;

        // General outgoing/incoming
        bonus += ctx.AttackerOutDmgBonus + ctx.TargetInDmgBonus;
        reduction *= ctx.AttackerOutDmgReduction * ctx.TargetInDmgReduction;

        ctx.DamageBonus += bonus;
        ctx.DamageReduction *= reduction;
    }

    // ── Stage 13: Apply Accumulated Modifiers ────────────────────────

    /// <summary>
    /// Applies accumulated <see cref="DamageContext.DamageBonus"/> and
    /// <see cref="DamageContext.DamageReduction"/> to damage and mitigation.
    /// Resets both to default (1.0) after application.
    /// </summary>
    internal static void ApplyModifiers(DamageContext ctx)
    {
        if (Math.Abs(ctx.DamageReduction - ctx.DamageBonus) < 1e-6f
            && Math.Abs(ctx.DamageBonus - 1f) < 1e-6f)
            return; // no modification

        float factor = ctx.DamageBonus * ctx.DamageReduction;
        ctx.Damage *= factor;
        ctx.Mitigation *= factor;

        ctx.DamageBonus = 1f;
        ctx.DamageReduction = 1f;
    }

    // ── Stage 14: AoE Pet Penalty ────────────────────────────────────

    /// <summary>
    /// Applies 50% damage reduction for AoE damage hitting pets.
    /// </summary>
    internal static void ApplyAoePetPenalty(DamageContext ctx)
    {
        if (ctx.IsAoE && ctx.TargetIsPet)
        {
            ctx.Damage *= AoePetPenalty;
            ctx.Mitigation *= AoePetPenalty;
        }
    }

    // ── Stage 15–16: Finalize ────────────────────────────────────────

    /// <summary>
    /// Writes final result values from the running pipeline state.
    /// Clamps damage to a minimum of 0.
    /// </summary>
    internal static void Finalize(DamageContext ctx)
    {
        ctx.FinalDamage = (uint)Math.Max(0, (int)ctx.Damage);
        ctx.FinalMitigation = (uint)Math.Max(0, (int)ctx.Mitigation);
        ctx.FinalAbsorption = (uint)Math.Max(0, (int)ctx.Absorption);
    }

    // ── Precalculated path (DoT/HoT ticks) ──────────────────────────

    /// <summary>
    /// Resolves a precalculated damage tick. Base damage and mitigation were
    /// pre-computed at cast time; each tick applies the fraction multiplier,
    /// then crit/percentage/guard stages run normally.
    /// </summary>
    private static void ResolvePrecalculated(DamageContext ctx)
    {
        ctx.Damage = ctx.PrecalcDamage * ctx.PrecalcMultiplier;
        ctx.Mitigation = ctx.PrecalcMitigation * ctx.PrecalcMultiplier;

        // Precalculated still allows crit per tick
        ApplyCriticalHit(ctx);

        // Percentage multipliers (general only, no type-specific for precalc)
        // V1: applies OutgoingDamagePercent + IncomingDamagePercent but not type power
        float bonus = ctx.AttackerOutDmgBonus + ctx.TargetInDmgBonus;
        float reduction = ctx.AttackerOutDmgReduction * ctx.TargetInDmgReduction;
        ctx.DamageBonus += bonus;
        ctx.DamageReduction *= reduction;

        ApplyModifiers(ctx);
        Finalize(ctx);
    }
}
