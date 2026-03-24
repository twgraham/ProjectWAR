namespace WorldServerV2.World.Stats;

/// <summary>
/// Identifies the source tier of a stat modifier, controlling stacking policy.
/// <para>
/// <b>Stacking rule</b>: classes <see cref="Buff0"/> and <see cref="Buff1"/> use
/// "highest only" semantics — within the same class, only the largest value of each
/// sign (bonus / reduction) counts. All other classes stack <b>additively</b>.
/// </para>
/// </summary>
public enum BuffClass : byte
{
    /// <summary>
    /// General buff category 0. Highest-only stacking (e.g. Strength buff from
    /// different buff sources — only the strongest applies).
    /// </summary>
    Buff0 = 0,

    /// <summary>
    /// General buff category 1. Highest-only stacking.
    /// </summary>
    Buff1 = 1,

    /// <summary>Tactic-sourced modifiers. Additive stacking.</summary>
    Tactic = 2,

    /// <summary>Career mechanic modifiers (e.g. Slayer rage bonus). Additive stacking.</summary>
    Career = 3,

    /// <summary>Count sentinel — not a valid class.</summary>
    Count = 4,
}

/// <summary>
/// Constants for <see cref="BuffClass"/>.
/// </summary>
public static class BuffClassConstants
{
    /// <summary>Number of distinct buff classes.</summary>
    public const int ClassCount = (int)BuffClass.Count;

    /// <summary>
    /// Returns <c>true</c> if the class uses "highest only" stacking
    /// (only the largest bonus and largest reduction count).
    /// </summary>
    public static bool IsHighestOnly(BuffClass bc) => bc <= BuffClass.Buff1;
}
