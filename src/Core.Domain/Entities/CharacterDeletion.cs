namespace Core.Domain.Entities;

public sealed class CharacterDeletion
{
    /// <summary>Surrogate UUID primary key.</summary>
    public Guid CharacterDeletionsId { get; set; }
    public string? DeletionIP { get; set; }
    public int? AccountID { get; set; }
    public string? AccountName { get; set; }
    public uint? CharacterID { get; set; }
    public string? CharacterName { get; set; }
    public DateTime? DeletionTimeSeconds { get; set; }
}
