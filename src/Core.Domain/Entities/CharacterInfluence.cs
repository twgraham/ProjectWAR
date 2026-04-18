namespace Core.Domain.Entities;

public sealed class CharacterInfluence
{
    public int CharacterId { get; set; }
    public ushort InfluenceId { get; set; }
    public uint InfluenceCount { get; set; }
    public bool Tier1Itemtaken { get; set; }
    public bool Tier2Itemtaken { get; set; }
    public bool Tier3Itemtaken { get; set; }
}
