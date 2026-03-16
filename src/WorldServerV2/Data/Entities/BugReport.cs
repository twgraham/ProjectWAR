namespace WorldServerV2.Data.Entities;

/// <summary>
/// A player-submitted bug report, mapped to the <c>bug_report</c> table.
/// Pure POCO — all DB column mapping is handled by <see cref="CharacterDbContext"/>.
/// </summary>
public sealed class BugReport
{
    /// <summary>Surrogate UUID primary key.</summary>
    public Guid BugReportId { get; set; }
    public uint AccountId { get; set; }
    public uint CharacterId { get; set; }
    public ushort ZoneId { get; set; }
    public ushort X { get; set; }
    public ushort Y { get; set; }
    public DateTime Time { get; set; }
    public byte Type { get; set; }
    public byte Category { get; set; }
    public string Message { get; set; } = string.Empty;
    public string ReportType { get; set; } = string.Empty;
    public string FieldSting { get; set; } = string.Empty;
    public string? Assigned { get; set; }
}
