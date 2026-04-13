using Core.GameWorld.Stats;
using Shouldly;

namespace Core.GameWorld.Tests;

/// <summary>
/// Tests for <see cref="StatEntry"/>, <see cref="StatContainer"/>, and the supporting
/// enums (<see cref="StatId"/>, <see cref="BuffClass"/>).
/// </summary>
public class StatContainerTests
{
    // ── Helpers ──────────────────────────────────────────────────────────

    private static StatContainer MakeContainer() => new();

    // ── StatConstants ───────────────────────────────────────────────────

    [Fact]
    public void StatConstants_SlotCount_covers_all_enum_values()
    {
        // SlotCount must be at least MaxStatValue + 1 so indexing never overflows.
        StatConstants.SlotCount.ShouldBeGreaterThanOrEqualTo(StatConstants.MaxStatValue + 1);
    }

    [Theory]
    [InlineData(StatId.Strength, true)]
    [InlineData(StatId.Wounds, true)]
    [InlineData(StatId.WeaponSkill, true)]
    [InlineData(StatId.Block, false)]
    [InlineData(StatId.Armor, false)]
    public void IsBaseStat_classifies_correctly(StatId stat, bool expected)
    {
        StatConstants.IsBaseStat(stat).ShouldBe(expected);
    }

    // ── BuffClassConstants ──────────────────────────────────────────────

    [Theory]
    [InlineData(BuffClass.Buff0, true)]
    [InlineData(BuffClass.Buff1, true)]
    [InlineData(BuffClass.Tactic, false)]
    [InlineData(BuffClass.Career, false)]
    public void IsHighestOnly_returns_correct_stacking_policy(BuffClass bc, bool expected)
    {
        BuffClassConstants.IsHighestOnly(bc).ShouldBe(expected);
    }

    // ── StatEntry: Base layer only ──────────────────────────────────────

    [Fact]
    public void Entry_default_total_is_zero()
    {
        var entry = new StatEntry();
        entry.GetTotal().ShouldBe(0);
    }

    [Fact]
    public void Entry_base_plus_renown_sums_correctly()
    {
        var entry = new StatEntry { Base = 100, Renown = 25 };
        entry.GetTotal().ShouldBe(125);
    }

    // ── StatEntry: Item bonus layer ─────────────────────────────────────

    [Fact]
    public void Entry_item_bonus_adds_to_total()
    {
        var entry = new StatEntry { Base = 100, ItemBonus = 50 };
        entry.GetTotal().ShouldBe(150);
    }

    [Fact]
    public void Entry_bolster_factor_scales_item_bonus()
    {
        var entry = new StatEntry { Base = 100, ItemBonus = 100, BolsterFactor = 0.5f };
        entry.GetTotal().ShouldBe(150); // 100 base + 100*0.5 item
    }

    [Fact]
    public void Entry_item_bonus_disabled_excludes_item_contribution()
    {
        var entry = new StatEntry { Base = 100, ItemBonus = 200, ItemBonusDisabled = true };
        entry.GetTotal().ShouldBe(100);
    }

    // ── StatEntry: Additive buff stacking ───────────────────────────────

    [Fact]
    public void Entry_tactic_class_stacks_additively()
    {
        var entry = new StatEntry { Base = 100 };
        entry.AddBonus(30, BuffClass.Tactic);
        entry.AddBonus(20, BuffClass.Tactic);

        entry.GetTotal().ShouldBe(150); // 100 + 30 + 20
    }

    [Fact]
    public void Entry_career_class_stacks_additively()
    {
        var entry = new StatEntry { Base = 100 };
        entry.AddBonus(40, BuffClass.Career);
        entry.AddBonus(10, BuffClass.Career);

        entry.GetTotal().ShouldBe(150);
    }

    // ── StatEntry: Highest-only stacking ────────────────────────────────

    [Fact]
    public void Entry_buff0_uses_highest_only_bonus()
    {
        var entry = new StatEntry { Base = 100 };
        entry.AddBonus(30, BuffClass.Buff0);
        entry.AddBonus(50, BuffClass.Buff0);

        // Highest-only: only the 50 applies.
        entry.GetTotal().ShouldBe(150);
    }

    [Fact]
    public void Entry_buff0_remove_highest_falls_back_to_next()
    {
        var entry = new StatEntry { Base = 100 };
        entry.AddBonus(30, BuffClass.Buff0);
        entry.AddBonus(50, BuffClass.Buff0);
        entry.RemoveBonus(50, BuffClass.Buff0);

        // After removing 50, the 30 becomes the highest.
        entry.GetTotal().ShouldBe(130);
    }

    [Fact]
    public void Entry_buff0_remove_all_returns_to_base()
    {
        var entry = new StatEntry { Base = 100 };
        entry.AddBonus(30, BuffClass.Buff0);
        entry.RemoveBonus(30, BuffClass.Buff0);

        entry.GetTotal().ShouldBe(100);
    }

    [Fact]
    public void Entry_buff1_uses_highest_only_reduction()
    {
        var entry = new StatEntry { Base = 200 };
        entry.AddReduction(60, BuffClass.Buff1);
        entry.AddReduction(30, BuffClass.Buff1);

        // Highest-only reduction: only the 60 applies.
        entry.GetTotal().ShouldBe(140); // 200 - 60
    }

    // ── StatEntry: Reductions ───────────────────────────────────────────

    [Fact]
    public void Entry_additive_reduction_subtracts_from_total()
    {
        var entry = new StatEntry { Base = 200 };
        entry.AddReduction(50, BuffClass.Tactic);

        entry.GetTotal().ShouldBe(150);
    }

    [Fact]
    public void Entry_remove_reduction_restores_value()
    {
        var entry = new StatEntry { Base = 200 };
        entry.AddReduction(50, BuffClass.Tactic);
        entry.RemoveReduction(50, BuffClass.Tactic);

        entry.GetTotal().ShouldBe(200);
    }

    // ── StatEntry: Multipliers ──────────────────────────────────────────

    [Fact]
    public void Entry_bonus_multiplier_scales_total()
    {
        var entry = new StatEntry { Base = 100 };
        entry.AddBonusMultiplier(1.5f, BuffClass.Tactic);

        entry.GetTotal().ShouldBe(150); // 100 * 1.5
    }

    [Fact]
    public void Entry_reduction_multiplier_scales_total()
    {
        var entry = new StatEntry { Base = 100 };
        entry.AddReductionMultiplier(0.8f, BuffClass.Tactic);

        entry.GetTotal().ShouldBe(80); // 100 * 0.8
    }

    [Fact]
    public void Entry_combined_multipliers_multiply_together()
    {
        var entry = new StatEntry { Base = 100 };
        entry.AddBonusMultiplier(1.5f, BuffClass.Tactic);     // +50%
        entry.AddReductionMultiplier(0.8f, BuffClass.Career);  // -20%

        entry.GetTotal().ShouldBe(120); // 100 * 1.5 * 0.8 = 120
    }

    [Fact]
    public void Entry_remove_bonus_multiplier_restores_value()
    {
        var entry = new StatEntry { Base = 100 };
        entry.AddBonusMultiplier(2.0f, BuffClass.Tactic);
        entry.RemoveBonusMultiplier(2.0f, BuffClass.Tactic);

        entry.GetTotal().ShouldBe(100);
    }

    [Fact]
    public void Entry_remove_reduction_multiplier_restores_value()
    {
        var entry = new StatEntry { Base = 100 };
        entry.AddReductionMultiplier(0.5f, BuffClass.Career);
        entry.RemoveReductionMultiplier(0.5f, BuffClass.Career);

        entry.GetTotal().ShouldBe(100);
    }

    [Fact]
    public void Entry_buff0_highest_only_bonus_multiplier()
    {
        var entry = new StatEntry { Base = 100 };
        entry.AddBonusMultiplier(1.2f, BuffClass.Buff0);
        entry.AddBonusMultiplier(1.5f, BuffClass.Buff0);

        // Highest-only: only 1.5 applies.
        entry.GetTotal().ShouldBe(150);
    }

    [Fact]
    public void Entry_buff0_highest_only_reduction_multiplier_takes_strongest()
    {
        var entry = new StatEntry { Base = 100 };
        entry.AddReductionMultiplier(0.8f, BuffClass.Buff0);
        entry.AddReductionMultiplier(0.5f, BuffClass.Buff0);

        // Strongest reduction is 0.5 (reduces more).
        entry.GetTotal().ShouldBe(50);
    }

    // ── StatEntry: Full formula integration ─────────────────────────────

    [Fact]
    public void Entry_full_5_layer_formula()
    {
        var entry = new StatEntry
        {
            Base = 100,
            Renown = 20,
            ItemBonus = 30
        };
        entry.AddBonus(50, BuffClass.Buff0);
        entry.AddReduction(10, BuffClass.Tactic);
        entry.AddBonusMultiplier(1.1f, BuffClass.Career); // +10%

        // Linear = (100 + 20 + 30 + 50 - 10) = 190
        // Multiplier = 1.1
        // Total = (int)(190 * 1.1) = 209
        entry.GetTotal().ShouldBe(209);
    }

    [Fact]
    public void Entry_floor_at_zero_prevents_negative()
    {
        var entry = new StatEntry { Base = 10 };
        entry.AddReduction(100, BuffClass.Tactic);

        entry.GetTotal(floorAtZero: true).ShouldBe(0);
        entry.GetTotal(floorAtZero: false).ShouldBe(-90);
    }

    // ── StatEntry: ClearModifiers ───────────────────────────────────────

    [Fact]
    public void Entry_clear_modifiers_preserves_base_and_renown()
    {
        var entry = new StatEntry { Base = 100, Renown = 20, ItemBonus = 50 };
        entry.AddBonus(30, BuffClass.Buff0);
        entry.AddBonusMultiplier(1.5f, BuffClass.Tactic);

        entry.ClearModifiers();

        entry.GetTotal().ShouldBe(120); // Base + Renown only
    }

    // ── StatContainer: Basic API ────────────────────────────────────────

    [Fact]
    public void Container_default_totals_are_zero()
    {
        var container = MakeContainer();

        container.GetTotal(StatId.Strength).ShouldBe(0);
        container.GetTotal(StatId.Wounds).ShouldBe(0);
        container.GetTotal(StatId.Armor).ShouldBe(0);
    }

    [Fact]
    public void Container_set_base_updates_total()
    {
        var container = MakeContainer();

        container.SetBase(StatId.Strength, 100);

        container.GetTotal(StatId.Strength).ShouldBe(100);
    }

    [Fact]
    public void Container_set_renown_updates_total()
    {
        var container = MakeContainer();
        container.SetBase(StatId.Strength, 100);
        container.SetRenown(StatId.Strength, 25);

        container.GetTotal(StatId.Strength).ShouldBe(125);
    }

    [Fact]
    public void Container_add_remove_bonus_round_trips()
    {
        var container = MakeContainer();
        container.SetBase(StatId.WeaponSkill, 200);
        container.AddBonus(StatId.WeaponSkill, 50, BuffClass.Buff0);

        container.GetTotal(StatId.WeaponSkill).ShouldBe(250);

        container.RemoveBonus(StatId.WeaponSkill, 50, BuffClass.Buff0);

        container.GetTotal(StatId.WeaponSkill).ShouldBe(200);
    }

    [Fact]
    public void Container_add_remove_multiplier_round_trips()
    {
        var container = MakeContainer();
        container.SetBase(StatId.Armor, 1000);
        container.AddBonusMultiplier(StatId.Armor, 1.5f, BuffClass.Tactic);

        container.GetTotal(StatId.Armor).ShouldBe(1500);

        container.RemoveBonusMultiplier(StatId.Armor, 1.5f, BuffClass.Tactic);

        container.GetTotal(StatId.Armor).ShouldBe(1000);
    }

    // ── StatContainer: Dirty flag ───────────────────────────────────────

    [Fact]
    public void Container_starts_not_dirty()
    {
        var container = MakeContainer();
        container.IsDirty.ShouldBeFalse();
    }

    [Fact]
    public void Container_mutations_set_dirty()
    {
        var container = MakeContainer();

        container.SetBase(StatId.Strength, 100);
        container.IsDirty.ShouldBeTrue();
    }

    [Fact]
    public void Container_flush_clears_dirty()
    {
        var container = MakeContainer();
        container.SetBase(StatId.Strength, 100);

        container.Flush();

        container.IsDirty.ShouldBeFalse();
    }

    [Fact]
    public void Container_flush_noop_when_clean()
    {
        var container = MakeContainer();
        uint callbackCount = 0;
        container.OnMaxHealthChanged = _ => callbackCount++;

        container.Flush();

        callbackCount.ShouldBe(0u);
    }

    // ── StatContainer: Derived stat — MaxHealth ─────────────────────────

    [Fact]
    public void Container_flush_computes_max_health_from_wounds()
    {
        var container = MakeContainer();
        uint receivedMaxHealth = 0;
        container.OnMaxHealthChanged = val => receivedMaxHealth = val;

        container.SetBase(StatId.Wounds, 50);
        container.Flush();

        receivedMaxHealth.ShouldBe(500u); // 50 * 10
    }

    [Fact]
    public void Container_flush_max_health_floors_at_1()
    {
        var container = MakeContainer();
        uint receivedMaxHealth = 0;
        container.OnMaxHealthChanged = val => receivedMaxHealth = val;

        // All wounds are zero → max health should floor at 1, not 0.
        container.MarkDirty();
        container.Flush();

        receivedMaxHealth.ShouldBe(1u);
    }

    [Fact]
    public void Container_flush_max_health_includes_buff_modifiers()
    {
        var container = MakeContainer();
        uint receivedMaxHealth = 0;
        container.OnMaxHealthChanged = val => receivedMaxHealth = val;

        container.SetBase(StatId.Wounds, 50);
        container.AddBonus(StatId.Wounds, 10, BuffClass.Tactic);
        container.Flush();

        receivedMaxHealth.ShouldBe(600u); // (50 + 10) * 10
    }

    // ── StatContainer: Base stat flooring ───────────────────────────────

    [Fact]
    public void Container_base_stats_floor_at_zero()
    {
        var container = MakeContainer();
        container.SetBase(StatId.Strength, 10);
        container.AddReduction(StatId.Strength, 100, BuffClass.Tactic);

        // GetTotal for base stats should floor at 0.
        container.GetTotal(StatId.Strength).ShouldBe(0);
    }

    [Fact]
    public void Container_non_base_stats_can_go_negative()
    {
        var container = MakeContainer();
        container.SetBase(StatId.Armor, 10);
        container.AddReduction(StatId.Armor, 100, BuffClass.Tactic);

        // Non-base stats don't floor.
        container.GetTotal(StatId.Armor).ShouldBe(-90);
    }

    // ── StatContainer: ClearAllModifiers ────────────────────────────────

    [Fact]
    public void Container_clear_all_modifiers_preserves_base()
    {
        var container = MakeContainer();
        container.SetBase(StatId.Strength, 100);
        container.SetRenown(StatId.Strength, 20);
        container.AddBonus(StatId.Strength, 50, BuffClass.Buff0);
        container.SetItemBonus(StatId.Strength, 30);

        container.ClearAllModifiers();

        // Only base and renown survive.
        container.GetTotal(StatId.Strength).ShouldBe(120);
    }

    // ── StatContainer: ResetAll ─────────────────────────────────────────

    [Fact]
    public void Container_reset_all_zeroes_everything()
    {
        var container = MakeContainer();
        container.SetBase(StatId.Strength, 100);
        container.SetRenown(StatId.Strength, 20);

        container.ResetAll();

        container.GetTotal(StatId.Strength).ShouldBe(0);
    }

    // ── StatContainer: Indexer access ───────────────────────────────────

    [Fact]
    public void Container_indexer_provides_direct_entry_access()
    {
        var container = MakeContainer();
        container.SetBase(StatId.Toughness, 75);

        var entry = container[StatId.Toughness];
        entry.Base.ShouldBe(75);
    }

    // ── StatContainer: Multiple stats independent ───────────────────────

    [Fact]
    public void Container_different_stats_are_independent()
    {
        var container = MakeContainer();
        container.SetBase(StatId.Strength, 100);
        container.SetBase(StatId.Toughness, 200);
        container.AddBonus(StatId.Strength, 50, BuffClass.Tactic);

        container.GetTotal(StatId.Strength).ShouldBe(150);
        container.GetTotal(StatId.Toughness).ShouldBe(200);
    }
}
