using Microsoft.EntityFrameworkCore;
using WorldServerV2.Data;
using WorldServerV2.Data.Entities;

namespace WorldServerV2.Services;

/// <summary>
/// Stub implementation of <see cref="ICharacterService"/>.
/// Will be replaced with a full database-backed implementation in System 5 (Character Management).
/// </summary>
internal sealed class CharacterService : ICharacterService
{
    private readonly IDbContextFactory<WorldDbContext> _dbContextFactory;

    public CharacterService(IDbContextFactory<WorldDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public void LoadCharactersForAccount(uint accountId)
    {
        // TODO: Load from database via CharacterRepository
    }

    public byte GetAccountRealm(uint accountId) => 0;

    public Character? GetCharacterBySlot(uint accountId, byte slot) => null;
    
    public async Task<List<Character>> GetCharactersForAccount(uint accountId)
    {
        var ctx = await _dbContextFactory.CreateDbContextAsync();
        throw new NotImplementedException();
    }
}
