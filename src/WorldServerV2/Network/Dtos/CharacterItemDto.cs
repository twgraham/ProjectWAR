using Core.Infrastructure.Network;
using Core.Infrastructure.Network.Serialization.Attributes;

namespace WorldServerV2.Network.Dtos;

public class CharacterItemDto
{
    [LittleEndian]
    public uint ModelId { get; set; }
    
    [LittleEndian]
    public ushort PrimaryDye { get; set; }
    
    [LittleEndian]
    public ushort SecondaryDye { get; set; }
}