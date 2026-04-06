namespace Core.Domain.Entities;

/// <summary>
/// A rank definition within a guild, mapped to the <c>guild_ranks</c> table.
/// Pure POCO — all DB column mapping is handled by <see cref="CharacterDbContext"/>.
/// </summary>
public sealed class GuildRank
{
    public uint GuildId { get; set; }
    public byte RankId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Permissions { get; set; } = string.Empty;
    public bool Enabled { get; set; }
}
