using Core.Domain.Entities;
using Core.GameWorld.Combat;
using Core.GameWorld.Combat.Buffs;
using Core.GameWorld.Combat.Buffs.Effects;
using Core.GameWorld.Entities;
using Core.GameWorld.Stats;
using Shouldly;

namespace Core.GameWorld.Tests;

/// <summary>
/// Unit tests for Step 6: concrete <see cref="IBuffEffect"/> implementations,
/// <see cref="BuffEffectFactory"/>, and end-to-end buff lifecycle integration.
/// </summary>
public class BuffEffectTests
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
        // Set Wounds so Stats.Flush() preserves maxHealth (Wounds × 10).
        entity.Stats.SetBase(StatId.Wounds, (int)(maxHealth / 10));
        return entity;
    }

    private static BuffContainer MakeContainer(UnitEntity owner)
    {
        var c = owner.Buffs;
        c.EffectFactory = BuffEffectFactory.Default;
        return c;
    }

    private static BuffDefinition MakeDef(
        ushort entry = 100,
        uint durationMs = 10_000,
        ushort intervalMs = 0,
        byte maxStacks = 1,
        CrowdControlFlags cc = CrowdControlFlags.None,
        BuffClass buffClass = BuffClass.Buff0,
        List<BuffEffectDefinition>? effects = null)
    {
        return new BuffDefinition
        {
            Entry = entry,
            Name = $"Buff{entry}",
            BuffClass = buffClass,
            DurationMs = durationMs,
            IntervalMs = intervalMs,
            MaxStacks = maxStacks,
            InitialStacks = 1,
            CrowdControl = cc,
            Effects = effects ?? [],
        };
    }

    private static BuffEffectDefinition MakeEffectDef(
        BuffEffectType type,
        BuffPhase invokeOn = BuffPhase.Start,
        StatId statId = StatId.None,
        int primary = 0,
        int secondary = 0,
        int tertiary = 0,
        CombatEventType eventSub = CombatEventType.None,
        CombatEventPriority priority = CombatEventPriority.FinalReaction,
        BuffClass? classOverride = null)
    {
        return new BuffEffectDefinition
        {
            EffectType = type,
            InvokeOn = invokeOn,
            StatId = statId,
            PrimaryValue = primary,
            SecondaryValue = secondary,
            TertiaryValue = tertiary,
            EventSubscription = eventSub,
            EventPriority = priority,
            BuffClassOverride = classOverride,
        };
    }

    /// <summary>Applies the buff and runs Update to drain the pending queue.</summary>
    private static void ApplyBuff(BuffContainer container, BuffDefinition def,
        UnitEntity? caster = null, byte buffLevel = 40)
    {
        container.QueueBuff(def, caster, buffLevel: buffLevel);
        container.Update(0);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  StatModifierEffect
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void StatModifier_adds_bonus_on_start_and_removes_on_end()
    {
        var unit = MakeUnit();
        var container = MakeContainer(unit);

        var effectDef = MakeEffectDef(BuffEffectType.StatModifier,
            statId: StatId.Strength, primary: 100, secondary: 100);
        var def = MakeDef(effects: [effectDef]);

        int baseStat = unit.Stats.GetTotal(StatId.Strength);
        ApplyBuff(container, def, unit);

        // Buff level 40 → lerp(100,100, 1.0) = 100 × stack=1 = 100
        unit.Stats.GetTotal(StatId.Strength).ShouldBe(baseStat + 100);

        // Remove → stat reverts
        container.RemoveByEntry(def.Entry);
        unit.Stats.GetTotal(StatId.Strength).ShouldBe(baseStat);
    }

    [Fact]
    public void StatModifier_negative_value_adds_reduction()
    {
        var unit = MakeUnit();
        unit.Stats.SetBase(StatId.Toughness, 200);
        unit.Stats.Flush();
        var container = MakeContainer(unit);

        var effectDef = MakeEffectDef(BuffEffectType.StatModifier,
            statId: StatId.Toughness, primary: -50, secondary: -50);
        var def = MakeDef(effects: [effectDef]);

        ApplyBuff(container, def, unit);

        // Original 200 - 50 reduction = 150
        unit.Stats.GetTotal(StatId.Toughness).ShouldBe(150);

        container.RemoveByEntry(def.Entry);
        unit.Stats.GetTotal(StatId.Toughness).ShouldBe(200);
    }

    [Fact]
    public void StatModifier_level_interpolation()
    {
        var unit = MakeUnit();
        unit.Level = 20;
        var container = MakeContainer(unit);

        // PrimaryValue=0 (level 1), SecondaryValue=390 (level 40)
        var effectDef = MakeEffectDef(BuffEffectType.StatModifier,
            statId: StatId.Strength, primary: 0, secondary: 390);
        var def = MakeDef(effects: [effectDef]);

        // buffLevel = 20 → lerp(0, 390, (20-1)/39) = 390 × 19/39 = 190
        ApplyBuff(container, def, unit, buffLevel: 20);
        unit.Stats.GetTotal(StatId.Strength).ShouldBe(190);
    }

    [Fact]
    public void StatModifier_uses_class_override()
    {
        var unit = MakeUnit();
        var container = MakeContainer(unit);

        var effectDef = MakeEffectDef(BuffEffectType.StatModifier,
            statId: StatId.Strength, primary: 50, secondary: 50,
            classOverride: BuffClass.Tactic);
        // Buff def says Buff0, but effect overrides to Tactic
        var def = MakeDef(effects: [effectDef], buffClass: BuffClass.Buff0);

        ApplyBuff(container, def, unit);

        // Tactic class stacks additively, so value should be applied
        unit.Stats.GetTotal(StatId.Strength).ShouldBe(50);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  PercentageStatModifierEffect
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void PercentageStat_adds_bonus_multiplier_and_removes()
    {
        var unit = MakeUnit();
        unit.Stats.SetBase(StatId.Strength, 100);
        unit.Stats.Flush();
        var container = MakeContainer(unit);

        // +20% at all levels
        var effectDef = MakeEffectDef(BuffEffectType.PercentageStatModifier,
            statId: StatId.Strength, primary: 20, secondary: 20);
        var def = MakeDef(effects: [effectDef]);

        ApplyBuff(container, def, unit);
        unit.Stats.Flush();

        // 100 base × 1.20 = 120
        unit.Stats.GetTotal(StatId.Strength).ShouldBe(120);

        container.RemoveByEntry(def.Entry);
        unit.Stats.Flush();
        unit.Stats.GetTotal(StatId.Strength).ShouldBe(100);
    }

    [Fact]
    public void PercentageStat_negative_adds_reduction_multiplier()
    {
        var unit = MakeUnit();
        unit.Stats.SetBase(StatId.Strength, 200);
        unit.Stats.Flush();
        var container = MakeContainer(unit);

        // -25% at all levels
        var effectDef = MakeEffectDef(BuffEffectType.PercentageStatModifier,
            statId: StatId.Strength, primary: -25, secondary: -25);
        var def = MakeDef(effects: [effectDef]);

        ApplyBuff(container, def, unit);
        unit.Stats.Flush();

        // 200 base × 0.75 = 150
        unit.Stats.GetTotal(StatId.Strength).ShouldBe(150);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  DamageOverTimeEffect
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void DoT_deals_damage_per_tick()
    {
        var caster = MakeUnit(1);
        var target = MakeUnit(2);
        var container = MakeContainer(target);

        // 300 total damage (RawDamage), 3 ticks over 3500ms at 1000ms interval
        // Duration longer than 3×interval ensures all 3 ticks fire before expiry.
        var effectDef = MakeEffectDef(BuffEffectType.DamageOverTime,
            invokeOn: BuffPhase.Tick,
            primary: 300, secondary: 300,
            tertiary: (int)DamageType.RawDamage);
        var def = MakeDef(durationMs: 3500, intervalMs: 1000, effects: [effectDef]);

        ApplyBuff(container, def, caster);

        // intervals = 3500/1000 = 3 → per tick = 300/3 = 100
        container.Update(1000);
        target.Health.Current.ShouldBe(900u);

        container.Update(2000);
        target.Health.Current.ShouldBe(800u);

        container.Update(3000);
        target.Health.Current.ShouldBe(700u);
    }

    [Fact]
    public void DoT_stops_on_target_death()
    {
        var caster = MakeUnit(1);
        var target = MakeUnit(2, maxHealth: 150);
        var container = MakeContainer(target);

        // 300 raw damage total, 3 ticks of 100 each
        var effectDef = MakeEffectDef(BuffEffectType.DamageOverTime,
            invokeOn: BuffPhase.Tick,
            primary: 300, secondary: 300,
            tertiary: (int)DamageType.RawDamage);
        var def = MakeDef(durationMs: 3500, intervalMs: 1000, effects: [effectDef]);

        ApplyBuff(container, def, caster);

        container.Update(1000); // 150 - 100 = 50
        target.Health.Current.ShouldBe(50u);

        container.Update(2000); // 50 - 100 → 0 (clamped)
        target.Health.IsDead.ShouldBeTrue();
    }

    // ═══════════════════════════════════════════════════════════════════
    //  HealOverTimeEffect
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void HoT_heals_per_tick()
    {
        var caster = MakeUnit(1);
        var target = MakeUnit(2);
        target.Health.TakeDamage(800); // 200/1000
        var container = MakeContainer(target);

        // 400 total heal, 4 ticks at 1000ms, = 100 per tick
        var effectDef = MakeEffectDef(BuffEffectType.HealOverTime,
            invokeOn: BuffPhase.Tick,
            primary: 400, secondary: 400);
        var def = MakeDef(durationMs: 4000, intervalMs: 1000, effects: [effectDef]);

        ApplyBuff(container, def, caster);

        container.Update(1000);
        target.Health.Current.ShouldBe(300u); // 200 + 100

        container.Update(2000);
        target.Health.Current.ShouldBe(400u);

        container.Update(3000);
        target.Health.Current.ShouldBe(500u);
    }

    [Fact]
    public void HoT_does_not_heal_dead_target()
    {
        var target = MakeUnit(1, maxHealth: 100);
        target.Health.TakeDamage(100); // dead
        var container = MakeContainer(target);

        var effectDef = MakeEffectDef(BuffEffectType.HealOverTime,
            invokeOn: BuffPhase.Tick, primary: 50, secondary: 50);
        var def = MakeDef(durationMs: 2000, intervalMs: 1000, effects: [effectDef]);

        ApplyBuff(container, def, target);
        container.Update(1000);

        target.Health.IsDead.ShouldBeTrue();
        target.Health.Current.ShouldBe(0u);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  CrowdControlEffect
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void CC_effect_flags_active_while_buff_is_up()
    {
        var unit = MakeUnit();
        var container = MakeContainer(unit);

        var effectDef = MakeEffectDef(BuffEffectType.CrowdControl);
        var def = MakeDef(cc: CrowdControlFlags.Silence, effects: [effectDef]);

        container.GetActiveCrowdControl().ShouldBe(CrowdControlFlags.None);

        ApplyBuff(container, def, unit);
        container.GetActiveCrowdControl().HasFlag(CrowdControlFlags.Silence).ShouldBeTrue();

        container.RemoveByEntry(def.Entry);
        container.GetActiveCrowdControl().ShouldBe(CrowdControlFlags.None);
    }

    [Fact]
    public void CC_multiple_types_combine()
    {
        var unit = MakeUnit();
        var container = MakeContainer(unit);

        var silenceDef = MakeDef(entry: 100,
            cc: CrowdControlFlags.Silence,
            effects: [MakeEffectDef(BuffEffectType.CrowdControl)]);
        var rootDef = MakeDef(entry: 101,
            cc: CrowdControlFlags.Root,
            effects: [MakeEffectDef(BuffEffectType.CrowdControl)]);

        ApplyBuff(container, silenceDef, unit);
        ApplyBuff(container, rootDef, unit);

        var cc = container.GetActiveCrowdControl();
        cc.HasFlag(CrowdControlFlags.Silence).ShouldBeTrue();
        cc.HasFlag(CrowdControlFlags.Root).ShouldBeTrue();
    }

    // ═══════════════════════════════════════════════════════════════════
    //  SpeedModifierEffect
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void SpeedModifier_applies_and_removes_velocity()
    {
        var unit = MakeUnit();
        unit.Stats.SetBase(StatId.Velocity, 100);
        unit.Stats.Flush();
        var container = MakeContainer(unit);

        // -40 = 40% slow
        var effectDef = MakeEffectDef(BuffEffectType.SpeedModifier, primary: -40);
        var def = MakeDef(effects: [effectDef]);

        ApplyBuff(container, def, unit);

        unit.Stats.GetTotal(StatId.Velocity).ShouldBe(60); // 100 - 40

        container.RemoveByEntry(def.Entry);
        unit.Stats.GetTotal(StatId.Velocity).ShouldBe(100);
    }

    [Fact]
    public void SpeedModifier_positive_adds_haste()
    {
        var unit = MakeUnit();
        unit.Stats.SetBase(StatId.Velocity, 100);
        unit.Stats.Flush();
        var container = MakeContainer(unit);

        var effectDef = MakeEffectDef(BuffEffectType.SpeedModifier, primary: 25);
        var def = MakeDef(effects: [effectDef]);

        ApplyBuff(container, def, unit);
        unit.Stats.GetTotal(StatId.Velocity).ShouldBe(125);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  AbsorbShieldEffect
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void AbsorbShield_absorbs_damage_from_combat_event()
    {
        var unit = MakeUnit();
        var container = MakeContainer(unit);

        // Shield with 200 HP value (level 40: lerp(200,200,1.0) = 200)
        var effectDef = MakeEffectDef(BuffEffectType.AbsorbShield,
            invokeOn: BuffPhase.Start,
            primary: 200, secondary: 200,
            eventSub: CombatEventType.ReceivingDamage,
            priority: CombatEventPriority.AbsorbShield);
        var def = MakeDef(effects: [effectDef]);

        ApplyBuff(container, def, unit);

        // Simulate receiving 150 damage
        var ctx = new DamageContext { Damage = 150f, DamageType = DamageType.Physical };
        container.NotifyCombatEvent(CombatEventType.ReceivingDamage, ctx, null);

        ctx.Damage.ShouldBe(0f);           // fully absorbed
        ctx.Absorption.ShouldBe(150f);     // tracked

        // Shield still has 50 HP remaining — buff should still be active
        container.HasBuff(def.Entry).ShouldBeTrue();
    }

    [Fact]
    public void AbsorbShield_depletes_and_expires_buff()
    {
        var unit = MakeUnit();
        var container = MakeContainer(unit);

        var effectDef = MakeEffectDef(BuffEffectType.AbsorbShield,
            invokeOn: BuffPhase.Start,
            primary: 100, secondary: 100,
            eventSub: CombatEventType.ReceivingDamage,
            priority: CombatEventPriority.AbsorbShield);
        var def = MakeDef(effects: [effectDef]);

        ApplyBuff(container, def, unit);

        // Damage exceeds shield
        var ctx = new DamageContext { Damage = 250f, DamageType = DamageType.Physical };
        container.NotifyCombatEvent(CombatEventType.ReceivingDamage, ctx, null);

        ctx.Damage.ShouldBe(150f);         // 250 - 100 shield
        ctx.Absorption.ShouldBe(100f);

        // Buff should be flagged expired, removed on next Update
        container.Update(100);
        container.HasBuff(def.Entry).ShouldBeFalse();
    }

    [Fact]
    public void AbsorbShield_ignores_raw_damage()
    {
        var unit = MakeUnit();
        var container = MakeContainer(unit);

        var effectDef = MakeEffectDef(BuffEffectType.AbsorbShield,
            invokeOn: BuffPhase.Start,
            primary: 200, secondary: 200,
            eventSub: CombatEventType.ReceivingDamage,
            priority: CombatEventPriority.AbsorbShield);
        var def = MakeDef(effects: [effectDef]);

        ApplyBuff(container, def, unit);

        // RawDamage bypasses shields
        var ctx = new DamageContext { Damage = 100f, DamageType = DamageType.RawDamage };
        container.NotifyCombatEvent(CombatEventType.ReceivingDamage, ctx, null);

        ctx.Damage.ShouldBe(100f);     // unchanged
        ctx.Absorption.ShouldBe(0f);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  DamageSplitEffect (Guard)
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void DamageSplit_redirects_damage_to_caster()
    {
        var tank = MakeUnit(1);
        var target = MakeUnit(2);
        var container = MakeContainer(target);

        // 50% target keeps, 50% goes to tank
        var effectDef = MakeEffectDef(BuffEffectType.DamageSplit,
            invokeOn: BuffPhase.Start,
            primary: 50, secondary: 50,
            eventSub: CombatEventType.ReceivingDamage,
            priority: CombatEventPriority.Guard);
        var def = MakeDef(effects: [effectDef]);

        container.QueueBuff(def, tank);
        container.Update(0);

        var ctx = new DamageContext { Damage = 200f };
        container.NotifyCombatEvent(CombatEventType.ReceivingDamage, ctx, null);

        ctx.Damage.ShouldBe(100f);             // target takes 50%
        ctx.GuardSplitAmount.ShouldBe(100f);   // tank absorbs 50%
    }

    [Fact]
    public void DamageSplit_inactive_when_caster_dead()
    {
        var tank = MakeUnit(1, maxHealth: 100);
        tank.Health.TakeDamage(100); // dead
        var target = MakeUnit(2);
        var container = MakeContainer(target);

        var effectDef = MakeEffectDef(BuffEffectType.DamageSplit,
            invokeOn: BuffPhase.Start,
            primary: 50, secondary: 50,
            eventSub: CombatEventType.ReceivingDamage,
            priority: CombatEventPriority.Guard);
        var def = MakeDef(effects: [effectDef]);

        container.QueueBuff(def, tank);
        container.Update(0);

        var ctx = new DamageContext { Damage = 200f };
        container.NotifyCombatEvent(CombatEventType.ReceivingDamage, ctx, null);

        ctx.Damage.ShouldBe(200f);             // no split — tank dead
        ctx.GuardSplitAmount.ShouldBe(0f);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  ProcDamageEffect
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ProcDamage_deals_damage_to_instigator_on_event()
    {
        var defender = MakeUnit(1);
        var attacker = MakeUnit(2);
        var container = MakeContainer(defender);

        // Proc: 100 raw damage when attacked
        var effectDef = MakeEffectDef(BuffEffectType.ProcDamage,
            invokeOn: BuffPhase.None,
            primary: 100, secondary: 100,
            tertiary: (int)DamageType.RawDamage,
            eventSub: CombatEventType.ReceivedDamage,
            priority: CombatEventPriority.FinalReaction);
        var def = MakeDef(effects: [effectDef]);

        container.QueueBuff(def, defender);
        container.Update(0);

        // Fire event — attacker is the instigator
        container.NotifyCombatEvent(CombatEventType.ReceivedDamage, null, attacker);

        attacker.Health.Current.ShouldBe(900u); // 1000 - 100
    }

    [Fact]
    public void ProcDamage_no_damage_if_instigator_null()
    {
        var unit = MakeUnit();
        var container = MakeContainer(unit);

        var effectDef = MakeEffectDef(BuffEffectType.ProcDamage,
            primary: 100, secondary: 100,
            tertiary: (int)DamageType.RawDamage,
            eventSub: CombatEventType.ReceivedDamage,
            priority: CombatEventPriority.FinalReaction);
        var def = MakeDef(effects: [effectDef]);

        container.QueueBuff(def, unit);
        container.Update(0);

        // No instigator → no proc
        container.NotifyCombatEvent(CombatEventType.ReceivedDamage, null, null);
        // No crash, no damage to anyone
    }

    // ═══════════════════════════════════════════════════════════════════
    //  ProcHealEffect
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ProcHeal_heals_buff_target_on_event()
    {
        var unit = MakeUnit();
        unit.Health.TakeDamage(500); // at 500/1000
        var container = MakeContainer(unit);

        var effectDef = MakeEffectDef(BuffEffectType.ProcHeal,
            primary: 200, secondary: 200,
            eventSub: CombatEventType.DealtDamage,
            priority: CombatEventPriority.FinalReaction);
        var def = MakeDef(effects: [effectDef]);

        container.QueueBuff(def, unit);
        container.Update(0);

        container.NotifyCombatEvent(CombatEventType.DealtDamage, null, null);

        unit.Health.Current.ShouldBe(700u); // 500 + 200
    }

    // ═══════════════════════════════════════════════════════════════════
    //  ProcBuffEffect
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ProcBuff_queues_new_buff_on_self()
    {
        var unit = MakeUnit();
        var container = MakeContainer(unit);

        // The proc will apply buff entry 200
        var innerDef = MakeDef(entry: 200, durationMs: 5000);

        // Set up the factory's lookup
        BuffEffectFactory.BuffLookup = entry => entry == 200 ? innerDef : null;

        var effectDef = MakeEffectDef(BuffEffectType.ProcBuff,
            primary: 200, secondary: 0, // 0 = apply to self
            eventSub: CombatEventType.DealtDamage,
            priority: CombatEventPriority.FinalReaction);
        var def = MakeDef(entry: 100, effects: [effectDef]);

        container.QueueBuff(def, unit);
        container.Update(0);

        container.NotifyCombatEvent(CombatEventType.DealtDamage, null, null);

        // Inner buff is queued — drain on next update
        container.Update(100);
        container.HasBuff(200).ShouldBeTrue();

        // Cleanup
        BuffEffectFactory.BuffLookup = null;
    }

    [Fact]
    public void ProcBuff_queues_buff_on_instigator()
    {
        var unit = MakeUnit(1);
        var attacker = MakeUnit(2);
        var attackerContainer = MakeContainer(attacker);
        var container = MakeContainer(unit);

        var innerDef = MakeDef(entry: 300, durationMs: 5000);
        BuffEffectFactory.BuffLookup = entry => entry == 300 ? innerDef : null;

        var effectDef = MakeEffectDef(BuffEffectType.ProcBuff,
            primary: 300, secondary: 1, // 1 = apply to instigator
            eventSub: CombatEventType.ReceivedDamage,
            priority: CombatEventPriority.FinalReaction);
        var def = MakeDef(entry: 100, effects: [effectDef]);

        container.QueueBuff(def, unit);
        container.Update(0);

        container.NotifyCombatEvent(CombatEventType.ReceivedDamage, null, attacker);

        // Inner buff queued on attacker — drain
        attackerContainer.Update(100);
        attacker.Buffs.HasBuff(300).ShouldBeTrue();

        BuffEffectFactory.BuffLookup = null;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  BuffEffectFactory
    // ═══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(BuffEffectType.StatModifier, typeof(StatModifierEffect))]
    [InlineData(BuffEffectType.PercentageStatModifier, typeof(PercentageStatModifierEffect))]
    [InlineData(BuffEffectType.DamageOverTime, typeof(DamageOverTimeEffect))]
    [InlineData(BuffEffectType.HealOverTime, typeof(HealOverTimeEffect))]
    [InlineData(BuffEffectType.CrowdControl, typeof(CrowdControlEffect))]
    [InlineData(BuffEffectType.SpeedModifier, typeof(SpeedModifierEffect))]
    [InlineData(BuffEffectType.AbsorbShield, typeof(AbsorbShieldEffect))]
    [InlineData(BuffEffectType.DamageSplit, typeof(DamageSplitEffect))]
    [InlineData(BuffEffectType.ProcDamage, typeof(ProcDamageEffect))]
    [InlineData(BuffEffectType.ProcHeal, typeof(ProcHealEffect))]
    [InlineData(BuffEffectType.ProcBuff, typeof(ProcBuffEffect))]
    public void Factory_creates_correct_effect_type(BuffEffectType effectType, Type expectedType)
    {
        var def = new BuffEffectDefinition { EffectType = effectType };
        var effect = BuffEffectFactory.Create(def);
        effect.ShouldBeOfType(expectedType);
        effect.Definition.ShouldBeSameAs(def);
    }

    [Fact]
    public void Factory_returns_NullEffect_for_unknown_type()
    {
        var def = new BuffEffectDefinition { EffectType = BuffEffectType.AuraPropagation };
        var effect = BuffEffectFactory.Create(def);
        effect.ShouldBeOfType<BuffEffectFactory.NullEffect>();
    }

    // ═══════════════════════════════════════════════════════════════════
    //  End-to-end Integration
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Multiple_effects_on_one_buff()
    {
        var unit = MakeUnit();
        unit.Stats.SetBase(StatId.Strength, 100);
        unit.Stats.SetBase(StatId.Velocity, 100);
        unit.Stats.Flush();
        var container = MakeContainer(unit);

        var statEffect = MakeEffectDef(BuffEffectType.StatModifier,
            statId: StatId.Strength, primary: 50, secondary: 50);
        var speedEffect = MakeEffectDef(BuffEffectType.SpeedModifier,
            primary: -20);
        var def = MakeDef(effects: [statEffect, speedEffect]);

        ApplyBuff(container, def, unit);

        unit.Stats.GetTotal(StatId.Strength).ShouldBe(150);  // +50
        unit.Stats.GetTotal(StatId.Velocity).ShouldBe(80);   // -20

        container.RemoveByEntry(def.Entry);
        unit.Stats.GetTotal(StatId.Strength).ShouldBe(100);
        unit.Stats.GetTotal(StatId.Velocity).ShouldBe(100);
    }

    [Fact]
    public void AbsorbShield_then_guard_priority_ordering()
    {
        var tank = MakeUnit(1);
        var target = MakeUnit(2);
        var container = MakeContainer(target);

        // Shield absorbs first (priority 1), guard splits remainder (priority 2)
        var shieldEffect = MakeEffectDef(BuffEffectType.AbsorbShield,
            invokeOn: BuffPhase.Start,
            primary: 100, secondary: 100,
            eventSub: CombatEventType.ReceivingDamage,
            priority: CombatEventPriority.AbsorbShield);
        var guardEffect = MakeEffectDef(BuffEffectType.DamageSplit,
            invokeOn: BuffPhase.Start,
            primary: 50, secondary: 50,
            eventSub: CombatEventType.ReceivingDamage,
            priority: CombatEventPriority.Guard);

        var shieldDef = MakeDef(entry: 100, effects: [shieldEffect]);
        var guardDef = MakeDef(entry: 101, effects: [guardEffect]);

        container.QueueBuff(shieldDef, target);
        container.QueueBuff(guardDef, tank);
        container.Update(0);

        // 300 incoming damage → shield absorbs 100 → guard splits remaining 200
        var ctx = new DamageContext { Damage = 300f, DamageType = DamageType.Physical };
        container.NotifyCombatEvent(CombatEventType.ReceivingDamage, ctx, null);

        ctx.Absorption.ShouldBe(100f);          // shield absorbed 100
        ctx.Damage.ShouldBe(100f);              // target takes 200 × 0.5 = 100
        ctx.GuardSplitAmount.ShouldBe(100f);    // tank takes 200 × 0.5 = 100
    }

    [Fact]
    public void DoT_and_stat_modifier_on_same_buff()
    {
        var caster = MakeUnit(1);
        var target = MakeUnit(2);
        target.Stats.SetBase(StatId.Strength, 100);
        target.Stats.Flush();
        var container = MakeContainer(target);

        var statEffect = MakeEffectDef(BuffEffectType.StatModifier,
            statId: StatId.Strength, primary: -30, secondary: -30);
        var dotEffect = MakeEffectDef(BuffEffectType.DamageOverTime,
            invokeOn: BuffPhase.Tick,
            primary: 200, secondary: 200,
            tertiary: (int)DamageType.RawDamage);
        var def = MakeDef(durationMs: 2500, intervalMs: 1000, effects: [statEffect, dotEffect]);

        ApplyBuff(container, def, caster);

        // Stat mod applied immediately
        target.Stats.GetTotal(StatId.Strength).ShouldBe(70);

        // intervals = 2500/1000 = 2, per tick = 200/2 = 100
        container.Update(1000);
        target.Health.Current.ShouldBe(900u); // 1000 - 100

        // Remove restores stat
        container.RemoveByEntry(def.Entry);
        target.Stats.GetTotal(StatId.Strength).ShouldBe(100);
    }

    [Fact]
    public void Buff_expiry_cleans_up_stat_modifier()
    {
        var unit = MakeUnit();
        unit.Stats.SetBase(StatId.Toughness, 100);
        unit.Stats.Flush();
        var container = MakeContainer(unit);

        var effectDef = MakeEffectDef(BuffEffectType.StatModifier,
            statId: StatId.Toughness, primary: 50, secondary: 50);
        var def = MakeDef(durationMs: 2000, effects: [effectDef]);

        ApplyBuff(container, def, unit);
        unit.Stats.GetTotal(StatId.Toughness).ShouldBe(150);

        // Expire at tick 2000
        container.Update(2000);
        unit.Stats.GetTotal(StatId.Toughness).ShouldBe(100);
        container.HasBuff(def.Entry).ShouldBeFalse();
    }
}
