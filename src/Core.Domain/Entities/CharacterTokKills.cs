namespace Core.Domain.Entities;

public sealed class CharacterTokKills
{
    public uint CharacterId { get; set; }
    public ushort NPCEntry { get; set; }
    public uint? Count { get; set; }
}
