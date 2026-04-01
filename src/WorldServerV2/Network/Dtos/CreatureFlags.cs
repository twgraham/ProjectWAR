namespace WorldServerV2.Network.Dtos;

/// <summary>
/// Bitfield encoded in the <c>Faction</c> byte on the <c>F_CREATE_MONSTER</c> wire format.
/// The client decomposes this byte into realm, creature sub-type, and behavioral flags.
/// </summary>
/// <remarks>
/// <list type="table">
///   <listheader><term>Bits</term><description>Meaning</description></listheader>
///   <item><term>7–6</term><description>Realm identity (0 = neutral, 1 = Order, 2 = Destruction).</description></item>
///   <item><term>5</term><description><see cref="Stationary"/> — creature is immobile.</description></item>
///   <item><term>4</term><description><see cref="HasExtendedStreamData"/> — extra u32 read from stream after Name.</description></item>
///   <item><term>3</term><description>Unknown client flag.</description></item>
///   <item><term>2–1</term><description>Creature sub-type (2-bit value, mask <c>0x06</c>).</description></item>
///   <item><term>0</term><description>Unknown client flag.</description></item>
/// </list>
/// </remarks>
[Flags]
public enum CreatureFlags : byte
{
    /// <summary>No flags.</summary>
    None = 0,

    /// <summary>Bit 0 — unknown client-side boolean flag.</summary>
    Unk0 = 1 << 0,

    /// <summary>Bit 1 — low bit of the 2-bit creature sub-type field (mask <c>0x06</c>).</summary>
    SubTypeBit0 = 1 << 1,

    /// <summary>Bit 2 — high bit of the 2-bit creature sub-type field (mask <c>0x06</c>).</summary>
    SubTypeBit1 = 1 << 2,

    /// <summary>Bit 3 — unknown client-side boolean flag.</summary>
    Unk3 = 1 << 3,

    /// <summary>
    /// Bit 4 — when set, the client reads an additional <c>u32</c> from the stream
    /// in the post-Name section (stored at creature+0x39C).
    /// </summary>
    HasExtendedStreamData = 1 << 4,

    /// <summary>
    /// Bit 5 — creature is stationary (client disables movement processing).
    /// </summary>
    Stationary = 1 << 5,

    /// <summary>Bit 6 — low bit of the 2-bit realm identity field (mask <c>0xC0</c>).</summary>
    RealmBit0 = 1 << 6,

    /// <summary>Bit 7 — high bit of the 2-bit realm identity field (mask <c>0xC0</c>).</summary>
    RealmBit1 = 1 << 7,

    // ── Convenience aliases ──

    /// <summary>Realm = Order (bits 7–6 = <c>01</c>).</summary>
    RealmOrder = RealmBit0,

    /// <summary>Realm = Destruction (bits 7–6 = <c>10</c>).</summary>
    RealmDestruction = RealmBit1,
}
