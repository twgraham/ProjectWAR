using Core.Infrastructure.Network.Serialization.Attributes;

namespace WorldServerV2.Network.Dtos;

/// <summary>
/// Outbound <c>F_PLAYER_STATE2</c> (0x62) relay packet. Sent to nearby players so their
/// client can render another player's movement. The payload mirrors the inbound
/// <see cref="PlayerStateRequest"/> raw data followed by a single <c>0x00</c> terminator
/// byte.
/// <para>
/// Wire layout: <c>[Data (raw bitstream)][0x00 terminator]</c>
/// </para>
/// </summary>
public class PlayerStateRelayResponse
{
    /// <summary>
    /// The raw inbound packet payload plus a trailing <c>0x00</c> terminator byte.
    /// Written verbatim to the wire — the server does not reinterpret the bitstream.
    /// </summary>
    [RawBytes]
    public required byte[] Data { get; set; }

    /// <summary>
    /// Builds a relay response from an inbound <see cref="PlayerStateRequest"/>.
    /// Copies the raw payload and appends the <c>0x00</c> terminator.
    /// </summary>
    public static PlayerStateRelayResponse FromRequest(PlayerStateRequest request)
    {
        var data = new byte[request.Data.Length + 1];
        request.Data.CopyTo(data, 0);
        // data[^1] is already 0x00 from array initialization

        return new PlayerStateRelayResponse
        {
            Data = data
        };
    }
}
