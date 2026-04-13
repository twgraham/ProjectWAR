namespace Core.Domain.Entities;

/// <summary>
/// An audit log entry for a GM command, mapped to the <c>gmcommandlogs</c> table.
/// Pure POCO — all DB column mapping is handled by <see cref="CharacterDbContext"/>.
/// </summary>
public sealed class GmCommandLog
{
    /// <summary>Surrogate UUID primary key.</summary>
    public Guid GmCommandLogsId { get; set; }
    public uint? AccountId { get; set; }
    public string? PlayerName { get; set; }
    public string? Command { get; set; }
    public DateTime? Date { get; set; }
}
