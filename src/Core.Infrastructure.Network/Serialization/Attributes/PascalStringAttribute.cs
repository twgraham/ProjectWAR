namespace Core.Infrastructure.Network.Serialization.Attributes;

/// <summary>
/// Specifies that a <c>string</c> property is serialized as a Pascal string:
/// a 1-byte unsigned length prefix (0–255) followed by that many raw encoded bytes.
/// No null terminator is written or expected. Strings whose encoded length exceeds
/// 255 bytes are silently truncated on write.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class PascalStringAttribute : Attribute, IStringSerializationAttribute
{
    /// <inheritdoc />
    public void Write(ref BinaryPacketSerializer.SpanWriter writer, string value)
        => writer.WritePascalString(value);

    /// <inheritdoc />
    public string Read(ref BinaryPacketSerializer.SpanReader reader)
        => reader.ReadPascalString();

    /// <inheritdoc />
    public int? FixedWireSize => null;
}
