using Core.Infrastructure.Network.Serialization.Attributes;

namespace WorldServerV2.Network.Dtos;

public class InitializePlayerRequest
{
    public ushort Unk1 { get; set; }
    public ushort Unk2 { get; set; }
    public ushort Unk3 { get; set; }
    public ushort Unk4 { get; set; }
    public ushort Unk5 { get; set; }
    public ushort Unk6 { get; set; }
    public ushort Unk7 { get; set; }
    public byte Unk8 { get; set; }
    
    [FixedLength(5)]
    public required byte[] Unk9 { get; set; }
}