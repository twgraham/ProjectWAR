namespace Core.Domain.Entities;

/// <summary>
/// A character's known ability and its last cast time, mapped to the <c>character_abilities</c> table.
/// Pure POCO — all DB column mapping is handled by <see cref="CharacterDbContext"/>.
/// </summary>
public sealed class CharacterAbility
{
    /// <summary>Surrogate UUID primary key.</summary>
    public Guid CharacterAbilitiesId { get; set; }
    public int? CharacterID { get; set; }
    public int? AbilityID { get; set; }
    public int? LastCast { get; set; }
}
