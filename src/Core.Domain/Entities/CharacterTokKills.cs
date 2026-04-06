namespace Core.Domain.Entities;

/// <summary>
/// A Tome of Knowledge NPC kill counter for a character, mapped to the <c>characters_toks_kills</c> table.
/// Pure POCO — all DB column mapping is handled by <see cref="CharacterDbContext"/>.
/// </summary>
public sealed class CharacterTokKills
{
    public uint CharacterId { get; set; }
    public ushort NPCEntry { get; set; }
    public uint? Count { get; set; }
}
