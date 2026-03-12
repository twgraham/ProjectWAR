namespace WorldServerV2.Data.Entities;

/// <summary>
/// A loot bag pool entry for a character, mapped to the <c>character_bag_pools</c> table.
/// Pure POCO — all DB column mapping is handled by <see cref="CharacterDbContext"/>.
/// </summary>
public sealed class CharacterBagPool
{
    public int CharacterId { get; set; }
    public int BagType { get; set; }
    public int BagPoolValue { get; set; }
}
