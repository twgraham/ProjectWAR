using Core.Infrastructure.Network.Serialization.Attributes;

namespace WorldServerV2.Network.Dtos;

/// <summary>
/// Server response for <c>F_TACTICS</c> (0xF7) — the player's active tactic abilities.
/// <para>
/// Wire format: <c>constant:u8(3) | count:u8 | entries[count × u16]</c>
/// </para>
/// </summary>
public class TacticsResponse
{
    /// <summary>Constant byte (always 3 in V1).</summary>
    public byte Constant { get; set; } = 3;

    /// <summary>Active tactic ability IDs. Length-prefixed by 1 byte (default).</summary>
    public ushort[] Tactics { get; set; } = [];
}
