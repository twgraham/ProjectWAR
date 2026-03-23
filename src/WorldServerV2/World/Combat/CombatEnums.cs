namespace WorldServerV2.World.Combat;

/// <summary>
/// Primary damage element. Determines which resistance stat mitigates the damage
/// and which percentage modifiers apply.
/// </summary>
public enum DamageType : byte
{
    /// <summary>Mitigated by Armor. Modified by MeleePower / IncomingMeleeDamage.</summary>
    Physical = 0,

    /// <summary>Mitigated by Spirit Resistance.</summary>
    Spiritual = 1,

    /// <summary>Mitigated by Elemental Resistance.</summary>
    Elemental = 2,

    /// <summary>Mitigated by Corporeal Resistance.</summary>
    Corporeal = 3,

    /// <summary>Healing — bypasses armor/resistance, uses heal pipeline path.</summary>
    Healing = 4,

    /// <summary>Raw healing — ignores all modifiers.</summary>
    RawHealing = 254,

    /// <summary>Raw damage — ignores armor/resistance/defense.</summary>
    RawDamage = 255,
}

/// <summary>Siege sub-type for special damage categories.</summary>
public enum SubDamageType : byte
{
    None = 0,
    Cleave = 1,
    Artillery = 2,
    Cannon = 3,
    Ram = 4,
    Oil = 5,
}

/// <summary>
/// Which weapon slot(s) contribute DPS to damage calculation.
/// </summary>
public enum WeaponDamageContribution : byte
{
    /// <summary>No weapon contribution (typically proc or precalculated).</summary>
    None = 0,

    /// <summary>Main-hand weapon DPS only.</summary>
    MainHand = 1,

    /// <summary>Off-hand weapon DPS only.</summary>
    OffHand = 2,

    /// <summary>Ranged weapon DPS only.</summary>
    Ranged = 3,

    /// <summary>Main-hand + off-hand DPS combined (dual-wield abilities).</summary>
    DualWield = 4,

    /// <summary>Main-hand + ranged DPS combined (Squig Herder / Engineer).</summary>
    MainAndRanged = 5,
}

/// <summary>
/// Outcome of a defense roll. Only one defense type can succeed per attack.
/// </summary>
public enum DefenseType : byte
{
    /// <summary>No defense — attack landed.</summary>
    None = 0,

    /// <summary>Blocked with shield (requires shield + frontal arc).</summary>
    Block = 1,

    /// <summary>Parried (melee only, frontal).</summary>
    Parry = 2,

    /// <summary>Evaded (ranged attacks).</summary>
    Evade = 3,

    /// <summary>Disrupted (magic attacks).</summary>
    Disrupt = 4,
}
