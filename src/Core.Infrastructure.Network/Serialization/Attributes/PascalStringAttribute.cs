namespace Core.Infrastructure.Network.Serialization.Attributes;

/// <summary>
/// Specifies that a <c>string</c> property is serialized as a Pascal string:
/// a 1-byte unsigned length prefix (0–255) followed by that many raw encoded bytes.
/// No null terminator is written or expected. Strings whose encoded length exceeds
/// 255 bytes are silently truncated on write.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class PascalStringAttribute : Attribute, ISerializerRule
{
    /// <inheritdoc />
    public bool CanRead(SerializerPropertyContext ctx)
        => ctx.UnderlyingType == typeof(string);

    /// <inheritdoc />
    public object? Read(ref BinaryPacketSerializer.SpanReader reader, SerializerPropertyContext ctx)
        => reader.ReadPascalString();

    /// <inheritdoc />
    public bool CanWrite(SerializerPropertyContext ctx)
        => ctx.UnderlyingType == typeof(string);

    /// <inheritdoc />
    public void Write(ref BinaryPacketSerializer.SpanWriter writer, object? value, SerializerPropertyContext ctx)
        => writer.WritePascalString((string?)value ?? string.Empty);
}
