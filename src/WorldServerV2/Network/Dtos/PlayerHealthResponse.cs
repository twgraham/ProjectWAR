namespace WorldServerV2.Network.Dtos;

/// <summary>
/// Server response for <c>F_PLAYER_HEALTH</c> (0x05).
/// Sends the player's current and max HP, action points, and morale.
/// </summary>
public class PlayerHealthResponse
{
    /// <summary>Current hit points.</summary>
    public uint Health { get; set; }

    /// <summary>Maximum hit points.</summary>
    public uint MaxHealth { get; set; }

    /// <summary>Current action points.</summary>
    public ushort ActionPoints { get; set; }

    /// <summary>Maximum action points.</summary>
    public ushort MaxActionPoints { get; set; }

    /// <summary>Current morale (blue bar).</summary>
    public ushort Morale { get; set; }

    /// <summary>Maximum morale (hardcoded 3600 = 0x0E10).</summary>
    public ushort MaxMorale { get; set; } = 0x0E10;
}
