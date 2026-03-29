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
/// <para>
/// The source generator avoids reflection by instantiating the attribute
/// directly (e.g. <c>new PascalStringAttribute().Read(ref reader)</c>).
/// </para>
/// <para>
/// <b>Wire size convention for the source generator:</b> Because the source
/// generator cannot evaluate <see cref="FixedWireSize"/> at compile time, it
/// infers the fixed wire size from the attribute's constructor arguments. If
/// the first constructor argument is a positive <see cref="int"/>, it is
/// treated as the fixed wire size in bytes; otherwise the encoding is assumed
/// to be variable-length. This convention is used when computing entry sizes
/// for <c>[SizedEntry]</c> at source-generation time. The reflection-based
/// serializer always uses the <see cref="FixedWireSize"/> property directly.
/// </para>
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
