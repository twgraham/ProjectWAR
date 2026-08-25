using Core.Domain.ValueObjects;
using Core.GameWorld.Combat.AutoAttack;
using Core.Infrastructure.Network;
using Microsoft.Extensions.Logging;
using WorldServerV2.Network.Dtos;
using WorldServerV2.Services;

namespace WorldServerV2.Network.Handlers;

/// <summary>
/// Handles inbound combat packets (<c>F_DO_ABILITY</c>, <c>F_INTERRUPT</c>).
/// <para>
/// Follows the thin-handler pattern: decode DTO, call service, return.
/// All validation and region enqueuing happens in <see cref="CombatService"/>.
/// </para>
/// </summary>
public class CombatHandler : IPacketHandler
{
    private readonly ILogger<CombatHandler> _logger;

    public CombatHandler(ILogger<CombatHandler> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Handles <c>F_DO_ABILITY</c> (0xD5).
    /// <para>
    /// Reads the client's ability request and delegates to <see cref="CombatService"/>
    /// for validation, cast-bar feedback, and region enqueuing.
    /// </para>
    /// </summary>
    [Rpc((int)Opcodes.F_DO_ABILITY)]
    public void F_DO_ABILITY(
        DoAbilityRequest request,
        IConnectionContext context,
        [FromServices] PlayerService playerService,
        [FromServices] CombatService combatService)
    {
        var player = playerService.GetPlayer(context.Session);
        if (player is null)
        {
            _logger.LogWarning(
                "No player bound in F_DO_ABILITY for session {SessionId}",
                context.Session.Id);
            return;
        }

        if (!player.IsActive)
            return;

        combatService.TryCast(
            context.Session,
            player,
            request.AbilityId,
            request.AbilityGroup,
            request.IsEnemyVisible(),
            request.IsFriendlyVisible(),
            request.IsMoving);
    }
    
    /// <summary>
    /// Handles <c>F_PLAYER_INFO</c> (0x18) — client target change.
    /// <para>
    /// Updates the player's <see cref="Core.GameWorld.Entities.UnitEntity.CurrentTargetOid"/>
    /// and sends <c>F_SET_TARGET</c> back to the client. The target OID is a simple
    /// <c>ushort</c> field that is captured by value when enqueuing ability actions,
    /// so a mid-tick change on the handler thread cannot corrupt an in-flight cast.
    /// </para>
    /// </summary>
    [Rpc((int)Opcodes.F_PLAYER_INFO)]
    public void F_PLAYER_INFO(
        PlayerInfoRequest request,
        IConnectionContext context,
        [FromServices] PlayerService playerService,
        [FromServices] WorldService worldService)
    {
        var player = playerService.GetPlayer(context.Session);
        if (player is null)
            return;

        // Matches V1 CombatInterface_Player.SetTarget: any enemy-target change stops
        // auto-attack. The stop runs on the region thread to avoid races with the
        // auto-attack component. We only enqueue if the target actually changed.
        if (request.TargetType is TargetType.Enemy or TargetType.None
            && player.CurrentTargetOid != request.Oid)
        {
            var region = worldService.Regions.Get(player.Position.RegionId);
            region?.EnqueueAction(new StopAutoAttackAction(player.ObjectId));
        }

        player.CurrentTargetOid = request.Oid;

        // Send F_SET_TARGET acknowledgement to the client
        context.Session.SendSetTarget(new SetTargetResponse
        {
            TargetOid = request.Oid,
            PlayerOid = player.ObjectId,
            SwitchType = request.TargetType == TargetType.Enemy ? (byte)1 : (byte)0,
        });

        // TODO: Send F_INIT_EFFECTS (target buff list) when buff system
        // is fully wired. Requires reading target entity's BuffContainer
        // on the region thread via an enqueued action.
    }

    /// <summary>
    /// Handles <c>F_SWITCH_ATTACK_MODE</c> (0xDC) — client auto-attack toggle.
    /// <para>
    /// Enqueues a <see cref="ToggleAutoAttackAction"/> to the region thread, which
    /// resolves the target and toggles <see cref="AutoAttackComponent.IsAttacking"/>.
    /// Matches V1 behaviour: each packet inverts the current state.
    /// </para>
    /// </summary>
    [Rpc((int)Opcodes.F_SWITCH_ATTACK_MODE)]
    public void F_SWITCH_ATTACK_MODE(
        SwitchAttackModeRequest request,
        IConnectionContext context,
        [FromServices] PlayerService playerService,
        [FromServices] WorldService worldService)
    {
        var player = playerService.GetPlayer(context.Session);
        if (player is null)
            return;

        if (!player.IsActive)
            return;

        var region = worldService.Regions.Get(player.Position.RegionId);
        if (region is null)
        {
            _logger.LogWarning(
                "Player {Name} sent F_SWITCH_ATTACK_MODE but region {RegionId} not found",
                player.Name, player.Position.RegionId);
            return;
        }

        region.EnqueueAction(new ToggleAutoAttackAction(player.ObjectId));
    }
}
