using Core.Infrastructure.Network.Serialization.Attributes;

namespace WorldServerV2.Network.Dtos;

/// <summary>
/// Server response for <c>F_PLAYER_INIT_COMPLETE</c> (0xEF).
/// Final packet in the init sequence — tells the client all init data has been sent.
/// </summary>
public class PlayerInitCompleteResponse
{
    /// <summary>Player's runtime OID (little-endian per old protocol).</summary>
    [LittleEndian]
    public ushort Oid { get; set; }
}
