namespace Core.Domain.Entities;

/// <summary>
/// A Tome of Knowledge entry for a character, mapped to the <c>characters_toks</c> table.
/// Pure POCO — all DB column mapping is handled by <see cref="CharacterDbContext"/>.
/// </summary>
public sealed class CharacterTok
{
    public uint CharacterId { get; set; }
    public ushort TokEntry { get; set; }
    public uint? Count { get; set; }
}
