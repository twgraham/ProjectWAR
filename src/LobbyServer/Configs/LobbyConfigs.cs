namespace LobbyServer;

public class LobbyConfigs
{
    public bool IConfiguredTheFile { get; init; }
    public int ClientPort { get; init; } = 8048;
    public string ClientVersion { get; init; } = "1.4.8";
    public bool SeverOnFinish { get; init; } = true;
}
