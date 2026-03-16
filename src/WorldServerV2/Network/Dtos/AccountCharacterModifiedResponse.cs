using Core.Infrastructure.Network;
using Core.Infrastructure.Network.Serialization.Attributes;

namespace WorldServerV2.Network.Dtos;

public class AccountCharacterModifiedResponse
{
    [PascalString]
    public string AccountUsername { get; set; }
}