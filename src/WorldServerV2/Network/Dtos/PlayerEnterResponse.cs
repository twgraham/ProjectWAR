using Core.Infrastructure.Network;
using Core.Infrastructure.Network.Serialization.Attributes;

namespace WorldServerV2.Network.Dtos;

public class PlayerEnterResponse
{
    [LittleEndian]
    public ushort SessionId { get; set; }
}