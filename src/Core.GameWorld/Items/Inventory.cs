namespace Core.GameWorld.Items;

/// <summary>
/// Player inventory container. Holds all item instances across the fixed slot layout:
/// <list type="bullet">
///   <item>Equipment: slots 0–39</item>
///   <item>Backpack: slots 40–(40 + 32 + expansions×16)</item>
///   <item>Crafting: slots 400–499</item>
///   <item>Currency: slots 500–599</item>
///   <item>Quest: slots 700–799</item>
///   <item>Bank: slots 800–(800 + 80 + expansions×8)</item>
///   <item>Overflow: slots 1100–1149</item>
/// </list>
/// <para>
/// Items are stored in sparse slot-indexed arrays. Equipment and backpack are the
/// primary arrays; others are lazily allocated on first access.
/// </para>
/// </summary>
public sealed class Inventory
{
    // ── Slot layout constants (wire-format, must match client) ───────

    public const ushort MaxEquipmentSlot = 40;
    public const ushort BackpackStart = 40;
    public const ushort CraftingStart = 400;
    public const ushort CurrencyStart = 500;
    public const ushort QuestStart = 700;
    public const ushort BankStart = 800;
    public const ushort BankEnd = 1039;
    public const ushort OverflowStart = 1100;
    public const ushort OverflowEnd = 1150;
    public const ushort DeleteSlot = 1040;

    /// <summary>Base backpack slots (always available).</summary>
    private const int BaseBackpackSlots = 32;

    /// <summary>Slots added per backpack expansion purchase.</summary>
    public const int SlotsPerBackpackExpansion = 16;

    /// <summary>Maximum backpack expansion tiers.</summary>
    private const int MaxBackpackExpansions = 5;

    /// <summary>Base bank slots (always available).</summary>
    private const int BaseBankSlots = 80;

    /// <summary>Slots added per bank expansion purchase.</summary>
    public const int SlotsPerBankExpansion = 8;

    /// <summary>Maximum bank expansion tiers.</summary>
    private const int MaxBankExpansions = 30;

    /// <summary>Base price for first backpack expansion (in copper).</summary>
    public const uint BaseBackpackPrice = 2000; // 100 * 20

    /// <summary>Base price for first bank expansion (in copper).</summary>
    public const uint BaseBankPrice = 60000;

    /// <summary>Price interval between bank expansion tiers.</summary>
    public const uint BankPriceInterval = 20000;

    // ── Slot arrays ─────────────────────────────────────────────────

    private readonly Item?[] _equipment = new Item?[MaxEquipmentSlot];
    private Item?[] _backpack;
    private Item?[]? _crafting;
    private Item?[]? _currency;
    private Item?[]? _quest;
    private Item?[]? _bank;
    private Item?[]? _overflow;

    // ── Expansion state ─────────────────────────────────────────────

    /// <summary>Number of purchased backpack expansion tiers (0–5).</summary>
    public byte BackpackExpansions { get; set; }

    /// <summary>Number of purchased bank expansion tiers (0–30).</summary>
    public byte BankExpansions { get; set; }

    /// <summary>Total usable backpack slots (base 32 + expansions × 16).</summary>
    public int MaxBackpackSlots => BaseBackpackSlots + BackpackExpansions * SlotsPerBackpackExpansion;

    /// <summary>Total usable bank slots (base 80 + expansions × 8).</summary>
    public int MaxBankSlots => BaseBankSlots + BankExpansions * SlotsPerBankExpansion;

    /// <summary>Cost of the next backpack expansion in copper.</summary>
    public uint NextBackpackExpansionCost =>
        BackpackExpansions >= MaxBackpackExpansions ? 0 : BaseBackpackPrice * (uint)(1 << BackpackExpansions);

    /// <summary>Cost of the next bank expansion in copper.</summary>
    public uint NextBankExpansionCost =>
        BankExpansions >= MaxBankExpansions ? 0 : BaseBankPrice + BankPriceInterval * BankExpansions;

    public Inventory()
    {
        _backpack = new Item?[BaseBackpackSlots];
    }

    // ── Slot access ─────────────────────────────────────────────────

    /// <summary>
    /// Gets the item at the given wire-format slot, or <c>null</c> if empty.
    /// </summary>
    public Item? GetItem(ushort slot)
    {
        if (slot < MaxEquipmentSlot)
            return _equipment[slot];

        if (slot >= BackpackStart && slot < BackpackStart + MaxBackpackSlots)
        {
            var idx = slot - BackpackStart;
            return idx < _backpack.Length ? _backpack[idx] : null;
        }

        if (slot >= CraftingStart && slot < CurrencyStart)
        {
            var idx = slot - CraftingStart;
            return _crafting != null && idx < _crafting.Length ? _crafting[idx] : null;
        }

        if (slot >= CurrencyStart && slot < CurrencyStart + 100)
        {
            var idx = slot - CurrencyStart;
            return _currency != null && idx < _currency.Length ? _currency[idx] : null;
        }

        if (slot >= QuestStart && slot < QuestStart + 100)
        {
            var idx = slot - QuestStart;
            return _quest != null && idx < _quest.Length ? _quest[idx] : null;
        }

        if (slot >= BankStart && slot <= BankEnd)
        {
            var idx = slot - BankStart;
            return _bank != null && idx < _bank.Length ? _bank[idx] : null;
        }

        if (slot >= OverflowStart && slot < OverflowEnd)
        {
            var idx = slot - OverflowStart;
            return _overflow != null && idx < _overflow.Length ? _overflow[idx] : null;
        }

        return null;
    }

    /// <summary>
    /// Places an item at the given wire-format slot, replacing any existing item.
    /// </summary>
    public void SetItem(ushort slot, Item? item)
    {
        if (item != null)
            item.SlotId = slot;

        if (slot < MaxEquipmentSlot)
        {
            _equipment[slot] = item;
            return;
        }

        if (slot >= BackpackStart && slot < BackpackStart + MaxBackpackSlots)
        {
            EnsureBackpackCapacity();
            _backpack[slot - BackpackStart] = item;
            return;
        }

        if (slot >= CraftingStart && slot < CurrencyStart)
        {
            _crafting ??= new Item?[CurrencyStart - CraftingStart];
            _crafting[slot - CraftingStart] = item;
            return;
        }

        if (slot >= CurrencyStart && slot < CurrencyStart + 100)
        {
            _currency ??= new Item?[100];
            _currency[slot - CurrencyStart] = item;
            return;
        }

        if (slot >= QuestStart && slot < QuestStart + 100)
        {
            _quest ??= new Item?[100];
            _quest[slot - QuestStart] = item;
            return;
        }

        if (slot >= BankStart && slot <= BankEnd)
        {
            EnsureBankCapacity();
            _bank![slot - BankStart] = item;
            return;
        }

        if (slot >= OverflowStart && slot < OverflowEnd)
        {
            _overflow ??= new Item?[OverflowEnd - OverflowStart];
            _overflow[slot - OverflowStart] = item;
        }
    }

    // ── Enumeration ─────────────────────────────────────────────────

    /// <summary>
    /// Enumerates all non-null items across all slot regions. Used for packet sending.
    /// </summary>
    public IEnumerable<Item> GetAllItems()
    {
        foreach (var item in _equipment)
            if (item != null) yield return item;

        foreach (var item in _backpack)
            if (item != null) yield return item;

        if (_crafting != null)
            foreach (var item in _crafting)
                if (item != null) yield return item;

        if (_currency != null)
            foreach (var item in _currency)
                if (item != null) yield return item;

        if (_quest != null)
            foreach (var item in _quest)
                if (item != null) yield return item;

        if (_bank != null)
            foreach (var item in _bank)
                if (item != null) yield return item;

        if (_overflow != null)
            foreach (var item in _overflow)
                if (item != null) yield return item;
    }

    /// <summary>
    /// Enumerates all non-null equipped items (slots 0–39).
    /// </summary>
    public IEnumerable<Item> GetEquippedItems()
    {
        foreach (var item in _equipment)
            if (item != null) yield return item;
    }

    /// <summary>
    /// Returns the total number of items across all slots.
    /// </summary>
    public int Count()
    {
        var count = 0;
        foreach (var item in _equipment)
            if (item != null) count++;
        foreach (var item in _backpack)
            if (item != null) count++;
        if (_crafting != null)
            foreach (var item in _crafting)
                if (item != null) count++;
        if (_currency != null)
            foreach (var item in _currency)
                if (item != null) count++;
        if (_quest != null)
            foreach (var item in _quest)
                if (item != null) count++;
        if (_bank != null)
            foreach (var item in _bank)
                if (item != null) count++;
        if (_overflow != null)
            foreach (var item in _overflow)
                if (item != null) count++;
        return count;
    }

    // ── Internal helpers ────────────────────────────────────────────

    private void EnsureBackpackCapacity()
    {
        var needed = MaxBackpackSlots;
        if (_backpack.Length >= needed) return;
        var newArray = new Item?[needed];
        Array.Copy(_backpack, newArray, _backpack.Length);
        _backpack = newArray;
    }

    private void EnsureBankCapacity()
    {
        var needed = MaxBankSlots;
        if (_bank != null && _bank.Length >= needed) return;
        var newArray = new Item?[needed];
        if (_bank != null) Array.Copy(_bank, newArray, _bank.Length);
        _bank = newArray;
    }
}
