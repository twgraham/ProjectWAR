using Core.Infrastructure.Network.Serialization.Attributes;

namespace WorldServerV2.Network.Dtos;

/// <summary>
/// Server response for <c>F_PLAYER_STATS</c> (0x46).
/// Sends the player's base stats (strength, agility, etc.) and level information.
/// <para>
/// The wire format is: item-side header (6 bytes) + stats header (2 bytes) +
/// 21 stat entries (3 bytes each = 63 bytes) + terminator (1 byte) = 72 bytes total.
/// </para>
/// </summary>
public class PlayerStatsResponse
{
    // ── Items header (6 bytes, from ItmInterface.BuildStats) ────────────

    /// <summary>Base stat count constant (0x15 = 21 stats).</summary>
    public byte BaseStatCount { get; set; } = 0x15;

    /// <summary>Number of unlocked tactic slots (Level / 10 if Level > 10, else 0). Level 10 yields 0 slots — first slot unlocks at level 11.</summary>
    public byte TacticSlots { get; set; }

    /// <summary>Unknown constant (always 0x01).</summary>
    public byte Unknown1 { get; set; } = 0x01;

    /// <summary>Unknown constant (always 0xF4).</summary>
    public byte Unknown2 { get; set; } = 0xF4;

    /// <summary>Total armor value.</summary>
    public ushort Armor { get; set; }

    // ── Stats header (2 bytes, from StsInterface.BuildStats) ────────────

    /// <summary>Effective level after bolster (same as Level if no bolster).</summary>
    public byte BolsterLevel { get; set; }

    /// <summary>Actual character level.</summary>
    public byte Level { get; set; }

    // ── 21 stat entries (63 bytes) ──────────────────────────────────────

    /// <summary>
    /// Stat entries: 21 × [uint8 statId, uint16 statValue].
    /// Serialized as a fixed-length byte array matching the old wire format.
    /// </summary>
    [FixedLength(63)]
    public byte[] StatEntries { get; set; } = new byte[63];

    // ── Terminator (1 byte) ─────────────────────────────────────────────

    /// <summary>Terminator byte (always 0x00).</summary>
    public byte Terminator { get; set; }

    /// <summary>
    /// Helper to write a single stat entry into <see cref="StatEntries"/>.
    /// </summary>
    /// <param name="index">Zero-based index (0–20).</param>
    /// <param name="statId">The stat identifier (1–21).</param>
    /// <param name="value">The stat value.</param>
    public void SetStat(int index, byte statId, ushort value)
    {
        var offset = index * 3;
        StatEntries[offset] = statId;
        StatEntries[offset + 1] = (byte)(value >> 8); // big-endian high byte
        StatEntries[offset + 2] = (byte)(value & 0xFF); // big-endian low byte
    }
}
