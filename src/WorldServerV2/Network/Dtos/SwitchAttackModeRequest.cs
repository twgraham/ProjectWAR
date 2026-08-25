namespace WorldServerV2.Network.Dtos;

/// <summary>
/// <c>F_SWITCH_ATTACK_MODE</c> (0xDC) — Client toggle for auto-attack on/off.
/// <para>
/// The V1 packet also carries an optional <see cref="WeaponStance"/> byte for
/// ranged/melee stance switching, but the primary effect is toggling
/// <c>IsAttacking</c> on the combat interface.
/// </para>
/// </summary>
public class SwitchAttackModeRequest
{
    /// <summary>First byte — unknown / reserved.</summary>
    public byte Unk1 { get; set; }

    /// <summary>Weapon stance byte (0 = standard, used for ranged toggle in V1).</summary>
    public byte WeaponStance { get; set; }
}
