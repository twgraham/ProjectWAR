namespace Core.Domain.Entities;

public sealed class CharacterSocial
{
    public uint CharacterId { get; set; }
    public uint DistCharacterId { get; set; }
    public string DistName { get; set; } = string.Empty;
    public bool Friend { get; set; }
    public bool Ignore { get; set; }
}
