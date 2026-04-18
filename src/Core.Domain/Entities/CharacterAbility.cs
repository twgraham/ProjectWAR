namespace Core.Domain.Entities;

public sealed class CharacterAbility
{
    /// <summary>Surrogate UUID primary key.</summary>
    public Guid CharacterAbilitiesId { get; set; }
    public int? CharacterID { get; set; }
    public int? AbilityID { get; set; }
    public int? LastCast { get; set; }
}
