using Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Core.Domain;

/// <summary>
/// EF Core context for the Characters database (per-account and per-character mutable data).
/// </summary>
public sealed class CharacterDbContext(DbContextOptions<CharacterDbContext> options)
    : DbContext(options)
{
    public DbSet<Auction> Auctions => Set<Auction>();
    public DbSet<BannedName> BannedNames => Set<BannedName>();
    public DbSet<BugReport> BugReports => Set<BugReport>();
    public DbSet<Character> Characters => Set<Character>();
    public DbSet<CharacterAbility> CharacterAbilities => Set<CharacterAbility>();
    public DbSet<CharacterBagPool> CharacterBagPools => Set<CharacterBagPool>();
    public DbSet<CharacterClientData> CharacterClientData => Set<CharacterClientData>();
    public DbSet<CharacterDeletion> CharacterDeletions => Set<CharacterDeletion>();
    public DbSet<CharacterInfluence> CharacterInfluences => Set<CharacterInfluence>();
    public DbSet<CharacterItem> CharacterItems => Set<CharacterItem>();
    public DbSet<CharacterMail> CharacterMails => Set<CharacterMail>();
    public DbSet<CharacterQuest> CharacterQuests => Set<CharacterQuest>();
    public DbSet<CharacterSavedBuff> CharacterSavedBuffs => Set<CharacterSavedBuff>();
    public DbSet<CharacterSocial> CharacterSocials => Set<CharacterSocial>();
    public DbSet<CharacterTok> CharacterToks => Set<CharacterTok>();
    public DbSet<CharacterTokKills> CharacterTokKills => Set<CharacterTokKills>();
    public DbSet<CharacterValue> CharacterValues => Set<CharacterValue>();
    public DbSet<GmCommandLog> GmCommandLogs => Set<GmCommandLog>();
    public DbSet<GuildAllianceInfo> GuildAllianceInfos => Set<GuildAllianceInfo>();
    public DbSet<GuildEvent> GuildEvents => Set<GuildEvent>();
    public DbSet<GuildInfo> GuildInfos => Set<GuildInfo>();
    public DbSet<GuildLog> GuildLogs => Set<GuildLog>();
    public DbSet<GuildMember> GuildMembers => Set<GuildMember>();
    public DbSet<GuildRank> GuildRanks => Set<GuildRank>();
    public DbSet<GuildVaultItem> GuildVaultItems => Set<GuildVaultItem>();
    public DbSet<ScenarioDuration> ScenarioDurations => Set<ScenarioDuration>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureAuction(modelBuilder);
        ConfigureBannedName(modelBuilder);
        ConfigureBugReport(modelBuilder);
        ConfigureCharacter(modelBuilder);
        ConfigureCharacterAbility(modelBuilder);
        ConfigureCharacterBagPool(modelBuilder);
        ConfigureCharacterClientData(modelBuilder);
        ConfigureCharacterDeletion(modelBuilder);
        ConfigureCharacterInfluence(modelBuilder);
        ConfigureCharacterItem(modelBuilder);
        ConfigureCharacterMail(modelBuilder);
        ConfigureCharacterQuest(modelBuilder);
        ConfigureCharacterSavedBuff(modelBuilder);
        ConfigureCharacterSocial(modelBuilder);
        ConfigureCharacterTok(modelBuilder);
        ConfigureCharacterTokKills(modelBuilder);
        ConfigureCharacterValue(modelBuilder);
        ConfigureGmCommandLog(modelBuilder);
        ConfigureGuildAllianceInfo(modelBuilder);
        ConfigureGuildEvent(modelBuilder);
        ConfigureGuildInfo(modelBuilder);
        ConfigureGuildLog(modelBuilder);
        ConfigureGuildMember(modelBuilder);
        ConfigureGuildRank(modelBuilder);
        ConfigureGuildVaultItem(modelBuilder);
        ConfigureScenarioDuration(modelBuilder);
    }

    private static void ConfigureAuction(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Auction>(entity =>
        {
            entity.ToTable("auctions");
            entity.HasKey(e => e.AuctionId);

            entity.Property(e => e.AuctionId).HasColumnName("AuctionId");
            entity.Property(e => e.Realm).HasColumnName("Realm");
            entity.Property(e => e.SellerId).HasColumnName("SellerId");
            entity.Property(e => e.ItemId).HasColumnName("ItemId");
            entity.Property(e => e.SellPrice).HasColumnName("SellPrice");
            entity.Property(e => e.Count).HasColumnName("Count");
            entity.Property(e => e.StartTime).HasColumnName("StartTime");
            entity.Property(e => e.Talismans).HasColumnName("Talismans").HasMaxLength(40);
            entity.Property(e => e.PrimaryDye).HasColumnName("PrimaryDye");
            entity.Property(e => e.SecondaryDye).HasColumnName("SecondaryDye");
        });
    }

    private static void ConfigureBannedName(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BannedName>(entity =>
        {
            entity.ToTable("banned_names");
            entity.HasKey(e => e.NameString);

            entity.Property(e => e.NameString).HasColumnName("NameString").HasMaxLength(255);
            entity.Property(e => e.FilterTypeString).HasColumnName("FilterTypeString");
        });
    }

    private static void ConfigureBugReport(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BugReport>(entity =>
        {
            entity.ToTable("bug_report");
            entity.HasKey(e => e.BugReportId);

            entity.Property(e => e.BugReportId).HasColumnName("bug_report_ID").ValueGeneratedOnAdd();
            entity.Property(e => e.AccountId).HasColumnName("AccountId");
            entity.Property(e => e.CharacterId).HasColumnName("CharacterId");
            entity.Property(e => e.ZoneId).HasColumnName("ZoneId");
            entity.Property(e => e.X).HasColumnName("X");
            entity.Property(e => e.Y).HasColumnName("Y");
            entity.Property(e => e.Time).HasColumnName("Time");
            entity.Property(e => e.Type).HasColumnName("Type");
            entity.Property(e => e.Category).HasColumnName("Category");
            entity.Property(e => e.Message).HasColumnName("Message");
            entity.Property(e => e.ReportType).HasColumnName("ReportType");
            entity.Property(e => e.FieldSting).HasColumnName("FieldSting");
            entity.Property(e => e.Assigned).HasColumnName("Assigned");
        });
    }

    private static void ConfigureCharacter(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Character>(entity =>
        {
            entity.ToTable("characters");
            entity.HasKey(e => e.CharacterId);

            entity.Property(e => e.CharacterId)
                .HasColumnName("CharacterId")
                .ValueGeneratedOnAdd();
            entity.Property(e => e.Name).HasColumnName("Name").HasMaxLength(24);
            entity.Property(e => e.Surname).HasColumnName("Surname").HasMaxLength(24);
            entity.Property(e => e.RealmId).HasColumnName("RealmId");
            entity.Property(e => e.AccountId).HasColumnName("AccountId");
            entity.Property(e => e.SlotId).HasColumnName("SlotId");
            entity.Property(e => e.ModelId).HasColumnName("ModelId");
            entity.Property(e => e.Career).HasColumnName("Career");
            entity.Property(e => e.CareerLine).HasColumnName("CareerLine");
            entity.Property(e => e.Realm).HasColumnName("Realm");
            entity.Property(e => e.HeldLeft).HasColumnName("HeldLeft");
            entity.Property(e => e.Race).HasColumnName("Race");
            entity.Property(e => e.Traits).HasColumnName("Traits");
            entity.Property(e => e.Sex).HasColumnName("Sex");
            entity.Property(e => e.Anonymous).HasColumnName("Anonymous");
            entity.Property(e => e.Hidden).HasColumnName("Hidden");
            entity.Property(e => e.OldName).HasColumnName("OldName").HasMaxLength(24);
            entity.Property(e => e.PetName).HasColumnName("PetName").HasMaxLength(24);
            entity.Property(e => e.PetModel).HasColumnName("PetModel");
            entity.Property(e => e.HonorPoints).HasColumnName("HonorPoints");
            entity.Property(e => e.HonorRank).HasColumnName("HonorRank");

            // Runtime-only properties — no backing DB columns
            entity.Ignore(e => e.CareerFlags);
            entity.Ignore(e => e.Level);
            entity.Ignore(e => e.FirstConnect);
            
            // Relationships
            entity.HasOne(x => x.Value)
                .WithOne(x => x.Character)
                .HasForeignKey<CharacterValue>(x => x.CharacterId);
            
            entity.HasMany(x => x.Items)
                .WithOne(x => x.Character)
                .HasForeignKey(x => x.CharacterId);

            entity.HasIndex(e => e.Name).IsUnique();
        });
    }

    private static void ConfigureCharacterAbility(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CharacterAbility>(entity =>
        {
            entity.ToTable("character_abilities");
            entity.HasKey(e => e.CharacterAbilitiesId);

            entity.Property(e => e.CharacterAbilitiesId).HasColumnName("character_abilities_ID").ValueGeneratedOnAdd();
            entity.Property(e => e.CharacterID).HasColumnName("CharacterID");
            entity.Property(e => e.AbilityID).HasColumnName("AbilityID");
            entity.Property(e => e.LastCast).HasColumnName("LastCast");
        });
    }

    private static void ConfigureCharacterBagPool(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CharacterBagPool>(entity =>
        {
            entity.ToTable("character_bag_pools");
            entity.HasKey(e => new { e.CharacterId, e.BagType });

            entity.Property(e => e.CharacterId).HasColumnName("CharacterId");
            entity.Property(e => e.BagType).HasColumnName("Bag_Type");
            entity.Property(e => e.BagPoolValue).HasColumnName("BagPool_Value");
        });
    }

    private static void ConfigureCharacterClientData(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CharacterClientData>(entity =>
        {
            entity.ToTable("character_client_data");
            entity.HasKey(e => e.CharacterId);

            entity.Property(e => e.CharacterId).HasColumnName("CharacterId");
            entity.Property(e => e.ClientDataString).HasColumnName("ClientDataString");
        });
    }

    private static void ConfigureCharacterDeletion(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CharacterDeletion>(entity =>
        {
            entity.ToTable("character_deletions");
            entity.HasKey(e => e.CharacterDeletionsId);

            entity.Property(e => e.CharacterDeletionsId).HasColumnName("character_deletions_ID").ValueGeneratedOnAdd();
            entity.Property(e => e.DeletionIP).HasColumnName("DeletionIP");
            entity.Property(e => e.AccountID).HasColumnName("AccountID");
            entity.Property(e => e.AccountName).HasColumnName("AccountName");
            entity.Property(e => e.CharacterID).HasColumnName("CharacterID");
            entity.Property(e => e.CharacterName).HasColumnName("CharacterName");
            entity.Property(e => e.DeletionTimeSeconds).HasColumnName("DeletionTimeSeconds");
        });
    }

    private static void ConfigureCharacterInfluence(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CharacterInfluence>(entity =>
        {
            entity.ToTable("character_influences");
            entity.HasKey(e => new { e.CharacterId, e.InfluenceId });

            entity.Property(e => e.CharacterId).HasColumnName("CharacterId");
            entity.Property(e => e.InfluenceId).HasColumnName("InfluenceId");
            entity.Property(e => e.InfluenceCount).HasColumnName("InfluenceCount");
            entity.Property(e => e.Tier1Itemtaken).HasColumnName("Tier_1_Itemtaken");
            entity.Property(e => e.Tier2Itemtaken).HasColumnName("Tier_2_Itemtaken");
            entity.Property(e => e.Tier3Itemtaken).HasColumnName("Tier_3_Itemtaken");
        });
    }

    private static void ConfigureCharacterItem(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CharacterItem>(entity =>
        {
            entity.ToTable("characters_items");
            entity.HasKey(e => e.CharactersItemsId);

            entity.Property(e => e.CharactersItemsId).HasColumnName("characters_items_ID").ValueGeneratedOnAdd();
            entity.Property(e => e.Guid).HasColumnName("Guid");
            entity.Property(e => e.CharacterId).HasColumnName("CharacterId");
            entity.Property(e => e.Entry).HasColumnName("Entry");
            entity.Property(e => e.SlotId).HasColumnName("SlotId");
            entity.Property(e => e.ModelId).HasColumnName("ModelId");
            entity.Property(e => e.Counts).HasColumnName("Counts");
            entity.Property(e => e.Talismans).HasColumnName("Talismans").HasMaxLength(40);
            entity.Property(e => e.PrimaryDye).HasColumnName("PrimaryDye");
            entity.Property(e => e.SecondaryDye).HasColumnName("SecondaryDye");
            entity.Property(e => e.BoundtoPlayer).HasColumnName("BoundtoPlayer");
            entity.Property(e => e.AlternateAppereanceEntry).HasColumnName("Alternate_AppereanceEntry");
            
            // Relationships
            entity.HasOne(x => x.Character)
                .WithMany(x => x.Items)
                .HasForeignKey(x => x.CharacterId)
                .IsRequired();
        });
    }

    private static void ConfigureCharacterMail(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CharacterMail>(entity =>
        {
            entity.ToTable("characters_mails");
            entity.HasKey(e => e.Guid);

            entity.Property(e => e.Guid).HasColumnName("Guid").ValueGeneratedOnAdd();
            entity.Property(e => e.AuctionType).HasColumnName("AuctionType");
            entity.Property(e => e.CharacterId).HasColumnName("CharacterId");
            entity.Property(e => e.CharacterIdSender).HasColumnName("CharacterIdSender");
            entity.Property(e => e.SenderName).HasColumnName("SenderName").HasMaxLength(255);
            entity.Property(e => e.ReceiverName).HasColumnName("ReceiverName").HasMaxLength(255);
            entity.Property(e => e.SendDate).HasColumnName("SendDate");
            entity.Property(e => e.ReadDate).HasColumnName("ReadDate");
            entity.Property(e => e.Title).HasColumnName("Title").HasMaxLength(255);
            entity.Property(e => e.Content).HasColumnName("Content");
            entity.Property(e => e.Money).HasColumnName("Money");
            entity.Property(e => e.Cr).HasColumnName("Cr");
            entity.Property(e => e.Opened).HasColumnName("Opened");
            entity.Property(e => e.ItemsString).HasColumnName("ItemsString");
        });
    }

    private static void ConfigureCharacterQuest(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CharacterQuest>(entity =>
        {
            entity.ToTable("characters_quests");
            entity.HasKey(e => new { e.CharacterId, e.QuestID });

            entity.Property(e => e.CharacterId).HasColumnName("CharacterId");
            entity.Property(e => e.QuestID).HasColumnName("QuestID");
            entity.Property(e => e.Objectives).HasColumnName("Objectives").HasMaxLength(64);
            entity.Property(e => e.Done).HasColumnName("Done");
        });
    }

    private static void ConfigureCharacterSavedBuff(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CharacterSavedBuff>(entity =>
        {
            entity.ToTable("character_saved_buffs");
            entity.HasKey(e => new { e.CharacterId, e.BuffId });

            entity.Property(e => e.CharacterId).HasColumnName("CharacterId");
            entity.Property(e => e.BuffId).HasColumnName("BuffId");
            entity.Property(e => e.Level).HasColumnName("Level");
            entity.Property(e => e.StackLevel).HasColumnName("StackLevel");
            entity.Property(e => e.EndTimeSeconds).HasColumnName("EndTimeSeconds");
        });
    }

    private static void ConfigureCharacterSocial(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CharacterSocial>(entity =>
        {
            entity.ToTable("characters_socials");
            entity.HasKey(e => new { e.CharacterId, e.DistCharacterId });

            entity.Property(e => e.CharacterId).HasColumnName("CharacterId");
            entity.Property(e => e.DistCharacterId).HasColumnName("DistCharacterId");
            entity.Property(e => e.DistName).HasColumnName("DistName").HasMaxLength(255);
            entity.Property(e => e.Friend).HasColumnName("Friend");
            entity.Property(e => e.Ignore).HasColumnName("Ignore");
        });
    }

    private static void ConfigureCharacterTok(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CharacterTok>(entity =>
        {
            entity.ToTable("characters_toks");
            entity.HasKey(e => new { e.CharacterId, e.TokEntry });

            entity.Property(e => e.CharacterId).HasColumnName("CharacterId");
            entity.Property(e => e.TokEntry).HasColumnName("TokEntry");
            entity.Property(e => e.Count).HasColumnName("Count");
        });
    }

    private static void ConfigureCharacterTokKills(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CharacterTokKills>(entity =>
        {
            entity.ToTable("characters_toks_kills");
            entity.HasKey(e => new { e.CharacterId, e.NPCEntry });

            entity.Property(e => e.CharacterId).HasColumnName("CharacterId");
            entity.Property(e => e.NPCEntry).HasColumnName("NPCEntry");
            entity.Property(e => e.Count).HasColumnName("Count");
        });
    }

    private static void ConfigureCharacterValue(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CharacterValue>(entity =>
        {
            entity.ToTable("characters_value");
            entity.HasKey(e => e.CharacterId);

            entity.Property(e => e.CharacterId)
                .HasColumnName("CharacterId")
                .ValueGeneratedNever();
            entity.Property(e => e.Level).HasColumnName("Level");
            entity.Property(e => e.Xp).HasColumnName("Xp");
            entity.Property(e => e.XpMode).HasColumnName("XpMode");
            entity.Property(e => e.RestXp).HasColumnName("RestXp");
            entity.Property(e => e.Renown).HasColumnName("Renown");
            entity.Property(e => e.RenownRank).HasColumnName("RenownRank");
            entity.Property(e => e.Money).HasColumnName("Money");
            entity.Property(e => e.Speed).HasColumnName("Speed");
            entity.Property(e => e.PlayedTime).HasColumnName("PlayedTime");
            entity.Property(e => e.LastSeen).HasColumnName("LastSeen");
            entity.Property(e => e.RegionId).HasColumnName("RegionId");
            entity.Property(e => e.ZoneId).HasColumnName("ZoneId");
            entity.Property(e => e.WorldX).HasColumnName("WorldX");
            entity.Property(e => e.WorldY).HasColumnName("WorldY");
            entity.Property(e => e.WorldZ).HasColumnName("WorldZ");
            entity.Property(e => e.WorldO).HasColumnName("WorldO");
            entity.Property(e => e.RallyPoint).HasColumnName("RallyPoint");
            entity.Property(e => e.BagBuy).HasColumnName("BagBuy");
            entity.Property(e => e.BankBuy).HasColumnName("BankBuy");
            entity.Property(e => e.Skills).HasColumnName("Skills");
            entity.Property(e => e.Online).HasColumnName("Online");
            entity.Property(e => e.GearShow).HasColumnName("GearShow");
            entity.Property(e => e.TitleId).HasColumnName("TitleId");
            entity.Property(e => e.RenownSkills).HasColumnName("RenownSkills");
            entity.Property(e => e.MasterySkills).HasColumnName("MasterySkills");
            entity.Property(e => e.Morale1).HasColumnName("Morale1");
            entity.Property(e => e.Morale2).HasColumnName("Morale2");
            entity.Property(e => e.Morale3).HasColumnName("Morale3");
            entity.Property(e => e.Morale4).HasColumnName("Morale4");
            entity.Property(e => e.Tactic1).HasColumnName("Tactic1");
            entity.Property(e => e.Tactic2).HasColumnName("Tactic2");
            entity.Property(e => e.Tactic3).HasColumnName("Tactic3");
            entity.Property(e => e.Tactic4).HasColumnName("Tactic4");
            entity.Property(e => e.GatheringSkill).HasColumnName("GatheringSkill");
            entity.Property(e => e.GatheringSkillLevel).HasColumnName("GatheringSkillLevel");
            entity.Property(e => e.CraftingSkill).HasColumnName("CraftingSkill");
            entity.Property(e => e.CraftingSkillLevel).HasColumnName("CraftingSkillLevel");
            entity.Property(e => e.ExperimentalMode).HasColumnName("ExperimentalMode");
            entity.Property(e => e.RVRKills).HasColumnName("RVRKills");
            entity.Property(e => e.RVRDeaths).HasColumnName("RVRDeaths");
            entity.Property(e => e.CraftingBags).HasColumnName("CraftingBags");
            entity.Property(e => e.PendingXp).HasColumnName("PendingXp");
            entity.Property(e => e.PendingRenown).HasColumnName("PendingRenown");
            entity.Property(e => e.Lockouts).HasColumnName("Lockouts");
            // Preserves the original (typo-inclusive) column name from the SQL migration.
            entity.Property(e => e.DisconcetTime).HasColumnName("DisconcetTime");
            
            // Relationships
            entity.HasOne(x => x.Character)
                .WithOne(x => x.Value)
                .HasForeignKey<CharacterValue>(x => x.CharacterId)
                .IsRequired();
        });
    }

    private static void ConfigureGmCommandLog(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GmCommandLog>(entity =>
        {
            entity.ToTable("gmcommandlogs");
            entity.HasKey(e => e.GmCommandLogsId);

            entity.Property(e => e.GmCommandLogsId).HasColumnName("gmcommandlogs_ID").ValueGeneratedOnAdd();
            entity.Property(e => e.AccountId).HasColumnName("AccountId");
            entity.Property(e => e.PlayerName).HasColumnName("PlayerName").HasMaxLength(255);
            entity.Property(e => e.Command).HasColumnName("Command");
            entity.Property(e => e.Date).HasColumnName("Date");
        });
    }

    private static void ConfigureGuildAllianceInfo(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GuildAllianceInfo>(entity =>
        {
            entity.ToTable("guild_alliance_info");
            entity.HasKey(e => e.AllianceId);

            entity.Property(e => e.AllianceId).HasColumnName("AllianceId");
            entity.Property(e => e.Name).HasColumnName("Name").HasMaxLength(255);

            entity.HasIndex(e => e.Name).IsUnique();
        });
    }

    private static void ConfigureGuildEvent(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GuildEvent>(entity =>
        {
            entity.ToTable("guild_event");
            entity.HasKey(e => e.GuildEventId);

            entity.Property(e => e.GuildEventId).HasColumnName("guild_event_ID").ValueGeneratedOnAdd();
            entity.Property(e => e.SlotId).HasColumnName("SlotId");
            entity.Property(e => e.GuildId).HasColumnName("GuildId");
            entity.Property(e => e.CharacterId).HasColumnName("CharacterId");
            entity.Property(e => e.Begin).HasColumnName("Begin");
            entity.Property(e => e.End).HasColumnName("End");
            entity.Property(e => e.Name).HasColumnName("Name");
            entity.Property(e => e.Description).HasColumnName("Description");
            entity.Property(e => e.Alliance).HasColumnName("Alliance");
            entity.Property(e => e.Locked).HasColumnName("Locked");
            entity.Property(e => e.Signups).HasColumnName("Signups");
        });
    }

    private static void ConfigureGuildInfo(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GuildInfo>(entity =>
        {
            entity.ToTable("guild_info");
            entity.HasKey(e => e.GuildId);

            entity.Property(e => e.GuildId).HasColumnName("GuildId");
            entity.Property(e => e.Name).HasColumnName("Name").HasMaxLength(255);
            entity.Property(e => e.Level).HasColumnName("Level");
            entity.Property(e => e.Realm).HasColumnName("Realm");
            entity.Property(e => e.LeaderId).HasColumnName("LeaderId");
            entity.Property(e => e.CreateDate).HasColumnName("CreateDate");
            entity.Property(e => e.Motd).HasColumnName("Motd");
            entity.Property(e => e.AboutUs).HasColumnName("AboutUs");
            entity.Property(e => e.Xp).HasColumnName("Xp");
            entity.Property(e => e.Renown).HasColumnName("Renown");
            entity.Property(e => e.BriefDescription).HasColumnName("BriefDescription");
            entity.Property(e => e.Summary).HasColumnName("Summary");
            entity.Property(e => e.PlayStyle).HasColumnName("PlayStyle");
            entity.Property(e => e.Atmosphere).HasColumnName("Atmosphere");
            entity.Property(e => e.CareersNeeded).HasColumnName("CareersNeeded");
            entity.Property(e => e.Interests).HasColumnName("Interests");
            entity.Property(e => e.ActivelyRecruiting).HasColumnName("ActivelyRecruiting");
            entity.Property(e => e.RanksNeeded).HasColumnName("RanksNeeded");
            entity.Property(e => e.Tax).HasColumnName("Tax");
            entity.Property(e => e.Money).HasColumnName("Money");
            // Preserves the original all-lowercase column name from the SQL migration.
            entity.Property(e => e.GuildVaultPurchased).HasColumnName("guildvaultpurchased");
            entity.Property(e => e.Banners).HasColumnName("Banners");
            entity.Property(e => e.Heraldry).HasColumnName("Heraldry");
            entity.Property(e => e.GuildTacticsPurchased).HasColumnName("GuildTacticsPurchased");
            entity.Property(e => e.AllianceId).HasColumnName("AllianceId");

            entity.HasIndex(e => e.Name).IsUnique();
        });
    }

    private static void ConfigureGuildLog(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GuildLog>(entity =>
        {
            entity.ToTable("guild_logs");
            entity.HasKey(e => e.GuildLogsId);

            entity.Property(e => e.GuildLogsId).HasColumnName("guild_logs_ID").ValueGeneratedOnAdd();
            entity.Property(e => e.GuildId).HasColumnName("GuildId");
            entity.Property(e => e.Time).HasColumnName("Time");
            entity.Property(e => e.Type).HasColumnName("Type");
            entity.Property(e => e.Text).HasColumnName("Text");
        });
    }

    private static void ConfigureGuildMember(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GuildMember>(entity =>
        {
            entity.ToTable("guild_members");
            entity.HasKey(e => e.CharacterId);

            entity.Property(e => e.CharacterId).HasColumnName("CharacterId");
            entity.Property(e => e.GuildId).HasColumnName("GuildId");
            entity.Property(e => e.RankId).HasColumnName("RankId");
            entity.Property(e => e.PublicNote).HasColumnName("PublicNote");
            entity.Property(e => e.OfficerNote).HasColumnName("OfficerNote");
            entity.Property(e => e.JoinDate).HasColumnName("JoinDate");
            entity.Property(e => e.LastSeen).HasColumnName("LastSeen");
            entity.Property(e => e.RealmCaptain).HasColumnName("RealmCaptain");
            entity.Property(e => e.StandardBearer).HasColumnName("StandardBearer");
            entity.Property(e => e.GuildRecruiter).HasColumnName("GuildRecruiter");
            entity.Property(e => e.RenownContributed).HasColumnName("RenownContributed");
            entity.Property(e => e.Tithe).HasColumnName("Tithe");
            entity.Property(e => e.TitheContributed).HasColumnName("TitheContributed");
        });
    }

    private static void ConfigureGuildRank(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GuildRank>(entity =>
        {
            entity.ToTable("guild_ranks");
            entity.HasKey(e => new { e.GuildId, e.RankId });

            entity.Property(e => e.GuildId).HasColumnName("GuildId");
            entity.Property(e => e.RankId).HasColumnName("RankId");
            entity.Property(e => e.Name).HasColumnName("Name");
            entity.Property(e => e.Permissions).HasColumnName("Permissions");
            entity.Property(e => e.Enabled).HasColumnName("Enabled");
        });
    }

    private static void ConfigureGuildVaultItem(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GuildVaultItem>(entity =>
        {
            entity.ToTable("guild_vault_item");
            entity.HasKey(e => new { e.GuildId, e.VaultId, e.SlotId });

            entity.Property(e => e.GuildId).HasColumnName("GuildId");
            entity.Property(e => e.Entry).HasColumnName("Entry");
            entity.Property(e => e.VaultId).HasColumnName("VaultId");
            entity.Property(e => e.SlotId).HasColumnName("SlotId");
            entity.Property(e => e.Counts).HasColumnName("Counts");
            entity.Property(e => e.Talismans).HasColumnName("Talismans").HasMaxLength(40);
            entity.Property(e => e.PrimaryDye).HasColumnName("PrimaryDye");
            entity.Property(e => e.SecondaryDye).HasColumnName("SecondaryDye");
        });
    }

    private static void ConfigureScenarioDuration(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ScenarioDuration>(entity =>
        {
            entity.ToTable("scenario_durations");
            entity.HasKey(e => e.Guid);

            entity.Property(e => e.Guid).HasColumnName("Guid").ValueGeneratedOnAdd();
            entity.Property(e => e.ScenarioId).HasColumnName("ScenarioId");
            entity.Property(e => e.Tier).HasColumnName("Tier");
            entity.Property(e => e.StartTime).HasColumnName("StartTime");
            entity.Property(e => e.DurationSeconds).HasColumnName("DurationSeconds");
        });
    }
}
