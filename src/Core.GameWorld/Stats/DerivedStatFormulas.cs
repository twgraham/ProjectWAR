namespace Core.GameWorld.Stats;

/// <summary>
/// Pure-math helpers for derived display stats sent in the <c>F_PLAYER_STATS</c>
/// packet. These are the values for stat slots 10–13 (Block, Parry, Evade, Disrupt
/// skills) that are computed from primary stats and effective level.
/// <para>
/// Formulas are taken from V1's <c>StsInterface.BuildStats()</c>:
/// <code>
///   Skill% = PrimaryStat / ((7.5 × EffectiveLevel + 50) × Factor) × 100
/// </code>
/// Where Factor is 0.075 for Parry/Evade/Disrupt and 0.2 for Block (shield-based).
/// </para>
/// All methods are pure static — no dependencies, no state.
/// </summary>
public static class DerivedStatFormulas
{
    /// <summary>Factor used for Parry, Evade, and Disrupt skill conversion.</summary>
    private const float StandardFactor = 0.075f;

    /// <summary>Factor used for Block skill conversion (shield armor based).</summary>
    private const float BlockFactor = 0.2f;

    /// <summary>
    /// Computes the effective level denominator: <c>(7.5 × level + 50)</c>.
    /// </summary>
    private static float LevelDenom(byte level) => 7.5f * level + 50f;

    /// <summary>
    /// Computes Parry skill percentage from <see cref="StatId.WeaponSkill"/> and level.
    /// </summary>
    public static ushort ParrySkill(int weaponSkill, byte effectiveLevel)
    {
        if (effectiveLevel == 0 || weaponSkill <= 0) return 0;
        float result = weaponSkill / (LevelDenom(effectiveLevel) * StandardFactor) * 100f;
        return ClampToUshort(result);
    }

    /// <summary>
    /// Computes Evade skill percentage from <see cref="StatId.Initiative"/> and level.
    /// </summary>
    public static ushort EvadeSkill(int initiative, byte effectiveLevel)
    {
        if (effectiveLevel == 0 || initiative <= 0) return 0;
        float result = initiative / (LevelDenom(effectiveLevel) * StandardFactor) * 100f;
        return ClampToUshort(result);
    }

    /// <summary>
    /// Computes Disrupt skill percentage from <see cref="StatId.Willpower"/> and level.
    /// </summary>
    public static ushort DisruptSkill(int willpower, byte effectiveLevel)
    {
        if (effectiveLevel == 0 || willpower <= 0) return 0;
        float result = willpower / (LevelDenom(effectiveLevel) * StandardFactor) * 100f;
        return ClampToUshort(result);
    }

    /// <summary>
    /// Computes Block skill percentage from shield armor value and level.
    /// Requires an equipped shield — pass 0 if no shield is equipped.
    /// </summary>
    public static ushort BlockSkill(int shieldArmor, byte effectiveLevel)
    {
        if (effectiveLevel == 0 || shieldArmor <= 0) return 0;
        float result = shieldArmor / (LevelDenom(effectiveLevel) * BlockFactor) * 100f;
        return ClampToUshort(result);
    }

    /// <summary>
    /// Computes the tactic slot count for a given level.
    /// First slot unlocks at level 10 (returns 1), second at 20, third at 30, fourth at 40.
    /// </summary>
    public static byte TacticSlots(byte level) =>
        (byte)(level / 10);

    private static ushort ClampToUshort(float value) =>
        (ushort)Math.Clamp((int)value, 0, ushort.MaxValue);
}
