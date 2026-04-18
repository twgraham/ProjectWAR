namespace Core.Domain.Entities;

public sealed class GmCommandLog
{
    /// <summary>Surrogate UUID primary key.</summary>
    public Guid GmCommandLogsId { get; set; }
    public uint? AccountId { get; set; }
    public string? PlayerName { get; set; }
    public string? Command { get; set; }
    public DateTime? Date { get; set; }
}
