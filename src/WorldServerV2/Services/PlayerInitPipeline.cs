using Microsoft.Extensions.Logging;
using WorldServerV2.Network;
using WorldServerV2.Services.PlayerInit;
using WorldServerV2.World.Entities;

namespace WorldServerV2.Services;

/// <summary>
/// Orchestrates the player initialization sequence that transitions a client from the
/// character screen into the live game world.
/// <para>
/// <b>Responsibilities</b>:
/// <list type="number">
///   <item>Calls <see cref="PlayerEntity.InitializeFromCharacter"/> to hydrate entity
///         state from the persistent DB record (Phase B — compute).</item>
///   <item>Iterates through registered <see cref="IPlayerInitStep"/> implementations in
///         order, each of which reads from the initialized entity and sends one or more
///         packets to the client (Phase C — serialize/send).</item>
/// </list>
/// </para>
/// <para>
/// <b>Threading</b>: <see cref="Initialize"/> is invoked on the <b>handler thread</b>
/// after the caller has reserved an OID via <c>Region.ReserveOid()</c>. This keeps
/// all computation and packet serialization off the region tick loop.
/// <see cref="GameSession.Send{T}"/> is thread-safe (channel-based send queue), so
/// packets can be enqueued from any thread.
/// </para>
/// <para>
/// <b>Packet sequence</b> (minimum viable set, determined by step registration order):
/// <list type="number">
///   <item><c>F_MAX_VELOCITY</c> (0x1E) — movement speed</item>
///   <item><c>S_PLAYER_INITTED</c> (0x88) — identity, position, realm, career</item>
///   <item><c>F_PLAYER_STATS</c> (0x46) — base stats</item>
///   <item><c>F_PLAYER_HEALTH</c> (0x05) — HP, AP, morale</item>
///   <item><c>S_PLAYER_LOADED</c> (0x89) — data-complete marker</item>
///   <item><c>F_MAX_VELOCITY</c> (0x1E) — speed (sent twice, matching old server)</item>
///   <item><c>F_PLAYER_STATS</c> (0x46) — stats (sent twice, matching old server)</item>
/// </list>
/// <c>F_PLAYER_INIT_COMPLETE</c> (0xEF) is sent by the caller (<c>CharacterScreenHandler</c>)
/// after the entity has been placed in the region via <c>Region.AddAsync()</c>.
/// </para>
/// Registered as a <b>singleton</b> in the DI container.
/// </summary>
public sealed class PlayerInitPipeline
{
    private readonly ILogger<PlayerInitPipeline> _logger;
    private readonly IReadOnlyList<IPlayerInitStep> _steps;

    public PlayerInitPipeline(ILogger<PlayerInitPipeline> logger, IEnumerable<IPlayerInitStep> steps)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _steps = (steps ?? throw new ArgumentNullException(nameof(steps))).ToArray();
    }

    /// <summary>
    /// Executes Phase B (compute mutable state) and Phase C (serialize and send packets)
    /// for the given player. Called on the handler thread after the entity has been
    /// assigned a pre-reserved OID via <c>Region.ReserveOid()</c>.
    /// </summary>
    /// <param name="player">The player entity with a pre-assigned OID.</param>
    /// <param name="session">The player's network session for sending packets.</param>
    public void Initialize(PlayerEntity player, GameSession session)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(session);

        // ── Phase B: Compute mutable entity state from DB record ────────
        player.InitializeFromCharacter();

        _logger.LogDebug(
            "Phase B complete for {Name} (OID {Oid}): Level={Level}, Realm={Realm}, " +
            "HP={Hp}/{MaxHp}",
            player.Name, player.ObjectId, player.Level, player.Realm,
            player.Health.Current, player.Health.Max);

        // ── Phase C: Execute init steps (serialize and send packets) ────
        foreach (var step in _steps)
        {
            step.Execute(player, session);
        }

        // F_PLAYER_INIT_COMPLETE and state transition to Playing are handled by the
        // caller (CharacterScreenHandler) after the entity has been placed in the region.

        _logger.LogInformation(
            "Player {Name} ({CharId}, OID {Oid}) init packets sent — awaiting placement",
            player.Name, player.CharacterId, player.ObjectId);
    }
}
