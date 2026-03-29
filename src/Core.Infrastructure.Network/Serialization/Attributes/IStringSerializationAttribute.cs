namespace Core.Infrastructure.Network.Serialization.Attributes;

/// <summary>
/// Defines custom string serialization behaviour for a property attribute.
/// Implement this interface on an <see cref="Attribute"/> to control how a
/// <c>string</c> property is read from and written to the wire.
/// <para>
/// Both the reflection-based <see cref="BinaryPacketSerializer"/> and the
/// source-generated serializer honour this interface automatically — no
/// changes to either serializer are required when a new implementation is
/// added.
/// </para>
/// </summary>
/// <remarks>
/// The source generator avoids reflection by instantiating the attribute
/// directly (e.g. <c>new PascalStringAttribute().Read(ref reader)</c>).
/// </remarks>
public interface IStringSerializationAttribute
{
    /// <summary>
    /// Writes <paramref name="value"/> to the wire using this string encoding.
    /// </summary>
    void Write(ref BinaryPacketSerializer.SpanWriter writer, string value);

    /// <summary>
    /// Reads a string from the wire using this string encoding.
    /// </summary>
    string Read(ref BinaryPacketSerializer.SpanReader reader);

    /// <summary>
    /// The fixed wire size in bytes if this encoding always produces a fixed
    /// number of bytes, or <c>null</c> if the size is variable.
    /// Used by <c>[SizedEntry]</c> to compute per-entry byte counts.
    /// </summary>
    int? FixedWireSize { get; }
}
