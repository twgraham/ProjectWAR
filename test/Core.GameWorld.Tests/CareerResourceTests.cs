using Core.Domain.Entities;
using Core.GameWorld.Combat.Abilities;
using Core.GameWorld.Combat.Career;
using Core.GameWorld.Entities;
using Core.GameWorld.Stats;
using Shouldly;

namespace Core.GameWorld.Tests;

/// <summary>
/// Unit tests for Step 7: Career Resource archetypes — each archetype's
/// generate/consume/decay cycle plus AbilityCastService integration.
/// </summary>
public class CareerResourceTests
{
    // ═══════════════════════════════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════════════════════════════

    private static PlayerEntity MakeUnit(ushort id = 1, uint maxHealth = 1000)
    {
        var entity = new PlayerEntity(id,
            new Character { CharacterId = id, Name = $"Unit{id}" }, maxHealth);
        entity.Level = 40;
        entity.ActionPoints = 250;
        entity.Stats.SetBase(StatId.Wounds, (int)(maxHealth / 10));
        return entity;
    }

    private static void AttachResource(UnitEntity entity, ICareerResource resource)
    {
        entity.Attach(new CareerResourceComponent(resource));
    }

    // ═══════════════════════════════════════════════════════════════════
    //  ContinuousResource
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Continuous_starts_at_zero()
    {
        var res = new ContinuousResource(new ContinuousResourceConfig());
        res.Current.ShouldBe((byte)0);
        res.Max.ShouldBe((byte)100);
        res.Level.ShouldBe((byte)0);
    }

    [Fact]
    public void Continuous_generate_clamps_to_max()
    {
        var res = new ContinuousResource(new ContinuousResourceConfig { Max = 100 });
        res.Generate(120);
        res.Current.ShouldBe((byte)100);
    }

    [Fact]
    public void Continuous_consume_returns_false_when_insufficient()
    {
        var res = new ContinuousResource(new ContinuousResourceConfig());
        res.Generate(10);
        res.Consume(20).ShouldBeFalse();
        res.Current.ShouldBe((byte)10); // unchanged
    }

    [Fact]
    public void Continuous_consume_deducts_on_success()
    {
        var res = new ContinuousResource(new ContinuousResourceConfig());
        res.Generate(50);
        res.Consume(20).ShouldBeTrue();
        res.Current.ShouldBe((byte)30);
    }

    [Fact]
    public void Continuous_has_resource_checks_gte()
    {
        var res = new ContinuousResource(new ContinuousResourceConfig());
        res.Generate(50);
        res.HasResource(50).ShouldBeTrue();
        res.HasResource(51).ShouldBeFalse();
    }

    [Fact]
    public void Continuous_level_thresholds()
    {
        var res = new ContinuousResource(new ContinuousResourceConfig
        {
            LevelThresholds = [25, 50, 75, 100]
        });

        res.Generate(10);
        res.Level.ShouldBe((byte)0);

        res.Generate(15); // now 25
        res.Level.ShouldBe((byte)1);

        res.Generate(25); // now 50
        res.Level.ShouldBe((byte)2);

        res.Generate(50); // now 100
        res.Level.ShouldBe((byte)4);
    }

    [Fact]
    public void Continuous_level_changed_callback()
    {
        byte capturedLevel = 255;
        var res = new ContinuousResource(new ContinuousResourceConfig
        {
            LevelThresholds = [25, 50, 75, 100],
            OnLevelChanged = (_, lvl) => capturedLevel = lvl
        });

        res.Generate(25);
        capturedLevel.ShouldBe((byte)1);

        res.Generate(75); // now 100 → level 4
        capturedLevel.ShouldBe((byte)4);

        res.Consume(80); // now 20 → level 0
        capturedLevel.ShouldBe((byte)0);
    }

    [Fact]
    public void Continuous_decay_after_idle_timeout()
    {
        var res = new ContinuousResource(new ContinuousResourceConfig
        {
            Max = 100,
            DecayRate = 20,
            DecayIntervalMs = 2000,
            IdleTimeoutMs = 5000,
        });

        res.Generate(100);
        res.NotifyAction(0);

        // Before idle timeout — no decay
        res.Update(4000);
        res.Current.ShouldBe((byte)100);

        // After idle timeout + first decay interval
        res.Update(7000);
        res.Current.ShouldBe((byte)80);

        // Second decay tick
        res.Update(9000);
        res.Current.ShouldBe((byte)60);
    }

    [Fact]
    public void Continuous_no_decay_when_idle_timeout_zero_and_recent_action()
    {
        var res = new ContinuousResource(new ContinuousResourceConfig
        {
            IdleTimeoutMs = 0, // decay starts immediately
            DecayRate = 10,
            DecayIntervalMs = 1000,
        });

        res.Generate(50);

        // IdleTimeoutMs=0 means the idle check is skipped, decays immediately
        res.Update(1000);
        res.Current.ShouldBe((byte)40);
    }

    [Fact]
    public void Continuous_decay_stops_at_zero()
    {
        var res = new ContinuousResource(new ContinuousResourceConfig
        {
            Max = 100,
            DecayRate = 60,
            DecayIntervalMs = 1000,
            IdleTimeoutMs = 0,
        });

        res.Generate(50);
        res.Update(1000);
        res.Current.ShouldBe((byte)0);

        // Further ticks are no-ops
        res.Update(2000);
        res.Current.ShouldBe((byte)0);
    }

    [Fact]
    public void Continuous_set_resource()
    {
        var res = new ContinuousResource(new ContinuousResourceConfig
        {
            Max = 100,
            LevelThresholds = [25, 50, 75, 100]
        });

        res.SetResource(75);
        res.Current.ShouldBe((byte)75);
        res.Level.ShouldBe((byte)3);

        res.SetResource(200); // clamps to max
        res.Current.ShouldBe((byte)100);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  ComboResource
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Combo_starts_at_zero()
    {
        var res = new ComboResource(new ComboResourceConfig { Max = 5 });
        res.Current.ShouldBe((byte)0);
        res.Max.ShouldBe((byte)5);
        res.Level.ShouldBe((byte)0);
    }

    [Fact]
    public void Combo_generate_increments()
    {
        var res = new ComboResource(new ComboResourceConfig { Max = 5 });
        res.Generate(1);
        res.Current.ShouldBe((byte)1);
        res.Generate(1);
        res.Current.ShouldBe((byte)2);
    }

    [Fact]
    public void Combo_generate_clamps_without_wrap()
    {
        var res = new ComboResource(new ComboResourceConfig { Max = 5, WrapOnOverflow = false });
        res.Generate(6);
        res.Current.ShouldBe((byte)5); // clamped
    }

    [Fact]
    public void Combo_generate_wraps_to_one()
    {
        var res = new ComboResource(new ComboResourceConfig { Max = 2, WrapOnOverflow = true });
        res.Generate(1); // 1
        res.Generate(1); // 2
        res.Generate(1); // wraps to 1
        res.Current.ShouldBe((byte)1);
    }

    [Fact]
    public void Combo_consume_all_resets_to_zero()
    {
        var res = new ComboResource(new ComboResourceConfig
        {
            Max = 5,
            ConsumeAll = true
        });

        res.Generate(3);
        res.Consume(1).ShouldBeTrue(); // consumes all, not just 1
        res.Current.ShouldBe((byte)0);
    }

    [Fact]
    public void Combo_consume_partial()
    {
        var res = new ComboResource(new ComboResourceConfig
        {
            Max = 5,
            ConsumeAll = false
        });

        res.Generate(4);
        res.Consume(2).ShouldBeTrue();
        res.Current.ShouldBe((byte)2);
    }

    [Fact]
    public void Combo_exact_match_mode()
    {
        var res = new ComboResource(new ComboResourceConfig
        {
            Max = 2,
            ExactMatch = true
        });

        res.Generate(1);
        res.HasResource(1).ShouldBeTrue();
        res.HasResource(2).ShouldBeFalse(); // not exact

        res.Generate(1); // now 2
        res.HasResource(2).ShouldBeTrue();
        res.HasResource(1).ShouldBeFalse(); // not exact
    }

    [Fact]
    public void Combo_gte_mode_default()
    {
        var res = new ComboResource(new ComboResourceConfig { Max = 5 });
        res.Generate(3);
        res.HasResource(1).ShouldBeTrue();
        res.HasResource(3).ShouldBeTrue();
        res.HasResource(4).ShouldBeFalse();
    }

    [Fact]
    public void Combo_timeout_resets()
    {
        var res = new ComboResource(new ComboResourceConfig
        {
            Max = 5,
            TimeoutMs = 10_000
        });

        res.Generate(3);
        res.NotifyAction(0);

        res.Update(9000);
        res.Current.ShouldBe((byte)3); // not yet timed out

        res.Update(10_000);
        res.Current.ShouldBe((byte)0); // timed out
    }

    [Fact]
    public void Combo_no_timeout_when_zero()
    {
        var res = new ComboResource(new ComboResourceConfig
        {
            Max = 5,
            TimeoutMs = 0 // disabled
        });

        res.Generate(3);
        res.Update(100_000); // very long time
        res.Current.ShouldBe((byte)3); // no timeout
    }

    [Fact]
    public void Combo_level_equals_current()
    {
        var res = new ComboResource(new ComboResourceConfig { Max = 5 });
        res.Generate(4);
        res.Level.ShouldBe((byte)4);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  StanceResource
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Stance_starts_at_none()
    {
        var res = new StanceResource(new StanceResourceConfig { StanceCount = 3 });
        res.Current.ShouldBe((byte)0);
        res.Max.ShouldBe((byte)3);
    }

    [Fact]
    public void Stance_set_and_query()
    {
        var res = new StanceResource(new StanceResourceConfig { StanceCount = 3 });
        res.SetResource(2);
        res.Current.ShouldBe((byte)2);
        res.Level.ShouldBe((byte)2);
    }

    [Fact]
    public void Stance_has_resource_direct_match()
    {
        var res = new StanceResource(new StanceResourceConfig { StanceCount = 3 });
        res.SetResource(2);

        res.HasResource(2).ShouldBeTrue();
        res.HasResource(1).ShouldBeFalse();
        res.HasResource(3).ShouldBeFalse();
        res.HasResource(0).ShouldBeTrue(); // 0 = no requirement
    }

    [Fact]
    public void Stance_composite_mask()
    {
        var res = new StanceResource(new StanceResourceConfig
        {
            StanceCount = 3,
            CompositeMasks = new Dictionary<int, HashSet<byte>>
            {
                [4] = [1, 2],       // cost 4 = stance 1 or 2
                [5] = [2, 3],       // cost 5 = stance 2 or 3
                [7] = [1, 2, 3]     // cost 7 = any stance
            }
        });

        res.SetResource(1);
        res.HasResource(4).ShouldBeTrue();  // 1 ∈ {1,2}
        res.HasResource(5).ShouldBeFalse(); // 1 ∉ {2,3}
        res.HasResource(7).ShouldBeTrue();  // 1 ∈ {1,2,3}

        res.SetResource(3);
        res.HasResource(4).ShouldBeFalse(); // 3 ∉ {1,2}
        res.HasResource(5).ShouldBeTrue();  // 3 ∈ {2,3}
    }

    [Fact]
    public void Stance_consume_always_true_if_has()
    {
        var res = new StanceResource(new StanceResourceConfig { StanceCount = 3 });
        res.SetResource(2);
        res.Consume(2).ShouldBeTrue();
        res.Current.ShouldBe((byte)2); // not consumed — stances persist
    }

    [Fact]
    public void Stance_generate_is_noop()
    {
        var res = new StanceResource(new StanceResourceConfig { StanceCount = 3 });
        res.Generate(5);
        res.Current.ShouldBe((byte)0); // unchanged
    }

    [Fact]
    public void Stance_changed_callback()
    {
        byte captured = 255;
        var res = new StanceResource(new StanceResourceConfig
        {
            StanceCount = 3,
            OnStanceChanged = (_, val) => captured = val
        });

        res.SetResource(2);
        captured.ShouldBe((byte)2);

        res.SetResource(2); // same stance — no callback
        captured.ShouldBe((byte)2); // not re-fired

        res.SetResource(1);
        captured.ShouldBe((byte)1);
    }

    [Fact]
    public void Stance_rejects_invalid_value()
    {
        var res = new StanceResource(new StanceResourceConfig { StanceCount = 3 });
        res.SetResource(5); // out of range
        res.Current.ShouldBe((byte)0); // unchanged
    }

    // ═══════════════════════════════════════════════════════════════════
    //  BalanceNeedleResource
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Needle_starts_at_center()
    {
        var res = new BalanceNeedleResource(new BalanceNeedleConfig { Max = 5 });
        res.Current.ShouldBe((byte)5); // center
        res.Max.ShouldBe((byte)10);    // 2 × Max
        res.Center.ShouldBe((byte)5);
        res.Level.ShouldBe((byte)0);   // distance from center = 0
        res.DamageSideDepth.ShouldBe((byte)0);
        res.HealSideDepth.ShouldBe((byte)0);
    }

    [Fact]
    public void Needle_generate_pushes_damage_side()
    {
        var res = new BalanceNeedleResource(new BalanceNeedleConfig { Max = 5 });
        res.Generate(1); // push toward damage (5 → 4)
        res.Current.ShouldBe((byte)4);
        res.DamageSideDepth.ShouldBe((byte)1);
        res.HealSideDepth.ShouldBe((byte)0);
        res.Level.ShouldBe((byte)1);

        res.Generate(1); // 4 → 3
        res.Current.ShouldBe((byte)3);
        res.DamageSideDepth.ShouldBe((byte)2);
        res.Level.ShouldBe((byte)2);
    }

    [Fact]
    public void Needle_consume_pushes_heal_side()
    {
        var res = new BalanceNeedleResource(new BalanceNeedleConfig { Max = 5 });
        res.Consume(1); // center → 6 (heal side)
        res.Current.ShouldBe((byte)6);
        res.HealSideDepth.ShouldBe((byte)1);
        res.DamageSideDepth.ShouldBe((byte)0);
        res.Level.ShouldBe((byte)1);
    }

    [Fact]
    public void Needle_clamps_at_extremes()
    {
        var res = new BalanceNeedleResource(new BalanceNeedleConfig { Max = 5 });

        // Push all the way to damage side
        for (int i = 0; i < 10; i++) res.Generate(1);
        res.Current.ShouldBe((byte)1); // clamped at 1
        res.DamageSideDepth.ShouldBe((byte)4);

        // Reset to center, push all the way to heal side
        res.SetResource(5);
        for (int i = 0; i < 10; i++) res.Consume(1);
        res.Current.ShouldBe((byte)10); // clamped at 2 × Max
        res.HealSideDepth.ShouldBe((byte)5);
    }

    [Fact]
    public void Needle_has_resource_always_true()
    {
        var res = new BalanceNeedleResource(new BalanceNeedleConfig { Max = 5 });
        res.HasResource(0).ShouldBeTrue();
        res.HasResource(99).ShouldBeTrue(); // never blocks
    }

    [Fact]
    public void Needle_decay_toward_center()
    {
        var res = new BalanceNeedleResource(new BalanceNeedleConfig
        {
            Max = 5,
            IdleTimeoutMs = 5000,
            DecayIntervalMs = 2000,
        });

        // Push to full damage side: position 1
        for (int i = 0; i < 4; i++) res.Generate(1);
        res.Current.ShouldBe((byte)1);
        res.NotifyAction(0);

        // Before idle timeout
        res.Update(4000);
        res.Current.ShouldBe((byte)1);

        // After idle timeout + first decay
        res.Update(7000);
        res.Current.ShouldBe((byte)2); // one step toward center

        // Second decay
        res.Update(9000);
        res.Current.ShouldBe((byte)3);
    }

    [Fact]
    public void Needle_decay_from_heal_side()
    {
        var res = new BalanceNeedleResource(new BalanceNeedleConfig
        {
            Max = 5,
            IdleTimeoutMs = 0,
            DecayIntervalMs = 1000,
        });

        // Push two steps heal-side
        res.Consume(1);
        res.Consume(1);
        res.Current.ShouldBe((byte)7);

        res.Update(1000);
        res.Current.ShouldBe((byte)6); // one step toward center (5)

        res.Update(2000);
        res.Current.ShouldBe((byte)5); // at center

        res.Update(3000);
        res.Current.ShouldBe((byte)5); // stays at center
    }

    [Fact]
    public void Needle_level_changed_callback()
    {
        byte capturedLevel = 255;
        var res = new BalanceNeedleResource(new BalanceNeedleConfig
        {
            Max = 5,
            OnLevelChanged = (_, lvl) => capturedLevel = lvl
        });

        res.Generate(1); // level 0→1
        capturedLevel.ShouldBe((byte)1);

        res.Generate(1); // level 1→2
        capturedLevel.ShouldBe((byte)2);

        res.SetResource(5); // back to center, level 0
        capturedLevel.ShouldBe((byte)0);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  StancedContinuousResource
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void StancedContinuous_starts_at_initial_value()
    {
        var res = new StancedContinuousResource(new StancedContinuousConfig
        {
            Max = 250,
            InitialValue = 250
        });

        res.Current.ShouldBe((byte)250);
        res.Max.ShouldBe((byte)250);
    }

    [Fact]
    public void StancedContinuous_consume_and_generate()
    {
        var res = new StancedContinuousResource(new StancedContinuousConfig
        {
            Max = 250,
            InitialValue = 100
        });

        res.Consume(30).ShouldBeTrue();
        res.Current.ShouldBe((byte)70);

        res.Generate(20);
        res.Current.ShouldBe((byte)90);
    }

    [Fact]
    public void StancedContinuous_consume_fails_when_insufficient()
    {
        var res = new StancedContinuousResource(new StancedContinuousConfig
        {
            Max = 250,
            InitialValue = 10
        });

        res.Consume(20).ShouldBeFalse();
        res.Current.ShouldBe((byte)10);
    }

    [Fact]
    public void StancedContinuous_set_resource_switches_stance()
    {
        var res = new StancedContinuousResource(new StancedContinuousConfig
        {
            Max = 250,
            InitialValue = 100,
            StanceCount = 3
        });

        res.SetResource(2); // values 1-3 switch stance
        res.Stance.ShouldBe((byte)2);
        res.Current.ShouldBe((byte)100); // unchanged
    }

    [Fact]
    public void StancedContinuous_stance_changed_callback()
    {
        byte captured = 255;
        var res = new StancedContinuousResource(new StancedContinuousConfig
        {
            StanceCount = 3,
            OnStanceChanged = (_, s) => captured = s
        });

        res.SetStance(2);
        captured.ShouldBe((byte)2);
    }

    [Fact]
    public void StancedContinuous_in_combat_drain()
    {
        var res = new StancedContinuousResource(new StancedContinuousConfig
        {
            Max = 250,
            InitialValue = 100,
            StanceCount = 3,
            InCombatDrainPerStance = [0, 5, 0, 0], // stance 1 drains 5/s
            TickIntervalMs = 1000
        });

        res.SetStance(1);
        res.InCombat = true;

        res.Update(1000);
        res.Current.ShouldBe((byte)95); // drained 5

        res.Update(2000);
        res.Current.ShouldBe((byte)90);
    }

    [Fact]
    public void StancedContinuous_ooc_regen()
    {
        var res = new StancedContinuousResource(new StancedContinuousConfig
        {
            Max = 250,
            InitialValue = 200,
            OutOfCombatRegenPerSec = 20,
            TickIntervalMs = 1000
        });

        res.InCombat = false;

        res.Update(1000);
        res.Current.ShouldBe((byte)220);

        res.Update(2000);
        res.Current.ShouldBe((byte)240);

        res.Update(3000);
        res.Current.ShouldBe((byte)250); // clamped to max
    }

    [Fact]
    public void StancedContinuous_level_from_conversion_factor()
    {
        var res = new StancedContinuousResource(new StancedContinuousConfig
        {
            Max = 250,
            InitialValue = 100,
            LevelConversionFactor = 0.16f
        });

        // 100 × 0.16 = 16
        res.Level.ShouldBe((byte)16);

        res.Consume(50); // 50 × 0.16 = 8
        res.Level.ShouldBe((byte)8);
    }

    [Fact]
    public void StancedContinuous_level_changed_callback()
    {
        byte captured = 255;
        var res = new StancedContinuousResource(new StancedContinuousConfig
        {
            Max = 250,
            InitialValue = 100,
            LevelConversionFactor = 0.16f,
            OnLevelChanged = (_, lvl) => captured = lvl
        });

        // Initial level = 16 via constructor RecalcLevel
        captured.ShouldBe((byte)16);

        res.Generate(50); // 150 × 0.16 = 24
        captured.ShouldBe((byte)24);
    }

    [Fact]
    public void StancedContinuous_no_drain_in_stance_zero()
    {
        var res = new StancedContinuousResource(new StancedContinuousConfig
        {
            Max = 250,
            InitialValue = 100,
            InCombatDrainPerStance = [0, 5, 0, 0],
            TickIntervalMs = 1000
        });

        res.InCombat = true;
        // Stance = 0, in combat drain for stance 0 = 0
        res.Update(1000);
        res.Current.ShouldBe((byte)100); // no drain
    }

    // ═══════════════════════════════════════════════════════════════════
    //  CareerResourceComponent
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Component_ticks_resource_via_entity_update()
    {
        var unit = MakeUnit();
        var res = new ContinuousResource(new ContinuousResourceConfig
        {
            Max = 100,
            DecayRate = 10,
            DecayIntervalMs = 1000,
            IdleTimeoutMs = 0,
        });
        res.Generate(50);

        AttachResource(unit, res);

        // Entity.Update ticks all ITickable components
        unit.Update(1000);
        res.Current.ShouldBe((byte)40); // decayed by 10
    }

    [Fact]
    public void Component_exposes_resource()
    {
        var unit = MakeUnit();
        var res = new ComboResource(new ComboResourceConfig { Max = 5 });
        AttachResource(unit, res);

        var comp = unit.TryGet<CareerResourceComponent>();
        comp.ShouldNotBeNull();
        comp.Resource.ShouldBeSameAs(res);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  AbilityCastService Integration
    // ═══════════════════════════════════════════════════════════════════

    private static AbilityDefinition MakeAbilityDef(
        ushort entry = 1000, byte apCost = 0, short specialCost = 0,
        IReadOnlyList<AbilityCommandDefinition>? commands = null)
    {
        return new AbilityDefinition
        {
            Entry = entry,
            Name = $"TestAbility{entry}",
            ApCost = apCost,
            SpecialCost = specialCost,
            CastTime = 0,
            Cooldown = 0,
            AbilityType = AbilityType.Melee,
            TargetType = CommandTargetType.Caster,
            Commands = commands ?? [],
        };
    }

    private static (PlayerEntity entity, AbilityComponent comp) MakeCaster(
        ushort id = 1, uint maxHealth = 1000)
    {
        var entity = MakeUnit(id, maxHealth);
        var comp = new AbilityComponent();
        entity.Attach(comp);
        return (entity, comp);
    }

    [Fact]
    public void Initiate_fails_with_NotEnoughResource_when_no_component()
    {
        var (caster, comp) = MakeCaster();
        var service = new AbilityCastService();
        var def = MakeAbilityDef(specialCost: 10);

        var result = service.TryInitiate(comp, def, caster, null, 0, out var failure);
        result.ShouldBeNull();
        failure.ShouldBe(AbilityFailure.NotEnoughResource);
    }

    [Fact]
    public void Initiate_fails_with_NotEnoughResource_when_insufficient()
    {
        var (caster, comp) = MakeCaster();
        var res = new ContinuousResource(new ContinuousResourceConfig());
        res.Generate(5); // only 5
        AttachResource(caster, res);

        var service = new AbilityCastService();
        var def = MakeAbilityDef(specialCost: 10);

        var result = service.TryInitiate(comp, def, caster, null, 0, out var failure);
        result.ShouldBeNull();
        failure.ShouldBe(AbilityFailure.NotEnoughResource);
    }

    [Fact]
    public void Initiate_succeeds_when_resource_sufficient()
    {
        var (caster, comp) = MakeCaster();
        var res = new ContinuousResource(new ContinuousResourceConfig());
        res.Generate(50);
        AttachResource(caster, res);

        var service = new AbilityCastService();
        var def = MakeAbilityDef(specialCost: 20);

        var result = service.TryInitiate(comp, def, caster, null, 0, out var failure);
        result.ShouldNotBeNull();
        failure.ShouldBe(AbilityFailure.Ok);
    }

    [Fact]
    public void CompleteCast_consumes_career_resource()
    {
        var (caster, comp) = MakeCaster();
        var res = new ContinuousResource(new ContinuousResourceConfig());
        res.Generate(50);
        AttachResource(caster, res);

        var service = new AbilityCastService();
        var def = MakeAbilityDef(specialCost: 20);

        var ctx = service.TryInitiate(comp, def, caster, null, 0, out _);
        ctx.ShouldNotBeNull();

        service.ConfirmCast(comp, ctx, 0); // instant cast → CompleteCast

        res.Current.ShouldBe((byte)30); // 50 - 20
    }

    [Fact]
    public void Zero_special_cost_skips_resource_check()
    {
        var (caster, comp) = MakeCaster();
        // No career resource attached, but SpecialCost = 0 → should pass
        var service = new AbilityCastService();
        var def = MakeAbilityDef(specialCost: 0);

        var result = service.TryInitiate(comp, def, caster, null, 0, out var failure);
        result.ShouldNotBeNull();
        failure.ShouldBe(AbilityFailure.Ok);
    }

    [Fact]
    public void ModifyCareerResource_command_generates()
    {
        var (caster, comp) = MakeCaster();
        var res = new ContinuousResource(new ContinuousResourceConfig());
        res.Generate(10);
        AttachResource(caster, res);

        var cmd = new AbilityCommandDefinition
        {
            EffectType = AbilityEffectType.ModifyCareerResource,
            PrimaryValue = 25,
        };

        var service = new AbilityCastService();
        var def = MakeAbilityDef(commands: [cmd]);

        var ctx = service.TryInitiate(comp, def, caster, null, 0, out _);
        ctx.ShouldNotBeNull();
        service.ConfirmCast(comp, ctx, 0);

        res.Current.ShouldBe((byte)35); // 10 + 25
    }

    [Fact]
    public void ModifyCareerResource_command_negative_consumes()
    {
        var (caster, comp) = MakeCaster();
        var res = new ContinuousResource(new ContinuousResourceConfig());
        res.Generate(50);
        AttachResource(caster, res);

        var cmd = new AbilityCommandDefinition
        {
            EffectType = AbilityEffectType.ModifyCareerResource,
            PrimaryValue = -15,
        };

        var service = new AbilityCastService();
        var def = MakeAbilityDef(commands: [cmd]);

        var ctx = service.TryInitiate(comp, def, caster, null, 0, out _);
        ctx.ShouldNotBeNull();
        service.ConfirmCast(comp, ctx, 0);

        res.Current.ShouldBe((byte)35); // 50 - 15
    }

    [Fact]
    public void Stance_resource_check_passes_in_correct_stance()
    {
        var (caster, comp) = MakeCaster();
        var res = new StanceResource(new StanceResourceConfig { StanceCount = 3 });
        res.SetResource(2);
        AttachResource(caster, res);

        var service = new AbilityCastService();
        var def = MakeAbilityDef(specialCost: 2); // requires stance 2

        var result = service.TryInitiate(comp, def, caster, null, 0, out var failure);
        result.ShouldNotBeNull();
        failure.ShouldBe(AbilityFailure.Ok);
    }

    [Fact]
    public void Stance_resource_check_fails_in_wrong_stance()
    {
        var (caster, comp) = MakeCaster();
        var res = new StanceResource(new StanceResourceConfig { StanceCount = 3 });
        res.SetResource(1); // in stance 1
        AttachResource(caster, res);

        var service = new AbilityCastService();
        var def = MakeAbilityDef(specialCost: 2); // requires stance 2

        var result = service.TryInitiate(comp, def, caster, null, 0, out var failure);
        result.ShouldBeNull();
        failure.ShouldBe(AbilityFailure.NotEnoughResource);
    }

    [Fact]
    public void BalanceNeedle_never_blocks_cast()
    {
        var (caster, comp) = MakeCaster();
        var res = new BalanceNeedleResource(new BalanceNeedleConfig { Max = 5 });
        AttachResource(caster, res);

        var service = new AbilityCastService();
        var def = MakeAbilityDef(specialCost: 99); // high cost

        // Balance needle always returns HasResource = true
        var result = service.TryInitiate(comp, def, caster, null, 0, out var failure);
        result.ShouldNotBeNull();
        failure.ShouldBe(AbilityFailure.Ok);
    }
}
