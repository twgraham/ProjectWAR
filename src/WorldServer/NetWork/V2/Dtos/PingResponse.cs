namespace WorldServer.NetWork.V2.Dtos;

public class PingResponse
{
    public uint ClientTimestamp { get; set; }
    public ulong Timestamp { get; set; }
    public uint Sequence { get; set; }
    public uint Unk1 { get; set; }
}