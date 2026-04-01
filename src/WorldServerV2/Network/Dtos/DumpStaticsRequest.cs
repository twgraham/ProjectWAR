namespace WorldServerV2.Network.Dtos;

/// <summary>
/// Client request for <c>F_DUMP_STATICS</c> (0x0D).
/// Sent by the client after it has loaded the terrain and static objects.
/// This signals that the client is ready to receive entity-create packets
/// and participate in the game world.
/// </summary>
public class DumpStaticsRequest
{
    public uint Unk1 { get; set; }
    public ushort Unk2 { get; set; }
    public ushort OffsetX { get; set; }
    public ushort Unk3 { get; set; }
    public ushort OffsetY { get; set; }
}
