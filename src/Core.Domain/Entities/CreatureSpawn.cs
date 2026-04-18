namespace Core.Domain.Entities;

public sealed class CreatureSpawn
{
    public uint Guid { get; set; }
    public uint Entry { get; set; }
    public ushort ZoneId { get; set; }
    public int WorldX { get; set; }
    public int WorldY { get; set; }
    public int WorldZ { get; set; }
    public int WorldO { get; set; }
    public byte Icone { get; set; }
    public byte Emote { get; set; }
    public ushort RespawnMinutes { get; set; }
    public byte Faction { get; set; }
    public byte? WaypointType { get; set; }
    public byte Level { get; set; }
    public uint Oid { get; set; }
    public byte Enabled { get; set; }

    // Navigation properties

    /// <summary>
    /// Resolved from <see cref="Entry"/> → <see cref="CreatureProto.Entry"/>.
    /// </summary>
    public CreatureProto? Proto { get; set; }
}
