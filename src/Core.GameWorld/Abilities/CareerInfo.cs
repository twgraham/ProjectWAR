namespace Core.GameWorld.Abilities;

/// <summary>
/// Static career metadata: display names and racial-pair package ID offsets.
/// <para>
/// The WAR client uses a <c>packageId</c> (distinct from the server's ability entry)
/// to look up ability icons, names, and visual data from its local .myp files.
/// The <c>referenceId</c> sent in the packet equals the server's DB entry.
/// The offset between them is constant within a racial pairing, discovered through
/// IDA reverse-engineering and validated via sniff data.
/// </para>
/// <para>
/// Career lines follow the <c>GameData.CareerLine</c> enum:
/// Dwarves 1–4, Greenskins 5–8, Empire 9–12, Chaos 13–16, High Elves 17–20, Dark Elves 21–24.
/// </para>
/// </summary>
public static class CareerInfo
{
    // ── Package ID offsets (by racial pairing) ─────────────────────

    /// <summary>Offset for Dwarves (1–4) and Greenskins (5–8).</summary>
    private const int DwarfGreenskinOffset = 1399;

    /// <summary>Offset for Empire (9–12) and Chaos (13–16).</summary>
    private const int EmpireChaosOffset = 4251;

    /// <summary>Offset for High Elves (17–20) and Dark Elves (21–24).</summary>
    private const int ElfOffset = 4351;

    /// <summary>
    /// Computes the client package ID for a given ability entry and career line.
    /// <c>packageId = abilityEntry + GetPackageIdOffset(careerLine)</c>.
    /// The <c>referenceId</c> field in the packet is simply the raw DB entry.
    /// </summary>
    public static uint ComputePackageId(ushort abilityEntry, byte careerLine)
    {
        var offset = GetPackageIdOffset(careerLine);
        return (uint)(abilityEntry + offset);
    }

    /// <summary>
    /// Returns the racial-pair package ID offset for a career line.
    /// Always positive: <c>packageId = entry + offset</c>.
    /// </summary>
    public static int GetPackageIdOffset(byte careerLine) => careerLine switch
    {
        // Dwarves (1–4) + Greenskins (5–8)
        >= 1 and <= 8 => DwarfGreenskinOffset,

        // Empire (9–12) + Chaos (13–16)
        >= 9 and <= 16 => EmpireChaosOffset,

        // High Elves (17–20) + Dark Elves (21–24)
        >= 17 and <= 24 => ElfOffset,

        _ => 0,
    };

    // ── Career display names ───────────────────────────────────────

    /// <summary>
    /// Display names indexed by career line, matching <c>GameData.CareerLine</c>.
    /// </summary>
    private static readonly string[] CareerNames =
    [
        "",                // 0  — unused
        "Ironbreaker",     // 1  — Dwarf Tank
        "Slayer",          // 2  — Dwarf MDPS
        "Rune Priest",     // 3  — Dwarf Healer
        "Engineer",        // 4  — Dwarf RDPS
        "Black Orc",       // 5  — Greenskin Tank
        "Choppa",          // 6  — Greenskin MDPS
        "Shaman",          // 7  — Greenskin Healer
        "Squig Herder",    // 8  — Greenskin RDPS
        "Witch Hunter",    // 9  — Empire MDPS
        "Knight of the BS",// 10 — Empire Tank
        "Bright Wizard",   // 11 — Empire RDPS
        "Warrior Priest",  // 12 — Empire Healer
        "Chosen",          // 13 — Chaos Tank
        "Marauder",        // 14 — Chaos MDPS
        "Zealot",          // 15 — Chaos Healer
        "Magus",           // 16 — Chaos RDPS
        "Swordmaster",     // 17 — High Elf Tank
        "Shadow Warrior",  // 18 — High Elf RDPS
        "White Lion",      // 19 — High Elf MDPS
        "Archmage",        // 20 — High Elf Healer
        "Black Guard",     // 21 — Dark Elf Tank
        "Witch Elf",       // 22 — Dark Elf MDPS
        "Disciple",        // 23 — Dark Elf Healer
        "Sorcerer",        // 24 — Dark Elf RDPS
    ];

    /// <summary>
    /// Returns the display name for a career line (1–24).
    /// </summary>
    public static string GetCareerName(byte careerLine) =>
        careerLine < CareerNames.Length ? CareerNames[careerLine] : $"Career {careerLine}";

    /// <summary>
    /// Returns the race display name for a career line (used in tree 5/6 names).
    /// </summary>
    public static string GetRaceName(byte careerLine) => careerLine switch
    {
        >= 1 and <= 4 => "Dwarf",
        >= 5 and <= 8 => "Greenskin",
        >= 9 and <= 12 => "Empire",
        >= 13 and <= 16 => "Chaos",
        >= 17 and <= 20 => "High Elf",
        >= 21 and <= 24 => "Dark Elf",
        _ => "Unknown",
    };

    /// <summary>
    /// Returns the archetype/role display name for a career line (used in tree 3/4 names).
    /// Career-to-role mapping varies per race (e.g. Empire index 9 = MDPS, not Tank).
    /// </summary>
    public static string GetRoleName(byte careerLine) => careerLine switch
    {
        1 or 5 or 10 or 13 or 17 or 21 => "Tank",
        2 or 6 or 9 or 14 or 19 or 22 => "Melee DPS",
        3 or 7 or 12 or 15 or 20 or 23 => "Healer",
        4 or 8 or 11 or 16 or 18 or 24 => "Ranged DPS",
        _ => "Unknown",
    };
}
