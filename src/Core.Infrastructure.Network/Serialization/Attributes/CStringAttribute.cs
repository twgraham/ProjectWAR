namespace Core.Infrastructure.Network.Serialization.Attributes;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class CStringAttribute : Attribute, ICustomSerializationAttribute
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
    public void Write(ref BinaryPacketSerializer.SpanWriter writer, object value)
        => writer.WriteCString((string)value, Length);

    /// <inheritdoc />
    public object Read(ref BinaryPacketSerializer.SpanReader reader)
        => reader.ReadCString(Length);

    /// <inheritdoc />
    public int? FixedWireSize => Length;
}