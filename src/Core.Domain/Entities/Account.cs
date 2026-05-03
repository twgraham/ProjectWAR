using System.Security.Cryptography;
using System.Text;

namespace Core.Domain.Entities;

/// <summary>
/// Persistent account record mapped to the <c>accounts</c> table.
/// Pure POCO — all DB column mapping is handled by <see cref="AccountDbContext"/>.
/// <para>
/// This replaces the legacy <c>Common.Account</c> class — no ORM base class
/// or FrameWork dependency.
/// </para>
/// </summary>
public sealed class Account
{
    public int AccountId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string CryptPassword { get; set; } = string.Empty;
    public string Ip { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public sbyte GmLevel { get; set; }
    public int Banned { get; set; }
    public string? BanReason { get; set; }
    public bool PacketLog { get; set; }
    public int AdviceBlockEnd { get; set; }
    public int StealthMuteEnd { get; set; }
    public int CoreLevel { get; set; }
    public int LastLogged { get; set; }
    public int LastNameChanged { get; set; }
    public string? LastPatcherLog { get; set; }
    public uint InvalidPasswordCount { get; set; }
    public sbyte NoSurname { get; set; }
    public string? Email { get; set; }

    /// <summary>Returns true if the account's ban timestamp is in the future (or permanent).</summary>
    public bool IsBanned => Banned != 0 && (Banned == 1 || Banned > (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds());

    /// <summary>Returns true if stealth-mute is still active.</summary>
    public bool IsStealthMuted => StealthMuteEnd > (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    /// <summary>Returns true if the advice-block is still active.</summary>
    public bool IsAdviceBlocked => AdviceBlockEnd > (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    public static string ConvertSHA256(string value)
    {
        byte[] data = SHA256.HashData(Encoding.ASCII.GetBytes(value));
        var sb = new StringBuilder();
        foreach (byte b in data)
            sb.Append(b.ToString("x2"));
        return sb.ToString();
    }
}
