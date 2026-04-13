namespace Core.Domain.Entities;

/// <summary>
/// An audit record of a character deletion, mapped to the <c>character_deletions</c> table.
/// Pure POCO — all DB column mapping is handled by <see cref="CharacterDbContext"/>.
/// </summary>
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
