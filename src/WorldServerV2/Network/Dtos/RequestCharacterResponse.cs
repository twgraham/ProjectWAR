namespace WorldServerV2.Network.Dtos;

public class RequestCharacterResponse
{
    public string AccountUsername { get; set; } = string.Empty;
    public uint RemainingLockoutTime { get; set; } = 0;
    public byte Unk1 { get; set; }
    public byte Unk2 { get; set; } // Realm type?
    public byte MaxCharacterSlots { get; set; } = 20; // Careful changing this, uncertain whether other values supported
    public byte GameplayRulesetType { get; set; } = 0;
    public byte LastSwitchedToRealm { get; set; } = 0;
    public byte NumberOfPaidChangesAvailable { get; set; } = 0;
    public byte Unk3 { get; set; }
    
}