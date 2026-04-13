using Core.GameWorld.Entities;

namespace Core.GameWorld.Combat.Buffs;

/// <summary>
/// Runtime instance of an active buff on a <see cref="UnitEntity"/>.
/// <para>
/// Mutable — owned by a <see cref="BuffContainer"/> which runs on the region thread.
/// The <see cref="Definition"/> reference is immutable and shared; all per-instance
/// state lives here.
/// </para>
/// </summary>
public sealed class Buff
{
    /// <summary>Immutable definition (shared across all instances of this buff).</summary>
    public BuffDefinition Definition { get; }

    /// <summary>Slot index in the container (0–199). Used for client packets.</summary>
    public byte SlotId { get; internal set; }

    /// <summary>The entity that applied this buff. May be null for system buffs.</summary>
    public UnitEntity? Caster { get; }

    /// <summary>The entity this buff is applied to.</summary>
    public UnitEntity Target { get; }

    /// <summary>Current stack count (1 to <see cref="BuffDefinition.MaxStacks"/>).</summary>
    public byte StackLevel { get; private set; }

    /// <summary>Buff level (mastery tree rank, ability rank, etc.).</summary>
    public byte BuffLevel { get; init; }

    /// <summary>Tick timestamp (ms) when this buff expires. 0 = permanent.</summary>
    public long EndTime { get; private set; }

    /// <summary>Tick timestamp (ms) of the next tick. 0 = no ticking.</summary>
    public long NextTickTime { get; private set; }

    /// <summary>Whether the buff has been flagged for removal.</summary>
    public bool IsExpired { get; private set; }

    /// <summary>
    /// Active effect instances, one per <see cref="BuffDefinition.Effects"/> entry.
    /// Populated by <see cref="BuffContainer"/> via <see cref="BuffEffectFactory"/>
    /// or directly when the buff is applied.
    /// </summary>
    public IBuffEffect[] Effects { get; internal set; } = [];

    // ── Per-effect mutable state (shields, proc cooldowns) ───────────

    /// <summary>
    /// Remaining absorb amount for shield effects. Indexed by effect position.
    /// Null if no shield effects are present.
    /// </summary>
    public float[]? ShieldValues { get; set; }

    /// <summary>
    /// Tick timestamps for per-effect retrigger cooldowns. Indexed by effect position.
    /// Null if no effects have retrigger intervals.
    /// </summary>
    public long[]? RetriggerTimestamps { get; set; }

    public Buff(BuffDefinition definition, UnitEntity? caster, UnitEntity target, long currentTick)
    {
        Definition = definition;
        Caster = caster;
        Target = target;
        StackLevel = definition.InitialStacks;

        if (definition.DurationMs > 0)
            EndTime = currentTick + definition.DurationMs;

        if (definition.IntervalMs > 0)
            NextTickTime = currentTick + definition.IntervalMs;
    }

    /// <summary>
    /// Invokes <see cref="IBuffEffect.OnStart"/> on all effects.
    /// Called by <see cref="BuffContainer"/> after slot assignment.
    /// </summary>
    public void Start()
    {
        for (var i = 0; i < Effects.Length; i++)
            Effects[i].OnStart(this, Target);
    }

    /// <summary>
    /// Invokes <see cref="IBuffEffect.OnTick"/> on effects that have
    /// <see cref="BuffPhase.Tick"/> set, then advances <see cref="NextTickTime"/>.
    /// </summary>
    public void Tick(long currentTick)
    {
        for (var i = 0; i < Effects.Length; i++)
        {
            if ((Effects[i].Definition.InvokeOn & BuffPhase.Tick) != 0)
                Effects[i].OnTick(this, Target, currentTick);
        }

        if (Definition.IntervalMs > 0)
            NextTickTime = currentTick + Definition.IntervalMs;
    }

    /// <summary>
    /// Invokes <see cref="IBuffEffect.OnEnd"/> on all effects.
    /// Called by <see cref="BuffContainer"/> during removal.
    /// </summary>
    public void End()
    {
        for (var i = 0; i < Effects.Length; i++)
            Effects[i].OnEnd(this, Target);
    }

    /// <summary>
    /// Refresh duration to full and accumulate stacks (up to <see cref="BuffDefinition.MaxStacks"/>).
    /// </summary>
    public void Refresh(long currentTick)
    {
        if (Definition.DurationMs > 0)
            EndTime = currentTick + Definition.DurationMs;

        if (StackLevel < Definition.MaxStacks)
            StackLevel++;
    }

    /// <summary>
    /// Consume one stack. If stacks reach zero the buff is flagged as expired.
    /// </summary>
    public void ConsumeStack()
    {
        if (StackLevel > 0)
            StackLevel--;

        if (StackLevel == 0)
            IsExpired = true;
    }

    /// <summary>
    /// Check if the buff has expired based on time. Does not modify state.
    /// </summary>
    public bool HasExpired(long currentTick) =>
        EndTime > 0 && currentTick >= EndTime;

    /// <summary>
    /// Check if this buff is due for a tick at the given time. Does not modify state.
    /// </summary>
    public bool IsDueForTick(long currentTick) =>
        NextTickTime > 0 && currentTick >= NextTickTime;

    /// <summary>Mark the buff for removal during the next container update pass.</summary>
    public void FlagExpired() => IsExpired = true;
}
