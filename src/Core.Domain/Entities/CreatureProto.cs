namespace Core.Domain.Entities;

/// <summary>
/// Creature prototype loaded from the <c>creature_protos</c> table.
/// Pure POCO — all DB column mapping is handled by <see cref="WorldDbContext"/>.
/// </summary>
public sealed class CreatureProto
{
    public uint Entry { get; set; }
    public string Name { get; set; } = string.Empty;
    public ushort Model1 { get; set; }
    public ushort Model2 { get; set; }
    public ushort MinScale { get; set; }
    public ushort MaxScale { get; set; }
    public byte MinLevel { get; set; }
    public byte MaxLevel { get; set; }
    public byte Faction { get; set; }
    public byte CreatureType { get; set; }
    public byte CreatureSubType { get; set; }
    public ushort Ranged { get; set; }
    public ushort IsWandering { get; set; }
    public byte Icone { get; set; }
    public byte Emote { get; set; }
    public ushort Title { get; set; }
    public ushort Unk { get; set; }
    public ushort Unk1 { get; set; }
    public ushort Unk2 { get; set; }
    public ushort Unk3 { get; set; }
    public ushort Unk4 { get; set; }
    public ushort Unk5 { get; set; }
    public ushort Unk6 { get; set; }
    public string Flag { get; set; } = string.Empty;
    public string? ScriptName { get; set; }
    public ushort LairBoss { get; set; }
    public ushort VendorId { get; set; }
    public string? TokUnlock { get; set; }
    public byte[]? States { get; set; }
    public byte[]? FigLeafData { get; set; }
    public int? BaseRadiusUnits { get; set; }
    public byte Career { get; set; }
    public float PowerModifier { get; set; }
    public float WoundsModifier { get; set; }
    public byte Invulnerable { get; set; }
    public ushort WeaponDps { get; set; }
    public byte ImmuneToCC { get; set; }
}
