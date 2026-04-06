namespace Core.GameWorld.Entities;

/// <summary>
/// Describes how a player's session ended.
/// </summary>
public enum DisconnectType
{
    /// <summary>Connection dropped unexpectedly (timeout, crash, network failure).</summary>
    Unclean,

    /// <summary>Player logged out gracefully via the UI.</summary>
    Clean,

    /// <summary>Client crash detected.</summary>
    Crash,
}
