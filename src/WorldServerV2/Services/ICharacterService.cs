using WorldServerV2.Data.Entities;
using WorldServerV2.Data.Models;

namespace WorldServerV2.Services;

/// <summary>
/// Unified service for character persistence, lightweight cross-session lookups,
/// and offline targeted writes.
/// <para>
/// Registered as a <b>singleton</b>. Uses <c>IDbContextFactory&lt;CharacterDbContext&gt;</c>
/// internally for DB access — no repository layer. Maintains an internal
/// <see cref="CharacterSummary"/> directory for O(1) name/ID lookups; session-scoped
/// character data lives on <see cref="Network.GameSession.Characters"/> instead of
/// a singleton cache.
/// </para>
/// </summary>
public interface ICharacterService
{
    // ── Session-scoped: called during character select ──────────────

    /// <summary>
    /// Loads all characters for the given account from the database.
    /// The caller (handler) stores the result on the <see cref="Network.GameSession"/>.
    /// </summary>
    Task<IReadOnlyList<Character>> GetCharactersForAccountAsync(uint accountId);

    // ── Lightweight projections: cross-session lookups ──────────────

    /// <summary>
    /// Finds a character summary by exact name (case-insensitive).
    /// Returns <c>null</c> if no character with that name exists.
    /// Reads from the internal projection cache — no DB hit.
    /// </summary>
    CharacterSummary? FindByName(string name);

    /// <summary>
    /// Finds a character summary by character ID.
    /// Returns <c>null</c> if no character with that ID exists.
    /// Reads from the internal projection cache — no DB hit.
    /// </summary>
    CharacterSummary? FindById(uint characterId);

    /// <summary>
    /// Populates the internal projection directory from the database.
    /// Called once at startup (via hosted service or manual initialization).
    /// </summary>
    Task LoadDirectoryAsync();

    Task CreateCharacterAsync(uint accountId, ushort realmId, NewCharacter request);
}