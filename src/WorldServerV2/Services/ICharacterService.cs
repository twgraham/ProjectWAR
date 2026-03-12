using WorldServerV2.Data.Entities;

namespace WorldServerV2.Services;

public interface ICharacterService
{
    void LoadCharactersForAccount(uint accountId);
    byte GetAccountRealm(uint accountId);

    /// <summary>
    /// Retrieves the <see cref="Character"/> for the given account and character slot,
    /// or <c>null</c> if no character exists in that slot.
    /// </summary>
    Character? GetCharacterBySlot(uint accountId, byte slot);
    
    Task<List<Character>> GetCharactersForAccount(uint accountId);
}