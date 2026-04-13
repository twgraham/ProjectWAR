namespace Core.Domain.Entities;

/// <summary>
/// Client UI state data for a character, mapped to the <c>character_client_data</c> table.
/// Pure POCO — all DB column mapping is handled by <see cref="CharacterDbContext"/>.
/// </summary>
public sealed class CharacterClientData
{
    public uint CharacterId { get; set; }

    /// <summary>Base-64 encoded client data blob.</summary>
    public string ClientDataString { get; set; } = string.Empty;
}
