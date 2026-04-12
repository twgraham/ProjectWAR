using Core.GameWorld.Combat.Abilities;
using Core.GameWorld.Entities;
using Core.GameWorld.Events;
using Core.GameWorld.Spatial;
using Core.Session;
using WorldServerV2.Network;
using WorldServerV2.Network.Dtos;

namespace WorldServerV2.RegionHandlers;

/// <summary>
/// Handles combat-related region events and sends ability packets to clients.
/// <para>
/// Registered as a singleton — the same instance handles all four event types.
/// Each handler resolves the caster's session and, for broadcast events, iterates
/// the caster's visibility set to notify nearby players.
/// </para>
/// <para>
/// <b>Packets sent:</b>
/// <list type="bullet">
///   <item><c>F_USE_ABILITY</c> (0xDA) — cast started, completed, cancelled → caster + nearby players</item>
///   <item><c>F_SET_ABILITY_TIMER</c> (0x7E) — cast bar, setback, cooldown → caster only</item>
///   <item><c>F_CAST_PLAYER_EFFECT</c> (0xB3) — damage/heal/defense numbers → target + nearby players</item>
/// </list>
/// </para>
/// </summary>
public class CombatRegionHandler :
    IRegionEventHandler<AbilityCastConfirmed>,
    IRegionEventHandler<AbilityCastCompleted>,
    IRegionEventHandler<AbilityCastFailed>,
    IRegionEventHandler<AbilityCooldownApplied>,
    IRegionEventHandler<DamageDealt>,
    IRegionEventHandler<EntityDied>
{
    private readonly ISessionResolver<PlayerEntity> _sessionResolver;

    public CombatRegionHandler(ISessionResolver<PlayerEntity> sessionResolver)
    {
        _sessionResolver = sessionResolver;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  CAST CONFIRMED (cast-bar/channel started)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// A cast was confirmed on the region thread. For cast-bar abilities, sends the
    /// cast-bar timer to the caster and broadcasts <c>F_USE_ABILITY</c> (state=started)
    /// to all nearby players.
    /// </summary>
    public void Handle(AbilityCastConfirmed @event)
    { 
        var caster = @event.Caster;
        var ctx = @event.Context;
        var def = ctx.Definition;
        var targetOid = ctx.Target?.ObjectId ?? 0;

        // Build the F_USE_ABILITY (started) packet — sent to caster + observers
        var useAbility = UseAbilityResponse.CastStarted(
            def.Entry,
            caster.ObjectId,
            def.EffectId,
            targetOid,
            (byte)def.Origin,
            (uint)ctx.CastTime,
            ctx.CastSequence);

        // Send to caster
        if (caster is PlayerEntity casterPlayer)
        {
            var casterSession = _sessionResolver.GetSession(casterPlayer);
            if (casterSession is not null)
            {
                casterSession.SendUseAbility(useAbility);

                // Send cast-bar timer to caster only
                casterSession.SendCastBarTimer(
                    CastBarTimerResponse.CastBar(
                        def.Entry,
                        (ushort)ctx.CastTime,
                        ctx.CastSequence));
            }
        }

        // Broadcast to nearby players
        BroadcastToVisiblePlayers(caster, useAbility);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  CAST COMPLETED
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// A cast completed execution (instant or cast-bar finished). Broadcasts
    /// <c>F_USE_ABILITY</c> (state=completed) to nearby players.
    /// </summary>
    public void Handle(AbilityCastCompleted @event)
    {
        var caster = @event.Caster;
        var ctx = @event.Context;
        var def = ctx.Definition;
        var targetOid = ctx.Target?.ObjectId ?? 0;

        var useAbility = UseAbilityResponse.CastCompleted(
            def.Entry,
            caster.ObjectId,
            def.EffectId,
            targetOid,
            (byte)def.Origin,
            ctx.CastSequence);

        // Send to caster
        if (caster is PlayerEntity casterPlayer)
        {
            var casterSession = _sessionResolver.GetSession(casterPlayer);
            casterSession?.SendUseAbility(useAbility);
        }

        // Broadcast to nearby players
        BroadcastToVisiblePlayers(caster, useAbility);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  CAST FAILED / CANCELLED
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// A cast was rejected or interrupted. Sends <c>F_USE_ABILITY</c> (state=cancelled)
    /// to the caster and broadcasts to nearby players.
    /// </summary>
    public void Handle(AbilityCastFailed @event)
    {
        var caster = @event.Caster;
        var ctx = @event.Context;
        var def = ctx.Definition;
        var targetOid = ctx.Target?.ObjectId ?? 0;

        var useAbility = UseAbilityResponse.CastCancelled(
            def.Entry,
            caster.ObjectId,
            def.EffectId,
            targetOid,
            (byte)@event.Reason,
            ctx.CastSequence);

        // Send to caster (always — they need to know the cast failed)
        if (caster is PlayerEntity casterPlayer)
        {
            var casterSession = _sessionResolver.GetSession(casterPlayer);
            casterSession?.SendUseAbility(useAbility);
        }

        // Broadcast to nearby players (so cast-bar animation stops for observers)
        BroadcastToVisiblePlayers(caster, useAbility);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  COOLDOWN APPLIED
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// A cooldown was applied after cast completion. Sends <c>F_SET_ABILITY_TIMER</c>
    /// (cooldown format) to the caster only.
    /// </summary>
    public void Handle(AbilityCooldownApplied @event)
    {
        if (@event.Caster is not PlayerEntity casterPlayer)
            return;

        var session = _sessionResolver.GetSession(casterPlayer);
        session?.SendCooldownTimer(
            CooldownTimerResponse.Cooldown(@event.AbilityEntry, (uint)@event.CooldownMs));
    }

    // ═══════════════════════════════════════════════════════════════════
    //  DAMAGE DEALT
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// An ability dealt damage (or was fully defended). Broadcasts
    /// <c>F_CAST_PLAYER_EFFECT</c> (0xB3) to the target and all nearby players,
    /// then sends <c>F_HIT_PLAYER</c> (0x14) to update health bars.
    /// If the target is a player, also sends <c>F_PLAYER_HEALTH</c> (0x05)
    /// so their own HP/AP display updates.
    /// </summary>
    public void Handle(DamageDealt @event)
    {
        CastPlayerEffectResponse packet;

        if (@event.WasDefended)
        {
            packet = CastPlayerEffectResponse.Defense(
                @event.Caster.ObjectId,
                @event.Target.ObjectId,
                @event.AbilityEntry,
                @event.DefenseType);
        }
        else
        {
            packet = CastPlayerEffectResponse.Damage(
                @event.Caster.ObjectId,
                @event.Target.ObjectId,
                @event.AbilityEntry,
                @event.CommandIndex,
                @event.Damage,
                @event.Mitigation,
                @event.Absorption,
                @event.WasCritical);
        }

        // Send to target (always — they need to see the damage number)
        if (@event.Target is PlayerEntity targetPlayer)
        {
            var targetSession = _sessionResolver.GetSession(targetPlayer);
            targetSession?.SendCastPlayerEffect(packet);
        }

        // Broadcast to all players near the target (includes caster if in range)
        BroadcastToNearbyPlayers(@event.Target, packet);

        // ── Health bar updates ───────────────────────────────────────

        // Build F_HIT_PLAYER — tells clients to update the target's health bar.
        // Health was already reduced by AbilityEffectExecutor before this event
        // was emitted, so Target.Health reflects the post-damage state.
        var hitPlayer = new HitPlayerResponse
        {
            CasterOid = @event.Caster.ObjectId,
            TargetOid = @event.Target.ObjectId,
            Health = (ushort)Math.Min(@event.Target.Health.Current, ushort.MaxValue),
            PctHealth = @event.Target.Health.Percent,
        };

        // Send F_HIT_PLAYER to the target themselves
        if (@event.Target is PlayerEntity hitTarget)
        {
            var targetSession = _sessionResolver.GetSession(hitTarget);
            if (targetSession is not null)
            {
                targetSession.SendHitPlayer(hitPlayer);

                // Also send F_PLAYER_HEALTH so the player's own HP bar updates
                targetSession.SendPlayerHealth(new PlayerHealthResponse
                {
                    Health = @event.Target.Health.Current,
                    MaxHealth = @event.Target.Health.Max,
                    ActionPoints = (ushort)Math.Max(0, hitTarget.ActionPoints),
                    MaxActionPoints = 250, // TODO: derive from stats
                });
            }
        }

        // Broadcast F_HIT_PLAYER to all nearby players (observers update
        // the target's nameplate / target-frame health bar)
        BroadcastToNearbyPlayers(@event.Target, hitPlayer);
    }
    
    public void Handle(EntityDied @event)
    {
        // If the entity that died is a player, send them a death notification.
        foreach (var entity in @event.Entity.Visibility.Entities)
        {
            if (entity is not PlayerEntity observer)
                continue;
            
            var session = _sessionResolver.GetSession(observer);
            session?.SendObjectDeath(new ObjectDeathResponse
            {
                ObjectId = @event.Entity.ObjectId
            });
        }
    } 

    // ═══════════════════════════════════════════════════════════════════
    //  HELPERS
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Sends a <see cref="UseAbilityResponse"/> to all players in the caster's visibility
    /// set, excluding the caster (who has already been sent the packet separately).
    /// </summary>
    private void BroadcastToVisiblePlayers(UnitEntity caster, UseAbilityResponse response)
    {
        foreach (var entity in caster.Visibility.Entities)
        {
            if (entity == caster)
                continue;

            if (entity is not PlayerEntity observer)
                continue;

            var session = _sessionResolver.GetSession(observer);
            session?.SendUseAbility(response);
        }
    }

    /// <summary>
    /// Sends a <see cref="CastPlayerEffectResponse"/> to all players in the
    /// <paramref name="origin"/> entity's visibility set, excluding the origin itself.
    /// </summary>
    private void BroadcastToNearbyPlayers(UnitEntity origin, CastPlayerEffectResponse response)
    {
        foreach (var entity in origin.Visibility.Entities)
        {
            if (entity == origin)
                continue;

            if (entity is not PlayerEntity observer)
                continue;

            var session = _sessionResolver.GetSession(observer);
            session?.SendCastPlayerEffect(response);
        }
    }

    /// <summary>
    /// Sends a <see cref="HitPlayerResponse"/> to all players in the
    /// <paramref name="origin"/> entity's visibility set, excluding the origin itself.
    /// </summary>
    private void BroadcastToNearbyPlayers(UnitEntity origin, HitPlayerResponse response)
    {
        foreach (var entity in origin.Visibility.Entities)
        {
            if (entity == origin)
                continue;

            if (entity is not PlayerEntity observer)
                continue;

            var session = _sessionResolver.GetSession(observer);
            session?.SendHitPlayer(response);
        }
    }
}
