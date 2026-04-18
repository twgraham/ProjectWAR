namespace Core.Domain.Entities;

public sealed class CharacterTok
{
    public uint CharacterId { get; set; }
    public ushort TokEntry { get; set; }
    public uint? Count { get; set; }
}
