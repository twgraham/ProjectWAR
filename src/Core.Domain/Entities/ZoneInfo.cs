namespace Core.Domain.Entities;

public sealed class ZoneInfo
{
    public ushort ZoneId { get; set; }
    public string Name { get; set; } = string.Empty;
    public byte MinLevel { get; set; }
    public byte MaxLevel { get; set; }
    public int Type { get; set; }
    public int Tier { get; set; }
    public byte Pairing { get; set; }
    public ushort Price { get; set; }
    public ushort Region { get; set; }
    public int OffX { get; set; }
    public int OffY { get; set; }
    public ushort Collision { get; set; }
    public ushort Illegal { get; set; }
}
