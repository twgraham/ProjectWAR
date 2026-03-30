namespace Core.Infrastructure.Network.Serialization;

/// <summary>
/// Defines how a property is read/written on the wire at runtime (reflection path).
/// Attributes that control binary format can implement this interface directly,
/// allowing the reflection-based serializer to dispatch to them without hardcoded branching.
/// </summary>
/// <remarks>
/// When the <see cref="BinaryPacketSerializer"/> encounters a property whose attribute
/// implements <see cref="ISerializerRule"/>, it delegates read/write to the attribute
/// instead of using its built-in type-based logic.
///
/// Every <see cref="ISerializerRule"/> must have a corresponding
/// <c>ISerializerRuleCodeGen</c> in the source generator project so that the
/// source-generated path emits equivalent logic without reflection.
/// </remarks>
public interface ISerializerRule
{
    /// <summary>
    /// Returns <c>true</c> if this rule can handle reading the property described by <paramref name="ctx"/>.
    /// </summary>
    bool CanRead(SerializerPropertyContext ctx);

    /// <summary>
    /// Reads the property value from the binary reader.
    /// </summary>
    object? Read(ref BinaryPacketSerializer.SpanReader reader, SerializerPropertyContext ctx);

    /// <summary>
    /// Returns <c>true</c> if this rule can handle writing the property described by <paramref name="ctx"/>.
    /// </summary>
    bool CanWrite(SerializerPropertyContext ctx);

    /// <summary>
    /// Writes the property value to the binary writer.
    /// </summary>
    void Write(ref BinaryPacketSerializer.SpanWriter writer, object? value, SerializerPropertyContext ctx);

    /// <summary>
    /// Returns the fixed serialized size in bytes, or <c>null</c> if the size is variable.
    /// Used by <c>[SizedEntry]</c> wire-size computation.
    /// </summary>
    int? SerializedSize(SerializerPropertyContext ctx) => null;
}
