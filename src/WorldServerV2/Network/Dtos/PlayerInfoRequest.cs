namespace WorldServerV2.Network.Dtos;

public class PlayerInfoRequest
{
    public ushort Unk1 { get; set; }
    public ushort Oid { get; set; }
    public byte LOSFlag { get; set; }
    public byte TargetType { get; set; }
}