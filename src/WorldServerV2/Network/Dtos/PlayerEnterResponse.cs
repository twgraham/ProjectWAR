using Core.Infrastructure.Network;

namespace WorldServerV2.Network.Dtos;

public class PlayerEnterResponse
{
    [LittleEndian]
    public ushort SessionId { get; set; }
}