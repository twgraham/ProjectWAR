namespace WorldServerV2.Network.Dtos;

/// <summary>
/// Bitfield encoded in the <c>StaticFlags</c> byte of the
/// <c>F_CREATE_STATIC</c> wire format.
/// The client decomposes this byte into realm identity, selectability, and behavioral flags.
/// </summary>
/// <remarks>
/// <para>
/// V1 writes <c>(byte)Realm</c> for non-neutral game-objects (setting only realm bits 0-1)
/// and <c>(byte)(Unks[0] &amp; 0xFF)</c> for neutral objects (the full DB value including
/// additional flag bits).
/// </para>
/// <list type="table">
///   <listheader><term>Bits</term><description>Meaning</description></listheader>
///   <item><term>1-0</term><description>Realm identity (0 = neutral, 1 = Order, 2 = Destruction, 3 = both).</description></item>
///   <item><term>2</term><description><see cref="Selectable"/> — object is selectable/targetable.</description></item>
///   <item><term>3</term><description>Dead — not tested by client.</description></item>
///   <item><term>4</term><description><see cref="EntityBitFlag3"/> — sets entity bit-array flag 3.</description></item>
///   <item><term>5</term><description><see cref="HasNameSuffix"/> — triggers reading career-line name suffix data after DoorId section.</description></item>
///   <item><term>6</term><description><see cref="EntityBitFlag19"/> — sets entity bit-array flag 19.</description></item>
///   <item><term>7</term><description><see cref="Unk7"/> — purpose unknown.</description></item>
/// </list>
/// </remarks>
[Flags]
public enum StaticFlags : byte
{
    /// <summary>No flags.</summary>
    None = 0,

    /// <summary>Bit 0 — low bit of the 2-bit realm identity field (mask <c>0x03</c>). 1 = Order.</summary>
    RealmBit0 = 1 << 0,

    /// <summary>Bit 1 — high bit of the 2-bit realm identity field (mask <c>0x03</c>). 2 = Destruction.</summary>
    RealmBit1 = 1 << 1,

    /// <summary>
    /// Bit 2 — object is selectable / targetable.
    /// </summary>
    Selectable = 1 << 2,

    // Bit 3 is dead — not tested by the client handler.

    /// <summary>Bit 4 — sets entity bit-array flag 3 (purpose TBD).</summary>
    EntityBitFlag3 = 1 << 4,

    /// <summary>
    /// Bit 5 — indicates that extension data (name-suffix / keep-door metadata) follows
    /// the DoorId section at the end of the packet. V1 never sets this for standard objects.
    /// </summary>
    HasNameSuffix = 1 << 5,

    /// <summary>Bit 6 — sets entity bit-array flag 19 (purpose TBD).</summary>
    EntityBitFlag19 = 1 << 6,

    /// <summary>Bit 7 — purpose unknown.</summary>
    Unk7 = 1 << 7,
}
