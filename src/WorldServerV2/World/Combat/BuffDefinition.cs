using WorldServerV2.World.Stats;

namespace WorldServerV2.World.Combat;

/// <summary>
/// Immutable definition of a buff loaded from the database (via GameDataStore).
/// Each <see cref="Buffs.Buff"/> instance holds a reference to its definition.
/// The definition is never modified at runtime.
/// </summary>
public sealed class BuffDefinition
{
    public required ushort Entry { get; init; }
    public required string Name { get; init; }

    // ── Classification ───────────────────────────────────────────────

    /// <summary>Buff class for stat-modifier stacking (Buff0/Buff1/Tactic/Career).</summary>
    public required BuffClass BuffClass { get; init; }

    /// <summary>Debuff cleanse type (Hex/Curse/Ailment/Blessing/Enchantment/None).</summary>
    public BuffType BuffType { get; init; }

    /// <summary>Group-based stacking category.</summary>
    public BuffGroup Group { get; init; }

    /// <summary>How this buff stacks with existing copies.</summary>
    public StackingPolicy StackingPolicy { get; init; }

    // ── Duration & ticking ───────────────────────────────────────────

    /// <summary>Base duration in milliseconds. 0 = permanent (until removed).</summary>
    public uint DurationMs { get; init; }

    /// <summary>Tick interval in milliseconds. 0 = no ticking.</summary>
    public ushort IntervalMs { get; init; }

    // ── Stacking ─────────────────────────────────────────────────────

    /// <summary>Maximum stack count per application source.</summary>
    public byte MaxStacks { get; init; } = 1;

    /// <summary>Initial stacks applied on first application.</summary>
    public byte InitialStacks { get; init; } = 1;

    /// <summary>
    /// Maximum copies of this buff on one entity. Only used with
    /// <see cref="StackingPolicy.MaxCopies"/>. 0 = unlimited.
    /// </summary>
    public byte MaxCopies { get; init; }

    /// <summary>Whether re-application refreshes duration and accumulates stacks.</summary>
    public bool CanRefresh { get; init; } = true;

    // ── Persistence ──────────────────────────────────────────────────

    /// <summary>Buff persists through death.</summary>
    public bool PersistsOnDeath { get; init; }

    /// <summary>Buff persists through logout (saved to DB).</summary>
    public bool PersistsOnLogout { get; init; }

    /// <summary>Buff only active while target is dead (e.g. resurrection buffs).</summary>
    public bool RequiresTargetDead { get; init; }

    // ── CC ───────────────────────────────────────────────────────────

    /// <summary>Crowd-control flags applied while this buff is active.</summary>
    public CrowdControlFlags CrowdControl { get; init; }

    // ── Mastery ──────────────────────────────────────────────────────

    /// <summary>Mastery tree (0 = core, 1/2/3 = spec paths).</summary>
    public byte MasteryTree { get; init; }

    // ── Effects ──────────────────────────────────────────────────────

    /// <summary>
    /// Ordered list of effect definitions. Each becomes an <see cref="Buffs.IBuffEffect"/>
    /// instance when the buff is applied.
    /// </summary>
    public IReadOnlyList<BuffEffectDefinition> Effects { get; init; } = [];
}

/// <summary>
/// Immutable definition of a single effect within a <see cref="BuffDefinition"/>.
/// Replaces V1's <c>BuffCommandInfo</c>.
/// </summary>
public sealed class BuffEffectDefinition
{
    /// <summary>What type of effect this is (stat mod, DoT, proc, etc.).</summary>
    public required BuffEffectType EffectType { get; init; }

    /// <summary>When this effect is invoked during the buff lifecycle.</summary>
    public BuffPhase InvokeOn { get; init; } = BuffPhase.Start;

    // ── Event subscription (for proc effects) ────────────────────────

    /// <summary>Combat event to subscribe to. <c>None</c> = not event-driven.</summary>
    public CombatEventType EventSubscription { get; init; }

    /// <summary>Priority tier for event processing.</summary>
    public CombatEventPriority EventPriority { get; init; }

    /// <summary>Proc chance (0–100). 0 = always triggers.</summary>
    public byte EventChance { get; init; }

    /// <summary>Minimum time between triggers in milliseconds.</summary>
    public ushort RetriggerIntervalMs { get; init; }

    /// <summary>Whether triggering this effect consumes a stack.</summary>
    public bool ConsumesStack { get; init; }

    // ── Parameters ───────────────────────────────────────────────────

    /// <summary>Primary parameter (stat ID, damage value, etc.).</summary>
    public int PrimaryValue { get; init; }

    /// <summary>Secondary parameter.</summary>
    public int SecondaryValue { get; init; }

    /// <summary>Tertiary parameter.</summary>
    public int TertiaryValue { get; init; }

    // ── Stat modifier specifics ──────────────────────────────────────

    /// <summary>Which stat to modify (for stat modifier effects).</summary>
    public StatId StatId { get; init; }

    /// <summary>Buff class override for stat modification (allows effect-level class).</summary>
    public BuffClass? BuffClassOverride { get; init; }

    // ── AoE targeting ────────────────────────────────────────────────

    /// <summary>Effect radius for AoE effects (0 = single target).</summary>
    public byte EffectRadius { get; init; }

    /// <summary>Cone angle for directional AoE (0 = full circle).</summary>
    public short EffectAngle { get; init; }

    /// <summary>Maximum targets for AoE effects.</summary>
    public byte MaxTargets { get; init; }
}
