namespace Core.Domain.Entities;

public sealed class GameObjectSpawn
{
    public long Guid { get; set; }
    public long Entry { get; set; }
    public int ZoneId { get; set; }
    public int WorldX { get; set; }
    public int WorldY { get; set; }
    public int WorldZ { get; set; }
    public int WorldO { get; set; }
    public long DisplayId { get; set; }
    public short Unk1 { get; set; }
    public short Unk2 { get; set; }
    public long Unk3 { get; set; }
    public long Unk4 { get; set; }
    public ushort[]? Unks { get; set; }
    public long? DoorId { get; set; }
    public long VfxState { get; set; }

    // Computed helpers

    /// <summary>
    /// Whether this object can be interacted with.
    /// Defaults to <c>true</c>; specialised systems may override at spawn time.
    /// </summary>
    public bool IsInteractable => true;
}
