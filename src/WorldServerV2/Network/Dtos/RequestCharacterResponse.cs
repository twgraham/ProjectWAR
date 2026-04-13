using Core.Domain.Entities;
using Core.Infrastructure.Network.Serialization.Attributes;
using Core.Session;

namespace WorldServerV2.Network.Dtos;

public class RequestCharacterResponse
{
    [CString(20)]
    public string AccountUsername { get; set; } = string.Empty;
    public uint RemainingLockoutTime { get; set; } = 0;
    public byte Unk1 { get; set; }
    public byte Unk2 { get; set; } // Realm type?
    public byte MaxCharacterSlots { get; set; } = 20; // Careful changing this, uncertain whether other values supported
    public byte GameplayRulesetType { get; set; } = 0;
    public byte LastSwitchedToRealm { get; set; } = 0;
    public byte NumberOfPaidChangesAvailable { get; set; } = 0;
    public byte Unk3 { get; set; } = 0;
    public byte Unk4 { get; set; } = 0;
    
    [FixedLength(20)]
    public CharacterDto[] Characters { get; set; } = [];

    public RequestCharacterResponse()
    {
    }

    public RequestCharacterResponse(AccountInfo accountInfo, List<Character> characters)
    {
        ArgumentNullException.ThrowIfNull(accountInfo);
        
        AccountUsername = accountInfo.Username;
        Characters = Enumerable.Repeat(0, 20)
            .Select((_, idx) => characters.Count > idx
                ? new CharacterDto(characters[idx])
                : new CharacterDto()).ToArray();
    }
}