namespace Core.Infrastructure.Network.Serialization.Attributes;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class CStringAttribute : Attribute, IStringSerializationAttribute
{
    public int? Length { get; }

    /// <summary>Null-terminated C-string with no fixed field width.</summary>
    public CStringAttribute()
    {
        Length = null;
    }

    /// <summary>Fixed-width C-string field of exactly <paramref name="length"/> bytes.</summary>
    public CStringAttribute(int length)
    {
        if (length <= 0)
            throw new ArgumentOutOfRangeException(nameof(length), "CString length must be positive");
        Length = length;
    }

    /// <inheritdoc />
    public void Write(ref BinaryPacketSerializer.SpanWriter writer, string value)
        => writer.WriteCString(value, Length);

    /// <inheritdoc />
    public string Read(ref BinaryPacketSerializer.SpanReader reader)
        => reader.ReadCString(Length);

    /// <inheritdoc />
    public int? FixedWireSize => Length;
}