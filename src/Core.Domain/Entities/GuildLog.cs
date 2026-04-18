namespace Core.Domain.Entities;

public sealed class GuildLog
{
    /// <summary>Surrogate UUID primary key.</summary>
    public Guid GuildLogsId { get; set; }
    public uint GuildId { get; set; }
    public DateTime Time { get; set; }
    public byte Type { get; set; }
    public string Text { get; set; } = string.Empty;
}
