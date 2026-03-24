namespace WorldServerV2.World.Stats;

/// <summary>
/// Strongly-typed stat identifier. Values match the V1 <c>Stats</c> enum and the
/// wire protocol — do <b>not</b> renumber.
/// <para>
/// The array is indexed [0..<see cref="StatConstants.SlotCount"/>]. Gaps (17-20, 96-99) are unused
/// but reserved to keep alignment with DB columns and the client.
/// </para>
/// </summary>
public enum StatId : byte
{
    None = 0,

    // ── Primary Attributes (1–9) ─────────────────────────────────────────
    Strength = 1,
    Agility = 2,
    Willpower = 3,
    Toughness = 4,
    Wounds = 5,
    Initiative = 6,
    WeaponSkill = 7,
    BallisticSkill = 8,
    Intelligence = 9,

    // ── Defensive Skills / Display (10–16) ───────────────────────────────
    BlockSkill = 10,
    ParrySkill = 11,
    EvadeSkill = 12,
    DisruptSkill = 13,
    SpiritResistance = 14,
    ElementalResistance = 15,
    CorporealResistance = 16,

    // ── Damage Modifiers (22–27) ─────────────────────────────────────────
    IncomingDamage = 22,
    IncomingDamagePercent = 23,
    OutgoingDamage = 24,
    OutgoingDamagePercent = 25,
    Armor = 26,
    Velocity = 27,

    // ── Combat Ratings (28–47) ───────────────────────────────────────────
    Block = 28,
    Parry = 29,
    Evade = 30,
    Disrupt = 31,
    ActionPointRegen = 32,
    MoraleRegen = 33,
    Cooldown = 34,
    BuildTime = 35,
    CriticalDamage = 36,
    Range = 37,
    AutoAttackSpeed = 38,
    Radius = 39,
    AutoAttackDamage = 40,
    ActionPointCost = 41,
    CriticalHitRate = 42,
    CriticalDamageTaken = 43,
    EffectResist = 44,
    EffectBuff = 45,
    MinimumRange = 46,
    DamageAbsorb = 47,

    // ── Setback & NPC (48–58) ────────────────────────────────────────────
    SetbackChance = 48,
    SetbackValue = 49,
    XpWorth = 50,
    RenownWorth = 51,
    InfluenceWorth = 52,
    MonetaryWorth = 53,
    AggroRadius = 54,
    TargetDuration = 55,
    Specialization = 56,
    GoldLooted = 57,
    XpReceived = 58,

    // ── Trade Skills (59–64) ─────────────────────────────────────────────
    Butchering = 59,
    Scavenging = 60,
    Cultivation = 61,
    Apothecary = 62,
    TalismanMaking = 63,
    Salvaging = 64,

    // ── Stealth & Hate (65–75) ───────────────────────────────────────────
    Stealth = 65,
    StealthDetection = 66,
    HateCaused = 67,
    HateReceived = 68,
    OffhandProcChance = 69,
    OffhandDamage = 70,
    RenownReceived = 71,
    InfluenceReceived = 72,
    DismountChance = 73,
    Gravity = 74,
    LevitationHeight = 75,

    // ── Advanced Combat (76–95) ──────────────────────────────────────────
    MeleeCritRate = 76,
    RangedCritRate = 77,
    MagicCritRate = 78,
    HealthRegen = 79,
    MeleePower = 80,
    RangedPower = 81,
    MagicPower = 82,
    ArmorPenetrationReduction = 83,
    CriticalHitRateReduction = 84,
    BlockStrikethrough = 85,
    ParryStrikethrough = 86,
    EvadeStrikethrough = 87,
    DisruptStrikethrough = 88,
    HealCritRate = 89,
    MaxActionPoints = 90,
    Mastery1Bonus = 91,
    Mastery2Bonus = 92,
    Mastery3Bonus = 93,
    HealingPower = 94,
    InteractTime = 95,

    // ── Extended (100–107) — gap at 96–99 ────────────────────────────────
    OutgoingHealPercent = 100,
    SnareDuration = 101,
    KnockdownDuration = 102,
    IncomingHealPercent = 103,
    IncomingMeleeDamage = 104,
    IncomingRangedDamage = 105,
    IncomingMagicDamage = 106,
    ArmorPenetration = 107,
}

/// <summary>
/// Constants and helpers for <see cref="StatId"/>.
/// </summary>
public static class StatConstants
{
    /// <summary>Total number of array slots required (0 .. 108 inclusive).</summary>
    public const int SlotCount = 109;

    /// <summary>
    /// The highest valid <see cref="StatId"/> value.
    /// Identical to V1's <c>Stats.MaxStatCount</c>.
    /// </summary>
    public const int MaxStatValue = 108;

    /// <summary>
    /// Stats with IDs 1..<see cref="BaseStatBoundary"/> (exclusive) are "base stats" —
    /// the primary/defensive attributes displayed on the character sheet (IDs 1–16).
    /// V1's <c>Stats.BaseStatCount = 21</c> is the sentinel, not the count.
    /// </summary>
    public const int BaseStatBoundary = 21;

    /// <summary>True if the stat is a base/primary stat (IDs 1–16).</summary>
    public static bool IsBaseStat(StatId stat) => (byte)stat >= 1 && (byte)stat <= 16;
}
