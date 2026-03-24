using WorldServerV2.World.Entities;

namespace WorldServerV2.World.Combat.Buffs.Effects;

/// <summary>
/// Absorbs incoming damage up to a shield value, reducing damage taken.
/// <para>
/// <see cref="BuffEffectDefinition.PrimaryValue"/> — shield HP at level 1.<br/>
/// <see cref="BuffEffectDefinition.SecondaryValue"/> — shield HP at level 40.<br/>
/// Subscribes to <see cref="CombatEventType.ReceivingDamage"/> at
/// <see cref="CombatEventPriority.AbsorbShield"/>. When damage arrives,
/// absorbs up to the remaining shield value. Expires when depleted.
/// </para>
/// </summary>
public sealed class AbsorbShieldEffect : IBuffEffect
{
    public BuffEffectDefinition Definition { get; }

    /// <summary>
    /// Index into <see cref="Buff.ShieldValues"/> for this effect's remaining HP.
    /// Set during <see cref="OnStart"/>.
    /// </summary>
    private int _shieldIndex = -1;

    public AbsorbShieldEffect(BuffEffectDefinition definition)
    {
        Definition = definition;
    }

    public void OnStart(Buff buff, UnitEntity target)
    {
        // Resolve shield HP via level interpolation.
        int lo = Definition.PrimaryValue;
        int hi = Definition.SecondaryValue;
        byte level = (buff.Caster ?? target).Level;
        float t = Math.Clamp((level - 1) / 39f, 0f, 1f);
        float shieldHp = (lo + (hi - lo) * t) * buff.StackLevel;

        // Find our index in the buff's effect list.
        _shieldIndex = FindEffectIndex(buff);
        if (_shieldIndex < 0) return;

        // Store shield value. ShieldValues array is pre-allocated by BuffContainer.
        buff.ShieldValues ??= new float[buff.Effects.Length];
        buff.ShieldValues[_shieldIndex] = Math.Max(0, shieldHp);
    }

    public void OnTick(Buff buff, UnitEntity target, long tick) { }

    public void OnEnd(Buff buff, UnitEntity target)
    {
        // Clear shield value on removal.
        if (_shieldIndex >= 0 && buff.ShieldValues is not null
            && _shieldIndex < buff.ShieldValues.Length)
        {
            buff.ShieldValues[_shieldIndex] = 0;
        }
    }

    public void OnCombatEvent(Buff buff, CombatEventType eventType,
        DamageContext? context, UnitEntity? instigator)
    {
        if (context is null || _shieldIndex < 0) return;
        if (buff.ShieldValues is null || _shieldIndex >= buff.ShieldValues.Length) return;

        // Skip raw damage — shields don't absorb it (matches V1 behavior).
        if (context.DamageType == DamageType.RawDamage) return;

        float remaining = buff.ShieldValues[_shieldIndex];
        if (remaining <= 0) return;

        float damage = context.Damage;
        if (damage <= 0) return;

        if (damage >= remaining)
        {
            // Shield depleted — absorb what's left, expire the buff.
            context.Damage -= remaining;
            context.Absorption += remaining;
            buff.ShieldValues[_shieldIndex] = 0;
            buff.FlagExpired();
        }
        else
        {
            // Shield has capacity remaining.
            context.Damage = 0;
            context.Absorption += damage;
            buff.ShieldValues[_shieldIndex] -= damage;
        }
    }

    // ── Internals ────────────────────────────────────────────────────

    private int FindEffectIndex(Buff buff)
    {
        for (int i = 0; i < buff.Effects.Length; i++)
        {
            if (ReferenceEquals(buff.Effects[i], this))
                return i;
        }
        return -1;
    }

    /// <summary>Gets the remaining shield HP (for tooltip/diagnostics).</summary>
    internal float GetRemainingShield(Buff buff)
    {
        if (_shieldIndex < 0 || buff.ShieldValues is null ||
            _shieldIndex >= buff.ShieldValues.Length)
            return 0;

        return buff.ShieldValues[_shieldIndex];
    }
}
