namespace WorldServerV2.Data.Entities;

/// <summary>
/// Game object spawn point loaded from the <c>gameobject_spawns</c> table.
/// Pure POCO — all DB column mapping is handled by <see cref="WorldDbContext"/>.
/// <para>
/// Column types are kept in their natural Npgsql → C# mappings (bigint → long,
/// integer → int, smallint → short) to avoid EF Core type-mismatch errors.
/// Narrowing casts to game-domain types (uint, byte, ushort) are done in the
/// <see cref="WorldServerV2.Data.Providers.SpawnDescriptorFactory"/>.
/// </para>
/// </summary>
public sealed class GameObjectSpawn
{
    public long  Guid      { get; set; }  // bigint PK, identity
    public long  Entry     { get; set; }  // bigint
    public int   ZoneId    { get; set; }  // integer
    public int   WorldX    { get; set; }  // integer
    public int   WorldY    { get; set; }  // integer
    public int   WorldZ    { get; set; }  // integer
    public int   WorldO    { get; set; }  // integer
    public long  DisplayId { get; set; }  // bigint
    public short Unk1      { get; set; }  // smallint
    public short Unk2      { get; set; }  // smallint
    public long     Unk3      { get; set; }  // bigint
    public long     Unk4      { get; set; }  // bigint
    public ushort[]? Unks    { get; set; }  // text, space-separated ushort[6] values
    public long? DoorId      { get; set; }  // bigint nullable
    public long  VfxState    { get; set; }  // bigint

    // ── Computed helpers ─────────────────────────────────────────────────

    /// <summary>
    /// Whether this object can be interacted with.
    /// Defaults to <c>true</c>; specialised systems may override at spawn time.
    /// </summary>
    public bool IsInteractable => true;
}
