namespace WorldServerV2.Services;

/// <summary>
/// Lightweight read-only projection of a character, used for cross-session lookups
/// (social features, mail recipient resolution, guild member display, GM commands).
/// <para>
/// Stored in the <see cref="ICharacterService"/>'s internal directory and returned
/// by <see cref="ICharacterService.FindByName"/> / <see cref="ICharacterService.FindById"/>.
/// </para>
/// </summary>
public readonly record struct CharacterSummary(
    uint CharacterId,
    string Name,
    byte Realm,
    byte Career,
    int AccountId);
