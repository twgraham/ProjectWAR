namespace WorldServerV2.Data.Entities;

/// <summary>
/// A character's membership in a guild, mapped to the <c>guild_members</c> table.
/// Pure POCO — all DB column mapping is handled by <see cref="CharacterDbContext"/>.
/// </summary>
public sealed class GuildMember
{
    public uint CharacterId { get; set; }
    public uint GuildId { get; set; }
    public byte RankId { get; set; }
    public string PublicNote { get; set; } = string.Empty;
    public string OfficerNote { get; set; } = string.Empty;
    public DateTime JoinDate { get; set; }
    public DateTime LastSeen { get; set; }
    public bool RealmCaptain { get; set; }
    public bool StandardBearer { get; set; }
    public bool GuildRecruiter { get; set; }
    public long RenownContributed { get; set; }
    public byte Tithe { get; set; }
    public long TitheContributed { get; set; }
}
