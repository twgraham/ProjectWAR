using Core.Infrastructure.Network.Serialization.Attributes;

namespace WorldServerV2.Network.Dtos;

/// <summary>
/// Client request for <c>F_REQUEST_WORLD_LARGE</c> (0x40).
/// Sent by the client after it has processed the init-complete packet,
/// requesting the server to finalize world loading (time + world-sent signal).
/// </summary>
public class RequestWorldLargeRequest
{
    /// <summary>Payload bytes (variable length, content not used by the server).</summary>
    [RawBytes]
    public required byte[] Data { get; set; }
}
