using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Core.Infrastructure.Network;

namespace WorldServerV2.Network;

/// <summary>
/// Thread-safe registry of all active <see cref="GameSession"/> instances, with O(1)
/// lookup by session ID or account ID.
/// <para>
/// <b>Thread-safety model:</b> A single <see cref="Lock"/> serializes all compound write
/// operations (<see cref="CreateSession"/>, <see cref="SetSessionAccount"/>, <see cref="Remove"/>)
/// to guarantee cross-dictionary atomicity. Read methods (<see cref="FindBySessionId"/>,
/// <see cref="FindByAccountId"/>) are lock-free — <see cref="ConcurrentDictionary{TKey,TValue}"/>
/// provides safe concurrent reads even while writes are in progress behind the lock.
/// </para>
/// <para>
/// Session IDs are <c>ushort</c> (1–65535) as required by the client protocol. ID 0 is
/// reserved ("no session"). When a session disconnects, its ID is returned to a free pool
/// for reuse — both allocation and recycling are O(1).
/// </para>
/// <para>
/// Mutations (connect / disconnect / auth) are infrequent relative to reads,
/// so the single lock introduces no meaningful contention.
/// </para>
/// Registered as a <b>singleton</b> in the DI container.
/// </summary>
public sealed class SessionRegistry
{
    /// <summary>
    /// Maximum number of concurrent sessions (ushort range minus the reserved zero ID).
    /// </summary>
    public const int MaxSessionId = ushort.MaxValue; // 65535

    private readonly ConcurrentDictionary<ushort, GameSession> _bySessionId = new();
    private readonly ConcurrentDictionary<uint, GameSession> _byAccountId = new();
    private readonly Lock _writeLock = new();

    /// <summary>
    /// Pool of available session IDs. Pre-populated with 1–65535 (ID 0 is reserved).
    /// Protected by <see cref="_writeLock"/> — a plain <see cref="Stack{T}"/> is sufficient.
    /// </summary>
    private readonly Stack<ushort> _freeIds;

    public SessionRegistry()
    {
        // Pre-populate the free pool with IDs 65535 → 1 so that Pop() yields 1 first.
        _freeIds = new Stack<ushort>(MaxSessionId);
        for (var id = MaxSessionId; id >= 1; id--)
            _freeIds.Push((ushort)id);
    }

    // ── Write operations (serialized) ───────────────────────────────────

    /// <summary>
    /// Creates a new <see cref="GameSession"/> for the given connection and registers it.
    /// The session is also stored in <see cref="IConnectionContext.Items"/> so packet handlers
    /// can retrieve it via the <c>context.Session</c> extension property.
    /// </summary>
    /// <param name="connection">The newly accepted connection.</param>
    /// <returns>The newly created session.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no session IDs are available (server at capacity).
    /// </exception>
    internal GameSession CreateSession(IConnectionContext connection)
    {
        lock (_writeLock)
        {
            if (_freeIds.Count == 0)
                throw new InvalidOperationException(
                    "No session IDs available — server has reached the maximum of 65535 concurrent sessions.");

            var sessionId = _freeIds.Pop();
            var session = new GameSession(sessionId, connection);

            _bySessionId[sessionId] = session;

            // Stash on the connection so handlers can reach it via the extension property.
            connection.Items[GameSession.ItemKey] = session;

            return session;
        }
    }

    /// <summary>
    /// Associates an authenticated <see cref="AccountInfo"/> with a session and indexes it
    /// for O(1) lookup by account ID.
    /// <para>
    /// If another session already holds this account (duplicate login), the old session is
    /// <b>displaced</b> and returned so the caller can disconnect it. The new session always
    /// claims the account.
    /// </para>
    /// </summary>
    /// <param name="session">The session that just authenticated.</param>
    /// <param name="account">The authenticated account.</param>
    /// <returns>
    /// The previously registered session for <paramref name="account"/>, or <c>null</c>
    /// if no conflict existed.
    /// </returns>
    public GameSession? SetSessionAccount(GameSession session, AccountInfo account)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(account);

        lock (_writeLock)
        {
            GameSession? displaced = null;

            if (_byAccountId.TryGetValue(account.Id, out var existing) && existing != session)
                displaced = existing;

            session.Account = account;
            _byAccountId[account.Id] = session;

            return displaced;
        }
    }

    /// <summary>
    /// Removes a session from all indexes. Safe to call multiple times for the same session.
    /// <para>
    /// The account index entry is only removed if it still points to <paramref name="session"/>
    /// — if another session has already claimed the same account (via <see cref="SetSessionAccount"/>),
    /// that entry is left intact.
    /// </para>
    /// </summary>
    public void Remove(GameSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        lock (_writeLock)
        {
            if (!_bySessionId.TryRemove(session.Id, out _))
                return; // Already removed — don't double-return the ID.

            // Return the session ID to the free pool for reuse.
            _freeIds.Push(session.Id);

            if (session.Account is { } account)
            {
                // Only remove from the account index if it still points to this session.
                // Another session may have already claimed the account via SetSessionAccount.
                _byAccountId.TryRemove(
                    new KeyValuePair<uint, GameSession>(account.Id, session));
            }
        }
    }

    // ── Lock-free reads ─────────────────────────────────────────────────

    /// <summary>O(1) lookup by session ID.</summary>
    public GameSession? FindBySessionId(ushort sessionId)
        => _bySessionId.TryGetValue(sessionId, out var session) ? session : null;

    /// <summary>O(1) lookup by account ID.</summary>
    public GameSession? FindByAccountId(uint accountId)
        => _byAccountId.TryGetValue(accountId, out var session) ? session : null;

    /// <summary>The number of active sessions.</summary>
    public int Count => _bySessionId.Count;

    /// <summary>The number of session IDs available in the free pool.</summary>
    public int AvailableIdCount
    {
        get { lock (_writeLock) return _freeIds.Count; }
    }

    /// <summary>
    /// Enumerates all active sessions. The returned sequence is a snapshot of the
    /// dictionary's values at the time of enumeration.
    /// </summary>
    public IEnumerable<GameSession> ActiveSessions => _bySessionId.Values;
}
