using Core.Infrastructure.Network.Serialization.Attributes;

namespace WorldServerV2.Network.Dtos;

/// <summary>
/// Server response for <c>F_MORALE_LIST</c> (0x8C) — the 4 morale ability slots.
/// <para>
/// Wire format: <c>slot1:u16 | slot2:u16 | slot3:u16 | slot4:u16 | padding[3]</c>
/// A slot value of 0 means empty.
/// </para>
/// </summary>
public class MoraleListResponse
{
    /// <summary>Morale slot 1 ability ID (0 = empty).</summary>
    public ushort Slot1 { get; set; }

    /// <summary>Morale slot 2 ability ID (0 = empty).</summary>
    public ushort Slot2 { get; set; }

    /// <summary>Morale slot 3 ability ID (0 = empty).</summary>
    public ushort Slot3 { get; set; }

    /// <summary>Morale slot 4 ability ID (0 = empty).</summary>
    public ushort Slot4 { get; set; }

    /// <summary>3 trailing zero bytes.</summary>
    [FixedLength(3)]
    public byte[] Padding { get; set; } = new byte[3];
}
