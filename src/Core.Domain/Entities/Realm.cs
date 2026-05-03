namespace Core.Domain.Entities;

/// <summary>
/// Persistent realm record mapped to the <c>realms</c> table.
/// Pure POCO — all DB column mapping is handled by <see cref="AccountDbContext"/>.
/// <para>
/// This replaces the legacy <c>Common.Realm</c> class — no ORM base class
/// or FrameWork dependency.
/// </para>
/// </summary>
public sealed class Realm
{
    public byte RealmId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string Adresse { get; set; } = string.Empty;
    public int Port { get; set; }
    public string AllowTrials { get; set; } = "0";
    public string? CharfxerAvailable { get; set; }
    public string? Legacy { get; set; }
    public string BonusDestruction { get; set; } = "0";
    public string BonusOrder { get; set; } = "0";
    public string Redirect { get; set; } = "0";
    public string Region { get; set; } = "STR_REGION_NORTHAMERICA";
    public string Retired { get; set; } = "0";
    public string WaitingDestruction { get; set; } = "0";
    public string WaitingOrder { get; set; } = "0";
    public string DensityDestruction { get; set; } = "0";
    public string DensityOrder { get; set; } = "0";
    public string OpenRvr { get; set; } = "1";
    public string Rp { get; set; } = "1";
    public string Status { get; set; } = "0";
    public byte Online { get; set; }
    public DateTime OnlineDate { get; set; }
    public uint OnlinePlayers { get; set; }
    public uint OrderCount { get; set; }
    public uint DestructionCount { get; set; }
    public uint MaxPlayers { get; set; }
    public uint OrderCharacters { get; set; }
    public uint DestruCharacters { get; set; }
    public long NextRotationTime { get; set; }
    public string? MasterPassword { get; set; }
    public int BootTime { get; set; }
}
