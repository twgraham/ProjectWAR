using Microsoft.EntityFrameworkCore;
using WorldServerV2.Data.Entities;

namespace WorldServerV2.Data;

/// <summary>
/// EF Core context for the World database (static/read-only game data).
/// <para>
/// Uses Npgsql (PostgreSQL). Table and column names are configured to match the
/// existing schema for seamless data migration from the legacy MySQL database.
/// </para>
/// </summary>
public sealed class WorldDbContext(DbContextOptions<WorldDbContext> options)
    : DbContext(options)
{
    public DbSet<ClassInfo> ClassInfos => Set<ClassInfo>();
    public DbSet<ClassInfoItem> ClassInfoItems => Set<ClassInfoItem>();
    public DbSet<ItemInfo> ItemInfos => Set<ItemInfo>();
    public DbSet<CreatureProto> CreatureProtos => Set<CreatureProto>();
    public DbSet<CreatureSpawn> CreatureSpawns => Set<CreatureSpawn>();
    public DbSet<ZoneInfo> ZoneInfos => Set<ZoneInfo>();
    public DbSet<ZoneJump> ZoneJumps => Set<ZoneJump>();
    public DbSet<CharacterInfoStat> CharacterInfoStats => Set<CharacterInfoStat>();
    public DbSet<AbilityInfoEntity> AbilityInfos => Set<AbilityInfoEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureClassInfo(modelBuilder);
        ConfigureClassInfoItem(modelBuilder);
        ConfigureItemInfo(modelBuilder);
        ConfigureCreatureProto(modelBuilder);
        ConfigureCreatureSpawn(modelBuilder);
        ConfigureZoneInfo(modelBuilder);
        ConfigureZoneJump(modelBuilder);
        ConfigureCharacterInfoStat(modelBuilder);
        ConfigureAbilityInfo(modelBuilder);
    }

    private static void ConfigureClassInfo(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ClassInfo>(entity =>
        {
            entity.ToTable("characterinfo");

            entity.HasKey(e => e.Id);
                
            entity.Property(e => e.Id)
                .HasColumnName("career_line");
            entity.Property(e => e.ClassId)
                .HasColumnName("career")
                .HasConversion<byte>();
            entity.Property(e => e.ClassName).HasColumnName("career_name").HasMaxLength(255);
            entity.Property(e => e.Faction)
                .HasColumnName("realm")
                .HasConversion<byte>();
            entity.Property(e => e.Region).HasColumnName("region");
            entity.Property(e => e.ZoneId).HasColumnName("zone_id");
            entity.Property(x => x.WorldX).HasColumnName("world_x");
            entity.Property(x => x.WorldY).HasColumnName("world_y");
            entity.Property(x => x.WorldZ).HasColumnName("world_z");
            entity.Property(x => x.WorldO).HasColumnName("world_o");
            entity.Property(e => e.RallyPt).HasColumnName("rally_pt");
            entity.Property(e => e.Skills).HasColumnName("skills");
        });
    }
    
    private static void ConfigureClassInfoItem(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ClassInfoItem>(entity =>
        {
            entity.ToTable("characterinfo_items");

            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id)
                .HasColumnName("career_line");
            entity.Property(e => e.Entry).HasColumnName("entry");
            entity.Property(e => e.SlotId).HasColumnName("slot_id");
            entity.Property(e => e.Count).HasColumnName("count");
            entity.Property(e => e.ModelId).HasColumnName("model_id");
        });
    }

    private static void ConfigureItemInfo(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ItemInfo>(entity =>
        {
            entity.ToTable("item_infos");
            entity.HasKey(e => e.Entry);

            entity.Property(e => e.Entry).HasColumnName("entry");
            entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(255);
            entity.Property(e => e.Description).HasColumnName("description").HasMaxLength(255);
            entity.Property(e => e.Type).HasColumnName("type");
            entity.Property(e => e.Race).HasColumnName("race");
            entity.Property(e => e.ModelId).HasColumnName("model_id");
            entity.Property(e => e.SlotId).HasColumnName("slot_id");
            entity.Property(e => e.Rarity).HasColumnName("rarity");
            entity.Property(e => e.Career).HasColumnName("career");
            entity.Property(e => e.Skills).HasColumnName("skills");
            entity.Property(e => e.Bind).HasColumnName("bind");
            entity.Property(e => e.Armor).HasColumnName("armor");
            entity.Property(e => e.SpellId).HasColumnName("spell_id");
            entity.Property(e => e.ItemSet).HasColumnName("item_set");
            entity.Property(e => e.Dps).HasColumnName("dps");
            entity.Property(e => e.Speed).HasColumnName("speed");
            entity.Property(e => e.MinRank).HasColumnName("min_rank");
            entity.Property(e => e.MinRenown).HasColumnName("min_renown");
            entity.Property(e => e.ObjectLevel).HasColumnName("object_level");
            entity.Property(e => e.UniqueEquipped).HasColumnName("unique_equiped");
            entity.Property(e => e.StartQuest).HasColumnName("start_quest");
            entity.Property(e => e.Stats).HasColumnName("stats");
            entity.Property(e => e.Effects).HasColumnName("effects");
            entity.Property(e => e.Crafts).HasColumnName("crafts");
            entity.Property(e => e.SellPrice).HasColumnName("sell_price");
            entity.Property(e => e.SellRequiredItems).HasColumnName("sell_required_items");
            entity.Property(e => e.TalismanSlots).HasColumnName("talisman_slots");
            entity.Property(e => e.MaxStack).HasColumnName("max_stack");
            entity.Property(e => e.Unk27).HasColumnName("unk27");
            entity.Property(e => e.ScriptName).HasColumnName("script_name").HasMaxLength(255);
            entity.Property(e => e.TwoHanded).HasColumnName("two_handed");
            entity.Property(e => e.CraftResult).HasColumnName("craft_result");
            entity.Property(e => e.DyeAble).HasColumnName("dye_able");
            entity.Property(e => e.Salvageable).HasColumnName("salvageable");
            entity.Property(e => e.BaseColor1).HasColumnName("base_color1");
            entity.Property(e => e.BaseColor2).HasColumnName("base_color2");
            entity.Property(e => e.TokUnlock).HasColumnName("tok_unlock");
            entity.Property(e => e.TokUnlock2).HasColumnName("tok_unlock2");
            entity.Property(e => e.IsSiege).HasColumnName("is_siege");
        });
    }

    private static void ConfigureCreatureProto(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CreatureProto>(entity =>
        {
            entity.ToTable("creature_protos");
            entity.HasKey(e => e.Entry);

            entity.Property(e => e.Entry).HasColumnName("entry");
            entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(255);
            entity.Property(e => e.Model1).HasColumnName("model1");
            entity.Property(e => e.Model2).HasColumnName("model2");
            entity.Property(e => e.MinScale).HasColumnName("min_scale");
            entity.Property(e => e.MaxScale).HasColumnName("max_scale");
            entity.Property(e => e.MinLevel).HasColumnName("min_level");
            entity.Property(e => e.MaxLevel).HasColumnName("max_level");
            entity.Property(e => e.Faction).HasColumnName("faction");
            entity.Property(e => e.CreatureType).HasColumnName("creature_type");
            entity.Property(e => e.CreatureSubType).HasColumnName("creature_sub_type");
            entity.Property(e => e.Ranged).HasColumnName("ranged");
            entity.Property(e => e.IsWandering).HasColumnName("is_wandering");
            entity.Property(e => e.Icone).HasColumnName("icone");
            entity.Property(e => e.Emote).HasColumnName("emote");
            entity.Property(e => e.Title).HasColumnName("title");
            entity.Property(e => e.Unk).HasColumnName("unk");
            entity.Property(e => e.Unk1).HasColumnName("unk1");
            entity.Property(e => e.Unk2).HasColumnName("unk2");
            entity.Property(e => e.Unk3).HasColumnName("unk3");
            entity.Property(e => e.Unk4).HasColumnName("unk4");
            entity.Property(e => e.Unk5).HasColumnName("unk5");
            entity.Property(e => e.Unk6).HasColumnName("unk6");
            entity.Property(e => e.Flag).HasColumnName("flag").HasMaxLength(255);
            entity.Property(e => e.ScriptName).HasColumnName("script_name").HasMaxLength(255);
            entity.Property(e => e.LairBoss).HasColumnName("lair_boss");
            entity.Property(e => e.VendorId).HasColumnName("vendor_id");
            entity.Property(e => e.TokUnlock).HasColumnName("tok_unlock");
            entity.Property(e => e.States).HasColumnName("states");
            entity.Property(e => e.FigLeafData).HasColumnName("fig_leaf_data");
            entity.Property(e => e.BaseRadiusUnits).HasColumnName("base_radius_units");
            entity.Property(e => e.Career).HasColumnName("career");
            entity.Property(e => e.PowerModifier).HasColumnName("power_modifier");
            entity.Property(e => e.WoundsModifier).HasColumnName("wounds_modifier");
            entity.Property(e => e.Invulnerable).HasColumnName("invulnerable");
            entity.Property(e => e.WeaponDps).HasColumnName("weapon_dps");
            entity.Property(e => e.ImmuneToCC).HasColumnName("immune_to_cc");
        });
    }

    private static void ConfigureCreatureSpawn(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CreatureSpawn>(entity =>
        {
            entity.ToTable("creature_spawns");
            entity.HasKey(e => e.Guid);
            entity.Property(e => e.Guid).HasColumnName("guid").ValueGeneratedOnAdd();

            entity.Property(e => e.Entry).HasColumnName("entry");
            entity.Property(e => e.ZoneId).HasColumnName("zone_id");
            entity.Property(e => e.WorldX).HasColumnName("world_x");
            entity.Property(e => e.WorldY).HasColumnName("world_y");
            entity.Property(e => e.WorldZ).HasColumnName("world_z");
            entity.Property(e => e.WorldO).HasColumnName("world_o");
            entity.Property(e => e.Icone).HasColumnName("icone");
            entity.Property(e => e.Emote).HasColumnName("emote");
            entity.Property(e => e.RespawnMinutes).HasColumnName("respawn_minutes");
            entity.Property(e => e.Faction).HasColumnName("faction");
            entity.Property(e => e.WaypointType).HasColumnName("waypoint_type");
            entity.Property(e => e.Level).HasColumnName("level");
            entity.Property(e => e.Oid).HasColumnName("oid");
            entity.Property(e => e.Enabled).HasColumnName("enabled");

            // Proto is a runtime cross-link — not a DB column
            entity.Ignore(e => e.Proto);
        });
    }

    private static void ConfigureZoneInfo(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ZoneInfo>(entity =>
        {
            entity.ToTable("zone_infos");
            entity.HasKey(e => e.ZoneId);

            entity.Property(e => e.ZoneId).HasColumnName("zone_id");
            entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(255);
            entity.Property(e => e.MinLevel).HasColumnName("min_level");
            entity.Property(e => e.MaxLevel).HasColumnName("max_level");
            entity.Property(e => e.Type).HasColumnName("type");
            entity.Property(e => e.Tier).HasColumnName("tier");
            entity.Property(e => e.Pairing).HasColumnName("pairing");
            entity.Property(e => e.Price).HasColumnName("price");
            entity.Property(e => e.Region).HasColumnName("region");
            entity.Property(e => e.OffX).HasColumnName("off_x");
            entity.Property(e => e.OffY).HasColumnName("off_y");
            entity.Property(e => e.Collision).HasColumnName("collision");
            entity.Property(e => e.Illegal).HasColumnName("illegal");
        });
    }

    private static void ConfigureZoneJump(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ZoneJump>(entity =>
        {
            entity.ToTable("zone_jumps");
            entity.HasKey(e => e.Entry);

            entity.Property(e => e.Entry).HasColumnName("entry");
            entity.Property(e => e.ZoneId).HasColumnName("zone_id");
            entity.Property(e => e.WorldX).HasColumnName("world_x");
            entity.Property(e => e.WorldY).HasColumnName("world_y");
            entity.Property(e => e.WorldZ).HasColumnName("world_z");
            entity.Property(e => e.WorldO).HasColumnName("world_o");
            entity.Property(e => e.Type).HasColumnName("type");
            entity.Property(e => e.Enabled).HasColumnName("enabled");
            entity.Property(e => e.InstanceId).HasColumnName("instance_id");
        });
    }

    private static void ConfigureCharacterInfoStat(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CharacterInfoStat>(entity =>
        {
            entity.ToTable("characterinfo_stats");
            entity.HasKey(e => new { e.CareerLine, e.Level, e.StatId });

            entity.Property(e => e.CareerLine).HasColumnName("career_line");
            entity.Property(e => e.Level).HasColumnName("level");
            entity.Property(e => e.StatId).HasColumnName("stat_id");
            entity.Property(e => e.StatValue).HasColumnName("stat_value");
        });
    }

    private static void ConfigureAbilityInfo(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AbilityInfoEntity>(entity =>
        {
            entity.ToTable("abilities");
            entity.HasKey(e => e.Entry);

            entity.Property(e => e.Entry).HasColumnName("entry");
            entity.Property(e => e.CareerLine).HasColumnName("career_line");
            entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(255);
            entity.Property(e => e.MinRange).HasColumnName("min_range");
            entity.Property(e => e.Range).HasColumnName("range");
            entity.Property(e => e.CastTime).HasColumnName("cast_time");
            entity.Property(e => e.Cooldown).HasColumnName("cooldown");
            entity.Property(e => e.ApCost).HasColumnName("ap_cost");
            entity.Property(e => e.SpecialCost).HasColumnName("special_cost");
            entity.Property(e => e.MoveCast).HasColumnName("move_cast");
            entity.Property(e => e.InvokeDelay).HasColumnName("invoke_delay");
            entity.Property(e => e.EffectDelay).HasColumnName("effect_delay");
            entity.Property(e => e.EffectId).HasColumnName("effect_id");
            entity.Property(e => e.ChannelId).HasColumnName("channel_id");
            entity.Property(e => e.CooldownEntry).HasColumnName("cooldown_entry");
            entity.Property(e => e.ToggleEntry).HasColumnName("toggle_entry");
            entity.Property(e => e.CastAngle).HasColumnName("cast_angle");
            entity.Property(e => e.AbilityType).HasColumnName("ability_type");
            entity.Property(e => e.MasteryTree).HasColumnName("mastery_tree");
            entity.Property(e => e.Specline).HasColumnName("specline").HasMaxLength(255);
            entity.Property(e => e.WeaponNeeded).HasColumnName("weapon_needed");
            entity.Property(e => e.AffectsDead).HasColumnName("affects_dead");
            entity.Property(e => e.IgnoreGlobalCooldown).HasColumnName("ignore_global_cooldown");
            entity.Property(e => e.IgnoreOwnModifiers).HasColumnName("ignore_own_modifiers");
            entity.Property(e => e.Fragile).HasColumnName("fragile");
            entity.Property(e => e.MinimumRank).HasColumnName("minimum_rank");
            entity.Property(e => e.MinimumRenown).HasColumnName("minimum_renown");
            entity.Property(e => e.IconId).HasColumnName("icon_id");
            entity.Property(e => e.Category).HasColumnName("category");
            entity.Property(e => e.Flags).HasColumnName("flags");
            entity.Property(e => e.PointCost).HasColumnName("point_cost");
            entity.Property(e => e.CashCost).HasColumnName("cash_cost");
            entity.Property(e => e.StealthInteraction).HasColumnName("stealth_interaction");
            entity.Property(e => e.AiRange).HasColumnName("ai_range");
            entity.Property(e => e.IgnoreCooldownReduction).HasColumnName("ignore_cooldown_reduction");
            entity.Property(e => e.CooldownCap).HasColumnName("c_dcap");
        });
    }
}
