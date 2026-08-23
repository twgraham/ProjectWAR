using Core.GameWorld.Combat;
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
    IRegionEventHandler<AbilityProjectileFired>,
    IRegionEventHandler<DamageDealt>,
    IRegionEventHandler<EntityDied>,
    IRegionEventHandler<AutoAttackSwing>,
    IRegionEventHandler<AutoAttackDamageDealt>,
    IRegionEventHandler<CombatStateChanged>
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

        // Mirror V1 target-OID resolution: use the caster's own OID for self-targeted
        // (Range == 0) abilities when no explicit target entity is present.
        var targetOid = ctx.Target?.ObjectId
            ?? (def.Range == 0 ? caster.ObjectId : (ushort)0);

        // Build the F_USE_ABILITY (started) packet — sent to caster + observers.
        // For instant casts CastTime == 0; the client plays the start-of-cast animation
        // gesture immediately and expects state=2 to follow.
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
                // Cast-bar timer: only relevant when CastTime > 0. Sending a
                // zero-duration timer for instants confuses the client UI.
                // V1 sends this before F_USE_ABILITY(state=1), which initializes
                // the client cast-bar state before the cast animation begins.
                if (ctx.CastTime > 0)
                    casterSession.SendCastBarTimer(
                        CastBarTimerResponse.CastBar(
                            def.Entry,
                            (ushort)ctx.CastTime,
                            ctx.CastSequence));

                casterSession.SendUseAbility(useAbility);
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
    /// <c>F_USE_ABILITY</c> (state=completed) to the caster and all nearby players.
    /// <para>
    /// The <c>EffectId</c> in the <c>F_USE_ABILITY</c> packet drives all client-side
    /// VFX and character animations. The subsequent <c>F_CAST_PLAYER_EFFECT</c> damage
    /// or defense packet's <c>ShowVisual</c> flag drives the target hit-flash.
    /// </para>
    /// </summary>
    public void Handle(AbilityCastCompleted @event)
    {
        var caster = @event.Caster;
        var ctx = @event.Context;
        var def = ctx.Definition;
        // For the animation packet, default the targetOid to the caster's own OID when
        // there is no explicit target (AoE, self-cast) so the client has a valid anchor.
        var targetOid = ctx.Target?.ObjectId ?? caster.ObjectId;

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
            // Release the caster's animation pose (V1: SetCastCompleted on forced cancel)
            casterSession?.SendCastCompletion(CastCompletionResponse.Create(
                caster.ObjectId, def.Entry));
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
    //  PROJECTILE FIRED
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// An ability fired a projectile (<c>EffectDelay != 0</c>). Sends
    /// <c>F_USE_ABILITY</c> state=6 to the caster and nearby players so the client
    /// plays the projectile flight animation. Damage is applied after
    /// <see cref="AbilityProjectileFired.FlightTimeMs"/> elapses on the server.
    /// </summary>
    public void Handle(AbilityProjectileFired @event)
    {
        var caster = @event.Caster;
        var ctx = @event.Context;
        var def = ctx.Definition;

        var packet = UseAbilityResponse.ProjectileFlight(
            def.Entry,
            caster.ObjectId,
            def.EffectId,
            ctx.Target?.ObjectId ?? 0,
            @event.FlightTimeMs,
            ctx.CastSequence);

        if (caster is PlayerEntity casterPlayer)
        {
            var casterSession = _sessionResolver.GetSession(casterPlayer);
            casterSession?.SendUseAbility(packet);
        }

        BroadcastToVisiblePlayers(caster, packet);
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

        // Send to caster (direct — caster always needs to see their own damage numbers)
        if (@event.Caster is PlayerEntity casterPlayer)
        {
            var casterSession = _sessionResolver.GetSession(casterPlayer);
            casterSession?.SendCastPlayerEffect(packet);
        }

        // Send to target (always — they need to see the damage number)
        if (@event.Target is PlayerEntity targetPlayer)
        {
            var targetSession = _sessionResolver.GetSession(targetPlayer);
            targetSession?.SendCastPlayerEffect(packet);
        }

        // Broadcast to all players near the target (includes caster if in range),
        // skipping the caster to avoid a duplicate from the direct send above.
        BroadcastToNearbyPlayers(@event.Target, packet, skip: @event.Caster);

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
    //  AUTO-ATTACK SWING (animation)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// An auto-attack swing occurred. Broadcasts <c>F_USE_ABILITY</c> with
    /// <c>abilityEntry = 0</c> (state = completed) to trigger the melee/ranged
    /// swing animation on nearby clients. Matches V1 behaviour.
    /// </summary>
    public void Handle(AutoAttackSwing @event)
    {
        var response = UseAbilityResponse.CastCompleted(
            abilityEntry: 0,
            casterOid: @event.Caster.ObjectId,
            effectId: 0,
            targetOid: @event.Target.ObjectId,
            origin: 0,
            castSequence: 0);

        // Send to caster if player
        if (@event.Caster is PlayerEntity casterPlayer)
        {
            var session = _sessionResolver.GetSession(casterPlayer);
            session?.SendUseAbility(response);
        }

        // Broadcast to nearby players
        BroadcastToVisiblePlayers(@event.Caster, response);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  AUTO-ATTACK DAMAGE
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Auto-attack damage was resolved. Broadcasts <c>F_CAST_PLAYER_EFFECT</c>
    /// and <c>F_HIT_PLAYER</c> with <c>abilityEntry = 0</c>, mirroring the
    /// ability-damage packet flow.
    /// </summary>
    public void Handle(AutoAttackDamageDealt @event)
    {
        var ctx = @event.Context;

        CastPlayerEffectResponse packet;
        if (ctx.WasDefended)
        {
            packet = CastPlayerEffectResponse.Defense(
                @event.Caster.ObjectId,
                @event.Target.ObjectId,
                abilityEntry: 0,
                ctx.DefenseType);
        }
        else
        {
            packet = CastPlayerEffectResponse.AutoAttackDamage(
                @event.Caster.ObjectId,
                @event.Target.ObjectId,
                ctx.FinalDamage,
                ctx.FinalMitigation,
                ctx.FinalAbsorption,
                ctx.WasCritical);
        }

        // Send to caster (direct — caster always needs to see their own damage numbers)
        if (@event.Caster is PlayerEntity casterPlayer)
        {
            var casterSession = _sessionResolver.GetSession(casterPlayer);
            casterSession?.SendCastPlayerEffect(packet);
        }

        // Send to target
        if (@event.Target is PlayerEntity targetPlayer && @event.Target != @event.Caster)
        {
            var targetSession = _sessionResolver.GetSession(targetPlayer);
            targetSession?.SendCastPlayerEffect(packet);
        }

        // Broadcast to nearby players, skipping the caster who already received direct.
        BroadcastToNearbyPlayers(@event.Target, packet, skip: @event.Caster);

        // ── Health bar updates ───────────────────────────────────────
        var hitPlayer = new HitPlayerResponse
        {
            CasterOid = @event.Caster.ObjectId,
            TargetOid = @event.Target.ObjectId,
            Health = (ushort)Math.Min(@event.Target.Health.Current, ushort.MaxValue),
            PctHealth = @event.Target.Health.Percent,
        };

        if (@event.Target is PlayerEntity hitTarget)
        {
            var targetSession = _sessionResolver.GetSession(hitTarget);
            if (targetSession is not null)
            {
                targetSession.SendHitPlayer(hitPlayer);
                targetSession.SendPlayerHealth(new PlayerHealthResponse
                {
                    Health = @event.Target.Health.Current,
                    MaxHealth = @event.Target.Health.Max,
                    ActionPoints = (ushort)Math.Max(0, hitTarget.ActionPoints),
                    MaxActionPoints = 250, // TODO: derive from stats
                });
            }
        }

        BroadcastToNearbyPlayers(@event.Target, hitPlayer);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  COMBAT STATE CHANGED
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// A unit entered or left combat. Broadcasts <c>F_UPDATE_STATE</c>
    /// (StateOpcode 0x1A) to the entity (if player) and all nearby players.
    /// </summary>
    public void Handle(CombatStateChanged @event)
    {
        var response = UpdateStateResponse.Combat(
            @event.Entity.ObjectId,
            @event.InCombat);

        // Send to self if player
        if (@event.Entity is PlayerEntity player)
        {
            var session = _sessionResolver.GetSession(player);
            session?.SendUpdateState(response);
        }

        // Broadcast to nearby players
        BroadcastToNearbyPlayers(@event.Entity, response);
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
    private void BroadcastToNearbyPlayers(UnitEntity origin, CastPlayerEffectResponse response,
        UnitEntity? skip = null)
    {
        foreach (var entity in origin.Visibility.Entities)
        {
            if (entity == origin || entity == skip)
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

    /// <summary>
    /// Sends an <see cref="UpdateStateResponse"/> to all players in the
    /// <paramref name="origin"/> entity's visibility set, excluding the origin itself.
    /// </summary>
    private void BroadcastToNearbyPlayers(UnitEntity origin, UpdateStateResponse response)
    {
        foreach (var entity in origin.Visibility.Entities)
        {
            if (entity == origin)
                continue;

            if (entity is not PlayerEntity observer)
                continue;

            var session = _sessionResolver.GetSession(observer);
            session?.SendUpdateState(response);
        }
    }
}
