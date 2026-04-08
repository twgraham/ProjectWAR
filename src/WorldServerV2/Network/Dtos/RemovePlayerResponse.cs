namespace WorldServerV2.Network.Dtos;

/// <summary>
/// <c>F_REMOVE_PLAYER</c> (0x49) — Tells a client to destroy a previously visible entity.
/// Sent when an entity leaves the observer's visibility range or is removed from the world.
/// </summary>
/// <remarks>
/// Wire layout (4 bytes total):
/// <code>
/// +0x00 Oid (u16)   — the OID of the entity being removed
/// +0x02 Padding (u16) — always 0
/// </code>
/// </remarks>
public sealed class RemovePlayerResponse
{
    /// <summary>OID of the entity to remove from the client's view.</summary>
    public ushort Oid { get; set; }

    /// <summary>Reserved — always 0.</summary>
    public ushort Padding { get; set; }
}
