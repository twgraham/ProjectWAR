namespace WorldServerV2.World.Combat.Abilities;

/// <summary>
/// Immutable definition of an ability loaded from the database (via GameDataStore).
/// Replaces V1's mutable <c>AbilityInfo</c> + <c>AbilityConstants</c>.
/// <para>
/// Per-cast mutable state lives in <see cref="AbilityCastContext"/>, which holds
/// a reference to this definition.
/// </para>
/// </summary>
public sealed class AbilityDefinition
{
    // ── Identity ─────────────────────────────────────────────────────

    public required ushort Entry { get; init; }
    public required string Name { get; init; }

    // ── Classification ───────────────────────────────────────────────

    /// <summary>Career line bitmask (which careers have this ability).</summary>
    public uint CareerLine { get; init; }

    /// <summary>Mastery tree (0 = core, 1/2/3 = spec paths).</summary>
    public byte MasteryTree { get; init; }

    /// <summary>Melee / Ranged / Verbal / Effect.</summary>
    public AbilityType AbilityType { get; init; }

    /// <summary>Where the ability comes from (standard, item, morale).</summary>
    public AbilityOrigin Origin { get; init; }

    // ── Requirements ─────────────────────────────────────────────────

    /// <summary>Minimum player rank to train.</summary>
    public byte MinimumRank { get; init; }

    /// <summary>Minimum renown rank to train.</summary>
    public byte MinimumRenown { get; init; }

    /// <summary>Mastery point cost to spec into.</summary>
    public byte PointCost { get; init; }

    /// <summary>Required weapon type (shield, 2H, ranged, etc.).</summary>
    public WeaponRequirement WeaponNeeded { get; init; }

    // ── Casting parameters ───────────────────────────────────────────

    /// <summary>Base cast time in milliseconds. 0 = instant.</summary>
    public ushort CastTime { get; init; }

    /// <summary>Base cooldown in milliseconds.</summary>
    public ushort Cooldown { get; init; }

    /// <summary>
    /// Minimum cooldown after reductions. Prevents abilities from becoming
    /// spammable through cooldown reduction stacking.
    /// </summary>
    public ushort CooldownCap { get; init; }

    /// <summary>Shared cooldown group entry. 0 = no shared cooldown.</summary>
    public ushort CooldownEntry { get; init; }

    /// <summary>Action point cost.</summary>
    public byte ApCost { get; init; }

    /// <summary>Special resource cost (career resource / morale tier).</summary>
    public short SpecialCost { get; init; }

    /// <summary>Whether this ability can be cast while moving.</summary>
    public bool CanCastWhileMoving { get; init; }

    /// <summary>Whether this ability ignores the global cooldown.</summary>
    public bool IgnoreGlobalCooldown { get; init; }

    /// <summary>Whether abilities shouldn't apply own modifiers.</summary>
    public bool IgnoreOwnModifiers { get; init; }

    // ── Targeting ────────────────────────────────────────────────────

    /// <summary>Who/what the ability can target.</summary>
    public CommandTargetType TargetType { get; init; }

    /// <summary>Maximum range in feet.</summary>
    public ushort Range { get; init; }

    /// <summary>Minimum range in feet (prevents point-blank use of ranged).</summary>
    public byte MinRange { get; init; }

    /// <summary>AoE radius in feet. 0 = single target.</summary>
    public byte AoERadius { get; init; }

    /// <summary>Cone angle for directional AoE. 0 = full circle.</summary>
    public ushort AoEAngle { get; init; }

    /// <summary>Maximum targets for AoE. 0 defaults to 9.</summary>
    public byte MaxTargets { get; init; }

    /// <summary>Whether this ability can affect dead targets (resurrection).</summary>
    public bool AffectsDead { get; init; }

    // ── Channeling ───────────────────────────────────────────────────

    /// <summary>Channel definition entry. 0 = not channeled.</summary>
    public ushort ChannelId { get; init; }

    /// <summary>Channel tick interval in milliseconds.</summary>
    public ushort ChannelInterval { get; init; }

    // ── Toggle ───────────────────────────────────────────────────────

    /// <summary>
    /// Toggle pair entry. 0 = not a toggle. Non-zero = toggling this ability
    /// off activates the paired entry.
    /// </summary>
    public ushort ToggleEntry { get; init; }

    // ── Stealth ──────────────────────────────────────────────────────

    /// <summary>How this ability interacts with stealth.</summary>
    public AbilityStealthType StealthInteraction { get; init; }

    // ── Power level ──────────────────────────────────────────────────

    /// <summary>Base power/level of the ability (1–40 typically).</summary>
    public byte Level { get; init; }

    /// <summary>
    /// Boost level for shifter mechanic — used when caster ≠ target.
    /// </summary>
    public byte BoostLevel { get; init; }

    // ── Visual / client data ─────────────────────────────────────────

    /// <summary>Visual effect entry for client-side rendering.</summary>
    public ushort EffectId { get; init; }

    /// <summary>Cast angle for client-side facing check.</summary>
    public ushort CastAngle { get; init; }

    /// <summary>Delay before invoke (for animation sync).</summary>
    public ushort InvokeDelay { get; init; }

    /// <summary>
    /// Effect delay. Positive = scaled by range (projectile).
    /// Negative = absolute ms delay.
    /// </summary>
    public short EffectDelay { get; init; }

    /// <summary>Projectile speed multiplier. Default 1.0.</summary>
    public float FlightTimeMod { get; init; } = 1f;

    /// <summary>Fragile flag — abilities that break on retarget.</summary>
    public byte Fragile { get; init; }

    /// <summary>AI-specific range hint (may differ from player range).</summary>
    public ushort AiRange { get; init; }

    // ── Commands & Modifiers ─────────────────────────────────────────

    /// <summary>
    /// Ordered list of commands executed when the ability resolves.
    /// </summary>
    public IReadOnlyList<AbilityCommandDefinition> Commands { get; init; } = [];

    /// <summary>
    /// Ordered list of modifiers (pre-cast, post-cast, buff, delayed).
    /// </summary>
    public IReadOnlyList<AbilityModifierDefinition> Modifiers { get; init; } = [];

    /// <summary>
    /// Entry of the buff this ability invokes (shortcut). 0 = no buff.
    /// </summary>
    public ushort BuffEntry { get; init; }

    // ── Derived helpers ──────────────────────────────────────────────

    /// <summary>True if this ability has a cast-time bar.</summary>
    public bool IsInstant => CastTime == 0 && ChannelId == 0;

    /// <summary>True if this ability is channeled.</summary>
    public bool IsChanneled => ChannelId > 0;

    /// <summary>True if this ability is a toggle.</summary>
    public bool IsToggle => ToggleEntry > 0;
}

/// <summary>
/// Immutable definition of a single command within an <see cref="AbilityDefinition"/>.
/// Replaces V1's <c>AbilityCommandInfo</c> (mutable, linked-list, string-keyed).
/// </summary>
public sealed class AbilityCommandDefinition
{
    /// <summary>Command chain ID (for grouped/sequenced commands).</summary>
    public byte CommandId { get; init; }

    /// <summary>Sequence within the chain.</summary>
    public byte CommandSequence { get; init; }

    /// <summary>What this command does (replaces string CommandName).</summary>
    public required AbilityEffectType EffectType { get; init; }

    /// <summary>Who/what this command targets.</summary>
    public CommandTargetType TargetType { get; init; }

    /// <summary>AoE source point (for PBAoE vs targeted AoE).</summary>
    public CommandTargetType AoESource { get; init; }

    /// <summary>Effect radius in feet. 0 = single target.</summary>
    public byte EffectRadius { get; init; }

    /// <summary>Cone angle for directional AoE. 0 = full circle.</summary>
    public byte EffectAngle { get; init; }

    /// <summary>Maximum targets for AoE. 0 defaults to 9.</summary>
    public byte MaxTargets { get; init; }

    /// <summary>Primary parameter (damage value, buff entry, resource amount, etc.).</summary>
    public int PrimaryValue { get; init; }

    /// <summary>Secondary parameter.</summary>
    public int SecondaryValue { get; init; }

    /// <summary>Stat index used for defense check.</summary>
    public byte AttackingStat { get; init; }

    /// <summary>Whether this command has a delayed effect (projectile travel).</summary>
    public bool IsDelayedEffect { get; init; }

    /// <summary>Use all targets from AoE resolution (not just primary).</summary>
    public bool FromAllTargets { get; init; }

    /// <summary>Must be added by a modifier — not auto-executed.</summary>
    public bool NoAutoUse { get; init; }

    /// <summary>
    /// Optional damage/heal scaling data for damage-dealing commands.
    /// Null for non-damage commands (buff application, CC, etc.).
    /// </summary>
    public DamageDefinition? Damage { get; init; }

    /// <summary>
    /// Chained sub-commands (bounce, multi-hit sequences).
    /// </summary>
    public IReadOnlyList<AbilityCommandDefinition> ChainedCommands { get; init; } = [];
}

/// <summary>
/// Immutable damage/heal scaling data for an ability command.
/// Replaces V1's mutable <c>AbilityDamageInfo</c> (runtime fields extracted to
/// <see cref="DamageContext"/>).
/// </summary>
public sealed class DamageDefinition
{
    /// <summary>Display ability entry for combat log.</summary>
    public ushort DisplayEntry { get; init; }

    // ── Base damage range ────────────────────────────────────────────

    /// <summary>Minimum base damage (before level scaling).</summary>
    public ushort MinDamage { get; init; }

    /// <summary>Maximum base damage (before level scaling).</summary>
    public ushort MaxDamage { get; init; }

    /// <summary>Damage variance percentage (±N%).</summary>
    public ushort DamageVariance { get; init; }

    // ── Scaling ──────────────────────────────────────────────────────

    /// <summary>
    /// Cast time multiplier for damage scaling. Default 1.5 for abilities,
    /// weapon-speed/100 for auto-attacks.
    /// </summary>
    public float CastTimeDamageMult { get; init; } = 1.5f;

    /// <summary>Which weapon(s) contribute DPS.</summary>
    public WeaponDamageContribution WeaponMod { get; init; }

    /// <summary>Weapon DPS scaling factor.</summary>
    public float WeaponDamageScale { get; init; }

    /// <summary>Which stat provides scaling (as StatId byte value).</summary>
    public byte StatUsed { get; init; }

    /// <summary>Stat-to-damage scaling factor.</summary>
    public float StatDamageScale { get; init; } = 1f;

    /// <summary>
    /// If > 0, uses alternate PriStat formula: stat/5 × PriStatMultiplier
    /// instead of the normal stat × coefficient × scale × castMult.
    /// </summary>
    public float PriStatMultiplier { get; init; }

    // ── Damage type ──────────────────────────────────────────────────

    /// <summary>Primary damage element.</summary>
    public DamageType DamageType { get; init; }

    /// <summary>Siege sub-type.</summary>
    public SubDamageType SubDamageType { get; init; }

    // ── Critical hit ─────────────────────────────────────────────────

    /// <summary>Flat crit-rate bonus from the ability.</summary>
    public byte CriticalHitRate { get; init; }

    /// <summary>Crit damage multiplier bonus from the ability.</summary>
    public float CriticalHitDamageBonus { get; init; }

    // ── Armor penetration ────────────────────────────────────────────

    /// <summary>Percentage armor/resist pen factor (0.0–1.0).</summary>
    public float ArmorResistPenFactor { get; init; }

    /// <summary>Minimum flat armor pen (level-scaled).</summary>
    public ushort MinArmorPen { get; init; }

    /// <summary>Maximum flat armor pen (level-scaled).</summary>
    public ushort MaxArmorPen { get; init; }

    // ── Flags ────────────────────────────────────────────────────────

    /// <summary>Cannot be blocked/parried/evaded/disrupted.</summary>
    public bool Undefendable { get; init; }

    /// <summary>Cannot critically hit.</summary>
    public bool NoCrits { get; init; }

    /// <summary>Include mitigated amount in result (for steal-life etc.).</summary>
    public bool ResultFromRaw { get; init; }

    // ── Hatred / threat ──────────────────────────────────────────────

    /// <summary>Threat multiplier for damage (default 1.0).</summary>
    public float HatredScale { get; init; } = 1f;

    /// <summary>Threat multiplier for heals (default 1.0).</summary>
    public float HealHatredScale { get; init; } = 1f;

    // ── Resource ─────────────────────────────────────────────────────

    /// <summary>Career resource generated by this damage command.</summary>
    public short ResourceBuild { get; init; }

    // ── Derived helpers ──────────────────────────────────────────────

    /// <summary>True if this is a healing-type definition.</summary>
    public bool IsHeal => DamageType is DamageType.Healing or DamageType.RawHealing;
}

/// <summary>
/// Immutable definition of a modifier that adjusts an <see cref="AbilityCastContext"/>
/// or buff parameters before/after cast. Replaces V1's string-keyed
/// <c>AbilityModifierEffect</c> + <c>AbilityModifierCheck</c>.
/// </summary>
public sealed class AbilityModifierDefinition
{
    /// <summary>When this modifier is applied in the cast pipeline.</summary>
    public ModifierStage Stage { get; init; }

    /// <summary>
    /// Which operation to perform. For ~70% of modifiers this maps to a pure
    /// function via <see cref="ModifierApplicator"/>. For complex modifiers,
    /// the applicator delegates to a registered <c>IAbilityModifier</c>.
    /// </summary>
    public required ModifierOperation Operation { get; init; }

    /// <summary>Primary parameter for the operation.</summary>
    public float Value { get; init; }

    /// <summary>Secondary parameter (for two-value operations).</summary>
    public float SecondaryValue { get; init; }

    /// <summary>
    /// Optional pre-condition that must be true for this modifier to apply.
    /// Null = always applies.
    /// </summary>
    public ModifierCondition? Condition { get; init; }

    /// <summary>
    /// Condition parameter (buff entry to check, HP threshold, etc.).
    /// Interpretation depends on <see cref="Condition"/>.
    /// </summary>
    public int ConditionValue { get; init; }

    /// <summary>
    /// Which command ID this modifier targets (for command-specific modifiers).
    /// 0 = applies to the ability as a whole.
    /// </summary>
    public byte TargetCommandId { get; init; }

    /// <summary>
    /// Which command sequence this modifier targets.
    /// </summary>
    public byte TargetCommandSequence { get; init; }
}
