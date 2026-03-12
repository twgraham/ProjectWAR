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
    public DbSet<ItemInfo> ItemInfos => Set<ItemInfo>();
    public DbSet<CreatureProto> CreatureProtos => Set<CreatureProto>();
    public DbSet<CreatureSpawn> CreatureSpawns => Set<CreatureSpawn>();
    public DbSet<ZoneInfo> ZoneInfos => Set<ZoneInfo>();
    public DbSet<ZoneJump> ZoneJumps => Set<ZoneJump>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureItemInfo(modelBuilder);
        ConfigureCreatureProto(modelBuilder);
        ConfigureCreatureSpawn(modelBuilder);
        ConfigureZoneInfo(modelBuilder);
        ConfigureZoneJump(modelBuilder);
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
}
