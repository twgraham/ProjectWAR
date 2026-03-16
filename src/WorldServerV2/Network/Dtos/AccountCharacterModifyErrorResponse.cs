using Core.Infrastructure.Network.Serialization.Attributes;

namespace WorldServerV2.Network.Dtos;

public class AccountCharacterModifyErrorResponse
{
    [CString(24)]
    public string AccountUsername { get; set; }
    
    [CString]
    public string ErrorMessage { get; set; }
}