using Core.Infrastructure.Network;
using Core.Infrastructure.Network.Serialization.Attributes;

namespace WorldServerV2.Network.Dtos;

public class CheckNameResponse
{
    [CString(30)]
    public required string CharacterName { get; set; }
    [CString(20)]
    public required string AccountUsername { get; set; }
    public bool Invalid { get; set; }
    public byte Unk1 { get; set; }
    public byte Unk2 { get; set; }
    public byte Unk3 { get; set; }
}
