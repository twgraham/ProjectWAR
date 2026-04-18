namespace Core.Domain.Entities;

public sealed class CharacterSavedBuff
{
    public uint CharacterId { get; set; }
    public ushort BuffId { get; set; }
    public byte? Level { get; set; }
    public byte? StackLevel { get; set; }
    public uint? EndTimeSeconds { get; set; }
}
