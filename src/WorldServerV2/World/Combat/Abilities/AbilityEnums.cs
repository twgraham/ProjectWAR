namespace WorldServerV2.World.Combat.Abilities;

/// <summary>
/// Whether the ability is melee, ranged, verbal (magic), or a passive effect.
/// Determines which defense stat is checked and which stat bonuses apply.
/// <para>Matches V1 <c>AbilityType</c> values.</para>
/// </summary>
public enum AbilityType : byte
{
    None = 0,
    Melee = 1,
    Ranged = 2,
    Verbal = 3,
    Effect = 255,
}

/// <summary>
/// Cast state for an in-progress ability.
/// V1 uses implicit state via timers; V2 makes it explicit.
/// </summary>
public enum CastState : byte
{
    /// <summary>Effects applied immediately in execution phase.</summary>
    Instant = 0,

    /// <summary>Timer countdown — effects applied at completion.</summary>
    Casting = 1,

    /// <summary>Effects applied at intervals over duration.</summary>
    Channeling = 2,
}

/// <summary>
/// Where the ability originated (core, item proc, morale, etc.).
/// </summary>
public enum AbilityOrigin : byte
{
    None = 0,
    Standard = 1,
    Item = 2,
    Morale = 3,
}

/// <summary>
/// How the ability interacts with stealth.
/// </summary>
public enum AbilityStealthType : byte
{
    /// <summary>Cannot be used from stealth.</summary>
    Block = 0,

    /// <summary>Breaks stealth upon use.</summary>
    Break = 1,

    /// <summary>Can be used from stealth without breaking it.</summary>
    Ignore = 2,
}

/// <summary>
/// What weapon(s) must be equipped to use the ability.
/// <para>Matches V1 <c>WeaponRequirements</c>.</para>
/// </summary>
public enum WeaponRequirement : byte
{
    None = 0,
    MainHand = 1,
    OffHand = 2,
    Ranged = 3,
    TwoHander = 4,
    DualWield = 5,
    Shield = 6,
}

/// <summary>
/// Target selection type for ability commands and AoE sources.
/// Uses [Flags] to support composite targeting in V2 extensions,
/// but the base values match V1's <c>CommandTargetTypes</c>.
/// </summary>
public enum CommandTargetType : byte
{
    /// <summary>Re-use last resolved target.</summary>
    Last = 0,

    /// <summary>The caster.</summary>
    Caster = 1,

    /// <summary>A friendly target (not self).</summary>
    Ally = 2,

    /// <summary>A friendly target (including self).</summary>
    AllyOrSelf = 3,

    /// <summary>A hostile target.</summary>
    Enemy = 4,

    /// <summary>Career-specific target (guard target, oath friend, etc.).</summary>
    CareerTarget = 5,

    /// <summary>Host entity (for pets).</summary>
    Host = 6,

    /// <summary>Ally or career target.</summary>
    AllyOrCareerTarget = 7,

    /// <summary>Group members (not caster).</summary>
    Groupmates = 16,

    /// <summary>Group members (including caster).</summary>
    Group = 17,

    /// <summary>Grouped ally.</summary>
    GroupedAlly = 18,

    /// <summary>Entities within the group.</summary>
    WithinGroup = 19,

    /// <summary>The entity that triggered a combat event.</summary>
    EventInstigator = 32,

    /// <summary>Siege weapon target.</summary>
    Siege = 64,

    /// <summary>Siege cannon specifically.</summary>
    SiegeCannon = 68,

    /// <summary>NPC ally (for NPC healer abilities).</summary>
    NpcAlly = 69,
}

/// <summary>
/// What an ability command does when it executes. Replaces V1's string-keyed
/// <c>AbilityEffectInvoker</c> dispatch with a typed enum.
/// <para>Compile-time exhaustiveness checking ensures every type is handled.</para>
/// </summary>
public enum AbilityEffectType : byte
{
    // ── Damage ───────────────────────────────────────────────────────
    DealDamage = 0,
    MultipleDealDamage = 1,
    BounceDamage = 2,
    Slay = 3,
    StealLife = 4,

    // ── Buff application ─────────────────────────────────────────────
    InvokeBuff = 10,
    InvokeAura = 11,
    InvokeLinkedBuff = 12,

    // ── Crowd control / movement ─────────────────────────────────────
    Knockback = 20,
    Pull = 21,
    JumpTo = 22,

    // ── Cleansing ────────────────────────────────────────────────────
    CleanseCC = 30,
    CleanseDebuffType = 31,

    // ── Utility ──────────────────────────────────────────────────────
    Interrupt = 40,
    SummonPet = 41,

    // ── Resource manipulation ────────────────────────────────────────
    ModifyCareerResource = 50,
    ModifyMorale = 51,
    ModifyActionPoints = 52,

    // ── Ground / positional ──────────────────────────────────────────
    GroundEffect = 60,
    CreateLandMine = 61,
}

/// <summary>
/// When an ability modifier is applied during the cast pipeline.
/// </summary>
public enum ModifierStage : byte
{
    /// <summary>Applied during initiation (handler thread, before cast bar).</summary>
    PreCast = 0,

    /// <summary>Applied during execution (region thread, after cast completes).</summary>
    PostCast = 1,

    /// <summary>Applied to the buff that the ability invokes.</summary>
    Buff = 2,

    /// <summary>Applied on delayed effects (projectile arrival).</summary>
    Delayed = 3,
}

/// <summary>
/// Why an ability cast failed. Sent to the client as the failure code.
/// </summary>
public enum AbilityFailure : byte
{
    Ok = 0,
    NotEnoughAp = 1,
    NotEnoughResource = 2,
    OutOfRange = 3,
    TooClose = 4,
    InvalidTarget = 5,
    Cooldown = 6,
    Silenced = 7,
    Disarmed = 8,
    Knockdown = 9,
    Moving = 10,
    NoLineOfSight = 11,
    WrongWeapon = 12,
    AlreadyActive = 13,
    TargetDead = 14,
    CasterDead = 15,
    Interrupted = 16,
    Cancelled = 17,
    StealthRequired = 18,
}
