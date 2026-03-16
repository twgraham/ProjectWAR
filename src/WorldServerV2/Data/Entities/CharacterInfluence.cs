namespace WorldServerV2.Data.Entities;

/// <summary>
/// A character's influence progress with a chapter, mapped to the <c>character_influences</c> table.
/// Pure POCO — all DB column mapping is handled by <see cref="CharacterDbContext"/>.
/// </summary>
public sealed class CharacterInfluence
{
    public int CharacterId { get; set; }
    public ushort InfluenceId { get; set; }
    public uint InfluenceCount { get; set; }
    public bool Tier1Itemtaken { get; set; }
    public bool Tier2Itemtaken { get; set; }
    public bool Tier3Itemtaken { get; set; }
}
