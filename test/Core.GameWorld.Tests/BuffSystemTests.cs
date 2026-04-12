using Core.Domain.Entities;
using Core.GameWorld.Combat;
using Core.GameWorld.Combat.Buffs;
using Core.GameWorld.Entities;
using Core.GameWorld.Stats;
using Shouldly;

namespace Core.GameWorld.Tests;

/// <summary>
/// Tests for the buff system: <see cref="Buff"/>, <see cref="BuffContainer"/>,
/// <see cref="BuffDefinition"/>, stacking policies, combat event dispatch,
/// and tick/expiry lifecycle.
/// </summary>
public class BuffSystemTests
{
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    //  Helpers
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    private static UnitEntity MakeUnit(ushort id = 1)
        => new PlayerEntity(id, new Character { CharacterId = id, Name = $"Unit{id}" }, 1000);

    private static BuffDefinition MakeDef(
        ushort entry = 100,
        uint durationMs = 10_000,
        ushort intervalMs = 0,
        byte maxStacks = 1,
        StackingPolicy policy = StackingPolicy.Unique,
        BuffGroup group = BuffGroup.None,
        CrowdControlFlags cc = CrowdControlFlags.None,
        bool persistsOnDeath = false,
        List<BuffEffectDefinition>? effects = null)
    {
        return new BuffDefinition
        {
            Entry = entry,
            Name = $"Buff{entry}",
            BuffClass = BuffClass.Buff0,
            BuffType = BuffType.None,
            Group = group,
            StackingPolicy = policy,
            DurationMs = durationMs,
            IntervalMs = intervalMs,
            MaxStacks = maxStacks,
            InitialStacks = 1,
            MaxCopies = 3,
            CanRefresh = true,
            PersistsOnDeath = persistsOnDeath,
            CrowdControl = cc,
            Effects = effects ?? [],
        };
    }

    /// <summary>Stub effect that records lifecycle calls for assertions.</summary>
    private sealed class SpyEffect : IBuffEffect
    {
        public BuffEffectDefinition Definition { get; }
        public int StartCount { get; private set; }
        public int TickCount { get; private set; }
        public int EndCount { get; private set; }
        public int EventCount { get; private set; }
        public CombatEventType LastEventType { get; private set; }

        public SpyEffect(BuffEffectDefinition definition)
        {
            Definition = definition;
        }

        public void OnStart(Buff buff, UnitEntity target) => StartCount++;
        public void OnTick(Buff buff, UnitEntity target, long tick) => TickCount++;
        public void OnEnd(Buff buff, UnitEntity target) => EndCount++;

        public void OnCombatEvent(Buff buff, CombatEventType eventType, DamageContext? context, UnitEntity? instigator)
        {
            EventCount++;
            LastEventType = eventType;
        }
    }

    private static BuffEffectDefinition MakeEffectDef(
        BuffEffectType type = BuffEffectType.StatModifier,
        BuffPhase invokeOn = BuffPhase.Start,
        CombatEventType eventSub = CombatEventType.None,
        CombatEventPriority priority = CombatEventPriority.DamageModification)
    {
        return new BuffEffectDefinition
        {
            EffectType = type,
            InvokeOn = invokeOn,
            EventSubscription = eventSub,
            EventPriority = priority,
        };
    }

    /// <summary>
    /// Sets up a container with a SpyEffect factory so effects are created
    /// and lifecycle calls can be observed.
    /// </summary>
    private static (BuffContainer container, List<SpyEffect> spies) MakeContainerWithSpies(UnitEntity owner)
    {
        var spies = new List<SpyEffect>();
        var container = owner.Buffs;
        container.EffectFactory = def =>
        {
            var spy = new SpyEffect(def);
            spies.Add(spy);
            return spy;
        };
        return (container, spies);
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    //  Buff â€” Construction & basic properties
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    [Fact]
    public void Buff_constructor_sets_initial_state()
    {
        var target = MakeUnit();
        var caster = MakeUnit(2);
        var def = MakeDef(durationMs: 5000, intervalMs: 1000);

        var buff = new Buff(def, caster, target, currentTick: 100);

        buff.Definition.ShouldBe(def);
        buff.Caster.ShouldBe(caster);
        buff.Target.ShouldBe(target);
        buff.StackLevel.ShouldBe((byte)1);
        buff.EndTime.ShouldBe(5100L);
        buff.NextTickTime.ShouldBe(1100L);
        buff.IsExpired.ShouldBeFalse();
    }

    [Fact]
    public void Buff_permanent_has_zero_end_time()
    {
        var target = MakeUnit();
        var def = MakeDef(durationMs: 0);

        var buff = new Buff(def, null, target, currentTick: 500);

        buff.EndTime.ShouldBe(0L);
    }

    [Fact]
    public void Buff_no_interval_has_zero_next_tick_time()
    {
        var target = MakeUnit();
        var def = MakeDef(intervalMs: 0);

        var buff = new Buff(def, null, target, currentTick: 500);

        buff.NextTickTime.ShouldBe(0L);
    }

    [Fact]
    public void Buff_HasExpired_returns_true_when_time_passed()
    {
        var target = MakeUnit();
        var def = MakeDef(durationMs: 1000);
        var buff = new Buff(def, null, target, currentTick: 0);

        buff.HasExpired(999).ShouldBeFalse();
        buff.HasExpired(1000).ShouldBeTrue();
        buff.HasExpired(2000).ShouldBeTrue();
    }

    [Fact]
    public void Buff_permanent_never_expires_by_time()
    {
        var target = MakeUnit();
        var def = MakeDef(durationMs: 0);
        var buff = new Buff(def, null, target, currentTick: 0);

        buff.HasExpired(long.MaxValue).ShouldBeFalse();
    }

    [Fact]
    public void Buff_IsDueForTick_returns_true_at_correct_time()
    {
        var target = MakeUnit();
        var def = MakeDef(intervalMs: 500);
        var buff = new Buff(def, null, target, currentTick: 0);

        buff.IsDueForTick(499).ShouldBeFalse();
        buff.IsDueForTick(500).ShouldBeTrue();
    }

    [Fact]
    public void Buff_Refresh_resets_duration_and_adds_stack()
    {
        var target = MakeUnit();
        var def = MakeDef(durationMs: 5000, maxStacks: 3);
        var buff = new Buff(def, null, target, currentTick: 0);

        buff.StackLevel.ShouldBe((byte)1);

        buff.Refresh(3000);

        buff.EndTime.ShouldBe(8000L); // 3000 + 5000
        buff.StackLevel.ShouldBe((byte)2);

        buff.Refresh(6000);

        buff.EndTime.ShouldBe(11000L);
        buff.StackLevel.ShouldBe((byte)3);
    }

    [Fact]
    public void Buff_Refresh_does_not_exceed_max_stacks()
    {
        var target = MakeUnit();
        var def = MakeDef(durationMs: 5000, maxStacks: 2);
        var buff = new Buff(def, null, target, currentTick: 0);

        buff.Refresh(1000);
        buff.Refresh(2000);

        buff.StackLevel.ShouldBe((byte)2);
    }

    [Fact]
    public void Buff_ConsumeStack_decrements_and_flags_expired_at_zero()
    {
        var target = MakeUnit();
        var def = MakeDef(maxStacks: 2);
        var buff = new Buff(def, null, target, currentTick: 0);
        buff.Refresh(0); // stack = 2

        buff.ConsumeStack();
        buff.StackLevel.ShouldBe((byte)1);
        buff.IsExpired.ShouldBeFalse();

        buff.ConsumeStack();
        buff.StackLevel.ShouldBe((byte)0);
        buff.IsExpired.ShouldBeTrue();
    }

    [Fact]
    public void Buff_FlagExpired_sets_IsExpired()
    {
        var target = MakeUnit();
        var def = MakeDef();
        var buff = new Buff(def, null, target, currentTick: 0);

        buff.FlagExpired();

        buff.IsExpired.ShouldBeTrue();
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    //  Buff â€” Lifecycle callbacks
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    [Fact]
    public void Buff_Start_invokes_OnStart_on_all_effects()
    {
        var target = MakeUnit();
        var def = MakeDef(effects: [MakeEffectDef(), MakeEffectDef()]);
        var buff = new Buff(def, null, target, currentTick: 0);
        var spy1 = new SpyEffect(def.Effects[0]);
        var spy2 = new SpyEffect(def.Effects[1]);
        buff.Effects = [spy1, spy2];

        buff.Start();

        spy1.StartCount.ShouldBe(1);
        spy2.StartCount.ShouldBe(1);
    }

    [Fact]
    public void Buff_Tick_only_invokes_effects_with_Tick_phase()
    {
        var target = MakeUnit();
        var startOnly = MakeEffectDef(invokeOn: BuffPhase.Start);
        var tickable = MakeEffectDef(invokeOn: BuffPhase.Tick);
        var def = MakeDef(intervalMs: 1000, effects: [startOnly, tickable]);
        var buff = new Buff(def, null, target, currentTick: 0);
        var spyStart = new SpyEffect(startOnly);
        var spyTick = new SpyEffect(tickable);
        buff.Effects = [spyStart, spyTick];

        buff.Tick(1000);

        spyStart.TickCount.ShouldBe(0);
        spyTick.TickCount.ShouldBe(1);
    }

    [Fact]
    public void Buff_Tick_advances_NextTickTime()
    {
        var target = MakeUnit();
        var def = MakeDef(intervalMs: 500);
        var buff = new Buff(def, null, target, currentTick: 0);
        buff.Effects = [];

        buff.Tick(500);

        buff.NextTickTime.ShouldBe(1000L); // 500 + 500
    }

    [Fact]
    public void Buff_End_invokes_OnEnd_on_all_effects()
    {
        var target = MakeUnit();
        var def = MakeDef(effects: [MakeEffectDef()]);
        var buff = new Buff(def, null, target, currentTick: 0);
        var spy = new SpyEffect(def.Effects[0]);
        buff.Effects = [spy];

        buff.End();

        spy.EndCount.ShouldBe(1);
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    //  BuffContainer â€” Basic application
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    [Fact]
    public void Container_QueueBuff_then_Update_applies_buff()
    {
        var owner = MakeUnit();
        var (container, spies) = MakeContainerWithSpies(owner);
        var def = MakeDef();

        container.QueueBuff(def, null);
        container.Update(0);

        container.ActiveBuffs.Count.ShouldBe(1);
        container.HasBuff(100).ShouldBeTrue();
        spies.Count.ShouldBe(0); // No effects defined
    }

    [Fact]
    public void Container_applies_buff_with_effect_and_invokes_OnStart()
    {
        var owner = MakeUnit();
        var (container, spies) = MakeContainerWithSpies(owner);
        var def = MakeDef(effects: [MakeEffectDef()]);

        container.QueueBuff(def, null);
        container.Update(0);

        container.ActiveBuffs.Count.ShouldBe(1);
        spies.Count.ShouldBe(1);
        spies[0].StartCount.ShouldBe(1);
    }

    [Fact]
    public void Container_assigns_unique_slot_ids()
    {
        var owner = MakeUnit();
        var (container, _) = MakeContainerWithSpies(owner);

        container.QueueBuff(MakeDef(entry: 1, policy: StackingPolicy.Unlimited), null);
        container.QueueBuff(MakeDef(entry: 2, policy: StackingPolicy.Unlimited), null);
        container.QueueBuff(MakeDef(entry: 3, policy: StackingPolicy.Unlimited), null);
        container.Update(0);

        var slots = container.ActiveBuffs.Select(b => b.SlotId).ToList();
        slots.Distinct().Count().ShouldBe(3);
    }

    [Fact]
    public void Container_GetBuff_returns_buff_or_null()
    {
        var owner = MakeUnit();
        var (container, _) = MakeContainerWithSpies(owner);
        var def = MakeDef(entry: 42);

        container.QueueBuff(def, null);
        container.Update(0);

        container.GetBuff(42).ShouldNotBeNull();
        container.GetBuff(99).ShouldBeNull();
    }

    [Fact]
    public void Container_GetBuffBySlot_returns_buff_or_null()
    {
        var owner = MakeUnit();
        var (container, _) = MakeContainerWithSpies(owner);
        var def = MakeDef(entry: 42);

        container.QueueBuff(def, null);
        container.Update(0);

        var slot = container.ActiveBuffs[0].SlotId;
        container.GetBuffBySlot(slot).ShouldNotBeNull();
        container.GetBuffBySlot(199).ShouldBeNull();
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    //  BuffContainer â€” Expiry & removal
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    [Fact]
    public void Container_expires_buff_by_time()
    {
        var owner = MakeUnit();
        var (container, spies) = MakeContainerWithSpies(owner);
        var def = MakeDef(durationMs: 5000, effects: [MakeEffectDef()]);

        container.QueueBuff(def, null);
        container.Update(0);

        container.ActiveBuffs.Count.ShouldBe(1);
        spies[0].EndCount.ShouldBe(0);

        container.Update(5000);

        container.ActiveBuffs.Count.ShouldBe(0);
        spies[0].EndCount.ShouldBe(1);
    }

    [Fact]
    public void Container_permanent_buff_does_not_expire()
    {
        var owner = MakeUnit();
        var (container, _) = MakeContainerWithSpies(owner);
        var def = MakeDef(durationMs: 0);

        container.QueueBuff(def, null);
        container.Update(0);

        container.Update(999_999_999);

        container.ActiveBuffs.Count.ShouldBe(1);
    }

    [Fact]
    public void Container_RemoveBuff_by_slot_invokes_OnEnd()
    {
        var owner = MakeUnit();
        var (container, spies) = MakeContainerWithSpies(owner);
        var def = MakeDef(effects: [MakeEffectDef()]);

        container.QueueBuff(def, null);
        container.Update(0);

        var slot = container.ActiveBuffs[0].SlotId;
        container.RemoveBuff(slot).ShouldBeTrue();

        container.ActiveBuffs.Count.ShouldBe(0);
        spies[0].EndCount.ShouldBe(1);
    }

    [Fact]
    public void Container_RemoveByEntry_removes_first_match()
    {
        var owner = MakeUnit();
        var (container, _) = MakeContainerWithSpies(owner);
        var def = MakeDef(entry: 55);

        container.QueueBuff(def, null);
        container.Update(0);

        container.RemoveByEntry(55).ShouldBeTrue();
        container.ActiveBuffs.Count.ShouldBe(0);
    }

    [Fact]
    public void Container_RemoveByEntry_returns_false_for_missing()
    {
        var owner = MakeUnit();
        var (container, _) = MakeContainerWithSpies(owner);

        container.RemoveByEntry(999).ShouldBeFalse();
    }

    [Fact]
    public void Container_RemoveByGroup_removes_all_in_group()
    {
        var owner = MakeUnit();
        var (container, _) = MakeContainerWithSpies(owner);

        container.QueueBuff(MakeDef(entry: 1, group: BuffGroup.Guard, policy: StackingPolicy.Unlimited), null);
        container.QueueBuff(MakeDef(entry: 2, group: BuffGroup.Guard, policy: StackingPolicy.Unlimited), null);
        container.QueueBuff(MakeDef(entry: 3, group: BuffGroup.Detaunt), null);
        container.Update(0);

        container.RemoveByGroup(BuffGroup.Guard).ShouldBe(2);
        container.ActiveBuffs.Count.ShouldBe(1);
        container.ActiveBuffs[0].Definition.Entry.ShouldBe((ushort)3);
    }

    [Fact]
    public void Container_RemoveByType_removes_matching_buff_type()
    {
        var owner = MakeUnit();
        var (container, _) = MakeContainerWithSpies(owner);
        var hex = MakeDef(entry: 1);
        hex = new BuffDefinition
        {
            Entry = 1, Name = "Hex", BuffClass = BuffClass.Buff0, BuffType = BuffType.Hex,
            DurationMs = 10_000, StackingPolicy = StackingPolicy.Unlimited,
        };
        var curse = new BuffDefinition
        {
            Entry = 2, Name = "Curse", BuffClass = BuffClass.Buff0, BuffType = BuffType.Curse,
            DurationMs = 10_000, StackingPolicy = StackingPolicy.Unlimited,
        };

        container.QueueBuff(hex, null);
        container.QueueBuff(curse, null);
        container.Update(0);

        container.RemoveByType(BuffType.Hex).ShouldBe(1);
        container.ActiveBuffs.Count.ShouldBe(1);
        container.ActiveBuffs[0].Definition.BuffType.ShouldBe(BuffType.Curse);
    }

    [Fact]
    public void Container_CleanseCC_removes_buffs_matching_flags()
    {
        var owner = MakeUnit();
        var (container, _) = MakeContainerWithSpies(owner);

        container.QueueBuff(MakeDef(entry: 1, cc: CrowdControlFlags.Root, policy: StackingPolicy.Unlimited), null);
        container.QueueBuff(MakeDef(entry: 2, cc: CrowdControlFlags.Snare, policy: StackingPolicy.Unlimited), null);
        container.QueueBuff(MakeDef(entry: 3, cc: CrowdControlFlags.Silence, policy: StackingPolicy.Unlimited), null);
        container.Update(0);

        container.CleanseCC(CrowdControlFlags.MoveImpedance).ShouldBe(2); // Root + Snare
        container.ActiveBuffs.Count.ShouldBe(1);
        container.ActiveBuffs[0].Definition.Entry.ShouldBe((ushort)3);
    }

    [Fact]
    public void Container_GetActiveCrowdControl_aggregates_all_flags()
    {
        var owner = MakeUnit();
        var (container, _) = MakeContainerWithSpies(owner);

        container.QueueBuff(MakeDef(entry: 1, cc: CrowdControlFlags.Root, policy: StackingPolicy.Unlimited), null);
        container.QueueBuff(MakeDef(entry: 2, cc: CrowdControlFlags.Silence, policy: StackingPolicy.Unlimited), null);
        container.Update(0);

        container.GetActiveCrowdControl().ShouldBe(CrowdControlFlags.Root | CrowdControlFlags.Silence);
    }

    [Fact]
    public void Container_RemoveAll_clears_all_buffs()
    {
        var owner = MakeUnit();
        var (container, spies) = MakeContainerWithSpies(owner);

        container.QueueBuff(MakeDef(entry: 1, effects: [MakeEffectDef()], policy: StackingPolicy.Unlimited), null);
        container.QueueBuff(MakeDef(entry: 2, effects: [MakeEffectDef()], policy: StackingPolicy.Unlimited), null);
        container.Update(0);

        container.RemoveAll();

        container.ActiveBuffs.Count.ShouldBe(0);
        spies.ShouldAllBe(s => s.EndCount == 1);
    }

    [Fact]
    public void Container_RemoveAll_deathClean_preserves_persistent_buffs()
    {
        var owner = MakeUnit();
        var (container, _) = MakeContainerWithSpies(owner);

        container.QueueBuff(MakeDef(entry: 1, persistsOnDeath: false, policy: StackingPolicy.Unlimited), null);
        container.QueueBuff(MakeDef(entry: 2, persistsOnDeath: true, policy: StackingPolicy.Unlimited), null);
        container.Update(0);

        container.RemoveAll(deathClean: true);

        container.ActiveBuffs.Count.ShouldBe(1);
        container.ActiveBuffs[0].Definition.Entry.ShouldBe((ushort)2);
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    //  BuffContainer â€” Slot recycling
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    [Fact]
    public void Container_recycles_slot_after_removal()
    {
        var owner = MakeUnit();
        var (container, _) = MakeContainerWithSpies(owner);

        container.QueueBuff(MakeDef(entry: 1), null);
        container.Update(0);

        var slot = container.ActiveBuffs[0].SlotId;
        container.RemoveByEntry(1);

        // Apply a new buff â€” should reuse the freed slot.
        container.QueueBuff(MakeDef(entry: 2), null);
        container.Update(1);

        container.ActiveBuffs[0].SlotId.ShouldBe(slot);
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    //  BuffContainer â€” Ticking
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    [Fact]
    public void Container_ticks_buff_at_interval()
    {
        var owner = MakeUnit();
        var (container, spies) = MakeContainerWithSpies(owner);
        var tickable = MakeEffectDef(invokeOn: BuffPhase.Tick);
        var def = MakeDef(durationMs: 10_000, intervalMs: 1000, effects: [tickable]);

        container.QueueBuff(def, null);
        container.Update(0);

        // Not yet due.
        container.Update(500);
        spies[0].TickCount.ShouldBe(0);

        // Due at 1000.
        container.Update(1000);
        spies[0].TickCount.ShouldBe(1);

        // Due again at 2000.
        container.Update(2000);
        spies[0].TickCount.ShouldBe(2);
    }

    [Fact]
    public void Container_does_not_tick_expired_buff()
    {
        var owner = MakeUnit();
        var (container, spies) = MakeContainerWithSpies(owner);
        var tickable = MakeEffectDef(invokeOn: BuffPhase.Tick);
        var def = MakeDef(durationMs: 1500, intervalMs: 1000, effects: [tickable]);

        container.QueueBuff(def, null);
        container.Update(0);

        container.Update(1000);
        spies[0].TickCount.ShouldBe(1);

        // Buff expires at 1500, so at 2000 it should be gone â€” no second tick.
        container.Update(2000);
        spies[0].TickCount.ShouldBe(1);
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    //  BuffContainer â€” Stacking Policies
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    [Fact]
    public void Stacking_Unique_refreshes_on_reapplication()
    {
        var owner = MakeUnit();
        var (container, _) = MakeContainerWithSpies(owner);
        var def = MakeDef(entry: 10, durationMs: 5000, maxStacks: 3, policy: StackingPolicy.Unique);

        container.QueueBuff(def, null);
        container.Update(0);

        container.ActiveBuffs.Count.ShouldBe(1);
        container.ActiveBuffs[0].StackLevel.ShouldBe((byte)1);

        // Reapply at tick 2000.
        container.QueueBuff(def, null);
        container.Update(2000);

        container.ActiveBuffs.Count.ShouldBe(1);
        container.ActiveBuffs[0].StackLevel.ShouldBe((byte)2);
        container.ActiveBuffs[0].EndTime.ShouldBe(7000L); // refreshed to 2000+5000
    }

    [Fact]
    public void Stacking_PerCaster_same_caster_refreshes()
    {
        var owner = MakeUnit();
        var caster = MakeUnit(2);
        var (container, _) = MakeContainerWithSpies(owner);
        var def = MakeDef(entry: 10, durationMs: 5000, maxStacks: 3, policy: StackingPolicy.PerCaster);

        container.QueueBuff(def, caster);
        container.Update(0);

        container.QueueBuff(def, caster);
        container.Update(1000);

        container.ActiveBuffs.Count.ShouldBe(1);
        container.ActiveBuffs[0].StackLevel.ShouldBe((byte)2);
    }

    [Fact]
    public void Stacking_PerCaster_different_caster_adds_new()
    {
        var owner = MakeUnit();
        var caster1 = MakeUnit(2);
        var caster2 = MakeUnit(3);
        var (container, _) = MakeContainerWithSpies(owner);
        var def = MakeDef(entry: 10, durationMs: 5000, policy: StackingPolicy.PerCaster);

        container.QueueBuff(def, caster1);
        container.QueueBuff(def, caster2);
        container.Update(0);

        container.ActiveBuffs.Count.ShouldBe(2);
    }

    [Fact]
    public void Stacking_Exclusive_replaces_same_group()
    {
        var owner = MakeUnit();
        var (container, _) = MakeContainerWithSpies(owner);
        var def1 = MakeDef(entry: 10, group: BuffGroup.SelfClassBuff, policy: StackingPolicy.Exclusive);
        var def2 = MakeDef(entry: 20, group: BuffGroup.SelfClassBuff, policy: StackingPolicy.Exclusive);

        container.QueueBuff(def1, null);
        container.Update(0);

        container.ActiveBuffs.Count.ShouldBe(1);
        container.ActiveBuffs[0].Definition.Entry.ShouldBe((ushort)10);

        container.QueueBuff(def2, null);
        container.Update(1000);

        container.ActiveBuffs.Count.ShouldBe(1);
        container.ActiveBuffs[0].Definition.Entry.ShouldBe((ushort)20);
    }

    [Fact]
    public void Stacking_HighestLevel_replaces_lower_level()
    {
        var owner = MakeUnit();
        var (container, _) = MakeContainerWithSpies(owner);
        var def = MakeDef(entry: 10, group: BuffGroup.Resurrection, policy: StackingPolicy.HighestLevel);

        container.QueueBuff(def, null, buffLevel: 1);
        container.Update(0);

        container.ActiveBuffs[0].BuffLevel.ShouldBe((byte)1);

        container.QueueBuff(def, null, buffLevel: 3);
        container.Update(1000);

        container.ActiveBuffs.Count.ShouldBe(1);
        container.ActiveBuffs[0].BuffLevel.ShouldBe((byte)3);
    }

    [Fact]
    public void Stacking_HighestLevel_rejects_lower_or_equal()
    {
        var owner = MakeUnit();
        var (container, _) = MakeContainerWithSpies(owner);
        var def = MakeDef(entry: 10, group: BuffGroup.Resurrection, policy: StackingPolicy.HighestLevel);

        container.QueueBuff(def, null, buffLevel: 5);
        container.Update(0);

        container.QueueBuff(def, null, buffLevel: 5);
        container.QueueBuff(def, null, buffLevel: 3);
        container.Update(1000);

        container.ActiveBuffs.Count.ShouldBe(1);
        container.ActiveBuffs[0].BuffLevel.ShouldBe((byte)5);
    }

    [Fact]
    public void Stacking_MaxCopies_rejects_beyond_limit()
    {
        var owner = MakeUnit();
        var caster1 = MakeUnit(2);
        var caster2 = MakeUnit(3);
        var caster3 = MakeUnit(4);
        var caster4 = MakeUnit(5);
        var (container, _) = MakeContainerWithSpies(owner);
        var def = MakeDef(entry: 10, policy: StackingPolicy.MaxCopies);
        // MaxCopies defaults to 3 in MakeDef.

        container.QueueBuff(def, caster1);
        container.QueueBuff(def, caster2);
        container.QueueBuff(def, caster3);
        container.QueueBuff(def, caster4); // Should be rejected.
        container.Update(0);

        container.ActiveBuffs.Count.ShouldBe(3);
    }

    [Fact]
    public void Stacking_Unlimited_allows_any_number()
    {
        var owner = MakeUnit();
        var (container, _) = MakeContainerWithSpies(owner);

        for (var i = 0; i < 10; i++)
            container.QueueBuff(MakeDef(entry: 10, policy: StackingPolicy.Unlimited), MakeUnit((ushort)(i + 10)));

        container.Update(0);

        container.ActiveBuffs.Count.ShouldBe(10);
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    //  BuffContainer â€” Combat Events
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    [Fact]
    public void NotifyCombatEvent_invokes_subscribed_effects()
    {
        var owner = MakeUnit();
        var (container, spies) = MakeContainerWithSpies(owner);
        var effectDef = MakeEffectDef(
            eventSub: CombatEventType.ReceivingDamage,
            priority: CombatEventPriority.DamageModification);
        var def = MakeDef(effects: [effectDef]);

        container.QueueBuff(def, null);
        container.Update(0);

        var ctx = new DamageContext();
        container.NotifyCombatEvent(CombatEventType.ReceivingDamage, ctx, null);

        spies[0].EventCount.ShouldBe(1);
        spies[0].LastEventType.ShouldBe(CombatEventType.ReceivingDamage);
    }

    [Fact]
    public void NotifyCombatEvent_skips_unsubscribed_events()
    {
        var owner = MakeUnit();
        var (container, spies) = MakeContainerWithSpies(owner);
        var effectDef = MakeEffectDef(eventSub: CombatEventType.ReceivingDamage);
        var def = MakeDef(effects: [effectDef]);

        container.QueueBuff(def, null);
        container.Update(0);

        // Fire a different event type.
        container.NotifyCombatEvent(CombatEventType.DealingDamage, null, null);

        spies[0].EventCount.ShouldBe(0);
    }

    [Fact]
    public void NotifyCombatEvent_respects_priority_ordering()
    {
        var owner = MakeUnit();
        var callOrder = new List<string>();

        var container = owner.Buffs;
        container.EffectFactory = def =>
        {
            var label = def.EventPriority.ToString();
            return new OrderTrackingEffect(def, label, callOrder);
        };

        var highPri = MakeEffectDef(
            eventSub: CombatEventType.ReceivingDamage,
            priority: CombatEventPriority.DamageModification);
        var midPri = MakeEffectDef(
            eventSub: CombatEventType.ReceivingDamage,
            priority: CombatEventPriority.AbsorbShield);
        var lowPri = MakeEffectDef(
            eventSub: CombatEventType.ReceivingDamage,
            priority: CombatEventPriority.FinalReaction);

        // Apply in reverse order â€” should still fire in priority order.
        container.QueueBuff(MakeDef(entry: 1, effects: [lowPri], policy: StackingPolicy.Unlimited), null);
        container.QueueBuff(MakeDef(entry: 2, effects: [highPri], policy: StackingPolicy.Unlimited), null);
        container.QueueBuff(MakeDef(entry: 3, effects: [midPri], policy: StackingPolicy.Unlimited), null);
        container.Update(0);

        container.NotifyCombatEvent(CombatEventType.ReceivingDamage, new DamageContext(), null);

        callOrder.Count.ShouldBe(3);
        callOrder[0].ShouldBe("DamageModification");
        callOrder[1].ShouldBe("AbsorbShield");
        callOrder[2].ShouldBe("FinalReaction");
    }

    [Fact]
    public void NotifyCombatEvent_fast_rejects_when_no_subscriptions()
    {
        var owner = MakeUnit();
        var (container, spies) = MakeContainerWithSpies(owner);
        // Buff with no event subscriptions.
        var def = MakeDef(effects: [MakeEffectDef(eventSub: CombatEventType.None)]);

        container.QueueBuff(def, null);
        container.Update(0);

        // Should not throw and should not invoke any effect.
        container.NotifyCombatEvent(CombatEventType.DealingDamage, null, null);

        spies[0].EventCount.ShouldBe(0);
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    //  BuffContainer â€” Override duration
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    [Fact]
    public void Container_applies_override_duration()
    {
        var owner = MakeUnit();
        var (container, _) = MakeContainerWithSpies(owner);
        var def = MakeDef(entry: 10, durationMs: 5000);

        container.QueueBuff(def, null, overrideDurationMs: 2000);
        container.Update(0);

        container.ActiveBuffs[0].EndTime.ShouldBe(2000L);
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    //  BuffContainer â€” UnitEntity integration
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    [Fact]
    public void UnitEntity_has_Buffs_at_construction()
    {
        var unit = MakeUnit();

        unit.Buffs.ShouldNotBeNull();
    }

    [Fact]
    public void UnitEntity_Update_drains_buff_queue()
    {
        var unit = MakeUnit();
        unit.Buffs.EffectFactory = _ => new SpyEffect(MakeEffectDef());
        var def = MakeDef();

        unit.Buffs.QueueBuff(def, null);
        unit.Update(0);

        unit.Buffs.ActiveBuffs.Count.ShouldBe(1);
    }

    [Fact]
    public void UnitEntity_Update_expires_timed_buff()
    {
        var unit = MakeUnit();
        var def = MakeDef(durationMs: 1000);

        unit.Buffs.QueueBuff(def, null);
        unit.Update(0);

        unit.Buffs.ActiveBuffs.Count.ShouldBe(1);

        unit.Update(1000);

        unit.Buffs.ActiveBuffs.Count.ShouldBe(0);
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    //  BuffDefinition â€” immutability and defaults
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    [Fact]
    public void BuffDefinition_defaults_are_sensible()
    {
        var def = new BuffDefinition
        {
            Entry = 1,
            Name = "Test",
            BuffClass = BuffClass.Buff0,
        };

        def.MaxStacks.ShouldBe((byte)1);
        def.InitialStacks.ShouldBe((byte)1);
        def.CanRefresh.ShouldBeTrue();
        def.DurationMs.ShouldBe(0u);
        def.IntervalMs.ShouldBe((ushort)0);
        def.Group.ShouldBe(BuffGroup.None);
        def.BuffType.ShouldBe(BuffType.None);
        def.CrowdControl.ShouldBe(CrowdControlFlags.None);
        def.PersistsOnDeath.ShouldBeFalse();
        def.Effects.Count.ShouldBe(0);
    }

    [Fact]
    public void BuffEffectDefinition_defaults_are_sensible()
    {
        var def = new BuffEffectDefinition
        {
            EffectType = BuffEffectType.StatModifier,
        };

        def.InvokeOn.ShouldBe(BuffPhase.Start);
        def.EventSubscription.ShouldBe(CombatEventType.None);
        def.EventChance.ShouldBe((byte)0);
        def.ConsumesStack.ShouldBeFalse();
        def.PrimaryValue.ShouldBe(0);
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    //  Helper â€” order-tracking effect for priority tests
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    private sealed class OrderTrackingEffect : IBuffEffect
    {
        private readonly string _label;
        private readonly List<string> _callOrder;

        public BuffEffectDefinition Definition { get; }

        public OrderTrackingEffect(BuffEffectDefinition definition, string label, List<string> callOrder)
        {
            Definition = definition;
            _label = label;
            _callOrder = callOrder;
        }

        public void OnStart(Buff buff, UnitEntity target) { }
        public void OnTick(Buff buff, UnitEntity target, long tick) { }
        public void OnEnd(Buff buff, UnitEntity target) { }

        public void OnCombatEvent(Buff buff, CombatEventType eventType, DamageContext? context, UnitEntity? instigator)
        {
            _callOrder.Add(_label);
        }
    }
}
