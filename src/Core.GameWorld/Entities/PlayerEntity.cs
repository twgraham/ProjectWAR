using Core.Domain.Entities;
using Core.GameWorld.Combat.AutoAttack;
using Core.GameWorld.Items;
using Core.GameWorld.Stats;

namespace Core.GameWorld.Entities;

/// <summary>
/// A human-controlled character in the game world. Holds the persistent
/// <see cref="Character"/> record and session-scoped state as direct fields.
/// <para>
/// <b>Type safety</b>: APIs that require a player (e.g. <c>PlayerService.Bind</c>)
/// accept <c>PlayerEntity</c> — not <c>WorldEntity</c> — making it impossible at
/// compile time to pass a creature or game object.
/// </para>
/// </summary>
public sealed class PlayerEntity : UnitEntity
{
    public PlayerEntity(ushort objectId, Character character, uint maxHealth)
        : base(objectId, EntityType.Player,
            (character ?? throw new ArgumentNullException(nameof(character))).Name, maxHealth)
    {
        Character = character;
        // Players start inactive — they become active when the client signals
        // readiness via F_DUMP_STATICS, after the loading screen completes.
        IsActive = false;
    }

    /// <summary>The persistent DB character record.</summary>
    public Character Character { get; }

    /// <summary>Shorthand for <see cref="Character.CharacterId"/>.</summary>
    public uint CharacterId => Character.CharacterId;

    /// <summary>
    /// The player's inventory — equipment, backpack, bank, etc.
    /// Populated during init from DB <c>characters_items</c> rows.
    /// </summary>
    public Inventory Inventory { get; } = new();

    /// <summary>How the player disconnected (set during the logout flow).</summary>
    public DisconnectType DisconnectType { get; set; }

    /// <inheritdoc />
    public override WeaponInfo? GetWeaponInfo(WeaponSlot slot)
    {
        ushort equipSlot = slot switch
        {
            WeaponSlot.MainHand => 10,
            WeaponSlot.OffHand  => 11,
            WeaponSlot.Ranged   => 12,
            _                   => 0,
        };

        if (equipSlot == 0)
            return null;

        var item = Inventory.GetItem(equipSlot);
        if (item is null || item.Info.Dps == 0)
            return null;

        return new WeaponInfo(
            Dps:         item.Info.Dps * 0.1f,
            Speed:       item.Info.Speed,
            IsTwoHanded: item.Info.TwoHanded,
            IsShield:    item.Info.IsShield);
    }

    /// <summary>
    /// Recalculates and applies all stat bonuses from currently equipped items.
    /// Call after any equip or unequip operation, and during player initialisation.
    /// <para>
    /// NOTE: This performs a full recalculation across all equipped slots.
    /// A potential optimisation is to apply/remove stats incrementally (only the
    /// changed item), matching V1's per-item EquipItem/UnEquipItem approach.
    /// </para>
    /// </summary>
    public void ApplyEquipmentStats()
    {
        Span<int> bonuses = stackalloc int[StatConstants.SlotCount];

        foreach (var item in Inventory.GetEquippedItems())
        {
            foreach (var (statId, val) in item.Info.Stats)
            {
                if (statId < bonuses.Length)
                    bonuses[statId] += val;
            }

            // Armor is a separate field on ItemDefinition (not in the Stats dictionary).
            // Weapon slots (10–12) do not contribute physical armor.
            if (item.Info.Armor > 0 && (item.SlotId < 10 || item.SlotId > 12))
                bonuses[(int)StatId.Armor] += item.Info.Armor;
        }

        for (int i = 0; i < bonuses.Length; i++)
            Stats.SetItemBonus((StatId)i, bonuses[i]);
    }
}
