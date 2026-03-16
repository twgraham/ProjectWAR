using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WorldServerV2.Data;
using WorldServerV2.Data.Entities;
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
internal sealed class CharacterService : ICharacterService
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

    // ── Session-scoped ──────────────────────────────────────────────

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

    // ── Lightweight projections ─────────────────────────────────────

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
        var classInfo = _gameDataStore.Classes.Infos[model.Class];
        var classItems = _gameDataStore.Classes.Items[model.Class];

        var character = new Character {
            AccountId = (int)accountId,
            SlotId = model.Slot,
            Name = model.Name,
            Race = (byte)model.Race,
            Sex = (byte)model.Sex,
            Career = (byte)model.Class,
            Traits = model.Traits,
            ModelId =  model.Model,
            Realm = (byte)model.Race.GetFaction(),
            RealmId = realmId,
            FirstConnect = true,
            Value = new CharacterValue
            {
                Level = 1,
                Money = 2000,
                Online = false,
                RallyPoint = classInfo.RallyPt,
                RegionId = classInfo.Region,
                ZoneId = classInfo.ZoneId,
                Renown = 0,
                RenownRank = 1,
                RestXp = 0,
                Skills = classInfo.Skills,
                Speed = 100,
                PlayedTime = 0,
                WorldX =  classInfo.WorldX,
                WorldY = classInfo.WorldY,
                WorldZ = classInfo.WorldZ,
                WorldO =  classInfo.WorldO,
            },
            Items = classItems.Select(x => new CharacterItem
            {
                Entry = x.Entry,
                SlotId = x.SlotId,
                ModelId = x.ModelId,
                Counts = x.Count,
            }).ToList()
        };
        await using var db = await _dbContextFactory.CreateDbContextAsync();
        db.Characters.Add(character);
        
        await db.SaveChangesAsync();
    }
}
