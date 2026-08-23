using System.Collections.Concurrent;
using Core.Domain;
using Core.Domain.Entities;
using Core.GameWorld.DataStore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WorldServerV2.Data;
using WorldServerV2.Data.Models;

namespace WorldServerV2.Services;

/// <summary>
/// Singleton service that provides character persistence and a lightweight
/// in-memory directory for cross-session name/ID lookups.
/// <para>
/// <b>Directory</b>: At startup, <see cref="LoadDirectoryAsync"/> populates two
/// concurrent dictionaries with <see cref="CharacterSummary"/> projections (name→summary
/// and id→summary). These are updated automatically when characters are created,
/// deleted, or renamed. Cross-session consumers (social, mail, guild, GM commands)
/// use <see cref="FindByName"/>/<see cref="FindById"/> for O(1) lookups with zero DB cost.
/// </para>
/// <para>
/// <b>Session data</b>: Full <see cref="Character"/> entities are loaded per-account
/// via <see cref="GetCharactersForAccountAsync"/> and stored on
/// <see cref="Network.GameSession.Characters"/> by the handler. No singleton cache
/// of full entities — the session owns its own character state.
/// </para>
/// </summary>
internal sealed class CharacterService : ICharacterService, IHostedService
{
    private readonly IDbContextFactory<CharacterDbContext> _dbContextFactory;
    private readonly GameDataStore _gameDataStore;
    private readonly ILogger<CharacterService> _logger;

    // ── Directory: lightweight projections for cross-session lookups ─
    private readonly ConcurrentDictionary<string, CharacterSummary> _byName =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<uint, CharacterSummary> _byId = new();

    public CharacterService(
        IDbContextFactory<CharacterDbContext> dbContextFactory,
        GameDataStore gameDataStore,
        ILogger<CharacterService> logger)
    {
        _dbContextFactory = dbContextFactory;
        _gameDataStore = gameDataStore;
        _logger = logger;
    }
    
    /// <inheritdoc />
    public async Task<IReadOnlyList<Character>> GetCharactersForAccountAsync(uint accountId)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var characters = await db.Characters
            .Where(c => c.AccountId == (int)accountId)
            .Include(c => c.Value)
            .Include(c => c.Items)
            .AsNoTracking()
            .ToListAsync();

        _logger.LogDebug(
            "Loaded {Count} character(s) for account {AccountId}",
            characters.Count, accountId);

        return characters;
    }
    
    /// <inheritdoc />
    public CharacterSummary? FindByName(string name)
        => _byName.TryGetValue(name, out var summary) ? summary : null;

    /// <inheritdoc />
    public CharacterSummary? FindById(uint characterId)
        => _byId.TryGetValue(characterId, out var summary) ? summary : null;

    /// <inheritdoc />
    public async Task LoadDirectoryAsync()
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var summaries = await db.Characters
            .AsNoTracking()
            .Select(c => new CharacterSummary(
                c.CharacterId, c.Name, c.Realm, c.Career, c.AccountId))
            .ToListAsync();

        _byName.Clear();
        _byId.Clear();

        foreach (var summary in summaries)
        {
            _byName[summary.Name] = summary;
            _byId[summary.CharacterId] = summary;
        }

        _logger.LogInformation(
            "Character directory loaded: {Count} entries", summaries.Count);
    }

    public async Task CreateCharacterAsync(uint accountId, ushort realmId, NewCharacter model)
    {
        var character = model.ToEntity(accountId, realmId, _gameDataStore.Classes);
        await using var db = await _dbContextFactory.CreateDbContextAsync();
        db.Characters.Add(character);
        
        await db.SaveChangesAsync();
        
        var summary = new CharacterSummary(character.CharacterId, character.Name, character.Realm, character.Career, (int)accountId);
        
        _byId[character.CharacterId] = summary;
        _byName[character.Name] = summary;
    }

    public async Task DeleteCharacterAsync(Character character)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();
        db.Characters.Remove(character);
        await db.SaveChangesAsync();
        
        _byId.TryRemove(character.CharacterId, out _);
        _byName.TryRemove(character.Name, out _);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        return LoadDirectoryAsync();
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
