namespace Core.Domain.Entities;

/// <summary>
/// A persistent buff saved for a character across sessions, mapped to the <c>character_saved_buffs</c> table.
/// Pure POCO — all DB column mapping is handled by <see cref="CharacterDbContext"/>.
/// </summary>
public sealed class CharacterSavedBuff
{
    public uint CharacterId { get; set; }
    public ushort BuffId { get; set; }
    public byte? Level { get; set; }
    public byte? StackLevel { get; set; }
    public uint? EndTimeSeconds { get; set; }
}
