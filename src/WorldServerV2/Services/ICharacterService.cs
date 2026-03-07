namespace WorldServerV2.Services;

public interface ICharacterService
{
    void LoadCharactersForAccount(uint accountId);
    byte GetAccountRealm(uint accountId);
}