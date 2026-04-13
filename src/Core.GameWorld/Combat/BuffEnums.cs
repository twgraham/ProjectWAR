namespace Core.GameWorld.Combat;

/// <summary>
/// Combat events that buffs can subscribe to. When a combat event fires,
/// <see cref="Buffs.BuffContainer.NotifyCombatEvent"/> iterates subscribed
/// buff effects in priority order.
/// <para>Matches V1 <c>BuffCombatEvents</c> enum values exactly.</para>
/// </summary>
public enum CombatEventType : byte
{
    None = 0,
    AttackedTarget = 1,
    WasDefended = 2,
    DealingDamage = 3,
    DealtDamage = 4,
    DirectDamageDealt = 5,
    WasAttacked = 6,
    DefendedAgainst = 7,
    ShieldPass = 8,
    ReceivingDamage = 9,
    ReceivedDamage = 10,
    DirectDamageReceived = 11,
    DealingHeal = 12,
    ReceivingHeal = 13,
    DirectHealDealt = 14,
    DirectHealReceived = 15,
    OnKill = 16,
    OnDie = 17,
    OnResurrect = 18,
    AbilityStarted = 19,
    AbilityCasted = 20,
    PetEvent = 21,
    ResourceGained = 22,
    ResourceLost = 23,
    ResourceSet = 24,
    MainWeaponChanged = 25,
    ShieldChanged = 26,
    Manual = 27,
    OnAcceptResurrection = 28,
}

/// <summary>
/// Priority tiers for buff combat event processing. Lower value = processed first.
/// Ensures deterministic ordering: damage mods → shields → guard → reactive procs.
/// </summary>
public enum CombatEventPriority : byte
{
    /// <summary>% increase/decrease (DealingDamage / ReceivingDamage modifiers).</summary>
    DamageModification = 0,

    /// <summary>Absorb shields consume damage.</summary>
    AbsorbShield = 1,

    /// <summary>Guard damage split to tank.</summary>
    Guard = 2,

    /// <summary>DealtDamage / ReceivedDamage — reactive procs after final damage.</summary>
    FinalReaction = 3,
}

/// <summary>
/// Crowd-control type flags. Multiple CC types can be active simultaneously.
/// <para>Matches V1 <c>CrowdControlTypes</c> values exactly.</para>
/// </summary>
[Flags]
public enum CrowdControlFlags : byte
{
    None = 0,
    Snare = 1,
    Root = 2,
    Disarm = 4,
    Silence = 8,
    Knockdown = 16,
    Stagger = 32,
    Grapple = 128,

    // ── Composite masks ──────────────────────────────────────────────
    MoveImpedance = Snare | Root,
    Unstoppable = Disarm | Silence | Knockdown,
    AllStandard = Snare | Root | Disarm | Silence | Knockdown,
    Disabled = Knockdown | Stagger,
    NoAutoAttack = Disarm | Knockdown | Stagger,
    All = 255,
}

/// <summary>
/// Group-based buff stacking category. Buffs in the same group follow
/// group-specific replacement rules (one per group, level-based, etc.).
/// <para>Matches V1 <c>BuffGroups</c> values.</para>
/// </summary>
public enum BuffGroup : byte
{
    None = 0,
    SelfClassBuff = 1,
    OtherClassBuff = 2,
    SelfClassSecondaryBuff = 3,
    Aura = 5,
    Vanity = 6,
    Resurrection = 7,
    Detaunt = 10,
    HealPotion = 20,
    StatPotion = 21,
    DefensePotion = 22,
    Caltrops = 23,
    SharedCooldown1 = 24,
    ItemProc = 30,
    HoldTheLine = 50,
    Guard = 51,
    OathFriend = 52,
}

/// <summary>
/// Debuff cleanse type. Abilities that cleanse debuffs target a specific type.
/// </summary>
public enum BuffType : byte
{
    None = 0,
    Hex = 1,
    Curse = 2,
    Ailment = 3,
    Blessing = 4,
    Enchantment = 5,
}

/// <summary>
/// When a buff command should be invoked during the buff lifecycle.
/// </summary>
[Flags]
public enum BuffPhase : byte
{
    None = 0,
    Start = 1,
    Tick = 2,
    End = 4,
}

/// <summary>
/// Stacking policy for buff application. Determines how a new buff interacts
/// with existing buffs of the same entry or group.
/// </summary>
public enum StackingPolicy : byte
{
    /// <summary>
    /// One per entry. Re-application refreshes duration and accumulates stacks
    /// up to <c>MaxStacks</c>.
    /// </summary>
    Unique = 0,

    /// <summary>
    /// One copy per caster per entry. Same caster refreshes; different caster
    /// adds a new copy.
    /// </summary>
    PerCaster = 1,

    /// <summary>
    /// One buff in the group total. New application replaces old.
    /// </summary>
    Exclusive = 2,

    /// <summary>
    /// One buff in group. Higher-level buff replaces lower. Same or lower is rejected.
    /// </summary>
    HighestLevel = 3,

    /// <summary>
    /// Up to N instances (from <c>MaxCopies</c>). Same caster refreshes existing.
    /// </summary>
    MaxCopies = 4,

    /// <summary>
    /// No limit on copies (e.g. guard buffs).
    /// </summary>
    Unlimited = 5,
}

/// <summary>
/// Categorizes the type of effect a buff command performs. Used for dispatch
/// in the V2 system instead of V1's string-keyed delegates.
/// </summary>
public enum BuffEffectType : byte
{
    // ── Stat modification ────────────────────────────────────────────
    StatModifier = 0,
    PercentageStatModifier = 1,

    // ── Damage / Heal over time ──────────────────────────────────────
    DamageOverTime = 10,
    HealOverTime = 11,

    // ── Crowd control ────────────────────────────────────────────────
    CrowdControl = 20,
    SpeedModifier = 21,

    // ── Shields & damage transfer ────────────────────────────────────
    AbsorbShield = 30,
    DamageSplit = 31,

    // ── Proc-driven effects ──────────────────────────────────────────
    ProcDamage = 40,
    ProcHeal = 41,
    ProcBuff = 42,

    // ── Resource ─────────────────────────────────────────────────────
    ResourceModifier = 50,

    // ── Aura ─────────────────────────────────────────────────────────
    AuraPropagation = 60,

    // ── Utility ──────────────────────────────────────────────────────
    GrantAbility = 70,
    Detaunt = 71,
    Bolster = 72,
}
