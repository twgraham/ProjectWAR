namespace WorldServerV2.World.Spatial;

/// <summary>
/// A disposable ticket representing a reserved Object ID (OID) from a specific
/// <see cref="Region"/>'s pool. Guarantees safe lifecycle management:
/// <list type="bullet">
///   <item>The OID is returned to the pool when the reservation is disposed
///         (e.g. if initialization fails before the entity is enqueued).</item>
///   <item>Once consumed (via <see cref="Region.AddAsync"/>),
///         disposal becomes a no-op — the OID is owned by the entity.</item>
///   <item>The owning region is tracked, preventing accidental cross-region usage.</item>
/// </list>
/// <para>
/// <b>Usage pattern</b>:
/// <code>
/// using var reservation = region.ReserveOid();
/// player.AssignOid(reservation.Oid);
/// initPipeline.Initialize(player, session);
/// await region.AddAsync(player, position, reservation); // consumes the reservation
/// // Dispose is called at end of scope — no-op because reservation was consumed
/// </code>
/// If <c>Initialize</c> throws, the <c>using</c> block disposes the reservation and
/// the OID is returned to the pool automatically.
/// </para>
/// </summary>
public sealed class OidReservation : IDisposable
{
    private const int StateActive = 0;
    private const int StateConsumed = 1;
    private const int StateReleased = 2;

    private int _state = StateActive;

    internal OidReservation(ushort oid, Region owner)
    {
        Oid = oid;
        Owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    /// <summary>The reserved Object ID.</summary>
    public ushort Oid { get; }

    /// <summary>The region that issued this reservation.</summary>
    internal Region Owner { get; }

    /// <summary>
    /// Whether this reservation is still active (not yet consumed or disposed).
    /// </summary>
    public bool IsActive => Volatile.Read(ref _state) == StateActive;

    /// <summary>
    /// Attempts to transition the reservation from <c>Active</c> to <c>Consumed</c>.
    /// Called by <see cref="Region.AddAsync"/>
    /// when the entity is successfully enqueued — after this, disposal is a no-op.
    /// </summary>
    /// <returns><c>true</c> if the reservation was active and is now consumed;
    /// <c>false</c> if it was already consumed or released.</returns>
    internal bool TryConsume()
    {
        return Interlocked.CompareExchange(ref _state, StateConsumed, StateActive) == StateActive;
    }

    /// <summary>
    /// If the reservation is still active (not consumed by <see cref="Region.AddAsync"/>),
    /// returns the OID to the owning region's pool. Safe to call multiple times.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _state, StateReleased, StateActive) == StateActive)
        {
            Owner.ReturnReservedOid(Oid);
        }
    }
}
