namespace Core.Domain.Entities;

/// <summary>
/// Pending account record mapped to the <c>accounts_pending</c> table.
/// Pure POCO — all DB column mapping is handled by <see cref="AccountDbContext"/>.
/// <para>
/// This replaces the legacy <c>Common.AccountPending</c> class — no ORM base class
/// or FrameWork dependency.
/// </para>
/// </summary>
public sealed class AccountPending
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public DateTime Expires { get; set; }
}
