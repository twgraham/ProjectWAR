using Core.Domain.ValueObjects;

namespace Core.Domain.Entities;

public class ClassInfo
{
    public uint Id { get; set; }
    public Class ClassId { get; set; }
    public string ClassName { get; set; } = string.Empty;
    public Faction Faction { get; set; }
    public ushort Region { get; set; }
    public ushort ZoneId { get; set; }
    public int WorldX { get; set; }
    public int WorldY { get; set; }
    public int WorldZ { get; set; }
    public int WorldO { get; set; }
    public ushort RallyPt { get; set; }
    public uint Skills { get; set; }
}