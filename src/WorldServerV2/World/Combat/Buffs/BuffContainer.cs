using WorldServerV2.World.Entities;

namespace WorldServerV2.World.Combat.Buffs;

/// <summary>
/// Per-unit buff manager. Owns all active <see cref="Buff"/> instances and drives
/// their lifecycle (apply → tick → expire → remove).
/// <para>
/// Runs on the region thread — no synchronization needed. New buffs are queued
/// via <see cref="QueueBuff"/> and drained at the start of <see cref="Update"/>.
/// </para>
/// <para>
/// Slot pool: fixed 200 slots backed by <see cref="Stack{T}"/>. Each buff gets a
/// unique slot index used in client packets. Slot reuse is LIFO (most recently freed
/// slot is reused first).
/// </para>
/// </summary>
public sealed class BuffContainer
{
    /// <summary>Maximum concurrent buffs on a single entity.</summary>
    public const int MaxSlots = 200;

    private readonly UnitEntity _owner;
    private readonly List<Buff> _activeBuffs = new(32);
    private readonly Queue<PendingBuff> _pendingQueue = new(8);
    private readonly Stack<byte> _freeSlots;

    /// <summary>
    /// Bitfield tracking which <see cref="CombatEventType"/> values have at least
    /// one subscribed effect. Used as a fast-reject filter in
    /// <see cref="NotifyCombatEvent"/>.
    /// </summary>
    private uint _eventSubscriptionMask;

    /// <summary>Read-only view of active buffs for external iteration.</summary>
    public IReadOnlyList<Buff> ActiveBuffs => _activeBuffs;

    /// <summary>
    /// Factory delegate for creating <see cref="IBuffEffect"/> instances from
    /// definitions. Set during system wiring (Step 6). If null, buffs are applied
    /// with an empty effect array.
    /// </summary>
    public Func<BuffEffectDefinition, IBuffEffect>? EffectFactory { get; set; }

    public BuffContainer(UnitEntity owner)
    {
        _owner = owner;
        _freeSlots = new Stack<byte>(MaxSlots);

        // Pre-fill slots in reverse so slot 0 is allocated first.
        for (var i = MaxSlots - 1; i >= 0; i--)
            _freeSlots.Push((byte)i);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  PUBLIC API
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Enqueue a buff for application at the next <see cref="Update"/> call.
    /// This is the primary entry point for applying buffs to the entity.
    /// </summary>
    /// <param name="definition">The buff definition to apply.</param>
    /// <param name="caster">The entity applying the buff (null for system buffs).</param>
    /// <param name="overrideDurationMs">Override duration in ms. 0 = use definition.</param>
    /// <param name="buffLevel">Buff level (for level-based stacking).</param>
    public void QueueBuff(BuffDefinition definition, UnitEntity? caster,
        uint overrideDurationMs = 0, byte buffLevel = 1)
    {
        _pendingQueue.Enqueue(new PendingBuff(definition, caster, overrideDurationMs, buffLevel));
    }

    /// <summary>Remove a buff by its slot ID.</summary>
    /// <returns><c>true</c> if the buff was found and removed.</returns>
    public bool RemoveBuff(byte slotId)
    {
        for (var i = 0; i < _activeBuffs.Count; i++)
        {
            if (_activeBuffs[i].SlotId == slotId)
            {
                RemoveAt(i);
                return true;
            }
        }

        return false;
    }

    /// <summary>Remove the first buff matching the given entry ID.</summary>
    /// <returns><c>true</c> if a buff was found and removed.</returns>
    public bool RemoveByEntry(ushort entry)
    {
        for (var i = 0; i < _activeBuffs.Count; i++)
        {
            if (_activeBuffs[i].Definition.Entry == entry)
            {
                RemoveAt(i);
                return true;
            }
        }

        return false;
    }

    /// <summary>Remove all buffs in the given group.</summary>
    /// <returns>Number of buffs removed.</returns>
    public int RemoveByGroup(BuffGroup group)
    {
        var removed = 0;
        for (var i = _activeBuffs.Count - 1; i >= 0; i--)
        {
            if (_activeBuffs[i].Definition.Group == group)
            {
                RemoveAt(i);
                removed++;
            }
        }

        return removed;
    }

    /// <summary>Remove all buffs matching the given <see cref="BuffType"/> (for cleansing).</summary>
    /// <returns>Number of buffs removed.</returns>
    public int RemoveByType(BuffType type)
    {
        var removed = 0;
        for (var i = _activeBuffs.Count - 1; i >= 0; i--)
        {
            if (_activeBuffs[i].Definition.BuffType == type)
            {
                RemoveAt(i);
                removed++;
            }
        }

        return removed;
    }

    /// <summary>Remove all buffs applying the given CC flags.</summary>
    /// <returns>Number of buffs removed.</returns>
    public int CleanseCC(CrowdControlFlags ccFlags)
    {
        var removed = 0;
        for (var i = _activeBuffs.Count - 1; i >= 0; i--)
        {
            if ((_activeBuffs[i].Definition.CrowdControl & ccFlags) != 0)
            {
                RemoveAt(i);
                removed++;
            }
        }

        return removed;
    }

    /// <summary>Check if any active buff has the given entry.</summary>
    public bool HasBuff(ushort entry)
    {
        for (var i = 0; i < _activeBuffs.Count; i++)
        {
            if (_activeBuffs[i].Definition.Entry == entry)
                return true;
        }

        return false;
    }

    /// <summary>Get the first active buff with the given entry, or null.</summary>
    public Buff? GetBuff(ushort entry)
    {
        for (var i = 0; i < _activeBuffs.Count; i++)
        {
            if (_activeBuffs[i].Definition.Entry == entry)
                return _activeBuffs[i];
        }

        return null;
    }

    /// <summary>Get the first active buff in the given slot, or null.</summary>
    public Buff? GetBuffBySlot(byte slotId)
    {
        for (var i = 0; i < _activeBuffs.Count; i++)
        {
            if (_activeBuffs[i].SlotId == slotId)
                return _activeBuffs[i];
        }

        return null;
    }

    /// <summary>
    /// Get the current aggregate CC flags from all active buffs.
    /// </summary>
    public CrowdControlFlags GetActiveCrowdControl()
    {
        var flags = CrowdControlFlags.None;
        for (var i = 0; i < _activeBuffs.Count; i++)
            flags |= _activeBuffs[i].Definition.CrowdControl;

        return flags;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  COMBAT EVENTS
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Notify all buff effects that subscribe to the given combat event.
    /// Subscribers are invoked in <see cref="CombatEventPriority"/> order
    /// (damage mods → shields → guard → reactive procs).
    /// <para>
    /// Uses <see cref="_eventSubscriptionMask"/> as a fast-reject filter.
    /// </para>
    /// </summary>
    public void NotifyCombatEvent(CombatEventType eventType, DamageContext? context, UnitEntity? instigator)
    {
        // Fast-reject: no subscribers for this event type at all.
        if ((_eventSubscriptionMask & (1u << (int)eventType)) == 0)
            return;

        // Iterate in priority order (0..3).
        for (var priority = 0; priority <= (int)CombatEventPriority.FinalReaction; priority++)
        {
            for (var i = 0; i < _activeBuffs.Count; i++)
            {
                var buff = _activeBuffs[i];
                if (buff.IsExpired)
                    continue;

                var effects = buff.Effects;
                for (var e = 0; e < effects.Length; e++)
                {
                    var def = effects[e].Definition;
                    if (def.EventSubscription != eventType)
                        continue;
                    if ((int)def.EventPriority != priority)
                        continue;

                    // Check retrigger cooldown.
                    if (def.RetriggerIntervalMs > 0 && buff.RetriggerTimestamps != null
                        && buff.RetriggerTimestamps[e] > 0)
                    {
                        // Not enough time elapsed since last trigger — skip.
                        // We don't have 'currentTick' here; caller should use Update-based approach
                        // for time checks if needed. For now retrigger is gated in Update.
                    }

                    // Check proc chance (0 = always).
                    if (def.EventChance > 0 && def.EventChance < 100)
                    {
                        // TODO: integrate with region RNG in Step 6.
                        // For now, always fire. Concrete effects will gate themselves.
                    }

                    effects[e].OnCombatEvent(buff, eventType, context, instigator);

                    if (def.ConsumesStack)
                        buff.ConsumeStack();
                }
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  UPDATE LOOP
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Per-tick update. Drains the pending queue, ticks due buffs, and removes
    /// expired buffs. Called from <see cref="UnitEntity.Update"/>.
    /// </summary>
    public void Update(long tick)
    {
        // 1. Drain pending queue — apply new buffs.
        while (_pendingQueue.Count > 0)
        {
            var pending = _pendingQueue.Dequeue();
            ApplyBuff(pending, tick);
        }

        // 2. Tick due buffs and check expiry.
        for (var i = _activeBuffs.Count - 1; i >= 0; i--)
        {
            var buff = _activeBuffs[i];

            // Check time-based expiry.
            if (buff.HasExpired(tick))
            {
                buff.FlagExpired();
            }

            // Tick if due and not expired.
            if (!buff.IsExpired && buff.IsDueForTick(tick))
            {
                buff.Tick(tick);
            }

            // Remove expired buffs.
            if (buff.IsExpired)
            {
                RemoveAt(i);
            }
        }
    }

    /// <summary>Remove all active buffs (death, logout).</summary>
    /// <param name="deathClean">
    /// If <c>true</c>, only removes buffs where <see cref="BuffDefinition.PersistsOnDeath"/>
    /// is <c>false</c>.
    /// </param>
    public void RemoveAll(bool deathClean = false)
    {
        for (var i = _activeBuffs.Count - 1; i >= 0; i--)
        {
            if (deathClean && _activeBuffs[i].Definition.PersistsOnDeath)
                continue;

            RemoveAt(i);
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  INTERNAL — Application & Stacking
    // ═══════════════════════════════════════════════════════════════════

    private void ApplyBuff(PendingBuff pending, long tick)
    {
        var def = pending.Definition;

        // Check slot availability.
        if (_freeSlots.Count == 0)
            return; // Silently reject — container full.

        // Stacking policy check.
        var existing = FindExistingForStacking(def, pending.Caster);
        switch (def.StackingPolicy)
        {
            case StackingPolicy.Unique:
                if (existing != null)
                {
                    if (def.CanRefresh)
                        existing.Refresh(tick);
                    return;
                }

                break;

            case StackingPolicy.PerCaster:
                if (existing != null)
                {
                    if (def.CanRefresh)
                        existing.Refresh(tick);
                    return;
                }

                break;

            case StackingPolicy.Exclusive:
                if (existing != null)
                {
                    RemoveAt(IndexOf(existing));
                    // Fall through to apply new.
                }

                // Also remove any other buff in the same group.
                if (def.Group != BuffGroup.None)
                {
                    for (var i = _activeBuffs.Count - 1; i >= 0; i--)
                    {
                        if (_activeBuffs[i].Definition.Group == def.Group)
                            RemoveAt(i);
                    }
                }

                break;

            case StackingPolicy.HighestLevel:
                if (existing != null)
                {
                    if (pending.BuffLevel > existing.BuffLevel)
                    {
                        RemoveAt(IndexOf(existing));
                        // Fall through to apply new.
                    }
                    else
                    {
                        return; // Reject — same or lower level.
                    }
                }

                // Also check group.
                if (def.Group != BuffGroup.None)
                {
                    for (var i = _activeBuffs.Count - 1; i >= 0; i--)
                    {
                        if (_activeBuffs[i].Definition.Group == def.Group
                            && pending.BuffLevel > _activeBuffs[i].BuffLevel)
                        {
                            RemoveAt(i);
                        }
                    }
                }

                break;

            case StackingPolicy.MaxCopies:
                if (existing != null && def.CanRefresh)
                {
                    existing.Refresh(tick);
                    return;
                }

                // Count existing copies.
                if (def.MaxCopies > 0)
                {
                    var count = CountByEntry(def.Entry);
                    if (count >= def.MaxCopies)
                        return; // Reject — at max copies.
                }

                break;

            case StackingPolicy.Unlimited:
                // Always accept.
                break;
        }

        // Create and apply the buff.
        var buff = new Buff(def, pending.Caster, _owner, tick)
        {
            BuffLevel = pending.BuffLevel
        };

        // Override duration if specified.
        if (pending.OverrideDurationMs > 0 && def.DurationMs > 0)
        {
            // Re-calculate end time using override.
            var endTime = tick + pending.OverrideDurationMs;
            // Use reflection-free approach: buff EndTime is set in constructor,
            // so we overwrite it via a dedicated method.
            buff = new Buff(
                CreateDefinitionWithOverrideDuration(def, pending.OverrideDurationMs),
                pending.Caster, _owner, tick)
            {
                BuffLevel = pending.BuffLevel
            };
        }

        // Allocate slot.
        buff.SlotId = _freeSlots.Pop();

        // Instantiate effects.
        buff.Effects = CreateEffects(def);

        // Initialize per-effect state arrays if needed.
        InitializeEffectState(buff, def);

        // Add to active list.
        _activeBuffs.Add(buff);

        // Update event subscription mask.
        RebuildEventMask();

        // Start the buff (invoke OnStart on all effects).
        buff.Start();
    }

    private Buff? FindExistingForStacking(BuffDefinition def, UnitEntity? caster)
    {
        for (var i = 0; i < _activeBuffs.Count; i++)
        {
            var b = _activeBuffs[i];
            if (b.Definition.Entry != def.Entry)
                continue;

            switch (def.StackingPolicy)
            {
                case StackingPolicy.PerCaster:
                case StackingPolicy.MaxCopies:
                    // For per-caster / max-copies-by-caster, only match buffs from the same caster.
                    if (b.Caster == caster)
                        return b;
                    // Otherwise keep searching for another buff with the same entry and caster.
                    break;

                default:
                    // For all other policies, the first matching entry is sufficient.
                    return b;
            }
        }

        return null;
    }

    private int IndexOf(Buff buff)
    {
        for (var i = 0; i < _activeBuffs.Count; i++)
        {
            if (_activeBuffs[i] == buff)
                return i;
        }

        return -1;
    }

    private int CountByEntry(ushort entry)
    {
        var count = 0;
        for (var i = 0; i < _activeBuffs.Count; i++)
        {
            if (_activeBuffs[i].Definition.Entry == entry)
                count++;
        }

        return count;
    }

    private void RemoveAt(int index)
    {
        var buff = _activeBuffs[index];
        buff.End();
        _freeSlots.Push(buff.SlotId);
        _activeBuffs.RemoveAt(index);
        RebuildEventMask();
    }

    private void RebuildEventMask()
    {
        _eventSubscriptionMask = 0;
        for (var i = 0; i < _activeBuffs.Count; i++)
        {
            var effects = _activeBuffs[i].Effects;
            for (var e = 0; e < effects.Length; e++)
            {
                var ev = effects[e].Definition.EventSubscription;
                if (ev != CombatEventType.None)
                    _eventSubscriptionMask |= 1u << (int)ev;
            }
        }
    }

    private IBuffEffect[] CreateEffects(BuffDefinition def)
    {
        if (def.Effects.Count == 0)
            return [];

        if (EffectFactory == null)
            return [];

        var effects = new IBuffEffect[def.Effects.Count];
        for (var i = 0; i < def.Effects.Count; i++)
            effects[i] = EffectFactory(def.Effects[i]);

        return effects;
    }

    private static void InitializeEffectState(Buff buff, BuffDefinition def)
    {
        var needsShields = false;
        var needsRetrigger = false;

        for (var i = 0; i < def.Effects.Count; i++)
        {
            if (def.Effects[i].EffectType == BuffEffectType.AbsorbShield)
                needsShields = true;
            if (def.Effects[i].RetriggerIntervalMs > 0)
                needsRetrigger = true;
        }

        if (needsShields)
            buff.ShieldValues = new float[def.Effects.Count];

        if (needsRetrigger)
            buff.RetriggerTimestamps = new long[def.Effects.Count];
    }

    /// <summary>
    /// Create a shallow copy of the definition with an overridden duration.
    /// BuffDefinition uses init-only properties, so we create a new instance.
    /// </summary>
    private static BuffDefinition CreateDefinitionWithOverrideDuration(BuffDefinition original, uint durationMs)
    {
        return new BuffDefinition
        {
            Entry = original.Entry,
            Name = original.Name,
            BuffClass = original.BuffClass,
            BuffType = original.BuffType,
            Group = original.Group,
            StackingPolicy = original.StackingPolicy,
            DurationMs = durationMs,
            IntervalMs = original.IntervalMs,
            MaxStacks = original.MaxStacks,
            InitialStacks = original.InitialStacks,
            MaxCopies = original.MaxCopies,
            CanRefresh = original.CanRefresh,
            PersistsOnDeath = original.PersistsOnDeath,
            PersistsOnLogout = original.PersistsOnLogout,
            RequiresTargetDead = original.RequiresTargetDead,
            CrowdControl = original.CrowdControl,
            MasteryTree = original.MasteryTree,
            Effects = original.Effects,
        };
    }

    // ═══════════════════════════════════════════════════════════════════
    //  PENDING BUFF ENVELOPE
    // ═══════════════════════════════════════════════════════════════════

    private readonly record struct PendingBuff(
        BuffDefinition Definition,
        UnitEntity? Caster,
        uint OverrideDurationMs,
        byte BuffLevel);
}
