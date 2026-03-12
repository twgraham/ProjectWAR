namespace WorldServerV2.Config;

public class DatabaseConfig
{
    public required string Host { get; set; }
    public int Port { get; set; } = 5432;
    public required string Database { get; set; }
    public required string Username { get; set; }
    public required string Password { get; set; }
}