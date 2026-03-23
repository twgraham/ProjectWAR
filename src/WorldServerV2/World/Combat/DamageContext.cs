namespace WorldServerV2.World.Combat;

/// <summary>
/// All data needed for a single damage resolution pass. Organized in logical
/// sections that reflect the pipeline flow:
/// <list type="number">
///   <item><b>Input</b> — ability definition + flags (set once at creation)</item>
///   <item><b>Stat snapshots</b> — attacker/target stats resolved from <c>StatContainer</c> at creation</item>
///   <item><b>Pipeline state</b> — running totals mutated by pipeline stages</item>
///   <item><b>Result</b> — final values written once at pipeline end</item>
/// </list>
/// <para>
/// <b>Randomness</b>: random rolls are pre-resolved into the context so the
/// pipeline is 100% deterministic — identical inputs produce identical outputs.
/// The caller (e.g. <c>AbilityCastService</c>) fills these from the RNG.
/// </para>
/// </summary>
public sealed class DamageContext
{
    // ═══════════════════════════════════════════════════════════════════
    //  INPUT — Ability parameters (set once at creation)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>Ability entry ID (for combat log / packet).</summary>
    public ushort AbilityEntry;

    /// <summary>Primary damage element.</summary>
    public DamageType DamageType;

    /// <summary>Siege sub-type (if applicable).</summary>
    public SubDamageType SubDamageType;

    // ── Levels ───────────────────────────────────────────────────────

    /// <summary>Attacker's effective level (for scaling formulas).</summary>
    public byte AttackerLevel;

    /// <summary>Target's effective level (for creature bonuses).</summary>
    public byte TargetLevel;

    // ── Base damage range ────────────────────────────────────────────

    /// <summary>Minimum base damage (level-scaled by formula).</summary>
    public ushort MinDamage;

    /// <summary>Maximum base damage (level-scaled by formula).</summary>
    public ushort MaxDamage;

    /// <summary>Damage variance percentage (±N%, 0 = fixed).</summary>
    public ushort DamageVariance;

    // ── Weapon contribution ──────────────────────────────────────────

    /// <summary>Pre-resolved weapon DPS (from equipment service).</summary>
    public float WeaponDps;

    /// <summary>Weapon DPS scaling factor (from ability definition).</summary>
    public float WeaponDamageScale;

    // ── Scaling coefficients ─────────────────────────────────────────

    /// <summary>
    /// Cast time multiplier — longer casts deal more damage.
    /// Default 1.5 for abilities, weapon-speed/100 for auto-attacks.
    /// </summary>
    public float CastTimeDamageMult = 1.5f;

    /// <summary>Stat-to-damage scaling factor from ability definition.</summary>
    public float StatDamageScale = 1f;

    /// <summary>
    /// Stat coefficient: 0.2 for abilities, 0.1 for auto-attacks, 0.05 for offhand.
    /// </summary>
    public float StatCoefficient = 0.2f;

    /// <summary>
    /// If &gt; 0, uses alternate PriStat formula: <c>stat/5 × PriStatMultiplier</c>
    /// instead of the normal <c>stat × coefficient × scale × castMult</c>.
    /// </summary>
    public float PriStatMultiplier;

    // ── Crit parameters (from ability definition) ────────────────────

    /// <summary>Flat crit-rate bonus from the ability itself.</summary>
    public byte BaseCritRate;

    /// <summary>Crit damage multiplier bonus from the ability.</summary>
    public float BaseCritDamageBonus;

    // ── Armor penetration (from ability definition) ──────────────────

    /// <summary>Percentage armor/resist pen factor (0.0–1.0) from the ability.</summary>
    public float ArmorResistPenFactor;

    /// <summary>Minimum flat armor penetration (level-scaled).</summary>
    public ushort MinArmorPen;

    /// <summary>Maximum flat armor penetration (level-scaled).</summary>
    public ushort MaxArmorPen;

    // ── Defensibility ────────────────────────────────────────────────

    /// <summary>
    /// Flat modifier to defense chance from the ability. Positive = easier to defend,
    /// negative = harder.
    /// </summary>
    public int Defensibility;

    // ── Flags ────────────────────────────────────────────────────────

    /// <summary>Auto-attack (weapon swing, not ability).</summary>
    public bool IsAutoAttack;

    /// <summary>Off-hand swing (90% damage penalty).</summary>
    public bool IsOffhand;

    /// <summary>Proc damage (no weapon DPS, no defense roll, no crit).</summary>
    public bool IsProc;

    /// <summary>Pre-calculated DoT/HoT tick — base damage already resolved.</summary>
    public bool IsPrecalculated;

    /// <summary>Area-of-effect damage (50% penalty vs pets).</summary>
    public bool IsAoE;

    /// <summary>Cannot be blocked, parried, evaded, or disrupted.</summary>
    public bool Undefendable;

    /// <summary>Cannot critically hit.</summary>
    public bool NoCrits;

    // ── Precalculated values (DoT per-tick) ──────────────────────────

    /// <summary>Pre-computed damage value (set during initial ability cast).</summary>
    public float PrecalcDamage;

    /// <summary>Pre-computed mitigation value (set during initial ability cast).</summary>
    public float PrecalcMitigation;

    /// <summary>Per-tick fraction multiplier (default 1.0).</summary>
    public float PrecalcMultiplier = 1f;

    // ═══════════════════════════════════════════════════════════════════
    //  STAT SNAPSHOTS — Resolved from StatContainers at context creation
    // ═══════════════════════════════════════════════════════════════════
    //
    // Captured once when the context is created. Step 5 (AbilityCastService)
    // will provide factory methods that populate these from entities.

    // ── Attacker stats ───────────────────────────────────────────────

    /// <summary>Total primary stat (Str/WP/BS/Int) — before soft/hard cap.</summary>
    public int AttackerPrimaryStat;

    /// <summary>Bonus power stat (MeleePower/HealingPower/RangedPower/MagicPower). Bypasses caps.</summary>
    public int AttackerPowerStat;

    /// <summary>Attacker's total Weapon Skill (used in armor pen formula).</summary>
    public int AttackerWeaponSkill;

    /// <summary>Net armor penetration % bonus (bonus − reduction, 0.0–1.0).</summary>
    public float AttackerArmorPenPct;

    /// <summary>Total general crit rate (from stats).</summary>
    public int AttackerCritRate;

    /// <summary>Type-specific crit rate (MeleeCritRate/RangedCritRate/MagicCritRate).</summary>
    public int AttackerTypeCritRate;

    /// <summary>Total crit damage bonus (as whole %, e.g. 10 = +10%).</summary>
    public int AttackerCritDamage;

    /// <summary>Type-specific power % bonus (MeleePower or MagicPower stat bonus modifier).</summary>
    public float AttackerTypePowerBonus;

    /// <summary>Type-specific power % reduction modifier.</summary>
    public float AttackerTypePowerReduction;

    /// <summary>General outgoing damage % bonus modifier.</summary>
    public float AttackerOutDmgBonus;

    /// <summary>General outgoing damage % reduction modifier.</summary>
    public float AttackerOutDmgReduction;

    /// <summary>Attacker's block strikethrough (flat %).</summary>
    public int AttackerBlockStrikethrough;

    /// <summary>Attacker's defense strikethrough (flat %).</summary>
    public int AttackerDefStrikethrough;

    // ── Target stats ─────────────────────────────────────────────────

    /// <summary>Target's total Toughness — before soft/hard cap.</summary>
    public int TargetToughness;

    /// <summary>Target's total Initiative (crit chance denominator).</summary>
    public int TargetInitiative;

    /// <summary>Target's total Armor (for physical mitigation).</summary>
    public int TargetArmor;

    /// <summary>Target's total Resistance for the damage type (Spirit/Elemental/Corporeal).</summary>
    public int TargetResistance;

    /// <summary>Target's flat crit-rate reduction.</summary>
    public int TargetCritReduction;

    /// <summary>Target's crit damage taken modifier (whole %, e.g. 5 = +5% crit damage taken).</summary>
    public int TargetCritDamageTaken;

    /// <summary>Type-specific incoming damage % bonus modifier.</summary>
    public float TargetInTypeDmgBonus;

    /// <summary>Type-specific incoming damage % reduction modifier.</summary>
    public float TargetInTypeDmgReduction;

    /// <summary>General incoming damage % bonus modifier.</summary>
    public float TargetInDmgBonus;

    /// <summary>General incoming damage % reduction modifier.</summary>
    public float TargetInDmgReduction;

    /// <summary>Target's armor pen resistance % bonus (0.0–1.0).</summary>
    public float TargetArmorPenReduction;

    /// <summary>Target's defense rating for the relevant type (WeaponSkill/Initiative/Willpower).</summary>
    public int TargetDefenseRating;

    /// <summary>Target's block rating (from shield). 0 if no shield.</summary>
    public int TargetBlockRating;

    /// <summary>Target's flat block % bonus (from stats).</summary>
    public int TargetBlock;

    /// <summary>Target's flat parry/evade/disrupt % bonus (from stats).</summary>
    public int TargetDefense;

    /// <summary>
    /// Whether the target is in the attacker's front arc (for block/parry eligibility).
    /// Set by caller based on positional check.
    /// </summary>
    public bool TargetIsFacing;

    /// <summary>Whether the target has a shield equipped (required for block).</summary>
    public bool TargetHasShield;

    /// <summary>Whether the target is a pet (for AoE pet penalty).</summary>
    public bool TargetIsPet;

    // ═══════════════════════════════════════════════════════════════════
    //  PRE-ROLLED RANDOM VALUES (deterministic pipeline)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Random variance fraction for base damage (−DamageVariance% to +DamageVariance%).
    /// Range: [-1.0, 1.0] — multiplied by <see cref="DamageVariance"/> / 100.
    /// </summary>
    public float DamageVarianceRoll;

    /// <summary>Defense roll result [0, 99]. Compared against computed defense chance.</summary>
    public int DefenseRoll;

    /// <summary>Critical hit roll result [0, 99]. Compared against computed crit chance.</summary>
    public int CritRoll;

    /// <summary>Critical damage variance [0.0, 0.2]. Added to base 1.35 crit multiplier.</summary>
    public float CritVarianceRoll;

    // ═══════════════════════════════════════════════════════════════════
    //  PIPELINE STATE (mutable — written by stages, read by later stages)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>Running damage total. Each stage reads/writes this.</summary>
    public float Damage;

    /// <summary>Running mitigation total (toughness + armor/resist).</summary>
    public float Mitigation;

    /// <summary>Damage absorbed by shields.</summary>
    public float Absorption;

    /// <summary>
    /// Multiplicative damage bonus accumulator. Starts at 1.0.
    /// Buff events add to this during DealingDamage/ReceivingDamage notifications.
    /// Applied by <see cref="DamagePipeline.ApplyModifiers"/>.
    /// </summary>
    public float DamageBonus = 1f;

    /// <summary>
    /// Multiplicative damage reduction accumulator. Starts at 1.0.
    /// Applied alongside <see cref="DamageBonus"/>.
    /// </summary>
    public float DamageReduction = 1f;

    // ═══════════════════════════════════════════════════════════════════
    //  RESULT (written once at pipeline end)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>Whether the attack was critically hit.</summary>
    public bool WasCritical;

    /// <summary>The crit damage multiplier that was applied (0 if not crit).</summary>
    public float CritMultiplier;

    /// <summary>Whether the attack was fully defended (block/parry/evade/disrupt).</summary>
    public bool WasDefended;

    /// <summary>Which defense type succeeded (None if not defended).</summary>
    public DefenseType DefenseType;

    /// <summary>Final damage dealt to the target after all stages.</summary>
    public uint FinalDamage;

    /// <summary>Total mitigation value (toughness + armor/resist combined).</summary>
    public uint FinalMitigation;

    /// <summary>Total absorption by shields.</summary>
    public uint FinalAbsorption;

    /// <summary>Damage redirected to a guard tank.</summary>
    public float GuardSplitAmount;

    /// <summary>Whether this hit killed the target.</summary>
    public bool WasKillingBlow;
}
