namespace WorldServerV2.World.Items;

/// <summary>
/// Immutable definition of an item set, parsed at startup from the DB <c>item_sets</c> table.
/// Contains pre-parsed item membership and bonus thresholds.
/// </summary>
public sealed class ItemSetDefinition
{
    public required uint Entry { get; init; }
    public required string Name { get; init; }

    /// <summary>Buff level modifier for spell bonuses.</summary>
    public byte BuffLevel { get; init; }

    /// <summary>Items that belong to this set (entry ID + display name).</summary>
    public required ReadOnlyMemory<ItemSetMember> Items { get; init; }

    /// <summary>Bonuses granted at various piece thresholds.</summary>
    public required ReadOnlyMemory<ItemSetBonus> Bonuses { get; init; }
}

/// <summary>
/// A single item in an item set.
/// </summary>
/// <param name="ItemEntry">The item's entry ID.</param>
/// <param name="ItemName">The item's display name (from the set definition).</param>
public readonly record struct ItemSetMember(uint ItemEntry, string ItemName);

/// <summary>
/// A bonus granted when enough pieces of a set are equipped.
/// </summary>
public readonly record struct ItemSetBonus
{
    /// <summary>Number of set pieces required to activate this bonus.</summary>
    public required byte ItemsRequired { get; init; }

    /// <summary>
    /// Bonus type: <see cref="ItemSetBonusType.Stat"/> for a stat bonus,
    /// <see cref="ItemSetBonusType.Spell"/> for a buff/proc.
    /// </summary>
    public required ItemSetBonusType BonusType { get; init; }

    /// <summary>Stat ID (only valid when <see cref="BonusType"/> is <see cref="ItemSetBonusType.Stat"/>).</summary>
    public byte StatId { get; init; }

    /// <summary>Stat value (only valid when <see cref="BonusType"/> is <see cref="ItemSetBonusType.Stat"/>).</summary>
    public ushort StatValue { get; init; }

    /// <summary>Whether this is a percentage-based bonus (only for stat bonuses).</summary>
    public bool IsPercentage { get; init; }

    /// <summary>Spell/buff entry triggered (only valid when <see cref="BonusType"/> is <see cref="ItemSetBonusType.Spell"/>).</summary>
    public ushort SpellId { get; init; }

    /// <summary>
    /// Raw bonus key from the DB, used during packet serialization.
    /// Encodes both the action type and the item threshold.
    /// </summary>
    public byte RawKey { get; init; }
}

/// <summary>
/// Discriminator for <see cref="ItemSetBonus"/>.
/// </summary>
public enum ItemSetBonusType : byte
{
    /// <summary>Grants a flat or percentage stat bonus.</summary>
    Stat = 3,

    /// <summary>Grants a buff/proc spell.</summary>
    Spell = 8,
}
