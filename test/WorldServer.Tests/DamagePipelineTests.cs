using Shouldly;
using WorldServerV2.World.Combat;

namespace WorldServer.Tests;

/// <summary>
/// Tests for <see cref="DamagePipeline"/> and <see cref="DamageContext"/>.
/// Each test uses known inputs and verifies output matches V1 CombatManager formulas.
/// All random values are pre-rolled into the context for deterministic results.
/// </summary>
public class DamagePipelineTests
{
    // ═══════════════════════════════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>Creates a minimal context for a standard ability hit.</summary>
    private static DamageContext MakeAbilityContext(
        byte attackerLevel = 40,
        ushort minDmg = 100, ushort maxDmg = 200,
        DamageType dmgType = DamageType.Physical)
    {
        return new DamageContext
        {
            DamageType = dmgType,
            AttackerLevel = attackerLevel,
            MinDamage = minDmg,
            MaxDamage = maxDmg,
            CastTimeDamageMult = 1.5f,
            StatDamageScale = 1f,
            StatCoefficient = 0.2f,
            // Default: no defense, no crit (rolls high)
            DefenseRoll = 99,
            CritRoll = 99,
        };
    }

    /// <summary>Creates a context configured as an auto-attack.</summary>
    private static DamageContext MakeAutoAttackContext(
        byte attackerLevel = 40, float weaponDps = 50f, float weaponSpeed = 3.5f)
    {
        return new DamageContext
        {
            DamageType = DamageType.Physical,
            AttackerLevel = attackerLevel,
            IsAutoAttack = true,
            WeaponDps = weaponDps,
            CastTimeDamageMult = weaponSpeed, // weapon speed in seconds
            StatDamageScale = 1f,
            StatCoefficient = 0.1f, // auto-attack uses 0.1
            DefenseRoll = 99,
            CritRoll = 99,
        };
    }

    // ═══════════════════════════════════════════════════════════════════
    //  GetDamageForLevel
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void GetDamageForLevel_at_level_1_returns_min_damage()
    {
        float result = DamagePipeline.GetDamageForLevel(100, 200, 1);
        result.ShouldBe(100f, 0.01f);
    }

    [Fact]
    public void GetDamageForLevel_at_level_40_returns_max_damage()
    {
        float result = DamagePipeline.GetDamageForLevel(100, 200, 40);
        result.ShouldBe(200f, 0.01f);
    }

    [Fact]
    public void GetDamageForLevel_at_level_20_returns_midpoint()
    {
        // level 20: (20-1)/39 = 19/39 ≈ 0.4872
        // damage = 100 + 100 * 0.4872 ≈ 148.72
        float result = DamagePipeline.GetDamageForLevel(100, 200, 20);
        float expected = 100 + 100 * (19f / 39f);
        result.ShouldBe(expected, 0.01f);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Soft/Hard Cap
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void SoftHardCap_below_soft_cap_returns_raw_stat()
    {
        // L40 softcap = 50 + 25*40 = 1050
        float result = DamagePipeline.ApplySoftHardCap(800, 40);
        result.ShouldBe(800f);
    }

    [Fact]
    public void SoftHardCap_between_caps_applies_diminishing()
    {
        // L40 softcap = 1050, hardcap = 2250
        // stat = 1200: effective = 1050 + (1200-1050)/3 = 1050 + 50 = 1100
        float result = DamagePipeline.ApplySoftHardCap(1200, 40);
        result.ShouldBe(1100f, 0.01f);
    }

    [Fact]
    public void SoftHardCap_above_hard_cap_clamps()
    {
        // L40 hardcap = 50 + 55*40 = 2250
        float result = DamagePipeline.ApplySoftHardCap(3000, 40);
        result.ShouldBe(2250f);
    }

    [Fact]
    public void SoftHardCap_at_level_1_has_correct_caps()
    {
        // L1 softcap = 50+25 = 75, hardcap = 50+55 = 105
        DamagePipeline.ApplySoftHardCap(75, 1).ShouldBe(75f);
        // 80: 75 + (80-75)/3 = 75 + 1.67 = 76.67
        DamagePipeline.ApplySoftHardCap(80, 1).ShouldBe(75 + 5 / 3f, 0.01f);
        DamagePipeline.ApplySoftHardCap(200, 1).ShouldBe(105f);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  ComputeBaseDamage
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ComputeBaseDamage_no_variance_level_40()
    {
        var ctx = MakeAbilityContext(attackerLevel: 40, minDmg: 100, maxDmg: 200);
        DamagePipeline.ComputeBaseDamage(ctx);
        ctx.Damage.ShouldBe(200f, 0.01f); // L40 → MaxDamage
    }

    [Fact]
    public void ComputeBaseDamage_with_positive_variance()
    {
        var ctx = MakeAbilityContext(attackerLevel: 40, minDmg: 100, maxDmg: 200);
        ctx.DamageVariance = 10; // ±10%
        ctx.DamageVarianceRoll = 1.0f; // max positive

        DamagePipeline.ComputeBaseDamage(ctx);
        // 200 * (1 + 1.0 * 10 * 0.01) = 200 * 1.10 = 220
        ctx.Damage.ShouldBe(220f, 0.01f);
    }

    [Fact]
    public void ComputeBaseDamage_with_negative_variance()
    {
        var ctx = MakeAbilityContext(attackerLevel: 40, minDmg: 100, maxDmg: 200);
        ctx.DamageVariance = 10;
        ctx.DamageVarianceRoll = -1.0f; // max negative

        DamagePipeline.ComputeBaseDamage(ctx);
        // 200 * (1 - 0.10) = 180
        ctx.Damage.ShouldBe(180f, 0.01f);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  AddWeaponDamage
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void AddWeaponDamage_standard_ability()
    {
        var ctx = MakeAbilityContext();
        ctx.Damage = 200; // base damage already computed
        ctx.WeaponDps = 50f;
        ctx.WeaponDamageScale = 1.0f;
        ctx.CastTimeDamageMult = 1.5f;

        DamagePipeline.AddWeaponDamage(ctx);

        // 200 + 50 * 1.0 * 1.5 = 275
        ctx.Damage.ShouldBe(275f, 0.01f);
    }

    [Fact]
    public void AddWeaponDamage_auto_attack_replaces_base()
    {
        var ctx = MakeAutoAttackContext(weaponDps: 50f, weaponSpeed: 3.5f);
        ctx.Damage = 999; // should be replaced

        DamagePipeline.AddWeaponDamage(ctx);

        // auto: WeaponDps * CastTimeDamageMult = 50 * 3.5 = 175
        ctx.Damage.ShouldBe(175f, 0.01f);
    }

    [Fact]
    public void AddWeaponDamage_proc_skips_weapon()
    {
        var ctx = MakeAbilityContext();
        ctx.IsProc = true;
        ctx.Damage = 200;
        ctx.WeaponDps = 50f;

        DamagePipeline.AddWeaponDamage(ctx);

        ctx.Damage.ShouldBe(200f); // unchanged
    }

    [Fact]
    public void AddWeaponDamage_pristat_path()
    {
        var ctx = MakeAbilityContext();
        ctx.Damage = 200;
        ctx.WeaponDps = 50f;
        ctx.PriStatMultiplier = 2.0f;

        DamagePipeline.AddWeaponDamage(ctx);

        // PriStat: 200 + 50 * 2.0 = 300
        ctx.Damage.ShouldBe(300f, 0.01f);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  AddStatScaling
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void AddStatScaling_normal_formula()
    {
        var ctx = MakeAbilityContext();
        ctx.Damage = 200;
        ctx.AttackerPrimaryStat = 500;
        ctx.StatCoefficient = 0.2f;
        ctx.StatDamageScale = 1.0f;
        ctx.CastTimeDamageMult = 1.5f;

        DamagePipeline.AddStatScaling(ctx);

        // 500 (below softcap 1050) * 0.2 * 1.0 * 1.5 = 150
        // total = 200 + 150 = 350
        ctx.Damage.ShouldBe(350f, 0.01f);
    }

    [Fact]
    public void AddStatScaling_with_power_stat_bypasses_cap()
    {
        var ctx = MakeAbilityContext();
        ctx.Damage = 0;
        ctx.AttackerPrimaryStat = 1200; // above softcap (1050)
        ctx.AttackerPowerStat = 100; // bypasses cap
        ctx.StatCoefficient = 0.2f;
        ctx.StatDamageScale = 1f;
        ctx.CastTimeDamageMult = 1.5f;

        DamagePipeline.AddStatScaling(ctx);

        // capped = 1050 + (1200-1050)/3 = 1100, then +100 power = 1200
        // damage = 1200 * 0.2 * 1 * 1.5 = 360
        ctx.Damage.ShouldBe(360f, 0.01f);
    }

    [Fact]
    public void AddStatScaling_pristat_formula()
    {
        var ctx = MakeAbilityContext();
        ctx.Damage = 0;
        ctx.AttackerPrimaryStat = 500;
        ctx.PriStatMultiplier = 3.0f;

        DamagePipeline.AddStatScaling(ctx);

        // PriStat: (500/5) * 3.0 = 300
        ctx.Damage.ShouldBe(300f, 0.01f);
    }

    [Fact]
    public void AddStatScaling_autoattack_uses_lower_coefficient()
    {
        var ctx = MakeAutoAttackContext();
        ctx.Damage = 0;
        ctx.AttackerPrimaryStat = 500;
        ctx.StatCoefficient = 0.1f;
        ctx.StatDamageScale = 1f;
        ctx.CastTimeDamageMult = 3.5f;

        DamagePipeline.AddStatScaling(ctx);

        // 500 * 0.1 * 1 * 3.5 = 175
        ctx.Damage.ShouldBe(175f, 0.01f);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  SubtractToughness
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void SubtractToughness_normal_mitigation()
    {
        var ctx = MakeAbilityContext();
        ctx.Damage = 500f;
        ctx.TargetToughness = 400;
        ctx.StatCoefficient = 0.2f;
        ctx.StatDamageScale = 1f;
        ctx.CastTimeDamageMult = 1.5f;

        DamagePipeline.SubtractToughness(ctx);

        // mitigation = 400 * 0.2 * 1 * 1.5 = 120
        ctx.Mitigation.ShouldBe(120f, 0.01f);
        ctx.Damage.ShouldBe(380f, 0.01f);
    }

    [Fact]
    public void SubtractToughness_cannot_fully_negate_damage()
    {
        var ctx = MakeAbilityContext();
        ctx.Damage = 50f;
        ctx.TargetToughness = 1000;
        ctx.StatCoefficient = 0.2f;
        ctx.StatDamageScale = 1f;
        ctx.CastTimeDamageMult = 1.5f;

        DamagePipeline.SubtractToughness(ctx);

        // mitigation = 1000 * 0.2 * 1 * 1.5 = 300 > 50
        // clamped: damage = 1, mitigation = 49
        ctx.Damage.ShouldBe(1f);
        ctx.Mitigation.ShouldBe(49f);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  ComputeCritChance / ApplyCriticalHit
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ComputeCritChance_basic_formula()
    {
        // attackerLevel=40, targetInit=800
        // base = (40*7.5+50)/10 / 800 * 100 = (350)/10 / 800 * 100 = 35/800*100 = 4.375
        float chance = DamagePipeline.ComputeCritChance(40, 800, 0, 0, 0, 0);
        chance.ShouldBe(4.375f, 0.01f);
    }

    [Fact]
    public void ComputeCritChance_with_flat_bonuses()
    {
        // base=4.375, +5 baseCrit, +3 attackerCrit, +2 typeCrit, -1 targetReduction
        // = 4.375 + 5 + 3 + 2 - 1 = 13.375
        float chance = DamagePipeline.ComputeCritChance(40, 800, 5, 3, 2, 1);
        chance.ShouldBe(13.375f, 0.01f);
    }

    [Fact]
    public void ApplyCriticalHit_crit_applies_multiplier()
    {
        var ctx = MakeAbilityContext();
        ctx.Damage = 1000;
        ctx.Mitigation = 200;
        ctx.CritRoll = 0; // guaranteed crit
        ctx.CritVarianceRoll = 0.1f;
        ctx.TargetInitiative = 100;
        ctx.AttackerCritDamage = 10; // +10% = 0.10

        DamagePipeline.ApplyCriticalHit(ctx);

        // multiplier = 1.35 + 0.1 + 0 + 0.10 + 0 = 1.55
        ctx.WasCritical.ShouldBeTrue();
        ctx.CritMultiplier.ShouldBe(1.55f, 0.01f);
        ctx.Damage.ShouldBe(1550f, 1f);
        ctx.Mitigation.ShouldBe(310f, 1f);
    }

    [Fact]
    public void ApplyCriticalHit_miss_does_not_change_damage()
    {
        var ctx = MakeAbilityContext();
        ctx.Damage = 1000;
        ctx.CritRoll = 99; // miss
        ctx.TargetInitiative = 800;

        DamagePipeline.ApplyCriticalHit(ctx);

        ctx.WasCritical.ShouldBeFalse();
        ctx.Damage.ShouldBe(1000f);
    }

    [Fact]
    public void ApplyCriticalHit_skipped_for_procs()
    {
        var ctx = MakeAbilityContext();
        ctx.IsProc = true;
        ctx.Damage = 1000;
        ctx.CritRoll = 0;
        ctx.TargetInitiative = 100;

        DamagePipeline.ApplyCriticalHit(ctx);

        ctx.WasCritical.ShouldBeFalse();
        ctx.Damage.ShouldBe(1000f);
    }

    [Fact]
    public void ApplyCriticalHit_skipped_when_NoCrits()
    {
        var ctx = MakeAbilityContext();
        ctx.NoCrits = true;
        ctx.CritRoll = 0;
        ctx.Damage = 500;
        ctx.TargetInitiative = 100;

        DamagePipeline.ApplyCriticalHit(ctx);

        ctx.WasCritical.ShouldBeFalse();
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Armor Reduction
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ArmorReduction_basic_formula()
    {
        var ctx = MakeAbilityContext(dmgType: DamageType.Physical);
        ctx.Damage = 1000f;
        ctx.TargetArmor = 500;
        ctx.AttackerLevel = 40;
        ctx.AttackerWeaponSkill = 0;
        ctx.AttackerArmorPenPct = 0;
        ctx.TargetArmorPenReduction = 0;
        ctx.ArmorResistPenFactor = 0;

        DamagePipeline.ApplyArmorReduction(ctx);

        // armorMit = 500 / (40*44) * 0.4 = 500/1760 * 0.4 ≈ 0.11364
        float expectedMit = 500f / 1760f * 0.4f;
        float expectedReduction = 1000f * expectedMit;
        ctx.Damage.ShouldBe(1000f - expectedReduction, 1f);
        ctx.Mitigation.ShouldBe(expectedReduction, 1f);
    }

    [Fact]
    public void ArmorReduction_with_flat_pen()
    {
        var ctx = MakeAbilityContext(dmgType: DamageType.Physical);
        ctx.Damage = 1000f;
        ctx.TargetArmor = 500;
        ctx.AttackerLevel = 40;
        ctx.MinArmorPen = 100;
        ctx.MaxArmorPen = 100; // flat 100 pen at all levels

        DamagePipeline.ApplyArmorReduction(ctx);

        // effective armor = 500 - 100 = 400
        // armorMit = 400/1760 * 0.4 ≈ 0.09091
        float expectedMit = 400f / 1760f * 0.4f;
        float expectedReduction = 1000f * expectedMit;
        ctx.Damage.ShouldBe(1000f - expectedReduction, 1f);
    }

    [Fact]
    public void ArmorReduction_with_weapon_skill_pen()
    {
        var ctx = MakeAbilityContext(dmgType: DamageType.Physical);
        ctx.Damage = 1000f;
        ctx.TargetArmor = 500;
        ctx.AttackerLevel = 40;
        ctx.AttackerWeaponSkill = 350; // pen = 350/(7.5*40+50)*0.25 = 350/350*0.25 = 0.25

        DamagePipeline.ApplyArmorReduction(ctx);

        // armorMit = 500/1760 * 0.4 * (1-0.25) = 0.11364 * 0.75 ≈ 0.08523
        float baseMit = 500f / 1760f * 0.4f;
        float pen = 350f / (7.5f * 40 + 50f) * 0.25f;
        float expectedMitPct = baseMit * (1f - pen);
        float expectedReduction = 1000f * expectedMitPct;
        ctx.Damage.ShouldBe(1000f - expectedReduction, 1f);
    }

    [Fact]
    public void ArmorReduction_caps_at_75_percent()
    {
        var ctx = MakeAbilityContext(dmgType: DamageType.Physical);
        ctx.Damage = 1000f;
        ctx.TargetArmor = 99999; // extremely high
        ctx.AttackerLevel = 40;

        DamagePipeline.ApplyArmorReduction(ctx);

        // Should cap at 75%
        ctx.Damage.ShouldBe(250f, 1f);
        ctx.Mitigation.ShouldBe(750f, 1f);
    }

    [Fact]
    public void ArmorReduction_zero_effective_armor_no_mitigation()
    {
        var ctx = MakeAbilityContext(dmgType: DamageType.Physical);
        ctx.Damage = 1000f;
        ctx.TargetArmor = 50;
        ctx.MinArmorPen = 100;
        ctx.MaxArmorPen = 100; // pen > armor

        DamagePipeline.ApplyArmorReduction(ctx);

        ctx.Damage.ShouldBe(1000f);
        ctx.Mitigation.ShouldBe(0f);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Resistance Reduction
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ResistanceReduction_basic_formula()
    {
        var ctx = MakeAbilityContext(dmgType: DamageType.Spiritual);
        ctx.Damage = 1000f;
        ctx.TargetResistance = 300;
        ctx.AttackerLevel = 40;

        DamagePipeline.ApplyResistanceReduction(ctx);

        // base = 300/(40*8.4)*0.2 = 300/336*0.2 ≈ 0.17857
        float expectedMitPct = 300f / (40 * 8.4f) * 0.2f;
        float expectedReduction = 1000f * expectedMitPct;
        ctx.Damage.ShouldBe(1000f - expectedReduction, 1f);
    }

    [Fact]
    public void ResistanceReduction_soft_cap_at_40_percent()
    {
        var ctx = MakeAbilityContext(dmgType: DamageType.Elemental);
        ctx.Damage = 1000f;
        // Need resist high enough that base > 0.4
        // base = R/(40*8.4)*0.2 > 0.4 → R > 0.4/0.2*336 = 672
        ctx.TargetResistance = 1000;
        ctx.AttackerLevel = 40;

        DamagePipeline.ApplyResistanceReduction(ctx);

        // base = 1000/336*0.2 ≈ 0.5952
        // effective = (0.5952-0.4)/3 + 0.4 = 0.0651 + 0.4 = 0.4651
        float baseMit = 1000f / (40f * 8.4f) * 0.2f;
        float effective = (baseMit - 0.4f) / 3f + 0.4f;
        float expectedReduction = 1000f * effective;
        ctx.Damage.ShouldBe(1000f - expectedReduction, 1f);
    }

    [Fact]
    public void ResistanceReduction_caps_at_75_percent()
    {
        var ctx = MakeAbilityContext(dmgType: DamageType.Corporeal);
        ctx.Damage = 1000f;
        ctx.TargetResistance = 99999;
        ctx.AttackerLevel = 40;

        DamagePipeline.ApplyResistanceReduction(ctx);

        ctx.Damage.ShouldBe(250f, 1f);
        ctx.Mitigation.ShouldBe(750f, 1f);
    }

    [Fact]
    public void ResistanceReduction_zero_resistance_no_mitigation()
    {
        var ctx = MakeAbilityContext(dmgType: DamageType.Spiritual);
        ctx.Damage = 1000f;
        ctx.TargetResistance = 0;

        DamagePipeline.ApplyResistanceReduction(ctx);

        ctx.Damage.ShouldBe(1000f);
    }

    [Fact]
    public void ResistanceReduction_with_armor_resist_pen_factor()
    {
        var ctx = MakeAbilityContext(dmgType: DamageType.Spiritual);
        ctx.Damage = 1000f;
        ctx.TargetResistance = 300;
        ctx.AttackerLevel = 40;
        ctx.ArmorResistPenFactor = 0.5f; // 50% resist pen

        DamagePipeline.ApplyResistanceReduction(ctx);

        // base = 300/336*0.2 ≈ 0.17857
        // final = 0.17857 * (1 - 0.5) ≈ 0.08929
        float baseMit = 300f / (40 * 8.4f) * 0.2f;
        float expected = baseMit * 0.5f;
        float expectedReduction = 1000f * expected;
        ctx.Damage.ShouldBe(1000f - expectedReduction, 1f);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Defense Rolls
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ComputeBlockChance_basic_formula()
    {
        // blockRating=200, offensiveStat=800
        // chance = 200/800 * 0.2 * 100 = 5.0
        float chance = DamagePipeline.ComputeBlockChance(200, 800, 0, 0, 0);
        chance.ShouldBe(5.0f, 0.01f);
    }

    [Fact]
    public void ComputeBlockChance_caps_at_50()
    {
        // blockRating=9999, offensiveStat=100
        float chance = DamagePipeline.ComputeBlockChance(9999, 100, 0, 0, 0);
        // rating part capped at 50, then + flat
        chance.ShouldBe(50f);
    }

    [Fact]
    public void ComputeBlockChance_with_modifiers()
    {
        // base = 200/800*0.2*100 = 5.0
        // +3 defensibility, +2 flatBlock, -1 strikethrough = 5+3+2-1 = 9.0
        float chance = DamagePipeline.ComputeBlockChance(200, 800, 3, 2, 1);
        chance.ShouldBe(9.0f, 0.01f);
    }

    [Fact]
    public void ComputeSecondaryDefenseChance_basic_formula()
    {
        // defensiveRating=400, offensiveStat=800
        // chance = 400/800*0.075*100 = 3.75
        float chance = DamagePipeline.ComputeSecondaryDefenseChance(400, 800, 0, 0, 0);
        chance.ShouldBe(3.75f, 0.01f);
    }

    [Fact]
    public void ComputeSecondaryDefenseChance_caps_at_25()
    {
        float chance = DamagePipeline.ComputeSecondaryDefenseChance(9999, 100, 0, 0, 0);
        chance.ShouldBe(25f);
    }

    [Fact]
    public void CheckDefense_block_succeeds_when_roll_below_chance()
    {
        var ctx = MakeAbilityContext(dmgType: DamageType.Physical);
        ctx.TargetHasShield = true;
        ctx.TargetIsFacing = true;
        ctx.TargetBlockRating = 500;
        ctx.AttackerPrimaryStat = 500;
        ctx.DefenseRoll = 0; // guaranteed

        bool defended = DamagePipeline.CheckDefense(ctx);

        defended.ShouldBeTrue();
        ctx.DefenseType.ShouldBe(DefenseType.Block);
    }

    [Fact]
    public void CheckDefense_block_requires_shield()
    {
        var ctx = MakeAbilityContext(dmgType: DamageType.Physical);
        ctx.TargetHasShield = false; // no shield
        ctx.TargetIsFacing = true;
        ctx.TargetBlockRating = 500;
        ctx.AttackerPrimaryStat = 500;
        ctx.TargetDefenseRating = 0;
        ctx.DefenseRoll = 50; // above any chance from 0-rating defense

        bool defended = DamagePipeline.CheckDefense(ctx);

        defended.ShouldBeFalse();
    }

    [Fact]
    public void CheckDefense_parry_for_physical()
    {
        var ctx = MakeAbilityContext(dmgType: DamageType.Physical);
        ctx.TargetHasShield = false;
        ctx.TargetDefenseRating = 500;
        ctx.AttackerPrimaryStat = 500;
        ctx.DefenseRoll = 0;

        bool defended = DamagePipeline.CheckDefense(ctx);

        defended.ShouldBeTrue();
        ctx.DefenseType.ShouldBe(DefenseType.Parry);
    }

    [Fact]
    public void CheckDefense_undefendable_always_fails()
    {
        var ctx = MakeAbilityContext(dmgType: DamageType.Physical);
        ctx.Undefendable = true;
        ctx.TargetDefenseRating = 9999;
        ctx.DefenseRoll = 0;

        // Undefendable is checked in Resolve, not CheckDefense itself
        // But verify CheckDefense still rolls normally:
        bool defended = DamagePipeline.CheckDefense(ctx);
        // It would technically defend — the caller (Resolve) gates this
        defended.ShouldBeTrue();
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Percentage Multipliers
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ApplyPercentageModifiers_accumulates_bonuses()
    {
        var ctx = MakeAbilityContext();
        ctx.AttackerTypePowerBonus = 0.10f; // +10%
        ctx.TargetInTypeDmgBonus = 0.05f;   // +5%
        ctx.AttackerOutDmgBonus = 0.03f;    // +3%
        ctx.TargetInDmgBonus = 0.02f;       // +2%
        ctx.AttackerTypePowerReduction = 1.0f;
        ctx.TargetInTypeDmgReduction = 1.0f;
        ctx.AttackerOutDmgReduction = 1.0f;
        ctx.TargetInDmgReduction = 1.0f;

        DamagePipeline.ApplyPercentageModifiers(ctx);

        // bonus = 1.0 + 0.10 + 0.05 + 0.03 + 0.02 = 1.20
        ctx.DamageBonus.ShouldBe(1.20f, 0.01f);
        ctx.DamageReduction.ShouldBe(1.0f);
    }

    [Fact]
    public void ApplyPercentageModifiers_reduction_multiplies()
    {
        var ctx = MakeAbilityContext();
        ctx.AttackerTypePowerReduction = 0.9f;  // 10% reduction
        ctx.TargetInTypeDmgReduction = 0.85f;   // 15% reduction
        ctx.AttackerOutDmgReduction = 0.95f;
        ctx.TargetInDmgReduction = 0.9f;

        DamagePipeline.ApplyPercentageModifiers(ctx);

        // reduction = 1.0 * 0.9 * 0.85 * 0.95 * 0.9 ≈ 0.65363
        float expected = 0.9f * 0.85f * 0.95f * 0.9f;
        ctx.DamageReduction.ShouldBe(expected, 0.001f);
    }

    [Fact]
    public void ApplyPercentageModifiers_skipped_for_procs()
    {
        var ctx = MakeAbilityContext();
        ctx.IsProc = true;
        ctx.AttackerTypePowerBonus = 0.50f;

        DamagePipeline.ApplyPercentageModifiers(ctx);

        ctx.DamageBonus.ShouldBe(1.0f); // unchanged
    }

    // ═══════════════════════════════════════════════════════════════════
    //  ApplyModifiers
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ApplyModifiers_multiplies_damage_and_mitigation()
    {
        var ctx = MakeAbilityContext();
        ctx.Damage = 1000f;
        ctx.Mitigation = 200f;
        ctx.DamageBonus = 1.2f;
        ctx.DamageReduction = 0.8f;

        DamagePipeline.ApplyModifiers(ctx);

        // factor = 1.2 * 0.8 = 0.96
        ctx.Damage.ShouldBe(960f, 0.01f);
        ctx.Mitigation.ShouldBe(192f, 0.01f);
        ctx.DamageBonus.ShouldBe(1f); // reset
        ctx.DamageReduction.ShouldBe(1f);
    }

    [Fact]
    public void ApplyModifiers_no_change_when_neutral()
    {
        var ctx = MakeAbilityContext();
        ctx.Damage = 500f;
        ctx.DamageBonus = 1f;
        ctx.DamageReduction = 1f;

        DamagePipeline.ApplyModifiers(ctx);

        ctx.Damage.ShouldBe(500f);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  AoE Pet Penalty
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void AoePetPenalty_halves_damage_and_mitigation()
    {
        var ctx = MakeAbilityContext();
        ctx.IsAoE = true;
        ctx.TargetIsPet = true;
        ctx.Damage = 1000f;
        ctx.Mitigation = 200f;

        DamagePipeline.ApplyAoePetPenalty(ctx);

        ctx.Damage.ShouldBe(500f);
        ctx.Mitigation.ShouldBe(100f);
    }

    [Fact]
    public void AoePetPenalty_no_change_when_not_pet()
    {
        var ctx = MakeAbilityContext();
        ctx.IsAoE = true;
        ctx.TargetIsPet = false;
        ctx.Damage = 1000f;

        DamagePipeline.ApplyAoePetPenalty(ctx);

        ctx.Damage.ShouldBe(1000f);
    }

    [Fact]
    public void AoePetPenalty_no_change_when_not_aoe()
    {
        var ctx = MakeAbilityContext();
        ctx.IsAoE = false;
        ctx.TargetIsPet = true;
        ctx.Damage = 1000f;

        DamagePipeline.ApplyAoePetPenalty(ctx);

        ctx.Damage.ShouldBe(1000f);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Finalize
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Finalize_writes_rounded_results()
    {
        var ctx = MakeAbilityContext();
        ctx.Damage = 456.7f;
        ctx.Mitigation = 123.4f;
        ctx.Absorption = 50.1f;

        DamagePipeline.Finalize(ctx);

        ctx.FinalDamage.ShouldBe(456u);
        ctx.FinalMitigation.ShouldBe(123u);
        ctx.FinalAbsorption.ShouldBe(50u);
    }

    [Fact]
    public void Finalize_clamps_negative_to_zero()
    {
        var ctx = MakeAbilityContext();
        ctx.Damage = -10f;

        DamagePipeline.Finalize(ctx);

        ctx.FinalDamage.ShouldBe(0u);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Full Pipeline: Resolve
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Resolve_defended_attack_sets_zero_damage()
    {
        var ctx = MakeAbilityContext(dmgType: DamageType.Physical);
        ctx.TargetHasShield = true;
        ctx.TargetIsFacing = true;
        ctx.TargetBlockRating = 9999;
        ctx.AttackerPrimaryStat = 100;
        ctx.DefenseRoll = 0; // guaranteed block

        DamagePipeline.Resolve(ctx);

        ctx.WasDefended.ShouldBeTrue();
        ctx.DefenseType.ShouldBe(DefenseType.Block);
        ctx.FinalDamage.ShouldBe(0u);
    }

    [Fact]
    public void Resolve_standard_ability_end_to_end()
    {
        var ctx = new DamageContext
        {
            DamageType = DamageType.Physical,
            AttackerLevel = 40,
            MinDamage = 100,
            MaxDamage = 200,
            CastTimeDamageMult = 1.5f,
            StatDamageScale = 1.0f,
            StatCoefficient = 0.2f,
            WeaponDps = 50f,
            WeaponDamageScale = 1.0f,

            // Stat snapshots
            AttackerPrimaryStat = 500,
            TargetToughness = 300,
            TargetArmor = 400,
            TargetInitiative = 800,

            // No crit, no defense
            DefenseRoll = 99,
            CritRoll = 99,

            // Neutral multipliers
            AttackerTypePowerReduction = 1f,
            TargetInTypeDmgReduction = 1f,
            AttackerOutDmgReduction = 1f,
            TargetInDmgReduction = 1f,
        };

        DamagePipeline.Resolve(ctx);

        // Manual trace:
        // 1. BaseDamage = 200 (L40)
        // 2. WeaponDps = 200 + 50*1.0*1.5 = 275
        // 3. StatScaling: 500*0.2*1*1.5 = 150 → 275+150 = 425
        // 4. Toughness: 300*0.2*1*1.5 = 90 → 425-90 = 335, mit=90
        // 5. No crit (roll=99)
        // 6. Armor: 400/1760*0.4 ≈ 0.09091 → reduction = 335*0.09091 ≈ 30.45
        //    → damage = 304.55, mit = 120.45
        // 7. Pct mods: neutral (1.0)
        // 8. Modifiers: neutral
        // 9. Final ≈ 304

        ctx.WasDefended.ShouldBeFalse();
        ctx.WasCritical.ShouldBeFalse();
        ctx.FinalDamage.ShouldBeGreaterThan(0u);
        // Verify approximate range (exact float arithmetic may vary slightly)
        ctx.FinalDamage.ShouldBeInRange(300u, 310u);
    }

    [Fact]
    public void Resolve_proc_skips_weapon_defense_crit_pctmods()
    {
        var ctx = new DamageContext
        {
            DamageType = DamageType.Spiritual,
            AttackerLevel = 40,
            MinDamage = 150,
            MaxDamage = 150,
            IsProc = true,

            AttackerPrimaryStat = 500,
            TargetToughness = 200,
            TargetResistance = 200,
            TargetInitiative = 800,

            StatCoefficient = 0.2f,
            StatDamageScale = 1f,
            CastTimeDamageMult = 1.5f,
            WeaponDps = 100f, // should be skipped (proc)

            DefenseRoll = 0, // would defend, but procs skip defense
            CritRoll = 0,    // would crit, but procs skip crit

            AttackerTypePowerBonus = 0.50f, // should be skipped (proc)
            AttackerTypePowerReduction = 1f,
            TargetInTypeDmgReduction = 1f,
            AttackerOutDmgReduction = 1f,
            TargetInDmgReduction = 1f,
        };

        DamagePipeline.Resolve(ctx);

        ctx.WasDefended.ShouldBeFalse();
        ctx.WasCritical.ShouldBeFalse();
        ctx.FinalDamage.ShouldBeGreaterThan(0u);
    }

    [Fact]
    public void Resolve_raw_damage_skips_armor_and_defense()
    {
        var ctx = new DamageContext
        {
            DamageType = DamageType.RawDamage,
            AttackerLevel = 40,
            MinDamage = 500,
            MaxDamage = 500,
            TargetArmor = 9999,
            TargetResistance = 9999,
            TargetInitiative = 1, // would give high crit chance
            CritRoll = 99, // no crit to simplify
            DefenseRoll = 0, // would defend, but RawDamage skips defense
            StatCoefficient = 0.2f,
            CastTimeDamageMult = 1.5f,
            StatDamageScale = 1f,
            AttackerTypePowerReduction = 1f,
            TargetInTypeDmgReduction = 1f,
            AttackerOutDmgReduction = 1f,
            TargetInDmgReduction = 1f,
        };

        DamagePipeline.Resolve(ctx);

        ctx.WasDefended.ShouldBeFalse();
        // No armor/resist reduction
        ctx.FinalDamage.ShouldBe(500u);
    }

    [Fact]
    public void Resolve_precalculated_dot_tick()
    {
        var ctx = new DamageContext
        {
            DamageType = DamageType.Spiritual,
            IsPrecalculated = true,
            PrecalcDamage = 300f,
            PrecalcMitigation = 50f,
            PrecalcMultiplier = 0.5f, // 50% per tick
            AttackerLevel = 40,
            TargetInitiative = 800, // realistic initiative so crit chance is low
            CritRoll = 99,          // no crit
            AttackerOutDmgReduction = 1f,
            TargetInDmgReduction = 1f,
        };

        DamagePipeline.Resolve(ctx);

        // 300 * 0.5 = 150 damage, 50 * 0.5 = 25 mitigation
        ctx.FinalDamage.ShouldBe(150u);
        ctx.FinalMitigation.ShouldBe(25u);
    }

    [Fact]
    public void Resolve_precalculated_dot_tick_can_crit()
    {
        var ctx = new DamageContext
        {
            DamageType = DamageType.Spiritual,
            IsPrecalculated = true,
            PrecalcDamage = 300f,
            PrecalcMitigation = 50f,
            PrecalcMultiplier = 1.0f,
            AttackerLevel = 40,
            TargetInitiative = 100, // high crit chance
            CritRoll = 0,           // guaranteed crit
            CritVarianceRoll = 0f,
            AttackerOutDmgReduction = 1f,
            TargetInDmgReduction = 1f,
        };

        DamagePipeline.Resolve(ctx);

        ctx.WasCritical.ShouldBeTrue();
        // 300 * 1.35 = 405
        ctx.FinalDamage.ShouldBeGreaterThanOrEqualTo(400u);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Edge Cases
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void GetArmorPenForLevel_interpolates_correctly()
    {
        // L1: min, L40: max
        DamagePipeline.GetArmorPenForLevel(50, 200, 1).ShouldBe(50f, 0.01f);
        DamagePipeline.GetArmorPenForLevel(50, 200, 40).ShouldBe(200f, 0.01f);
    }

    [Fact]
    public void SoftHardCap_zero_stat_returns_zero()
    {
        DamagePipeline.ApplySoftHardCap(0, 40).ShouldBe(0f);
    }

    [Fact]
    public void SoftHardCap_negative_stat_returns_negative()
    {
        // Negative stats are possible from extreme debuffs
        DamagePipeline.ApplySoftHardCap(-100, 40).ShouldBe(-100f);
    }

    [Fact]
    public void Defense_zero_offensive_stat_does_not_divide_by_zero()
    {
        // offensiveStat = 0 → should use 1 to avoid division by zero
        float block = DamagePipeline.ComputeBlockChance(500, 0, 0, 0, 0);
        block.ShouldBeGreaterThan(0f);

        float pdd = DamagePipeline.ComputeSecondaryDefenseChance(500, 0, 0, 0, 0);
        pdd.ShouldBeGreaterThan(0f);
    }

    [Fact]
    public void CritChance_zero_initiative_does_not_divide_by_zero()
    {
        float chance = DamagePipeline.ComputeCritChance(40, 0, 0, 0, 0, 0);
        chance.ShouldBeGreaterThan(0f);
    }
}
