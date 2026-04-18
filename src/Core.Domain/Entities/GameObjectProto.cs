namespace Core.Domain.Entities;

public sealed class GameObjectProto
{
    public long Entry { get; set; }
    public string Name { get; set; } = string.Empty;
    public int DisplayId { get; set; }
    public int Scale { get; set; }
    public short Level { get; set; }
    public short Faction { get; set; }
    public long HealthPoints { get; set; }
}
