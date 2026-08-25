namespace WorldServerV2.Network.Dtos;

/// <summary>
/// <c>F_UPDATE_STATE</c> (0xE4), opcode 0x1B — Releases the caster's animation pose
/// after a cast completes or is forcibly interrupted.
/// <para>
/// This is the <em>special-case</em> 10-byte variant of F_UPDATE_STATE that V1 sends
/// via <c>SetCastCompleted</c>. It does NOT follow the generic 11-byte
/// <c>SendUpdateState</c> layout; use <see cref="UpdateStateResponse"/> for all other
/// state opcodes.
/// </para>
/// <para>
/// Wire layout (10 bytes):
/// <c>OID(u16) | 0x1B(u8) | 0x0000(u16) | 0x00(u8) | abilityEntry(u16) | 0x00 0x00(u8 u8)</c>
/// </para>
/// </summary>
public class CastCompletionResponse
{
    /// <summary>OID of the entity whose cast has completed or been cancelled.</summary>
    public ushort ObjectId { get; set; }

    /// <summary>Always 0x1B.</summary>
    public byte StateOpcode { get; set; }

    /// <summary>Reserved — always 0x0000.</summary>
    public ushort Reserved1 { get; set; }

    /// <summary>Reserved — always 0x00.</summary>
    public byte Reserved2 { get; set; }

    /// <summary>The ability that completed or was interrupted.</summary>
    public ushort AbilityEntry { get; set; }

    /// <summary>Trailing padding — always 0.</summary>
    public byte Trailing1 { get; set; }

    /// <summary>Trailing padding — always 0.</summary>
    public byte Trailing2 { get; set; }

    // ═══════════════════════════════════════════════════════════════════
    //  FACTORY
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Creates a cast-completion release packet (V1: <c>SetCastCompleted</c>).
    /// </summary>
    public static CastCompletionResponse Create(ushort objectId, ushort abilityEntry)
        => new() { ObjectId = objectId, StateOpcode = 0x1B, AbilityEntry = abilityEntry };
}
