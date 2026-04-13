namespace Core.Domain.Entities;

/// <summary>
/// Game object prototype loaded from the <c>gameobject_protos</c> table.
/// Pure POCO — all DB column mapping is handled by <see cref="WorldDbContext"/>.
/// <para>
/// Types match the natural Npgsql mappings: bigint → long, integer → int,
/// smallint → short. Narrowing casts are performed in consumers.
/// </para>
/// </summary>
public sealed class GameObjectProto
{
    public long   Entry        { get; set; }  // bigint PK, identity
    public string Name         { get; set; } = string.Empty;  // varchar
    public int    DisplayId    { get; set; }  // integer
    public int    Scale        { get; set; }  // integer
    public short  Level        { get; set; }  // smallint
    public short  Faction      { get; set; }  // smallint
    public long   HealthPoints { get; set; }  // bigint
}
