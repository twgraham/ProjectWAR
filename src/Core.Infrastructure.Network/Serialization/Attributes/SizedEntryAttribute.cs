namespace Core.Infrastructure.Network.Serialization.Attributes;

/// <summary>
/// Specifies that a collection property includes a per-entry byte size field in its wire header.
/// The wire format becomes: [count] [entry_size] [entries...].
/// The entry size is computed at compile time from the element type's fixed-size properties.
/// </summary>
/// <remarks>
/// Combine with <see cref="PacketLengthAttribute"/> to control the count byte width,
/// or use alone (defaults to a 1-byte count prefix).
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class SizedEntryAttribute : Attribute
{
    /// <summary>
    /// Gets the number of bytes used to encode the entry size value.
    /// </summary>
    public int ByteCount { get; }

    /// <summary>
    /// Gets whether the entry size field is written in little-endian byte order.
    /// When <c>false</c> (the default), big-endian (network byte order) is used.
    /// </summary>
    public bool LittleEndian { get; }

    /// <summary>
    /// Creates a new <see cref="SizedEntryAttribute"/>.
    /// </summary>
    /// <param name="byteCount">Number of bytes for the entry size field (1, 2, or 4). Defaults to 2.</param>
    /// <param name="littleEndian">Whether to write the entry size in little-endian byte order.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="byteCount"/> is not 1, 2, or 4.</exception>
    public SizedEntryAttribute(int byteCount = 2, bool littleEndian = false)
    {
        if (byteCount is not (1 or 2 or 4))
            throw new ArgumentException("ByteCount must be 1, 2, or 4", nameof(byteCount));
        ByteCount = byteCount;
        LittleEndian = littleEndian;
    }
}
