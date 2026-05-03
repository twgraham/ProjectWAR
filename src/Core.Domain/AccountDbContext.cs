using Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Core.Domain;

/// <summary>
/// EF Core context for the Accounts database (accounts, realms, pending accounts, IP bans).
/// <para>
/// Uses Npgsql (PostgreSQL). Column names are configured to match the PascalCase column names
/// defined in <c>war_accounts.sql</c>, preserving the legacy MySQL naming convention that
/// was carried forward into the PostgreSQL migration.
/// </para>
/// </summary>
public sealed class AccountDbContext(DbContextOptions<AccountDbContext> options)
    : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Realm> Realms => Set<Realm>();
    public DbSet<AccountPending> AccountPendings => Set<AccountPending>();
    public DbSet<IpBan> IpBans => Set<IpBan>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureAccount(modelBuilder);
        ConfigureRealm(modelBuilder);
        ConfigureAccountPending(modelBuilder);
        ConfigureIpBan(modelBuilder);
    }

    private static void ConfigureAccount(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Account>(entity =>
        {
            entity.ToTable("accounts");
            entity.HasKey(e => e.AccountId);

            entity.Property(e => e.AccountId).HasColumnName("AccountId").ValueGeneratedOnAdd();
            entity.Property(e => e.PacketLog).HasColumnName("PacketLog");
            entity.Property(e => e.Username).HasColumnName("Username").HasMaxLength(255);
            entity.Property(e => e.Password).HasColumnName("Password").HasMaxLength(255);
            entity.Property(e => e.CryptPassword).HasColumnName("CryptPassword").HasMaxLength(255);
            entity.Property(e => e.Ip).HasColumnName("Ip").HasMaxLength(255);
            entity.Property(e => e.Token).HasColumnName("Token").HasMaxLength(255);
            entity.Property(e => e.GmLevel).HasColumnName("GmLevel");
            entity.Property(e => e.Banned).HasColumnName("Banned");
            entity.Property(e => e.BanReason).HasColumnName("BanReason");
            entity.Property(e => e.AdviceBlockEnd).HasColumnName("AdviceBlockEnd");
            entity.Property(e => e.StealthMuteEnd).HasColumnName("StealthMuteEnd");
            entity.Property(e => e.CoreLevel).HasColumnName("CoreLevel");
            entity.Property(e => e.LastLogged).HasColumnName("LastLogged");
            entity.Property(e => e.LastNameChanged).HasColumnName("LastNameChanged");
            entity.Property(e => e.LastPatcherLog).HasColumnName("LastPatcherLog");
            entity.Property(e => e.InvalidPasswordCount).HasColumnName("InvalidPasswordCount");
            entity.Property(e => e.NoSurname).HasColumnName("noSurname");
            entity.Property(e => e.Email).HasColumnName("Email");

            entity.Ignore(e => e.IsBanned);
            entity.Ignore(e => e.IsStealthMuted);
            entity.Ignore(e => e.IsAdviceBlocked);

            entity.HasIndex(e => e.Username).IsUnique();
        });
    }

    private static void ConfigureRealm(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Realm>(entity =>
        {
            entity.ToTable("realms");
            entity.HasKey(e => e.RealmId);

            entity.Property(e => e.RealmId).HasColumnName("RealmId");
            entity.Property(e => e.Name).HasColumnName("Name").HasMaxLength(255);
            entity.Property(e => e.Language).HasColumnName("Language").HasMaxLength(255);
            entity.Property(e => e.Adresse).HasColumnName("Adresse").HasMaxLength(255);
            entity.Property(e => e.Port).HasColumnName("Port");
            entity.Property(e => e.AllowTrials).HasColumnName("AllowTrials").HasMaxLength(32);
            entity.Property(e => e.CharfxerAvailable).HasColumnName("CharfxerAvailable").HasMaxLength(32);
            entity.Property(e => e.Legacy).HasColumnName("Legacy").HasMaxLength(32);
            entity.Property(e => e.BonusDestruction).HasColumnName("BonusDestruction").HasMaxLength(32);
            entity.Property(e => e.BonusOrder).HasColumnName("BonusOrder").HasMaxLength(32);
            entity.Property(e => e.Redirect).HasColumnName("Redirect").HasMaxLength(32);
            entity.Property(e => e.Region).HasColumnName("Region").HasMaxLength(32);
            entity.Property(e => e.Retired).HasColumnName("Retired").HasMaxLength(32);
            entity.Property(e => e.WaitingDestruction).HasColumnName("WaitingDestruction").HasMaxLength(32);
            entity.Property(e => e.WaitingOrder).HasColumnName("WaitingOrder").HasMaxLength(32);
            entity.Property(e => e.DensityDestruction).HasColumnName("DensityDestruction").HasMaxLength(32);
            entity.Property(e => e.DensityOrder).HasColumnName("DensityOrder").HasMaxLength(32);
            entity.Property(e => e.OpenRvr).HasColumnName("OpenRvr").HasMaxLength(32);
            entity.Property(e => e.Rp).HasColumnName("Rp").HasMaxLength(32);
            entity.Property(e => e.Status).HasColumnName("Status").HasMaxLength(32);
            entity.Property(e => e.Online).HasColumnName("Online");
            entity.Property(e => e.OnlineDate).HasColumnName("OnlineDate");
            entity.Property(e => e.OnlinePlayers).HasColumnName("OnlinePlayers");
            entity.Property(e => e.OrderCount).HasColumnName("OrderCount");
            entity.Property(e => e.DestructionCount).HasColumnName("DestructionCount");
            entity.Property(e => e.MaxPlayers).HasColumnName("MaxPlayers");
            entity.Property(e => e.OrderCharacters).HasColumnName("OrderCharacters");
            entity.Property(e => e.DestruCharacters).HasColumnName("DestruCharacters");
            entity.Property(e => e.NextRotationTime).HasColumnName("NextRotationTime");
            entity.Property(e => e.MasterPassword).HasColumnName("MasterPassword");
            entity.Property(e => e.BootTime).HasColumnName("BootTime");
        });
    }

    private static void ConfigureAccountPending(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AccountPending>(entity =>
        {
            entity.ToTable("accounts_pending");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasColumnName("Id").ValueGeneratedOnAdd();
            entity.Property(e => e.Username).HasColumnName("Username").HasMaxLength(255);
            entity.Property(e => e.Code).HasColumnName("Code").HasMaxLength(255);
            entity.Property(e => e.Expires).HasColumnName("Expires");

            entity.HasIndex(e => e.Username).IsUnique();
        });
    }

    private static void ConfigureIpBan(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IpBan>(entity =>
        {
            entity.ToTable("ip_bans");
            entity.HasKey(e => e.Ip);

            entity.Property(e => e.Ip).HasColumnName("Ip").HasMaxLength(255);
            entity.Property(e => e.Expire).HasColumnName("Expire");
        });
    }
}
