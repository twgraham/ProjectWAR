namespace WorldServerV2.Network.Dtos;

/// <summary>
/// Server response for <c>F_MAX_VELOCITY</c> (0x1E).
/// Sends the player's movement speed.
/// </summary>
public class SpeedResponse
{
    /// <summary>Movement speed value (typically 100 = normal speed).</summary>
    public ushort Speed { get; set; } = 100;

    /// <summary>Whether the player can move (1 = yes, 0 = rooted/dead).</summary>
    public byte CanMove { get; set; } = 1;

    /// <summary>Speed modifier percentage (hardcoded 100).</summary>
    public byte SpeedPercent { get; set; } = 100;
}
