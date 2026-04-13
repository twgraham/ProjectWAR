using Core.Domain.ValueObjects;
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
    public void F_PLAYER_INFO(PlayerInfoRequest request, IConnectionContext context, [FromServices] PlayerService playerService)
    {
        var player = playerService.GetPlayer(context.Session);
        if (player is null)
            return;
        
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
}
