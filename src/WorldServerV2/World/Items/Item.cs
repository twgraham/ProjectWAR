namespace WorldServerV2.World.Items;

/// <summary>
/// A talisman socketed into an item's talisman slot.
/// Mutable at runtime (can be fused, timer can tick).
/// </summary>
public sealed class Talisman
{
    /// <summary>The talisman item's entry ID.</summary>
    public uint Entry { get; set; }

    /// <summary>Reference to the talisman's item definition (for stats, name, model, effects).</summary>
    public ItemDefinition? Info { get; set; }

    /// <summary>Whether the talisman has been permanently fused (cannot be removed).</summary>
    public byte Fused { get; set; }

    /// <summary>Remaining timer in seconds (for temporary/expiring talismans). 0 = permanent.</summary>
    public uint Timer { get; set; }
}

/// <summary>
/// A live item instance in a player's inventory, equipment slot, or bank.
/// Holds per-instance mutable state; static item data is in <see cref="Info"/>.
/// </summary>
public sealed class Item
{
    /// <summary>The item's definition entry ID.</summary>
    public uint Entry { get; set; }

    /// <summary>Reference to the shared, immutable item definition.</summary>
    public required ItemDefinition Info { get; init; }

    /// <summary>Current slot index in the player's inventory layout.</summary>
    public ushort SlotId { get; set; }

    /// <summary>Visual model override (0 = use definition's ModelId).</summary>
    public uint ModelId { get; set; }

    /// <summary>Stack count (min 1 for non-stackable items).</summary>
    public ushort Count { get; set; } = 1;

    /// <summary>Primary dye color applied to this item instance.</summary>
    public ushort PrimaryDye { get; set; }

    /// <summary>Secondary dye color applied to this item instance.</summary>
    public ushort SecondaryDye { get; set; }

    /// <summary>Whether this item is bound to the player (for BoE items that have been equipped).</summary>
    public bool BoundToPlayer { get; set; }

    /// <summary>Alternate appearance entry (trophy display, etc.). 0 = none.</summary>
    public uint AlternateAppearanceEntry { get; set; }

    /// <summary>
    /// Socketed talismans. Length matches <see cref="ItemDefinition.TalismanSlots"/>.
    /// Null entries = empty talisman slot.
    /// </summary>
    public Talisman?[] Talismans { get; set; } = [];

    /// <summary>
    /// The effective model ID for display:
    /// uses <see cref="ModelId"/> if non-zero, otherwise falls back to the definition.
    /// </summary>
    public uint EffectiveModelId => ModelId != 0 ? ModelId : Info.ModelId;
}
