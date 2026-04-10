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
    
    [Rpc((int)Opcodes.F_PLAYER_INFO)]
    public void F_PLAYER_INFO(PlayerInfoRequest request, IConnectionContext context, [FromServices] PlayerService playerService)
    {
        //cclient.Plr.DebugMessage("F_PLAYER_INFO: SetTarget: "+Oid);
        // if (request.TargetType == (byte)TargetTypes.TARGETTYPES_TARGET_SELF)
        //     TargetType = (byte)TargetTypes.TARGETTYPES_TARGET_ALLY;

        var player = playerService.GetPlayer(context.Session);
        player.CurrentTargetOid = request.Oid;
        /*cclient.Plr.CbtInterface.SetTarget(Oid, (TargetTypes)TargetType);

        if (LOS == 0)
            cclient.Plr.AbtInterface.Cancel(true, (ushort)AbilityResult.ABILITYRESULT_NOTVISIBLECLIENT);*/
    }
}
