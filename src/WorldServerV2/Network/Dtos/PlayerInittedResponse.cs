using Core.Infrastructure.Network.Serialization.Attributes;

namespace WorldServerV2.Network.Dtos;

/// <summary>
/// Server response for <c>S_PLAYER_INITTED</c> (0x88).
/// Tells the client the player's identity, position, realm, and career.
/// Sent first in the init sequence after the player entity enters the region.
/// </summary>
public class PlayerInittedResponse
{
    /// <summary>Runtime object ID assigned by the region.</summary>
    public ushort Oid { get; set; }

    /// <summary>Padding (0x0000).</summary>
    public ushort Padding1 { get; set; }

    /// <summary>Persistent character ID.</summary>
    public uint CharacterId { get; set; }

    /// <summary>World Z coordinate.</summary>
    public ushort WorldZ { get; set; }

    /// <summary>Padding (0x0000).</summary>
    public ushort Padding2 { get; set; }

    /// <summary>World X coordinate.</summary>
    public uint WorldX { get; set; }

    /// <summary>World Y coordinate.</summary>
    public uint WorldY { get; set; }

    /// <summary>Facing direction (orientation).</summary>
    public ushort WorldO { get; set; }

    /// <summary>Reserved byte (0x00).</summary>
    public byte Reserved1 { get; set; }

    /// <summary>Player realm (1 = Order, 2 = Destruction).</summary>
    public byte Realm { get; set; }

    /// <summary>Instancing X offset (0 for open world).</summary>
    public ushort XOffset { get; set; }

    /// <summary>Instancing Y offset (0 for open world).</summary>
    public ushort YOffset { get; set; }

    /// <summary>Region identifier.</summary>
    public ushort RegionId { get; set; }

    /// <summary>Instance identifier (1 for default).</summary>
    public ushort InstanceId { get; set; } = 1;

    /// <summary>Reserved byte (0x00).</summary>
    public byte Reserved2 { get; set; }

    /// <summary>Career archetype identifier.</summary>
    public byte Career { get; set; }

    /// <summary>6 bytes of trailing zero padding.</summary>
    [FixedLength(6)]
    public byte[] Padding3 { get; set; } = new byte[6];

    /// <summary>Realm/server name (e.g. "Karak-Norn"). Pascal-encoded (1-byte length prefix).</summary>
    [PascalString]
    public string RealmName { get; set; } = string.Empty;

    /// <summary>3 bytes of trailing zero padding.</summary>
    [FixedLength(3)]
    public byte[] Padding4 { get; set; } = new byte[3];
}
