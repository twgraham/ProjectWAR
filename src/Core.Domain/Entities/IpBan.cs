namespace Core.Domain.Entities;

/// <summary>
/// IP ban record mapped to the <c>ip_bans</c> table.
/// Pure POCO — all DB column mapping is handled by <see cref="AccountDbContext"/>.
/// <para>
/// This replaces the legacy <c>Common.Ip_ban</c> class — no ORM base class
/// or FrameWork dependency.
/// </para>
/// </summary>
public sealed class IpBan
{
    public string Ip { get; set; } = string.Empty;
    public int Expire { get; set; }
}
