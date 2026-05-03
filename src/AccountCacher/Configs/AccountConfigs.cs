namespace AccountCacher;

public class AccountConfig
{
    public bool IConfiguredTheFile { get; set; }
    public DatabaseConfig AccountDB { get; set; } = new();
    public bool EnableCache { get; set; } = true;
    public int MaxCacheSize { get; set; } = 10000;
}

public class DatabaseConfig
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5432;
    public string Database { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
