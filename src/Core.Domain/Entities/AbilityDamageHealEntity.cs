namespace Core.Domain.Entities;

public sealed class AbilityDamageHealEntity
{
    public int Entry { get; set; }
    public int? DisplayEntry { get; set; }
    public short Index { get; set; }
    public string? Name { get; set; }
    public int? MinDamage { get; set; }
    public int? MaxDamage { get; set; }
    public int? DamageVariance { get; set; }
    public string? DamageType { get; set; }
    public short ParentCommandId { get; set; }
    public short ParentCommandSequence { get; set; }
    public float? CastTimeDamageMult { get; set; }
    public string? WeaponDamageFrom { get; set; }
    public float? WeaponDamageScale { get; set; }
    public short? NoCrits { get; set; }
    public short? Undefendable { get; set; }
    public short? OverrideDefenseEvent { get; set; }
    public short? StatUsed { get; set; }
    public float? StatDamageScale { get; set; }
    public short? ResourceBuild { get; set; }
    public short? CastPlayerSubId { get; set; }
    public float? ArmorResistPenFactor { get; set; }
    public float? HatredScale { get; set; }
    public float HealHatredScale { get; set; } = 1f;
    public float? PriStatMultiplier { get; set; }
}
