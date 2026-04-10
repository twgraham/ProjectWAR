namespace Core.Domain.Entities;

/// <summary>
/// EF Core entity mapped to the <c>ability_commands</c> table in the World database.
/// Read-only — loaded once at startup and converted into
/// <c>AbilityCommandDefinition</c>.
/// </summary>
public sealed class AbilityCommandEntity
{
    public int Entry { get; set; }
    public short CommandId { get; set; }
    public short CommandSequence { get; set; }
    public string? CommandName { get; set; }
    public int? PrimaryValue { get; set; }
    public int? SecondaryValue { get; set; }
    public string? Target { get; set; }
    public string? EffectSource { get; set; }
    public short? EffectRadius { get; set; }
    public short? EffectAngle { get; set; }
    public short? MaxTargets { get; set; }
    public short? AttackingStat { get; set; }
    public short? IsDelayedEffect { get; set; }
    public short? FromAllTargets { get; set; }
    public short? NoAutoUse { get; set; }
}
