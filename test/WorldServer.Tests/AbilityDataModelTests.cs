using Shouldly;
using WorldServerV2.Data.Entities;
using WorldServerV2.World.Combat;
using WorldServerV2.World.Combat.Abilities;
using WorldServerV2.World.Entities;

namespace WorldServer.Tests;

/// <summary>
/// Tests for Step 4: <see cref="AbilityDefinition"/>, <see cref="AbilityCastContext"/>,
/// <see cref="ModifierOperation"/>, and <see cref="ModifierApplicator"/>.
/// </summary>
public class AbilityDataModelTests
{
    // ═══════════════════════════════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════════════════════════════

    private static UnitEntity MakeUnit(ushort id = 1)
        => new PlayerEntity(id, new Character { CharacterId = id, Name = $"Unit{id}" }, 1000);

    private static AbilityDefinition MakeDef(
        ushort entry = 1000,
        ushort castTime = 0,
        ushort cooldown = 5000,
        byte apCost = 25,
        short specialCost = 0,
        ushort range = 100,
        byte minRange = 0,
        byte maxTargets = 0,
        ushort channelId = 0,
        bool canCastWhileMoving = false)
    {
        return new AbilityDefinition
        {
            Entry = entry,
            Name = $"Ability{entry}",
            CastTime = castTime,
            Cooldown = cooldown,
            ApCost = apCost,
            SpecialCost = specialCost,
            Range = range,
            MinRange = minRange,
            MaxTargets = maxTargets,
            ChannelId = channelId,
            CanCastWhileMoving = canCastWhileMoving,
        };
    }

    private static AbilityCastContext MakeContext(
        AbilityDefinition? def = null,
        UnitEntity? caster = null,
        UnitEntity? target = null)
    {
        return new AbilityCastContext(
            def ?? MakeDef(),
            caster ?? MakeUnit(),
            target);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  AbilityDefinition — Defaults and derived properties
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void AbilityDefinition_defaults_are_sensible()
    {
        var def = new AbilityDefinition { Entry = 1, Name = "Test" };

        def.AbilityType.ShouldBe(AbilityType.None);
        def.Origin.ShouldBe(AbilityOrigin.None);
        def.CastTime.ShouldBe((ushort)0);
        def.Cooldown.ShouldBe((ushort)0);
        def.ApCost.ShouldBe((byte)0);
        def.Range.ShouldBe((ushort)0);
        def.MaxTargets.ShouldBe((byte)0);
        def.ChannelId.ShouldBe((ushort)0);
        def.ToggleEntry.ShouldBe((ushort)0);
        def.FlightTimeMod.ShouldBe(1f);
        def.Commands.Count.ShouldBe(0);
        def.Modifiers.Count.ShouldBe(0);
    }

    [Fact]
    public void AbilityDefinition_IsInstant_when_no_cast_time_and_not_channeled()
    {
        var def = MakeDef(castTime: 0, channelId: 0);
        def.IsInstant.ShouldBeTrue();
        def.IsChanneled.ShouldBeFalse();
    }

    [Fact]
    public void AbilityDefinition_not_IsInstant_when_has_cast_time()
    {
        var def = MakeDef(castTime: 2000);
        def.IsInstant.ShouldBeFalse();
    }

    [Fact]
    public void AbilityDefinition_IsChanneled_when_channelId_set()
    {
        var def = MakeDef(channelId: 500);
        def.IsChanneled.ShouldBeTrue();
        // Technically still "instant" by the CastTime==0 check, but channel overrides.
        def.IsInstant.ShouldBeFalse();
    }

    [Fact]
    public void AbilityDefinition_IsToggle_when_toggleEntry_set()
    {
        var def = new AbilityDefinition { Entry = 1, Name = "Toggle", ToggleEntry = 2 };
        def.IsToggle.ShouldBeTrue();
    }

    // ═══════════════════════════════════════════════════════════════════
    //  DamageDefinition — defaults and IsHeal
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void DamageDefinition_defaults_are_sensible()
    {
        var dd = new DamageDefinition();

        dd.CastTimeDamageMult.ShouldBe(1.5f);
        dd.StatDamageScale.ShouldBe(1f);
        dd.HatredScale.ShouldBe(1f);
        dd.HealHatredScale.ShouldBe(1f);
        dd.IsHeal.ShouldBeFalse();
    }

    [Theory]
    [InlineData(DamageType.Healing, true)]
    [InlineData(DamageType.RawHealing, true)]
    [InlineData(DamageType.Physical, false)]
    [InlineData(DamageType.Spiritual, false)]
    [InlineData(DamageType.RawDamage, false)]
    public void DamageDefinition_IsHeal_classifies_correctly(DamageType type, bool expected)
    {
        var dd = new DamageDefinition { DamageType = type };
        dd.IsHeal.ShouldBe(expected);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  AbilityCastContext — Construction & snapshot
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void CastContext_snapshots_definition_values()
    {
        var def = MakeDef(
            castTime: 2000,
            cooldown: 8000,
            apCost: 30,
            specialCost: 20,
            range: 65,
            minRange: 5,
            maxTargets: 3);
        var ctx = MakeContext(def);

        ctx.CastTime.ShouldBe(2000f);
        ctx.Cooldown.ShouldBe(8000f);
        ctx.ApCost.ShouldBe(30f);
        ctx.SpecialCost.ShouldBe(20f);
        ctx.Range.ShouldBe(65f);
        ctx.MinRange.ShouldBe(5f);
        ctx.MaxTargets.ShouldBe(3);
    }

    [Fact]
    public void CastContext_maxTargets_defaults_to_9_when_zero()
    {
        var def = MakeDef(maxTargets: 0);
        var ctx = MakeContext(def);
        ctx.MaxTargets.ShouldBe(9);
    }

    [Fact]
    public void CastContext_sets_Instant_state_for_instant_ability()
    {
        var ctx = MakeContext(MakeDef(castTime: 0, channelId: 0));
        ctx.CastState.ShouldBe(CastState.Instant);
        ctx.IsInstant.ShouldBeTrue();
    }

    [Fact]
    public void CastContext_sets_Casting_state_for_cast_time_ability()
    {
        var ctx = MakeContext(MakeDef(castTime: 2000));
        ctx.CastState.ShouldBe(CastState.Casting);
        ctx.IsCasting.ShouldBeTrue();
    }

    [Fact]
    public void CastContext_sets_Channeling_state_for_channeled_ability()
    {
        var ctx = MakeContext(MakeDef(channelId: 100));
        ctx.CastState.ShouldBe(CastState.Channeling);
        ctx.IsChanneling.ShouldBeTrue();
    }

    [Fact]
    public void CastContext_has_no_initial_failure()
    {
        var ctx = MakeContext();
        ctx.HasFailed.ShouldBeFalse();
        ctx.FailureCode.ShouldBeNull();
    }

    [Fact]
    public void CastContext_Fail_sets_failure_code()
    {
        var ctx = MakeContext();

        ctx.Fail(AbilityFailure.NotEnoughAp);

        ctx.HasFailed.ShouldBeTrue();
        ctx.FailureCode.ShouldBe(AbilityFailure.NotEnoughAp);
    }

    [Fact]
    public void CastContext_damage_modifiers_default_to_neutral()
    {
        var ctx = MakeContext();

        ctx.DamageBonus.ShouldBe(1f);
        ctx.DamageReduction.ShouldBe(1f);
        ctx.CritBonus.ShouldBe(0f);
        ctx.CritDamageBonus.ShouldBe(0f);
        ctx.ArmorPenBonus.ShouldBe(0f);
        ctx.Defensibility.ShouldBe(0);
        ctx.IsUndefendable.ShouldBeFalse();
    }

    [Fact]
    public void CastContext_null_definition_throws()
    {
        Should.Throw<ArgumentNullException>(() => new AbilityCastContext(null!, MakeUnit()));
    }

    [Fact]
    public void CastContext_null_caster_throws()
    {
        Should.Throw<ArgumentNullException>(() => new AbilityCastContext(MakeDef(), null!));
    }

    [Fact]
    public void CastContext_snapshots_CanCastWhileMoving_from_definition()
    {
        var def = MakeDef(canCastWhileMoving: true);
        var ctx = MakeContext(def);
        ctx.CanCastWhileMoving.ShouldBeTrue();
    }

    // ═══════════════════════════════════════════════════════════════════
    //  ModifierApplicator — Cast time operations
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Apply_MultiplyCastTime_scales_cast_time()
    {
        var ctx = MakeContext(MakeDef(castTime: 2000));

        ModifierApplicator.Apply(ctx, ModifierOperation.MultiplyCastTime, 0.5f).ShouldBeTrue();

        ctx.CastTime.ShouldBe(1000f);
    }

    [Fact]
    public void Apply_AddCastTime_adds_to_cast_time()
    {
        var ctx = MakeContext(MakeDef(castTime: 2000));

        ModifierApplicator.Apply(ctx, ModifierOperation.AddCastTime, -500f).ShouldBeTrue();

        ctx.CastTime.ShouldBe(1500f);
    }

    [Fact]
    public void Apply_SetInstant_zeroes_cast_time_and_sets_state()
    {
        var ctx = MakeContext(MakeDef(castTime: 3000));

        ModifierApplicator.Apply(ctx, ModifierOperation.SetInstant, 0).ShouldBeTrue();

        ctx.CastTime.ShouldBe(0f);
        ctx.CastState.ShouldBe(CastState.Instant);
    }

    [Fact]
    public void Apply_SetMoveCast_enables_move_casting()
    {
        var ctx = MakeContext();
        ctx.CanCastWhileMoving.ShouldBeFalse();

        ModifierApplicator.Apply(ctx, ModifierOperation.SetMoveCast, 0).ShouldBeTrue();

        ctx.CanCastWhileMoving.ShouldBeTrue();
    }

    // ═══════════════════════════════════════════════════════════════════
    //  ModifierApplicator — Cooldown operations
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Apply_MultiplyCooldown_scales_cooldown()
    {
        var ctx = MakeContext(MakeDef(cooldown: 10000));

        ModifierApplicator.Apply(ctx, ModifierOperation.MultiplyCooldown, 0.8f).ShouldBeTrue();

        ctx.Cooldown.ShouldBe(8000f);
    }

    [Fact]
    public void Apply_AddCooldown_adds_to_cooldown()
    {
        var ctx = MakeContext(MakeDef(cooldown: 5000));

        ModifierApplicator.Apply(ctx, ModifierOperation.AddCooldown, 2000).ShouldBeTrue();

        ctx.Cooldown.ShouldBe(7000f);
    }

    [Fact]
    public void Apply_SetCooldown_overwrites_cooldown()
    {
        var ctx = MakeContext(MakeDef(cooldown: 5000));

        ModifierApplicator.Apply(ctx, ModifierOperation.SetCooldown, 3000).ShouldBeTrue();

        ctx.Cooldown.ShouldBe(3000f);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  ModifierApplicator — AP cost operations
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Apply_AddApCost_modifies_ap_cost()
    {
        var ctx = MakeContext(MakeDef(apCost: 25));

        ModifierApplicator.Apply(ctx, ModifierOperation.AddApCost, -5).ShouldBeTrue();

        ctx.ApCost.ShouldBe(20f);
    }

    [Fact]
    public void Apply_SetApCost_overwrites_ap_cost()
    {
        var ctx = MakeContext(MakeDef(apCost: 25));

        ModifierApplicator.Apply(ctx, ModifierOperation.SetApCost, 0).ShouldBeTrue();

        ctx.ApCost.ShouldBe(0f);
    }

    [Fact]
    public void Apply_MultiplyApCost_scales_ap_cost()
    {
        var ctx = MakeContext(MakeDef(apCost: 20));

        ModifierApplicator.Apply(ctx, ModifierOperation.MultiplyApCost, 1.5f).ShouldBeTrue();

        ctx.ApCost.ShouldBe(30f);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  ModifierApplicator — Range operations
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Apply_MultiplyRange_scales_range()
    {
        var ctx = MakeContext(MakeDef(range: 100));

        ModifierApplicator.Apply(ctx, ModifierOperation.MultiplyRange, 1.5f).ShouldBeTrue();

        ctx.Range.ShouldBe(150f);
    }

    [Fact]
    public void Apply_AddRange_modifies_range()
    {
        var ctx = MakeContext(MakeDef(range: 100));

        ModifierApplicator.Apply(ctx, ModifierOperation.AddRange, 20).ShouldBeTrue();

        ctx.Range.ShouldBe(120f);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  ModifierApplicator — Damage & crit operations
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Apply_AddDamageBonus_adds_to_multiplier()
    {
        var ctx = MakeContext();

        ModifierApplicator.Apply(ctx, ModifierOperation.AddDamageBonus, 0.25f).ShouldBeTrue();

        ctx.DamageBonus.ShouldBe(1.25f);
    }

    [Fact]
    public void Apply_MultiplyDamageBonus_scales_multiplier()
    {
        var ctx = MakeContext();
        ctx.DamageBonus = 1.5f;

        ModifierApplicator.Apply(ctx, ModifierOperation.MultiplyDamageBonus, 2f).ShouldBeTrue();

        ctx.DamageBonus.ShouldBe(3f);
    }

    [Fact]
    public void Apply_MultiplyDamageReduction_scales_reduction()
    {
        var ctx = MakeContext();

        ModifierApplicator.Apply(ctx, ModifierOperation.MultiplyDamageReduction, 0.9f).ShouldBeTrue();

        ctx.DamageReduction.ShouldBe(0.9f);
    }

    [Fact]
    public void Apply_AddCritRate_adds_flat_bonus()
    {
        var ctx = MakeContext();

        ModifierApplicator.Apply(ctx, ModifierOperation.AddCritRate, 10f).ShouldBeTrue();

        ctx.CritBonus.ShouldBe(10f);
    }

    [Fact]
    public void Apply_AddCritDamage_adds_crit_damage_bonus()
    {
        var ctx = MakeContext();

        ModifierApplicator.Apply(ctx, ModifierOperation.AddCritDamage, 0.15f).ShouldBeTrue();

        ctx.CritDamageBonus.ShouldBe(0.15f);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  ModifierApplicator — Defense & armor pen
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Apply_SetUndefendable_flags_context()
    {
        var ctx = MakeContext();

        ModifierApplicator.Apply(ctx, ModifierOperation.SetUndefendable, 0).ShouldBeTrue();

        ctx.IsUndefendable.ShouldBeTrue();
    }

    [Fact]
    public void Apply_AddDefensibility_modifies_defensibility()
    {
        var ctx = MakeContext();

        ModifierApplicator.Apply(ctx, ModifierOperation.AddDefensibility, -20).ShouldBeTrue();

        ctx.Defensibility.ShouldBe(-20);
    }

    [Fact]
    public void Apply_AddArmorPenFactor_modifies_pen()
    {
        var ctx = MakeContext();

        ModifierApplicator.Apply(ctx, ModifierOperation.AddArmorPenFactor, 0.25f).ShouldBeTrue();

        ctx.ArmorPenBonus.ShouldBe(0.25f);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  ModifierApplicator — AoE
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Apply_AddMaxTargets_modifies_max_targets()
    {
        var ctx = MakeContext();

        ModifierApplicator.Apply(ctx, ModifierOperation.AddMaxTargets, 3).ShouldBeTrue();

        ctx.MaxTargets.ShouldBe(12); // 9 default + 3
    }

    // ═══════════════════════════════════════════════════════════════════
    //  ModifierApplicator — Custom returns false
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Apply_Custom_returns_false()
    {
        var ctx = MakeContext();

        ModifierApplicator.Apply(ctx, ModifierOperation.Custom, 0).ShouldBeFalse();
    }

    // ═══════════════════════════════════════════════════════════════════
    //  ModifierApplicator — Compound modifier application
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Multiple_modifiers_accumulate_correctly()
    {
        var ctx = MakeContext(MakeDef(castTime: 3000, cooldown: 10000, apCost: 30, range: 100));

        // Tactic: -20% cast time
        ModifierApplicator.Apply(ctx, ModifierOperation.MultiplyCastTime, 0.8f);
        // Tactic: -5 AP
        ModifierApplicator.Apply(ctx, ModifierOperation.AddApCost, -5);
        // Mastery: +50% range
        ModifierApplicator.Apply(ctx, ModifierOperation.MultiplyRange, 1.5f);
        // Buff: +10% damage
        ModifierApplicator.Apply(ctx, ModifierOperation.AddDamageBonus, 0.1f);
        // Proc: +5% crit
        ModifierApplicator.Apply(ctx, ModifierOperation.AddCritRate, 5f);

        ctx.CastTime.ShouldBe(2400f);  // 3000 * 0.8
        ctx.ApCost.ShouldBe(25f);      // 30 - 5
        ctx.Range.ShouldBe(150f);      // 100 * 1.5
        ctx.DamageBonus.ShouldBe(1.1f); // 1.0 + 0.1
        ctx.CritBonus.ShouldBe(5f);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  ModifierApplicator — ApplyDefinition with conditions
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ApplyDefinition_applies_unconditional_modifier()
    {
        var ctx = MakeContext(MakeDef(cooldown: 10000));
        var mod = new AbilityModifierDefinition
        {
            Operation = ModifierOperation.MultiplyCooldown,
            Value = 0.5f,
        };

        ModifierApplicator.ApplyDefinition(ctx, mod).ShouldBeTrue();

        ctx.Cooldown.ShouldBe(5000f);
    }

    [Fact]
    public void ApplyDefinition_skips_conditional_when_no_evaluator()
    {
        var ctx = MakeContext(MakeDef(cooldown: 10000));
        var mod = new AbilityModifierDefinition
        {
            Operation = ModifierOperation.MultiplyCooldown,
            Value = 0.5f,
            Condition = ModifierCondition.HasBuff,
            ConditionValue = 999,
        };

        // No evaluator provided — should skip gracefully.
        ModifierApplicator.ApplyDefinition(ctx, mod).ShouldBeTrue();

        ctx.Cooldown.ShouldBe(10000f); // Unchanged.
    }

    [Fact]
    public void ApplyDefinition_applies_when_condition_met()
    {
        var ctx = MakeContext(MakeDef(cooldown: 10000));
        var mod = new AbilityModifierDefinition
        {
            Operation = ModifierOperation.MultiplyCooldown,
            Value = 0.5f,
            Condition = ModifierCondition.HasBuff,
            ConditionValue = 999,
        };

        // Evaluator says "yes, condition met".
        var applied = ModifierApplicator.ApplyDefinition(ctx, mod,
            conditionEvaluator: (_, _, _) => true);

        applied.ShouldBeTrue();
        ctx.Cooldown.ShouldBe(5000f);
    }

    [Fact]
    public void ApplyDefinition_skips_when_condition_not_met()
    {
        var ctx = MakeContext(MakeDef(cooldown: 10000));
        var mod = new AbilityModifierDefinition
        {
            Operation = ModifierOperation.MultiplyCooldown,
            Value = 0.5f,
            Condition = ModifierCondition.HasBuff,
            ConditionValue = 999,
        };

        // Evaluator says "no, condition not met".
        var applied = ModifierApplicator.ApplyDefinition(ctx, mod,
            conditionEvaluator: (_, _, _) => false);

        applied.ShouldBeTrue(); // Returns true (skipped, not failed).
        ctx.Cooldown.ShouldBe(10000f); // Unchanged.
    }

    [Fact]
    public void ApplyDefinition_returns_false_for_custom_operation()
    {
        var ctx = MakeContext();
        var mod = new AbilityModifierDefinition
        {
            Operation = ModifierOperation.Custom,
            Value = 42,
        };

        ModifierApplicator.ApplyDefinition(ctx, mod).ShouldBeFalse();
    }

    // ═══════════════════════════════════════════════════════════════════
    //  AbilityCommandDefinition — defaults
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void AbilityCommandDefinition_defaults_are_sensible()
    {
        var cmd = new AbilityCommandDefinition { EffectType = AbilityEffectType.DealDamage };

        cmd.CommandId.ShouldBe((byte)0);
        cmd.CommandSequence.ShouldBe((byte)0);
        cmd.TargetType.ShouldBe(CommandTargetType.Last);
        cmd.EffectRadius.ShouldBe((byte)0);
        cmd.MaxTargets.ShouldBe((byte)0);
        cmd.PrimaryValue.ShouldBe(0);
        cmd.SecondaryValue.ShouldBe(0);
        cmd.IsDelayedEffect.ShouldBeFalse();
        cmd.NoAutoUse.ShouldBeFalse();
        cmd.Damage.ShouldBeNull();
        cmd.ChainedCommands.Count.ShouldBe(0);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  AbilityModifierDefinition — defaults
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void AbilityModifierDefinition_defaults_are_sensible()
    {
        var mod = new AbilityModifierDefinition { Operation = ModifierOperation.AddDamageBonus };

        mod.Stage.ShouldBe(ModifierStage.PreCast);
        mod.Value.ShouldBe(0f);
        mod.SecondaryValue.ShouldBe(0f);
        mod.Condition.ShouldBeNull();
        mod.ConditionValue.ShouldBe(0);
        mod.TargetCommandId.ShouldBe((byte)0);
        mod.TargetCommandSequence.ShouldBe((byte)0);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  AddSpecialCost operation
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Apply_AddSpecialCost_modifies_special_cost()
    {
        var ctx = MakeContext(MakeDef(specialCost: 30));

        ModifierApplicator.Apply(ctx, ModifierOperation.AddSpecialCost, -10).ShouldBeTrue();

        ctx.SpecialCost.ShouldBe(20f);
    }
}
