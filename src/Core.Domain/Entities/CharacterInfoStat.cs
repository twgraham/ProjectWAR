namespace Core.Domain.Entities;

/// <summary>
/// EF entity mapping to the <c>characterinfo_stats</c> table.
/// One row per (career_line, level, stat_id) triple.
/// </summary>
public class CharacterInfoStat
{
    public short CareerLine { get; set; }
    public short Level { get; set; }
    public short StatId { get; set; }
    public int StatValue { get; set; }
}
