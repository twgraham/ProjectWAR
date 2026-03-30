using Core.Infrastructure.Network.Serialization.Attributes;

namespace WorldServerV2.Network.Dtos;

/// <summary>
/// Server response for <c>F_BAG_INFO</c> (0x0A) sub-opcode 0x0F — inventory capacity.
/// <para>
/// Tells the client how many backpack/bank slots are available, how many
/// expansion slots are purchasable, and the cost of the next expansion tier.
/// All multi-byte values are little-endian (matching V1's <c>WriteUInt16R/WriteUInt32R</c>).
/// </para>
/// </summary>
public class BagInfoResponse
{
    /// <summary>Sub-opcode (always 0x0F).</summary>
    public byte SubOpcode { get; set; } = 0x0F;

    /// <summary>Total usable backpack slots.</summary>
    [LittleEndian]
    public ushort BackpackSlots { get; set; }

    /// <summary>Slots added by next backpack expansion (or 0 if maxed).</summary>
    [LittleEndian]
    public ushort BackpackExpansionSlots { get; set; }

    /// <summary>Cost of next backpack expansion in copper (or 0 if maxed).</summary>
    [LittleEndian]
    public uint BackpackExpansionCost { get; set; }

    /// <summary>Total usable bank slots.</summary>
    [LittleEndian]
    public ushort BankSlots { get; set; }

    /// <summary>Slots added by next bank expansion (or 0 if maxed).</summary>
    [LittleEndian]
    public ushort BankExpansionSlots { get; set; }

    /// <summary>Cost of next bank expansion in copper (or 0 if maxed).</summary>
    [LittleEndian]
    public uint BankExpansionCost { get; set; }
}
