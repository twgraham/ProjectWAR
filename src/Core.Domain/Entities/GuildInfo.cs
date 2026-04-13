namespace Core.Domain.Entities;

/// <summary>
/// Core guild record, mapped to the <c>guild_info</c> table.
/// Pure POCO — all DB column mapping is handled by <see cref="CharacterDbContext"/>.
/// </summary>
public sealed class GuildInfo
{
    public uint GuildId { get; set; }
    public string Name { get; set; } = string.Empty;
    public byte Level { get; set; }
    public byte Realm { get; set; }
    public uint LeaderId { get; set; }
    public DateTime CreateDate { get; set; }
    public string Motd { get; set; } = string.Empty;
    public string AboutUs { get; set; } = string.Empty;
    public uint Xp { get; set; }
    public long Renown { get; set; }
    public string BriefDescription { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public byte PlayStyle { get; set; }
    public byte Atmosphere { get; set; }
    public uint CareersNeeded { get; set; }
    public byte Interests { get; set; }
    public bool ActivelyRecruiting { get; set; }
    public byte RanksNeeded { get; set; }
    public byte Tax { get; set; }
    public long Money { get; set; }
    /// <summary>Serialised vault-purchased flags (5 bytes, stored as text).</summary>
    public string GuildVaultPurchased { get; set; } = string.Empty;
    public string Banners { get; set; } = string.Empty;
    public string Heraldry { get; set; } = string.Empty;
    /// <summary>Serialised purchased tactics (up to 40 entries, stored as text).</summary>
    public string GuildTacticsPurchased { get; set; } = string.Empty;
    public uint? AllianceId { get; set; }
}
