namespace Core.Domain.Entities;

public sealed class CharacterClientData
{
    public uint CharacterId { get; set; }

    /// <summary>Base-64 encoded client data blob.</summary>
    public string ClientDataString { get; set; } = string.Empty;
}
