using Core.Infrastructure.Network;
using Core.Infrastructure.Network.Serialization.Attributes;

namespace WorldServerV2.Network.Dtos;

public class PlayerEnterRequest
{
    public ushort SID { get; set; }
    public byte Unk1 { get; set; }
    public byte ServerID { get; set; }
    [CString(24)]
    public string CharacterName { get; set; } = string.Empty;
    public ushort Unk2 { get; set; }
    [CString(2)]
    public string Language { get; set; }
    public uint Unk3 { get; set; }
    public byte CharacterSlot { get; set; }
}

