namespace WorldServerV2.Data.Entities;

/// <summary>
/// An alliance between guilds, mapped to the <c>guild_alliance_info</c> table.
/// Pure POCO — all DB column mapping is handled by <see cref="CharacterDbContext"/>.
/// </summary>
public sealed class GuildAllianceInfo
{
    public uint AllianceId { get; set; }
    public string Name { get; set; } = string.Empty;
}
