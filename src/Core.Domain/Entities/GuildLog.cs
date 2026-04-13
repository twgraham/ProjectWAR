namespace Core.Domain.Entities;

/// <summary>
/// An audit entry in a guild's activity log, mapped to the <c>guild_logs</c> table.
/// Pure POCO — all DB column mapping is handled by <see cref="CharacterDbContext"/>.
/// </summary>
public sealed class GuildLog
{
    /// <summary>Surrogate UUID primary key.</summary>
    public Guid GuildLogsId { get; set; }
    public uint GuildId { get; set; }
    public DateTime Time { get; set; }
    public byte Type { get; set; }
    public string Text { get; set; } = string.Empty;
}
