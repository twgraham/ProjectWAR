namespace WorldServerV2.Data.Entities;

/// <summary>
/// An item owned by a character, mapped to the <c>characters_items</c> table.
/// Pure POCO — all DB column mapping is handled by <see cref="CharacterDbContext"/>.
/// </summary>
public sealed class CharacterItem
{
    /// <summary>Surrogate UUID primary key.</summary>
    public Guid CharactersItemsId { get; set; }
    public long Guid { get; set; }
    public uint CharacterId { get; set; }
    public uint Entry { get; set; }
    public ushort SlotId { get; set; }
    public uint ModelId { get; set; }
    public ushort Counts { get; set; }
    public string? Talismans { get; set; }
    public ushort PrimaryDye { get; set; }
    public ushort SecondaryDye { get; set; }
    public bool BoundtoPlayer { get; set; }
    public uint AlternateAppereanceEntry { get; set; }
}
