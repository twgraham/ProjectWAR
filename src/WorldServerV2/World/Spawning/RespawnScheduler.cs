using WorldServerV2.Data.Domain;

namespace WorldServerV2.World.Spawning;

/// <summary>
/// Per-region priority queue for respawning dead creatures.
/// <para>
/// Lives on the <see cref="Spatial.Region"/> and is driven exclusively by the
/// region tick thread — no synchronisation is required.
/// </para>
/// <para>
/// <b>Lifecycle</b>:
/// <list type="bullet">
///   <item>
///     <description>
///       When a creature dies, call <see cref="Schedule"/> to queue its descriptor
///       for re-creation after <see cref="SpawnDescriptor.RespawnDelayMs"/> milliseconds.
///     </description>
///   </item>
///   <item>
///     <description>
///       At the start of each region tick, call <see cref="DrainDue"/> to re-create
///       all creatures whose respawn time has elapsed.
///     </description>
///   </item>
/// </list>
/// </para>
/// </summary>
public sealed class RespawnScheduler
{
    /// <summary>Descriptor held in the queue until its respawn time arrives.</summary>
    public readonly record struct RespawnEntry(SpawnDescriptor Descriptor);

    private readonly PriorityQueue<RespawnEntry, long> _queue = new();

    /// <summary>Number of respawns currently pending.</summary>
    public int Count => _queue.Count;

    /// <summary>
    /// Schedules a creature descriptor for respawn.
    /// No-op if <see cref="SpawnDescriptor.RespawnDelayMs"/> is <c>0</c>
    /// (temporary spawns are never re-queued).
    /// </summary>
    /// <param name="descriptor">The descriptor to re-use for the new spawn.</param>
    /// <param name="nowMs"><see cref="Environment.TickCount64"/> at time of death.</param>
    public void Schedule(SpawnDescriptor descriptor, long nowMs)
    {
        if (descriptor.RespawnDelayMs == 0)
            return;

        var dueAt = nowMs + descriptor.RespawnDelayMs;
        _queue.Enqueue(new RespawnEntry(descriptor), dueAt);
    }

    /// <summary>
    /// Passes all entries whose scheduled time has been reached to <paramref name="action"/>.
    /// Stops when the next entry is still in the future or the queue is empty.
    /// </summary>
    /// <param name="nowMs">Current tick timestamp.</param>
    /// <param name="action">Called once per due entry. Must not throw.</param>
    public void DrainDue(long nowMs, Action<RespawnEntry> action)
    {
        while (_queue.TryPeek(out _, out var priority) && priority <= nowMs)
        {
            _queue.TryDequeue(out var entry, out _);
            action(entry);
        }
    }
}
