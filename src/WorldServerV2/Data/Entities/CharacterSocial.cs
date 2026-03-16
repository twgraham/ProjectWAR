namespace WorldServerV2.Data.Entities;

/// <summary>
/// A friend or ignore-list entry for a character, mapped to the <c>characters_socials</c> table.
/// Pure POCO — all DB column mapping is handled by <see cref="CharacterDbContext"/>.
/// </summary>
public sealed class CharacterSocial
{
    public uint CharacterId { get; set; }
    public uint DistCharacterId { get; set; }
    public string DistName { get; set; } = string.Empty;
    public bool Friend { get; set; }
    public bool Ignore { get; set; }
}
