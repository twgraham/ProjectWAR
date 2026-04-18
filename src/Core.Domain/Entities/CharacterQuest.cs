namespace Core.Domain.Entities;

public sealed class CharacterQuest
{
    public uint CharacterId { get; set; }
    public ushort QuestID { get; set; }
    public string Objectives { get; set; } = string.Empty;
    public bool Done { get; set; }
}
