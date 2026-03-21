namespace WorldServerV2.Network.Dtos;

/// <summary>
/// Server response for <c>S_WORLD_SENT</c> (0x83).
/// The final signal that the world data has been transmitted and the client
/// can begin rendering. Sent immediately after <see cref="SetTimeResponse"/>.
/// </summary>
public class WorldSentResponse
{
    /// <summary>Reserved byte (always 0x00).</summary>
    public byte Reserved { get; set; }
}
