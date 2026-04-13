namespace WorldServerV2.Network.Dtos;

/// <summary>
/// Server response for <c>F_SET_TARGET</c> (0x5E).
/// Acknowledges a target change to the client, causing the UI to update the
/// target frame and nameplate highlight.
/// </summary>
/// <remarks>
/// Wire format (6 bytes):
/// <code>
/// UInt16  TargetOid      — OID of the entity being targeted (0 = clear)
/// UInt16  PlayerOid      — OID of the player who is targeting
/// Byte    SwitchType     — 0 = friendly/cycle-threat, 1 = enemy/cycle-target
/// Byte    Padding        — always 0
/// </code>
/// </remarks>
public class SetTargetResponse
{
    /// <summary>OID of the entity being targeted. 0 = target cleared.</summary>
    public ushort TargetOid { get; set; }

    /// <summary>OID of the player performing the targeting.</summary>
    public ushort PlayerOid { get; set; }

    /// <summary>
    /// Target switch type:
    /// <list type="bullet">
    ///   <item><c>0</c> — friendly target (ally/self)</item>
    ///   <item><c>1</c> — enemy target</item>
    /// </list>
    /// </summary>
    public byte SwitchType { get; set; }

    /// <summary>Padding byte — always 0.</summary>
    public byte Padding { get; set; }
}
