using Core.Infrastructure.Network.Serialization.Attributes;

namespace WorldServerV2.Network.Dtos;

public class InterfaceCommandRequest
{
    public byte Command { get; set; }
    
    [RawBytes]
    public byte[] Data { get; set; }
}