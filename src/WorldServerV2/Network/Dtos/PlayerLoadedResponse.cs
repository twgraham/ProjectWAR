namespace WorldServerV2.Network.Dtos;

/// <summary>
/// Server response for <c>S_PLAYER_LOADED</c> (0x89).
/// Signals the client that the player data has been fully loaded.
/// Sent after stats and health.
/// </summary>
public class PlayerLoadedResponse
{
    /// <summary>Reserved field (always 0x0000).</summary>
    public ushort Reserved { get; set; }
}
