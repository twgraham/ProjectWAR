namespace WorldServerV2.Data.Entities;

/// <summary>
/// EF Core entity mapped to the <c>abilities</c> table in the World database.
/// Read-only — loaded once at startup and converted into <see cref="WorldServerV2.World.Combat.Abilities.AbilityDefinition"/>.
/// </summary>
public sealed class AbilityInfoEntity
{
    public int Entry { get; set; }
    public uint? CareerLine { get; set; }
    public string? Name { get; set; }
    public short? MinRange { get; set; }
    public int? Range { get; set; }
    public int? CastTime { get; set; }
    public int? Cooldown { get; set; }
    public short? ApCost { get; set; }
    public short? SpecialCost { get; set; }
    public short? MoveCast { get; set; }
    public int? InvokeDelay { get; set; }
    public short? EffectDelay { get; set; }
    public int EffectId { get; set; }
    public int? ChannelId { get; set; }
    public int? CooldownEntry { get; set; }
    public int? ToggleEntry { get; set; }
    public int? CastAngle { get; set; }
    public short? AbilityType { get; set; }
    public short? MasteryTree { get; set; }
    public string? Specline { get; set; }
    public short? WeaponNeeded { get; set; }
    public short? AffectsDead { get; set; }
    public short? IgnoreGlobalCooldown { get; set; }
    public short? IgnoreOwnModifiers { get; set; }
    public short? Fragile { get; set; }
    public short? MinimumRank { get; set; }
    public short? MinimumRenown { get; set; }
    public int? IconId { get; set; }
    public short? Category { get; set; }
    public int? Flags { get; set; }
    public short? PointCost { get; set; }
    public ushort? CashCost { get; set; }
    public int? StealthInteraction { get; set; }
    public int? AiRange { get; set; }
    public int? IgnoreCooldownReduction { get; set; }
    public int? CooldownCap { get; set; }
}
