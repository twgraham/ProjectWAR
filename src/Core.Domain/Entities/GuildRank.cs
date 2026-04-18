namespace Core.Domain.Entities;

public sealed class GuildRank
{
    public uint GuildId { get; set; }
    public byte RankId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Permissions { get; set; } = string.Empty;
    public bool Enabled { get; set; }
}
