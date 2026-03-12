namespace WorldServerV2.Network.Dtos;

/// <summary>
/// Client request for <c>F_DUMP_ARENAS_LARGE</c> (0x35).
/// Sent when the player selects a character on the character screen.
/// </summary>
public class DumpArenasLargeRequest
{
    public byte CharacterSlot { get; set; }
}
