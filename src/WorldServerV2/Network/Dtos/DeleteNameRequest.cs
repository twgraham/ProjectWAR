using Core.Infrastructure.Network;
using Core.Infrastructure.Network.Serialization.Attributes;

namespace WorldServerV2.Network.Dtos;

public class DeleteNameRequest
{
    [CString(30)]
    public string CharacterName { get; set; }
    [CString(20)]
    public string AccountUsername { get; set; }
}