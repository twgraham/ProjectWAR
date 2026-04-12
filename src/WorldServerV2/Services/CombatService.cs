using Core.GameWorld.Combat.Abilities;
using Core.GameWorld.DataStore;
using Core.GameWorld.Entities;
using Core.GameWorld.Spatial;
using Core.Session;
using Microsoft.Extensions.Logging;
using WorldServerV2.Network;
using WorldServerV2.Network.Dtos;

namespace WorldServerV2.Services;

/// <summary>
/// Bidirectional combat service — bridges network handlers to the region-thread
/// ability lifecycle.
/// <para>
/// <b>Inbound</b> (handler thread → region): validates the cast request, enqueues a
/// <see cref="BeginCastAction"/> to the region for authoritative execution.
/// </para>
/// <para>
/// <b>Thread model</b>: <see cref="TryCast"/> runs on the handler thread.
/// All entity mutations happen inside the <see cref="BeginCastAction"/> on the
/// region thread. The region dispatches events (<c>AbilityCastConfirmed</c>, etc.)
/// which are handled by <see cref="RegionHandlers.CombatRegionHandler"/> to send
/// response packets back to clients.
/// </para>
/// Registered as a <b>singleton</b>.
/// </summary>
public sealed class CombatService
{
    private readonly IGameDataStore _gameData;
    private readonly WorldService _worldService;
    private readonly ILogger<CombatService> _logger;

    public CombatService(
        IGameDataStore gameData,
        WorldService worldService,
        ILogger<CombatService> logger)
    {
        _gameData = gameData ?? throw new ArgumentNullException(nameof(gameData));
        _worldService = worldService ?? throw new ArgumentNullException(nameof(worldService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Attempts to initiate an ability cast for a player.
    /// <para>
    /// Called on the handler thread from <see cref="Network.Handlers.CombatHandler"/>.
    /// Performs lightweight validation (ability lookup, region lookup) then enqueues
    /// a <see cref="BeginCastAction"/> to the region for authoritative validation and
    /// execution — all entity mutations happen on the region thread.
    /// </para>
    /// </summary>
    /// <param name="session">The caster's game session.</param>
    /// <param name="player">The caster entity.</param>
    /// <param name="abilityId">Ability entry ID from the client packet.</param>
    /// <param name="abilityGroup">Ability group (used for morale tier, etc.).</param>
    /// <param name="enemyVisible">Whether enemies are visible to the caster.</param>
    /// <param name="friendlyVisible">Whether allies are visible to the caster.</param>
    /// <param name="isMoving">Whether the caster is currently moving.</param>
    public void TryCast(
        IGameSession session,
        PlayerEntity player,
        ushort abilityId,
        byte abilityGroup,
        bool enemyVisible,
        bool friendlyVisible,
        bool isMoving)
    {
        // 1. Look up ability definition — lightweight, immutable data
        var definition = _gameData.Abilities.GetByEntry(abilityId);
        if (definition is null)
        {
            _logger.LogWarning(
                "Player {Name} tried unknown ability {AbilityId}",
                player.Name, abilityId);
            return;
        }

        var currentTarget = player.CurrentTargetOid;
        if (currentTarget is null)
        {
            _logger.LogDebug(
                "Player {Name} tried to cast {AbilityId} with no target",
                player.Name, abilityId);
            return;
        }

        // 2. Movement check (handler thread — advisory, saves a round-trip to region)
        if (isMoving && !definition.CanCastWhileMoving && definition.CastTime > 0)
        {
            _logger.LogDebug(
                "Player {Name} cannot cast {AbilityId} while moving",
                player.Name, abilityId);
            session.SendUseAbility(UseAbilityResponse.CastCancelled(
                abilityId,
                player.ObjectId,
                definition.EffectId,
                currentTarget.Value,
                (byte)AbilityFailure.Moving,
                castSequence: 0));
            return;
        }

        // 3. Resolve the region the caster is in
        var region = _worldService.Regions.Get(player.Position.RegionId);
        if (region is null)
        {
            _logger.LogWarning(
                "Player {Name} tried to cast but region {RegionId} not found",
                player.Name, player.Position.RegionId);
            return;
        }

        // 4. Enqueue authoritative cast action to the region thread.
        //    The BeginCastAction will call AbilityComponent.TryInitiate + ConfirmCast,
        //    resolve the target entity, and dispatch region events on success/failure.
        var action = new BeginCastAction(
            player.ObjectId,
            currentTarget.Value,
            definition);

        region.EnqueueAction(action);

        _logger.LogDebug(
            "Enqueued BeginCastAction for {Name} — ability {AbilityId} → region {RegionId}",
            player.Name, abilityId, region.RegionId);
    }
}
