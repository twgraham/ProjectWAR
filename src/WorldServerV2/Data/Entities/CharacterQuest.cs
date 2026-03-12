namespace WorldServerV2.Data.Entities;

/// <summary>
/// A character's active or completed quest, mapped to the <c>characters_quests</c> table.
/// Pure POCO — all DB column mapping is handled by <see cref="CharacterDbContext"/>.
/// </summary>
public sealed class CharacterQuest
{
    public uint CharacterId { get; set; }
    public ushort QuestID { get; set; }
    public string Objectives { get; set; } = string.Empty;
    public bool Done { get; set; }
}
