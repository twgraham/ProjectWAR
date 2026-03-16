using Core.Infrastructure.Network.Serialization.Attributes;

namespace WorldServerV2.Network.Dtos;

/// <summary>
/// Server response for <c>F_WORLD_ENTER</c> (0x19).
/// Sent after the player entity is created during character selection.
/// </summary>
public class WorldEnterResponse
{
    /// <summary>Protocol header — always 0x0608.</summary>
    public ushort Header { get; set; } = 0x0608;

    /// <summary>20 bytes of padding (reserved / unknown).</summary>
    [FixedLength(20)]
    public byte[] Padding { get; set; } = new byte[20];

    /// <summary>First port string ("38699").</summary>
    [CString(5)]
    public string Port1 { get; set; } = "38699";

    /// <summary>Second port string ("38700").</summary>
    [CString(5)]
    public string Port2 { get; set; } = "38700";

    /// <summary>IP address string.</summary>
    [CString(20)]
    public string IpAddress { get; set; } = "0.0.0.0";
}
