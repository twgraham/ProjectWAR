namespace WorldServerV2.Data.Entities;

/// <summary>
/// Zone travel (jump) point loaded from the <c>zone_jumps</c> table.
/// Pure POCO — all DB column mapping is handled by <see cref="WorldDbContext"/>.
/// </summary>
public sealed class ZoneJump
{
    public uint Entry { get; set; }
    public ushort ZoneId { get; set; }
    public uint WorldX { get; set; }
    public uint WorldY { get; set; }
    public ushort WorldZ { get; set; }
    public ushort WorldO { get; set; }
    public byte Type { get; set; }
    public ushort Enabled { get; set; }
    public ushort? InstanceId { get; set; }
}
