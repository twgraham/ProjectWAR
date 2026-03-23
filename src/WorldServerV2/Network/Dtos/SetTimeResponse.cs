namespace WorldServerV2.Network.Dtos;

/// <summary>
/// Server response for <c>F_SET_TIME</c> (0xD6).
/// Informs the client of the current in-game time. Sent in response to
/// <c>F_REQUEST_WORLD_LARGE</c>.
/// </summary>
public class SetTimeResponse
{
    /// <summary>Game time expressed as UTC seconds-of-day divided by 65.5.</summary>
    public ushort GameTime { get; set; }

    /// <summary>Padding (0x0000).</summary>
    public ushort Padding { get; set; }

    /// <summary>Game-seconds per real-second (hardcoded 1).</summary>
    public uint GameSecondsPerSecond { get; set; } = 1;
}
