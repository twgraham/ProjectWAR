namespace WorldServerV2.Data.Entities;

/// <summary>
/// Persistent character record mapped to the <c>characters</c> table.
/// Pure POCO — all DB column mapping is handled by <see cref="WorldDbContext"/>.
/// <para>
/// This replaces the legacy <c>Common.Character</c> class — no ORM base class
/// or FrameWork dependency.
/// </para>
/// </summary>
public sealed class Character
{
    public uint CharacterId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Surname { get; set; } = string.Empty;
    public int RealmId { get; set; }
    public int AccountId { get; set; }
    public byte SlotId { get; set; }
    public byte ModelId { get; set; }
    public byte Career { get; set; }
    public byte CareerLine { get; set; }
    public byte Realm { get; set; }
    public int HeldLeft { get; set; }
    public byte Race { get; set; }
    public string Traits { get; set; } = string.Empty;
    public byte Sex { get; set; }
    public bool Anonymous { get; set; }
    public bool Hidden { get; set; }
    public string OldName { get; set; } = string.Empty;
    public string PetName { get; set; } = string.Empty;
    public ushort PetModel { get; set; }
    public ushort HonorPoints { get; set; }
    public ushort HonorRank { get; set; }

    /// <summary>Computed career flag bitmask.</summary>
    public uint CareerFlags => CareerLine != 0 ? 1u << (CareerLine - 1) : 0;

    /// <summary>The raw trait bytes (decoded from <see cref="Traits"/>).</summary>
    public byte[] TraitBytes { get; set; } = [];

    public byte Level { get; set; } = 1;
    public bool FirstConnect { get; set; }
}
