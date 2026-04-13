using Core.Infrastructure.Network.Serialization.Attributes;

namespace WorldServerV2.Network.Dtos;

public class ObjectDeathResponse
{
    public ushort ObjectId { get; set; }
    public byte Unk1 { get; set; } = 1;
    public byte Unk2 { get; set; } = 0;
    public ushort KillerId { get; set; }
    
    [FixedLength(6)]
    public byte[] Padding { get; set; } = new byte[6];
}